package main

import (
	"context"
	"encoding/json"
	"testing"
	"time"
)

func TestActionsAuthorityLifecycleDedupAndCapacity(t *testing.T) {
	p := &peer{controls: make(chan []byte, 64)}
	q := &peer{controls: make(chan []byte, 64)}
	w := &world{players: map[*peer]*player{}, objects: map[uint64]objectState{}}
	w.players[p] = &player{peer: p, state: playerState{ID: 1}}
	w.players[q] = &player{peer: q, state: playerState{ID: 2}}
	action := func(peer *peer, id uint64, kind string) actionResult {
		w.apply(command{kind: "action", peer: peer, action: actionMessage{ID: id, Kind: kind}})
		for i := 0; i < 21; i++ {
			w.step(time.Now())
		}
		select {
		case data := <-peer.controls:
			var r actionResult
			if err := json.Unmarshal(data, &r); err != nil {
				t.Fatal(err)
			}
			return r
		default:
			t.Fatal("missing result")
			return actionResult{}
		}
	}
	first := action(p, 1, "spawn")
	if !first.Accepted || len(w.objects) != 1 {
		t.Fatal(first)
	}
	duplicate := action(p, 1, "spawn")
	if duplicate != first || w.nextObject != 1 {
		t.Fatal("duplicate executed again")
	}
	reborn := action(p, 2, "rebirth")
	if reborn.ObjectID != first.ObjectID || w.objects[1].Life != 2 {
		t.Fatal("rebirth changed identity")
	}
	if r := action(q, 1, "remove"); r.Accepted || r.Reason != "empty" || len(w.objects) != 1 {
		t.Fatal("foreign removal")
	}
	if r := action(p, 3, "deny"); r.Accepted || r.Reason != "demo_rejected" || len(w.objects) != 1 {
		t.Fatal("denial changed world")
	}
	for id := uint64(4); id <= 6; id++ {
		if r := action(p, id, "spawn"); !r.Accepted {
			t.Fatal(r)
		}
	}
	if r := action(p, 7, "spawn"); r.Accepted || r.Reason != "capacity" || len(w.objects) != 4 {
		t.Fatal("capacity")
	}
	if r := action(p, 8, "remove"); !r.Accepted || len(w.objects) != 3 {
		t.Fatal("removal")
	}
	w.apply(command{kind: "action", peer: p, action: actionMessage{ID: 9, Kind: "spawn"}})
	w.apply(command{kind: "remove", peer: p})
	if len(w.objects) != 0 || len(w.actions) != 0 {
		t.Fatal("disconnect leaked objects/pending actions")
	}
}

func TestTimeoutAndPendingBounds(t *testing.T) {
	p := &peer{controls: make(chan []byte, 64), failed: make(chan struct{}, 1)}
	w := &world{players: map[*peer]*player{p: {peer: p, state: playerState{ID: 1}}}}
	for id := uint64(1); id <= 9; id++ {
		w.apply(command{kind: "action", peer: p, action: actionMessage{ID: id, Kind: "timeout"}})
	}
	if len(w.actions) != 8 {
		t.Fatal("pending actions unbounded")
	}
	var busy actionResult
	_ = json.Unmarshal(<-p.controls, &busy)
	if busy.Reason != "busy" {
		t.Fatal(busy)
	}
	for i := 0; i < 21; i++ {
		w.step(time.Now())
	}
	if len(w.objects) != 0 || len(p.controls) != 0 {
		t.Fatal("timeout changed world or replied")
	}
	for id := uint64(10); id < 80; id++ {
		w.reply(w.players[p], actionResult{ID: id, Kind: "deny"})
		<-p.controls
	}
	if len(w.players[p].results) != 64 {
		t.Fatal("result cache unbounded")
	}
	for i := 0; i < 65; i++ {
		offerControl(p, []byte("result"))
	}
	select {
	case <-p.failed:
	default:
		t.Fatal("overflow must close instead of silently losing controls")
	}
}

func TestActionEnvelopeRejectsForeignOwnershipAndMalformedID(t *testing.T) {
	_, server := testApplication(t)
	for _, body := range []string{
		`{"type":"action","id":1,"kind":"remove","owner":2}`,
		`{"type":"action","id":0,"kind":"spawn"}`,
		`{"type":"action","id":9007199254740992,"kind":"spawn"}`,
		`{"type":"action","id":1,"id":2,"kind":"spawn"}`,
		`{"type":"action","id":1,"kind":"unknown"}`,
	} {
		c := dial(t, server)
		join(t, c)
		send(t, c, body)
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		rejected := false
		for {
			_, data, err := c.Read(ctx)
			if err != nil {
				break
			}
			var m struct{ Type, Code string }
			_ = json.Unmarshal(data, &m)
			if m.Type == "error" && m.Code == "invalid_message" {
				rejected = true
				break
			}
		}
		cancel()
		c.CloseNow()
		if !rejected {
			t.Fatalf("accepted %s", body)
		}
	}
}
