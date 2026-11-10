package gateway

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func TestDatagramSendDeadlineUnblocksFullConnectionQueue(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Millisecond)
	defer cancel()
	unblocked := make(chan struct{})
	var aborted atomic.Bool
	err := boundedDatagram(ctx, func() { aborted.Store(true); close(unblocked) }, func() error { <-unblocked; return errors.New("connection_closed") })
	if !errors.Is(err, context.DeadlineExceeded) || !aborted.Load() {
		t.Fatalf("deadline failed to abort blocked send: %v", err)
	}
	ctx2, cancel2 := context.WithCancel(context.Background())
	var abortCount atomic.Int32
	if err := boundedDatagram(ctx2, func() { abortCount.Add(1) }, func() error { return nil }); err != nil {
		t.Fatal(err)
	}
	cancel2()
	// Cancellation after a completed send must not kill the healthy connection.
	time.Sleep(30 * time.Millisecond)
	if abortCount.Load() != 0 {
		t.Fatal("completed send retained a cancellation callback")
	}
}

type slowCloseWire struct {
	hello            []byte
	closing, release chan struct{}
	once             sync.Once
}

func (w *slowCloseWire) ReadHello(context.Context) ([]byte, error) { return w.hello, nil }
func (w *slowCloseWire) ReadInput(context.Context) ([]byte, error) { return nil, io.EOF }
func (w *slowCloseWire) Write(context.Context, []byte, bool) error { return nil }
func (w *slowCloseWire) Reject(context.Context, []byte) error      { return nil }
func (w *slowCloseWire) Close()                                    { w.once.Do(func() { close(w.closing) }); <-w.release }

func TestSeatDetachesBeforeSlowTransportCleanup(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	engine := sim.NewEngine(ctx)
	auth, _ := NewAuth("bounded-cleanup-development-test-key")
	session, identity, err := auth.NewSession("bounded-cleanup-development-test-key")
	if err != nil {
		t.Fatal(err)
	}
	ticket, err := auth.Ticket(session, false)
	if err != nil {
		t.Fatal(err)
	}
	rules := shared.Rules()
	hello, _ := json.Marshal(protocol.Client{Type: "hello", Protocol: rules.Protocol, Content: rules.Content, MatchID: sim.MatchID, Ticket: ticket})
	wire := &slowCloseWire{hello: hello, closing: make(chan struct{}), release: make(chan struct{})}
	done := make(chan struct{})
	go func() { defer close(done); New(auth, engine, nil).serve(ctx, wire) }()
	defer func() {
		close(wire.release)
		select {
		case <-done:
		case <-time.After(time.Second):
			t.Error("handler did not finish")
		}
	}()
	select {
	case <-wire.closing:
	case <-time.After(time.Second):
		t.Fatal("cleanup never started")
	}
	deadline, stop := context.WithTimeout(ctx, 500*time.Millisecond)
	defer stop()
	for {
		state, err := engine.InspectPlayer(deadline, identity.UserID)
		if err != nil {
			t.Fatal("seat remained owned while transport cleanup blocked:", err)
		}
		for _, player := range state.Players {
			if player.PlayerID == state.PlayerID && !player.Online {
				return
			}
		}
	}
}
