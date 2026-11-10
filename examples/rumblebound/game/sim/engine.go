package sim

import (
	"context"
	"errors"
	"log"
	"sync/atomic"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/server/loop"
	"github.com/win30221/gamenetkit/server/netcore"
)

type command struct{ fn func(*World) }
type Connection struct {
	Welcome protocol.Server
	Updates <-chan protocol.Server
}
type Engine struct {
	recording          netcore.ReplaySink
	recordingError     string
	currentTick        atomic.Uint64
	commands           chan command
	Done               chan struct{}
	Started            chan struct{}
	Finished           chan struct{}
	resultRetention    time.Duration
	TickNanos          atomic.Int64
	Overruns           atomic.Uint64
	DroppedInputs      atomic.Uint64
	tickCount          atomic.Uint64
	tickHistogram      [8]atomic.Uint64
	dispatchLagNanos   atomic.Int64
	simulationLagNanos atomic.Int64
	observedNanos      atomic.Int64
	observedSteps      atomic.Uint64
}

// EngineMetrics is a non-blocking, point-in-time diagnostic view. Counters are
// atomic so health requests never need a command on the simulation goroutine.
type EngineMetrics struct {
	TickCount          uint64  `json:"tickCount"`
	TickNanos          int64   `json:"tickNanos"`
	Overruns           uint64  `json:"overruns"`
	DroppedInputs      uint64  `json:"droppedInputs"`
	QueueDepth         int     `json:"queueDepth"`
	QueueCapacity      int     `json:"queueCapacity"`
	DispatchLagNanos   int64   `json:"dispatchLagNanos"`
	SimulationLagNanos int64   `json:"simulationLagNanos"`
	ActualTickRate     float64 `json:"actualTickRate"`
	// Non-cumulative counts: <=.25,.5,1,2,5,10,25 milliseconds, then >25ms.
	TickHistogram [8]uint64 `json:"tickHistogram"`
}

func (e *Engine) Metrics() EngineMetrics {
	m := EngineMetrics{TickCount: e.tickCount.Load(), TickNanos: e.TickNanos.Load(), Overruns: e.Overruns.Load(), DroppedInputs: e.DroppedInputs.Load(), QueueDepth: len(e.commands), QueueCapacity: cap(e.commands)}
	m.DispatchLagNanos, m.SimulationLagNanos = e.dispatchLagNanos.Load(), e.simulationLagNanos.Load()
	if elapsed := e.observedNanos.Load(); elapsed > 0 {
		m.ActualTickRate = float64(e.observedSteps.Load()) / (float64(elapsed) / float64(time.Second))
	}
	for i := range m.TickHistogram {
		m.TickHistogram[i] = e.tickHistogram[i].Load()
	}
	return m
}

func (e *Engine) recordTick(elapsed, interval time.Duration) {
	e.TickNanos.Store(elapsed.Nanoseconds())
	e.tickCount.Add(1)
	bucket := 7
	for i, limit := range [...]time.Duration{250 * time.Microsecond, 500 * time.Microsecond, time.Millisecond, 2 * time.Millisecond, 5 * time.Millisecond, 10 * time.Millisecond, 25 * time.Millisecond} {
		if elapsed <= limit {
			bucket = i
			break
		}
	}
	e.tickHistogram[bucket].Add(1)
	if elapsed > interval {
		e.Overruns.Add(1)
	}
}

func NewEngine(ctx context.Context) *Engine {
	return newEngine(ctx, New())
}

func NewMatchEngine(ctx context.Context, matchID string, roster map[string]string) (*Engine, error) {
	w, err := newMatchWorld(matchID, roster)
	if err != nil {
		return nil, err
	}
	return newEngine(ctx, w), nil
}

func NewMatchEngineWithHeroes(ctx context.Context, matchID string, roster, heroes map[string]string) (*Engine, error) {
	w, err := newMatchWorldWithHeroes(matchID, roster, heroes)
	if err != nil {
		return nil, err
	}
	return newEngine(ctx, w), nil
}

// NewMatchEngineWithMap freezes the complete gameplay definition before admission.
// The diagnostic and legacy fixture constructors deliberately keep an empty arena.
func NewMatchEngineWithMap(ctx context.Context, matchID string, roster, heroes map[string]string, m *shared.ArenaMapDefinition) (*Engine, error) {
	w, err := newMatchWorldWithMap(matchID, roster, heroes, m)
	if err != nil {
		return nil, err
	}
	return newEngine(ctx, w), nil
}

func newEngine(ctx context.Context, w *World) *Engine {
	return newEngineWithRetention(ctx, w, 30*time.Second)
}

