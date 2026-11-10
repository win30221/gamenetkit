package main

import (
	"context"
	"encoding/json"
	"github.com/win30221/gamenetkit/server/interest"
	"testing"
	"time"
)

func hasObject(message stateMessage, id string) bool {
	for _, o := range message.Objects {
		if o.ID == id {
			return true
		}
	}
	return false
}
func TestRecipientProjectionHysteresisAndUnchangedAuthority(t *testing.T) {
	p, q := &peer{}, &peer{}
	one := &player{peer: p, state: playerState{ID: 1}, interestMode: "aoi", radius: 3, selector: interest.New(3, .75, maxEntities)}
	two := &player{peer: q, state: playerState{ID: 2, X: 7}, interestMode: "aoi", radius: 3, selector: interest.New(3, .75, maxEntities)}
	w := &world{players: map[*peer]*player{p: one, q: two}, objects: map[uint64]objectState{1: {ID: "object-1", Owner: 2, Life: 1, X: 2.9, Kind: "marker"}}}
	first := w.project(one, "snapshot")
	if !hasObject(first, "object-1") || !hasObject(first, "landmark-hub") || findPlayer(first, 1).ID != 1 || findPlayer(first, 2).ID != 0 || first.Interest.TotalObjects != 14 {
		t.Fatal("bad required/near projection", first)
	}
	target := w.objects[1]
	target.X = 3.5
	w.objects[1] = target
	if !hasObject(w.project(one, "snapshot"), "object-1") || hasObject(w.project(two, "snapshot"), "object-1") {
		t.Fatal("recipient hysteresis was shared")
	}
	target.X = 3.8
	w.objects[1] = target
	if hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("object did not leave at exit threshold")
	}
	target.X = 3.5
	w.objects[1] = target
	if hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("reentered outside inner radius")
	}
	target.X = 2.9
	target.Life = 2
	w.objects[1] = target
	if !hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("reentry missing")
	}
	target.X = 3.5
	w.objects[1] = target
	if !hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("lost exit margin")
	}
	w.apply(command{kind: "interest", peer: p, mode: "all", radius: 3})
	all := w.project(one, "snapshot")
	if len(all.Objects) != 14 || len(all.Players) != 2 {
		t.Fatal("all projection did not restore rosters")
	}
	w.apply(command{kind: "interest", peer: p, mode: "aoi", radius: 3})
	if hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("mode switch reused stale hysteresis")
	}
	w.apply(command{kind: "interest", peer: p, mode: "aoi", radius: 5})
	if !hasObject(w.project(one, "snapshot"), "object-1") {
		t.Fatal("radius change not applied")
	}
	if len(w.objects) != 1 || w.objects[1].Life != 2 || len(w.players) != 2 || len(w.objectRoster()) != 14 {
		t.Fatal("projection mutated authority")
	}
}
func TestActualWebSocketPerRecipientFilteringAndReentry(t *testing.T) {
	_, server := testApplication(t)
	a, b := dial(t, server), dial(t, server)
	one, two := join(t, a), join(t, b)
	all := untilState(t, a, func(m stateMessage) bool { return m.Type == "snapshot" && len(m.Players) == 2 })
	send(t, a, `{"type":"interest","mode":"aoi","radius":3}`)
	projected := untilState(t, a, func(m stateMessage) bool { return m.Interest.Mode == "aoi" })
	if len(projected.Players) != 1 || findPlayer(projected, one.PlayerID).ID == 0 || len(projected.Objects) >= len(all.Objects) || !hasObject(projected, "landmark-hub") || hasObject(projected, "landmark-12") {
		t.Fatal("far payload leaked", projected)
	}
	untouched := untilState(t, b, func(m stateMessage) bool { return m.Tick >= projected.Tick })
	if untouched.Interest.Mode != "all" || len(untouched.Objects) != 13 || len(untouched.Players) != 2 {
		t.Fatal("another recipient changed")
	}
	before, _ := json.Marshal(all)
	after, _ := json.Marshal(projected)
	if len(after) >= len(before) {
		t.Fatal("filtering did not reduce actual JSON payload")
	}
	send(t, a, `{"type":"input","seq":1,"x":1,"z":0}`)
	returned := untilState(t, a, func(m stateMessage) bool { return findPlayer(m, two.PlayerID).ID != 0 })
	if returned.Interest.TotalPlayers != 2 || returned.Interest.TotalObjects != 13 {
		t.Fatal("world counts changed with AOI")
	}
	send(t, a, `{"type":"input","seq":2,"x":0,"z":0}`)
	send(t, a, `{"type":"interest","mode":"all","radius":3}`)
	restored := untilState(t, a, func(m stateMessage) bool { return m.Interest.Mode == "all" })
	if len(restored.Objects) != 13 || len(restored.Players) != 2 {
		t.Fatal("all sync did not restore same authority")
	}
}
func TestInterestControlsRejectUntrustedScopeAndInvalidBounds(t *testing.T) {
	_, server := testApplication(t)
	for _, body := range []string{`{"type":"interest","mode":"aoi","radius":1}`, `{"type":"interest","mode":"aoi","radius":7}`, `{"type":"interest","mode":"unknown","radius":3}`, `{"type":"interest","mode":"aoi","radius":null}`, `{"type":"interest","mode":"aoi","radius":3,"centerX":8}`, `{"type":"interest","mode":"aoi","radius":3,"playerID":2}`, `{"type":"interest","mode":"all","mode":"aoi","radius":3}`} {
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
			t.Fatal("invalid scope accepted", body)
		}
	}
}
