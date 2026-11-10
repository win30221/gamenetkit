package main

import (
	"context"
	"testing"
	"time"
)

func TestAppliedInputTickAndOwnAuthorityBump(t *testing.T) {
	w := &world{players: make(map[*peer]*player), tick: 10}
	p, q := &peer{}, &peer{}
	w.players[p] = &player{state: playerState{ID: 1, X: 2}, peer: p}
	w.players[q] = &player{state: playerState{ID: 2, X: 4}, peer: q}
	w.apply(command{kind: "input", peer: p, input: inputMessage{Seq: 1, X: 1}})
	if w.players[p].state.InputTick != 11 {
		t.Fatal("input must start in next step")
	}
	w.step(time.Now())
	if w.tick-w.players[p].state.InputTick+1 != 1 {
		t.Fatal("first step not counted")
	}
	prior := w.players[p].state.X
	w.apply(command{kind: "bump", peer: p})
	if w.players[p].state.X != prior-1 || w.players[q].state.X != 4 {
		t.Fatal("bump must affect its owner only")
	}
	w.apply(command{kind: "input", peer: p, input: inputMessage{Seq: 1, X: -1}})
	if w.players[p].state.InputTick != 11 {
		t.Fatal("stale input changed timing")
	}
}

func TestBumpWebSocketIsAppliedAndRejectsForeignID(t *testing.T) {
	_, server := testApplication(t)
	first, second := dial(t, server), dial(t, server)
	one, two := join(t, first), join(t, second)
	send(t, first, `{"type":"bump"}`)
	state := untilState(t, second, func(s stateMessage) bool { return findPlayer(s, one.PlayerID).X == -3 })
	if findPlayer(state, two.PlayerID).X != 2 {
		t.Fatal("foreign player moved")
	}
	send(t, first, `{"type":"bump","playerID":2}`)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	for {
		_, data, err := first.Read(ctx)
		if err != nil {
			break
		}
		if string(data) == `{"code":"invalid_message","type":"error"}` {
			return
		}
	}
	// Closure is also valid after the strict error response; it must not apply a foreign bump.
	state = untilState(t, second, func(s stateMessage) bool { return len(s.Players) == 1 })
	if findPlayer(state, two.PlayerID).X != 2 {
		t.Fatal("foreign bump was accepted")
	}
}
