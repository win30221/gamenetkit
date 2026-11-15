package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"math"
	"os"
	"sort"
)

const tickRate = 30
const snapshotRate = 15
const maxUnits = 80
const guardRadius = .32

type point struct {
	X float64 `json:"x"`
	Z float64 `json:"z"`
}
type entity struct {
	ID           uint64  `json:"id"`
	Seat         int     `json:"seat"`
	Kind         string  `json:"kind"`
	X            float64 `json:"x"`
	Z            float64 `json:"z"`
	Radius       float64 `json:"radius"`
	HP           int     `json:"hp"`
	MaxHP        int     `json:"maxHP"`
	DX           float64 `json:"dx"`
	DZ           float64 `json:"dz"`
	AttackTick   uint64  `json:"attackTick"`
	ReadyTick    uint64  `json:"readyTick,omitempty"`
	HitTick      uint64  `json:"hitTick,omitempty"`
	HitTarget    uint64  `json:"hitTarget,omitempty"`
	Target       uint64  `json:"target"`
	LockedTarget uint64  `json:"lockedTarget,omitempty"`
	Active       bool    `json:"active"`
	Slowed       bool    `json:"slowed"`
	Charging     bool    `json:"charging"`
}
type mapDefinition struct {
	ID                 string    `json:"id"`
	Width              float64   `json:"width"`
	Length             float64   `json:"length"`
	RiverHalfWidth     float64   `json:"riverHalfWidth"`
	BridgeHalfWidth    float64   `json:"bridgeHalfWidth"`
	DeploymentCellSize float64   `json:"deploymentCellSize"`
	PocketDepth        float64   `json:"pocketDepth"`
	Bridges            []float64 `json:"bridges"`
	Towers             []entity  `json:"towers"`
	Hash               string    `json:"-"`
}

func loadMap(path string) (mapDefinition, error) {
	var m mapDefinition
	data, err := os.ReadFile(path)
	if err != nil {
		return m, err
	}
	if err = json.Unmarshal(data, &m); err != nil {
		return m, err
	}
	if m.ID != "twinbridge-1" || m.Width != 20 || m.Length != 30 || len(m.Bridges) != 2 || len(m.Towers) != 6 {
		return m, errors.New("unsupported map")
	}
	if m.DeploymentCellSize != 1 || m.PocketDepth != 4 {
		return m, errors.New("unsupported deployment geometry")
	}
	h := sha256.Sum256(data)
	m.Hash = hex.EncodeToString(h[:])
	return m, nil
}

type unit struct {
	State            entity
	Cooldown         int
	Path             []point
	Goal             uint64
	GoalPoint        point
	NextRoute        uint64
	LaneX            float64
	LockedTarget     uint64
	Travelled        float64
	SlowUntil        uint64
	SlowFactor       float64
	CooldownProgress float64
	WindupTarget     uint64
	WindupProgress   float64
}
type playerInfo struct {
	Seat               int     `json:"seat"`
	Name               string  `json:"name"`
	Ready              bool    `json:"ready"`
	Connected          bool    `json:"connected"`
	Rematch            bool    `json:"rematch"`
	ReconnectRemaining float64 `json:"reconnectRemaining"`
}
type actionResult struct {
	Type        string    `json:"type"`
	Match       string    `json:"match"`
	Action      uint64    `json:"action"`
	Accepted    bool      `json:"accepted"`
	Reason      string    `json:"reason"`
	UnitID      uint64    `json:"unitID"`
	Count       int       `json:"count"`
	Hand        [4]string `json:"hand"`
	NextCard    string    `json:"nextCard"`
	HandVersion uint64    `json:"handVersion"`
	Energy      float64   `json:"energy"`
}
type effectState struct {
	ID         uint64  `json:"id"`
	Kind       string  `json:"kind"`
	Seat       int     `json:"seat"`
	X          float64 `json:"x"`
	Z          float64 `json:"z"`
	Radius     float64 `json:"radius"`
	Start      uint64  `json:"start"`
	Impact     uint64  `json:"impact"`
	End        uint64  `json:"end"`
	Damage     int     `json:"-"`
	TowerScale float64 `json:"-"`
	Slow       float64 `json:"-"`
	Applied    bool    `json:"-"`
}
type stateMessage struct {
	Type            string        `json:"type"`
	Protocol        int           `json:"protocol"`
	Room            string        `json:"room"`
	Match           string        `json:"match"`
	MapHash         string        `json:"mapHash"`
	RulesHash       string        `json:"rulesHash"`
	Seat            int           `json:"seat"`
	Tick            uint64        `json:"tick"`
	TickRate        int           `json:"tickRate"`
	SnapshotRate    int           `json:"snapshotRate"`
	Phase           string        `json:"phase"`
	Countdown       float64       `json:"countdown"`
	Remaining       float64       `json:"remaining"`
	Energy          float64       `json:"energy"`
	Winner          int           `json:"winner"`
	Reason          string        `json:"reason"`
	Players         [2]playerInfo `json:"players"`
	Units           []entity      `json:"units"`
	Towers          []entity      `json:"towers"`
	Hand            [4]string     `json:"hand"`
	NextCard        string        `json:"nextCard"`
	HandVersion     uint64        `json:"handVersion"`
	ActionWatermark uint64        `json:"actionWatermark"`
	Effects         []effectState `json:"effects"`
	ResumeToken     string        `json:"resumeToken,omitempty"`
}
type match struct {
	ID, Room           string
	Map                mapDefinition
	Rules              rules
	Tick               uint64
	Phase              string
	Countdown, Elapsed int
	Players            [2]playerInfo
	Energy             [2]float64
	Winner             int
	Reason             string
	Units              map[uint64]*unit
	Towers             []entity
	NextID             uint64
	TowerCooldown      map[uint64]int
	TowerProgress      map[uint64]float64
	Results            [2]map[uint64]actionResult
	HighWater          [2]uint64
	Hands              [2][4]string
	Queues             [2][4]string
	HandVersion        [2]uint64
	Effects            []effectState
}

