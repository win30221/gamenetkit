package main

import (
	"context"
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"math"
	"time"

	"github.com/win30221/gamenetkit/server/loop"
)

type helloMessage struct {
	Type      string `json:"type"`
	Protocol  int    `json:"protocol"`
	Mode      string `json:"mode"`
	Room      string `json:"room"`
	Name      string `json:"name"`
	MapHash   string `json:"mapHash"`
	RulesHash string `json:"rulesHash"`
	Token     string `json:"token"`
}
type deployMessage struct {
	Type   string  `json:"type"`
	Match  string  `json:"match"`
	Action uint64  `json:"action"`
	Card   string  `json:"card"`
	X      float64 `json:"x"`
	Z      float64 `json:"z"`
}
type command struct {
	Kind   string
	Peer   *peer
	Hello  helloMessage
	Deploy deployMessage
	Match  string
	Ctx    context.Context
}
type joinResult struct {
	Welcome []byte
	Error   string
}
type peer struct {
	Snapshots chan []byte
	Reliable  chan []byte
	Joined    chan joinResult
	Cancel    context.CancelFunc
}
type room struct {
	Game   *match
	Peers  [2]*peer
	Tokens [2]string
	Until  [2]uint64
	Tick   uint64
}

const reconnectTicks = 30 * tickRate

func newResumeToken() string {
	var data [32]byte
	if _, err := rand.Read(data[:]); err != nil {
		return ""
	}
	return base64.RawURLEncoding.EncodeToString(data[:])
}
func (e *engine) welcome(r *room, seat int) []byte {
	state := r.Game.snapshot(seat, "welcome")
	state.ResumeToken = r.Tokens[seat]
	data, _ := json.Marshal(state)
	return data
}

type seatRef struct {
	Room *room
	Seat int
}
type engine struct {
	Ctx       context.Context
	cancel    context.CancelFunc
	done      chan struct{}
	Commands  chan command
	Map       mapDefinition
	Rules     rules
	Rooms     map[string]*room
	Seats     map[*peer]seatRef
	nextMatch uint64
}

