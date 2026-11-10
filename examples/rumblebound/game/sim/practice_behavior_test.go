package sim

import (
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestPracticeCaptureRoutesScoresAndFollowsRotation(t *testing.T) {
	w := practiceWorld(t, "capture")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -8, -8
	bot.State.X, bot.State.Z = -6, 0
	detoured := false
	for range 240 {
		w.Step()
		detoured = detoured || math.Abs(bot.State.Z) > 1.85
		if !shared.CircleClear(w.Map, bot.State.X, bot.State.Z, w.Rules.PlayerRadius) {
			t.Fatal("capture crossed cover")
		}
	}
	if !detoured || !w.insidePoint(bot) || w.ScoreTicks[bot.State.TeamID] < 30 || w.NextProjectileID != 0 {
		t.Fatalf("capture did not route and score without firing: bot=%+v scores=%v", bot.State, w.ScoreTicks)
	}
	w.Tick = w.Rules.PointRotationTicks - 1
	w.Step()
	if w.PointIndex != 1 {
		t.Fatal("point did not rotate")
	}
	for range 120 {
		w.Step()
	}
	if !w.insidePoint(bot) || bot.State.Z < 2.5 || w.ScoringTeamID != bot.State.TeamID {
		t.Fatalf("bot did not follow the newly active point: %+v", bot.State)
	}
}

func TestPracticeCaptureAttackKeepsObjectiveAndFiresInRange(t *testing.T) {
	w := practiceWorld(t, "capture+attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = 9, -9
	bot.State.X, bot.State.Z = 0, 0
	for range 60 {
		w.Step()
	}
	if math.Hypot(bot.State.X, bot.State.Z) > .01 || w.NextProjectileID != 0 || w.ScoreTicks[bot.State.TeamID] == 0 {
		t.Fatal("capture chased an out-of-range player instead of holding the point")
	}
	owner.State.X, owner.State.Z, owner.State.ProtectedUntilTick = 0, 4, 0
	for range 30 {
		w.Step()
	}
	if w.NextProjectileID == 0 || owner.State.HP >= 100 || !w.insidePoint(bot) {
		t.Fatal("bot did not attack while holding its objective")
	}
}

func TestPracticeDodgeAvoidsRealProjectileAndReturnsToPoint(t *testing.T) {
	for _, mode := range []string{"idle", "dodge", "capture+dodge", "capture+attack+dodge"} {
		t.Run(mode, func(t *testing.T) {
			w := practiceWorld(t, mode)
			owner, bot := w.Seats["human"], w.Seats["npc"]
			w.PointIndex = 1
			owner.State.X, owner.State.Z = -7, 5
			bot.State.X, bot.State.Z, bot.State.ProtectedUntilTick = 0, 5, 0
			if !w.Input("human", owner.Generation, InputIntent{Generation: owner.Generation, Seq: 1, Action: "fire", ActionID: 1, ActionTick: w.Tick, AimX: 1}) {
				t.Fatal("shot rejected")
			}
			moved := false
			for range 45 {
				w.Step()
				moved = moved || math.Abs(bot.State.Z-5) > .3
				if !shared.CircleClear(w.Map, bot.State.X, bot.State.Z, w.Rules.PlayerRadius) {
					t.Fatal("dodge crossed geometry")
				}
			}
			if mode == "idle" {
				if bot.State.HP != 75 || moved {
					t.Fatalf("control shot did not hit idle bot: %+v", bot.State)
				}
			} else if bot.State.HP != 100 || !moved {
				t.Fatalf("dodge did not evade the damaging shot: %+v", bot.State)
			}
			if mode == "capture+dodge" || mode == "capture+attack+dodge" {
				for range 90 {
					w.Step()
				}
				if !w.insidePoint(bot) || w.ScoreTicks[bot.State.TeamID] == 0 {
					t.Fatal("evasion abandoned capture goal")
				}
			}
		})
	}
}

func TestPracticeDodgeIgnoresSafeShotsAndEvadesGroundTelegraph(t *testing.T) {
	w := practiceWorld(t, "dodge")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -7, -7
	bot.State.X, bot.State.Z, bot.State.ProtectedUntilTick = 0, 5, 0
	w.Projectiles = []shot{{State: ProjectileState{OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID, X: 3, Z: 5, DX: 1, ExpiresTick: 30}, speed: 12, damage: 25},
		{State: ProjectileState{OwnerID: bot.State.PlayerID, TeamID: bot.State.TeamID, X: -3, Z: 5, DX: 1, ExpiresTick: 30}, speed: 12, damage: 25}}
	for range 12 {
		w.Step()
	}
	if bot.State.X != 0 || bot.State.Z != 5 {
		t.Fatal("bot dodged a friendly or receding shot")
	}
	w.GroundCasts = []groundCast{{ID: "warning", OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID, X: 0, Z: 5, Radius: 1.6, Damage: 35, ImpactTick: w.Tick + 18}}
	for range 22 {
		w.Step()
	}
	if bot.State.HP != 100 || math.Hypot(bot.State.X, bot.State.Z-5) <= 2 {
		t.Fatalf("bot ignored a visible ground-impact warning: %+v", bot.State)
	}
}

func TestPracticeCombinedModesRetainCollisionAndCooldowns(t *testing.T) {
	for _, mode := range []string{"move+attack", "move+dodge", "move+attack+dodge", "capture+attack+dodge", "attack+dodge"} {
		t.Run(mode, func(t *testing.T) {
			w := practiceWorld(t, mode)
			bot := w.Seats["npc"]
			lastFire := uint64(0)
			for range 180 {
				w.Step()
				if !shared.CircleClear(w.Map, bot.State.X, bot.State.Z, w.Rules.PlayerRadius) {
					t.Fatal("combined mode crossed cover")
				}
				if bot.State.FireReadyTick != lastFire {
					if lastFire != 0 && bot.State.FireReadyTick-lastFire < w.seatRules(bot).FireCooldownTicks {
						t.Fatal("combined mode bypassed fire cooldown")
					}
					lastFire = bot.State.FireReadyTick
				}
			}
			if err := w.setPracticeMode("human", "idle"); err != nil {
				t.Fatal(err)
			}
			if bot.Pending != nil || bot.DX != 0 || bot.DZ != 0 || bot.State.FireReadyTick != lastFire {
				t.Fatal("switching off behaviors retained intents or reset cooldown")
			}
		})
	}
}

func TestPracticeDodgeDoesNotInterruptSafeMovementWithAttackStop(t *testing.T) {
	w := practiceWorld(t, "move+attack+dodge")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = -5, 2
	bot.State.X, bot.State.Z, bot.State.ProtectedUntilTick = 0, 2, 0
	w.Projectiles = []shot{{State: ProjectileState{OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID, X: -2, Z: 2, DX: 1, ExpiresTick: 21}, speed: 12, damage: 25}}
	for range 15 {
		w.Step()
	}
	if bot.State.HP != 100 || bot.State.Z < 2.5 {
		t.Fatalf("bot stopped a safe patrol to attack and was hit: %+v", bot.State)
	}
}
