package main

import (
	"context"
	"encoding/json"
	"errors"
	"math"
	"strings"
	"sync"
	"time"

	"github.com/coder/websocket"
)

type entity struct {
	ID   uint64
	Seat int
	Kind string
	X, Z float64
	HP   int
}

type player struct {
	Name                      string
	Connected, Ready, Rematch bool
	ReconnectRemaining        float64
}

type message struct {
	Type, Room, Match, Phase, Reason, Code string
	ResumeToken                            string
	Seat                                   int
	Tick, Action, UnitID, HandVersion      uint64
	Energy                                 float64
	Accepted                               bool
	Players                                [2]player
	Units, Towers                          []entity
	Hand                                   [4]string
}

type deployment struct {
	Type   string  `json:"type"`
	Match  string  `json:"match"`
	Action uint64  `json:"action"`
	Card   string  `json:"card"`
	X      float64 `json:"x"`
	Z      float64 `json:"z"`
}

type bot struct {
	conn           *websocket.Conn
	room, mode     string
	cards          map[string]card
	mu, writeMu    sync.Mutex
	once           sync.Once
	state          message
	accepted       int
	rejected       int
	action         uint64
	plays          int
	nextThink      uint64
	pending        bool
	humanSeen      bool
	ready, rematch string
	started        time.Time
}

func connectBot(ctx context.Context, server string, config gameConfig, mode string) (*bot, error) {
	hello, _ := json.Marshal(map[string]any{"type": "hello", "protocol": config.Protocol, "mode": "create", "room": "", "name": "練習電腦", "mapHash": config.MapHash, "rulesHash": config.RulesHash, "token": ""})
	if mode == "sandbox" {
		hello, _ = json.Marshal(map[string]any{"type": "hello", "protocol": config.Protocol, "mode": "create", "room": "", "name": "測試靶場（不出兵）", "mapHash": config.MapHash, "rulesHash": config.RulesHash, "token": ""})
	}
	handshake, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(handshake, "ws"+strings.TrimPrefix(server, "http")+"/ws", nil)
	if err != nil {
		return nil, err
	}
	conn.SetReadLimit(64 * 1024)
	fail := func(err error) (*bot, error) { _ = conn.CloseNow(); return nil, err }
	if err := conn.Write(handshake, websocket.MessageText, hello); err != nil {
		return fail(err)
	}
	_, data, err := conn.Read(handshake)
	if err != nil {
		return fail(err)
	}
	var welcome message
	if json.Unmarshal(data, &welcome) != nil || welcome.Type != "welcome" || welcome.Seat != 0 || len(welcome.Room) != 6 {
		return fail(errors.New("normal backend rejected the bot's create handshake"))
	}
	b := &bot{conn: conn, room: welcome.Room, mode: mode, cards: make(map[string]card), state: welcome, started: time.Now()}
	for _, card := range config.Cards {
		b.cards[card.ID] = card
	}
	return b, nil
}

func (b *bot) send(value any) error {
	data, err := json.Marshal(value)
	if err != nil {
		return err
	}
	b.writeMu.Lock()
	defer b.writeMu.Unlock()
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	return b.conn.Write(ctx, websocket.MessageText, data)
}

func (b *bot) control(kind, match string) error {
	return b.send(map[string]string{"type": kind, "match": match})
}

func (b *bot) stop() {
	b.once.Do(func() {
		b.mu.Lock()
		match := b.state.Match
		b.mu.Unlock()
		_ = b.control("leave", match)
		_ = b.conn.CloseNow()
	})
}

func (b *bot) status() any {
	b.mu.Lock()
	defer b.mu.Unlock()
	return struct {
		Room     string `json:"room"`
		Mode     string `json:"mode"`
		Match    string `json:"match"`
		Phase    string `json:"phase"`
		Seat     int    `json:"seat"`
		Tick     uint64 `json:"tick"`
		Accepted int    `json:"accepted"`
		Rejected int    `json:"rejected"`
	}{b.room, b.mode, b.state.Match, b.state.Phase, b.state.Seat, b.state.Tick, b.accepted, b.rejected}
}

