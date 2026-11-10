package sim

import (
	"encoding/json"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"testing"
)

func netFixtureOptions() *protocol.NetOptions {
	return &protocol.NetOptions{Codec: "json", Interest: "all", Radius: 4, History: true, Lag: "melee"}
}
func TestMeleeHistoricalTargetsBoundedAndLifeSafe(t *testing.T) {
	if historyFactory == nil || rewindFactory == nil {
		t.Skip("optional history/rewind removed")
	}
	for _, scenario := range []string{"rewind", "off", "too-old", "future", "life", "generation", "protected"} {
		t.Run(scenario, func(t *testing.T) {
			w := meleeWorld(t)
			a, b := w.Seats["a"], w.Seats["b"]
			w.Tick = 100
			w.history = historyFactory()
			a.Net = netFixtureOptions()
			a.State.X, a.State.Z = 0, -4
			b.State.X, b.State.Z = 1, -4
			w.Tick = 94
			w.recordHistory()
			w.Tick = 100
			b.State.X = 3
			input := actionIntent{Kind: "fire", ID: 7, Tick: 100, ViewTick: 94, ObservedTick: 98, AimX: 1}
			switch scenario {
			case "off":
				a.Net.Lag = "off"
			case "too-old":
				input.ViewTick = 93
			case "future":
				input.ViewTick = 101
				input.ObservedTick = 101
			case "life":
				b.Life++
			case "generation":
				b.Generation++
			case "protected":
				b.State.ProtectedUntilTick = 101
			}
			hp := b.State.HP
			code := w.resolveAction(a, input)
			if code != "melee" {
				t.Fatal(code)
			}
			hit := b.State.HP < hp
			if hit != (scenario == "rewind") {
				t.Fatalf("hit=%v hp=%d rewind=%d", hit, b.State.HP, a.RewindTick)
			}
			if b.State.X != 3 || a.State.X != 0 {
				t.Fatal("history query mutated live collision positions")
			}
		})
	}
}
func TestNetProjectionInterestQuantizationAndHistoryIsolation(t *testing.T) {
	if interestFactory == nil || historyFactory == nil || quantizePosition == nil {
		t.Skip("optional modules removed")
	}
	w := meleeWorld(t)
	a, b := w.Seats["a"], w.Seats["b"]
	a.Net = netFixtureOptions()
	a.Net.Lag = "off"
	a.Net.Interest = "radius"
	a.Net.Quantize = true
	a.Interest = interestFactory(4)
	a.State.X, a.State.Z = 0.123456, -4
	b.State.X, b.State.Z = 8, -4
	w.history = historyFactory()
	w.recordHistory()
	before, _ := json.Marshal(a.State)
	view := w.Snapshot(a, "welcome")
	if len(view.Players) != 1 || view.Players[0].PlayerID != a.State.PlayerID || view.Tech.Visible != 1 || view.Tech.Total != 2 || view.Tech.HistorySamples != 1 {
		t.Fatalf("filtered state %+v", view)
	}
	if view.Players[0].X != .123 {
		t.Fatal("quantization did not apply")
	}
	after, _ := json.Marshal(a.State)
	if string(before) != string(after) {
		t.Fatal("wire projection mutated state")
	}
	b.State.X = 2
	if len(w.Snapshot(a, "snapshot").Players) != 2 {
		t.Fatal("reentry")
	}
	b.State.X = 5
	if len(w.Snapshot(a, "snapshot").Players) != 2 {
		t.Fatal("hysteresis")
	}
	b.State.X = 7
	if len(w.Snapshot(a, "snapshot").Players) != 1 {
		t.Fatal("exit")
	}
	for i := 0; i < 50; i++ {
		w.Tick++
		w.recordHistory()
	}
	if w.history.Len() != 32 {
		t.Fatal("history capacity")
	}
	q, _ := w.history.At(w.Tick)
	pose := q.Before[a.State.PlayerID]
	pose.X = 99
	q.Before[a.State.PlayerID] = pose
	next, _ := w.history.At(w.Tick)
	if next.Before[a.State.PlayerID].X == 99 {
		t.Fatal("query shared domain history")
	}
}
func TestNetOptionsRejectIncompatibleAndUnavailable(t *testing.T) {
	bad := netFixtureOptions()
	bad.History = false
	if ValidateNetOptions(bad) == nil {
		t.Fatal("compensation without history")
	}
	bad = netFixtureOptions()
	bad.Radius = 1
	if ValidateNetOptions(bad) == nil {
		t.Fatal("unsafe interest radius")
	}
}
