package main

import "math"

// contactDistance uses collision edges, just as the attack-range check does.
// Comparing centres alone unfairly favours small troops over a nearer tower.
func contactDistance(from, target entity) float64 {
	return math.Max(0, distance(position(from), position(target))-from.Radius-target.Radius)
}

func nearerTarget(from, candidate, current entity) bool {
	if current.ID == 0 {
		return true
	}
	delta := contactDistance(from, candidate) - contactDistance(from, current)
	if math.Abs(delta) > 1e-9 {
		return delta < 0
	}
	// Equal distances keep a building objective. Troop ties use stable IDs so
	// iteration order cannot change the target between simulation ticks.
	if current.Kind == "tower" || current.Kind == "keep" {
		return false
	}
	return candidate.Kind == "tower" || candidate.Kind == "keep" || candidate.ID < current.ID
}

func (w *match) buildingObjective(u *unit) entity {
	if u.LaneX == 0 {
		u.LaneX = w.nearestBridge(u.State.Seat, u.State.X)
	}
	// Keep the deployment lane, including every squad member. After that front
	// tower falls, continue to the keep rather than switching to the other lane.
	var front, keep entity
	for _, tower := range w.Towers {
		if tower.Seat == u.State.Seat || tower.HP <= 0 {
			continue
		}
		if tower.Kind == "tower" && tower.X == u.LaneX {
			front = tower
		} else if tower.Kind == "keep" {
			keep = tower
		}
	}
	chosen := front
	if chosen.ID == 0 {
		chosen = keep
	}
	// A building already in attack range is still a valid immediate objective
	// after a fight has pulled the unit away from its deployment lane.
	attackRange := w.Rules.ByID[u.State.Kind].Range
	for _, tower := range w.Towers {
		if tower.Seat != u.State.Seat && tower.HP > 0 && contactDistance(u.State, tower) <= attackRange && nearerTarget(u.State, tower, chosen) {
			chosen = tower
		}
	}
	return chosen
}

func (w *match) target(u *unit, units []*unit) (entity, bool) {
	definition := w.Rules.ByID[u.State.Kind]
	if u.LockedTarget != 0 {
		for _, enemy := range units {
			// A troop lock lasts only while that enemy remains inside aggro.
			// Escaping enemies fall through to ordinary nearest-target selection.
			if enemy.State.ID == u.LockedTarget && enemy.State.HP > 0 && enemy.State.Seat != u.State.Seat && distance(position(u.State), position(enemy.State)) <= definition.Aggro {
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
	chosen := w.buildingObjective(u)
	if definition.Target == "buildings" {
		return chosen, chosen.ID != 0
	}
	detectedEnemy := false
	for _, enemy := range units {
		if enemy.State.Seat == u.State.Seat || enemy.State.HP <= 0 {
			continue
		}
		if distance(position(u.State), position(enemy.State)) > definition.Aggro {
			continue
		}
		// Across the river, melee units engage through a bridge, not over water.
		if definition.Range < 2 && u.State.Z*enemy.State.Z < 0 && math.Abs(u.State.Z) < 2 && math.Abs(enemy.State.Z) < 2 {
			if !w.walkable(point{(u.State.X + enemy.State.X) / 2, 0}) {
				continue
			}
		}
		detectedEnemy = true
		if nearerTarget(u.State, enemy.State, chosen) {
			chosen = enemy.State
		}
	}
	if detectedEnemy {
		// Include nearby buildings in the same distance comparison. A defender
		// behind a nearer living tower must not lure an unlocked unit around it,
		// even after a fight on the other lane. Ordinary lane routing is unchanged
		// when there are no eligible enemies nearby.
		for _, tower := range w.Towers {
			if tower.Seat != u.State.Seat && tower.HP > 0 && nearerTarget(u.State, tower, chosen) {
				chosen = tower
			}
		}
	}
	return chosen, chosen.ID != 0
}
