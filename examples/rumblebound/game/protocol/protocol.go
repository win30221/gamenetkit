// Package protocol defines the battle wire contract. No client-supplied position,
// health or score is accepted. IDs and sequence numbers stay in JSON's safe range.
package protocol

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"math"
)

const MaxMessage = 4096
const MaxDatagram = 1200
const MaxSnapshot = 16 * 1024

type Client struct {
	Net             *NetOptions `json:"net,omitempty"`
	FrameAck        uint64      `json:"frameAck,omitempty"`
	ViewTick        uint64      `json:"viewTick,omitempty"`
	ObservedTick    uint64      `json:"observedTick,omitempty"`
	Type            string      `json:"type"`
	Protocol        int         `json:"protocol"`
	Content         string      `json:"content"`
	MatchID         string      `json:"matchID"`
	MapHash         string      `json:"mapHash,omitempty"`
	Ticket          string      `json:"ticket,omitempty"`
	Generation      uint32      `json:"generation,omitempty"`
	Seq             uint64      `json:"seq,omitempty"`
	X               float64     `json:"x,omitempty"`
	Z               float64     `json:"z,omitempty"`
	Echo            float64     `json:"echo,omitempty"`
	Action          string      `json:"action,omitempty"`
	TargetID        string      `json:"targetID,omitempty"`
	TargetLife      uint64      `json:"targetLife,omitempty"`
	ActionID        uint64      `json:"actionID,omitempty"`
	ActionTick      uint64      `json:"actionTick,omitempty"`
	AimX            float64     `json:"aimX,omitempty"`
	AimZ            float64     `json:"aimZ,omitempty"`
	HasGroundTarget bool        `json:"hasGroundTarget,omitempty"`
	TargetX         float64     `json:"targetX,omitempty"`
	TargetZ         float64     `json:"targetZ,omitempty"`
	FacingX         float64     `json:"facingX,omitempty"`
	FacingZ         float64     `json:"facingZ,omitempty"`
}

type Player struct {
	BasicReadyTick      uint64  `json:"basicReadyTick,omitempty"`
	AttackReleaseTick   uint64  `json:"attackReleaseTick,omitempty"`
	AttackTargetID      string  `json:"attackTargetID,omitempty"`
	AttackTargetLife    uint64  `json:"attackTargetLife,omitempty"`
	BasicActionID       uint64  `json:"basicActionID,omitempty"`
	Life                uint64  `json:"life,omitempty"`
	FireActionID        uint64  `json:"fireActionID,omitempty"`
	PlayerID            string  `json:"playerID"`
	EntityID            string  `json:"entityID"`
	TeamID              string  `json:"teamID,omitempty"`
	HeroID              string  `json:"heroID,omitempty"`
	X                   float64 `json:"x"`
	Z                   float64 `json:"z"`
	FacingX             float64 `json:"facingX,omitempty"`
	FacingZ             float64 `json:"facingZ,omitempty"`
	Ack                 uint64  `json:"ack"`
	Online              bool    `json:"online"`
	Left                bool    `json:"left,omitempty"`
	Bot                 bool    `json:"bot,omitempty"`
	HP                  int     `json:"hp,omitempty"`
	FireReadyTick       uint64  `json:"fireReadyTick,omitempty"`
	AttackStopUntilTick uint64  `json:"attackStopUntilTick,omitempty"`
	DashReadyTick       uint64  `json:"dashReadyTick,omitempty"`
	RespawnTick         uint64  `json:"respawnTick,omitempty"`
	ProtectedUntilTick  uint64  `json:"protectedUntilTick,omitempty"`
	DashX               float64 `json:"dashX,omitempty"`
	DashZ               float64 `json:"dashZ,omitempty"`
	DashEndTick         uint64  `json:"dashEndTick,omitempty"`
}

