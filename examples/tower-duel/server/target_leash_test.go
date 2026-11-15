package main

import (
	"fmt"
	"math"
	"testing"
)

func TestSoldierDropsEscapedTarget(t *testing.T) {
	for _, kind := range []string{"guard", "archer", "swords", "bomber", "charger"} {
		for seat := range 2 {
			for _, phase := range []string{"windup", "after_hit", "chasing"} {
				for _, replacement := range []bool{false, true} {
					t.Run(fmt.Sprintf("%s/seat%d/%s/replacement%t", kind, seat, phase, replacement), func(t *testing.T) {
						w := playingMatch(t)
						w.Rules.TowerDamage = 0
						forward := 1.0
						if seat == 1 {
							forward = -1
						}
						u := testSoldier(101, seat, kind, 0, -5*forward)
						old := testSoldier(201, 1-seat, "guard", 0, -3.85*forward)
						old.Cooldown = 10000
						w.Units[101], w.Units[201] = u, old
						card := w.Rules.ByID[kind]
						w.step()
						if u.LockedTarget != old.State.ID || u.WindupTarget != old.State.ID {
							t.Fatal("initial attack did not lock the enemy", u)
						}
						// The windup case escapes on the tick that would deal damage.
						steps := card.WindupTicks
						if phase == "windup" {
							steps--
						}
						for range steps {
							w.step()
						}
						if phase == "chasing" {
							old.State.X, old.State.Z = u.State.X+card.Aggro-.4, u.State.Z
							x := u.State.X
							w.step()
							if u.LockedTarget != old.State.ID || u.State.Target != old.State.ID || u.State.X <= x {
								t.Fatal("enemy still inside aggro was not pursued", u)
							}
						}
						old.State.X, old.State.Z = u.State.X+card.Aggro+.01, u.State.Z
						outside := testSoldier(203, 1-seat, "guard", u.State.X-card.Aggro-.1, u.State.Z)
						outside.Cooldown = 10000
						w.Units[203] = outside
						objective := w.buildingObjective(u)
						want := objective.ID
						if replacement {
							for id, d := range map[uint64]float64{202: .75, 204: 1.6} {
								enemy := testSoldier(id, 1-seat, "guard", u.State.X-d, u.State.Z)
								enemy.Cooldown = 10000
								w.Units[id] = enemy
							}
							want = 202
						}
						oldHP, hitTick, attackTick := old.State.HP, u.State.HitTick, u.State.AttackTick
						towerDistance := distance(position(u.State), position(objective))
						w.step()
						if u.State.Target != want || u.LockedTarget != 0 || u.WindupTarget != 0 || u.WindupProgress != 0 {
							t.Fatalf("escaped enemy retained lock or windup; want target %d, got %+v", want, u)
						}
						if old.State.HP != oldHP || u.State.HitTick != hitTick || u.State.AttackTick != attackTick {
							t.Fatal("escaping enemy took a cancelled hit or reset the attack cooldown", u)
						}
						if replacement && w.Units[202].State.HP != w.Units[202].State.MaxHP {
							t.Fatal("replacement inherited the cancelled windup")
						}
						if !replacement && (u.Goal != want || distance(position(u.State), position(objective)) >= towerDistance) {
							t.Fatal("soldier did not resume movement toward its tower objective", u)
						}
						for _, state := range w.snapshot(seat, "snapshot").Units {
							if state.ID == u.State.ID && (state.Target != want || state.LockedTarget != 0) {
								t.Fatal("snapshot still advertises the escaped enemy lock", state)
							}
						}
					})
				}
			}
		}
	}
}

func TestLockedEnemyAggroBoundary(t *testing.T) {
	for _, kind := range []string{"guard", "archer", "swords", "bomber", "charger"} {
		for _, offset := range []string{"inside", "boundary", "outside"} {
			t.Run(kind+"/"+offset, func(t *testing.T) {
				w := playingMatch(t)
				u := testSoldier(101, 0, kind, 0, -5)
				d := w.Rules.ByID[kind].Aggro
				if offset == "inside" {
					d = math.Nextafter(d, 0)
				} else if offset == "outside" {
					d = math.Nextafter(d, math.Inf(1))
				}
				enemy := testSoldier(201, 1, "guard", d, -5)
				u.LockedTarget = enemy.State.ID
				goal, _ := w.target(u, []*unit{u, enemy})
				if offset == "outside" {
					if goal.ID == enemy.State.ID || u.LockedTarget != 0 {
						t.Fatal("enemy immediately outside aggro retained the lock", goal, u.LockedTarget)
					}
				} else if goal.ID != enemy.State.ID || u.LockedTarget != enemy.State.ID {
					t.Fatal("enemy on or within aggro should retain its lock", goal, u.LockedTarget)
				}
			})
		}
	}
}
