package sim

import (
	"math"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func (w *World) practiceSpacingRange(bot *Seat) (near, safe float64) {
	c := w.seatRules(bot)
	reach := c.PlayerRadius + c.ProjectileRadius + .05 + c.ProjectileSpeed*float64(c.ProjectileLifetimeTicks)/float64(c.TickRate)
	if bot.hero != nil && bot.hero.AttackKind == "melee" {
		safe = math.Max(2*c.PlayerRadius+.15, bot.hero.MeleeRange*.8)
		return safe * .65, safe
	}
	if bot.hero != nil && bot.hero.AttackKind == "ground" {
		safe = math.Max(3, math.Min(5, bot.hero.CastRange*.55))
	} else {
		safe = math.Max(3.5, math.Min(5, reach*.5))
	}
	return safe * .78, safe
}

func (w *World) practiceKeepDistance(bot, owner *Seat) {
	brain := w.practice
	if owner == nil || owner.State.HP <= 0 || owner.State.Left {
		brain.retreating = false
		return
	}
	near, safe := w.practiceSpacingRange(bot)
	brain.spacingRange = safe
	ax, az := bot.State.X-owner.State.X, bot.State.Z-owner.State.Z
	gap := math.Hypot(ax, az)
	if gap < near {
		brain.retreating = true
	}
	if gap >= safe {
		brain.retreating = false
	}
	if !brain.retreating {
		// A capture/chase goal must not immediately pull the bot back into
		// the same unsafe gap. Hold the comfortable band while able to fire.
		if gap <= safe && ax*bot.DX+az*bot.DZ < -1e-6 {
			bot.DX, bot.DZ = 0, 0
		}
		return
	}
	if gap < 1e-6 {
		ax, az, gap = 1, 0, 1e-6
	}
	baseAngle := math.Atan2(az, ax)
	start := shared.Point{X: bot.State.X, Z: bot.State.Z}
	distance := w.seatRules(bot).Speed * .35
	behavior, _ := shared.PracticeBehaviors(w.BotMode)
	bestScore, bestX, bestZ := math.Inf(-1), 0.0, 0.0
	for i := range 16 {
		angle := baseAngle + float64(i)*math.Pi/8
		dx, dz := math.Cos(angle), math.Sin(angle)
		end := shared.Point{X: start.X + dx*distance, Z: start.Z + dz*distance}
		if !shared.CircleClear(w.Map, end.X, end.Z, w.Rules.PlayerRadius) || !w.practiceSegmentClear(start, end, w.Rules.PlayerRadius, owner) {
			continue
		}
		gain := math.Hypot(end.X-owner.State.X, end.Z-owner.State.Z) - gap
		if gain <= .02 {
			continue
		}
		score := gain + .04*(dx*brain.evadeX+dz*brain.evadeZ)
		if behavior&shared.PracticeDodge != 0 {
			// A safe diagonal is preferable to backing straight down a shot.
			clearance := w.practiceClearance(bot, dx, dz)
			if clearance < .15 {
				score -= 10 + math.Max(0, -clearance)*5
			}
		}
		if behavior&shared.PracticeCapture != 0 && w.insidePoint(bot) {
			point := w.Rules.ControlPoints[w.PointIndex]
			if math.Hypot(end.X-point.X, end.Z-point.Z) <= w.Rules.ControlPointRadius-.05 {
				score += .25
			}
		}
		if score > bestScore {
			bestScore, bestX, bestZ = score, dx, dz
		}
	}
	if math.IsInf(bestScore, -1) {
		return
	}
	bot.DX, bot.DZ = bestX, bestZ
	brain.state = "retreat"
	// Do not start a new attack lock while trying to make space. An attack
	// already in progress, normal speed and physical collisions still apply.
	bot.Pending = nil
	if behavior&shared.PracticeAttack == 0 && math.Hypot(bestX, bestZ) > 1e-6 {
		bot.State.FacingX, bot.State.FacingZ = bestX, bestZ
	}
}
