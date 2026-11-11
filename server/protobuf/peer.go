// Package protobuf connects generated envelopes to a bounded binary transport.
// It does not register message types or decide what an envelope body means.
package protobuf

import (
	"context"
	"errors"
	"fmt"

	"github.com/win30221/gamenetkit/server/transport"
	"google.golang.org/protobuf/proto"
)

var ErrFactory = errors.New("invalid_message_factory")
var ErrDecode = errors.New("protobuf_decode_failed")
var ErrNilMessage = errors.New("nil_protobuf_message")

// Peer has one read owner. Factory must return a fresh, non-nil message on every
// call. Read transfers ownership to the caller. Write marshals synchronously;
// callers must not mutate a message until Write returns. No message is retained.
// Unknown fields are preserved; absent/unknown oneof bodies are application policy.
type Peer[In proto.Message, Out proto.Message] struct {
	wire    transport.BinaryWire
	factory func() In
	limit   int
}

func NewPeer[In proto.Message, Out proto.Message](wire transport.BinaryWire, factory func() In, maxBytes int) (*Peer[In, Out], error) {
	if wire == nil || factory == nil {
		return nil, ErrFactory
	}
	message := factory()
	if any(message) == nil || !message.ProtoReflect().IsValid() {
		return nil, ErrFactory
	}
	if maxBytes <= 0 {
		maxBytes = transport.DefaultBinaryLimit
	}
	return &Peer[In, Out]{wire: wire, factory: factory, limit: maxBytes}, nil
}

func (p *Peer[In, Out]) Read(ctx context.Context) (In, error) {
	var zero In
	data, err := p.wire.Read(ctx)
	if err != nil {
		return zero, err
	}
	if len(data) > p.limit {
		return zero, transport.ErrMessageTooLarge
	}
	message := p.factory()
	if any(message) == nil || !message.ProtoReflect().IsValid() {
		return zero, ErrFactory
	}
	if err := (proto.UnmarshalOptions{RecursionLimit: 100}).Unmarshal(data, message); err != nil {
		return zero, fmt.Errorf("%w: %v", ErrDecode, err)
	}
	return message, nil
}

func (p *Peer[In, Out]) Write(ctx context.Context, message Out) error {
	if any(message) == nil || !message.ProtoReflect().IsValid() {
		return ErrNilMessage
	}
	if proto.Size(message) > p.limit {
		return transport.ErrMessageTooLarge
	}
	data, err := proto.Marshal(message)
	if err != nil {
		return err
	}
	if len(data) > p.limit {
		return transport.ErrMessageTooLarge
	}
	return p.wire.Write(ctx, data)
}

func (p *Peer[In, Out]) Close() { p.wire.Close() }
