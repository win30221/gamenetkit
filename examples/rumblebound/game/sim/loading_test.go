package sim

import (
	"context"
	"testing"
	"time"
)

func TestPrivateMatchWaitsForEveryLoadedSeatBeforeTickZeroStart(t *testing.T) {
	w, err := newMatchWorld("m-loading-1v2", map[string]string{"a": "t1", "b": "t2", "c": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	a, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	if w.MarkLoaded("a", a.Generation) || w.MarkLoaded("a", a.Generation) {
		t.Fatal("single 1V2 participant started the match")
	}
	if w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, X: 1}) {
		t.Fatal("movement input accepted during loading")
	}
	for range w.Rules.ReconnectTicks + 1 {
		w.Step()
	}
	if w.Tick != 0 || w.Result != nil || w.Seats["b"].State.Left || w.Phase != phaseLoading {
		t.Fatalf("loading advanced the game: %+v", w.Snapshot(a, "welcome"))
	}
	b, err := w.Attach("b", false)
	if err != nil {
		t.Fatal(err)
	}
	if w.MarkLoaded("b", b.Generation) {
		t.Fatal("two of three seats started the match")
	}
	c, err := w.Attach("c", false)
	if err != nil {
		t.Fatal(err)
	}
	if !w.MarkLoaded("c", c.Generation) || w.Phase != phasePlaying || w.Tick != 0 {
		t.Fatalf("complete roster did not start at tick zero: %+v", w.Snapshot(a, "snapshot"))
	}
	for _, seat := range []*Seat{a, b, c} {
		select {
		case started := <-seat.Updates:
			if started.Type != "start" || started.Tick != 0 || started.Match == nil || started.Match.Phase != phasePlaying {
				t.Fatalf("wrong tick-zero start for %s: %+v", seat.UserID, started)
			}
		default:
			t.Fatalf("seat %s missed start", seat.UserID)
		}
	}
	w.Step()
	if w.Tick != 1 {
		t.Fatal("playing did not advance after start")
	}
}

func TestQueuedStartSurvivesSnapshotBackpressure(t *testing.T) {
	w, err := newMatchWorld("m-loading-backpressure", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	a, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	b, err := w.Attach("b", false)
	if err != nil {
		t.Fatal(err)
	}
	w.MarkLoaded("a", a.Generation)
	if !w.MarkLoaded("b", b.Generation) {
		t.Fatal("match did not start")
	}
	// Leave both writer queues unread across several snapshot intervals.
	for range 3 * w.Rules.TickRate / w.Rules.SnapshotRate {
		w.Step()
	}
	for _, seat := range []*Seat{a, b} {
		select {
		case message := <-seat.Updates:
			if message.Type != "start" || message.Tick != 0 {
				t.Fatalf("snapshot replaced queued start for %s: %+v", seat.UserID, message)
			}
		default:
			t.Fatalf("start disappeared for %s", seat.UserID)
		}
	}
	w.Step()
	for range w.Rules.TickRate / w.Rules.SnapshotRate {
		w.Step()
	}
	for _, seat := range []*Seat{a, b} {
		select {
		case message := <-seat.Updates:
			if message.Type != "snapshot" || message.Tick <= 0 {
				t.Fatalf("snapshots did not resume after start for %s: %+v", seat.UserID, message)
			}
		default:
			t.Fatalf("no snapshot after start for %s", seat.UserID)
		}
	}
}

func TestLoadingAckRejectsOldGenerationAndRequiresReconnectAck(t *testing.T) {
	w, err := newMatchWorld("m-loading-reconnect", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	now := time.Unix(1000, 0)
	w.Now = func() time.Time { return now }
	a, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	oldGeneration := a.Generation
	w.MarkLoaded("a", oldGeneration)
	w.Disconnect("a", oldGeneration)
	if a.Loaded {
		t.Fatal("disconnect retained loading acknowledgement")
	}
	now = now.Add(40 * time.Second)
	a, err = w.Attach("a", false)
	if err != nil {
		t.Fatalf("loading reconnect was incorrectly expired: %v", err)
	}
	if w.MarkLoaded("a", oldGeneration) || a.Loaded {
		t.Fatal("old generation acknowledged after reconnect")
	}
	w.MarkLoaded("a", a.Generation)
	if !a.Loaded {
		t.Fatal("current generation acknowledgement ignored")
	}
	firstReconnectGeneration := a.Generation
	a, err = w.Attach("a", true)
	if err != nil {
		t.Fatal(err)
	}
	if a.Loaded || w.MarkLoaded("a", firstReconnectGeneration) {
		t.Fatal("takeover retained old loading acknowledgement")
	}
	b, err := w.Attach("b", false)
	if err != nil {
		t.Fatal(err)
	}
	if w.MarkLoaded("b", b.Generation) || w.Phase != phaseLoading {
		t.Fatal("match started before takeover connection loaded")
	}
	if !w.MarkLoaded("a", a.Generation) || w.Phase != phasePlaying {
		t.Fatal("current takeover connection did not start the match")
	}
	if w.MarkLoaded("a", a.Generation) {
		t.Fatal("duplicate acknowledgement restarted the match")
	}
}

func TestEngineStartedSignalAndReliableReadyStartMessage(t *testing.T) {
	w, err := newMatchWorld("m-loading-engine", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	w.Rules.SnapshotRate = 1 // Keep the one-time start message ahead of the next snapshot.
	ctx, cancel := context.WithCancel(context.Background())
	e := newEngine(ctx, w)
	defer func() { cancel(); <-e.Done }()
	a, err := e.Attach(ctx, "a", false)
	if err != nil {
		t.Fatal(err)
	}
	b, err := e.Attach(ctx, "b", false)
	if err != nil {
		t.Fatal(err)
	}
	e.Loaded("a", a.Welcome.Generation)
	select {
	case <-e.Started:
		t.Fatal("engine started after only one acknowledgement")
	default:
	}
	e.Loaded("b", b.Welcome.Generation)
	select {
	case <-e.Started:
	case <-time.After(2 * time.Second):
		t.Fatal("engine did not signal full loading completion")
	}
	for _, connection := range []Connection{a, b} {
		select {
		case started := <-connection.Updates:
			if started.Type != "start" || started.Tick != 0 || started.Match == nil || started.Match.Phase != phasePlaying {
				t.Fatalf("wrong start message: %+v", started)
			}
		case <-time.After(time.Second):
			t.Fatal("start message was not broadcast")
		}
	}
}
