package sim

import (
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

func takeActionUpdate(t *testing.T, seat *Seat) protocol.Server {
	t.Helper()
	select {
	case message := <-seat.Updates:
		return message
	default:
		t.Fatalf("missing update for %s", seat.UserID)
		return protocol.Server{}
	}
}

func noActionUpdate(t *testing.T, seat *Seat) {
	t.Helper()
	select {
	case message := <-seat.Updates:
		t.Fatalf("unexpected update for %s: %s tick=%d ack=%d", seat.UserID, message.Type, message.Tick, message.ActionAck)
	default:
	}
}

func TestActionResultsPublishOnNonSnapshotTickOnlyToOwner(t *testing.T) {
	for _, test := range []struct{ kind, code string }{{"cancel", "cancelled"}, {"fire", "invalid_aim"}} {
		t.Run(test.code, func(t *testing.T) {
			w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
			a, b := w.Seats["a"], w.Seats["b"]
			takeActionUpdate(t, a)
			takeActionUpdate(t, b)
			if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, Action: test.kind, ActionID: 1}) {
				t.Fatal("action input rejected before resolution")
			}
			w.Step() // Tick 1: the next ordinary snapshot would be tick 2.
			message := takeActionUpdate(t, a)
			if message.Type != "snapshot" || message.Tick != 1 || message.ActionAck != 1 || message.ActionCode != test.code || len(message.Players) != 2 {
				t.Fatalf("incomplete immediate action result: %+v", message)
			}
			noActionUpdate(t, a)
			noActionUpdate(t, b)
			w.Step()
			if takeActionUpdate(t, a).Tick != 2 || takeActionUpdate(t, b).Tick != 2 {
				t.Fatal("action result changed the ordinary snapshot cadence")
			}
			w.Step()
			noActionUpdate(t, a)
			noActionUpdate(t, b)
		})
	}
}

func TestActionResultOnRegularTickDoesNotDuplicateSnapshot(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	takeActionUpdate(t, a)
	takeActionUpdate(t, b)
	w.Step()
	noActionUpdate(t, a)
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, Action: "cancel", ActionID: 1, ActionTick: w.Tick}) {
		t.Fatal("action input rejected")
	}
	w.Step()
	if message := takeActionUpdate(t, a); message.Tick != 2 || message.ActionAck != 1 || message.ActionCode != "cancelled" {
		t.Fatalf("wrong regular-tick result: %+v", message)
	}
	noActionUpdate(t, a)
	if takeActionUpdate(t, b).Tick != 2 {
		t.Fatal("ordinary peer snapshot disappeared")
	}
}

func TestActionResultPreservesQueuedStartAndRetriesPublication(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, Action: "cancel", ActionID: 1}) {
		t.Fatal("action input rejected")
	}
	w.Step()
	w.Step() // Leave start unread across immediate and ordinary publication.
	if message := takeActionUpdate(t, a); message.Type != "start" || message.Tick != 0 {
		t.Fatalf("action acknowledgement replaced loading transition: %+v", message)
	}
	w.Step() // Tick 3 must retry the still-unpublished result.
	if message := takeActionUpdate(t, a); message.Tick != 3 || message.ActionAck != 1 {
		t.Fatalf("blocked action result was considered published: %+v", message)
	}
	if message := takeActionUpdate(t, b); message.Type != "start" {
		t.Fatal("owner acknowledgement changed another player's queued transition")
	}
}

func TestDroppedActionResultRecoversWithoutRepeatingActionOrReconnectAck(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	takeActionUpdate(t, a)
	takeActionUpdate(t, b)
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, Action: "fire", ActionID: 1, AimX: 1}) {
		t.Fatal("initial action input rejected")
	}
	w.Step()
	takeActionUpdate(t, a) // The transport drops this immediate result.
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 2, Action: "fire", ActionID: 1, ActionTick: w.Tick, AimX: 1}) {
		t.Fatal("duplicate action envelope rejected")
	}
	w.Step()
	if message := takeActionUpdate(t, a); message.Tick != 2 || message.ActionAck != 1 || message.ActionCode != "fired" || w.NextProjectileID != 1 {
		t.Fatalf("ordinary snapshot did not recover the one-use result: %+v shots=%d", message, w.NextProjectileID)
	}
	takeActionUpdate(t, b)
	w.Disconnect("a", a.Generation)
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	if welcome := w.Snapshot(a, "welcome"); welcome.ActionAck != 1 || welcome.ActionCode != "fired" {
		t.Fatal("reconnect lost the already-resolved action")
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 3, Action: "fire", ActionID: 1, ActionTick: w.Tick, AimX: 1}) {
		t.Fatal("reconnected duplicate envelope rejected")
	}
	w.Step()
	noActionUpdate(t, a)
	if w.NextProjectileID != 1 {
		t.Fatal("reconnect replayed an action already acknowledged by welcome")
	}
	w.Step()
	if message := takeActionUpdate(t, a); message.ActionAck != 1 || message.ActionCode != "fired" {
		t.Fatal("persistent action result disappeared after reconnect")
	}
}
