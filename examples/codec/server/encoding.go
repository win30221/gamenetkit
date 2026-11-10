package main

import (
	"encoding/json"
	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/netcore"
	"math"
)

type codecState struct {
	Mode     string  `json:"mode"`
	Step     float64 `json:"step"`
	MaxError float64 `json:"maxError"`
	Stream   uint32  `json:"stream"`
}
type frameMessage struct {
	Type     string `json:"type"`
	Protocol int    `json:"protocol"`
	Stream   uint32 `json:"stream"`
	Tick     uint64 `json:"tick"`
	ID       uint64 `json:"id"`
	Base     uint64 `json:"base"`
	Data     string `json:"data"`
}

func validStep(step float64) bool { return step == 0 || step == .01 || step == .1 }
func (w *world) codecProjection(p *player, kind string) stateMessage {
	m := w.project(p, kind)
	if p.codecMode == "" {
		p.codecMode = "json"
	}
	if p.stream == 0 {
		p.stream = 1
	}
	state := codecState{Mode: p.codecMode, Step: p.quantizeStep, Stream: p.stream}
	if state.Step > 0 {
		quantize := func(value float64) float64 {
			q, _ := codec.Quantize(value, state.Step)
			state.MaxError = math.Max(state.MaxError, math.Abs(q-value))
			return q
		}
		for i := range m.Players {
			m.Players[i].X = quantize(m.Players[i].X)
			m.Players[i].Z = quantize(m.Players[i].Z)
		}
		for i := range m.Objects {
			m.Objects[i].X = quantize(m.Objects[i].X)
			m.Objects[i].Z = quantize(m.Objects[i].Z)
		}
		m.Interest.CenterX = quantize(m.Interest.CenterX)
		m.Interest.CenterZ = quantize(m.Interest.CenterZ)
	}
	m.Codec = state
	return m
}
func (w *world) encodeSnapshot(p *player) ([]byte, error) {
	payload, err := json.Marshal(w.codecProjection(p, "snapshot"))
	if err != nil {
		return nil, err
	}
	if p.codecMode != "delta" {
		return payload, nil
	}
	if p.encoder == nil {
		p.encoder = codec.New()
	}
	frame, err := p.encoder.Encode(payload, p.decodedAck)
	if err != nil {
		return nil, err
	}
	p.lastFrame = frame.ID
	return json.Marshal(frameMessage{Type: "frame", Protocol: 1, Stream: p.stream, Tick: w.tick, ID: frame.ID, Base: frame.Base, Data: frame.Data})
}
func (w *world) setCodec(p *player, mode string, step float64) {
	if p.codecMode == mode && p.quantizeStep == step {
		return
	}
	p.codecMode = mode
	p.quantizeStep = step
	p.stream++
	p.encoder = codec.New()
	p.decodedAck = 0
	p.lastFrame = 0
}
func (w *world) acknowledge(p *player, stream uint32, id uint64) {
	if stream != p.stream || p.codecMode != "delta" || id > p.lastFrame {
		return
	}
	if id == 0 || id >= p.decodedAck {
		p.decodedAck = id
	}
}

// Keep these contracts visible in the teaching composition: the SDK consumes
// opaque bytes and immutable acknowledged baselines, never game state.
var _ netcore.FrameEncoder = (*codec.Encoder)(nil)
