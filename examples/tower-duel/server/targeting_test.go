package main

import (
	"fmt"
	"math"
	"testing"
)

func targetingFixture(t *testing.T, kind string, seat int, lane float64) (*match, *unit, *entity) {
	t.Helper()
	w := playingMatch(t)
	for i := range w.Towers {
		tower := &w.Towers[i]
		if tower.Seat == seat || tower.Kind != "tower" || tower.X != lane {
			continue
		}
		forward := 1.0
		if seat == 1 {
			forward = -1
		}
		card := w.Rules.ByID[kind]
		u := &unit{State: entity{ID: 10001, Seat: seat, Kind: kind,
			X: lane + math.Copysign(.5, lane), Z: tower.Z - 1.9*forward,
			Radius: guardRadius, HP: card.HP, MaxHP: card.HP}, LaneX: lane}
		w.Units[u.State.ID] = u
		return w, u, tower
	}
	t.Fatal("front tower missing")
	return nil, nil, nil
}

func TestTargetTowerAndNearbyDefenders(t *testing.T) {
	for _, scenario := range []string{"behind", "front", "outside_aggro", "no_defender", "after_tower_lock"} {
		for _, kind := range []string{"guard", "swords", "charger", "archer", "bomber", "siege"} {
			for seat := range 2 {
				for _, lane := range []float64{-5, 5} {
					t.Run(fmt.Sprintf("%s/%s/seat%d/lane%g", scenario, kind, seat, lane), func(t *testing.T) {
						w, u, tower := targetingFixture(t, kind, seat, lane)
						forward := 1.0
						if seat == 1 {
							forward = -1
						}
						if scenario == "after_tower_lock" {
							for wait := 0; wait < tickRate && u.LockedTarget == 0; wait++ {
								w.step()
							}
							if u.LockedTarget != tower.ID {
								t.Fatal("attacker did not begin a tower attack")
							}
						}
						want := tower.ID
						if scenario != "no_defender" {
							x, z := u.State.X, tower.Z+1.8*forward
							if scenario == "front" {
								x, z = lane+math.Copysign(1.5, lane), tower.Z-1.2*forward
							} else if scenario == "outside_aggro" {
								z = tower.Z + 6.8*forward
							}
							ack := w.deploy(1-seat, w.ID, 1, "archer", x, z)
							if !ack.Accepted {
								t.Fatalf("illegal defender placement: %+v", ack)
							}
							// Hold the defender in place; attacker stats, tower damage,
							// collision, navigation and attack timing remain canonical.
							w.Units[ack.UnitID].Cooldown = 10000
							if (scenario == "front" || scenario == "behind") && kind != "siege" {
								want = ack.UnitID
							}
						}
						goal, ok := w.target(u, w.sortedUnits())
						if !ok || goal.ID != want {
							t.Fatalf("initial target = %d, want %d (tower %d)", goal.ID, want, tower.ID)
						}
						lateral := 0.0
						for wait := 0; wait < 5*tickRate && u.State.HitTick == 0; wait++ {
							w.step()
							lateral = math.Max(lateral, math.Abs(u.State.X-tower.X))
							if w.Units[u.State.ID] == nil {
								t.Fatal("attacker died before attacking")
							}
						}
						if u.State.HitTick == 0 || u.State.HitTarget != want {
							t.Fatalf("first hit = %d at tick %d, want %d", u.State.HitTarget, u.State.HitTick, want)
						}
						if want == tower.ID && tower.HP == tower.MaxHP {
							t.Fatal("targeted tower did not take damage")
						}
						t.Logf("target=%d first-hit=%d tick=%d lateral=%.3f tower-hp=%d", goal.ID, u.State.HitTarget, u.State.HitTick, lateral, tower.HP)
					})
				}
			}
		}
	}
}

func TestUnstartedTowerAttackYieldsToEnemy(t *testing.T) {
	w, u, tower := targetingFixture(t, "guard", 0, -5)
	enemy := testSoldier(201, 1, "archer", -6.5, 6.9)
	w.Units[201] = enemy
	if distance(position(u.State), position(enemy.State)) >= distance(position(u.State), position(*tower)) {
		t.Fatal("fixture must put the enemy centre closer than the tower centre")
	}
	goal, _ := w.target(u, w.sortedUnits())
	if goal.ID != enemy.State.ID {
		t.Fatalf("nearer tower took priority over detected enemy: %+v", goal)
	}
}

