package sim

import (
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

func TestStationaryFacingReachesObserverSnapshotWithoutAction(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, observer := w.Seats["a"], w.Seats["b"]
	<-observer.Updates // Consume the reliable start message before regular updates.
	x, z := a.State.X, a.State.Z
	wire, err := protocol.Decode([]byte(`{"type":"input","generation":1,"seq":1,"facingX":0.3,"facingZ":0.4}`))
	if err != nil || !w.Input("a", a.Generation, inputIntent(wire)) {
		t.Fatalf("stationary facing input rejected: %v", err)
	}
	for range w.Rules.TickRate / w.Rules.SnapshotRate {
		w.Step()
	}
	select {
	case snapshot := <-observer.Updates:
		found := false
		for _, p := range snapshot.Players {
			if p.PlayerID != a.State.PlayerID {
				continue
			}
			found = true
			if math.Abs(p.FacingX-.6) > 1e-12 || math.Abs(p.FacingZ-.8) > 1e-12 || p.X != x || p.Z != z {
				t.Fatalf("observer did not receive normalized stationary facing: %+v", p)
			}
		}
		if !found || snapshot.PlayerID != observer.State.PlayerID {
			t.Fatal("facing was not published to the independent observer")
		}
	default:
		t.Fatal("observer received no periodic snapshot")
	}
	if a.ActionAck != 0 || a.Pending != nil || len(w.Projectiles) != 0 {
		t.Fatal("turning in place created a combat action")
	}
}

func TestFacingRetainsDirectionAcrossZeroInputAndReconnectRejectsStaleInput(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, FacingZ: 1}) {
		t.Fatal("initial facing input rejected")
	}
	for _, input := range []InputIntent{
		{Generation: a.Generation, Seq: 2},
		{Generation: a.Generation, Seq: 3, FacingX: 1e-9},
	} {
		if !w.Input("a", a.Generation, input) || a.State.FacingX != 0 || a.State.FacingZ != 1 {
			t.Fatal("zero or negligible facing erased the last direction")
		}
	}
	previousGeneration := a.Generation
	w.Disconnect("a", previousGeneration)
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	for _, p := range w.Snapshot(a, "welcome").Players {
		if p.PlayerID == a.State.PlayerID && (p.FacingX != 0 || p.FacingZ != 1) {
			t.Fatal("reconnect discarded the stored facing")
		}
	}
	for _, stale := range []struct {
		generation uint32
		input      InputIntent
	}{
		{a.Generation, InputIntent{Generation: a.Generation, Seq: 3, FacingX: -1}},
		{previousGeneration, InputIntent{Generation: previousGeneration, Seq: 4, FacingX: -1}},
		{a.Generation, InputIntent{Generation: previousGeneration, Seq: 4, FacingX: -1}},
	} {
		if w.Input("a", stale.generation, stale.input) || a.State.FacingX != 0 || a.State.FacingZ != 1 {
			t.Fatal("stale sequence or connection overwrote the facing")
		}
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 4, FacingX: -1}) || a.State.FacingX != -1 || a.State.FacingZ != 0 {
		t.Fatal("current connection could not update the facing")
	}
}

func TestFacingValidationAndInactivePlayers(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.FacingX = 1
	for _, value := range []float64{math.NaN(), math.Inf(1), math.Inf(-1), 1.01, -1.01} {
		for axis := range 2 {
			input := InputIntent{Generation: a.Generation, Seq: 1}
			if axis == 0 {
				input.FacingX = value
			} else {
				input.FacingZ = value
			}
			if w.Input("a", a.Generation, input) || a.State.Ack != 0 || a.State.FacingX != 1 || a.State.FacingZ != 0 {
				t.Fatalf("invalid facing mutated input state: %+v", input)
			}
		}
	}
	for _, left := range []bool{false, true} {
		a.State.HP, a.State.Left = 0, left
		if left {
			a.State.HP = w.Rules.MaxHP
		}
		if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: a.State.Ack + 1, FacingZ: 1}) || a.State.FacingX != 1 || a.State.FacingZ != 0 {
			t.Fatal("inactive player changed facing")
		}
	}
	lab := New()
	s, err := lab.Attach("lab", false)
	if err != nil {
		t.Fatal(err)
	}
	if !lab.Input("lab", s.Generation, InputIntent{Generation: s.Generation, Seq: 1, FacingZ: 1}) || s.State.FacingX != 0 || s.State.FacingZ != 0 {
		t.Fatal("private-match facing leaked into the transport laboratory")
	}
}

func TestFacingDoesNotChangeMovementOrActionDirection(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.Z = -4 // A clear lane through the production map.
	x, z := a.State.X, a.State.Z
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, X: 1, FacingZ: 1, Action: "dash", ActionID: 1, ActionTick: w.Tick, AimX: 1}) {
		t.Fatal("dash with independent facing rejected")
	}
	w.Step()
	if a.ActionCode != "dashed" || a.State.X <= x || a.State.Z != z || a.DashDX != 1 || a.DashDZ != 0 || a.State.FacingX != 0 || a.State.FacingZ != 1 {
		t.Fatalf("facing changed sideways dash: %+v", a)
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 2, FacingZ: -1, Action: "fire", ActionID: 2, ActionTick: w.Tick, AimX: -1}) {
		t.Fatal("fire with independent facing rejected")
	}
	w.Step()
	if a.ActionCode != "fired" || len(w.Projectiles) != 1 || w.Projectiles[0].State.DX != -1 || w.Projectiles[0].State.DZ != 0 || a.State.FacingX != 0 || a.State.FacingZ != -1 {
		t.Fatal("continuous facing overwrote captured action aim")
	}
}

func TestPracticeBotPublishesAttackAndPatrolFacing(t *testing.T) {
	for _, mode := range []string{"move", "attack"} {
		t.Run(mode, func(t *testing.T) {
			w := practiceWorld(t, mode)
			bot, owner := w.Seats["npc"], w.Seats["human"]
			wantX, wantZ := owner.State.X-bot.State.X, owner.State.Z-bot.State.Z
			w.advancePracticeBot()
			if mode == "move" {
				wantX, wantZ = bot.DX, bot.DZ
			}
			length := math.Hypot(wantX, wantZ)
			if length < 1e-6 || math.Abs(bot.State.FacingX-wantX/length) > 1e-12 || math.Abs(bot.State.FacingZ-wantZ/length) > 1e-12 {
				t.Fatalf("bot %s facing did not follow intent: %+v", mode, bot.State)
			}
			for _, p := range w.Snapshot(owner, "snapshot").Players {
				if p.Bot && (p.FacingX != bot.State.FacingX || p.FacingZ != bot.State.FacingZ) {
					t.Fatal("bot facing missing from the public snapshot")
				}
			}
		})
	}
}