func newEngineWithRetention(ctx context.Context, w *World, retention time.Duration) *Engine {
	e := &Engine{commands: make(chan command, 128), Done: make(chan struct{}), Started: make(chan struct{}), Finished: make(chan struct{}), resultRetention: retention}
	var recordErr error
	e.recording, recordErr = w.startReplay(ctx)
	if recordErr != nil {
		e.recordingError = recordErr.Error()
		log.Printf("Replay recording unavailable: %v", recordErr)
	} else if e.recording != nil {
		log.Printf("Replay recording started: %s", e.recording.Path())
	}
	if w.roster == nil || w.Phase == phasePlaying {
		close(e.Started)
	}
	go e.run(ctx, w)
	return e
}
func (e *Engine) run(ctx context.Context, w *World) {
	defer close(e.Done)
	defer func() {
		if e.recording != nil {
			if err := e.recording.Close(); err != nil {
				log.Printf("Replay incomplete %s: %v", e.recording.Path(), err)
			} else {
				log.Printf("Replay saved: %s", e.recording.Path())
			}
		}
	}()
	interval := time.Second / time.Duration(w.Rules.TickRate)
	var settled <-chan time.Time
	var retention *time.Timer
	defer func() {
		if retention != nil {
			retention.Stop()
		}
	}()
	defer func() {
		for _, seat := range w.Seats {
			if seat.Updates != nil {
				close(seat.Updates)
				seat.Updates = nil
			}
		}
	}()
	_ = loop.Run(ctx, loop.Options[command]{
		Interval: interval, CommandBudget: 128, Commands: e.commands,
		Apply: func(c command) { c.fn(w) },
		Step: func() {
			w.Step()
			e.currentTick.Store(w.Tick)
			if w.Result != nil && retention == nil {
				close(e.Finished)
				retention = time.NewTimer(e.resultRetention)
				settled = retention.C
			}
		},
		StopSignal: func() <-chan time.Time { return settled }, Measured: e.recordTick,
		Scheduled: func(dispatch, lag, elapsed time.Duration, steps uint64) {
			e.dispatchLagNanos.Store(dispatch.Nanoseconds())
			e.simulationLagNanos.Store(lag.Nanoseconds())
			e.observedSteps.Store(steps)
			e.observedNanos.Store(elapsed.Nanoseconds())
		},
	})
}
func (e *Engine) CurrentTick() uint64 { return e.currentTick.Load() }

func (e *Engine) Attach(ctx context.Context, user string, takeover bool) (Connection, error) {
	type result struct {
		c   Connection
		err error
	}
	reply := make(chan result, 1)
	cmd := command{fn: func(w *World) {
		if ctx.Err() != nil {
			reply <- result{err: ctx.Err()}
			return
		}
		s, err := w.Attach(user, takeover)
		if err != nil {
			reply <- result{err: err}
			return
		}
		kind := "welcome"
		if w.Result != nil {
			kind = "result"
		}
		reply <- result{c: Connection{Welcome: w.Snapshot(s, kind), Updates: s.Updates}}
	}}
	select {
	case e.commands <- cmd:
	case <-ctx.Done():
		return Connection{}, ctx.Err()
	case <-e.Done:
		return Connection{}, errors.New("server_stopped")
	}
	// Once queued, observe the result even if the caller cancels, so a successfully
	// attached seat always has a generation that its caller can disconnect.
	select {
	case r := <-reply:
		return r.c, r.err
	case <-e.Done:
		return Connection{}, errors.New("server_stopped")
	}
}
func (e *Engine) Input(user string, g uint32, c protocol.Client) {
	select {
	case e.commands <- command{fn: func(w *World) { w.Input(user, g, inputIntent(c)) }}:
	default:
		e.DroppedInputs.Add(1)
	}
}

// Loaded processes an idempotent loading acknowledgement. Only the current
// online generation can complete a frozen roster's loading barrier.
func (e *Engine) Loaded(user string, generation uint32) {
	select {
	case e.commands <- command{fn: func(w *World) {
		if w.MarkLoaded(user, generation) {
			close(e.Started)
		}
	}}:
	case <-e.Done:
	}
}
func (e *Engine) Disconnect(user string, g uint32) {
	select {
	case e.commands <- command{fn: func(w *World) { w.Disconnect(user, g) }}:
	case <-e.Done:
	}
}

// InspectPlayer copies state on the owner goroutine for the isolated network
// harness. It neither allocates a seat nor changes reconnect deadlines.
func (e *Engine) InspectPlayer(ctx context.Context, user string) (protocol.Server, error) {
	reply := make(chan protocol.Server, 1)
	select {
	case e.commands <- command{fn: func(w *World) {
		if seat := w.Seats[user]; seat != nil {
			reply <- w.Snapshot(seat, "snapshot")
		} else {
			reply <- protocol.Server{}
		}
	}}:
	case <-ctx.Done():
		return protocol.Server{}, ctx.Err()
	case <-e.Done:
		return protocol.Server{}, errors.New("server_stopped")
	}
	select {
	case state := <-reply:
		return state, nil
	case <-ctx.Done():
		return protocol.Server{}, ctx.Err()
	case <-e.Done:
		return protocol.Server{}, errors.New("server_stopped")
	}
}
