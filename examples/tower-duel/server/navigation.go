package main

import (
	"container/heap"
	"math"
)

func (w *match) walkable(p point) bool {
	return w.walkableFor(p, guardRadius, false)
}
func (w *match) walkableFor(p point, radius float64, airborne bool) bool {
	if math.Abs(p.X) > w.Map.Width/2-radius || math.Abs(p.Z) > w.Map.Length/2-radius {
		return false
	}
	if airborne {
		return true
	}
	if math.Abs(p.Z) < w.Map.barrierHalfWidth()+radius {
		bridge := false
		for _, x := range w.Map.Bridges {
			if math.Abs(p.X-x) <= w.Map.BridgeHalfWidth-radius {
				bridge = true
			}
		}
		if !bridge {
			return false
		}
	}
	for _, t := range w.Towers {
		if t.HP > 0 && distance(p, position(t)) < t.Radius+radius+.04 {
			return false
		}
	}
	return true
}
func (w *match) deploymentPoint(seat int, p point) bool {
	return w.deploymentPointFor(seat, p, guardRadius)
}
func (w *match) deploymentPointFor(seat int, p point, radius float64) bool {
	if !finite(p.X) || !finite(p.Z) || seat < 0 || seat > 1 {
		return false
	}
	forward := 1.0
	if seat == 1 {
		forward = -1
	}
	depth := p.Z * forward
	bank := w.Map.barrierHalfWidth() + radius + .08
	if depth <= -bank {
		return w.walkableFor(p, radius, false)
	}
	if depth < bank || depth > w.Map.barrierHalfWidth()+w.Map.PocketDepth {
		return false
	}
	// Each destroyed tower unlocks its own half. The seam belongs to both.
	left, right := false, false
	for _, tower := range w.Towers {
		if tower.Seat != seat && tower.Kind == "tower" && tower.HP == 0 {
			left = left || tower.X < 0
			right = right || tower.X > 0
		}
	}
	annexed := (p.X < 0 && left) || (p.X > 0 && right) || (p.X == 0 && left && right)
	return annexed && w.walkableFor(p, radius, false)
}

func (w *match) nearestBridge(seat int, x float64) float64 {
	lane := w.Map.Bridges[0]
	for _, bridge := range w.Map.Bridges[1:] {
		d, best := math.Abs(x-bridge), math.Abs(x-lane)
		if d < best || (d == best && seat == 1) {
			lane = bridge
		}
	}
	return lane
}

type routeNode struct {
	ID int
	F  float64
}
type routeHeap []routeNode

func (h routeHeap) Len() int { return len(h) }
func (h routeHeap) Less(i, j int) bool {
	if h[i].F == h[j].F {
		return h[i].ID < h[j].ID
	}
	return h[i].F < h[j].F
}
func (h routeHeap) Swap(i, j int) { h[i], h[j] = h[j], h[i] }
func (h *routeHeap) Push(x any)   { *h = append(*h, x.(routeNode)) }
func (h *routeHeap) Pop() any     { old := *h; x := old[len(old)-1]; *h = old[:len(old)-1]; return x }