func (b *bot) run(ctx context.Context) {
	defer b.stop()
	stop := context.AfterFunc(ctx, b.stop)
	defer stop()
	if err := b.control("ready", b.state.Match); err != nil {
		return
	}
	b.ready = b.state.Match
	for {
		_, data, err := b.conn.Read(ctx)
		if err != nil {
			return
		}
		var s message
		if json.Unmarshal(data, &s) != nil || s.Type == "error" {
			return
		}
		if s.Type == "action" {
			if b.pending && s.Action == b.action {
				b.mu.Lock()
				if s.Accepted {
					b.accepted++
					b.plays++
				} else {
					b.rejected++
				}
				b.mu.Unlock()
				b.pending = false
			}
			continue
		}
		if s.Type != "snapshot" && s.Type != "welcome" {
			continue
		}
		b.mu.Lock()
		fresh := s.Match != b.state.Match
		b.state = s
		b.mu.Unlock()
		if fresh {
			b.action, b.nextThink, b.plays, b.pending = 0, 0, 0, false
		}
		human := s.Players[1-s.Seat]
		b.humanSeen = b.humanSeen || human.Connected
		humanLeft := human.Name != "" && !human.Connected && human.ReconnectRemaining <= 0
		if (!b.humanSeen && time.Since(b.started) > 30*time.Second) || humanLeft || (s.Phase == "ended" && !human.Connected) {
			return
		}
		if s.Phase == "waiting" && !s.Players[s.Seat].Ready && b.ready != s.Match {
			if b.control("ready", s.Match) != nil {
				return
			}
			b.ready = s.Match
		}
		if s.Phase == "ended" && human.Connected && b.rematch != s.Match {
			if b.control("rematch", s.Match) != nil {
				return
			}
			b.rematch = s.Match
		}
		if b.mode != "practice" || b.pending || (s.Phase != "playing" && s.Phase != "overtime") || s.Tick < b.nextThink {
			continue
		}
		b.nextThink = s.Tick + 30
		if move, ok := b.choose(s); ok {
			b.action++
			move.Type, move.Match, move.Action = "deploy", s.Match, b.action
			if b.send(move) != nil {
				return
			}
			b.pending = true
			b.nextThink = s.Tick + 90
		}
	}
}

// Decisions produce ordinary deploy messages. The original backend alone
// validates hand/energy/placement and simulates movement, attacks and results.
func (b *bot) choose(s message) (deployment, bool) {
	direction := 1.0
	if s.Seat == 1 {
		direction = -1
	}
	lane := -5.0
	if b.plays%2 != 0 {
		lane = 5
	}
	var threat entity
	for _, enemy := range s.Units {
		if enemy.Seat != s.Seat && enemy.HP > 0 && (threat.ID == 0 || -enemy.Z*direction > -threat.Z*direction) {
			threat = enemy
		}
	}
	if threat.ID != 0 && -threat.Z*direction > -2 {
		lane = math.Copysign(5, threat.X)
	}
	for offset := 0; offset < len(s.Hand); offset++ {
		id := s.Hand[(b.plays+offset)%len(s.Hand)]
		card, known := b.cards[id]
		if !known || s.Energy+1e-8 < card.Cost {
			continue
		}
		move := deployment{Card: id, X: lane + .5, Z: -3.5 * direction}
		if card.Type == "spell" {
			if threat.ID != 0 {
				move.X, move.Z = threat.X, threat.Z
			} else if id == "fireball" {
				found := false
				for _, tower := range s.Towers {
					if tower.Seat != s.Seat && tower.HP > 0 && (tower.X == lane || tower.Kind == "keep") {
						move.X, move.Z, found = tower.X, tower.Z, true
						if tower.Kind == "tower" {
							break
						}
					}
				}
				if !found {
					continue
				}
			} else {
				continue
			}
		}
		return move, true
	}
	return deployment{}, false
}
