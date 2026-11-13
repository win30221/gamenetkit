package main

import (
	"fmt"
	"testing"
)

func TestOverlappingDeploymentsPushApart(t *testing.T) {
	for _, p := range []point{{0, -5}, {5, -3}, {8.9, -13}, {-8.9, -13}} {
		t.Run(fmt.Sprint(p), func(t *testing.T) {
			w := playingMatch(t)
			w.Energy[0] = 10
			for action := uint64(1); action <= 3; action++ {
				// Geometry fixture: explicitly restore the squad slot; cycling is tested separately.
				w.Hands[0][2] = "swords"
				r := w.deploy(0, w.ID, action, "swords", p.X, p.Z)
				if !r.Accepted || r.Count != 3 {
					t.Fatal("repeat deployment rejected", r)
				}
				if duplicate := w.deploy(0, w.ID, action, "swords", p.X, p.Z); duplicate != r {
					t.Fatal("retry changed deployment")
				}
			}
			if len(w.Units) != 9 || w.Energy[0] != 1 {
				t.Fatal("wrong charge or duplicate spawn")
			}
			units := w.sortedUnits()
			for range 12 {
				w.resolvePush(units)
			}
			for i, a := range units {
				if !finite(a.State.X) || !finite(a.State.Z) || !w.walkable(position(a.State)) {
					t.Fatal("push entered terrain", a.State)
				}
				for _, b := range units[i+1:] {
					if d := distance(position(a.State), position(b.State)); d < guardRadius*2-.002 {
						t.Fatalf("unresolved overlap %d/%d: %g", a.State.ID, b.State.ID, d)
					}
				}
			}
		})
	}
}

func TestWalkingSoldierPushesAlly(t *testing.T) {
	w := playingMatch(t)
	a, b := testSoldier(101, 0, "guard", 5, -5), testSoldier(102, 0, "archer", 5, -4)
	units := []*unit{a, b}
	goal := w.Towers[5]
	for range 30 {
		w.move(a, goal, units)
		w.resolvePush(units)
	}
	if b.State.Z <= -3.9 || a.State.Z <= -4.5 {
		t.Fatal("walking soldier could not push its ally", a.State, b.State)
	}
	if distance(position(a.State), position(b.State)) < guardRadius*2-.002 {
		t.Fatal("pushed soldiers overlapped")
	}
}

func TestOpposingSoldiersAlsoSeparate(t *testing.T) {
	w := playingMatch(t)
	a, b := testSoldier(101, 0, "guard", 5, 0), testSoldier(201, 1, "guard", 5, .1)
	w.resolvePush([]*unit{a, b})
	if distance(position(a.State), position(b.State)) < guardRadius*2-.002 || !w.walkable(position(a.State)) || !w.walkable(position(b.State)) {
		t.Fatal("opponents did not collide", a.State, b.State)
	}
}
