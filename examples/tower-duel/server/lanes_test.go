package main

import (
	"fmt"
	"math"
	"testing"
)

func TestDeploymentLaneAfterFrontTowerDestroyed(t *testing.T) {
	for _, card := range []string{"guard", "archer", "swords"} {
		for seat := range 2 {
			for _, x := range []float64{-.1, .1, -2.5, 2.5, -5, 5} {
				t.Run(fmt.Sprintf("%s_seat%d_x%g", card, seat, x), func(t *testing.T) {
					w := playingMatch(t)
					w.Rules.TowerDamage = 0
					z, lane := -5.0, math.Copysign(5, x)
					if seat == 1 {
						z = -z
					}
					var keep uint64
					for i := range w.Towers {
						if w.Towers[i].Seat == seat {
							continue
						}
						if w.Towers[i].Kind == "keep" {
							keep = w.Towers[i].ID
						} else if w.Towers[i].X == lane {
							w.Towers[i].HP = 0
						}
					}
					a := w.deploy(seat, w.ID, 1, card, x, z)
					if !a.Accepted {
						t.Fatal(a)
					}
					units := w.sortedUnits()
					for _, u := range units {
						goal, ok := w.target(u, units)
						if !ok || goal.ID != keep {
							t.Fatalf("squad should continue to keep %d on lane %g, got %+v", keep, lane, goal)
						}
					}
					crossed := map[uint64]bool{}
					for range 20 * tickRate {
						w.step()
						for id, u := range w.Units {
							if math.Abs(u.State.Z) < w.Map.RiverHalfWidth {
								if math.Abs(u.State.X-lane) > w.Map.BridgeHalfWidth-guardRadius {
									t.Fatalf("unit %d used the other bridge: %+v", id, u.State)
								}
								crossed[id] = true
							}
							if u.State.Target != keep {
								t.Fatalf("unit changed to the other lane's tower: %+v", u.State)
							}
						}
					}
					if len(crossed) != w.Rules.ByID[card].Count {
						t.Fatalf("only %d squad members crossed", len(crossed))
					}
				})
			}
		}
	}
}

func TestSquadSharesLaneAtCentreLine(t *testing.T) {
	for seat := range 2 {
		for _, x := range []float64{-.1, .1} {
			w := playingMatch(t)
			z := -5.0
			if seat == 1 {
				z = -z
			}
			a := w.deploy(seat, w.ID, 1, "swords", x, z)
			if !a.Accepted {
				t.Fatal(a)
			}
			units := w.sortedUnits()
			for _, u := range units {
				goal, ok := w.target(u, units)
				if !ok || goal.Kind != "tower" || goal.X != math.Copysign(5, x) {
					t.Fatalf("squad split across lanes near the centre: %+v", goal)
				}
			}
		}
	}
}

func TestUnitReturnsToObjectiveAfterOtherBridgeFight(t *testing.T) {
	for seat := range 2 {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		x, lane := -5.0, 5.0
		if seat == 1 {
			x, lane = -x, -lane
		}
		w.Units[101] = &unit{State: entity{ID: 101, Seat: seat, Kind: "guard", X: x, Z: 0, Radius: guardRadius, HP: 220, MaxHP: 220}, LaneX: lane}
		for range 20 * tickRate {
			w.step()
		}
		u := w.Units[101]
		if u.State.AttackTick == 0 || u.State.X*lane <= 0 || u.LaneX != lane {
			t.Fatalf("unit became trapped after combat on another bridge: %+v", u)
		}
	}
}
