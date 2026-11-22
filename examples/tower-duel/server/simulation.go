package main

import "math"

func (w *match) end(winner int, reason string) {
	if w.Phase == "ended" {
		return
	}
	w.Phase = "ended"
	w.Winner = winner
	w.Reason = reason
}
func (w *match) step() {
	w.Tick++
	if w.Phase == "countdown" {
		w.Countdown--
		if w.Countdown <= 0 {
			w.Phase = "playing"
		}
		return
	}
	if w.Phase != "playing" && w.Phase != "overtime" {
		return
	}
	w.Elapsed++
	regen := 1.0 / (3 * tickRate)
	if w.Phase == "overtime" {
		regen *= 2
	}
	for i := 0; i < 2; i++ {
		w.Energy[i] = math.Min(10, w.Energy[i]+regen)
	}
	units := w.sortedUnits()
	for _, u := range units {
		w.transform(u)
	}
	damage := make(map[uint64]int)
	w.stepEffects(units, damage)
	w.resolvePush(units)
	w.refreshUnitSlows(units)
	type movement struct {
		unit   *unit
		target entity
	}
	movements := make([]movement, 0, len(units))
	for _, u := range units {
		definition := w.Rules.ByID[u.State.Kind]
		factor := w.speedFactor(u)
		u.State.Slowed = factor < 1
		// Deployment uses the authoritative match clock, independent of slows.
		// Bodies can still be pushed and damaged, but cannot act or build charge.
		if w.Tick < u.State.ReadyTick || w.Tick < u.State.PausedUntil {
			continue
		}
		u.State.Charging = definition.ChargeDistance > 0 && u.Travelled >= definition.ChargeDistance
		if u.Cooldown > 0 {
			u.CooldownProgress += factor
			for u.CooldownProgress >= 1 && u.Cooldown > 0 {
				u.Cooldown--
				u.CooldownProgress--
			}
		}
		target, ok := w.target(u, units)
		inRange := ok && distance(position(u.State), position(target)) <= u.State.Radius+target.Radius+definition.Range && w.attackLine(u.State, target, position(u.State), position(target))
		if u.WindupTarget != 0 && (!inRange || target.ID != u.WindupTarget) {
			// A cancelled swing cannot carry its progress over to another target.
			u.WindupTarget = 0
			u.WindupProgress = 0
		}
		if !ok {
			u.State.Target = 0
			continue
		}
		u.State.Target = target.ID
		if inRange {
			if u.WindupTarget != 0 {
				u.WindupProgress += factor
				if u.WindupProgress+1e-9 < float64(definition.WindupTicks) {
					continue
				}
				hit := definition.Damage
				if u.State.Charging {
					hit = int(math.Round(float64(hit) * definition.ChargeMultiplier))
				}
				if definition.Splash > 0 {
					w.areaDamage(u.State.Seat, position(target), definition.Splash, hit, definition, u.State.Airborne, units, damage)
				} else {
					damage[target.ID] += hit
				}
				u.Travelled = 0
				u.State.Charging = false
				u.State.HitTick = w.Tick
				u.State.HitTarget = target.ID
				u.WindupTarget = 0
				u.WindupProgress = 0
			} else if u.Cooldown == 0 {
				u.LockedTarget = target.ID
				u.WindupTarget = target.ID
				u.WindupProgress = 0
				// Cooldown includes windup so the interval between attacks stays the same.
				u.Cooldown = definition.Cooldown
				u.CooldownProgress = 0
				u.State.AttackTick = w.Tick
				d := distance(position(u.State), position(target))
				if d > .001 {
					u.State.DX = (target.X - u.State.X) / d
					u.State.DZ = (target.Z - u.State.Z) / d
				}
			}
		} else {
			movements = append(movements, movement{u, target})
		}
	}
	// Decide attacks against one shared position state before moving anyone.
	for _, move := range movements {
		w.move(move.unit, move.target, units)
	}
	w.resolvePush(units)
	// Publish status for the final foot positions, including boundary crossings
	// caused by walking or collision pushes during this simulation step.
	w.refreshUnitSlows(units)
	// Keep activation is permanent, including the tick that damages it or
	// destroys either front tower. A sleeping keep is still a valid target.
	for i := range w.Towers {
		t := &w.Towers[i]
		if t.Kind != "keep" || t.Active || t.HP <= 0 {
			continue
		}
		if t.HP < t.MaxHP || damage[t.ID] > 0 {
			t.Active = true
		}
		for _, front := range w.Towers {
			if front.Seat == t.Seat && front.Kind == "tower" && (front.HP <= 0 || damage[front.ID] >= front.HP) {
				t.Active = true
			}
		}
	}
	for i := range w.Towers {
		t := &w.Towers[i]
		if t.HP <= 0 || !t.Active {
			continue
		}
		if w.TowerCooldown[t.ID] > 0 {
			factor := w.towerSlow(*t)
			w.TowerProgress[t.ID] += factor
			for w.TowerProgress[t.ID] >= 1 && w.TowerCooldown[t.ID] > 0 {
				w.TowerCooldown[t.ID]--
				w.TowerProgress[t.ID]--
			}
		}
		if w.TowerCooldown[t.ID] > 0 {
			continue
		}
		attackRange := w.Rules.TowerRange
		if t.Kind == "keep" {
			attackRange = w.Rules.KeepRange
		}
		reach := attackRange + t.Radius
		best := math.Inf(1)
		var target *unit
		for _, u := range units {
			if u.State.ID == t.Target && u.State.HP > 0 && u.State.Seat != t.Seat {
				target = u
				break
			}
		}
		if target == nil {
			t.Target = 0
			for _, u := range units {
				if u.State.Seat == t.Seat || u.State.HP <= 0 {
					continue
				}
				d := distance(position(*t), position(u.State))
				if d <= reach+u.State.Radius && d < best {
					best = d
					target = u
				}
			}
		}
		if target != nil && distance(position(*t), position(target.State)) <= reach+target.State.Radius {
			damage[target.State.ID] += w.Rules.TowerDamage
			w.TowerCooldown[t.ID] = w.Rules.TowerCooldownTicks
			w.TowerProgress[t.ID] = 0
			t.AttackTick = w.Tick
			t.Target = target.State.ID
		}
	}
	// Completed attacks on the same tick resolve together. Unfinished windups
	// never enter the damage map and disappear with soldiers killed this tick.
	destroyed := [2]bool{}
	for i := range w.Towers {
		t := &w.Towers[i]
		before := t.HP
		t.HP = max(0, t.HP-damage[t.ID])
		if before > 0 && t.HP == 0 {
			destroyed[t.Seat] = true
		}
	}
	for _, u := range units {
		u.State.HP -= damage[u.State.ID]
		if u.State.HP <= 0 {
			u.WindupTarget = 0
			u.WindupProgress = 0
			delete(w.Units, u.State.ID)
		}
	}
	dead := [2]bool{}
	for _, t := range w.Towers {
		if t.Kind == "keep" && t.HP == 0 {
			dead[t.Seat] = true
		}
	}
	if dead[0] && dead[1] {
		w.end(-1, "simultaneous")
	} else if dead[0] {
		w.end(1, "keep_destroyed")
	} else if dead[1] {
		w.end(0, "keep_destroyed")
	}
	if w.Phase == "ended" {
		return
	}
	if w.Phase == "overtime" && (destroyed[0] || destroyed[1]) {
		if destroyed[0] && destroyed[1] {
			w.end(-1, "simultaneous")
		} else if destroyed[0] {
			w.end(1, "sudden_death")
		} else {
			w.end(0, "sudden_death")
		}
		return
	}
	if w.Elapsed == w.Rules.Duration {
		score := [2]int{}
		for _, t := range w.Towers {
			if t.Kind == "tower" && t.HP == 0 {
				score[1-t.Seat]++
			}
		}
		if score[0] > score[1] {
			w.end(0, "time_score")
		} else if score[1] > score[0] {
			w.end(1, "time_score")
		} else {
			w.Phase = "overtime"
		}
	}
	if w.Elapsed >= w.Rules.Duration+60*tickRate {
		w.end(-1, "time_draw")
	}
}