// Server-owned half-metre grid. River crossings and living tower footprints
// are evaluated from the same canonical map used by the client preview.
func (w *match) route(from, to point, stopDistance, laneX float64, mover *unit, units []*unit) []point {
	radius := mover.State.Radius
	goal := w.Units[mover.Goal]
	airTarget := goal != nil && goal.State.Airborne
	// A soldier pulled onto another bridge during combat must be able to
	// finish that crossing before returning to its deployment lane's objective.
	if math.Abs(from.Z) < w.Map.barrierHalfWidth()+radius {
		for _, bridge := range w.Map.Bridges {
			if math.Abs(from.X-bridge) <= w.Map.BridgeHalfWidth-radius {
				laneX = bridge
				break
			}
		}
	}
	const step = .5
	halfWidth, halfLength := w.Map.Width/2, w.Map.Length/2
	cols, rows := int(w.Map.Width/step)+1, int(w.Map.Length/step)+1
	index := func(p point) int {
		x := int(math.Round((p.X + halfWidth) / step))
		z := int(math.Round((p.Z + halfLength) / step))
		x = max(0, min(cols-1, x))
		z = max(0, min(rows-1, z))
		return z*cols + x
	}
	coord := func(id int) point {
		return point{float64(id%cols)*step - halfWidth, float64(id/cols)*step - halfLength}
	}
	occupied := make([]bool, cols*rows)
	for _, other := range units {
		if !blocksMovement(mover, other) {
			continue
		}
		centre := index(position(other.State))
		for dz := -2; dz <= 2; dz++ {
			for dx := -2; dx <= 2; dx++ {
				x, z := centre%cols+dx, centre/cols+dz
				if x >= 0 && x < cols && z >= 0 && z < rows {
					id := z*cols + x
					occupied[id] = occupied[id] || distance(coord(id), position(other.State)) < radius+other.State.Radius-.015
				}
			}
		}
	}
	allowed := func(p point) bool {
		if occupied[index(p)] || (laneX != 0 && math.Abs(p.Z) < w.Map.barrierHalfWidth()+radius && math.Abs(p.X-laneX) > w.Map.BridgeHalfWidth-radius) {
			return false
		}
		return w.walkableFor(p, radius, false)
	}
	start := index(from)
	if !allowed(coord(start)) || !w.clearSegment(from, coord(start), mover, units) {
		best := math.Inf(1)
		found := -1
		for dz := -2; dz <= 2; dz++ {
			for dx := -2; dx <= 2; dx++ {
				x := start%cols + dx
				z := start/cols + dz
				if x < 0 || x >= cols || z < 0 || z >= rows {
					continue
				}
				id := z*cols + x
				p := coord(id)
				if d := distance(from, p); allowed(p) && d < best && w.clearSegment(from, p, mover, units) {
					found = id
					best = d
				}
			}
		}
		if found < 0 {
			return nil
		}
		start = found
	}
	frontier := &routeHeap{}
	heap.Push(frontier, routeNode{start, distance(coord(start), to)})
	g := map[int]float64{start: 0}
	came := make(map[int]int)
	closed := make(map[int]bool)
	neighbors := [][2]int{{0, 1}, {-1, 0}, {1, 0}, {0, -1}, {-1, 1}, {1, 1}, {-1, -1}, {1, -1}}
	for frontier.Len() > 0 {
		id := heap.Pop(frontier).(routeNode).ID
		if closed[id] {
			continue
		}
		closed[id] = true
		p := coord(id)
		if distance(p, to) <= stopDistance && (airTarget || w.wallClear(p, to)) {
			path := []point{p}
			for id != start {
				id = came[id]
				path = append(path, coord(id))
			}
			for i, j := 0, len(path)-1; i < j; i, j = i+1, j-1 {
				path[i], path[j] = path[j], path[i]
			}
			return path
		}
		for _, n := range neighbors {
			x := id%cols + n[0]
			z := id/cols + n[1]
			if x < 0 || x >= cols || z < 0 || z >= rows {
				continue
			}
			next := z*cols + x
			q := coord(next)
			if closed[next] || !allowed(q) {
				continue
			}
			if n[0] != 0 && n[1] != 0 && (!allowed(point{q.X, p.Z}) || !allowed(point{p.X, q.Z})) {
				continue
			}
			if !clearUnits(p, q, mover, units) {
				continue
			}
			cost := g[id] + distance(p, q)
			previous, ok := g[next]
			if !ok || cost < previous {
				g[next] = cost
				came[next] = id
				heap.Push(frontier, routeNode{next, cost + math.Max(0, distance(q, to)-stopDistance)})
			}
		}
	}
	return nil
}

// Siege units let the weighted collision solver push defenders aside.
func blocksMovement(mover, other *unit) bool {
	return mover.State.Kind != "siege" && other != mover && other.State.HP > 0 &&
		other.State.Seat != mover.State.Seat && other.State.Airborne == mover.State.Airborne
}

func clearUnits(from, to point, mover *unit, units []*unit) bool {
	dx, dz := to.X-from.X, to.Z-from.Z
	length := dx*dx + dz*dz
	for _, other := range units {
		if !blocksMovement(mover, other) {
			continue
		}
		p := position(other.State)
		gap := mover.State.Radius + other.State.Radius - .015
		if p.X < math.Min(from.X, to.X)-gap || p.X > math.Max(from.X, to.X)+gap || p.Z < math.Min(from.Z, to.Z)-gap || p.Z > math.Max(from.Z, to.Z)+gap {
			continue
		}
		scale := 0.0
		if length > 0 {
			scale = math.Max(0, math.Min(1, ((p.X-from.X)*dx+(p.Z-from.Z)*dz)/length))
		}
		x, z := p.X-from.X-scale*dx, p.Z-from.Z-scale*dz
		if x*x+z*z < gap*gap {
			return false
		}
	}
	return true
}

func (w *match) clearSegment(from, to point, mover *unit, units []*unit) bool {
	d := distance(from, to)
	if d < .001 {
		return true
	}
	for travel := .1; travel < d+.1; travel += .1 {
		scale := math.Min(travel, d) / d
		if !w.walkableFor(point{from.X + (to.X-from.X)*scale, from.Z + (to.Z-from.Z)*scale}, mover.State.Radius, mover.State.Airborne) {
			return false
		}
	}
	return clearUnits(from, to, mover, units)
}

