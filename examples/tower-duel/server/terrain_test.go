package main

import (
	"math"
	"testing"
)

func TestMudMovementOnlyBothSeatsAndExit(t *testing.T) {
	for seat := 0; seat < 2; seat++ {
		w := playingMatch(t)
		w.Units = map[uint64]*unit{}
		direction := 1.0
		if seat == 1 {
			direction = -1
		}
		p := point{5, -4 * direction}
		u := &unit{State: entity{ID: 100, Kind: "guard", Seat: seat, X: p.X, Z: p.Z, HP: 100, Radius: guardRadius},
			Path: []point{{5, p.Z + direction}}, Goal: 999, NextRoute: 999}
		goal := entity{ID: 999, Kind: "tower", Seat: 1 - seat, X: 5, Z: 7.7 * direction, Radius: 1}
		w.Units[100] = u
		w.refreshUnitSlows([]*unit{u})
		w.move(u, goal, []*unit{u})
		expected := w.Rules.ByID["guard"].Speed * .7 / tickRate
		if math.Abs(distance(p, position(u.State))-expected) > 1e-8 {
			t.Fatalf("seat%d movement not 70%%: %+v", seat, u.State)
		}
		if !u.State.Muddy || u.State.Slowed || w.speedFactor(u) != 1 {
			t.Fatal("mud changed attack slow", u.State)
		}
		u.State.X = -5
		w.refreshUnitSlows([]*unit{u})
		if u.State.Muddy || w.Map.terrainFactor(position(u.State)) != 1 {
			t.Fatal("mud did not clear on exit")
		}
	}
}

func TestMudContinuousThroughGateAndFootBoundary(t *testing.T) {
	m := testMap(t)
	for _, z := range []float64{-7.7, -.6, 0, .6, 7.7} {
		if m.terrainFactor(point{5, z}) != .7 {
			t.Fatal("mud gap at gate or road", z)
		}
	}
	for _, p := range []point{{3.3, 0}, {6.7, 0}, {5, -7.7}, {5, 7.7}} {
		if m.terrainFactor(p) != .7 {
			t.Fatal("foot on border not muddy", p)
		}
	}
	for _, p := range []point{{3.2999, 0}, {6.7001, 0}, {5, -7.7001}, {5, 7.7001}, {-5, 0}} {
		if m.terrainFactor(p) != 1 {
			t.Fatal("dry foot slowed", p)
		}
	}
}

func TestWallBlocksExceptGatesAndAllowsAmbushDeployment(t *testing.T) {
	w := playingMatch(t)
	if w.walkable(point{0, 0}) || w.walkable(point{3, 0}) {
		t.Fatal("walked through wall")
	}
	for _, x := range []float64{-5, 5} {
		if !w.walkable(point{x, 0}) {
			t.Fatal("gate blocked", x)
		}
	}
	for seat := 0; seat < 2; seat++ {
		z := -.5
		if seat == 1 {
			z = .5
		}
		if !w.deploymentPoint(seat, point{0, z}) {
			t.Fatal("cannot deploy beside own wall", seat)
		}
		if w.deploymentPoint(seat, point{0, -z}) {
			t.Fatal("enemy wall side deploy allowed", seat)
		}
	}
}

func TestMudDoesNotChangeWindupOrCooldown(t *testing.T) {
	run := func(x float64) (uint64, uint64) {
		w := playingMatch(t)
		w.Towers = nil
		c := w.Rules.ByID["guard"]
		a := &unit{State: entity{ID: 100, Kind: "guard", Seat: 0, X: x, Z: -4, HP: 10000, Radius: guardRadius}}
		b := &unit{State: entity{ID: 101, Kind: "guard", Seat: 1, X: x, Z: -3.3, HP: 10000, Radius: guardRadius}}
		w.Units = map[uint64]*unit{100: a, 101: b}
		for i := 0; i < c.Cooldown+c.WindupTicks+8; i++ {
			w.step()
		}
		return a.State.AttackTick, a.State.HitTick
	}
	aa, ah := run(-5)
	ma, mh := run(5)
	if aa != ma || ah != mh || ah == 0 {
		t.Fatalf("terrain altered attacks: dry %d/%d mud %d/%d", aa, ah, ma, mh)
	}
}
