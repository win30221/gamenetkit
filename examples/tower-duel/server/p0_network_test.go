package main

import (
	"context"
	"encoding/json"
	"github.com/coder/websocket"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestRealWebSocketResumeRestoresHandAndWatermark(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	m := testMap(t)
	config := defaultRules()
	app := newApplication(ctx, m, config, "")
	defer app.engine.close()
	srv := httptest.NewServer(app.handler)
	defer srv.Close()
	dial := func(mode, room, token string) *websocket.Conn {
		c, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(srv.URL, "http")+"/ws", nil)
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { _ = c.CloseNow() })
		sendTest(t, c, helloMessage{Type: "hello", Protocol: protocolVersion, Mode: mode, Room: room, Name: "Resume tester", MapHash: m.Hash, RulesHash: config.Hash, Token: token})
		return c
	}
	a := dial("create", "", "")
	first := readStateTest(t, a, "")
	b := dial("join", first.Room, "")
	second := readStateTest(t, b, "")
	if first.ResumeToken == "" || first.ResumeToken == second.ResumeToken {
		t.Fatal("credentials not private per seat")
	}
	for _, c := range []*websocket.Conn{a, b} {
		sendTest(t, c, map[string]string{"type": "ready", "match": first.Match})
	}
	playing := readStateTest(t, a, "playing")
	readStateTest(t, b, "playing")
	command := deployMessage{"deploy", first.Match, 1, playableUnitCard(t, config, playing.Hand), -5, -3}
	sendTest(t, a, command)
	var accepted actionResult
	readTest(t, a, func(data []byte) bool { _ = json.Unmarshal(data, &accepted); return accepted.Type == "action" })
	if !accepted.Accepted {
		t.Fatal(accepted)
	}
	_ = a.CloseNow()
	other := readStateTest(t, b, "playing")
	for other.Players[0].Connected {
		other = readStateTest(t, b, "playing")
	}
	if other.ResumeToken != "" || other.HandVersion != 0 {
		t.Fatal("opponent private data was projected")
	}
	resumed := dial("resume", first.Room, first.ResumeToken)
	welcome := readStateTest(t, resumed, "playing")
	if welcome.Match != first.Match || welcome.Seat != 0 || welcome.Hand != accepted.Hand || welcome.ActionWatermark != 1 || welcome.HandVersion != 1 {
		t.Fatal("resume did not restore authoritative progress")
	}
	sendTest(t, resumed, command)
	var retry actionResult
	readTest(t, resumed, func(data []byte) bool { _ = json.Unmarshal(data, &retry); return retry.Type == "action" })
	if retry != accepted {
		t.Fatal("retry after a real reconnect changed result")
	}
	state := readStateTest(t, resumed, "playing")
	if len(state.Units) != config.ByID[command.Card].Count {
		t.Fatal("reconnect duplicated soldier")
	}
}

func TestBothOfflineRoomsAreReleased(t *testing.T) {
	e, a, b, r := engineFixture(t)
	r.Game.Phase = "playing"
	e.apply(command{Kind: "remove", Peer: a})
	e.apply(command{Kind: "remove", Peer: b})
	for range reconnectTicks {
		e.step()
	}
	if len(e.Rooms) != 0 || len(e.Seats) != 0 {
		t.Fatal("offline room leaked")
	}
}
func TestReservedCreatorDoesNotBlockFreeSecondSeat(t *testing.T) {
	e, a, b, r := engineFixture(t)
	e.apply(command{Kind: "leave", Peer: b})
	e.apply(command{Kind: "remove", Peer: a})
	c := testEnginePeer()
	e.apply(command{Kind: "join", Peer: c, Hello: helloMessage{Mode: "join", Room: r.Game.Room, Name: "New friend", MapHash: e.Map.Hash, RulesHash: e.Rules.Hash}, Ctx: context.Background()})
	if j := <-c.Joined; j.Error != "" {
		t.Fatal(j.Error)
	}
	if e.Seats[c].Seat != 1 || r.Tokens[0] == "" {
		t.Fatal("reserved seat was stolen")
	}
}
func TestCapacitySnapshotFitsTransportAndLatestQueueIsBounded(t *testing.T) {
	w := playingMatch(t)
	for i := 0; i < maxUnits; i++ {
		id := uint64(200 + i)
		card := w.Rules.Cards[i%6]
		w.Units[id] = &unit{State: entity{ID: id, Seat: i % 2, Kind: card.ID, X: 8.123456789, Z: 12.987654321, Radius: guardRadius, HP: card.HP, MaxHP: card.HP, DX: .123456789, DZ: .987654321, AttackTick: 999999, HitTick: 999999, HitTarget: 999999, Target: 999999}, LockedTarget: 999999}
	}
	for i := 0; i < 16; i++ {
		w.Effects = append(w.Effects, effectState{ID: uint64(400 + i), Kind: "frost", Seat: i % 2, X: 8.123456789, Z: 12.987654321, Radius: 2.8, Start: 999999, Impact: 1000000, End: 1000120})
	}
	payload, err := json.Marshal(w.snapshot(0, "snapshot"))
	if err != nil {
		t.Fatal(err)
	}
	if len(payload) > 32*1024 {
		t.Fatalf("snapshot exceeded SDK bound: %d", len(payload))
	}
	t.Logf("80-unit + 16-effect worst-position snapshot: %d bytes, %.1f KiB/s per recipient at 15 Hz", len(payload), float64(len(payload))*15/1024)
	queue := make(chan []byte, 1)
	for i := 0; i < 1000; i++ {
		offerLatest(queue, []byte{byte(i % 256)})
	}
	if len(queue) != 1 || (<-queue)[0] != byte(999%256) {
		t.Fatal("slow snapshot consumer accumulated backlog")
	}
}