// Building marches merge into their lane early. The grid still handles combat
// routes and approaches that have no clear route around a building.
func (w *match) routeToGoal(from point, goal entity, stop, lane float64, mover *unit, units []*unit) []point {
	to := position(goal)
	if lane == 0 {
		return w.route(from, to, stop, lane, mover, units)
	}

	forward := math.Copysign(1, goal.Z)
	depth := from.Z * forward
	radius := mover.State.Radius
	clearance := w.Map.barrierHalfWidth() + radius + .05
	halfTrack := math.Max(0, w.Map.BridgeHalfWidth-radius-.1)
	trackX := math.Max(lane-halfTrack, math.Min(lane+halfTrack, from.X))
	lateral := math.Abs(trackX - from.X)
	var path []point
	switch {
	case depth < -clearance:
		if lateral > .001 {
			entry := point{trackX, math.Min(-clearance, depth+lateral) * forward}
			path = append(path, entry)
		}
		path = append(path, point{trackX, clearance * forward})
	case depth >= clearance && lateral > .001:
		// A unit off its lane on the enemy bank rejoins it on the way to
		// the front tower. Beyond the tower line it can advance on the keep.
		mergeLimit := goal.Z*forward - stop
		if goal.Kind == "keep" {
			for _, tower := range w.Map.Towers {
				if tower.Seat == goal.Seat && tower.Kind == "tower" && tower.X == lane {
					mergeLimit = tower.Z * forward
					break
				}
			}
		}
		if depth < mergeLimit {
			entry := point{trackX, math.Min(mergeLimit, depth+lateral) * forward}
			path = append(path, entry)
		}
	}
	if len(path) == 0 {
		return w.route(from, to, stop, lane, mover, units)
	}

	current := from
	for _, next := range path {
		if !w.clearSegment(current, next, mover, units) {
			if bypass := w.buildingBypass(from, current, next, forward, lane, mover, units); len(bypass) > 0 {
				if suffix := w.routeToGoal(bypass[len(bypass)-1], goal, stop, lane, mover, units); len(suffix) > 0 {
					return append(bypass, suffix...)
				}
			}
			return w.route(from, to, stop, lane, mover, units)
		}
		current = next
	}
	if d := distance(current, to); d > stop {
		fraction := (d - stop) / d
		end := point{current.X + (to.X-current.X)*fraction, current.Z + (to.Z-current.Z)*fraction}
		if w.wallClear(end, to) && w.clearSegment(current, end, mover, units) {
			return append(path, end)
		}
	} else if w.wallClear(current, to) {
		return path
	}
	if suffix := w.route(current, to, stop, lane, mover, units); len(suffix) > 0 {
		return append(path, suffix...)
	}
	return w.route(from, to, stop, lane, mover, units)
}

// Skirt the nearest allied building obstructing the planned lane approach.
// Every bypass ends ahead of its building before planning the lane merge again.
func (w *match) buildingBypass(from, blockedFrom, blockedTo point, forward, lane float64, mover *unit, units []*unit) []point {
	dx, dz := blockedTo.X-blockedFrom.X, blockedTo.Z-blockedFrom.Z
	lengthSquared := dx*dx + dz*dz
	blockerDepth := math.Inf(1)
	var blocker entity
	for _, tower := range w.Towers {
		depth := (tower.Z - from.Z) * forward
		if tower.Seat != mover.State.Seat || tower.HP <= 0 || depth <= 0 || depth >= blockerDepth {
			continue
		}
		x, z := tower.X-blockedFrom.X, tower.Z-blockedFrom.Z
		along := math.Max(0, math.Min(1, (x*dx+z*dz)/lengthSquared))
		if math.Hypot(x-along*dx, z-along*dz) < tower.Radius+mover.State.Radius+.04 {
			blocker, blockerDepth = tower, depth
		}
	}
	if blocker.ID == 0 {
		return nil
	}

	side := from.X - blocker.X
	if math.Abs(side) < .001 {
		side = lane
	}
	clearance := blocker.Radius + mover.State.Radius + .14
	x := blocker.X + math.Copysign(clearance, side)
	depart := point{x, blocker.Z + forward*clearance*.65}
	// Aim diagonally at the flank. Start earlier if the straighter
	// approach brushes the rear of the building's collision footprint.
	for _, setback := range []float64{.4, .7, 1} {
		entryDepth := blockerDepth - clearance*setback
		if entryDepth <= 0 {
			continue
		}
		entry := point{x, from.Z + forward*entryDepth}
		if w.clearSegment(from, entry, mover, units) && w.clearSegment(entry, depart, mover, units) {
			return []point{entry, depart}
		}
	}
	return nil
}

