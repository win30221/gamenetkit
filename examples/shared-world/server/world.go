package main

import (
	"context"
	"encoding/json"
	"math"
	"sort"
	"time"

	"github.com/win30221/gamenetkit/server/loop"
)

const tickRate = 30
const snapshotRate = 15
const inputTTL = 500 * time.Millisecond

type playerState struct {
	ID  uint64  `json:"id"`
	X   float64 `json:"x"`
	Z   float64 `json:"z"`
	Ack uint64  `json:"ack"`
}
type stateMessage struct {
	Type         string        `json:"type"`
	Protocol     int           `json:"protocol"`
	PlayerID     uint64        `json:"playerID,omitempty"`
	Tick         uint64        `json:"tick"`
	TickRate     int           `json:"tickRate,omitempty"`
	SnapshotRate int           `json:"snapshotRate,omitempty"`
	Players      []playerState `json:"players"`
}
type inputMessage struct {
	Type string  `json:"type"`
	Seq  uint64  `json:"seq"`
	X    float64 `json:"x"`
	Z    float64 `json:"z"`
}
type peer struct {
	snapshots chan []byte
	joined    chan joinResult
}
type joinResult struct {
	welcome []byte
	err     string
}
type player struct {
	state     playerState
	peer      *peer
	x, z      float64
	lastInput time.Time
}
type command struct {
	kind  string
	peer  *peer
	ctx   context.Context
	input inputMessage
}
type world struct {
	ctx          context.Context
	cancel       context.CancelFunc
	done         chan struct{}
	commands     chan command
	players      map[*peer]*player
	nextID, tick uint64
}

func newWorld(parent context.Context) *world {
	ctx, cancel := context.WithCancel(parent)
	w := &world{ctx: ctx, cancel: cancel, done: make(chan struct{}), commands: make(chan command, 256), players: make(map[*peer]*player)}
	go func() {
		defer close(w.done)
		_ = loop.Run(ctx, loop.Options[command]{Interval: time.Second / tickRate, CommandBudget: 256, Commands: w.commands, Apply: w.apply, Step: func() { w.step(time.Now()) }})
	}()
	return w
}
func (w *world) close() { w.cancel(); <-w.done }
func (w *world) submit(ctx context.Context, c command) bool {
	select {
	case <-ctx.Done():
		return false
	case <-w.done:
		return false
	case w.commands <- c:
		return true
	}
}
func (w *world) roster() []playerState {
	states := make([]playerState, 0, len(w.players))
	for _, p := range w.players {
		states = append(states, p.state)
	}
	sort.Slice(states, func(i, j int) bool { return states[i].ID < states[j].ID })
	return states
}
func (w *world) apply(c command) {
	switch c.kind {
	case "join":
		if c.ctx.Err() != nil {
			return
		}
		if len(w.players) >= 8 {
			c.peer.joined <- joinResult{err: "world_full"}
			return
		}
		w.nextID++
		x := float64(0)
		if len(w.players) == 0 {
			x = -2
		} else if len(w.players) == 1 {
			x = 2
		}
		w.players[c.peer] = &player{state: playerState{ID: w.nextID, X: x}, peer: c.peer}
		welcome, _ := json.Marshal(stateMessage{Type: "welcome", Protocol: 1, PlayerID: w.nextID, Tick: w.tick, TickRate: tickRate, SnapshotRate: snapshotRate, Players: w.roster()})
		c.peer.joined <- joinResult{welcome: welcome}
	case "remove":
		delete(w.players, c.peer)
	case "input":
		p := w.players[c.peer]
		if p == nil || c.input.Seq <= p.state.Ack {
			return
		}
		p.state.Ack = c.input.Seq
		p.x = c.input.X
		p.z = c.input.Z
		p.lastInput = time.Now()
	}
}
func (w *world) step(now time.Time) {
	w.tick++
	for _, p := range w.players {
		if now.Sub(p.lastInput) >= inputTTL {
			p.x = 0
			p.z = 0
		}
		x, z := p.x, p.z
		if length := math.Hypot(x, z); length > 1 {
			x /= length
			z /= length
		}
		p.state.X = math.Max(-8, math.Min(8, p.state.X+x*4/tickRate))
		p.state.Z = math.Max(-8, math.Min(8, p.state.Z+z*4/tickRate))
	}
	if w.tick%2 != 0 {
		return
	}
	data, _ := json.Marshal(stateMessage{Type: "snapshot", Protocol: 1, Tick: w.tick, Players: w.roster()})
	for _, p := range w.players {
		offerLatest(p.peer.snapshots, data)
	}
}
func offerLatest(queue chan []byte, data []byte) {
	select {
	case queue <- data:
		return
	default:
	}
	select {
	case <-queue:
	default:
	}
	select {
	case queue <- data:
	default:
	}
}
