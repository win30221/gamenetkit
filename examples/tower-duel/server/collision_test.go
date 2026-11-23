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
				if !finite(a.State.X) || !finite(a.State.Z) || !w.walkableFor(position(a.State), a.State.Radius, a.State.Airborne) {
					t.Fatal("push entered terrain", a.State)
				}
				for _, b := range units[i+1:] {
					if d := distance(position(a.State), position(b.State)); d < a.State.Radius+b.State.Radius-.002 {
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
	if distance(position(a.State), position(b.State)) < a.State.Radius+b.State.Radius-.002 {
		t.Fatal("pushed soldiers overlapped")
	}
}

func TestOpposingSoldiersAlsoSeparate(t *testing.T) {
	w := playingMatch(t)
	gate := w.Map.Bridges[1]
	a, b := testSoldier(101, 0, "guard", gate, 0), testSoldier(201, 1, "guard", gate, .1)
	w.resolvePush([]*unit{a, b})
	if distance(position(a.State), position(b.State)) < a.State.Radius+b.State.Radius-.002 || !w.walkableFor(position(a.State), a.State.Radius, a.State.Airborne) || !w.walkable(position(b.State)) {
		t.Fatal("opponents did not collide", a.State, b.State)
	}
}

func TestSiegePushesDefendersStraightThroughGate(t *testing.T) {
	for seat := range 2 {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		direction := 1.0
		if seat == 1 {
			direction = -1
		}
		lane := w.Map.Bridges[seat]
		siege := testSoldier(101, seat, "siege", lane, -2.5*direction)
		siege.State.Radius = w.Rules.ByID["siege"].bodyRadius()
		siege.LaneX = lane
		front := testSoldier(102, 1-seat, "guard", lane, -1.7*direction)
		front.State.Radius = w.Rules.ByID["guard"].bodyRadius()
		front.Cooldown = 10000
		back := testSoldier(103, 1-seat, "archer", lane, -.9*direction)
		back.State.Radius, back.Cooldown = w.Rules.ByID["archer"].bodyRadius(), front.Cooldown
		w.Units[101], w.Units[102], w.Units[103] = siege, front, back
		for range 180 {
			w.step()
			for _, u := range []*unit{siege, front, back} {
				if u.State.X != lane || !w.walkableFor(position(u.State), u.State.Radius, false) {
					t.Fatalf("seat %d: ground unit sidestepped or entered terrain: %+v", seat, u.State)
				}
			}
			if siege.State.Z*direction >= .5 {
				break
			}
		}
		if siege.State.Z*direction < .5 || front.State.Z*direction <= siege.State.Z*direction ||
			back.State.Z*direction <= front.State.Z*direction || siege.State.HitTick != 0 {
			t.Fatalf("seat %d: expected siege to push both defenders without attacking them: %v %v %v",
				seat, position(siege.State), position(front.State), position(back.State))
		}
	}
}
