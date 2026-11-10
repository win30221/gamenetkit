package sim

import (
	"encoding/json"
	"math"
	"strings"
	"testing"
)

func TestAttackStopFreezesEachHeroAndResumesHeldMovement(t *testing.T) {
	for _, heroID := range []string{"tank", "ranger", "mage"} {
		for _, mapped := range []bool{false, true} {
			name := heroID + "/empty"
			if mapped {
				name = heroID + "/mapped"
			}
			t.Run(name, func(t *testing.T) {
				w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
				if !mapped {
					w.Map = nil
				}
				w.Rules.InputTimeoutTicks = 1000
				a, b := w.Seats["a"], w.Seats["b"]
				h, _ := w.Rules.Hero(heroID)
				a.hero, a.State.HeroID = &h, heroID
				a.State.X, a.State.Z, b.State.X, b.State.Z = 0, -4, 8, 4
				intent := InputIntent{Generation: a.Generation, Seq: 1, X: 1, Action: "fire", ActionID: 1,
					ActionTick: w.Tick, AimX: 1}
				if heroID == "mage" {
					intent.HasGroundTarget, intent.TargetX, intent.TargetZ = true, 3, -4
				}
				if !w.Input("a", a.Generation, intent) {
					t.Fatal("attack input rejected")
				}
				w.Step()
				until := w.Tick + h.AttackStopTicks
				if h.AttackStopTicks == 0 || a.State.AttackStopUntilTick != until || a.State.X != 0 {
					t.Fatalf("accepted attack did not stop its first movement tick: %+v", a.State)
				}
				for w.Tick+1 < until {
					w.Step()
					if a.State.X != 0 || a.State.Z != -4 {
						t.Fatal("held movement bypassed attack stop")
					}
				}
				w.Step()
				if want := h.Speed / float64(w.Rules.TickRate); math.Abs(a.State.X-want) > 1e-9 || a.DX != 1 {
					t.Fatalf("movement did not resume exactly at recovery boundary: x=%v tick=%d until=%d", a.State.X, w.Tick, until)
				}
			})
		}
	}
}

func TestAttackStopCancelsDashAndRejectsBypassWithoutExtendingRecovery(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.X, a.State.Z = 0, -4
	combatAction(t, w, "a", 1, 1, "dash", 1, 0)
	if a.DashEndTick <= w.Tick {
		t.Fatal("fixture dash did not start")
	}
	combatAction(t, w, "a", 2, 2, "fire", 1, 0)
	until, dashReady := a.State.AttackStopUntilTick, a.State.DashReadyTick
	if a.ActionCode != "fired" || a.DashEndTick != 0 || until <= w.Tick {
		t.Fatal("accepted attack did not cancel its active dash")
	}
	if code := w.resolveAction(a, actionIntent{Kind: "dash", Tick: w.Tick, AimX: 1}); code != "attack_locked" || a.State.DashReadyTick != dashReady || a.State.AttackStopUntilTick != until {
		t.Fatal("dash bypassed recovery or consumed cooldown")
	}
	w.Step()
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, AimX: 1}); code != "cooldown" || a.State.AttackStopUntilTick != until {
		t.Fatal("cooldown retry extended attack stop")
	}
	w, a, _ = mageWorld(t)
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true, TargetX: 8}); code != "out_of_range" || a.State.AttackStopUntilTick != 0 {
		t.Fatal("rejected ground target created an attack stop")
	}
}

func TestAttackStopSurvivesReconnectAndRemainsPushable(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t1", "c": "t2"})
	w.Rules.InputTimeoutTicks = 1000
	a, b, c := w.Seats["a"], w.Seats["b"], w.Seats["c"]
	a.State.X, a.State.Z, b.State.X, b.State.Z, c.State.X, c.State.Z = 0, 3, -.8, 3, 8, 4
	combatAction(t, w, "a", 1, 1, "fire", 0, 1)
	until := a.State.AttackStopUntilTick
	w.Disconnect("a", a.Generation)
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	snapshot := w.Snapshot(a, "welcome")
	if snapshot.Players[0].AttackStopUntilTick != until {
		t.Fatal("reconnect lost the authoritative recovery boundary")
	}
	wire, err := json.Marshal(snapshot)
	if err != nil || !strings.Contains(string(wire), "\"attackStopUntilTick\"") {
		t.Fatal("snapshot omitted attack recovery")
	}
	a.DX, b.DX = -1, 1
	w.Step()
	if a.State.X <= 0 || math.Hypot(a.State.X-b.State.X, a.State.Z-b.State.Z) < 2*w.Rules.PlayerRadius-1e-5 {
		t.Fatal("attack stop made the body immovable or nonsolid")
	}
	a.State.HP, a.State.RespawnTick, a.State.AttackStopUntilTick = 0, w.Tick+1, w.Tick+1000
	w.Step()
	if a.State.HP == 0 || a.State.AttackStopUntilTick != 0 {
		t.Fatal("respawn retained an attack stop")
	}
}
