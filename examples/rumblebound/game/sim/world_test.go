package sim

import (
	"encoding/json"
	"fmt"
	"math"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestSharedMovementCases(t *testing.T) {
	var fixtures struct {
		Cases []struct {
			Name                               string
			X, Z, DX, DZ, ExpectedX, ExpectedZ float64
			Steps                              int
		}
	}
	if err := json.Unmarshal(shared.MovementCases, &fixtures); err != nil {
		t.Fatal(err)
	}
	for _, c := range fixtures.Cases {
		t.Run(c.Name, func(t *testing.T) {
			x, z := c.X, c.Z
			for range c.Steps {
				x, z = Move(x, z, c.DX, c.DZ, shared.Rules())
			}
			if math.Abs(x-c.ExpectedX) > 1e-9 || math.Abs(z-c.ExpectedZ) > 1e-9 {
				t.Fatalf("got %.12f %.12f", x, z)
			}
		})
	}
}

func TestMovementAuthorityAndTimeout(t *testing.T) {
	w := New()
	s, _ := w.Attach("a", false)
	c := InputIntent{Generation: s.Generation, Seq: 1, X: 1, Z: 1}
	if !w.Input("a", 1, c) {
		t.Fatal("input rejected")
	}
	w.Step()
	if math.Abs(math.Hypot(s.State.X, s.State.Z)-0.2) > 1e-9 {
		t.Fatal("diagonal speed differs")
	}
	for range 30 {
		w.Step()
	}
	x := s.State.X
	w.Step()
	if x != s.State.X {
		t.Fatal("stale input continued moving")
	}
	c.Seq = 1
	c.X = -1
	if w.Input("a", 1, c) {
		t.Fatal("duplicate input accepted")
	}
}

func TestSnapshotInputTimelineAndReconnect(t *testing.T) {
	w := New()
	s, _ := w.Attach("a", false)
	w.Step()
	w.Input("a", s.Generation, InputIntent{Generation: s.Generation, Seq: 7, X: 1, Echo: 1234})
	w.Step()
	w.Step()
	state := w.Snapshot(s, "snapshot")
	if state.InputTick != 1 || state.Tick != 3 || state.Echo != 1234 || s.State.Ack != 7 || math.Abs(s.State.X-.4) > 1e-9 {
		t.Fatalf("held input timeline mismatch: %+v", state)
	}
	w.Disconnect("a", s.Generation)
	s, _ = w.Attach("a", false)
	state = w.Snapshot(s, "welcome")
	if state.Echo != 0 || state.InputTick != state.Tick || s.State.Ack != 7 {
		t.Fatal("reconnect leaked previous connection input timing")
	}
}
func TestReconnectKeepsEntityAndRejectsOldGeneration(t *testing.T) {
	w := New()
	s, _ := w.Attach("a", false)
	id, entity := s.State.PlayerID, s.State.EntityID
	s.State.X = 4.5
	old := s.Updates
	w.Disconnect("a", 1)
	for range 150 {
		w.Step()
	}
	s, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	if s.State.PlayerID != id || s.State.EntityID != entity || s.State.X != 4.5 || len(w.Seats) != 1 {
		t.Fatal("reconnect replaced player")
	}
	if _, ok := <-old; ok {
		t.Fatal("old output open")
	}
	if w.Input("a", 1, InputIntent{Generation: 1, Seq: 3, X: 1}) {
		t.Fatal("old input accepted")
	}
	w.Disconnect("a", 1)
	if !s.State.Online {
		t.Fatal("old disconnect killed new connection")
	}
	if _, err = w.Attach("a", false); err == nil {
		t.Fatal("silent takeover")
	}
	if _, err = w.Attach("a", true); err != nil {
		t.Fatal(err)
	}
}
func TestReconnectDeadlineNotExtended(t *testing.T) {
	w := New()
	now := time.Now()
	w.Now = func() time.Time { return now }
	_, _ = w.Attach("a", false)
	w.Disconnect("a", 1)
	now = now.Add(29 * time.Second)
	w.Disconnect("a", 1)
	now = now.Add(time.Second)
	w.Step()
	if _, err := w.Attach("a", true); err == nil {
		t.Fatal("expired seat resurrected")
	}
}
func TestCapacityAndSnapshotBudget(t *testing.T) {
	w := New()
	var s *Seat
	for i := range 8 {
		s, _ = w.Attach(fmt.Sprint(i), false)
		s.State.X = 9.599999999999999
		s.State.Z = -9.599999999999999
		s.State.Ack = 9007199254740991
	}
	if _, err := w.Attach("extra", false); err == nil {
		t.Fatal("capacity exceeded")
	}
	b, _ := json.Marshal(w.Snapshot(s, "snapshot"))
	if len(b) > protocol.MaxDatagram {
		t.Fatalf("snapshot %d > %d", len(b), protocol.MaxDatagram)
	}
}
func TestBoundaryAndLatestSnapshotOnly(t *testing.T) {
	w := New()
	s, _ := w.Attach("a", false)
	s.State.X = 9.59
	w.Input("a", 1, InputIntent{Generation: 1, Seq: 1, X: 1})
	for range 100 {
		w.Step()
	}
	if s.State.X != 9.6 {
		t.Fatal(s.State.X)
	}
	if len(s.Updates) != 1 {
		t.Fatal("unbounded snapshots")
	}
	if (<-s.Updates).Tick != 100 {
		t.Fatal("stale snapshot")
	}
}