func newMatch(id, room string, m mapDefinition, r rules) *match {
	w := &match{ID: id, Room: room, Map: m, Rules: r, Phase: "waiting", Winner: -2, Units: make(map[uint64]*unit), NextID: 100, TowerCooldown: make(map[uint64]int), TowerProgress: make(map[uint64]float64)}
	w.Towers = append([]entity(nil), m.Towers...)
	for i := range w.Towers {
		hp := r.TowerHP
		if w.Towers[i].Kind == "keep" {
			hp = r.KeepHP
		}
		w.Towers[i].HP = hp
		w.Towers[i].MaxHP = hp
		w.Towers[i].Active = w.Towers[i].Kind != "keep"
	}
	for i := 0; i < 2; i++ {
		w.Energy[i] = 5
		w.Players[i].Seat = i
		w.Results[i] = make(map[uint64]actionResult)
		for j := 0; j < 4; j++ {
			w.Hands[i][j] = r.Cards[j].ID
			w.Queues[i][j] = r.Cards[j+4].ID
		}
	}
	return w
}
func (w *match) ready(seat int) {
	if w.Phase != "waiting" {
		return
	}
	w.Players[seat].Ready = true
	if w.Players[0].Connected && w.Players[1].Connected && w.Players[0].Ready && w.Players[1].Ready {
		w.Phase = "countdown"
		w.Countdown = 2 * tickRate
	}
}
func (w *match) deploy(seat int, matchID string, action uint64, card string, x, z float64) actionResult {
	r := actionResult{Type: "action", Match: matchID, Action: action, Hand: w.Hands[seat], NextCard: w.Queues[seat][0], HandVersion: w.HandVersion[seat], Energy: w.Energy[seat]}
	if matchID != w.ID {
		r.Reason = "stale_match"
		return r
	}
	if old, ok := w.Results[seat][action]; ok {
		return old
	}
	if action == 0 || action > 9007199254740991 || action <= w.HighWater[seat] {
		r.Reason = "stale_action"
		return r
	}
	w.HighWater[seat] = action
	definition, known := w.Rules.ByID[card]
	spots := formation(seat, x, z, definition.Count)
	legal := finite(x) && finite(z)
	if definition.Type == "spell" {
		legal = legal && math.Abs(x) <= w.Map.Width/2 && math.Abs(z) <= w.Map.Length/2
	} else {
		for _, p := range spots {
			if !w.deploymentPoint(seat, p) {
				legal = false
			}
		}
	}
	slot := -1
	for i, id := range w.Hands[seat] {
		if id == card {
			slot = i
			break
		}
	}
	switch {
	case w.Phase != "playing" && w.Phase != "overtime":
		r.Reason = "not_playing"
	case !known:
		r.Reason = "unknown_card"
	case !legal:
		r.Reason = "invalid_placement"
	case w.Energy[seat]+1e-8 < definition.Cost:
		r.Reason = "not_enough_energy"
	case slot < 0:
		r.Reason = "card_not_in_hand"
	case definition.Type == "troop" && len(w.Units)+definition.Count > maxUnits:
		r.Reason = "unit_limit"
	case definition.Type == "spell" && len(w.Effects) >= 16:
		r.Reason = "effect_limit"
	default:
		direction := 1.0
		if seat == 1 {
			direction = -1
		}
		if definition.Type == "spell" {
			w.NextID++
			w.Effects = append(w.Effects, effectState{ID: w.NextID, Kind: card, Seat: seat, X: x, Z: z, Radius: definition.Radius, Start: w.Tick, Impact: w.Tick + uint64(definition.DelayTicks) + 1, End: w.Tick + uint64(definition.DelayTicks+definition.DurationTicks) + 1, Damage: definition.Damage, TowerScale: definition.TowerScale, Slow: definition.Slow})
			r.UnitID = w.NextID
		} else {
			readyTick := uint64(0)
			if definition.DeployTicks > 0 {
				readyTick = w.Tick + uint64(definition.DeployTicks)
			}
			for _, p := range spots {
				w.NextID++
				w.Units[w.NextID] = &unit{State: entity{ID: w.NextID, Seat: seat, Kind: card, X: p.X, Z: p.Z, Radius: guardRadius, HP: definition.HP, MaxHP: definition.HP, DZ: direction, ReadyTick: readyTick}, LaneX: w.nearestBridge(seat, x)}
				if r.UnitID == 0 {
					r.UnitID = w.NextID
				}
			}
		}
		w.Energy[seat] -= definition.Cost
		w.Hands[seat][slot] = w.Queues[seat][0]
		copy(w.Queues[seat][:3], w.Queues[seat][1:])
		w.Queues[seat][3] = card
		w.HandVersion[seat]++
		r.Accepted = true
		r.Count = definition.Count
	}
	r.Hand = w.Hands[seat]
	r.NextCard = w.Queues[seat][0]
	r.HandVersion = w.HandVersion[seat]
	r.Energy = w.Energy[seat]
	w.Results[seat][action] = r
	// Monotonic high-water mark prevents replay even after bounded result eviction.
	if len(w.Results[seat]) > 128 {
		oldest := action
		for id := range w.Results[seat] {
			if id < oldest {
				oldest = id
			}
		}
		delete(w.Results[seat], oldest)
	}
	return r
}
func (w *match) snapshot(seat int, kind string) stateMessage {
	units := make([]entity, 0, len(w.Units))
	for _, u := range w.sortedUnits() {
		state := u.State
		state.LockedTarget = u.LockedTarget
		units = append(units, state)
	}
	towers := append([]entity(nil), w.Towers...)
	for i := range towers {
		towers[i].LockedTarget = towers[i].Target
	}
	remaining := math.Max(0, float64(w.Rules.Duration-w.Elapsed)/tickRate)
	if w.Phase == "overtime" {
		remaining = math.Max(0, float64(w.Rules.Duration+60*tickRate-w.Elapsed)/tickRate)
	}
	return stateMessage{Type: kind, Protocol: protocolVersion, Room: w.Room, Match: w.ID, MapHash: w.Map.Hash, RulesHash: w.Rules.Hash, Seat: seat, Tick: w.Tick, TickRate: tickRate, SnapshotRate: snapshotRate, Phase: w.Phase, Countdown: float64(w.Countdown) / tickRate, Remaining: remaining, Energy: w.Energy[seat], Winner: w.Winner, Reason: w.Reason, Players: w.Players, Units: units, Towers: towers, Hand: w.Hands[seat], NextCard: w.Queues[seat][0], HandVersion: w.HandVersion[seat], ActionWatermark: w.HighWater[seat], Effects: append([]effectState{}, w.Effects...)}
}
func (w *match) sortedUnits() []*unit {
	list := make([]*unit, 0, len(w.Units))
	for _, u := range w.Units {
		list = append(list, u)
	}
	sort.Slice(list, func(i, j int) bool { return list[i].State.ID < list[j].State.ID })
	return list
}
func finite(n float64) bool       { return !math.IsNaN(n) && !math.IsInf(n, 0) }
func position(e entity) point     { return point{e.X, e.Z} }
func distance(a, b point) float64 { return math.Hypot(a.X-b.X, a.Z-b.Z) }
