package main

import (
	"context"
	"encoding/json"
	"math"
	"strings"
	"testing"
)

func TestHandCycleIsAtomicAndPrivate(t *testing.T) {
	w := playingMatch(t)
	initial := w.Hands[0]
	for i, card := range []string{"guard", "charger", "siege", "fireball", "frost"} {
		w.Energy[0] = 10
		r := w.deploy(0, w.ID, uint64(i+1), card, 5, -4)
		if !r.Accepted {
			t.Fatalf("cycle %s: %+v", card, r)
		}
		if again := w.deploy(0, w.ID, uint64(i+1), card, -5, -4); again != r {
			t.Fatal("retry changed hand/ack")
		}
		if i < 4 && w.Hands[0][0] == "guard" {
			t.Fatal("guard returned too early")
		}
	}
	if w.Hands[0][0] != "guard" || w.HandVersion[0] != 5 || w.Hands[1] != initial {
		t.Fatal("incorrect four-card cycle")
	}
	before := w.Hands[0]
	energy := w.Energy[0]
	r := w.deploy(0, w.ID, 8, "charger", 5, -4)
	if r.Accepted || r.Reason != "card_not_in_hand" || w.Hands[0] != before || w.Energy[0] != energy {
		t.Fatal("out-of-hand play changed state", r)
	}
	w.Hands[1][0] = "fireball"
	data, _ := json.Marshal(w.snapshot(0, "snapshot"))
	var view map[string]json.RawMessage
	_ = json.Unmarshal(data, &view)
	if _, ok := view["queues"]; ok {
		t.Fatal("private queue leaked")
	}
	if string(view["hand"]) != "[\"guard\",\"archer\",\"swords\",\"bomber\"]" {
		t.Fatal("recipient hand missing", string(data))
	}
}

func TestFireballIsDelayedEnemyOnlyAndIdempotent(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	w.Hands[0][0] = "fireball"
	for i, seat := range []int{0, 1, 1} {
		x := 0.0
		if i == 2 {
			x = 6
		}
		id := uint64(200 + i)
		w.Units[id] = &unit{State: entity{ID: id, Seat: seat, Kind: "guard", X: x, Z: -5, HP: 220, MaxHP: 220, Radius: guardRadius}, Cooldown: 1000}
	}
	r := w.deploy(0, w.ID, 1, "fireball", 0, -5)
	if !r.Accepted || len(w.Effects) != 1 {
		t.Fatal(r)
	}
	_ = w.deploy(0, w.ID, 1, "fireball", 0, -5)
	if len(w.Effects) != 1 || w.Energy[0] != 1 {
		t.Fatal("spell duplicated")
	}
	for _, u := range w.Units {
		u.Path = nil
		u.LockedTarget = 0
	}
	// Hold positions while resolving the effect independently of combat/navigation.
	damage := map[uint64]int{}
	w.Tick = w.Effects[0].Impact - 1
	w.stepEffects(w.sortedUnits(), damage)
	if len(damage) != 0 {
		t.Fatal("spell applied before impact")
	}
	w.Tick++
	w.stepEffects(w.sortedUnits(), damage)
	if damage[200] != 0 || damage[201] != 110 || damage[202] != 0 {
		t.Fatal("wrong spell targets", damage)
	}
	w.stepEffects(w.sortedUnits(), damage)
	if damage[201] != 110 {
		t.Fatal("impact applied twice")
	}
}

