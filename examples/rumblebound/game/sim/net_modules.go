package sim

import (
	"context"
	"errors"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/server/netcore"
	"math"
	"slices"
)

type historicalPose struct {
	X, Z       float64
	Entity     string
	Life       uint64
	Generation uint32
	Alive      bool
}
type poseFrame map[string]historicalPose

var historyFactory func() netcore.History[poseFrame]
var interestFactory func(float64) netcore.Interest
var rewindFactory func(int) netcore.RewindPolicy
var encoderFactory func() netcore.FrameEncoder
var quantizePosition func(float64) float64

func NetFeatures() []string {
	features := []string{"clock", "replication", "actions"}
	if quantizePosition != nil {
		features = append(features, "quantize")
	}
	if historyFactory != nil {
		features = append(features, "history")
	}
	if rewindFactory != nil && historyFactory != nil {
		features = append(features, "lag")
	}
	if interestFactory != nil {
		features = append(features, "interest")
	}
	if encoderFactory != nil {
		features = append(features, "codec")
	}
	return features
}
func NewFrameEncoder() netcore.FrameEncoder {
	if encoderFactory == nil {
		return nil
	}
	return encoderFactory()
}
func ValidateNetOptions(o *protocol.NetOptions) error {
	if o == nil {
		return nil
	}
	if (o.Codec != "json" && o.Codec != "delta") || (o.Interest != "all" && o.Interest != "radius") || (o.Lag != "off" && o.Lag != "melee") || math.IsNaN(o.Radius) || math.IsInf(o.Radius, 0) || o.Radius < 4 || o.Radius > 40 {
		return errors.New("invalid_net_options")
	}
	if o.Quantize && quantizePosition == nil || o.Codec == "delta" && encoderFactory == nil || o.Interest == "radius" && interestFactory == nil || o.History && historyFactory == nil || o.Lag == "melee" && (rewindFactory == nil || !o.History) {
		return errors.New("unsupported_net_modules")
	}
	return nil
}
func (e *Engine) ConfigureNet(ctx context.Context, user string, g uint32, options *protocol.NetOptions) (protocol.Server, error) {
	if err := ValidateNetOptions(options); err != nil {
		return protocol.Server{}, err
	}
	if options != nil {
		copy := *options
		options = &copy
	}
	reply := make(chan protocol.Server, 1)
	select {
	case e.commands <- command{fn: func(w *World) {
		if ctx.Err() != nil {
			reply <- protocol.Server{}
			return
		}
		reply <- w.configureNet(user, g, options)
	}}:
	case <-ctx.Done():
		return protocol.Server{}, ctx.Err()
	case <-e.Done:
		return protocol.Server{}, errors.New("server_stopped")
	}
	select {
	case value := <-reply:
		if value.Type == "" {
			return value, errors.New("stale_generation")
		}
		return value, nil
	case <-ctx.Done():
		return protocol.Server{}, ctx.Err()
	case <-e.Done:
		return protocol.Server{}, errors.New("server_stopped")
	}
}
func (w *World) configureNet(user string, g uint32, options *protocol.NetOptions) protocol.Server {
	if w.recording != nil {
		defer w.recordEvent("net", replayEventPayload{User: user, Generation: g, Options: options})()
	}
	s := w.Seats[user]
	if s == nil || s.Generation != g {
		return protocol.Server{}
	}
	if options != nil {
		copy := *options
		s.Net = &copy
		if copy.Interest == "radius" {
			s.Interest = interestFactory(copy.Radius)
		}
		if copy.History && w.history == nil {
			w.history = historyFactory()
		}
		w.recordHistory()
	}
	kind := "welcome"
	if w.Result != nil {
		kind = "result"
	}
	return w.Snapshot(s, kind)
}
func clonePoseFrame(source poseFrame) poseFrame {
	out := make(poseFrame, len(source))
	for k, v := range source {
		out[k] = v
	}
	return out
}
func (w *World) recordHistory() {
	if w.history == nil {
		return
	}
	needed := false
	for _, s := range w.Seats {
		if s.Net != nil && s.Net.History && s.State.Online {
			needed = true
			break
		}
	}
	if !needed {
		w.history.Clear()
		return
	}
	frame := make(poseFrame, len(w.Seats))
	for _, s := range w.Seats {
		frame[s.State.PlayerID] = historicalPose{X: s.State.X, Z: s.State.Z, Entity: s.State.EntityID, Life: s.Life, Generation: s.Generation, Alive: s.State.Online && !s.State.Left && s.State.HP > 0}
	}
	w.history.Add(w.Tick, frame)
}
func (w *World) historicalPosition(target *Seat, tick uint64) (float64, float64, bool) {
	if w.history == nil {
		return 0, 0, false
	}
	sample, ok := w.history.At(tick)
	if !ok {
		return 0, 0, false
	}
	a, ok := sample.Before[target.State.PlayerID]
	if !ok {
		return 0, 0, false
	}
	b, ok := sample.After[target.State.PlayerID]
	if !ok || !a.Alive || !b.Alive || a.Entity != target.State.EntityID || b.Entity != a.Entity || a.Life != target.Life || b.Life != a.Life || a.Generation != target.Generation || b.Generation != a.Generation {
		return 0, 0, false
	}
	alpha := 0.0
	if sample.AfterTick > sample.BeforeTick {
		alpha = float64(tick-sample.BeforeTick) / float64(sample.AfterTick-sample.BeforeTick)
	}
	// Avoid interpolating a teleport, even within the same life.
	if math.Hypot(b.X-a.X, b.Z-a.Z) > 2 {
		return 0, 0, false
	}
	return a.X + (b.X-a.X)*alpha, a.Z + (b.Z-a.Z)*alpha, true
}
func (w *World) projectNet(s *Seat, state protocol.Server) protocol.Server {
	if s.Net == nil {
		return state
	}
	// Publication views can be shared by asynchronous writers. Copy only the
	// arrays this recipient's projection mutates; never recycle published data.
	if s.Interest != nil || s.Net.Quantize {
		state.Players = slices.Clone(state.Players)
		state.Projectiles = slices.Clone(state.Projectiles)
	}
	if s.Interest != nil {
		state.GroundCasts = slices.Clone(state.GroundCasts)
		state.ProjectileEnds = slices.Clone(state.ProjectileEnds)
		state.CombatEvents = slices.Clone(state.CombatEvents)
		state.GroundImpacts = slices.Clone(state.GroundImpacts)
	}
	total := len(state.Players) + len(state.Projectiles) + len(state.GroundCasts)
	if s.Interest != nil {
		objects := make([]netcore.InterestObject, 0, total+128)
		for _, p := range state.Players {
			objects = append(objects, netcore.InterestObject{ID: "p:" + p.PlayerID, X: p.X, Z: p.Z, Always: p.PlayerID == s.State.PlayerID})
		}
		for _, p := range state.Projectiles {
			objects = append(objects, netcore.InterestObject{ID: "b:" + p.ID, X: p.X, Z: p.Z, Always: p.OwnerID == s.State.PlayerID})
		}
		for _, p := range state.GroundCasts {
			objects = append(objects, netcore.InterestObject{ID: "g:" + p.ID, X: p.X, Z: p.Z, Always: p.OwnerID == s.State.PlayerID})
		}
		for _, p := range state.ProjectileEnds {
			objects = append(objects, netcore.InterestObject{ID: "e:" + p.ID, X: p.X, Z: p.Z, Always: p.OwnerID == s.State.PlayerID || p.TargetID == s.State.PlayerID})
		}
		for _, p := range state.CombatEvents {
			objects = append(objects, netcore.InterestObject{ID: "m:" + p.ID, X: p.X, Z: p.Z, Always: p.OwnerID == s.State.PlayerID || p.TargetID == s.State.PlayerID})
		}
		for _, p := range state.GroundImpacts {
			objects = append(objects, netcore.InterestObject{ID: "i:" + p.ID, X: p.X, Z: p.Z, Always: p.OwnerID == s.State.PlayerID})
		}
		visible := s.Interest.Select(s.State.X, s.State.Z, objects)
		players := state.Players[:0]
		for _, p := range state.Players {
			if visible["p:"+p.PlayerID] {
				players = append(players, p)
			}
		}
		state.Players = players
		projectiles := state.Projectiles[:0]
		for _, p := range state.Projectiles {
			if visible["b:"+p.ID] {
				projectiles = append(projectiles, p)
			}
		}
		state.Projectiles = projectiles
		casts := state.GroundCasts[:0]
		for _, p := range state.GroundCasts {
			if visible["g:"+p.ID] {
				casts = append(casts, p)
			}
		}
		state.GroundCasts = casts
		ends := state.ProjectileEnds[:0]
		for _, p := range state.ProjectileEnds {
			if visible["e:"+p.ID] {
				ends = append(ends, p)
			}
		}
		state.ProjectileEnds = ends
		events := state.CombatEvents[:0]
		for _, p := range state.CombatEvents {
			if visible["m:"+p.ID] {
				events = append(events, p)
			}
		}
		state.CombatEvents = events
		impacts := state.GroundImpacts[:0]
		for _, p := range state.GroundImpacts {
			if visible["i:"+p.ID] {
				impacts = append(impacts, p)
			}
		}
		state.GroundImpacts = impacts
	}
	if s.Net.Quantize {
		quantize := quantizePosition
		for i := range state.Players {
			p := &state.Players[i]
			x, z := quantize(p.X), quantize(p.Z)
			if w.Map == nil || shared.CircleClear(w.Map, x, z, w.Rules.PlayerRadius) {
				p.X, p.Z = x, z
			}
		}
		for i := range state.Projectiles {
			p := &state.Projectiles[i]
			p.X, p.Z = quantize(p.X), quantize(p.Z)
		}
	}
	samples := 0
	if w.history != nil {
		samples = w.history.Len()
	}
	state.Tech = &protocol.TechState{Options: *s.Net, HistorySamples: samples, RewindTick: s.RewindTick, RewindUsed: s.RewindUsed, Total: total, Visible: len(state.Players) + len(state.Projectiles) + len(state.GroundCasts)}
	return state
}
