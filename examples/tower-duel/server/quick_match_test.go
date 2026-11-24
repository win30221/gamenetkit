package main

import (
	"context"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/coder/websocket"
)

func TestQuickMatchDealsEachDeckAndStartsAutomatically(t *testing.T) {
	ctx := context.Background()
	m := testMap(t)
	config := defaultRules()
	app := newApplication(ctx, m, config, "")
	defer app.engine.close()
	server := httptest.NewServer(app.handler)
	defer server.Close()

	join := func(name string, deck []string) *websocket.Conn {
		t.Helper()
		conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http")+"/ws", nil)
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { _ = conn.CloseNow() })
		sendTest(t, conn, helloMessage{
			Type: "hello", Protocol: protocolVersion, Mode: "quick",
			Name: name, MapHash: m.Hash, RulesHash: config.Hash, Deck: deck,
		})
		return conn
	}

	invalid := join("Invalid", []string{"guard", "guard"})
	readTest(t, invalid, func(data []byte) bool { return strings.Contains(string(data), "invalid_deck") })
	defaultDeck, _ := config.deck(nil)
	customDeck := defaultDeck
	customDeck[0], customDeck[7] = customDeck[7], customDeck[0]
	first := join("First", defaultDeck[:])
	readTest(t, first, func(data []byte) bool { return string(data) == string(queuedNotice) })
	second := join("Second", customDeck[:])

	a := readStateTest(t, first, "countdown")
	b := readStateTest(t, second, "countdown")
	if !a.Quick || !b.Quick || a.Match != b.Match || a.Seat != 0 || b.Seat != 1 {
		t.Fatalf("invalid quick match: first=%+v second=%+v", a, b)
	}
	for i, state := range []stateMessage{a, b} {
		deck := [2][deckSize]string{defaultDeck, customDeck}[i]
		allowed := make(map[string]bool, deckSize)
		for _, id := range deck {
			allowed[id] = true
		}
		for _, id := range append(state.Hand[:], state.NextCard) {
			if !allowed[id] {
				t.Fatalf("invalid or duplicate dealt card: %q", id)
			}
			delete(allowed, id)
		}
	}
	readStateTest(t, first, "playing")
	readStateTest(t, second, "playing")
}

func TestCancelledSearchCannotClaimASeat(t *testing.T) {
	m := testMap(t)
	config := defaultRules()
	e := &engine{Map: m, Rules: config, Rooms: make(map[string]*room), Seats: make(map[*peer]seatRef)}
	ctx, cancel := context.WithCancel(context.Background())
	first := &peer{Joined: make(chan joinResult, 1)}
	second := &peer{Joined: make(chan joinResult, 1)}
	hello := helloMessage{Mode: "quick", MapHash: m.Hash, RulesHash: config.Hash}
	e.apply(command{Kind: "join", Peer: first, Hello: hello, Ctx: ctx})
	cancel()
	e.apply(command{Kind: "join", Peer: second, Hello: hello, Ctx: context.Background()})
	if len(e.Rooms) != 0 || e.waiting == nil || e.waiting.Peer != second {
		t.Fatal("cancelled player was paired")
	}
	e.apply(command{Kind: "remove", Peer: second})
	if e.waiting != nil {
		t.Fatal("disconnected player remained queued")
	}
}
