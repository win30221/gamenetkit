package main

import (
	"encoding/json"
	"github.com/win30221/gamenetkit/server/history"
	"github.com/win30221/gamenetkit/server/lagcomp"
	"math"
)

const targetRadius = .35
const historyCapacity = 90
const rewindTicks = 15

type targetState struct {
	Life uint32  `json:"life"`
	HP   int     `json:"hp"`
	X    float64 `json:"x"`
	Z    float64 `json:"z"`
}
type historyState struct {
	First     uint64 `json:"first"`
	Last      uint64 `json:"last"`
	Count     int    `json:"count"`
	MaxRewind uint64 `json:"maxRewind"`
}
type shotMessage struct {
	Type       string  `json:"type"`
	ID         uint64  `json:"id"`
	Tick       uint64  `json:"tick"`
	Observed   uint64  `json:"observed"`
	Life       uint32  `json:"life"`
	X          float64 `json:"x"`
	Z          float64 `json:"z"`
	Compensate bool    `json:"compensate"`
}
type shotOutcome struct {
	ID            uint64  `json:"id"`
	Accepted      bool    `json:"accepted"`
	Hit           bool    `json:"hit"`
	CurrentHit    bool    `json:"currentHit"`
	HistoricalHit bool    `json:"historicalHit"`
	Compensated   bool    `json:"compensated"`
	ShotTick      uint64  `json:"shotTick"`
	UsedTick      uint64  `json:"usedTick"`
	Life          uint32  `json:"life"`
	Reason        string  `json:"reason"`
	OriginX       float64 `json:"originX"`
	OriginZ       float64 `json:"originZ"`
	AimX          float64 `json:"aimX"`
	AimZ          float64 `json:"aimZ"`
	CurrentX      float64 `json:"currentX"`
	CurrentZ      float64 `json:"currentZ"`
	PastX         float64 `json:"pastX"`
	PastZ         float64 `json:"pastZ"`
}
type historyReply struct {
	Type     string       `json:"type"`
	Protocol int          `json:"protocol"`
	ID       uint64       `json:"id"`
	Tick     uint64       `json:"tick"`
	Found    bool         `json:"found"`
	Target   *targetState `json:"target,omitempty"`
	History  historyState `json:"history"`
}

func (w *world) ensureTarget() {
	if w.history != nil {
		return
	}
	w.history = history.New[targetState](historyCapacity, func(value targetState) targetState { return value })
	w.target = targetState{Life: 1, HP: 5, X: -5, Z: 3}
	w.targetDirection = 1
	w.history.Add(w.tick, w.target)
}
func (w *world) resetTarget() {
	w.ensureTarget()
	w.target.Life++
	w.target.HP = 5
	w.target.X = -5
	w.target.Z = 3
	w.targetDirection = 1
	w.history.Clear()
	w.history.Add(w.tick, w.target)
}
func (w *world) advanceTarget() {
	w.ensureTarget()
	w.target.X += w.targetDirection * 6 / tickRate
	if w.target.X > 5 {
		w.target.X = 10 - w.target.X
		w.targetDirection = -1
	}
	if w.target.X < -5 {
		w.target.X = -10 - w.target.X
		w.targetDirection = 1
	}
	if w.tick%2 == 0 {
		w.history.Add(w.tick, w.target)
	}
}
func (w *world) historyInfo() historyState {
	w.ensureTarget()
	first, last, _ := w.history.Bounds()
	return historyState{First: first, Last: last, Count: w.history.Len(), MaxRewind: rewindTicks}
}
func (w *world) poseAt(tick uint64) (targetState, bool) {
	w.ensureTarget()
	bracket, ok := w.history.At(tick)
	if !ok || bracket.Before.Life != bracket.After.Life {
		return targetState{}, false
	}
	pose := bracket.Before
	if bracket.AfterTick > bracket.BeforeTick {
		amount := float64(tick-bracket.BeforeTick) / float64(bracket.AfterTick-bracket.BeforeTick)
		pose.X += (bracket.After.X - pose.X) * amount
		pose.Z += (bracket.After.Z - pose.Z) * amount
	}
	return pose, true
}
func (w *world) inspect(p *peer, id, tick uint64) {
	pose, found := w.poseAt(tick)
	reply := historyReply{Type: "history-result", Protocol: 1, ID: id, Tick: tick, Found: found, History: w.historyInfo()}
	if found {
		reply.Target = &pose
	}
	data, _ := json.Marshal(reply)
	offerControl(p, data)
}
func rayHits(origin playerState, aimX, aimZ float64, target targetState) bool {
	dx, dz := aimX-origin.X, aimZ-origin.Z
	length := math.Hypot(dx, dz)
	if length < .001 {
		return false
	}
	dx /= length
	dz /= length
	tx, tz := target.X-origin.X, target.Z-origin.Z
	along := tx*dx + tz*dz
	return along >= 0 && along <= 16 && math.Abs(tx*dz-tz*dx) <= targetRadius
}
func (w *world) shoot(p *player, s shotMessage) {
	w.ensureTarget()
	if s.ID <= p.lastAction {
		if cached, ok := p.results[s.ID]; ok {
			offerControl(p.peer, cached)
		}
		return
	}
	p.lastAction = s.ID
	outcome := shotOutcome{ID: s.ID, Compensated: s.Compensate, ShotTick: w.tick, Life: w.target.Life, OriginX: p.state.X, OriginZ: p.state.Z, AimX: s.X, AimZ: s.Z, CurrentX: w.target.X, CurrentZ: w.target.Z}
	reason := ""
	used, allowed := (lagcomp.Bounded{MaxTicks: rewindTicks}).Target(w.tick, s.Tick, s.Observed)
	if s.Observed > p.lastSnapshot {
		reason = "unobserved_tick"
	} else if s.Life != w.target.Life {
		reason = "life_changed"
	} else if !allowed {
		reason = "outside_window"
	}
	past, found := w.poseAt(used)
	if reason == "" && (!found || past.Life != w.target.Life) {
		reason = "missing_history"
	}
	if reason == "" {
		outcome.Accepted = true
		outcome.UsedTick = used
		outcome.PastX = past.X
		outcome.PastZ = past.Z
		outcome.CurrentHit = rayHits(p.state, s.X, s.Z, w.target)
		outcome.HistoricalHit = rayHits(p.state, s.X, s.Z, past)
		outcome.Hit = outcome.CurrentHit
		if s.Compensate {
			outcome.Hit = outcome.HistoricalHit
		}
		if outcome.Hit {
			w.target.HP--
			if w.target.HP == 0 {
				w.resetTarget()
			}
		}
	}
	outcome.Reason = reason
	w.reply(p, actionResult{ID: s.ID, Kind: "shot", Accepted: outcome.Accepted, Reason: reason, X: s.X, Z: s.Z, Shot: &outcome})
}
