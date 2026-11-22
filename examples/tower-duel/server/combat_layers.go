package main

import "math"

func (c cardDefinition) bodyRadius() float64 {
	if c.BodyRadius > 0 {
		return c.BodyRadius
	}
	return guardRadius
}

func (c cardDefinition) weight() float64 {
	if c.Weight > 0 {
		return c.Weight
	}
	return 1
}

func (c cardDefinition) transformTick(deployed uint64) uint64 {
	if c.TransformAfterTicks == 0 {
		return 0
	}
	return deployed + uint64(c.TransformAfterTicks)
}

func (c cardDefinition) canTarget(from, target entity) bool {
	if target.Seat == from.Seat || target.HP <= 0 {
		return false
	}
	if target.Kind == "tower" || target.Kind == "keep" {
		return true
	}
	return c.Target != "buildings" && (!target.Airborne || c.HitAir || from.Airborne)
}

func (w *match) transform(u *unit) {
	if u.State.TransformTick == 0 || w.Tick < u.State.TransformTick || u.State.HP <= 0 {
		return
	}
	multiplier := w.Rules.ByID[u.State.Kind].HpMultiplier
	u.State.HP *= multiplier
	u.State.MaxHP *= multiplier
	u.State.Airborne = true
	u.State.TransformTick = 0
	u.Path = nil
	u.Goal = 0
}

func (w *match) startKnockback(u *unit, center point, card cardDefinition) {
	if card.PauseTicks <= 0 || card.KnockbackDistance <= 0 || w.Rules.ByID[u.State.Kind].weight() >= card.WeightThreshold {
		return
	}
	x, z := u.State.X-center.X, u.State.Z-center.Z
	if math.Hypot(x, z) < 1e-9 {
		x, z = -u.State.DX, -u.State.DZ
		if math.Hypot(x, z) < 1e-9 {
			z = 1
		}
	}
	scale := card.KnockbackDistance / (float64(card.PauseTicks) * math.Hypot(x, z))
	u.Knockback = point{x * scale, z * scale}
	u.KnockbackSteps = card.PauseTicks
	u.State.PausedUntil = max(u.State.PausedUntil, w.Tick+uint64(card.PauseTicks))
	u.Path = nil
	u.Goal = 0
}

func (w *match) advanceKnockback(u *unit) {
	if u.KnockbackSteps <= 0 {
		return
	}
	u.KnockbackSteps--
	start := position(u.State)
	end := point{start.X + u.Knockback.X, start.Z + u.Knockback.Z}
	if w.walkableFor(end, u.State.Radius, u.State.Airborne) {
		u.State.X, u.State.Z = end.X, end.Z
		return
	}
	// Advance to the legal side of the boundary, then end the displacement.
	low, high := 0.0, 1.0
	for range 16 {
		mid := (low + high) / 2
		if w.walkableFor(point{start.X + u.Knockback.X*mid, start.Z + u.Knockback.Z*mid}, u.State.Radius, u.State.Airborne) {
			low = mid
		} else {
			high = mid
		}
	}
	u.State.X, u.State.Z = start.X+u.Knockback.X*low, start.Z+u.Knockback.Z*low
	u.KnockbackSteps = 0
}

func (w *match) movementFactor(u *unit, units []*unit) float64 {
	factor := w.speedFactor(u)
	if !u.State.Airborne {
		factor *= w.Map.terrainFactor(position(u.State))
	}
	boost := 1.0
	for _, ally := range units {
		card := w.Rules.ByID[ally.State.Kind]
		if ally.State.HP > 0 && w.Tick >= ally.State.ReadyTick && ally.State.Seat == u.State.Seat && card.AuraRadius > 0 && distance(position(ally.State), position(u.State)) <= card.AuraRadius {
			boost = max(boost, card.MoveMultiplier)
		}
	}
	return factor * boost
}
