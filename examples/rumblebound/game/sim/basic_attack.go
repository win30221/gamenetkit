package sim

import "math"
import "github.com/win30221/gamenetkit/examples/rumblebound/game/shared"

func (w *World) attackTarget(s *Seat, id string, life uint64) *Seat {
	for _, target := range w.Seats {
		if target.State.PlayerID == id && target.Life == life && target != s &&
			target.State.TeamID != s.State.TeamID && target.State.Online && !target.State.Left &&
			target.State.HP > 0 && w.Tick >= target.State.ProtectedUntilTick {
			return target
		}
	}
	return nil
}

func (w *World) basicInRange(s, target *Seat) bool {
	if s.hero == nil || target == nil {
		return false
	}
	dx, dz := target.State.X-s.State.X, target.State.Z-s.State.Z
	if math.Hypot(dx, dz) > s.hero.BasicRange {
		return false
	}
	_, _, _, blocked := shared.FirstMapHit(w.Map, s.State.X, s.State.Z, dx, dz, 0, true)
	return !blocked
}

func (w *World) cancelBasic(s *Seat) {
	if s.State.AttackReleaseTick == 0 {
		return
	}
	s.State.AttackReleaseTick = 0
	s.State.AttackTargetID = ""
	s.State.AttackTargetLife = 0
	s.State.AttackStopUntilTick = 0
	// An uncommitted attack deals no damage and does not spend the attack period.
	// A committed attack has no windup state, so its cooldown cannot be reset.
	s.State.BasicReadyTick = w.Tick
}

func (w *World) startBasic(s *Seat, a actionIntent) string {
	if s.hero == nil || s.hero.BasicRange <= 0 {
		return "unsupported_attack"
	}
	if s.State.AttackReleaseTick != 0 || w.Tick < s.State.BasicReadyTick {
		return "cooldown"
	}
	if w.Tick < s.State.AttackStopUntilTick {
		return "attack_locked"
	}
	target := w.attackTarget(s, a.TargetID, a.TargetLife)
	if target == nil {
		return "invalid_target"
	}
	if !w.basicInRange(s, target) {
		return "out_of_range"
	}
	s.State.AttackTargetID, s.State.AttackTargetLife = a.TargetID, a.TargetLife
	s.State.AttackReleaseTick = w.Tick + s.hero.BasicWindupTicks
	s.State.BasicReadyTick = w.Tick + s.hero.BasicCooldownTicks
	s.State.AttackStopUntilTick = s.State.AttackReleaseTick
	s.State.BasicActionID = a.ID
	s.State.ProtectedUntilTick, s.DashEndTick = 0, 0
	dx, dz := target.State.X-s.State.X, target.State.Z-s.State.Z
	if length := math.Hypot(dx, dz); length > 1e-6 {
		s.State.FacingX, s.State.FacingZ = dx/length, dz/length
	}
	w.recordCombatEvent("basic_swing", s, target.State.PlayerID, s.State.X, s.State.Z, s.State.FacingX, s.State.FacingZ)
	return "attack_started"
}

func (w *World) advanceBasicAttacks(seats []*Seat) {
	for _, s := range seats {
		if s.State.AttackReleaseTick == 0 {
			continue
		}
		target := w.attackTarget(s, s.State.AttackTargetID, s.State.AttackTargetLife)
		if !s.State.Online || s.State.Left || s.State.HP <= 0 || !w.basicInRange(s, target) {
			w.cancelBasic(s)
			continue
		}
		if w.Tick < s.State.AttackReleaseTick {
			continue
		}
		// Release commits once. Movement can cancel the remaining animation, while
		// BasicReadyTick continues to enforce the complete attack period.
		s.State.AttackReleaseTick, s.State.AttackStopUntilTick = 0, 0
		s.State.AttackTargetID, s.State.AttackTargetLife = "", 0
		s.ExecutingActionID = s.State.BasicActionID
		target.State.HP = max(0, target.State.HP-s.hero.BasicDamage)
		w.recordCombatEvent("basic_hit", s, target.State.PlayerID, target.State.X, target.State.Z, s.State.FacingX, s.State.FacingZ)
		if target.State.HP == 0 {
			w.cancelBasic(target)
			target.State.AttackStopUntilTick = 0
			target.State.RespawnTick = w.Tick + w.Rules.RespawnTicks
			target.DX, target.DZ, target.DashEndTick = 0, 0, 0
		}
	}
}