func newEngine(parent context.Context, m mapDefinition, config rules) *engine {
	ctx, cancel := context.WithCancel(parent)
	e := &engine{Ctx: ctx, cancel: cancel, done: make(chan struct{}), Commands: make(chan command, 256), Map: m, Rules: config, Rooms: make(map[string]*room), Seats: make(map[*peer]seatRef)}
	go func() {
		defer close(e.done)
		_ = loop.Run(ctx, loop.Options[command]{Interval: time.Second / tickRate, CommandBudget: 128, Commands: e.Commands, Apply: e.apply, Step: e.step})
	}()
	return e
}
func (e *engine) close() { e.cancel(); <-e.done }
func (e *engine) submit(ctx context.Context, c command) bool {
	select {
	case <-ctx.Done():
		return false
	case <-e.Ctx.Done():
		return false
	case e.Commands <- c:
		return true
	}
}
func (e *engine) roomCode() string {
	const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
	for attempts := 0; attempts < 16; attempts++ {
		var data [6]byte
		if _, err := rand.Read(data[:]); err != nil {
			return ""
		}
		for i := range data {
			data[i] = alphabet[int(data[i])%len(alphabet)]
		}
		code := string(data[:])
		if e.Rooms[code] == nil {
			return code
		}
	}
	return ""
}
func (e *engine) apply(c command) {
	switch c.Kind {
	case "join":
		if c.Ctx.Err() != nil {
			return
		}
		fail := func(code string) { c.Peer.Joined <- joinResult{Error: code} }
		if c.Hello.MapHash != e.Map.Hash {
			fail("map_mismatch")
			return
		}
		if c.Hello.RulesHash != e.Rules.Hash {
			fail("rules_mismatch")
			return
		}
		var r *room
		seat := 0
		if c.Hello.Mode == "create" {
			if len(e.Rooms) >= 32 {
				fail("server_full")
				return
			}
			code := e.roomCode()
			if code == "" {
				fail("room_creation_failed")
				return
			}
			e.nextMatch++
			r = &room{Game: newMatch(fmt.Sprintf("%s-%x", code, e.nextMatch), code, e.Map, e.Rules)}
			e.Rooms[code] = r
		} else if c.Hello.Mode == "resume" {
			r = e.Rooms[c.Hello.Room]
			if r == nil || len(c.Hello.Token) != 43 {
				fail("resume_expired")
				return
			}
			seat = -1
			for i, token := range r.Tokens {
				if token != "" && subtle.ConstantTimeCompare([]byte(c.Hello.Token), []byte(token)) == 1 {
					seat = i
					break
				}
			}
			if seat < 0 || (r.Until[seat] > 0 && r.Tick >= r.Until[seat]) {
				fail("resume_expired")
				return
			}
			if r.Peers[seat] != nil {
				fail("seat_connected")
				return
			}
		} else {
			r = e.Rooms[c.Hello.Room]
			if r == nil {
				fail("room_not_found")
				return
			}
			if r.Game.Phase != "waiting" {
				fail("room_started")
				return
			}
			seat = -1
			reserved := false
			for i := range r.Peers {
				if r.Peers[i] == nil {
					if r.Tokens[i] == "" {
						seat = i
						break
					}
					reserved = true
				}
			}
			if seat < 0 {
				if reserved {
					fail("seat_reserved")
				} else {
					fail("room_full")
				}
				return
			}
		}
		if c.Hello.Mode != "resume" {
			r.Tokens[seat] = newResumeToken()
			if r.Tokens[seat] == "" {
				fail("room_creation_failed")
				return
			}
			r.Game.Players[seat] = playerInfo{Seat: seat, Name: c.Hello.Name}
		}
		r.Peers[seat] = c.Peer
		r.Until[seat] = 0
		r.Game.Players[seat].Connected = true
		r.Game.Players[seat].ReconnectRemaining = 0
		e.Seats[c.Peer] = seatRef{r, seat}
		c.Peer.Joined <- joinResult{Welcome: e.welcome(r, seat)}
	case "remove", "leave":
		ref, ok := e.Seats[c.Peer]
		if !ok {
			return
		}
		delete(e.Seats, c.Peer)
		r := ref.Room
		r.Peers[ref.Seat] = nil
		r.Game.Players[ref.Seat].Connected = false
		r.Until[ref.Seat] = r.Tick + reconnectTicks
		if c.Kind == "leave" {
			r.Tokens[ref.Seat] = ""
			r.Until[ref.Seat] = r.Tick
			r.Game.Players[ref.Seat].Ready = false
			if r.Game.Phase != "waiting" {
				r.Game.end(1-ref.Seat, "opponent_left")
			}
			if c.Peer.Cancel != nil {
				c.Peer.Cancel()
			}
		}
		if r.Peers[0] == nil && r.Peers[1] == nil && r.Tokens[0] == "" && r.Tokens[1] == "" {
			delete(e.Rooms, r.Game.Room)
		}
	case "ready":
		if ref, ok := e.Seats[c.Peer]; ok && ref.Room.Game.ID == c.Match {
			ref.Room.Game.ready(ref.Seat)
		}
	case "rematch":
		ref, ok := e.Seats[c.Peer]
		if !ok || ref.Room.Game.ID != c.Match || ref.Room.Game.Phase != "ended" {
			return
		}
		r := ref.Room
		r.Game.Players[ref.Seat].Rematch = true
		if r.Peers[0] != nil && r.Peers[1] != nil && r.Game.Players[0].Rematch && r.Game.Players[1].Rematch {
			e.nextMatch++
			next := newMatch(fmt.Sprintf("%s-%x", r.Game.Room, e.nextMatch), r.Game.Room, e.Map, e.Rules)
			for i := range next.Players {
				next.Players[i].Name = r.Game.Players[i].Name
				next.Players[i].Connected = true
			}
			r.Game = next
			for i, p := range r.Peers {
				select {
				case <-p.Snapshots:
				default:
				}
				select {
				case p.Reliable <- e.welcome(r, i):
				default:
					p.Cancel()
				}
			}
		}
	case "deploy":
		ref, ok := e.Seats[c.Peer]
		if !ok {
			return
		}
		d := c.Deploy
		result := ref.Room.Game.deploy(ref.Seat, d.Match, d.Action, d.Card, d.X, d.Z)
		data, _ := json.Marshal(result)
		select {
		case c.Peer.Reliable <- data:
		default:
			c.Peer.Cancel()
		}
	}
}
func (e *engine) step() {
	for _, r := range e.Rooms {
		r.Tick++
		expired := [2]bool{}
		for i := range r.Peers {
			if r.Peers[i] == nil && r.Tokens[i] != "" && r.Until[i] > 0 {
				r.Game.Players[i].ReconnectRemaining = math.Max(0, float64(r.Until[i])-float64(r.Tick)) / tickRate
				expired[i] = r.Tick >= r.Until[i]
			}
		}
		if r.Peers[0] == nil && r.Peers[1] == nil {
			if (r.Tokens[0] == "" || expired[0]) && (r.Tokens[1] == "" || expired[1]) {
				r.Game.end(-1, "both_offline")
				delete(e.Rooms, r.Game.Room)
				continue
			}
		} else {
			for i := range expired {
				if expired[i] {
					r.Game.end(1-i, "reconnect_timeout")
					r.Tokens[i] = ""
				}
			}
		}
		r.Game.step()
		if r.Game.Tick%2 != 0 {
			continue
		}
		for seat, p := range r.Peers {
			if p == nil {
				continue
			}
			data, _ := json.Marshal(r.Game.snapshot(seat, "snapshot"))
			if len(data) > 32*1024 {
				p.Cancel()
				continue
			}
			offerLatest(p.Snapshots, data)
		}
	}
}
func offerLatest(ch chan []byte, data []byte) {
	select {
	case ch <- data:
		return
	default:
	}
	select {
	case <-ch:
	default:
	}
	select {
	case ch <- data:
	default:
	}
}
