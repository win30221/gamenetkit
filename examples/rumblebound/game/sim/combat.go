package sim

import (
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"math"
)

const projectileEndRetentionTicks uint64 = 6

// Eight players cannot normally generate 64 ends in this six-tick window, even
// when an entire 24-shot roster hits together. A fixed cap also bounds unusual
// fixtures or future burst weapons without consuming live projectile capacity.
const maxProjectileEnds = 64

func (w *World) recordProjectileEnd(p ProjectileState, x, z float64, kind, targetID string) {
	if len(w.ProjectileEnds) == maxProjectileEnds {
		copy(w.ProjectileEnds, w.ProjectileEnds[1:])
		w.ProjectileEnds = w.ProjectileEnds[:maxProjectileEnds-1]
	}
	w.ProjectileEnds = append(w.ProjectileEnds, ProjectileEnd{ID: p.ID, X: x, Z: z, Tick: w.Tick,
		Kind: kind, TargetID: targetID, OwnerID: p.OwnerID})
}

func (w *World) pruneProjectileEnds() {
	remaining := w.ProjectileEnds[:0]
	for _, end := range w.ProjectileEnds {
		if w.Tick >= end.Tick && w.Tick-end.Tick > projectileEndRetentionTicks {
			continue
		}
		remaining = append(remaining, end)
	}
	w.ProjectileEnds = remaining
}

// Attacking interrupts voluntary movement, not collision response. Held input
// remains intact so movement resumes on the first tick after recovery.
func (w *World) beginAttackStop(s *Seat) {
	if s.hero == nil || s.hero.AttackStopTicks == 0 {
		return
	}
	s.State.AttackStopUntilTick = w.Tick + s.hero.AttackStopTicks
	s.DashEndTick = 0
}

func (w *World) resolveAction(s *Seat, a actionIntent) string {
	s.ExecutingActionID = a.ID
	if w.roster == nil {
		return "lab_disabled"
	}
	if !s.State.Online || s.State.HP == 0 || s.State.Left {
		return "dead_or_offline"
	}
	if a.Tick > w.Tick+2 {
		return "future_tick"
	}
	if w.Tick > a.Tick+15 {
		return "expired"
	}
	if a.Kind == "attack" {
		return w.startBasic(s, a)
	}
	if a.Kind == "cancel" {
		w.cancelBasic(s)
		return "cancelled"
	}
	if a.Kind == "skill" {
		// Direction/ground skill input may point beyond its cast radius. Clamp
		// on the authority too; the legacy fire diagnostic keeps strict validation.
		if s.hero != nil && s.hero.AttackKind == "ground" && a.HasGroundTarget &&
			!math.IsNaN(a.TargetX+a.TargetZ) && !math.IsInf(a.TargetX+a.TargetZ, 0) {
			dx, dz := a.TargetX-s.State.X, a.TargetZ-s.State.Z
			if distance := math.Hypot(dx, dz); distance > s.hero.CastRange {
				scale := (s.hero.CastRange - .00001) / distance
				a.TargetX, a.TargetZ = s.State.X+dx*scale, s.State.Z+dz*scale
			}
		}
		a.Kind = "fire"
		code := w.resolveAction(s, a)
		if code == "fired" || code == "melee" || code == "cast" {
			stop := s.State.AttackStopUntilTick
			w.cancelBasic(s)
			s.State.AttackStopUntilTick = stop
		}
		return code
	}
	if a.Kind == "fire" && s.hero != nil && s.hero.AttackKind == "ground" {
		return w.resolveGroundCast(s, a)
	}
	if a.HasGroundTarget {
		return "invalid_target"
	}
	length := math.Hypot(a.AimX, a.AimZ)
	if math.IsNaN(length) || math.IsInf(length, 0) || length < 1e-6 || math.Abs(a.AimX) > 1 || math.Abs(a.AimZ) > 1 {
		return "invalid_aim"
	}
	dx, dz := a.AimX/length, a.AimZ/length
	c := w.seatRules(s)
	switch a.Kind {
	case "fire":
		if w.Tick < s.State.FireReadyTick {
			return "cooldown"
		}
		if s.hero != nil && s.hero.AttackKind == "melee" {
			s.RewindTick = w.Tick
			if s.Net != nil && s.Net.Lag == "melee" && s.Net.History && w.history != nil && rewindFactory != nil {
				if tick, ok := rewindFactory(w.Rules.TickRate).Target(w.Tick, a.ViewTick, a.ObservedTick); ok {
					if _, exists := w.history.At(tick); exists {
						s.RewindTick = tick
					}
				}
			}
			return w.resolveMelee(s, dx, dz)
		}
		if len(w.Projectiles) >= w.Rules.MaxProjectiles {
			return "projectile_capacity"
		}
		w.NextProjectileID++
		spawn := w.Rules.PlayerRadius + w.Rules.ProjectileRadius + .05
		s.State.FireReadyTick = w.Tick + c.FireCooldownTicks
		s.State.ProtectedUntilTick = 0
		w.beginAttackStop(s)
		w.Projectiles = append(w.Projectiles, shot{State: ProjectileState{
			ID: "b" + idString(w.NextProjectileID), OwnerID: s.State.PlayerID, TeamID: s.State.TeamID, ActionID: a.ID,
			X: s.State.X + dx*spawn, Z: s.State.Z + dz*spawn, DX: dx, DZ: dz,
			ExpiresTick: w.Tick + c.ProjectileLifetimeTicks,
		}, speed: c.ProjectileSpeed, damage: c.ProjectileDamage, birthX: s.State.X, birthZ: s.State.Z, muzzlePending: true})
		return "fired"
	case "dash":
		if w.Tick >= s.State.DashReadyTick {
			w.cancelBasic(s)
		}
		if w.Tick < s.State.AttackStopUntilTick {
			return "attack_locked"
		}
		if w.Tick < s.State.DashReadyTick {
			return "cooldown"
		}
		s.DashDX, s.DashDZ = dx, dz
		s.DashEndTick = w.Tick + w.Rules.DashTicks
		s.State.DashReadyTick = w.Tick + c.DashCooldownTicks
		return "dashed"
	default:
		return "invalid_action"
	}
}

