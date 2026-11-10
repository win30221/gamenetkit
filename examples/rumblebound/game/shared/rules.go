package shared

import (
	_ "embed"
	"encoding/json"
	"fmt"
	"strings"
)

//go:embed rules.json
var rulesJSON []byte

//go:embed movement-cases.json
var MovementCases []byte

type Config struct {
	Protocol                int             `json:"protocol"`
	Content                 string          `json:"content"`
	TickRate                int             `json:"tickRate"`
	SnapshotRate            int             `json:"snapshotRate"`
	MaxPlayers              int             `json:"maxPlayers"`
	Speed                   float64         `json:"speed"`
	ArenaHalfSize           float64         `json:"arenaHalfSize"`
	PlayerRadius            float64         `json:"playerRadius"`
	InputTimeoutTicks       uint64          `json:"inputTimeoutTicks"`
	ReconnectTicks          uint64          `json:"reconnectTicks"`
	MaxHP                   int             `json:"maxHP"`
	MaxActiveGroundCasts    int             `json:"maxActiveGroundCasts"`
	MaxProjectiles          int             `json:"maxProjectiles"`
	ProjectileSpeed         float64         `json:"projectileSpeed"`
	ProjectileRadius        float64         `json:"projectileRadius"`
	ProjectileDamage        int             `json:"projectileDamage"`
	ProjectileLifetimeTicks uint64          `json:"projectileLifetimeTicks"`
	FireCooldownTicks       uint64          `json:"fireCooldownTicks"`
	DashSpeed               float64         `json:"dashSpeed"`
	DashTicks               uint64          `json:"dashTicks"`
	DashCooldownTicks       uint64          `json:"dashCooldownTicks"`
	RespawnTicks            uint64          `json:"respawnTicks"`
	LoadTimeoutSeconds      int             `json:"loadTimeoutSeconds"`
	MatchDurationTicks      uint64          `json:"matchDurationTicks"`
	ScoreGoal               uint64          `json:"scoreGoal"`
	PointRotationTicks      uint64          `json:"pointRotationTicks"`
	PointWarningTicks       uint64          `json:"pointWarningTicks"`
	ControlPointRadius      float64         `json:"controlPointRadius"`
	ControlPoints           []Point         `json:"controlPoints"`
	Heroes                  []HeroConfig    `json:"heroes"`
	Collision               CollisionConfig `json:"collision"`
}

type CollisionConfig struct {
	PushSpeed     float64 `json:"pushSpeed"`
	DashPushSpeed float64 `json:"dashPushSpeed"`
	PushStrength  float64 `json:"pushStrength"`
	BodyMass      float64 `json:"bodyMass"`
	Iterations    int     `json:"iterations"`
}

type HeroConfig struct {
	BasicRange              float64 `json:"basicRange,omitempty"`
	BasicDamage             int     `json:"basicDamage,omitempty"`
	BasicWindupTicks        uint64  `json:"basicWindupTicks,omitempty"`
	BasicCooldownTicks      uint64  `json:"basicCooldownTicks,omitempty"`
	HeroID                  string  `json:"heroID"`
	AttackKind              string  `json:"attackKind"`
	CastRange               float64 `json:"castRange,omitempty"`
	ImpactRadius            float64 `json:"impactRadius,omitempty"`
	GroundDamage            int     `json:"groundDamage,omitempty"`
	CastWindupTicks         uint64  `json:"castWindupTicks,omitempty"`
	FallTicks               uint64  `json:"fallTicks,omitempty"`
	MeleeRange              float64 `json:"meleeRange,omitempty"`
	MeleeDamage             int     `json:"meleeDamage,omitempty"`
	MeleeArcDegrees         float64 `json:"meleeArcDegrees,omitempty"`
	MaxHP                   int     `json:"maxHP"`
	Speed                   float64 `json:"speed"`
	ProjectileDamage        int     `json:"projectileDamage"`
	ProjectileSpeed         float64 `json:"projectileSpeed"`
	ProjectileLifetimeTicks uint64  `json:"projectileLifetimeTicks"`
	FireCooldownTicks       uint64  `json:"fireCooldownTicks"`
	AttackStopTicks         uint64  `json:"attackStopTicks"`
	DashSpeed               float64 `json:"dashSpeed"`
	DashCooldownTicks       uint64  `json:"dashCooldownTicks"`
}

