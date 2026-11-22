package main

import (
	"context"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func catalog(t *testing.T) gameConfig {
	t.Helper()
	data, err := os.ReadFile("../../client/Assets/Resources/TowerDuel/rules.json")
	if err != nil {
		t.Fatal(err)
	}
	var config gameConfig
	if err := json.Unmarshal(data, &config); err != nil {
		t.Fatal(err)
	}
	return config
}

func TestBotDecisionsUseOnlyAffordableCardsAndOwnHalf(t *testing.T) {
	for _, definition := range catalog(t).Cards {
		for _, seat := range []int{0, 1} {
			t.Run(definition.ID+string(rune('0'+seat)), func(t *testing.T) {
				b := &bot{cards: map[string]card{definition.ID: definition}}
				s := message{Seat: seat, Energy: definition.Cost - .01, Hand: [4]string{definition.ID}, Units: []entity{{ID: 101, Seat: 1 - seat, X: 2, Z: 2, HP: 100}}}
				if _, ok := b.choose(s); ok {
					t.Fatal("selected a card without sufficient energy")
				}
				s.Energy = definition.Cost
				move, ok := b.choose(s)
				if !ok || move.Card != definition.ID {
					t.Fatal("did not select the affordable hand card")
				}
				if definition.Type == "troop" && ((seat == 0 && move.Z >= -2) || (seat == 1 && move.Z <= 2)) {
					t.Fatal("deployed outside the bot's own half", move)
				}
				if definition.Type == "spell" && (move.X != 2 || move.Z != 2) {
					t.Fatal("spell did not target a real opponent", move)
				}
			})
		}
	}
}

func TestPracticeBuildsPushesInBacklineButDefendsAtRiver(t *testing.T) {
	for _, seat := range []int{0, 1} {
		direction := 1.0
		if seat == 1 {
			direction = -1
		}
		b := &bot{cards: map[string]card{"guard": {ID: "guard", Type: "troop", Cost: 3}}}
		s := message{Seat: seat, Energy: 5, Hand: [4]string{"guard"}}
		move, ok := b.choose(s)
		if !ok || move.Z*direction > -9 {
			t.Fatal("practice attacked from the river instead of assembling behind its tower", move)
		}
		s.Units = []entity{{ID: 101, Seat: 1 - seat, X: 5, Z: -3 * direction, HP: 100}}
		move, ok = b.choose(s)
		if !ok || move.X < 0 || move.Z*direction < -5 {
			t.Fatal("practice did not defend the threatened lane near the river", move)
		}
	}
}

func startBackend(t *testing.T, fast bool) string {
	t.Helper()
	binary := os.Getenv("TOWER_DUEL_SOLO_SERVER")
	if binary == "" {
		t.Skip("Run solo/Test.ps1 for real-backend integration tests")
	}
	mapPath, _ := filepath.Abs("../../client/Assets/Resources/TowerDuel/map.json")
	rulesPath, _ := filepath.Abs("../../client/Assets/Resources/TowerDuel/rules.json")
	if fast {
		data, err := os.ReadFile(rulesPath)
		if err != nil {
			t.Fatal(err)
		}
		var rules map[string]any
		if err := json.Unmarshal(data, &rules); err != nil {
			t.Fatal(err)
		}
		rules["towerHP"], rules["keepHP"], rules["towerDamage"] = 1, 1, 0
		data, _ = json.Marshal(rules)
		rulesPath = filepath.Join(t.TempDir(), "fast-combat-fixture.json")
		if err := os.WriteFile(rulesPath, data, 0600); err != nil {
			t.Fatal(err)
		}
	}
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	address := listener.Addr().String()
	listener.Close()
	ctx, cancel := context.WithCancel(context.Background())
	cmd := exec.CommandContext(ctx, binary, "-http", address, "-map", mapPath, "-rules", rulesPath)
	if err := cmd.Start(); err != nil {
		cancel()
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() { _ = cmd.Wait(); close(done) }()
	t.Cleanup(func() { cancel(); <-done })
	client := &http.Client{Timeout: 200 * time.Millisecond}
	url := "http://" + address
	for deadline := time.Now().Add(5 * time.Second); time.Now().Before(deadline); {
		select {
		case <-done:
			t.Fatal("normal game backend stopped before readiness")
		default:
		}
		if response, err := client.Get(url + "/healthz"); err == nil {
			response.Body.Close()
			if response.StatusCode == 200 {
				return url
			}
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("normal game backend did not become ready")
	return ""
}

type fixture struct {
	c      *companion
	api    *httptest.Server
	server string
}

func newFixture(t *testing.T, fast bool) fixture {
	t.Helper()
	server := startBackend(t, fast)
	ctx, cancel := context.WithCancel(context.Background())
	c, err := newCompanion(ctx, server)
	if err != nil {
		cancel()
		t.Fatal(err)
	}
	api := httptest.NewServer(c.handler())
	t.Cleanup(func() { cancel(); c.close(); api.Close() })
	return fixture{c, api, server}
}

func (f fixture) create(t *testing.T, mode string) string {
	t.Helper()
	response, err := http.Post(f.api.URL+"/solo/rooms", "application/json", strings.NewReader(`{"mode":"`+mode+`"}`))
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	var result struct{ Room, Mode, Server string }
	if err := json.NewDecoder(response.Body).Decode(&result); err != nil || response.StatusCode != 201 || len(result.Room) != 6 || result.Mode != mode || result.Server != f.server {
		t.Fatal("room creation failed", result, err)
	}
	return result.Room
}

func send(t *testing.T, conn *websocket.Conn, value any) {
	t.Helper()
	data, _ := json.Marshal(value)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := conn.Write(ctx, websocket.MessageText, data); err != nil {
		t.Fatal(err)
	}
}

func read(t *testing.T, conn *websocket.Conn, accept func(message) bool) message {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	for {
		_, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var result message
		if err := json.Unmarshal(data, &result); err != nil {
			t.Fatal(err)
		}
		if accept(result) {
			return result
		}
	}
}

func (f fixture) dial(t *testing.T, mode, room, token string) *websocket.Conn {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(f.server, "http")+"/ws", nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = conn.CloseNow() })
	send(t, conn, map[string]any{"type": "hello", "protocol": f.c.config.Protocol, "mode": mode, "room": room, "token": token, "name": "Solo tester", "mapHash": f.c.config.MapHash, "rulesHash": f.c.config.RulesHash})
	return conn
}

func (f fixture) playing(t *testing.T, mode string) (*websocket.Conn, message) {
	t.Helper()
	room := f.create(t, mode)
	conn := f.dial(t, "join", room, "")
	welcome := read(t, conn, func(s message) bool { return s.Type == "welcome" })
	if welcome.Seat != 1 || welcome.ResumeToken == "" || !welcome.Players[0].Connected {
		t.Fatal("normal handshake did not assign the second human seat")
	}
	send(t, conn, map[string]string{"type": "ready", "match": welcome.Match})
	return conn, read(t, conn, func(s message) bool { return s.Type == "snapshot" && s.Phase == "playing" })
}

func TestRealBackendPracticeAcknowledgesCostsDeduplicatesAndRunsBot(t *testing.T) {
	f := newFixture(t, false)
	conn, state := f.playing(t, "practice")
	move := deployment{Type: "deploy", Match: state.Match, Action: 1, Card: "guard", X: -5.5, Z: 3.5}
	send(t, conn, move)
	ack := read(t, conn, func(s message) bool { return s.Type == "action" })
	if !ack.Accepted || ack.Energy >= 3 || ack.HandVersion != 1 || ack.UnitID == 0 {
		t.Fatal("normal backend did not charge and confirm the human action", ack)
	}
	send(t, conn, move)
	retry := read(t, conn, func(s message) bool { return s.Type == "action" })
	if retry.Action != ack.Action || retry.Energy != ack.Energy || retry.UnitID != ack.UnitID {
		t.Fatal("normal server did not deduplicate the retry")
	}
	move.Action, move.Card, move.Z = 2, "archer", -6
	send(t, conn, move)
	invalid := read(t, conn, func(s message) bool { return s.Type == "action" })
	if invalid.Accepted || invalid.Reason != "invalid_placement" {
		t.Fatal("companion bypassed normal deployment authority", invalid)
	}
	read(t, conn, func(s message) bool {
		for _, unit := range s.Units {
			if unit.Seat == 0 {
				return true
			}
		}
		return false
	})
	f.c.mu.Lock()
	b := f.c.rooms[state.Room]
	f.c.mu.Unlock()
	b.mu.Lock()
	accepted, rejected := b.accepted, b.rejected
	b.mu.Unlock()
	if accepted < 1 || rejected != 0 {
		t.Fatal("backend bot did not receive an ordinary successful action", accepted, rejected)
	}
}

func TestRealBackendSandboxAndRematch(t *testing.T) {
	f := newFixture(t, true)
	conn, state := f.playing(t, "sandbox")
	send(t, conn, deployment{Type: "deploy", Match: state.Match, Action: 1, Card: "guard", X: -5.5, Z: 3.5})
	ack := read(t, conn, func(s message) bool { return s.Type == "action" })
	if !ack.Accepted {
		t.Fatal("sandbox did not use normal deploy", ack)
	}
	ended := read(t, conn, func(s message) bool {
		for _, unit := range s.Units {
			if unit.Seat == 0 {
				t.Fatal("sandbox bot deployed a troop")
			}
		}
		return s.Phase == "ended"
	})
	if ended.Reason != "keep_destroyed" || ended.Seat != 1 {
		t.Fatal("normal combat did not produce the sandbox result", ended.Reason)
	}
	send(t, conn, map[string]string{"type": "rematch", "match": ended.Match})
	next := read(t, conn, func(s message) bool { return s.Type == "welcome" && s.Match != ended.Match })
	if next.Phase != "waiting" || next.Energy != 5 || next.HandVersion != 0 || len(next.Units) != 0 {
		t.Fatal("normal rematch did not reset state")
	}
	send(t, conn, map[string]string{"type": "ready", "match": next.Match})
	read(t, conn, func(s message) bool { return s.Type == "snapshot" && s.Phase == "playing" })
}

func TestRealBackendRecoveryAndExplicitLeaveReleaseBot(t *testing.T) {
	f := newFixture(t, false)
	room := f.create(t, "sandbox")
	conn := f.dial(t, "join", room, "")
	welcome := read(t, conn, func(s message) bool { return s.Type == "welcome" })
	_ = conn.CloseNow()
	var restored message
	for deadline := time.Now().Add(2 * time.Second); time.Now().Before(deadline); {
		retry := f.dial(t, "resume", room, welcome.ResumeToken)
		restored = read(t, retry, func(s message) bool { return s.Type == "welcome" || s.Type == "error" })
		if restored.Type == "welcome" {
			conn = retry
			break
		}
		_ = retry.CloseNow()
		time.Sleep(20 * time.Millisecond)
	}
	if restored.Type != "welcome" || restored.Seat != 1 || restored.Match != welcome.Match {
		t.Fatal("normal resume failed", restored.Code)
	}
	send(t, conn, map[string]string{"type": "leave", "match": restored.Match})
	for deadline := time.Now().Add(3 * time.Second); time.Now().Before(deadline); {
		f.c.mu.Lock()
		remaining := len(f.c.rooms)
		f.c.mu.Unlock()
		if remaining == 0 {
			guest := f.dial(t, "join", room, "")
			missing := read(t, guest, func(s message) bool { return s.Type == "error" })
			if missing.Code != "room_not_found" {
				t.Fatal("solo bot left a room reserved after explicit leave", missing.Code)
			}
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatal("companion leaked a bot after explicit leave")
}

func TestCompanionRejectsInvalidModesAndBrowserOrigins(t *testing.T) {
	c := &companion{rooms: make(map[string]*bot)}
	for _, body := range []string{`{"mode":"create"}`, `{"mode":"sandbox","extra":1}`, `null`, `{"mode":"sandbox"} {}`} {
		request := httptest.NewRequest(http.MethodPost, "/solo/rooms", strings.NewReader(body))
		response := httptest.NewRecorder()
		c.handler().ServeHTTP(response, request)
		if response.Code != http.StatusBadRequest {
			t.Fatal("invalid companion request accepted", body)
		}
	}
	request := httptest.NewRequest(http.MethodGet, "/solo/config", nil)
	request.Header.Set("Origin", "https://example.com")
	response := httptest.NewRecorder()
	c.handler().ServeHTTP(response, request)
	if response.Code != http.StatusForbidden {
		t.Fatal("local native API accepted a browser origin")
	}
	if _, err := newCompanion(context.Background(), "https://example.com"); err == nil {
		t.Fatal("companion accepted a remote authority")
	}
	request = httptest.NewRequest(http.MethodDelete, "/solo/rooms/MISSING", nil)
	response = httptest.NewRecorder()
	c.handler().ServeHTTP(response, request)
	if response.Code != http.StatusNoContent {
		t.Fatal("missing bot cleanup was not idempotent")
	}
}
