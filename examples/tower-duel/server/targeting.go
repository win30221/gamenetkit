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
	for _, tower := range w.Towers {
		if tower.ID == u.LockedTarget && definition.canTarget(u.State, tower) {
			return tower, true
		}
	}
	var nearest entity
	for _, enemy := range units {
		state := enemy.State
		if !definition.canTarget(u.State, state) || distance(position(u.State), position(state)) > definition.Aggro {
			continue
		}
		if state.ID == u.LockedTarget {
			return state, true
		}
		if nearerTarget(u.State, state, nearest) {
			nearest = state
		}
	}
	u.LockedTarget = 0
	if nearest.ID != 0 {
		return nearest, true
	}
	building := w.buildingObjective(u)
	return building, building.ID != 0
}
