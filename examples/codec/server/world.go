package main

import (
	"context"
	"encoding/json"
	"fmt"
	"math"
	"sort"
	"time"

	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/interest"
	"github.com/win30221/gamenetkit/server/loop"
	"github.com/win30221/gamenetkit/server/netcore"
)

const tickRate = 30
const snapshotRate = 15
const inputTTL = 500 * time.Millisecond

type playerState struct {
	ID        uint64  `json:"id"`
	X         float64 `json:"x"`
	Z         float64 `json:"z"`
	Ack       uint64  `json:"ack"`
	InputTick uint64  `json:"inputTick"`
}
type stateMessage struct {
	Type         string        `json:"type"`
	Protocol     int           `json:"protocol"`
	PlayerID     uint64        `json:"playerID,omitempty"`
	Tick         uint64        `json:"tick"`
	TickRate     int           `json:"tickRate,omitempty"`
	SnapshotRate int           `json:"snapshotRate,omitempty"`
	Players      []playerState `json:"players"`
	Generation   uint32        `json:"generation"`
	Objects      []objectState `json:"objects"`
	Interest     interestState `json:"interest"`
	Codec        codecState    `json:"codec"`
}
type objectState struct {
	ID    string  `json:"id"`
	Owner uint64  `json:"owner"`
	Life  uint32  `json:"life"`
	X     float64 `json:"x"`
	Z     float64 `json:"z"`
	Kind  string  `json:"kind"`
}
type actionMessage struct {
	Type string `json:"type"`
	ID   uint64 `json:"id"`
	Kind string `json:"kind"`
}
type actionResult struct {
	Type     string  `json:"type"`
	Protocol int     `json:"protocol"`
	ID       uint64  `json:"id"`
	Kind     string  `json:"kind"`
	Accepted bool    `json:"accepted"`
	Reason   string  `json:"reason"`
	ObjectID string  `json:"objectID"`
	X        float64 `json:"x"`
	Z        float64 `json:"z"`
}
type scheduledAction struct {
	peer   *peer
	action actionMessage
	due    uint64
}
type inputMessage struct {
	Type string  `json:"type"`
	Seq  uint64  `json:"seq"`
	X    float64 `json:"x"`
	Z    float64 `json:"z"`
}
type peer struct {
	snapshots chan []byte
	controls  chan []byte
	failed    chan struct{}
	joined    chan joinResult
}
type joinResult struct {
	welcome []byte
	err     string
}
type player struct {
	state                 playerState
	peer                  *peer
	x, z                  float64
	lastInput             time.Time
	lastAction            uint64
	results               map[uint64][]byte
	resultOrder           []uint64
	interestMode          string
	radius                float64
	selector              netcore.Interest
	codecMode             string
	quantizeStep          float64
	stream                uint32
	encoder               *codec.Encoder
	decodedAck, lastFrame uint64
}
type command struct {
	kind     string
	peer     *peer
	ctx      context.Context
	input    inputMessage
	action   actionMessage
	mode     string
	radius   float64
	step     float64
	stream   uint32
	frameAck uint64
}
type world struct {
	ctx          context.Context
	cancel       context.CancelFunc
	done         chan struct{}
	commands     chan command
	players      map[*peer]*player
	nextID, tick uint64
	objects      map[uint64]objectState
	nextObject   uint64
	actions      []scheduledAction
}

