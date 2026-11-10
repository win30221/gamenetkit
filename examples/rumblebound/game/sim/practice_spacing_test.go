package sim

import (
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func assertPracticeSpacingBodies(t *testing.T, w *World) {
	t.Helper()
	owner, bot := w.Seats["human"], w.Seats["npc"]
	for _, seat := range []*Seat{owner, bot} {
		if seat.State.HP > 0 && !shared.CircleClear(w.Map, seat.State.X, seat.State.Z, w.Rules.PlayerRadius) {
			t.Fatalf("spacing crossed arena geometry: %+v", seat.State)
		}
	}
	if owner.State.HP > 0 && bot.State.HP > 0 && math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z) < 2*w.Rules.PlayerRadius-1e-6 {
		t.Fatal("spacing overlapped a living opponent")
	}
}

func TestPracticeSpacingRetreatsAndSettlesAtSafeRange(t *testing.T) {
	w := practiceWorld(t, "kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -2, 5
	bot.State.X, bot.State.Z = 0, 5
	for range 45 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
	}
	gap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
	if gap < 4 || gap > 5.5 || w.NextProjectileID != 0 {
		t.Fatalf("ranged spacing failed to create a safe gap without attacking: gap=%f bot=%+v", gap, bot.State)
	}
	x, z := bot.State.X, bot.State.Z
	for range 35 {
		w.Step()
	}
	if math.Hypot(bot.State.X-x, bot.State.Z-z) > .05 {
		t.Fatal("stationary enemy caused repeated retreat/approach oscillation")
	}
}

func TestPracticeSpacingDefersNewAttackThenResumesInRange(t *testing.T) {
	w := practiceWorld(t, "attack+kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z, owner.State.ProtectedUntilTick = -2, 5, 0
	bot.State.X, bot.State.Z = 0, 5
	w.Step()
	if bot.State.AttackStopUntilTick > w.Tick || math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z) <= 2.05 {
		t.Fatal("new attack locked the bot in place before it could create distance")
	}
	lastFire := bot.State.FireReadyTick
	sawFire := false
	for range 60 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
		if bot.State.FireReadyTick != lastFire {
			gap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
			if gap < 3.4 {
				t.Fatalf("bot resumed attacking before creating space: gap=%f", gap)
			}
			if lastFire != 0 && bot.State.FireReadyTick-lastFire < w.seatRules(bot).FireCooldownTicks {
				t.Fatal("spacing reset an attack cooldown")
			}
			lastFire, sawFire = bot.State.FireReadyTick, true
		}
	}
	if !sawFire || owner.State.HP >= 100 {
		t.Fatal("ranged bot never resumed its real attack after backing away")
	}
}

func TestPracticeSpacingUsesHeroWeaponRole(t *testing.T) {
	for _, heroID := range []string{"ranger", "mage", "tank"} {
		t.Run(heroID, func(t *testing.T) {
			w := practiceWorld(t, "kite")
			owner, bot := w.Seats["human"], w.Seats["npc"]
			hero, _ := w.Rules.Hero(heroID)
			bot.hero, bot.State.HeroID, bot.State.HP = &hero, heroID, hero.MaxHP
			owner.State.X, owner.State.Z, owner.State.ProtectedUntilTick = -1, 5, 0
			bot.State.X, bot.State.Z = 0, 5
			for range 45 {
				w.Step()
				assertPracticeSpacingBodies(t, w)
			}
			gap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
			if heroID == "tank" {
				if gap < 1.3 || gap > 2.25 {
					t.Fatalf("knight spacing abandoned its melee range: gap=%f", gap)
				}
				if err := w.setPracticeMode("human", "attack+kite"); err != nil {
					t.Fatal(err)
				}
				for range 35 {
					w.Step()
				}
				if owner.State.HP >= 100 || w.NextProjectileID != 0 {
					t.Fatal("knight could not use ordinary melee after separating")
				}
			} else if gap < 3.6 || gap > 5.5 {
				t.Fatalf("ranged hero did not gain usable breathing room: gap=%f", gap)
			}
		})
	}
}