func TestTargetDeterministicTies(t *testing.T) {
	t.Run("troop_before_unlocked_tower", func(t *testing.T) {
		w, u, tower := targetingFixture(t, "guard", 0, -5)
		gap := distance(position(u.State), position(*tower)) - u.State.Radius - tower.Radius
		enemy := testSoldier(201, 1, "archer", u.State.X-gap-2*guardRadius, u.State.Z)
		goal, _ := w.target(u, []*unit{enemy, u})
		if goal.ID != enemy.State.ID {
			t.Fatal("unlocked tower should yield to the enemy", goal)
		}
	})
	t.Run("nearest_troop_then_id", func(t *testing.T) {
		w := playingMatch(t)
		u := testSoldier(101, 0, "guard", 0, -5)
		a := testSoldier(201, 1, "archer", -1, -3)
		b := testSoldier(202, 1, "archer", 1, -3)
		far := testSoldier(203, 1, "archer", 0, -1)
		ally := testSoldier(204, 0, "archer", .7, -5)
		dead := testSoldier(205, 1, "archer", -.7, -5)
		dead.State.HP = 0
		for _, units := range [][]*unit{{far, b, a, ally, dead}, {dead, ally, a, b, far}} {
			goal, _ := w.target(u, units)
			if goal.ID != a.State.ID {
				t.Fatal("nearest living enemy should be independent of iteration order", goal)
			}
		}
	})
}

func TestTargetAggroAcrossWall(t *testing.T) {
	for _, scenario := range []struct {
		name, kind string
		x, z, uz   float64
		wantEnemy  bool
	}{
		{"aggro_boundary", "guard", 4.2, -5, -5, true},
		{"outside_aggro", "guard", 4.2001, -5, -5, false},
		{"melee_across_wall", "guard", 0, .9, -.9, true},
		{"ranged_across_wall", "archer", 0, .8, -.8, true},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			w := playingMatch(t)
			u := testSoldier(101, 0, scenario.kind, 0, scenario.uz)
			enemy := testSoldier(201, 1, "guard", scenario.x, scenario.z)
			goal, _ := w.target(u, []*unit{u, enemy})
			if (goal.ID == enemy.State.ID) != scenario.wantEnemy {
				t.Fatal("incorrect enemy eligibility", goal)
			}
		})
	}
}

func TestTargetApproachesEnemyBeforeAttacking(t *testing.T) {
	w := playingMatch(t)
	u := testSoldier(101, 0, "guard", 0, -5)
	enemy := testSoldier(201, 1, "archer", 0, -2.5)
	enemy.Cooldown = 10000
	w.Units[101], w.Units[201] = u, enemy
	w.step()
	if u.State.Target != enemy.State.ID || u.State.Z <= -5 || u.State.AttackTick != 0 || u.LockedTarget != 0 {
		t.Fatal("detected enemy should be approached before starting an attack", u.State)
	}
	for wait := 0; wait < 3*tickRate && u.State.HitTick == 0; wait++ {
		w.step()
	}
	if u.State.HitTarget != enemy.State.ID || enemy.State.HP >= enemy.State.MaxHP {
		t.Fatal("attacker never reached and hit the detected enemy", u.State)
	}
}

func TestTargetReevaluatesBeforeFirstAttack(t *testing.T) {
	w, u, tower := targetingFixture(t, "guard", 0, -5)
	u.State.Z = 4
	enemy := testSoldier(201, 1, "archer", u.State.X, 5.6)
	enemy.Cooldown = 10000
	w.Units[201] = enemy
	w.step()
	if u.State.Target != 201 || u.LockedTarget != 0 {
		t.Fatal("expected an unlocked approach to the nearer enemy", u.State)
	}
	enemy.State.Z = 9.5
	w.step()
	if u.State.Target != tower.ID || u.LockedTarget != 0 {
		t.Fatal("unattacked enemy retreating behind a nearer tower should not retain priority", u.State)
	}
}

func TestTargetNearbyTowerAfterCrossLaneFight(t *testing.T) {
	w, u, _ := targetingFixture(t, "swords", 0, -5)
	u.LaneX = 5
	enemy := testSoldier(201, 1, "archer", u.State.X, 9.5)
	goal, _ := w.target(u, []*unit{u, enemy})
	if goal.ID != enemy.State.ID {
		t.Fatal("nearby enemy should take priority while unlocked", goal)
	}
	goal, _ = w.target(u, []*unit{u})
	if goal.Kind != "tower" || goal.X != u.LaneX {
		t.Fatal("without a local fight, the deployment lane must remain the objective", goal)
	}
}
