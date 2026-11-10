package sim

import (
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"math"
)

const maxGroundImpacts = 8
const groundImpactRetentionTicks uint64 = 6

// The complete active cast survives reconnects. Combat values are frozen at
// acceptance; a later death/disconnect cannot cancel an accepted fireball.
type groundCast struct {
	ActionID                             uint64
	ID, OwnerID, TeamID                  string
	X, Z, Radius                         float64
	StartTick, FallStartTick, ImpactTick uint64
	Damage                               int
}
type GroundImpact struct {
	ID, OwnerID  string
	X, Z, Radius float64
	Tick         uint64
	Targets      []string
}

func (w *World) resolveGroundCast(s *Seat, a actionIntent) string {
	h := s.hero
	if !a.HasGroundTarget || math.IsNaN(a.TargetX) || math.IsNaN(a.TargetZ) || math.IsInf(a.TargetX, 0) || math.IsInf(a.TargetZ, 0) {
		return "invalid_target"
	}
	if math.Abs(a.TargetX) > w.Rules.ArenaHalfSize || math.Abs(a.TargetZ) > w.Rules.ArenaHalfSize ||
		(w.Map != nil && !shared.CircleClear(w.Map, a.TargetX, a.TargetZ, 0)) {
		return "invalid_target"
	}
	dx, dz := a.TargetX-s.State.X, a.TargetZ-s.State.Z
	distance := math.Hypot(dx, dz)
	if distance > h.CastRange {
		return "out_of_range"
	}
	if w.Tick < s.State.FireReadyTick {
		return "cooldown"
	}
	if len(w.GroundCasts) >= w.Rules.MaxActiveGroundCasts {
		return "cast_capacity"
	}
	w.NextGroundCastID++
	w.GroundCasts = append(w.GroundCasts, groundCast{ID: "g" + idString(w.NextGroundCastID), OwnerID: s.State.PlayerID, TeamID: s.State.TeamID,
		ActionID: a.ID,
		X:        a.TargetX, Z: a.TargetZ, Radius: h.ImpactRadius, Damage: h.GroundDamage,
		StartTick: w.Tick, FallStartTick: w.Tick + h.CastWindupTicks, ImpactTick: w.Tick + h.CastWindupTicks + h.FallTicks})
	s.State.FireReadyTick = w.Tick + h.FireCooldownTicks
	s.State.ProtectedUntilTick = 0
	w.beginAttackStop(s)
	if distance > 1e-6 {
		s.State.FacingX, s.State.FacingZ = dx/distance, dz/distance
	}
	return "cast"
}

func (w *World) advanceGroundCasts(seats []*Seat) {
	impacts := w.GroundImpacts[:0]
	for _, event := range w.GroundImpacts {
		if w.Tick <= event.Tick+groundImpactRetentionTicks {
			impacts = append(impacts, event)
		}
	}
	w.GroundImpacts = impacts
	active := w.GroundCasts[:0]
	for _, cast := range w.GroundCasts {
		if w.Tick < cast.ImpactTick {
			active = append(active, cast)
			continue
		}
		impact := GroundImpact{ID: cast.ID, OwnerID: cast.OwnerID, X: cast.X, Z: cast.Z, Radius: cast.Radius, Tick: w.Tick}
		for _, target := range seats {
			s := &target.State
			if s.PlayerID == cast.OwnerID || s.TeamID == cast.TeamID || s.HP <= 0 || s.Left || target.Generation == 0 || w.Tick < s.ProtectedUntilTick {
				continue
			}
			dx, dz := s.X-cast.X, s.Z-cast.Z
			if math.Hypot(dx, dz) > cast.Radius+w.Rules.PlayerRadius {
				continue
			}
			if w.Map != nil {
				if _, _, _, blocked := shared.FirstMapHit(w.Map, cast.X, cast.Z, dx, dz, 0, true); blocked {
					continue
				}
			}
			s.HP -= cast.Damage
			if s.HP <= 0 {
				s.HP = 0
				s.AttackStopUntilTick = 0
				s.RespawnTick = w.Tick + w.Rules.RespawnTicks
				target.DX, target.DZ, target.DashEndTick = 0, 0, 0
			}
			impact.Targets = append(impact.Targets, s.PlayerID)
		}
		if len(w.GroundImpacts) >= maxGroundImpacts {
			w.GroundImpacts = w.GroundImpacts[1:]
		}
		w.GroundImpacts = append(w.GroundImpacts, impact)
	}
	w.GroundCasts = active
}

func (w *World) groundSnapshot() ([]protocol.GroundCast, []protocol.GroundImpact) {
	casts := make([]protocol.GroundCast, 0, len(w.GroundCasts))
	for _, c := range w.GroundCasts {
		casts = append(casts, protocol.GroundCast{ID: c.ID, OwnerID: c.OwnerID, TeamID: c.TeamID, X: c.X, Z: c.Z, Radius: c.Radius, StartTick: c.StartTick, FallStartTick: c.FallStartTick, ImpactTick: c.ImpactTick, ActionID: c.ActionID})
	}
	impacts := make([]protocol.GroundImpact, 0, len(w.GroundImpacts))
	for _, i := range w.GroundImpacts {
		impacts = append(impacts, protocol.GroundImpact{ID: i.ID, OwnerID: i.OwnerID, X: i.X, Z: i.Z, Radius: i.Radius, Tick: i.Tick, Targets: append([]string(nil), i.Targets...)})
	}
	return casts, impacts
}