func TestPracticeSpacingRespectsExistingAttackStopAndCooldowns(t *testing.T) {
	w := practiceWorld(t, "kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -2, 5
	bot.State.X, bot.State.Z = 0, 5
	bot.State.AttackStopUntilTick = 10
	bot.State.FireReadyTick, bot.State.DashReadyTick = 20, 30
	for range 9 {
		w.Step()
		if bot.State.X != 0 || bot.State.Z != 5 || bot.State.AttackStopUntilTick != 10 {
			t.Fatal("spacing cancelled an attack stop that had already begun")
		}
	}
	for range 15 {
		w.Step()
	}
	if math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z) < 4 || bot.State.FireReadyTick != 20 || bot.State.DashReadyTick != 30 {
		t.Fatal("spacing did not resume normally after attack recovery, or reset real cooldowns")
	}
}

func TestPracticeSpacingFindsLateralEscapeAtWalls(t *testing.T) {
	for _, fixture := range []struct {
		name                 string
		ownerX, ownerZ, x, z float64
	}{
		{name: "arena", ownerX: 7.1, ownerZ: 5, x: 9.2, z: 5},
		{name: "cover", ownerX: -6.5, ownerZ: 0, x: -4.65, z: 0},
	} {
		t.Run(fixture.name, func(t *testing.T) {
			w := practiceWorld(t, "kite")
			owner, bot := w.Seats["human"], w.Seats["npc"]
			owner.State.X, owner.State.Z = fixture.ownerX, fixture.ownerZ
			bot.State.X, bot.State.Z = fixture.x, fixture.z
			for range 75 {
				w.Step()
				assertPracticeSpacingBodies(t, w)
			}
			gap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
			if gap < 3.5 || math.Abs(bot.State.Z-fixture.z) < 1 {
				t.Fatalf("bot ground into the wall instead of escaping sideways: gap=%f bot=%+v", gap, bot.State)
			}
		})
	}
}

func TestPracticeSpacingReturnsToCaptureWhenEnemyLeaves(t *testing.T) {
	w := practiceWorld(t, "capture+kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	w.PointIndex = 1
	owner.State.X, owner.State.Z = -1.5, 5
	bot.State.X, bot.State.Z = 0, 5
	for range 45 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
	}
	if math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z) < 3.4 {
		t.Fatal("capture priority prevented emergency separation")
	}
	owner.State.X, owner.State.Z = -8, -8
	for range 120 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
	}
	if !w.insidePoint(bot) || w.ScoreTicks[bot.State.TeamID] == 0 {
		t.Fatal("bot permanently abandoned its capture objective after retreating")
	}
}

func TestPracticeSpacingDodgeKeepsSafeEscapeAwayFromEnemy(t *testing.T) {
	w := practiceWorld(t, "dodge+kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -2, 5
	bot.State.X, bot.State.Z, bot.State.ProtectedUntilTick = 0, 5, 0
	w.Projectiles = []shot{{State: ProjectileState{OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID, X: -1.5, Z: 5, DX: 1, ExpiresTick: 21}, speed: 12, damage: 25}}
	previousGap := 2.0
	for range 30 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
		gap := math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
		if gap < previousGap-.05 {
			t.Fatalf("dodge steered toward the close enemy despite an available outward escape: before=%f after=%f", previousGap, gap)
		}
		previousGap = gap
	}
	if bot.State.HP != 100 || previousGap < 3.8 {
		t.Fatalf("combined spacing and dodge did not evade while separating: gap=%f bot=%+v", previousGap, bot.State)
	}
}

func TestPracticeSpacingTrappedCornerStillUsesSelectedAttack(t *testing.T) {
	w := practiceWorld(t, "attack+kite")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z, owner.State.ProtectedUntilTick = 8.92, 8.92, 0
	bot.State.X, bot.State.Z = 9.5, 9.5
	assertPracticeSpacingBodies(t, w)
	for range 30 {
		w.Step()
		assertPracticeSpacingBodies(t, w)
	}
	if owner.State.HP >= 100 || bot.State.FireReadyTick == 0 || w.NextProjectileID == 0 {
		t.Fatalf("unavailable retreat starved the selected real attack: targetHP=%d bot=%+v", owner.State.HP, bot.State)
	}
}