func (w *match) speedFactor(u *unit) float64 {
	if u.SlowUntil > w.Tick && u.SlowFactor > 0 {
		return u.SlowFactor
	}
	return 1
}
func (w *match) refreshUnitSlows(units []*unit) {
	for _, u := range units {
		u.SlowUntil = 0
		u.SlowFactor = 0
		u.State.Slowed = false
		u.State.Muddy = !u.State.Airborne && w.Map.terrainFactor(position(u.State)) < 1
		for _, e := range w.Effects {
			if e.Slow <= 0 || w.Tick < e.Impact || w.Tick >= e.End || u.State.Seat == e.Seat || (u.State.Airborne && !e.Definition.HitAir) {
				continue
			}
			if distance(point{e.X, e.Z}, position(u.State)) > e.Radius+u.State.Radius+1e-9 {
				continue
			}
			if u.SlowFactor == 0 || e.Slow < u.SlowFactor {
				u.SlowFactor = e.Slow
			}
			u.SlowUntil = w.Tick + 1
			u.State.Slowed = true
		}
	}
}
func (w *match) areaDamage(seat int, center point, radius float64, amount int, card cardDefinition, airborne bool, units []*unit, damage map[uint64]int) {
	for _, u := range units {
		if card.canTarget(entity{Seat: seat, Airborne: airborne}, u.State) && distance(center, position(u.State)) <= radius+u.State.Radius+1e-9 {
			damage[u.State.ID] += amount
		}
	}
	for _, t := range w.Towers {
		if t.Seat != seat && t.HP > 0 && distance(center, position(t)) <= radius+t.Radius+1e-9 {
			damage[t.ID] += amount
		}
	}
}
func (w *match) towerSlow(t entity) float64 {
	factor := 1.0
	for _, e := range w.Effects {
		if e.Slow > 0 && t.Seat != e.Seat && w.Tick >= e.Impact && w.Tick < e.End && distance(point{e.X, e.Z}, position(t)) <= e.Radius+t.Radius+1e-9 {
			factor = min(factor, e.Slow)
		}
	}
	return factor
}
func (w *match) stepEffects(units []*unit, damage map[uint64]int) {
	live := w.Effects[:0]
	for _, e := range w.Effects {
		if w.Tick >= e.End {
			continue
		}
		if w.Tick >= e.Impact && !e.Applied && e.Damage > 0 {
			center := point{e.X, e.Z}
			w.areaDamage(e.Seat, center, e.Radius, e.Damage, e.Definition, false, units, damage)
			for _, u := range units {
				if e.Definition.canTarget(entity{Seat: e.Seat}, u.State) && distance(center, position(u.State)) <= e.Radius+u.State.Radius+1e-9 {
					w.startKnockback(u, center, e.Definition)
				}
			}
			e.Applied = true
		}
		live = append(live, e)
	}
	w.Effects = live
	for _, u := range units {
		w.advanceKnockback(u)
	}
	for i := range w.Towers {
		w.Towers[i].Slowed = w.towerSlow(w.Towers[i]) < 1
	}
	w.refreshUnitSlows(units)
}