type Projectile struct {
	ActionID    uint64  `json:"actionID,omitempty"`
	ID          string  `json:"id"`
	OwnerID     string  `json:"ownerID"`
	TeamID      string  `json:"teamID"`
	X           float64 `json:"x"`
	Z           float64 `json:"z"`
	DX          float64 `json:"dx"`
	DZ          float64 `json:"dz"`
	ExpiresTick uint64  `json:"expiresTick"`
}

// ProjectileEnd is an authoritative terminal pose. It is repeated briefly so
// presentation can finish its delayed trajectory before releasing the visual.
type ProjectileEnd struct {
	ID       string  `json:"id"`
	X        float64 `json:"x"`
	Z        float64 `json:"z"`
	Tick     uint64  `json:"tick"`
	Kind     string  `json:"kind"`
	TargetID string  `json:"targetID,omitempty"`
	OwnerID  string  `json:"ownerID"`
}

// CombatEvent is a short-lived authoritative melee presentation event. It does
// not create a projectile and its ID deduplicates repeated snapshots.
type CombatEvent struct {
	ActionID uint64  `json:"actionID,omitempty"`
	ID       string  `json:"id"`
	Kind     string  `json:"kind"`
	OwnerID  string  `json:"ownerID"`
	TargetID string  `json:"targetID,omitempty"`
	X        float64 `json:"x"`
	Z        float64 `json:"z"`
	DX       float64 `json:"dx"`
	DZ       float64 `json:"dz"`
	Tick     uint64  `json:"tick"`
}

// MatchState is server-owned and is included in every private-match message.
// ScoreTicks is the exact accumulated score in 1/tickRate point units, while
// Score is its completed whole-point display value.
type MatchState struct {
	Phase         string        `json:"phase"`
	EndsAtTick    uint64        `json:"endsAtTick"`
	Point         ControlPoint  `json:"point"`
	RotationTick  uint64        `json:"rotationTick"`
	NextPoint     *ControlPoint `json:"nextPoint,omitempty"`
	PointStatus   string        `json:"pointStatus"`
	ScoringTeamID string        `json:"scoringTeamID,omitempty"`
	Scores        []TeamScore   `json:"scores"`
	Result        *MatchResult  `json:"result,omitempty"`
}

type ControlPoint struct {
	Index  int     `json:"index"`
	X      float64 `json:"x"`
	Z      float64 `json:"z"`
	Radius float64 `json:"radius"`
}

type TeamScore struct {
	TeamID     string `json:"teamID"`
	Score      uint64 `json:"score"`
	ScoreTicks uint64 `json:"scoreTicks"`
	Forfeited  bool   `json:"forfeited"`
}

type MatchResult struct {
	Outcome      string `json:"outcome"`
	Reason       string `json:"reason"`
	WinnerTeamID string `json:"winnerTeamID,omitempty"`
	EndedTick    uint64 `json:"endedTick"`
}

type GroundCast struct {
	ActionID      uint64  `json:"actionID,omitempty"`
	ID            string  `json:"id"`
	OwnerID       string  `json:"ownerID"`
	TeamID        string  `json:"teamID"`
	X             float64 `json:"x"`
	Z             float64 `json:"z"`
	Radius        float64 `json:"radius"`
	StartTick     uint64  `json:"startTick"`
	FallStartTick uint64  `json:"fallStartTick"`
	ImpactTick    uint64  `json:"impactTick"`
}
type GroundImpact struct {
	ID      string   `json:"id"`
	OwnerID string   `json:"ownerID"`
	X       float64  `json:"x"`
	Z       float64  `json:"z"`
	Radius  float64  `json:"radius"`
	Tick    uint64   `json:"tick"`
	Targets []string `json:"targets,omitempty"`
}

