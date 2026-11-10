package main

import (
	"context"
	"encoding/json"
	"math"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func testApplication(t *testing.T) (*application, *httptest.Server) {
	t.Helper()
	a, err := newApplication(context.Background(), "../../../packages/unity/transport/Browser/transport.js", "../web", "../client/Builds/WebGL")
	if err != nil {
		t.Fatal(err)
	}
	server := httptest.NewServer(a.handler)
	t.Cleanup(func() { a.close(); server.Close() })
	return a, server
}
func dial(t *testing.T, server *httptest.Server) *websocket.Conn {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http")+"/ws", nil)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { conn.CloseNow() })
	return conn
}
func send(t *testing.T, c *websocket.Conn, value string) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := c.Write(ctx, websocket.MessageText, []byte(value)); err != nil {
		t.Fatal(err)
	}
}
func readState(t *testing.T, c *websocket.Conn) stateMessage {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	_, data, err := c.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	var state stateMessage
	if err = json.Unmarshal(data, &state); err != nil {
		t.Fatal(err)
	}
	if state.Protocol != 1 {
		t.Fatalf("bad state %s", data)
	}
	return state
}
func join(t *testing.T, c *websocket.Conn) stateMessage {
	t.Helper()
	send(t, c, `{"type":"hello","protocol":1}`)
	state := readState(t, c)
	if state.Type != "welcome" || state.PlayerID == 0 || state.TickRate != 30 || state.SnapshotRate != 15 {
		t.Fatalf("bad welcome %+v", state)
	}
	return state
}
func untilState(t *testing.T, c *websocket.Conn, predicate func(stateMessage) bool) stateMessage {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		state := readState(t, c)
		if predicate(state) {
			return state
		}
	}
	t.Fatal("state condition timed out")
	return stateMessage{}
}
func findPlayer(s stateMessage, id uint64) playerState {
	for _, p := range s.Players {
		if p.ID == id {
			return p
		}
	}
	return playerState{}
}

func TestWebSocketSharedWorldOwnershipDisconnectAndRejoin(t *testing.T) {
	_, server := testApplication(t)
	first := dial(t, server)
	w1 := join(t, first)
	if len(w1.Players) != 1 || w1.Players[0].X != -2 {
		t.Fatalf("first spawn %+v", w1)
	}
	second := dial(t, server)
	w2 := join(t, second)
	if w2.PlayerID == w1.PlayerID || len(w2.Players) != 2 || findPlayer(w2, w2.PlayerID).X != 2 {
		t.Fatalf("second spawn %+v", w2)
	}
	shared1 := untilState(t, first, func(s stateMessage) bool { return len(s.Players) == 2 && s.Tick > w2.Tick })
	shared2 := untilState(t, second, func(s stateMessage) bool { return s.Tick >= shared1.Tick })
	if shared2.Tick < shared1.Tick {
		t.Fatal("world tick diverged")
	}
	send(t, first, `{"type":"input","seq":1,"x":1,"z":0}`)
	moved := untilState(t, second, func(s stateMessage) bool { return findPlayer(s, w1.PlayerID).X > -1.8 })
	if findPlayer(moved, w2.PlayerID).X != 2 || findPlayer(moved, w1.PlayerID).Ack != 1 {
		t.Fatalf("input ownership %+v", moved)
	}
	send(t, first, `{"type":"input","seq":2,"x":0,"z":0}`)
	stopped := untilState(t, second, func(s stateMessage) bool { return findPlayer(s, w1.PlayerID).Ack == 2 })
	after := untilState(t, second, func(s stateMessage) bool { return s.Tick > stopped.Tick+4 })
	if findPlayer(stopped, w1.PlayerID).X != findPlayer(after, w1.PlayerID).X {
		t.Fatal("zero input drifted")
	}
	first.CloseNow()
	survivor := untilState(t, second, func(s stateMessage) bool { return len(s.Players) == 1 && s.Tick > after.Tick })
	if survivor.Players[0].ID != w2.PlayerID {
		t.Fatal("wrong survivor")
	}
	third := dial(t, server)
	w3 := join(t, third)
	if w3.PlayerID <= w2.PlayerID || w3.Tick < survivor.Tick {
		t.Fatal("rejoin reused ID or reset world")
	}
}