func TestFrostRefreshesWithoutStackingAndExpires(t *testing.T) {
	w := playingMatch(t)
	w.Hands[0][0] = "frost"
	enemy := &unit{State: entity{ID: 200, Seat: 1, Kind: "guard", X: 0, Z: -5, HP: 220}}
	ally := &unit{State: entity{ID: 201, Seat: 0, Kind: "guard", X: 0, Z: -5, HP: 220}}
	w.Units[200] = enemy
	w.Units[201] = ally
	if r := w.deploy(0, w.ID, 1, "frost", 0, -5); !r.Accepted {
		t.Fatal(r)
	}
	w.Effects = append(w.Effects, w.Effects[0])
	w.Tick++
	w.stepEffects(w.sortedUnits(), map[uint64]int{})
	if math.Abs(w.speedFactor(enemy)-.55) > .00001 || w.speedFactor(ally) != 1 {
		t.Fatal("slow stacked or affected ally")
	}
	w.Tick = w.Effects[0].End
	w.stepEffects(w.sortedUnits(), map[uint64]int{})
	if w.speedFactor(enemy) != 1 || len(w.Effects) != 0 {
		t.Fatal("slow did not expire")
	}
}

func TestBomberSplashChargerAndSiege(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	w.Units[200] = &unit{State: entity{ID: 200, Seat: 0, Kind: "bomber", X: 0, Z: -5, HP: 85, Radius: guardRadius}}
	for i := 0; i < 2; i++ {
		id := uint64(201 + i)
		w.Units[id] = &unit{State: entity{ID: id, Seat: 1, Kind: "guard", X: float64(i) * .8, Z: -3.8, HP: 220, Radius: guardRadius}, Cooldown: 100}
	}
	for range w.Rules.ByID["bomber"].WindupTicks + 1 {
		w.step()
	}
	if w.Units[201].State.HP != 182 || w.Units[202].State.HP != 182 {
		t.Fatal("bomber did not damage group")
	}
	w = playingMatch(t)
	w.Rules.TowerDamage = 0
	w.Units[200] = &unit{State: entity{ID: 200, Seat: 0, Kind: "charger", X: 0, Z: -5, HP: 160, Radius: guardRadius}, Travelled: 3}
	w.Units[201] = &unit{State: entity{ID: 201, Seat: 1, Kind: "guard", X: 0, Z: -4, HP: 220, Radius: guardRadius}, Cooldown: 100}
	for range w.Rules.ByID["charger"].WindupTicks + 1 {
		w.step()
	}
	if w.Units[201].State.HP != 148 || w.Units[200].Travelled != 0 {
		t.Fatal("charge not consumed exactly once")
	}
	w = playingMatch(t)
	lane := w.Map.Bridges[0]
	siege := &unit{State: entity{ID: 200, Seat: 0, Kind: "siege", X: lane, Z: -3, HP: 380, Radius: guardRadius}, LaneX: lane}
	w.Units[200] = siege
	w.Units[201] = &unit{State: entity{ID: 201, Seat: 1, Kind: "guard", X: lane, Z: -2.2, HP: 220}}
	target, ok := w.target(siege, w.sortedUnits())
	if !ok || target.Kind != "tower" {
		t.Fatal("siege targeted soldier", target)
	}
}

