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
				if card == "swords" && math.Abs(x) < .5 {
					continue // The central squad has a dedicated split-lane test.
				}
				t.Run(fmt.Sprintf("%s_seat%d_x%g", card, seat, x), func(t *testing.T) {
					w := playingMatch(t)
					w.Rules.TowerDamage = 0
					z, lane := -5.0, w.nearestBridge(seat, x)
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
							if math.Abs(u.State.Z) < w.Map.barrierHalfWidth() {
								if math.Abs(u.State.X-lane) > w.Map.BridgeHalfWidth-guardRadius {
									t.Fatalf("unit %d used the other bridge: %+v", id, u.State)
								}
								crossed[id] = true
							}
							if w.Tick >= u.State.ReadyTick && u.State.Target != keep {
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

func TestSwordSquadSplitsAtCentreAndStaysTogetherOutsideIt(t *testing.T) {
	for seat := range 2 {
		for _, x := range []float64{-1.5, -.5, 0, .5, 1.5} {
			t.Run(fmt.Sprintf("seat%d/x%g", seat, x), func(t *testing.T) {
				w := playingMatch(t)
				w.Rules.TowerDamage = 0
				forward := 1.0
				if seat == 1 {
					forward = -1
				}
				ack := w.deploy(seat, w.ID, 1, "swords", x, -5*forward)
				if !ack.Accepted || ack.Count != 3 {
					t.Fatal(ack)
				}
				primary := w.nearestBridge(seat, x)
				want := map[float64]int{primary: 3}
				if math.Abs(x) <= .5 {
					want[w.Map.Bridges[0]], want[w.Map.Bridges[1]] = 1, 1
					want[primary]++
				}
				crossed := map[uint64]bool{}
				actual := map[float64]int{}
				for i := 0; i < 30*tickRate && len(crossed) < 3; i++ {
					w.step()
					for id, unit := range w.Units {
						if crossed[id] || unit.State.Z*forward < 0 {
							continue
						}
						lane := w.nearestBridge(seat, unit.State.X)
						if math.Abs(unit.State.X-lane) > w.Map.BridgeHalfWidth-unit.State.Radius {
							t.Fatalf("unit crossed outside a gate: %+v", unit.State)
						}
						crossed[id] = true
						actual[lane]++
					}
				}
				for _, lane := range w.Map.Bridges {
					if actual[lane] != want[lane] {
						t.Fatalf("gate %g: crossed %d swordsmen, want %d; crossings=%v", lane, actual[lane], want[lane], actual)
					}
				}
			})
		}
	}
}

func TestUnitFinishesOtherBridgeCrossingAndAttacksNearestTower(t *testing.T) {
	for seat := range 2 {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		x, lane := w.Map.Bridges[0], w.Map.Bridges[1]
		if seat == 1 {
			x, lane = -x, -lane
		}
		w.Units[101] = &unit{State: entity{ID: 101, Seat: seat, Kind: "guard", X: x, Z: 0, Radius: guardRadius, HP: 220, MaxHP: 220}, LaneX: lane}
		var nearestTower uint64
		for _, tower := range w.Towers {
			if tower.Seat != seat && tower.Kind == "tower" && tower.X == x {
				nearestTower = tower.ID
				break
			}
		}
		u := w.Units[101]
		for i := 0; i < 20*tickRate && u.State.HitTick == 0; i++ {
			w.step()
		}
		if nearestTower == 0 || u.State.HitTarget != nearestTower || u.LaneX != lane {
			t.Fatalf("unit did not finish crossing and attack the closest tower: %+v", u)
		}
	}
}
