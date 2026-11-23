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

	return chosen
}

func (w *match) target(u *unit, units []*unit) (entity, bool) {
	definition := w.Rules.ByID[u.State.Kind]
	var nearest entity
	consider := func(candidate entity) {
		if definition.canTarget(u.State, candidate) && contactDistance(u.State, candidate) <= definition.Aggro &&
			nearerTarget(u.State, candidate, nearest) {
			nearest = candidate
		}
	}
	for _, tower := range w.Towers {
		consider(tower)
	}
	for _, enemy := range units {
		consider(enemy.State)
	}
	if nearest.ID == 0 {
		nearest = w.buildingObjective(u)
	}
	if u.LockedTarget != nearest.ID {
		u.LockedTarget = 0
	}
	return nearest, nearest.ID != 0
}