type Server struct {
	Type           string          `json:"type"`
	ClockTick      uint64          `json:"clockTick,omitempty"`
	ClockValid     bool            `json:"clockValid,omitempty"`
	Tech           *TechState      `json:"tech,omitempty"`
	Code           string          `json:"code,omitempty"`
	Protocol       int             `json:"protocol"`
	Content        string          `json:"content"`
	MatchID        string          `json:"matchID"`
	MapHash        string          `json:"mapHash,omitempty"`
	Practice       bool            `json:"practice,omitempty"`
	BotMode        string          `json:"botMode,omitempty"`
	Tick           uint64          `json:"tick"`
	Generation     uint32          `json:"generation"`
	PlayerID       string          `json:"playerID"`
	Players        []Player        `json:"players"`
	Projectiles    []Projectile    `json:"projectiles,omitempty"`
	ProjectileEnds []ProjectileEnd `json:"projectileEnds,omitempty"`
	GroundCasts    []GroundCast    `json:"groundCasts,omitempty"`
	GroundImpacts  []GroundImpact  `json:"groundImpacts,omitempty"`
	CombatEvents   []CombatEvent   `json:"combatEvents,omitempty"`
	Echo           float64         `json:"echo"`
	InputTick      uint64          `json:"inputTick"`
	ActionAck      uint64          `json:"actionAck,omitempty"`
	ActionCode     string          `json:"actionCode,omitempty"`
	Match          *MatchState     `json:"match,omitempty"`
}

func Decode(b []byte) (Client, error) {
	var c Client
	if len(b) > MaxMessage {
		return c, errors.New("message_too_large")
	}
	d := json.NewDecoder(bytes.NewReader(b))
	d.DisallowUnknownFields()
	if err := d.Decode(&c); err != nil {
		return c, errors.New("invalid_message")
	}
	if d.Decode(new(any)) != io.EOF {
		return c, errors.New("trailing_data")
	}
	if c.FrameAck > 1<<53-1 || c.ViewTick > 1<<53-1 || c.ObservedTick > 1<<53-1 || c.Seq > 1<<53-1 || c.ActionID > 1<<53-1 || c.ActionTick > 1<<53-1 ||
		math.IsNaN(c.X) || math.IsNaN(c.Z) || math.IsInf(c.X, 0) || math.IsInf(c.Z, 0) || math.Abs(c.X) > 1 || math.Abs(c.Z) > 1 ||
		math.IsNaN(c.AimX) || math.IsNaN(c.AimZ) || math.IsInf(c.AimX, 0) || math.IsInf(c.AimZ, 0) || math.Abs(c.AimX) > 1 || math.Abs(c.AimZ) > 1 ||
		math.IsNaN(c.FacingX) || math.IsNaN(c.FacingZ) || math.IsInf(c.FacingX, 0) || math.IsInf(c.FacingZ, 0) || math.Abs(c.FacingX) > 1 || math.Abs(c.FacingZ) > 1 ||
		math.IsNaN(c.TargetX) || math.IsNaN(c.TargetZ) || math.IsInf(c.TargetX, 0) || math.IsInf(c.TargetZ, 0) || math.Abs(c.TargetX) > 100 || math.Abs(c.TargetZ) > 100 ||
		c.Echo < 0 || c.Echo > 1e15 {
		return c, errors.New("invalid_input")
	}
	if c.TargetLife > 1<<53-1 || len(c.TargetID) > 32 || (c.Action != "attack" && (c.TargetID != "" || c.TargetLife != 0)) ||
		(c.Action == "attack" && c.TargetID == "") ||
		(c.HasGroundTarget && c.Action != "fire" && c.Action != "skill") || (!c.HasGroundTarget && (c.TargetX != 0 || c.TargetZ != 0)) {
		return c, errors.New("invalid_input")
	}
	if c.Action == "" {
		if c.ActionID != 0 || c.ActionTick != 0 || c.AimX != 0 || c.AimZ != 0 {
			return c, errors.New("invalid_input")
		}
	} else if (c.Action != "fire" && c.Action != "dash" && c.Action != "skill" && c.Action != "attack" && c.Action != "cancel") || c.ActionID == 0 {
		return c, errors.New("invalid_input")
	}
	return c, nil
}