func testEnginePeer() *peer {
	return &peer{Snapshots: make(chan []byte, 1), Reliable: make(chan []byte, 16), Joined: make(chan joinResult, 1), Cancel: func() {}}
}
func engineFixture(t *testing.T) (*engine, *peer, *peer, *room) {
	t.Helper()
	m := testMap(t)
	config := defaultRules()
	e := &engine{Map: m, Rules: config, Rooms: map[string]*room{}, Seats: map[*peer]seatRef{}}
	a, b := testEnginePeer(), testEnginePeer()
	hello := helloMessage{Mode: "create", Name: "A", MapHash: m.Hash, RulesHash: config.Hash}
	e.apply(command{Kind: "join", Peer: a, Hello: hello, Ctx: context.Background()})
	joined := <-a.Joined
	if joined.Error != "" {
		t.Fatal(joined.Error)
	}
	r := e.Seats[a].Room
	hello.Mode = "join"
	hello.Room = r.Game.Room
	hello.Name = "B"
	e.apply(command{Kind: "join", Peer: b, Hello: hello, Ctx: context.Background()})
	if j := <-b.Joined; j.Error != "" {
		t.Fatal(j.Error)
	}
	return e, a, b, r
}
func TestResumeRestoresSameSeatAndPendingAction(t *testing.T) {
	e, a, _, r := engineFixture(t)
	r.Game.Phase = "playing"
	result := r.Game.deploy(0, r.Game.ID, 1, "guard", 5, -4)
	token := r.Tokens[0]
	id := r.Game.ID
	e.apply(command{Kind: "remove", Peer: a})
	e.step()
	if r.Game.Phase == "ended" || r.Game.Players[0].Connected {
		t.Fatal("disconnect ended match")
	}
	restored := testEnginePeer()
	e.apply(command{Kind: "join", Peer: restored, Hello: helloMessage{Mode: "resume", Room: r.Game.Room, Token: token, MapHash: e.Map.Hash, RulesHash: e.Rules.Hash}, Ctx: context.Background()})
	joined := <-restored.Joined
	if joined.Error != "" {
		t.Fatal(joined.Error)
	}
	if e.Seats[restored].Seat != 0 || r.Game.ID != id || !r.Game.Players[0].Connected {
		t.Fatal("did not restore original seat/match")
	}
	if again := r.Game.deploy(0, id, 1, "guard", 5, -4); again != result || len(r.Game.Units) != 1 {
		t.Fatal("pending action duplicated after resume")
	}
	var welcome stateMessage
	_ = json.Unmarshal(joined.Welcome, &welcome)
	if welcome.ResumeToken != token || welcome.Hand != r.Game.Hands[0] {
		t.Fatal("incomplete recovery")
	}
	public, _ := json.Marshal(r.Game.snapshot(1, "snapshot"))
	if strings.Contains(string(public), token) {
		t.Fatal("resume credential leaked")
	}
}
func TestResumeRejectsWrongTokenDuplicateAndExpiry(t *testing.T) {
	e, a, _, r := engineFixture(t)
	r.Game.Phase = "playing"
	token := r.Tokens[0]
	resume := func(value string) *peer {
		p := testEnginePeer()
		e.apply(command{Kind: "join", Peer: p, Hello: helloMessage{Mode: "resume", Room: r.Game.Room, Token: value, MapHash: e.Map.Hash, RulesHash: e.Rules.Hash}, Ctx: context.Background()})
		return p
	}
	if j := <-resume(token).Joined; j.Error != "seat_connected" {
		t.Fatal("duplicate seat accepted")
	}
	e.apply(command{Kind: "remove", Peer: a})
	if j := <-resume(strings.Repeat("x", 43)).Joined; j.Error != "resume_expired" {
		t.Fatal("bad token accepted")
	}
	for i := 0; i < reconnectTicks; i++ {
		e.step()
	}
	if r.Game.Phase != "ended" || r.Game.Winner != 1 || r.Game.Reason != "reconnect_timeout" {
		t.Fatal("grace expiry did not forfeit")
	}
	if j := <-resume(token).Joined; j.Error != "resume_expired" {
		t.Fatal("expired token accepted")
	}
}
func TestThreeRematchesClearAllState(t *testing.T) {
	e, a, b, r := engineFixture(t)
	for i := 0; i < 3; i++ {
		old := r.Game.ID
		r.Game.Phase = "playing"
		r.Game.deploy(0, old, 1, "guard", 5, -4)
		r.Game.end(0, "keep_destroyed")
		for _, p := range []*peer{a, b} {
			e.apply(command{Kind: "rematch", Peer: p, Match: old})
		}
		if r.Game.ID == old || r.Game.Phase != "waiting" || len(r.Game.Units) != 0 || len(r.Game.Effects) != 0 || r.Game.Energy[0] != 5 || r.Game.HandVersion[0] != 0 || r.Game.HighWater[0] != 0 || r.Game.Players[0].Ready {
			t.Fatal("rematch retained old state")
		}
		if result := r.Game.deploy(0, old, 999, "guard", 5, -4); result.Reason != "stale_match" || r.Game.HighWater[0] != 0 {
			t.Fatal("old match poisoned new match")
		}
	}
}
