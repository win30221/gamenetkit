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
func (w *match) target(u *unit, units []*unit) (entity, bool) {
	definition := w.Rules.ByID[u.State.Kind]
	if u.LockedTarget != 0 {
		for _, enemy := range units {
			if enemy.State.ID == u.LockedTarget && enemy.State.HP > 0 && enemy.State.Seat != u.State.Seat {
				return enemy.State, true
			}
		}
		for _, tower := range w.Towers {
			if tower.ID == u.LockedTarget && tower.HP > 0 && tower.Seat != u.State.Seat {
				return tower, true
			}
		}
		u.LockedTarget = 0
	}
	if u.LaneX == 0 {
		u.LaneX = w.nearestBridge(u.State.Seat, u.State.X)
	}
	best := math.Inf(1)
	var chosen entity
	found := false
	for _, enemy := range units {
		if definition.Target == "buildings" || enemy.State.Seat == u.State.Seat || enemy.State.HP <= 0 {
			continue
		}
		d := distance(position(u.State), position(enemy.State))
		if d > definition.Aggro {
			continue
		}
		// Across the river, engage through a bridge rather than attacking over water.
		if definition.Range < 2 && u.State.Z*enemy.State.Z < 0 && math.Abs(u.State.Z) < 2.0 && math.Abs(enemy.State.Z) < 2.0 {
			mid := point{(u.State.X + enemy.State.X) / 2, 0}
			if !w.walkable(mid) {
				continue
			}
		}
		if d < best {
			chosen = enemy.State
			best = d
			found = true
		}
	}
	if found {
		return chosen, true
	}
	for _, t := range w.Towers {
		if t.Seat != u.State.Seat && t.HP > 0 {
			d := distance(position(u.State), position(t))
			if d <= u.State.Radius+t.Radius+definition.Range && d < best {
				best = d
				chosen = t
				found = true
			}
		}
	}
	if found {
		return chosen, true
	}
	// Keep the lane chosen by the deployment centre, including every squad
	// member. After that front tower falls, continue to the keep.
	var keep entity
	for _, t := range w.Towers {
		if t.Seat == u.State.Seat || t.HP <= 0 {
			continue
		}
		if t.Kind == "tower" && t.X == u.LaneX {
			return t, true
		}
		if t.Kind == "keep" {
			keep = t
		}
	}
	return keep, keep.ID != 0
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
	if w.Elapsed >= w.Rules.Duration-60*tickRate {
		regen *= 2
	}
	for i := 0; i < 2; i++ {
		w.Energy[i] = math.Min(10, w.Energy[i]+regen)
	}
	units := w.sortedUnits()
	w.resolvePush(units)
	damage := make(map[uint64]int)
	w.stepEffects(units, damage)
	type movement struct {
		unit   *unit
		target entity
	}
	movements := make([]movement, 0, len(units))
	for _, u := range units {
		definition := w.Rules.ByID[u.State.Kind]
		factor := w.speedFactor(u)
		u.State.Slowed = factor < 1
		u.State.Charging = definition.ChargeDistance > 0 && u.Travelled >= definition.ChargeDistance
		if u.Cooldown > 0 {
			u.CooldownProgress += factor
			for u.CooldownProgress >= 1 && u.Cooldown > 0 {
				u.Cooldown--
				u.CooldownProgress--
			}
		}
		target, ok := w.target(u, units)
		if !ok {
			u.State.Target = 0
			continue
		}
		u.State.Target = target.ID
		if distance(position(u.State), position(target)) <= u.State.Radius+target.Radius+definition.Range {
			if u.Cooldown == 0 {
				u.LockedTarget = target.ID
				hit := definition.Damage
				if u.State.Charging {
					hit = int(math.Round(float64(hit) * definition.ChargeMultiplier))
				}
				if definition.Splash > 0 {
					w.areaDamage(u.State.Seat, position(target), definition.Splash, hit, 1, units, damage)
				} else {
					damage[target.ID] += hit
				}
				u.Travelled = 0
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
			factor := 1.0
			if t.Slowed {
				factor = .55
			}
			w.TowerProgress[t.ID] += factor
			for w.TowerProgress[t.ID] >= 1 && w.TowerCooldown[t.ID] > 0 {
				w.TowerCooldown[t.ID]--
				w.TowerProgress[t.ID]--
			}
		}
		if w.TowerCooldown[t.ID] > 0 {
			continue
		}
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
				if u.State.Seat == t.Seat {
					continue
				}
				d := distance(position(*t), position(u.State))
				if d <= 5+t.Radius+guardRadius && d < best {
					best = d
					target = u
				}
			}
		}
		if target != nil && distance(position(*t), position(target.State)) <= 5+t.Radius+guardRadius {
			damage[target.State.ID] += w.Rules.TowerDamage
			w.TowerCooldown[t.ID] = 27
			w.TowerProgress[t.ID] = 0
			t.AttackTick = w.Tick
			t.Target = target.State.ID
		}
	}
	// Apply the whole tick together, including retaliation by units killed this tick.
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
func (w *match) areaDamage(seat int, center point, radius float64, amount int, towerScale float64, units []*unit, damage map[uint64]int) {
	for _, u := range units {
		if u.State.Seat != seat && distance(center, position(u.State)) <= radius+u.State.Radius {
			damage[u.State.ID] += amount
		}
	}
	for _, t := range w.Towers {
		if t.Seat != seat && t.HP > 0 && distance(center, position(t)) <= radius+t.Radius {
			damage[t.ID] += int(math.Round(float64(amount) * towerScale))
		}
	}
}
func (w *match) stepEffects(units []*unit, damage map[uint64]int) {
	for i := range w.Towers {
		w.Towers[i].Slowed = false
	}
	live := w.Effects[:0]
	for _, e := range w.Effects {
		if w.Tick >= e.End {
			continue
		}
		if w.Tick >= e.Impact {
			if !e.Applied && e.Damage > 0 {
				w.areaDamage(e.Seat, point{e.X, e.Z}, e.Radius, e.Damage, e.TowerScale, units, damage)
				e.Applied = true
			}
			if e.Slow > 0 {
				for _, u := range units {
					if u.State.Seat != e.Seat && distance(point{e.X, e.Z}, position(u.State)) <= e.Radius+u.State.Radius {
						if u.SlowUntil <= w.Tick || e.Slow < u.SlowFactor {
							u.SlowFactor = e.Slow
						}
						u.SlowUntil = w.Tick + 1
					}
				}
				for i := range w.Towers {
					t := &w.Towers[i]
					if t.Seat != e.Seat && t.HP > 0 && distance(point{e.X, e.Z}, position(*t)) <= e.Radius+t.Radius {
						t.Slowed = true
					}
				}
			}
		}
		live = append(live, e)
	}
	w.Effects = live
}
