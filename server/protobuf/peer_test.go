package protobuf

import (
	"context"
	"errors"
	"testing"

	"github.com/win30221/gamenetkit/server/transport"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/types/known/wrapperspb"
)

type memoryWire struct {
	data   []byte
	writes int
}

func (w *memoryWire) Read(context.Context) ([]byte, error)    { return w.data, nil }
func (w *memoryWire) Write(_ context.Context, b []byte) error { w.data = b; w.writes++; return nil }
func (*memoryWire) Close()                                    {}

func TestPeerOwnershipLimitsAndDecode(t *testing.T) {
	wire := &memoryWire{}
	peer, err := NewPeer[*wrapperspb.StringValue, *wrapperspb.StringValue](wire, func() *wrapperspb.StringValue { return new(wrapperspb.StringValue) }, 16)
	if err != nil {
		t.Fatal(err)
	}
	ctx := context.Background()
	out := wrapperspb.String("hello")
	if err := peer.Write(ctx, out); err != nil {
		t.Fatal(err)
	}
	out.Value = "changed"
	first, err := peer.Read(ctx)
	if err != nil || first.Value != "hello" {
		t.Fatalf("roundtrip: %v %v", first, err)
	}
	second, err := peer.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	first.Value = "mutated"
	if second.Value != "hello" {
		t.Fatal("Read reused message storage")
	}
	wire.data = []byte{0x80}
	if _, err := peer.Read(ctx); !errors.Is(err, ErrDecode) {
		t.Fatalf("invalid: %v", err)
	}
	wire.data = make([]byte, 17)
	if _, err := peer.Read(ctx); !errors.Is(err, transport.ErrMessageTooLarge) {
		t.Fatalf("oversize read: %v", err)
	}
	if err := peer.Write(ctx, wrapperspb.String("this string is too long")); !errors.Is(err, transport.ErrMessageTooLarge) {
		t.Fatalf("oversize write: %v", err)
	}
	if wire.writes != 1 {
		t.Fatal("oversize write reached wire")
	}
	if err := peer.Write(ctx, nil); !errors.Is(err, ErrNilMessage) {
		t.Fatal(err)
	}
	// Unknown fields survive decode, allowing application-level evolution policy.
	wire.data = []byte{0x98, 0x06, 1}
	m, err := peer.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	encoded, _ := proto.Marshal(m)
	if len(encoded) != 3 {
		t.Fatal("unknown field discarded")
	}
}

func TestPeerRejectsNilFactory(t *testing.T) {
	_, err := NewPeer[*wrapperspb.StringValue, *wrapperspb.StringValue](&memoryWire{}, func() *wrapperspb.StringValue { return nil }, 0)
	if !errors.Is(err, ErrFactory) {
		t.Fatal(err)
	}
	_, err = NewPeer[proto.Message, proto.Message](&memoryWire{}, func() proto.Message { return nil }, 0)
	if !errors.Is(err, ErrFactory) {
		t.Fatal(err)
	}
}