func newWorld(parent context.Context) *world {
	ctx, cancel := context.WithCancel(parent)
	w := &world{ctx: ctx, cancel: cancel, done: make(chan struct{}), commands: make(chan command, 256), players: make(map[*peer]*player), objects: make(map[uint64]objectState)}
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
	case "codec":
		if p := w.players[c.peer]; p != nil {
			w.setCodec(p, c.mode, c.step)
		}
	case "frame-ack":
		if p := w.players[c.peer]; p != nil {
			w.acknowledge(p, c.stream, c.frameAck)
		}
	case "interest":
		if p := w.players[c.peer]; p != nil && (p.interestMode != c.mode || p.radius != c.radius) {
			p.interestMode = c.mode
			p.radius = c.radius
			p.selector = interest.New(c.radius, exitMargin, maxEntities)
		}
	case "action":
		p := w.players[c.peer]
		if p == nil {
			return
		}
		if c.action.ID <= p.lastAction {
			if data, ok := p.results[c.action.ID]; ok {
				offerControl(c.peer, data)
			}
			return
		}
		p.lastAction = c.action.ID
		pending := 0
		for _, a := range w.actions {
			if a.peer == c.peer {
				pending++
			}
		}
		if pending >= 8 {
			w.reply(p, actionResult{ID: c.action.ID, Kind: c.action.Kind, Reason: "busy"})
			return
		}
		w.actions = append(w.actions, scheduledAction{c.peer, c.action, w.tick + 21})
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
		p := &player{state: playerState{ID: w.nextID, X: x}, peer: c.peer, interestMode: "all", radius: 3, selector: interest.New(3, exitMargin, maxEntities)}
		w.players[c.peer] = p
		p.codecMode = "json"
		p.stream = 1
		p.encoder = codec.New()
		welcome, _ := json.Marshal(w.codecProjection(p, "welcome"))
		c.peer.joined <- joinResult{welcome: welcome}
	case "remove":
		if p := w.players[c.peer]; p != nil {
			for key, object := range w.objects {
				if object.Owner == p.state.ID {
					delete(w.objects, key)
				}
			}
		}
		remaining := w.actions[:0]
		for _, a := range w.actions {
			if a.peer != c.peer {
				remaining = append(remaining, a)
			}
		}
		w.actions = remaining
		delete(w.players, c.peer)
	case "input":
		p := w.players[c.peer]
		if p == nil || c.input.Seq <= p.state.Ack {
			return
		}
		p.state.Ack = c.input.Seq
		// Apply precedes Step: the input first participates in tick+1.
		p.state.InputTick = w.tick + 1
		p.x = c.input.X
		p.z = c.input.Z
		p.lastInput = time.Now()
	case "bump":
		if p := w.players[c.peer]; p != nil {
			p.state.X = math.Max(-8, p.state.X-1)
		}
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
	remaining := w.actions[:0]
	for _, a := range w.actions {
		if a.due > w.tick {
			remaining = append(remaining, a)
		} else if p := w.players[a.peer]; p != nil {
			w.execute(p, a.action)
		}
	}
	w.actions = remaining
	if w.tick%2 != 0 {
		return
	}
	for _, p := range w.players {
		data, err := w.encodeSnapshot(p)
		if err != nil {
			select {
			case p.peer.failed <- struct{}{}:
			default:
			}
			continue
		}
		offerLatest(p.peer.snapshots, data)
	}
}
func (w *world) objectRoster() []objectState {
	keys := make([]uint64, 0, len(w.objects))
	for key := range w.objects {
		keys = append(keys, key)
	}
	sort.Slice(keys, func(i, j int) bool { return keys[i] < keys[j] })
	out := landmarks()
	for _, key := range keys {
		out = append(out, w.objects[key])
	}
	return out
}
func offerControl(p *peer, data []byte) {
	select {
	case p.controls <- data:
	default:
		select {
		case p.failed <- struct{}{}:
		default:
		}
	}
}
func (w *world) reply(p *player, result actionResult) {
	result.Type = "action-result"
	result.Protocol = 1
	data, _ := json.Marshal(result)
	if p.results == nil {
		p.results = make(map[uint64][]byte)
	}
	p.results[result.ID] = data
	p.resultOrder = append(p.resultOrder, result.ID)
	if len(p.resultOrder) > 64 {
		delete(p.results, p.resultOrder[0])
		p.resultOrder = p.resultOrder[1:]
	}
	offerControl(p.peer, data)
}
func (w *world) execute(p *player, action actionMessage) {
	if action.Kind == "timeout" {
		return
	} // Teaching scenario: no world change and no result.
	result := actionResult{ID: action.ID, Kind: action.Kind}
	if action.Kind == "deny" {
		result.Reason = "demo_rejected"
		w.reply(p, result)
		return
	}
	count := 0
	var last uint64
	for key, o := range w.objects {
		if o.Owner == p.state.ID {
			count++
			if key > last {
				last = key
			}
		}
	}
	switch action.Kind {
	case "spawn":
		if count >= 4 {
			result.Reason = "capacity"
			break
		}
		if w.objects == nil {
			w.objects = make(map[uint64]objectState)
		}
		w.nextObject++
		dx, dz := 1.5, 1.5
		if count%2 == 1 {
			dz = -1.5
		}
		if count >= 2 {
			dx = -1.5
		}
		o := objectState{ID: fmt.Sprintf("object-%d", w.nextObject), Owner: p.state.ID, Life: 1, Kind: "marker", X: math.Max(-8, math.Min(8, p.state.X+dx)), Z: math.Max(-8, math.Min(8, p.state.Z+dz))}
		w.objects[w.nextObject] = o
		result.Accepted = true
		result.ObjectID = o.ID
		result.X = o.X
		result.Z = o.Z
	case "remove", "rebirth":
		if last == 0 {
			result.Reason = "empty"
			break
		}
		o := w.objects[last]
		result.Accepted = true
		result.ObjectID = o.ID
		result.X = o.X
		result.Z = o.Z
		if action.Kind == "remove" {
			delete(w.objects, last)
		} else {
			o.Life++
			w.objects[last] = o
		}
	default:
		result.Reason = "invalid_kind"
	}
	w.reply(p, result)
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