func (w *World) advanceProjectiles(seats []*Seat) {
	w.pruneProjectileEnds()
	if len(w.Projectiles) == 0 {
		return
	}
	remaining := w.Projectiles[:0]
	for _, p := range w.Projectiles {
		if w.Tick >= p.State.ExpiresTick {
			w.recordProjectileEnd(p.State, p.State.X, p.State.Z, "expired", "")
			continue
		}
		oldX, oldZ := p.State.X, p.State.Z
		if p.muzzlePending {
			oldX, oldZ = p.birthX, p.birthZ
			p.muzzlePending = false
		}
		speed, damage := p.speed, p.damage
		// Legacy fixtures and laboratory shots have no captured hero values.
		if speed <= 0 {
			speed = w.Rules.ProjectileSpeed
		}
		if damage <= 0 {
			damage = w.Rules.ProjectileDamage
		}
		p.State.X += p.State.DX * speed / float64(w.Rules.TickRate)
		p.State.Z += p.State.DZ * speed / float64(w.Rules.TickRate)
		var hit *Seat
		first := math.Inf(1)
		wallHit := false
		if w.Map != nil {
			if at, _, _, ok := shared.FirstMapHit(w.Map, oldX, oldZ, p.State.X-oldX, p.State.Z-oldZ, w.Rules.ProjectileRadius, true); ok {
				first, wallHit = at, true
			}
		}
		for _, s := range seats {
			if s.State.PlayerID == p.State.OwnerID || s.State.TeamID == p.State.TeamID || s.State.HP <= 0 || s.State.Left || s.Generation == 0 || w.Tick < s.State.ProtectedUntilTick {
				continue
			}
			if at, ok := segmentHit(oldX, oldZ, p.State.X, p.State.Z, s.State.X, s.State.Z, w.Rules.PlayerRadius+w.Rules.ProjectileRadius); ok && at < first {
				first, hit = at, s
			}
		}
		if hit != nil {
			w.recordProjectileEnd(p.State, oldX+(p.State.X-oldX)*first, oldZ+(p.State.Z-oldZ)*first, "player", hit.State.PlayerID)
			hit.State.HP -= damage
			if hit.State.HP <= 0 {
				hit.State.HP = 0
				hit.State.AttackStopUntilTick = 0
				hit.State.RespawnTick = w.Tick + w.Rules.RespawnTicks
				hit.DX, hit.DZ, hit.DashEndTick = 0, 0, 0
			}
			continue
		}
		if wallHit {
			x, z := oldX+(p.State.X-oldX)*first, oldZ+(p.State.Z-oldZ)*first
			kind := "wall"
			limit := w.Map.ArenaHalfSize - w.Rules.ProjectileRadius
			// FirstMapHit gives the arena boundary priority on equal contacts.
			// Classify the winning terminal pose with the same geometry tolerance.
			if math.Abs(math.Abs(x)-limit) <= shared.ContactEpsilon || math.Abs(math.Abs(z)-limit) <= shared.ContactEpsilon {
				kind = "boundary"
			}
			w.recordProjectileEnd(p.State, x, z, kind, "")
			continue
		}
		if math.Abs(p.State.X) > w.Rules.ArenaHalfSize || math.Abs(p.State.Z) > w.Rules.ArenaHalfSize {
			// The unmapped laboratory historically uses the projectile center
			// for its arena limit. Preserve that collision rule but publish the
			// exact crossing instead of an overshooting outside-arena endpoint.
			at := 1.0
			for _, axis := range [][2]float64{{oldX, p.State.X}, {oldZ, p.State.Z}} {
				if math.Abs(axis[1]) > w.Rules.ArenaHalfSize && axis[1] != axis[0] {
					boundary := math.Copysign(w.Rules.ArenaHalfSize, axis[1])
					at = math.Min(at, math.Max(0, (boundary-axis[0])/(axis[1]-axis[0])))
				}
			}
			w.recordProjectileEnd(p.State, oldX+(p.State.X-oldX)*at, oldZ+(p.State.Z-oldZ)*at, "boundary", "")
			continue
		}
		remaining = append(remaining, p)
	}
	w.Projectiles = remaining
}

// segmentHit returns the first normalized time when a projectile segment
// intersects a circular target, preventing a fast shot from tunneling through.
func segmentHit(x0, z0, x1, z1, cx, cz, radius float64) (float64, bool) {
	dx, dz := x1-x0, z1-z0
	mx, mz := x0-cx, z0-cz
	a := dx*dx + dz*dz
	c := mx*mx + mz*mz - radius*radius
	if c <= 0 {
		return 0, true
	}
	if a == 0 {
		return 0, false
	}
	b := 2 * (mx*dx + mz*dz)
	discriminant := b*b - 4*a*c
	if discriminant < 0 {
		return 0, false
	}
	t := (-b - math.Sqrt(discriminant)) / (2 * a)
	return t, t >= 0 && t <= 1
}