func (w *match) move(u *unit, goal entity, units []*unit) {
	definition := w.Rules.ByID[u.State.Kind]
	stop := u.State.Radius + goal.Radius + definition.Range - .05
	remaining := definition.Speed * w.movementFactor(u, units) / tickRate
	if u.State.Charging {
		remaining *= 1.45
	}
	if u.State.Airborne {
		from, to := position(u.State), position(goal)
		d := distance(from, to)
		step := math.Min(remaining, d-stop)
		if step > 0 {
			x, z := (to.X-from.X)/d, (to.Z-from.Z)/d
			next := point{from.X + x*step, from.Z + z*step}
			if w.walkableFor(next, u.State.Radius, true) {
				u.State.X, u.State.Z = next.X, next.Z
				u.State.DX, u.State.DZ = x, z
				u.Travelled += step
			}
		}
		return
	}
	lane := 0.0
	switch goal.Kind {
	case "tower":
		lane = goal.X
	case "keep":
		lane = u.LaneX
	}
	changed := u.Goal != goal.ID || (w.Tick >= u.NextRoute && distance(position(goal), u.GoalPoint) > .6)
	if changed || (len(u.Path) == 0 && w.Tick >= u.NextRoute) {
		u.Goal = goal.ID
		u.Path = w.routeToGoal(position(u.State), goal, stop, lane, u, units)
		u.GoalPoint = position(goal)
		u.NextRoute = w.Tick + 20
	}
	for len(u.Path) > 0 && remaining > 0 {
		p := position(u.State)
		q := u.Path[0]
		distanceToGoal := distance(p, position(goal))
		scale := 0.0
		if distanceToGoal > 0 {
			scale = math.Max(0, distanceToGoal-stop) / distanceToGoal
		}
		perimeter := point{p.X + (goal.X-p.X)*scale, p.Z + (goal.Z-p.Z)*scale}
		if distanceToGoal <= stop+u.State.Radius*3 && w.attackLine(u.State, goal, p, position(goal)) && w.clearSegment(p, perimeter, u, units) {
			// Spread around the target's perimeter instead of waiting for an
			// ally to vacate one final grid point. Static obstacles still apply.
			q = position(goal)
		} else if len(u.Path) > 1 {
			next := u.Path[1]
			// Avoidance can carry a unit past a waypoint without touching its
			// centre. Do not pull it back into the squad to visit that old point.
			if (p.X-q.X)*(next.X-q.X)+(p.Z-q.Z)*(next.Z-q.Z) > 0 {
				u.Path = u.Path[1:]
				continue
			}
		}
		d := distance(p, q)
		if d < .025 {
			u.Path = u.Path[1:]
			continue
		}
		step := math.Min(remaining, d)
		dx, dz := (q.X-p.X)/d, (q.Z-p.Z)/d
		candidate := point{p.X + dx*step, p.Z + dz*step}
		clear := func(v point) bool {
			if !w.walkableFor(v, u.State.Radius, false) {
				return false
			}
			for _, other := range units {
				if blocksMovement(u, other) && distance(v, position(other.State)) < u.State.Radius+other.State.Radius-.015 {
					return false
				}
			}
			return true
		}
		if !clear(candidate) {
			if w.Tick >= u.NextRoute {
				u.Path = w.routeToGoal(p, goal, stop, lane, u, units)
				u.NextRoute = w.Tick + 20
				return
			}
			// Try both sides, including forward diagonals, so a squad at a
			// bridge entrance can untangle without being pushed into the river.
			sign := 1.0
			if u.State.ID%2 == 0 {
				sign = -1
			}
			found := false
			for _, turn := range []float64{sign * math.Pi / 4, -sign * math.Pi / 4, sign * math.Pi / 2, -sign * math.Pi / 2} {
				vx, vz := dx*math.Cos(turn)-dz*math.Sin(turn), dx*math.Sin(turn)+dz*math.Cos(turn)
				alternative := point{p.X + vx*step, p.Z + vz*step}
				if clear(alternative) {
					candidate, found = alternative, true
					break
				}
			}
			if !found {
				return
			}
		}
		u.State.X = candidate.X
		u.State.Z = candidate.Z
		u.Travelled += distance(p, candidate)
		u.State.DX = (candidate.X - p.X) / step
		u.State.DZ = (candidate.Z - p.Z) / step
		remaining -= step
	}
}
