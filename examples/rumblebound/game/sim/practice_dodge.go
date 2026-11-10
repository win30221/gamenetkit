package sim

import (
	"math"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

// Predict only active, nearby enemy attacks, using their current published
// trajectory/telegraph. No future player inputs or unpublished aim are read.
func (w *World) practiceClearance(bot *Seat, dx, dz float64) float64 {
	const horizon = .65
	clearance := 3.0
	speed := w.seatRules(bot).Speed
	if w.Tick < bot.State.AttackStopUntilTick {
		speed = 0
	}
	for _, projectile := range w.Projectiles {
		p := projectile.State
		if p.TeamID == bot.State.TeamID || p.OwnerID == bot.State.PlayerID || p.ExpiresTick <= w.Tick {
			continue
		}
		qx, qz := p.X-bot.State.X, p.Z-bot.State.Z
		if qx*qx+qz*qz > 64 {
			continue
		}
		shotSpeed := projectile.speed
		if shotSpeed <= 0 {
			shotSpeed = w.Rules.ProjectileSpeed
		}
		vx, vz := p.DX*shotSpeed-dx*speed, p.DZ*shotSpeed-dz*speed
		until := math.Min(horizon, float64(p.ExpiresTick-w.Tick)/float64(w.Rules.TickRate))
		t := 0.0
		if vv := vx*vx + vz*vz; vv > 1e-9 {
			t = math.Max(0, math.Min(until, -(qx*vx+qz*vz)/vv))
		}
		if _, _, _, blocked := shared.FirstMapHit(w.Map, p.X, p.Z, p.DX*shotSpeed*t, p.DZ*shotSpeed*t, w.Rules.ProjectileRadius, true); blocked {
			continue
		}
		clearance = math.Min(clearance, math.Hypot(qx+vx*t, qz+vz*t)-w.Rules.PlayerRadius-w.Rules.ProjectileRadius)
	}
	for _, cast := range w.GroundCasts {
		if cast.TeamID == bot.State.TeamID || cast.OwnerID == bot.State.PlayerID || cast.ImpactTick < w.Tick {
			continue
		}
		t := float64(cast.ImpactTick-w.Tick) / float64(w.Rules.TickRate)
		if t > horizon {
			continue
		}
		x, z := bot.State.X+dx*speed*t, bot.State.Z+dz*speed*t
		if _, _, _, blocked := shared.FirstMapHit(w.Map, cast.X, cast.Z, x-cast.X, z-cast.Z, 0, true); blocked {
			continue
		}
		clearance = math.Min(clearance, math.Hypot(x-cast.X, z-cast.Z)-cast.Radius-w.Rules.PlayerRadius)
	}
	return clearance
}

func (w *World) practiceDodge(bot, owner *Seat) {
	if bot.Pending != nil && bot.Pending.Kind == "fire" && w.practiceClearance(bot, 0, 0) <= .15 {
		bot.Pending = nil
	}
	baseline := w.practiceClearance(bot, bot.DX, bot.DZ)
	if baseline > .15 {
		return
	}
	best, score := baseline, math.Inf(-1)
	bestX, bestZ := bot.DX, bot.DZ
	spacing, safeScore := false, math.Inf(-1)
	safeX, safeZ, safeClearance := 0.0, 0.0, 0.0
	gap := 0.0
	if owner != nil && owner.State.HP > 0 && !owner.State.Left && w.practice.spacingRange > 0 {
		gap = math.Hypot(bot.State.X-owner.State.X, bot.State.Z-owner.State.Z)
		spacing = gap <= w.practice.spacingRange+.2
	}
	start := shared.Point{X: bot.State.X, Z: bot.State.Z}
	for i := range 8 {
		angle := float64(i) * math.Pi / 4
		dx, dz := math.Cos(angle), math.Sin(angle)
		distance := w.seatRules(bot).Speed * .35
		end := shared.Point{X: start.X + dx*distance, Z: start.Z + dz*distance}
		if !shared.CircleClear(w.Map, end.X, end.Z, w.Rules.PlayerRadius) || !w.practiceSegmentClear(start, end, w.Rules.PlayerRadius, owner) {
			continue
		}
		clearance := w.practiceClearance(bot, dx, dz)
		// Safety dominates; ties prefer the current goal and the previous
		// escape direction so consecutive decisions do not zigzag in place.
		candidateScore := clearance + .03*(dx*bot.DX+dz*bot.DZ) + .04*(dx*w.practice.evadeX+dz*w.practice.evadeZ)
		if spacing {
			gain := math.Hypot(end.X-owner.State.X, end.Z-owner.State.Z) - gap
			candidateScore += .08 * gain
			if clearance > .15 && clearance > baseline+.02 && gain >= -.02 && candidateScore > safeScore {
				safeScore, safeX, safeZ, safeClearance = candidateScore, dx, dz, clearance
			}
		}
		if clearance > baseline+.02 && candidateScore > score {
			best, score, bestX, bestZ = clearance, candidateScore, dx, dz
		}
	}
	if !math.IsInf(safeScore, -1) {
		bestX, bestZ, best = safeX, safeZ, safeClearance
	}
	if best <= baseline+.02 {
		return
	}
	bot.DX, bot.DZ = bestX, bestZ
	w.practice.evadeX, w.practice.evadeZ = bestX, bestZ
	w.practice.state = "dodge"
	// Starting a new attack would lock voluntary movement. Evade first, then
	// attack again once the immediate threat passes; existing locks persist.
	bot.Pending = nil
	if w.Tick >= bot.State.AttackStopUntilTick {
		bot.State.FacingX, bot.State.FacingZ = bestX, bestZ
	}
}
