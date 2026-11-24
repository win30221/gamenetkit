package main

import (
	"fmt"
	"math"
	"testing"
)

func TestFrostOverlapsSoldierFootprint(t *testing.T) {
	cases := []struct {
		name   string
		x, z   float64
		radius float64
		slowed bool
	}{
		{"inside", 2.799, 0, guardRadius, true},
		{"on_circle", 3.12, 0, guardRadius, true},
		{"outside", 3.121, 0, guardRadius, false},
		{"body_overlap", 3, 0, guardRadius, true},
		{"large_body_outside", 4.31, 0, 1.5, false},
		{"diagonal_inside", 1.97, 1.97, guardRadius, true},
		{"diagonal_outside", 2.21, 2.21, guardRadius, false},
	}
	for _, seat := range []int{0, 1} {
		for _, card := range defaultRules().Cards {
			if card.Type != "troop" {
				continue
			}
			for _, tc := range cases {
				t.Run(fmt.Sprintf("seat%d/%s/%s", seat, card.ID, tc.name), func(t *testing.T) {
					w := playingMatch(t)
					w.Hands[seat][0] = "frost"
					if result := w.deploy(seat, w.ID, 1, "frost", 0, -5); !result.Accepted {
						t.Fatal(result)
					}
					enemy := testSoldier(200, 1-seat, card.ID, tc.x, -5+tc.z)
					ally := testSoldier(201, seat, card.ID, tc.x, -5+tc.z)
					enemy.State.Radius, ally.State.Radius = tc.radius, tc.radius
					w.Units[200], w.Units[201] = enemy, ally
					w.Tick = w.Effects[0].Impact
					w.stepEffects(w.sortedUnits(), map[uint64]int{})
					if (w.attackSpeedFactor(enemy) < 1) != tc.slowed || enemy.State.Slowed != tc.slowed {
						t.Fatalf("foot center (%v,%v): slowed=%v factor=%v", tc.x, tc.z, enemy.State.Slowed, w.attackSpeedFactor(enemy))
					}
					if w.attackSpeedFactor(ally) != 1 || ally.State.Slowed {
						t.Fatal("frost affected its caster's soldier")
					}
				})
			}
		}
	}
}

func TestFrostCrossingUpdatesAfterMovement(t *testing.T) {
	for _, seat := range []int{0, 1} {
		for _, entering := range []bool{false, true} {
			t.Run(fmt.Sprintf("seat%d/entering_%v", seat, entering), func(t *testing.T) {
				w := playingMatch(t)
				w.Rules.TowerDamage = 0
				direction := float64(1 - seat*2)
				u := testSoldier(200, seat, "guard", -5.5, -4*direction)
				u.LaneX = -5.5
				w.Units[200] = u
				center := point{u.State.X, u.State.Z - direction*3.119}
				if entering {
					center.Z = u.State.Z + direction*3.121
				}
				c := w.Rules.ByID["frost"]
				w.Effects = []effectState{{ID: 900, Kind: "frost", Seat: 1 - seat, X: center.X, Z: center.Z, Radius: c.Radius, Impact: 1, End: 121, Slow: c.Slow}}
				w.step()
				inside := distance(center, position(u.State)) <= c.Radius+u.State.Radius
				if inside != entering || u.State.Slowed != inside || (w.attackSpeedFactor(u) < 1) != inside {
					t.Fatalf("post-movement center distance=%v inside=%v slowed=%v factor=%v", distance(center, position(u.State)), inside, u.State.Slowed, w.attackSpeedFactor(u))
				}
			})
		}
	}
}

func TestFrostClearsImmediatelyAndReevaluatesOverlaps(t *testing.T) {
	w := playingMatch(t)
	u := testSoldier(200, 1, "guard", .5, -5)
	w.Units[200] = u
	w.Effects = []effectState{
		{ID: 900, Kind: "frost", Seat: 0, X: 0, Z: -5, Radius: 2.8, Impact: 1, End: 100, Slow: .25},
		{ID: 901, Kind: "frost", Seat: 0, X: 2, Z: -5, Radius: 2.8, Impact: 1, End: 200, Slow: .7},
	}
	w.Tick = 1
	check := func(x, factor float64) {
		t.Helper()
		u.State.X = x
		w.stepEffects(w.sortedUnits(), map[uint64]int{})
		if math.Abs(w.attackSpeedFactor(u)-factor) > 1e-9 || u.State.Slowed != (factor < 1) {
			t.Fatalf("x=%v: factor=%v slowed=%v, want=%v", x, w.attackSpeedFactor(u), u.State.Slowed, factor)
		}
	}
	check(.5, .25)
	check(3.2, .7) // Leaving the stronger circle must not keep its old factor.
	check(5.2, 1)  // Even within the same simulation tick, leaving clears the status.
	check(.5, .25)
	w.Tick = 100
	check(.5, .7)
	w.Tick = 200
	check(.5, 1)
}

func TestFrostCrossingUpdatesAfterCollisionPush(t *testing.T) {
	for _, seat := range []int{0, 1} {
		for _, entering := range []bool{false, true} {
			t.Run(fmt.Sprintf("seat%d/entering_%v", seat, entering), func(t *testing.T) {
				w := playingMatch(t)
				w.Rules.TowerDamage = 0
				direction := float64(1 - seat*2)
				u := testSoldier(200, seat, "guard", -5.5, -4*direction)
				pusher := testSoldier(201, seat, "guard", u.State.X, u.State.Z-.3*direction)
				u.State.ReadyTick, pusher.State.ReadyTick = 1000, 1000
				w.Units[200], w.Units[201] = u, pusher
				center := point{u.State.X, u.State.Z - direction*3.119}
				if entering {
					center.Z = u.State.Z + direction*3.121
				}
				c := w.Rules.ByID["frost"]
				w.Effects = []effectState{{ID: 900, Kind: "frost", Seat: 1 - seat, X: center.X, Z: center.Z, Radius: c.Radius, Impact: 1, End: 121, Slow: c.Slow}}
				w.step()
				inside := distance(center, position(u.State)) <= c.Radius+u.State.Radius
				if inside != entering || u.State.Slowed != inside || (w.attackSpeedFactor(u) < 1) != inside || u.State.ReadyTick != 1000 {
					t.Fatalf("collision crossing distance=%v inside=%v slowed=%v factor=%v readyTick=%v", distance(center, position(u.State)), inside, u.State.Slowed, w.attackSpeedFactor(u), u.State.ReadyTick)
				}
			})
		}
	}
}
