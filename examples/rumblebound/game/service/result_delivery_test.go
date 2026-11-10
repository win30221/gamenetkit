package gateway

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

type resultWriteWire struct {
	payload  []byte
	reliable bool
}

func (*resultWriteWire) ReadHello(context.Context) ([]byte, error) { panic("unused") }
func (*resultWriteWire) ReadInput(context.Context) ([]byte, error) { panic("unused") }
func (w *resultWriteWire) Write(_ context.Context, b []byte, reliable bool) error {
	w.payload = append([]byte(nil), b...)
	w.reliable = reliable
	return nil
}
func (*resultWriteWire) Reject(context.Context, []byte) error { panic("unused") }
func (*resultWriteWire) Close()                               {}

func TestMatchTransitionsUseReliableTransport(t *testing.T) {
	s := &Server{}
	w := &resultWriteWire{}
	if err := s.writeUpdate(context.Background(), w, protocol.Server{Type: "start", Tick: 0}); err != nil {
		t.Fatal(err)
	}
	if !w.reliable {
		t.Fatal("match start was sent as a lossy datagram")
	}
	result := protocol.Server{Type: "result", MatchID: "room-1", Match: &protocol.MatchState{
		Result: &protocol.MatchResult{Outcome: "win", Reason: "score", WinnerTeamID: "t1", EndedTick: 3600},
	}}
	if err := s.writeUpdate(context.Background(), w, result); err != nil {
		t.Fatal(err)
	}
	if !w.reliable {
		t.Fatal("terminal result was sent as a lossy datagram")
	}
	var delivered protocol.Server
	if err := json.Unmarshal(w.payload, &delivered); err != nil || delivered.Match == nil || delivered.Match.Result == nil || delivered.Match.Result.WinnerTeamID != "t1" {
		t.Fatalf("result payload changed: %+v, %v", delivered, err)
	}
	if err := s.writeUpdate(context.Background(), w, protocol.Server{Type: "snapshot"}); err != nil {
		t.Fatal(err)
	}
	if w.reliable {
		t.Fatal("ordinary snapshot did not use the datagram path")
	}
}
