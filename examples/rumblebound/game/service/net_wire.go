package gateway

import (
	"context"
	"encoding/json"
	"errors"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/server/netcore"
	"time"
)

// Work from a per-recipient projection. Domain state and other clients are untouched.
func boundedSnapshot(message protocol.Server) ([]byte, error) {
	data, err := json.Marshal(message)
	if err != nil || len(data) <= protocol.MaxSnapshot {
		return data, err
	}
	// Removing prefixes in the original priority order monotonically reduces
	// JSON size. Find the smallest removal count, preserving exactly the same
	// newest events as the former one-at-a-time loop.
	trim := func(count int) protocol.Server {
		out := message
		n := min(count, len(out.ProjectileEnds))
		out.ProjectileEnds = out.ProjectileEnds[n:]
		count -= n
		n = min(count, len(out.CombatEvents))
		out.CombatEvents = out.CombatEvents[n:]
		count -= n
		n = min(count, len(out.GroundImpacts))
		out.GroundImpacts = out.GroundImpacts[n:]
		return out
	}
	low, high := 1, len(message.ProjectileEnds)+len(message.CombatEvents)+len(message.GroundImpacts)
	if high == 0 {
		return nil, errors.New("snapshot_too_large")
	}
	best, err := json.Marshal(trim(high))
	if err != nil {
		return nil, err
	}
	if len(best) > protocol.MaxSnapshot {
		return nil, errors.New("snapshot_too_large")
	}
	for low < high {
		middle := low + (high-low)/2
		candidate, err := json.Marshal(trim(middle))
		if err != nil {
			return nil, err
		}
		if len(candidate) <= protocol.MaxSnapshot {
			high = middle
			best = candidate
		} else {
			low = middle + 1
		}
	}
	return best, nil
}
func (s *Server) writeNetUpdate(ctx context.Context, wire wire, message protocol.Server, encoder netcore.FrameEncoder, ack uint64) error {
	if encoder == nil || message.Type != "snapshot" {
		return s.writeUpdate(ctx, wire, message)
	}
	raw, err := boundedSnapshot(message)
	if err != nil {
		return err
	}
	frame, err := encoder.Encode(raw, ack)
	if err != nil {
		return err
	}
	encoded, err := json.Marshal(protocol.FrameEnvelope{Type: "frame", Protocol: message.Protocol, Content: message.Content, MatchID: message.MatchID, Generation: message.Generation, Tick: message.Tick, ID: frame.ID, Base: frame.Base, Data: frame.Data})
	if err != nil {
		return err
	}
	// Base64 can expand a full keyframe; the interoperable raw snapshot is the
	// explicit size fallback. Its unsent frame ID can never be acknowledged.
	if len(encoded) > protocol.MaxSnapshot {
		encoded = raw
	}
	c, cancel := context.WithTimeout(ctx, time.Second)
	defer cancel()
	if err = wire.Write(c, encoded, false); err == nil {
		s.BytesOut.Add(uint64(len(encoded)))
	}
	return err
}
