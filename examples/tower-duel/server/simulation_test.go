package main

import (
	"math"
	"testing"
)

func defaultRules() rules {
	r, err := loadRules("../client/Assets/Resources/TowerDuel/rules.json")
	if err != nil {
		panic(err)
	}
	return r
}

func testMap(t *testing.T) mapDefinition {
	t.Helper()
	m, err := loadMap("../client/Assets/Resources/TowerDuel/map.json")
	if err != nil {
		t.Fatal(err)
	}
	return m
}
func playingMatch(t *testing.T) *match {
	t.Helper()
	w := newMatch("match-1", "ABCDEF", testMap(t), defaultRules())
	w.Phase = "playing"
	return w
}
func TestDeployAuthorityAndIdempotence(t *testing.T) {
	w := playingMatch(t)
	good := w.deploy(0, w.ID, 1, "guard", -5, -2.2)
	if !good.Accepted || len(w.Units) != 1 || w.Energy[0] != 2 {
		t.Fatalf("deploy %+v", good)
	}
	if again := w.deploy(0, w.ID, 1, "guard", 5, -9); again != good || len(w.Units) != 1 || w.Energy[0] != 2 {
		t.Fatal("duplicate action changed state")
	}
	for i, p := range []point{{0, 0}, {5, 3}, {0, -11}, {w.Towers[1].X, w.Towers[1].Z}, {11, -4}, {math.NaN(), -3}} {
		before := w.Energy[0]
		r := w.deploy(0, w.ID, uint64(i+2), "guard", p.X, p.Z)
		if r.Accepted || before != w.Energy[0] {
			t.Fatalf("illegal placement accepted: %+v", p)
		}
	}
	if r := w.deploy(0, w.ID, 20, "guard", 5, -4); r.Reason != "not_enough_energy" {
		t.Fatalf("energy validation: %+v", r)
	}
	if r := w.deploy(0, "old-match", 1000, "guard", 5, -4); r.Reason != "stale_match" || w.HighWater[0] != 20 {
		t.Fatal("stale match poisoned action history")
	}
	for i := uint64(21); i < 200; i++ {
		w.deploy(0, w.ID, i, "unknown", 5, -4)
	}
	w.Energy[0] = 10
	if r := w.deploy(0, w.ID, 1, "guard", 5, -4); r.Accepted || r.Reason != "stale_action" || w.Energy[0] != 10 {
		t.Fatal("evicted action executed again")
	}
}
func TestGuardCrossesBridgeAndDestroysKeep(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	r := w.deploy(0, w.ID, 1, "guard", 0, -13)
	if !r.Accepted {
		t.Fatal(r)
	}
	crossed, hit, won := false, false, false
	for i := 0; i < 3500; i++ {
		w.step()
		u := w.Units[r.UnitID]
		if u == nil {
			t.Fatal("unopposed unit vanished")
		}
		p := position(u.State)
		if !w.walkable(p) {
			t.Fatalf("unit entered obstacle: %+v", p)
		}
		if p.Z > 2 {
			crossed = true
		}
		for _, tower := range w.Towers {
			if tower.Seat == 1 && tower.HP < tower.MaxHP {
				hit = true
			}
		}
		if w.Phase == "ended" {
			won = w.Winner == 0
			break
		}
	}
	if !crossed || !hit || !won {
		t.Fatalf("path/attack/result: crossed=%v hit=%v won=%v", crossed, hit, won)
	}
}
func TestMutualDamageIsSimultaneous(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	for i := range w.Towers {
		if w.Towers[i].Kind == "keep" {
			w.Towers[i].HP = 20
		}
	}
	w.Units[101] = &unit{State: entity{ID: 101, Seat: 0, Kind: "guard", X: 0, Z: 9, HP: 220, Radius: guardRadius}}
	w.Units[102] = &unit{State: entity{ID: 102, Seat: 1, Kind: "guard", X: 0, Z: -9, HP: 220, Radius: guardRadius}}
	for range w.Rules.ByID["guard"].WindupTicks + 1 {
		w.step()
	}
	if w.Winner != -1 || w.Reason != "simultaneous" {
		t.Fatalf("same tick must draw: %d %s", w.Winner, w.Reason)
	}
}
func TestOpposingGuardsFightAndDie(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	a := w.deploy(0, w.ID, 1, "guard", -5, -2)
	b := w.deploy(1, w.ID, 1, "guard", -5, 2)
	if !a.Accepted || !b.Accepted {
		t.Fatal(a, b)
	}
	hit := false
	for i := 0; i < 600; i++ {
		w.step()
		for _, u := range w.Units {
			if u.State.HP < 220 {
				hit = true
			}
		}
	}
	if !hit || len(w.Units) != 0 {
		t.Fatalf("mutual combat: hit=%v remaining=%d", hit, len(w.Units))
	}
}
func TestReadyCountdownAndPrivateSnapshots(t *testing.T) {
	w := playingMatch(t)
	w.Phase = "waiting"
	for i := range w.Players {
		w.Players[i].Connected = true
	}
	w.ready(0)
	if w.Phase != "waiting" {
		t.Fatal("started before both ready")
	}
	w.ready(1)
	for i := 0; i < 60; i++ {
		w.step()
	}
	if w.Phase != "playing" {
		t.Fatal("countdown")
	}
	w.Energy = [2]float64{2, 9}
	a, b := w.snapshot(0, "snapshot"), w.snapshot(1, "snapshot")
	if a.Energy != 2 || b.Energy != 9 || a.Match != b.Match || a.Tick != b.Tick {
		t.Fatal("per-seat state projection")
	}
}