func TestWebSocketRejectsInvalidMessages(t *testing.T) {
	_, server := testApplication(t)
	cases := []string{`{"type":"input","seq":1,"x":0,"z":0,"playerID":2}`, `{"type":"input","seq":0,"x":0,"z":0}`, `{"type":"input","seq":1,"x":2,"z":0}`, `{"type":"hello","protocol":1}`, `{"type":"input","seq":1,"x":null,"z":0}`, `{"type":"input","seq":1,"z":0}`, `{"type":"input","seq":1,"seq":2,"x":0,"z":0}`, `{"type":"input","seq":1.1,"x":0,"z":0}`, `{"type":"input","seq":1,"x":0,"z":0} {}`, `[]`, `garbage`}
	cases = append(cases, `{"type":"input","seq":9007199254740992,"x":0,"z":0}`, `{"type":"input","seq":1,"x":0,"X":1,"z":0}`)
	for _, value := range cases {
		t.Run(value, func(t *testing.T) {
			c := dial(t, server)
			join(t, c)
			send(t, c, value)
			expectError(t, c, "invalid_message")
		})
	}
}
func expectError(t *testing.T, c *websocket.Conn, code string) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	for {
		_, data, err := c.Read(ctx)
		if err != nil {
			t.Fatalf("expected error %s: %v", code, err)
		}
		var value struct{ Type, Code string }
		json.Unmarshal(data, &value)
		if value.Type == "error" {
			if value.Code != code {
				t.Fatalf("wrong error %s", data)
			}
			break
		}
	}
	if _, _, err := c.Read(ctx); err == nil {
		t.Fatal("connection remained open")
	}
}
func TestWebSocketHandshakeBoundsAndShutdown(t *testing.T) {
	a, server := testApplication(t)
	for _, value := range []string{`{"type":"hello","protocol":2}`, `{"type":"hello","protocol":1,"playerID":1}`, `null`} {
		c := dial(t, server)
		send(t, c, value)
		expectError(t, c, "invalid_hello")
	}
	c := dial(t, server)
	join(t, c)
	idle := dial(t, server) // Shutdown also closes sockets waiting for hello.
	a.world.cancel()
	select {
	case <-a.world.done:
	case <-time.After(time.Second):
		t.Fatal("world shutdown blocked")
	}
	for _, conn := range []*websocket.Conn{c, idle} {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		for {
			_, _, err := conn.Read(ctx)
			if err != nil {
				break
			}
		}
		if ctx.Err() != nil {
			t.Fatal("shutdown left socket open")
		}
		cancel()
	}
}

func TestWorldTTLSequenceNormalizationBoundsAndLatestQueue(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	w := &world{ctx: ctx, players: make(map[*peer]*player)}
	p := &peer{joined: make(chan joinResult, 1), snapshots: make(chan []byte, 1)}
	w.apply(command{kind: "join", peer: p, ctx: ctx})
	<-p.joined
	w.apply(command{kind: "input", peer: p, input: inputMessage{Seq: 2, X: 1, Z: 1}})
	player := w.players[p]
	acceptedAt := player.lastInput
	w.apply(command{kind: "input", peer: p, input: inputMessage{Seq: 1, X: -1}})
	if !player.lastInput.Equal(acceptedAt) || player.state.Ack != 2 || player.x != 1 {
		t.Fatal("stale input refreshed activity")
	}
	x, z := player.state.X, player.state.Z
	w.step(acceptedAt)
	if math.Abs(math.Hypot(player.state.X-x, player.state.Z-z)-4.0/30) > 1e-10 {
		t.Fatal("diagonal faster than cardinal")
	}
	w.step(acceptedAt.Add(inputTTL))
	x, z = player.state.X, player.state.Z
	w.step(acceptedAt.Add(inputTTL + time.Second))
	if player.state.X != x || player.state.Z != z {
		t.Fatal("expired input moved player")
	}
	player.state.X = 7.99
	player.state.Z = -7.99
	player.x = 1
	player.z = -1
	player.lastInput = acceptedAt
	w.step(acceptedAt)
	if player.state.X != 8 || player.state.Z != -8 {
		t.Fatal("position exceeded bounds")
	}
	for i := 0; i < 100; i++ {
		w.step(acceptedAt)
	}
	if len(p.snapshots) != 1 {
		t.Fatal("snapshot queue not bounded")
	}
	var latest stateMessage
	json.Unmarshal(<-p.snapshots, &latest)
	if latest.Tick != w.tick-w.tick%2 {
		t.Fatal("queue retained stale snapshot")
	}
}
func TestWorldCanceledJoinAndFullCapacity(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	w := &world{players: make(map[*peer]*player)}
	p := &peer{joined: make(chan joinResult, 1)}
	w.apply(command{kind: "join", peer: p, ctx: ctx})
	if len(w.players) != 0 {
		t.Fatal("canceled join leaked player")
	}
	for i := 0; i < 9; i++ {
		p = &peer{joined: make(chan joinResult, 1)}
		w.apply(command{kind: "join", peer: p, ctx: context.Background()})
		result := <-p.joined
		if i == 8 && result.err != "world_full" {
			t.Fatal("ninth player accepted")
		}
	}
}
