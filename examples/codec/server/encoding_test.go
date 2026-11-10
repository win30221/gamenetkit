package main

import (
	"context"
	"encoding/json"
	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/netcore"
	"math"
	"testing"
	"time"
)

func TestActualEncoderBaselinesStreamsAndQuantizedProjection(t *testing.T) {
	p, q := &peer{}, &peer{}
	one := &player{peer: p, state: playerState{ID: 1, X: 1.23456789, Z: -.987654321}, interestMode: "all", radius: 3, codecMode: "json", stream: 1}
	two := &player{peer: q, state: playerState{ID: 2, X: 2}, interestMode: "all", radius: 3, codecMode: "json", stream: 1}
	w := &world{tick: 10, players: map[*peer]*player{p: one, q: two}, objects: map[uint64]objectState{1: {ID: "object-1", Owner: 1, Life: 1, X: 2.3456789, Z: 3.456789, Kind: "marker"}}}
	original := one.state
	object := w.objects[1]
	w.setCodec(one, "delta", 0)
	stream := one.stream
	packet, err := w.encodeSnapshot(one)
	if err != nil {
		t.Fatal(err)
	}
	var first frameMessage
	_ = json.Unmarshal(packet, &first)
	if first.Base != 0 || first.ID != 1 || first.Stream != stream {
		t.Fatal(first)
	}
	base, err := codec.Decode(netcore.EncodedFrame{ID: first.ID, Base: first.Base, Data: first.Data}, nil)
	if err != nil {
		t.Fatal(err)
	}
	w.acknowledge(one, stream, first.ID)
	w.tick += 2
	one.state.X += .002
	packet, err = w.encodeSnapshot(one)
	if err != nil {
		t.Fatal(err)
	}
	var next frameMessage
	_ = json.Unmarshal(packet, &next)
	if next.Base != first.ID || len(packet) >= len(base) {
		t.Fatal("no useful acknowledged delta", next.Base, len(packet), len(base))
	}
	decoded, err := codec.Decode(netcore.EncodedFrame{ID: next.ID, Base: next.Base, Data: next.Data}, base)
	if err != nil {
		t.Fatal(err)
	}
	expected, _ := json.Marshal(w.codecProjection(one, "snapshot"))
	if string(decoded) != string(expected) {
		t.Fatal("delta roundtrip changed payload")
	}
	w.acknowledge(one, stream, 99999)
	if one.decodedAck != 1 {
		t.Fatal("future ack poisoned baseline")
	}
	w.acknowledge(one, stream+1, 0)
	if one.decodedAck != 1 {
		t.Fatal("foreign stream poisoned baseline")
	}
	w.acknowledge(one, stream, 0)
	packet, _ = w.encodeSnapshot(one)
	var full frameMessage
	_ = json.Unmarshal(packet, &full)
	if full.Base != 0 {
		t.Fatal("ack0 did not request full frame")
	}
	before := one.state
	w.setCodec(one, "delta", .1)
	projected := w.codecProjection(one, "snapshot")
	if one.stream == stream || one.decodedAck != 0 || one.lastFrame != 0 {
		t.Fatal("stream configuration did not reset per-peer codec")
	}
	if math.Abs(projected.Players[0].X-1.2) > 1e-8 || projected.Codec.MaxError <= 0 || projected.Codec.MaxError > .05+1e-9 {
		t.Fatal("bad quantized projection", projected.Codec)
	}
	if one.state != before || w.objects[1] != object || two.codecMode != "json" || two.quantizeStep != 0 {
		t.Fatal("quantization mutated authority or another recipient", original)
	}
	w.acknowledge(one, stream, first.ID)
	if one.decodedAck != 0 {
		t.Fatal("old stream ack accepted after reset")
	}
}
func TestCodecControlsRejectUnknownStepAndInvalidAck(t *testing.T) {
	_, server := testApplication(t)
	for _, body := range []string{`{"type":"codec","mode":"unknown","step":0}`, `{"type":"codec","mode":"delta","step":0.5}`, `{"type":"codec","mode":"delta","step":null}`, `{"type":"codec","mode":"delta","step":0,"owner":2}`, `{"type":"frame-ack","stream":0,"id":1}`, `{"type":"frame-ack","stream":1,"id":9007199254740992}`} {
		c := dial(t, server)
		join(t, c)
		send(t, c, body)
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		rejected := false
		for {
			_, data, err := c.Read(ctx)
			if err != nil {
				break
			}
			var m struct{ Type, Code string }
			_ = json.Unmarshal(data, &m)
			if m.Type == "error" && m.Code == "invalid_message" {
				rejected = true
				break
			}
		}
		cancel()
		c.CloseNow()
		if !rejected {
			t.Fatal("invalid codec control accepted", body)
		}
	}
}