func TestClockEnergyAndOvertime(t *testing.T) {
	w := playingMatch(t)
	w.Energy = [2]float64{0, 0}
	for i := 0; i < 3*tickRate; i++ {
		w.step()
	}
	if math.Abs(w.Energy[0]-1) > 1e-8 {
		t.Fatal("normal energy recovery", w.Energy)
	}
	w.Elapsed = w.Rules.Duration - 60*tickRate
	w.Energy = [2]float64{0, 0}
	for i := 0; i < 3*tickRate; i++ {
		w.step()
	}
	if math.Abs(w.Energy[0]-1) > 1e-8 || math.Abs(w.Energy[1]-1) > 1e-8 {
		t.Fatal("normal time accelerated energy recovery in the last minute", w.Energy)
	}
	w.Elapsed = w.Rules.Duration - 1
	w.Energy = [2]float64{0, 0}
	w.step()
	if w.Phase != "overtime" || w.snapshot(0, "snapshot").Remaining != 60 {
		t.Fatal("tied normal time did not start sixty seconds of overtime")
	}
	if math.Abs(w.Energy[0]-1.0/(3*tickRate)) > 1e-8 {
		t.Fatal("the final normal-time tick used overtime recovery", w.Energy)
	}
	w.Energy = [2]float64{0, 0}
	for range 3 * tickRate {
		w.step()
	}
	if math.Abs(w.Energy[0]-2) > 1e-8 || math.Abs(w.Energy[1]-2) > 1e-8 {
		t.Fatal("overtime did not double energy recovery for both players", w.Energy)
	}
	w.Energy = [2]float64{9.999, 9.999}
	w.step()
	if w.Energy != [2]float64{10, 10} {
		t.Fatal("overtime energy exceeded the cap", w.Energy)
	}
	w.Elapsed = w.Rules.Duration + 60*tickRate - 1
	w.step()
	if w.Winner != -1 || w.Reason != "time_draw" {
		t.Fatal("tied overtime did not draw")
	}
}

func TestTimeScoreAndSuddenDeath(t *testing.T) {
	w := playingMatch(t)
	w.Towers[4].HP = 0 // Seat 0 has destroyed one enemy front tower.
	w.Elapsed = w.Rules.Duration - 1
	w.step()
	if w.Winner != 0 || w.Reason != "time_score" {
		t.Fatal("normal-time tower score was not applied")
	}
	w = playingMatch(t)
	w.Phase = "overtime"
	w.Elapsed = w.Rules.Duration
	w.Towers[4].HP = 20
	w.Units[101] = &unit{State: entity{ID: 101, Seat: 0, Kind: "guard", X: w.Towers[4].X, Z: 5.7, HP: 220, MaxHP: 220, Radius: guardRadius}}
	for range w.Rules.ByID["guard"].WindupTicks + 1 {
		w.step()
	}
	if w.Winner != 0 || w.Reason != "sudden_death" {
		t.Fatal("overtime tower destruction did not end the match")
	}
}
