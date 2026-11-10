package sim

import (
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"math"
	"testing"
)

func basicFixture(t *testing.T) (*World, *Seat, *Seat) {
	w := combatWorld(t, map[string]string{"a": "blue", "b": "red"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z, b.State.X, b.State.Z = 0, 0, 2, 0
	a.State.ProtectedUntilTick, b.State.ProtectedUntilTick = 0, 0
	return w, a, b
}
func TestBasicWindupCommitAndMovementDoesNotResetPeriod(t *testing.T) {
	w, a, b := basicFixture(t)
	hp := b.State.HP
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, X: 1, Action: "attack", ActionID: 1, ActionTick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life}) {
		t.Fatal("input")
	}
	w.Step()
	release, ready := a.State.AttackReleaseTick, a.State.BasicReadyTick
	if a.ActionCode != "attack_started" || release <= w.Tick || b.State.HP != hp || a.State.X != 0 {
		t.Fatal("attack must wind up without damage or movement")
	}
	for w.Tick < release {
		a.LastInput = w.Tick
		w.Step()
	}
	if b.State.HP != hp-a.hero.BasicDamage || a.State.X <= 0 || a.State.AttackReleaseTick != 0 || a.State.BasicReadyTick != ready {
		t.Fatal("release must hit once and resume movement without resetting cooldown")
	}
	if w.resolveAction(a, actionIntent{Kind: "cancel", Tick: w.Tick}) != "cancelled" || a.State.BasicReadyTick != ready {
		t.Fatal("backswing cancellation reset attack period")
	}
	if w.resolveAction(a, actionIntent{Kind: "attack", Tick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life}) != "cooldown" {
		t.Fatal("period bypass")
	}
	if code := w.resolveAction(a, actionIntent{Kind: "skill", Tick: w.Tick, AimZ: 1}); code != "fired" || a.State.BasicReadyTick != ready {
		t.Fatal("independent skill", code)
	}
}
func TestBasicCancelAndTargetValidity(t *testing.T) {
	for _, kind := range []string{"cancel", "dash", "death", "respawn", "offline", "range", "ally"} {
		t.Run(kind, func(t *testing.T) {
			w, a, b := basicFixture(t)
			hp := b.State.HP
			if code := w.resolveAction(a, actionIntent{Kind: "attack", ID: 3, Tick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life}); code != "attack_started" {
				t.Fatal(code)
			}
			release := a.State.AttackReleaseTick
			switch kind {
			case "cancel":
				w.resolveAction(a, actionIntent{Kind: "cancel", Tick: w.Tick})
			case "dash":
				if code := w.resolveAction(a, actionIntent{Kind: "dash", Tick: w.Tick, AimZ: 1}); code != "dashed" {
					t.Fatal(code)
				}
			case "death":
				a.State.HP = 0
			case "respawn":
				b.Life++
			case "offline":
				w.Disconnect("a", a.Generation)
			case "range":
				b.State.X = 9
			case "ally":
				b.State.TeamID = a.State.TeamID
			}
			for i := uint64(0); i <= release+1; i++ {
				w.Step()
			}
			if b.State.HP != hp || a.State.AttackReleaseTick != 0 {
				t.Fatal("cancelled or invalid target attack dealt damage")
			}
		})
	}
}
func TestBasicRejectsInvalidTargetAndSnapshotCarriesWindup(t *testing.T) {
	w, a, b := basicFixture(t)
	if w.resolveAction(a, actionIntent{Kind: "attack", Tick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life + 1}) != "invalid_target" {
		t.Fatal("old life accepted")
	}
	w.resolveAction(a, actionIntent{Kind: "attack", ID: 7, Tick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life})
	snap := w.Snapshot(a, "snapshot")
	for _, p := range snap.Players {
		if p.PlayerID == a.State.PlayerID && (p.BasicActionID != 7 || p.AttackTargetID != b.State.PlayerID || p.AttackReleaseTick == 0 || p.BasicReadyTick == 0) {
			t.Fatal("snapshot lost attack state")
		}
	}
}

func TestRejectedSkillAndDashDoNotCancelBasic(t *testing.T) {
	w, a, b := basicFixture(t)
	w.resolveAction(a, actionIntent{Kind: "attack", ID: 1, Tick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life})
	release := a.State.AttackReleaseTick
	a.State.FireReadyTick = w.Tick + 20
	if code := w.resolveAction(a, actionIntent{Kind: "skill", Tick: w.Tick, AimX: 1}); code != "cooldown" || a.State.AttackReleaseTick != release {
		t.Fatal("unavailable skill cancelled attack", code)
	}
	if code := w.resolveAction(a, actionIntent{Kind: "dash", Tick: w.Tick}); code != "invalid_aim" || a.State.AttackReleaseTick != release {
		t.Fatal("invalid dash cancelled attack", code)
	}
}

func TestLargeMapWalkSpeedAppliesToAuthorityAndAllHeroes(t *testing.T) {
	m := shared.LargeArenaMap()
	w, err := newMatchWorldWithMap("slow-walk", map[string]string{"a": "blue", "b": "red"}, map[string]string{"a": "ranger", "b": "tank"}, m)
	if err != nil {
		t.Fatal(err)
	}
	for _, id := range []string{"a", "b"} {
		s, _ := w.Attach(id, false)
		w.MarkLoaded(id, s.Generation)
	}
	a := w.Seats["a"]
	start := a.State.X
	for i := uint64(1); i <= 30; i++ {
		w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: i, X: 1})
		w.Step()
	}
	if math.Abs(a.State.X-start-4.8) > 1e-8 {
		t.Fatal("authority did not walk 4.8 units in one second", a.State.X-start)
	}
	rules := shared.RulesForMap(m)
	for _, hero := range rules.Heroes {
		base, _ := shared.Rules().Hero(hero.HeroID)
		if math.Abs(hero.Speed-base.Speed*.8) > 1e-10 || hero.DashSpeed != base.DashSpeed {
			t.Fatal("walk/dash scaling", hero)
		}
	}
}
