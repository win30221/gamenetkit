package main

import "testing"

func TestSquadDeploymentIsAtomicAndIdempotent(t *testing.T) {
	w := playingMatch(t)
	invalid := w.deploy(0, w.ID, 1, "swords", -5, -w.Map.barrierHalfWidth()-.6)
	if invalid.Accepted || len(w.Units) != 0 || w.Energy[0] != 5 {
		t.Fatal("formation partly entered river or charged on failure")
	}
	good := w.deploy(0, w.ID, 2, "swords", -5, -3)
	if !good.Accepted || good.Count != 3 || len(w.Units) != 3 || w.Energy[0] != 2 {
		t.Fatal("squad did not spawn as one action", good)
	}
	if again := w.deploy(0, w.ID, 2, "archer", 5, -5); again != good || len(w.Units) != 3 || w.Energy[0] != 2 {
		t.Fatal("squad retry duplicated units")
	}
	for _, u := range w.Units {
		if u.State.Kind != "swords" || u.State.HP != 70 {
			t.Fatal("squad used wrong stats")
		}
	}
	w = playingMatch(t)
	for id := uint64(1000); id < 1078; id++ {
		w.Units[id] = &unit{State: entity{ID: id, Kind: "guard", HP: 220, X: 8, Z: -13}}
	}
	if r := w.deploy(0, w.ID, 1, "swords", -5, -3); r.Accepted || r.Reason != "unit_limit" || len(w.Units) != 78 || w.Energy[0] != 5 {
		t.Fatal("squad exceeded capacity or partly spawned", r)
	}
}

func TestArcherFiresAcrossBridgeWithoutAdvancing(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	a := w.deploy(0, w.ID, 1, "archer", -5, -2.5)
	b := w.deploy(1, w.ID, 1, "guard", -5, 2.5)
	if !a.Accepted || !b.Accepted {
		t.Fatal(a, b)
	}
	before := position(w.Units[a.UnitID].State)
	for range w.Rules.ByID["archer"].DeployTicks {
		w.step()
	}
	if w.Units[b.UnitID].State.HP != 220 || position(w.Units[a.UnitID].State) != before || w.Units[a.UnitID].State.AttackTick != w.Tick {
		t.Fatal("archer did not stop and begin windup at range")
	}
	for range w.Rules.ByID["archer"].WindupTicks {
		w.step()
	}
	if w.Units[b.UnitID].State.HP != 196 || position(w.Units[a.UnitID].State) != before || w.Units[a.UnitID].State.HitTick != w.Tick {
		t.Fatal("archer did not fire after windup")
	}
}

func TestThreeSwordsCanSurroundSingleGuard(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	a := w.deploy(0, w.ID, 1, "swords", 0, -7.5)
	if !a.Accepted {
		t.Fatal(a)
	}
	w.Units[900] = &unit{State: entity{ID: 900, Seat: 1, Kind: "guard", X: 0, Z: -5.8, Radius: guardRadius, HP: 220, MaxHP: 220}}
	for i := 0; i < 500 && w.Units[900] != nil; i++ {
		w.step()
	}
	if w.Units[900] != nil {
		t.Fatal("squad failed to surround and defeat guard", w.Units[900].State.HP)
	}
	living := 0
	for _, u := range w.Units {
		if u.State.Seat == 0 {
			living++
		}
	}
	if living == 0 {
		t.Fatal("squad counter left no survivor")
	}
}
