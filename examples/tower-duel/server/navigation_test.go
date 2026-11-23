package main

import (
	"fmt"
	"math"
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
					for range 40 * tickRate {
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

func TestMarchMergesDiagonallyBeforeGate(t *testing.T) {
	for seat := range 2 {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		forward := 1.0
		if seat == 1 {
			forward = -1
		}
		lane := w.Map.Bridges[seat]
		// Start inside the field and outside the lane, with a clear diagonal.
		start := point{lane - 3*forward, -6 * forward}
		ack := w.deploy(seat, w.ID, 1, "guard", start.X, start.Z)
		if !ack.Accepted {
			t.Fatal(ack)
		}
		u := w.Units[ack.UnitID]
		for w.Tick < u.State.ReadyTick {
			w.step()
		}
		if (u.State.X-start.X)*forward <= 0 || (u.State.Z-start.Z)*forward <= 0 {
			t.Fatalf("seat %d did not advance diagonally on the first step: %+v", seat, u.State)
		}
		gateSteps := 0
		for range 440 {
			previous := position(u.State)
			w.step()
			if math.Abs(previous.Z) < w.Map.barrierHalfWidth()-.15 {
				gateSteps++
				if math.Abs(u.State.X-previous.X) > .001 || (u.State.Z-previous.Z)*forward <= 0 {
					t.Fatalf("seat %d did not walk straight through the gate: %+v", seat, u.State)
				}
			}
			if u.State.Z*forward >= 1 {
				break
			}
		}
		if gateSteps == 0 || u.State.Z*forward < 1 {
			t.Fatalf("seat %d never crossed the gate: %+v", seat, u.State)
		}
	}
}

func TestMarchRejoinsLaneAfterFightingOnEnemySide(t *testing.T) {
	for seat := range 2 {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		forward := 1.0
		if seat == 1 {
			forward = -1
		}
		lane := w.Map.Bridges[seat]
		start := point{lane + 4*forward, 2.5 * forward}
		u := testSoldier(101, seat, "guard", start.X, start.Z)
		u.LaneX = lane
		building := w.buildingObjective(u)
		// The defender can interrupt an existing march from attack range,
		// leaving behind a waypoint the unit must no longer follow.
		u.Goal, u.NextRoute = building.ID, 100
		u.Path = []point{{start.X, start.Z + forward}}
		defender := testSoldier(102, 1-seat, "guard", start.X+forward, start.Z)
		defender.State.HP, defender.Cooldown = 1, 999
		w.Units[101], w.Units[102] = u, defender
		for w.Units[102] != nil && w.Tick < 60 {
			w.step()
		}
		if w.Units[102] != nil || u.State.HitTarget != 102 {
			t.Fatal("defender was not defeated")
		}
		before := position(u.State)
		w.step()
		if u.State.Target != building.ID || (u.State.X-before.X)*forward >= 0 || (u.State.Z-before.Z)*forward <= 0 {
			t.Fatalf("seat %d did not rejoin its deployment lane diagonally: %+v", seat, u.State)
		}
	}
}

func TestMarchHugsAlliedBuildingBeforeRejoiningLane(t *testing.T) {
	for _, spawn := range []struct {
		name string
		x    float64
	}{
		{"behind_keep", 0},
		{"outer_rear_corner", -9.5},
	} {
		for seat := range 2 {
			t.Run(fmt.Sprintf("%s/seat%d", spawn.name, seat), func(t *testing.T) {
				w := playingMatch(t)
				w.Rules.TowerDamage = 0
				forward := 1.0
				if seat == 1 {
					forward = -1
				}
				start := point{spawn.x * forward, -13 * forward}
				ack := w.deploy(seat, w.ID, 1, "guard", start.X, start.Z)
				if !ack.Accepted {
					t.Fatal(ack)
				}
				u := w.Units[ack.UnitID]
				for w.Tick < u.State.ReadyTick {
					w.step()
				}
				if (u.State.X-start.X)*(u.LaneX-start.X) <= 0 || (u.State.Z-start.Z)*forward <= 0 {
					t.Fatalf("first step should be diagonal towards the building flank: %+v", u.State)
				}

				var blocker entity
				for _, building := range w.Towers {
					if building.Seat == seat && (spawn.x == 0 && building.Kind == "keep" ||
						spawn.x != 0 && building.Kind == "tower" && building.X == u.LaneX) {
						blocker = building
						break
					}
				}
				if blocker.ID == 0 {
					t.Fatal("missing allied building")
				}
				passedFlank, merged := false, false
				for range 800 {
					before := position(u.State)
					w.step()
					after := position(u.State)
					if !w.walkableFor(after, u.State.Radius, false) {
						t.Fatalf("unit entered an obstacle: %+v", after)
					}
					if (before.Z-blocker.Z)*forward <= 0 && (after.Z-blocker.Z)*forward > 0 {
						gap := math.Abs(after.X-blocker.X) - blocker.Radius - u.State.Radius
						passedFlank = math.Abs(after.X-before.X) < .001 && gap >= .04 && gap < .3
					}
					if passedFlank && (after.X-before.X)*(u.LaneX-before.X) > .001 &&
						(after.Z-before.Z)*forward > 0 {
						merged = true
					}
					if after.Z*forward >= 0 {
						if math.Abs(after.X-u.LaneX) > w.Map.BridgeHalfWidth-u.State.Radius {
							t.Fatalf("unit left its lane: %+v", after)
						}
						break
					}
				}
				if !passedFlank || !merged || u.State.Z*forward < 0 {
					t.Fatalf("expected flank, diagonal merge, then gate crossing: flank=%v merge=%v state=%+v",
						passedFlank, merged, u.State)
				}
			})
		}
	}
}
