package main

import (
	"fmt"
	"testing"
)

func TestSwordSquadCrossesFromDeployment(t *testing.T) {
	for seat := range 2 {
		for _, x := range []float64{-8, -5, -2.5, 0, 2.5, 5, 8} {
			for _, z := range []float64{-13, -10, -7.5, -5, -3} {
				if seat == 1 {
					z = -z
				}
				t.Run(fmt.Sprintf("seat%d_x%g_z%g", seat, x, z), func(t *testing.T) {
					w := playingMatch(t)
					w.Rules.TowerDamage = 0
					// Keep the first objective alive so a win or retarget cannot
					// hide a squad member stuck outside its attack perimeter.
					for i := range w.Towers {
						if w.Towers[i].Seat != seat {
							w.Towers[i].HP, w.Towers[i].MaxHP = 10000, 10000
						}
					}
					a := w.deploy(seat, w.ID, 1, "swords", x, z)
					if !a.Accepted {
						if a.Reason != "invalid_placement" {
							t.Fatal(a)
						}
						t.Skip("Formation intersects a tower or river")
					}
					crossed := map[uint64]bool{}
					for range 20 * tickRate {
						w.step()
						for id, u := range w.Units {
							if !w.walkableFor(position(u.State), u.State.Radius, u.State.Airborne) {
								t.Fatalf("unit %d entered an obstacle: %+v", id, u.State)
							}
							if (seat == 0 && u.State.Z > 2) || (seat == 1 && u.State.Z < -2) {
								crossed[id] = true
							}
						}
					}
					for id, u := range w.Units {
						if !crossed[id] {
							t.Errorf("swordsman %d did not cross: pos=%+v path=%v goal=%d", id, position(u.State), u.Path, u.Goal)
						}
						if u.State.AttackTick == 0 {
							t.Errorf("swordsman %d never reached attack range: %+v", id, position(u.State))
						}
					}
					if len(crossed) != 3 {
						t.Fatalf("only %d squad members crossed", len(crossed))
					}
				})
			}
		}
	}
}
