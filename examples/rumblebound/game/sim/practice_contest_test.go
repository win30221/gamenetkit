package sim

import (
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func practiceContestKnight(t *testing.T, mode string) *World {
	t.Helper()
	w := practiceWorld(t, mode)
	bot := w.Seats["npc"]
	hero, _ := w.Rules.Hero("tank")
	bot.hero, bot.State.HeroID, bot.State.HP = &hero, hero.HeroID, hero.MaxHP
	w.Seats["human"].State.ProtectedUntilTick = 0
	return w
}

func assertPracticeContestBodies(t *testing.T, w *World) {
	t.Helper()
	owner, bot := w.Seats["human"], w.Seats["npc"]
	for _, seat := range []*Seat{owner, bot} {
		if seat.State.HP > 0 && !shared.CircleClear(w.Map, seat.State.X, seat.State.Z, w.Rules.PlayerRadius) {
			t.Fatalf("contesting crossed arena geometry: %+v", seat.State)
		}
	}
	if owner.State.HP > 0 && bot.State.HP > 0 && math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z) < 2*w.Rules.PlayerRadius-1e-6 {
		t.Fatal("contesting overlapped a living opponent")
	}
}

func TestPracticeContestKnightApproachesAndDamagesCurrentPointEnemy(t *testing.T) {
	for _, mode := range []string{"capture+attack", "capture+attack+dodge+kite"} {
		for _, edges := range []bool{false, true} {
			name := mode + "/spawn"
			if edges {
				name = mode + "/opposite-edges"
			}
			t.Run(name, func(t *testing.T) {
				w := practiceContestKnight(t, mode)
				owner, bot := w.Seats["human"], w.Seats["npc"]
				if edges {
					w.PointIndex = 1
					owner.State.X, owner.State.Z = -2.35, 5
					bot.State.X, bot.State.Z = 2.35, 5
				}
				initialGap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
				if !w.insidePoint(owner) || !w.insidePoint(bot) || initialGap <= bot.hero.MeleeRange+w.Rules.PlayerRadius {
					t.Fatal("fixture is not contested outside the knight's weapon reach")
				}
				damagedAt := uint64(0)
				lastFire := bot.State.FireReadyTick
				for range 90 {
					w.Step()
					assertPracticeContestBodies(t, w)
					if !w.insidePoint(bot) {
						t.Fatal("knight abandoned the point while its opponent stayed inside")
					}
					if owner.State.HP < 100 && damagedAt == 0 {
						damagedAt = w.Tick
					}
					if bot.State.FireReadyTick != lastFire {
						if lastFire != 0 && bot.State.FireReadyTick-lastFire < w.seatRules(bot).FireCooldownTicks {
							t.Fatal("contesting bypassed the real melee cooldown")
						}
						lastFire = bot.State.FireReadyTick
					}
				}
				if damagedAt == 0 || damagedAt > 30 || w.NextCombatEventID == 0 || w.NextProjectileID != 0 {
					t.Fatalf("knight failed to close and use real melee promptly: damagedAt=%d initialGap=%f targetHP=%d bot=%+v", damagedAt, initialGap, owner.State.HP, bot.State)
				}
			})
		}
	}
}

func TestPracticeContestWithoutAttackRemainsPassive(t *testing.T) {
	for _, mode := range []string{"capture", "capture+dodge+kite"} {
		t.Run(mode, func(t *testing.T) {
			w := practiceContestKnight(t, mode)
			owner, bot := w.Seats["human"], w.Seats["npc"]
			w.PointIndex = 1
			owner.State.X, owner.State.Z = -2.35, 5
			bot.State.X, bot.State.Z = 2.35, 5
			for range 90 {
				w.Step()
				assertPracticeContestBodies(t, w)
			}
			if owner.State.HP != 100 || bot.State.X != 2.35 || bot.State.Z != 5 || w.NextCombatEventID != 0 || w.NextProjectileID != 0 || w.PointStatus != "contested" {
				t.Fatal("a passive capture mode started chasing or attacking an occupant")
			}
		})
	}
}

func TestPracticeContestDoesNotChaseEnemyOutsidePoint(t *testing.T) {
	w := practiceContestKnight(t, "capture+attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	bot.State.X, bot.State.Z = 0, 0
	owner.State.X, owner.State.Z = 0, 3
	for range 60 {
		w.Step()
		assertPracticeContestBodies(t, w)
	}
	if bot.State.X != 0 || bot.State.Z != 0 || owner.State.HP != 100 || w.ScoreTicks[bot.State.TeamID] < 30 {
		t.Fatal("knight chased an enemy outside the point instead of protecting its score")
	}
}

func TestPracticeContestStartsWhenEnemyEntersCurrentPoint(t *testing.T) {
	w := practiceContestKnight(t, "capture+attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	w.PointIndex = 1
	bot.State.X, bot.State.Z = 2.35, 5
	owner.State.X, owner.State.Z = -8, -8
	for range 20 {
		w.Step()
	}
	owner.State.X, owner.State.Z = -2.35, 5
	for range 60 {
		w.Step()
		assertPracticeContestBodies(t, w)
	}
	if owner.State.HP >= 100 || bot.State.X > 1.5 || !w.insidePoint(bot) {
		t.Fatal("a newly contesting enemy did not interrupt passive guarding")
	}
}

func TestPracticeContestStopsPursuitWhenEnemyLeavesOrDies(t *testing.T) {
	for _, cause := range []string{"leave", "death", "disconnect"} {
		t.Run(cause, func(t *testing.T) {
			w := practiceContestKnight(t, "capture+attack+dodge+kite")
			owner, bot := w.Seats["human"], w.Seats["npc"]
			w.PointIndex = 1
			owner.State.X, owner.State.Z = -2.35, 5
			bot.State.X, bot.State.Z = 2.35, 5
			for range 3 {
				w.Step()
			}
			if cause == "leave" {
				owner.State.X, owner.State.Z = -8, -8
			} else if cause == "disconnect" {
				w.Disconnect("human", owner.Generation)
			} else {
				owner.State.HP, owner.State.RespawnTick = 0, w.Tick+900
			}
			x, z := bot.State.X, bot.State.Z
			for range 60 {
				w.Step()
				assertPracticeContestBodies(t, w)
			}
			if !w.insidePoint(bot) || w.ScoreTicks[bot.State.TeamID] < 30 || math.Hypot(bot.State.X-x, bot.State.Z-z) > .25 {
				t.Fatalf("bot retained its old chase after %s: old=(%f,%f) bot=%+v", cause, x, z, bot.State)
			}
		})
	}
}

func TestPracticeContestRotationAbandonsOldPointTarget(t *testing.T) {
	w := practiceContestKnight(t, "capture+attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	w.PointIndex = 1
	owner.State.X, owner.State.Z = -2.35, 5
	bot.State.X, bot.State.Z = 2.35, 5
	for range 3 {
		w.Step()
	}
	w.Tick = w.Rules.PointRotationTicks - 1
	w.Step()
	if w.PointIndex != 2 {
		t.Fatal("fixture did not rotate the point")
	}
	for range 240 {
		w.Step()
		assertPracticeContestBodies(t, w)
	}
	if !w.insidePoint(bot) || bot.State.Z > -2.5 || w.ScoreTicks[bot.State.TeamID] < 30 || owner.State.HP != 100 {
		t.Fatalf("rotation retained the obsolete contest target: bot=%+v targetHP=%d", bot.State, owner.State.HP)
	}
}