func (c Config) Hero(id string) (HeroConfig, bool) {
	for _, hero := range c.Heroes {
		if hero.HeroID == id {
			return hero, true
		}
	}
	return HeroConfig{}, false
}

type Point struct {
	X float64 `json:"x"`
	Z float64 `json:"z"`
}

// Rules returns an independent copy of the validated, embedded definition.
// Parsing/validation happens once at startup; a match or test may tune its copy
// without changing another world or the HTTP rules response.
var defaultRules = mustLoadRules()

func mustLoadRules() Config {
	var c Config
	if err := json.Unmarshal(rulesJSON, &c); err != nil {
		panic(err)
	}
	if err := ValidateConfig(c); err != nil {
		panic(fmt.Errorf("invalid embedded rules: %w", err))
	}
	return c
}

func Rules() Config { return defaultRules.Clone() }

func (c Config) Clone() Config {
	c.ControlPoints = append([]Point(nil), c.ControlPoints...)
	c.Heroes = append([]HeroConfig(nil), c.Heroes...)
	return c
}

// ValidateConfig checks the invariants used by the fixed-step simulation and
// its bounded wire protocol. Reject invalid content before starting goroutines,
// rather than discovering a zero divisor or unbounded workload in a battle.
func ValidateConfig(c Config) error {
	if c.Protocol < 1 || c.Protocol > 1_000_000 || len(c.Content) == 0 || len(c.Content) > 64 || strings.TrimSpace(c.Content) != c.Content {
		return fmt.Errorf("invalid protocol/content identity")
	}
	if c.TickRate < 1 || c.TickRate > 240 || c.SnapshotRate < 1 || c.SnapshotRate > c.TickRate || c.TickRate%c.SnapshotRate != 0 {
		return fmt.Errorf("snapshotRate must be a positive divisor of tickRate (1..240)")
	}
	if c.MaxPlayers < 2 || c.MaxPlayers > 8 || c.MaxProjectiles < 1 || c.MaxProjectiles > 24 || c.MaxActiveGroundCasts < 1 || c.MaxActiveGroundCasts > 8 {
		return fmt.Errorf("player/projectile capacity exceeds the supported snapshot budget")
	}
	positive := func(v, maximum float64) bool { return finite(v) && v > 0 && v <= maximum }
	if !positive(c.ArenaHalfSize, 100) || c.ArenaHalfSize < 4 || !positive(c.PlayerRadius, 2) || !positive(c.ProjectileRadius, 2) ||
		!positive(c.Speed, 1000) || !positive(c.ProjectileSpeed, 1000) || !positive(c.DashSpeed, 1000) || !positive(c.ControlPointRadius, c.ArenaHalfSize) {
		return fmt.Errorf("invalid movement/arena dimensions")
	}
	if c.MaxHP < 1 || c.MaxHP > 100_000 || c.ProjectileDamage < 1 || c.ProjectileDamage > 100_000 {
		return fmt.Errorf("invalid health/damage")
	}
	maxTicks := uint64(c.TickRate) * 3600
	for name, value := range map[string]uint64{
		"inputTimeoutTicks": c.InputTimeoutTicks, "reconnectTicks": c.ReconnectTicks,
		"projectileLifetimeTicks": c.ProjectileLifetimeTicks, "fireCooldownTicks": c.FireCooldownTicks,
		"dashTicks": c.DashTicks, "dashCooldownTicks": c.DashCooldownTicks, "respawnTicks": c.RespawnTicks,
		"matchDurationTicks": c.MatchDurationTicks, "pointRotationTicks": c.PointRotationTicks,
	} {
		if value == 0 || value > maxTicks {
			return fmt.Errorf("%s must be between 1 tick and one hour", name)
		}
	}
	if c.PointWarningTicks >= c.PointRotationTicks || c.DashTicks > c.DashCooldownTicks || c.InputTimeoutTicks > c.ReconnectTicks || c.LoadTimeoutSeconds < 1 || c.LoadTimeoutSeconds > 300 || c.ScoreGoal < 1 || c.ScoreGoal > 1_000_000 {
		return fmt.Errorf("invalid duration/score relationships")
	}
	if len(c.ControlPoints) == 0 || len(c.ControlPoints) > 16 {
		return fmt.Errorf("invalid control point count")
	}
	limit := c.ArenaHalfSize - c.PlayerRadius
	for _, p := range c.ControlPoints {
		if !finite(p.X) || !finite(p.Z) || p.X < -limit || p.X > limit || p.Z < -limit || p.Z > limit {
			return fmt.Errorf("control point outside the arena")
		}
	}
	if !positive(c.Collision.PushSpeed, 1000) || !positive(c.Collision.DashPushSpeed, 1000) || !positive(c.Collision.PushStrength, 1000) || !positive(c.Collision.BodyMass, 1000) || c.Collision.Iterations < 1 || c.Collision.Iterations > 32 {
		return fmt.Errorf("invalid collision configuration")
	}
	if len(c.Heroes) == 0 || len(c.Heroes) > 64 {
		return fmt.Errorf("invalid hero count")
	}
	seen := make(map[string]bool, len(c.Heroes))
	for _, h := range c.Heroes {
		if len(h.HeroID) == 0 || len(h.HeroID) > 64 || strings.TrimSpace(h.HeroID) != h.HeroID || seen[h.HeroID] {
			return fmt.Errorf("invalid or duplicate hero identity")
		}
		seen[h.HeroID] = true
		if h.AttackKind != "ranged" && h.AttackKind != "melee" && h.AttackKind != "ground" {
			return fmt.Errorf("invalid attack kind for hero %q", h.HeroID)
		}
		if h.BasicRange < 0 || h.BasicRange > c.ArenaHalfSize || h.BasicDamage < 0 || h.BasicDamage > 100000 || h.BasicWindupTicks > h.BasicCooldownTicks || h.BasicCooldownTicks > maxTicks {
			return fmt.Errorf("invalid basic attack definition %q", h.HeroID)
		}
		if h.AttackKind == "melee" && (!positive(h.MeleeRange, c.ArenaHalfSize) || h.MeleeDamage < 1 || h.MeleeDamage > 100_000 || !positive(h.MeleeArcDegrees, 180)) {
			return fmt.Errorf("invalid melee definition %q", h.HeroID)
		}
		if h.AttackKind == "ground" && (!positive(h.CastRange, c.ArenaHalfSize) || !positive(h.ImpactRadius, c.ArenaHalfSize) || h.GroundDamage < 1 || h.GroundDamage > 100_000 || h.CastWindupTicks > maxTicks || h.FallTicks == 0 || h.FallTicks > maxTicks || h.CastWindupTicks+h.FallTicks > maxTicks) {
			return fmt.Errorf("invalid ground attack definition %q", h.HeroID)
		}
		if h.AttackKind == "ranged" && (h.ProjectileDamage < 1 || h.ProjectileDamage > 100_000 || !positive(h.ProjectileSpeed, 1000) || h.ProjectileLifetimeTicks == 0 || h.ProjectileLifetimeTicks > maxTicks) {
			return fmt.Errorf("invalid ranged definition %q", h.HeroID)
		}
		if h.MaxHP < 1 || h.MaxHP > 100_000 || !positive(h.Speed, 1000) || !positive(h.DashSpeed, 1000) || h.FireCooldownTicks == 0 || h.FireCooldownTicks > maxTicks || h.DashCooldownTicks < c.DashTicks || h.DashCooldownTicks > maxTicks {
			return fmt.Errorf("invalid hero definition %q", h.HeroID)
		}
		if h.AttackStopTicks > h.FireCooldownTicks {
			return fmt.Errorf("attack stop exceeds attack cooldown for hero %q", h.HeroID)
		}
	}
	return nil
}
