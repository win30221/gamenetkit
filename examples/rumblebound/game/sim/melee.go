package sim

import (
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"math"
)

const maxCombatEvents = 32
const combatEventRetentionTicks uint64 = 6

// Mixed snapshots reserve a bounded total for terminal presentation metadata.
// Without melee the established 64 projectile endpoints are unchanged.
const maxMixedTerminalEvents = 52

func (w *World) recordCombatEvent(kind string, owner *Seat, targetID string, x, z, dx, dz float64) {
	if len(w.CombatEvents) == maxCombatEvents {
		copy(w.CombatEvents, w.CombatEvents[1:])
		w.CombatEvents = w.CombatEvents[:maxCombatEvents-1]
	}
	w.NextCombatEventID++
	w.CombatEvents = append(w.CombatEvents, CombatEvent{ID: "m" + idString(w.NextCombatEventID), Kind: kind,
		OwnerID: owner.State.PlayerID, TargetID: targetID, X: x, Z: z, DX: dx, DZ: dz, Tick: w.Tick, ActionID: owner.ExecutingActionID})
}

func (w *World) pruneCombatEvents() {
	remaining := w.CombatEvents[:0]
	for _, event := range w.CombatEvents {
		if w.Tick >= event.Tick && w.Tick-event.Tick > combatEventRetentionTicks {
			continue
		}
		remaining = append(remaining, event)
	}
	w.CombatEvents = remaining
}

func (w *World) resolveMelee(attacker *Seat, dx, dz float64) string {
	attacker.RewindUsed = false
	h := attacker.hero
	attacker.State.FireReadyTick = w.Tick + h.FireCooldownTicks
	attacker.State.ProtectedUntilTick = 0
	w.beginAttackStop(attacker)
	attacker.State.FacingX, attacker.State.FacingZ = dx, dz
	w.recordCombatEvent("melee_swing", attacker, "", attacker.State.X, attacker.State.Z, dx, dz)
	for _, target := range w.sortedSeats() {
		if target == attacker || target.State.TeamID == attacker.State.TeamID || !target.State.Online ||
			target.State.HP <= 0 || target.State.Left || target.Generation == 0 || w.Tick < target.State.ProtectedUntilTick {
			continue
		}
		targetX, targetZ := target.State.X, target.State.Z
		if attacker.Net != nil && attacker.Net.Lag == "melee" && attacker.RewindTick < w.Tick {
			if x, z, ok := w.historicalPosition(target, attacker.RewindTick); ok {
				targetX, targetZ = x, z
				attacker.RewindUsed = true
			}
		}
		x, z := targetX-attacker.State.X, targetZ-attacker.State.Z
		if !circleIntersectsMeleeSector(x, z, dx, dz, h.MeleeRange, h.MeleeArcDegrees, w.Rules.PlayerRadius) {
			continue
		}
		if w.Map != nil {
			if _, _, _, blocked := shared.FirstMapHit(w.Map, attacker.State.X, attacker.State.Z, x, z, 0, true); blocked {
				continue
			}
		}
		target.State.HP -= h.MeleeDamage
		if target.State.HP <= 0 {
			target.State.HP = 0
			target.State.AttackStopUntilTick = 0
			target.State.RespawnTick = w.Tick + w.Rules.RespawnTicks
			target.DX, target.DZ, target.DashEndTick = 0, 0, 0
		}
		w.recordCombatEvent("melee_hit", attacker, target.State.PlayerID, target.State.X, target.State.Z, dx, dz)
	}
	return "melee"
}

// Test the actual target disc against a finite sector: bodies grazing a side
// of the arc count, while a target wholly behind or beyond it does not.
func circleIntersectsMeleeSector(x, z, dx, dz, reach, arcDegrees, radius float64) bool {
	distance := math.Hypot(x, z)
	if distance > reach+radius {
		return false
	}
	if distance <= radius {
		return true
	}
	half := arcDegrees * math.Pi / 360
	if x*dx+z*dz >= distance*math.Cos(half) {
		return true
	}
	for _, sign := range []float64{-1, 1} {
		angle := sign * half
		ux, uz := dx*math.Cos(angle)-dz*math.Sin(angle), dx*math.Sin(angle)+dz*math.Cos(angle)
		t := math.Max(0, math.Min(reach, x*ux+z*uz))
		if math.Hypot(x-t*ux, z-t*uz) <= radius {
			return true
		}
	}
	return false
}
