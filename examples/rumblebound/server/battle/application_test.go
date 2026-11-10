package battle

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func request(t *testing.T, s *httptest.Server, path, token, body string, out any) {
	t.Helper()
	r, err := http.NewRequest("POST", s.URL+path, bytes.NewBufferString(body))
	if err != nil {
		t.Fatal(err)
	}
	r.Header.Set("Content-Type", "application/json")
	if token != "" {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	response, err := s.Client().Do(r)
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	data, err := io.ReadAll(response.Body)
	if err != nil || response.StatusCode != 200 {
		t.Fatalf("%s: %d %s %v", path, response.StatusCode, data, err)
	}
	if out != nil {
		if err = json.Unmarshal(data, out); err != nil {
			t.Fatal(err)
		}
	}
}
func TestRemakeServiceGuestPrivateRoomAndLoadedPractice(t *testing.T) {
	app, err := New(context.Background(), Options{Origins: []string{"http://public.invalid"}})
	if err != nil {
		t.Fatal(err)
	}
	server := httptest.NewServer(app.Handler)
	defer server.Close()
	defer app.Close()
	var guest struct{ Session, UserID string }
	request(t, server, "/api/session/guest", "", "{}", &guest)
	if guest.Session == "" || guest.UserID == "" {
		t.Fatal("guest session unavailable")
	}
	var room lobby.Snapshot
	request(t, server, "/api/room/create", guest.Session, `{"teamCount":2}`, &room)
	if room.HostID != guest.UserID || len(room.Teams) != 2 || room.Code == "" {
		t.Fatalf("bad private room %+v", room)
	}
	request(t, server, "/api/room/leave", guest.Session, `{}`, nil)
	c := shared.Rules()
	response, err := http.Get(server.URL + "/api/config")
	if err != nil {
		t.Fatal(err)
	}
	var config struct {
		MapHash string
		Rules   shared.Config
	}
	err = json.NewDecoder(response.Body).Decode(&config)
	response.Body.Close()
	if err != nil || config.MapHash == "" || config.Rules.ArenaHalfSize != 30 {
		t.Fatal("large map config", err)
	}
	definition := map[string]any{"heroID": "ranger", "botHeroID": "tank", "mode": "idle", "protocol": c.Protocol, "content": c.Content, "mapHash": config.MapHash}
	body, _ := json.Marshal(definition)
	request(t, server, "/api/practice/start", guest.Session, string(body), &room)
	var ticket struct{ Ticket, MatchID string }
	request(t, server, "/api/room/ticket", guest.Session, `{}`, &ticket)
	if ticket.MatchID != room.MatchID || ticket.Ticket == "" {
		t.Fatal("match ticket missing")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http")+"/ws", nil)
	if err != nil {
		t.Fatal(err)
	}
	defer conn.CloseNow()
	send := func(value any) {
		data, _ := json.Marshal(value)
		if err := conn.Write(ctx, websocket.MessageText, data); err != nil {
			t.Fatal(err)
		}
	}
	read := func() protocol.Server {
		_, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var s protocol.Server
		if err = json.Unmarshal(data, &s); err != nil {
			t.Fatal(err)
		}
		return s
	}
	send(protocol.Client{Type: "hello", Protocol: c.Protocol, Content: c.Content, MatchID: ticket.MatchID, Ticket: ticket.Ticket})
	welcome := read()
	if welcome.Type != "welcome" || !welcome.Practice || len(welcome.Players) != 2 || welcome.MapHash != config.MapHash {
		t.Fatalf("bad practice welcome %+v", welcome)
	}
	send(protocol.Client{Type: "loaded", Protocol: c.Protocol, Content: c.Content, MatchID: ticket.MatchID, MapHash: config.MapHash, Generation: welcome.Generation})
	started := false
	for !started {
		state := read()
		if state.Type == "start" || state.Type == "snapshot" && state.Match != nil && state.Match.Phase == "playing" {
			started = true
		}
	}
	send(protocol.Client{Type: "input", Protocol: c.Protocol, Content: c.Content, MatchID: ticket.MatchID, Generation: welcome.Generation, Seq: 1, X: 0, Z: 1})
	for {
		state := read()
		if state.Type != "snapshot" {
			continue
		}
		for _, p := range state.Players {
			if p.PlayerID == welcome.PlayerID && p.Ack == 1 {
				return
			}
		}
	}
}
