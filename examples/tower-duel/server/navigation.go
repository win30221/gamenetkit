package main

import (
	"container/heap"
	"math"
)

func (w *match) walkable(p point) bool {
	if math.Abs(p.X) > w.Map.Width/2-guardRadius || math.Abs(p.Z) > w.Map.Length/2-guardRadius {
		return false
	}
	if math.Abs(p.Z) < w.Map.barrierHalfWidth()+guardRadius {
		bridge := false
		for _, x := range w.Map.Bridges {
			if math.Abs(p.X-x) <= w.Map.BridgeHalfWidth-guardRadius {
				bridge = true
			}
		}
		if !bridge {
			return false
		}
	}
	for _, t := range w.Towers {
		if t.HP > 0 && distance(p, position(t)) < t.Radius+guardRadius+.04 {
			return false
		}
	}
	return true
}
func (w *match) deploymentPoint(seat int, p point) bool {
	if !finite(p.X) || !finite(p.Z) || seat < 0 || seat > 1 {
		return false
	}
	forward := 1.0
	if seat == 1 {
		forward = -1
	}
	depth := p.Z * forward
	bank := w.Map.barrierHalfWidth() + guardRadius + .08
	if depth <= -bank {
		return w.walkable(p)
	}
	if depth < bank || depth > w.Map.barrierHalfWidth()+w.Map.PocketDepth {
		return false
	}
	for _, tower := range w.Towers {
		if tower.Seat != seat && tower.Kind == "tower" && tower.HP == 0 &&
			((tower.X < 0 && p.X <= 0) || (tower.X > 0 && p.X >= 0)) {
			return w.walkable(p)
		}
	}
	return false
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
	// A soldier pulled onto another bridge during combat must be able to
	// finish that crossing before returning to its deployment lane's objective.
	if math.Abs(from.Z) < w.Map.barrierHalfWidth()+guardRadius {
		for _, bridge := range w.Map.Bridges {
			if math.Abs(from.X-bridge) <= w.Map.BridgeHalfWidth-guardRadius {
				laneX = bridge
				break
			}
		}
	}
	const cols = 41
	const rows = 61
	index := func(p point) int {
		x := int(math.Round((p.X + 10) * 2))
		z := int(math.Round((p.Z + 15) * 2))
		x = max(0, min(cols-1, x))
		z = max(0, min(rows-1, z))
		return z*cols + x
	}
	coord := func(id int) point { return point{float64(id%cols)*.5 - 10, float64(id/cols)*.5 - 15} }
	var occupied [cols * rows]bool
	for _, other := range units {
		if other == mover || other.State.HP <= 0 || other.State.Seat == mover.State.Seat {
			continue
		}
		centre := index(position(other.State))
		for dz := -2; dz <= 2; dz++ {
			for dx := -2; dx <= 2; dx++ {
				x, z := centre%cols+dx, centre/cols+dz
				if x >= 0 && x < cols && z >= 0 && z < rows {
					id := z*cols + x
					occupied[id] = occupied[id] || distance(coord(id), position(other.State)) < guardRadius*2-.015
				}
			}
		}
	}
	allowed := func(p point) bool {
		if occupied[index(p)] || (laneX != 0 && math.Abs(p.Z) < w.Map.barrierHalfWidth()+guardRadius && math.Abs(p.X-laneX) > w.Map.BridgeHalfWidth-guardRadius) {
			return false
		}
		return w.walkable(p)
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
		if distance(p, to) <= stopDistance && w.wallClear(p, to) {
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

func clearUnits(from, to point, mover *unit, units []*unit) bool {
	const gap = guardRadius*2 - .015
	dx, dz := to.X-from.X, to.Z-from.Z
	length := dx*dx + dz*dz
	minX, maxX := math.Min(from.X, to.X)-gap, math.Max(from.X, to.X)+gap
	minZ, maxZ := math.Min(from.Z, to.Z)-gap, math.Max(from.Z, to.Z)+gap
	for _, other := range units {
		if other == mover || other.State.HP <= 0 || other.State.Seat == mover.State.Seat {
			continue
		}
		p := position(other.State)
		if p.X < minX || p.X > maxX || p.Z < minZ || p.Z > maxZ {
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
		if !w.walkable(point{from.X + (to.X-from.X)*scale, from.Z + (to.Z-from.Z)*scale}) {
			return false
		}
	}
	return clearUnits(from, to, mover, units)
}

func (w *match) move(u *unit, goal entity, units []*unit) {
	definition := w.Rules.ByID[u.State.Kind]
	stop := u.State.Radius + goal.Radius + definition.Range - .05
	lane := 0.0
	if goal.Kind == "tower" || goal.Kind == "keep" {
		lane = u.LaneX
	}
	changed := u.Goal != goal.ID || (w.Tick >= u.NextRoute && distance(position(goal), u.GoalPoint) > .6)
	if changed || (len(u.Path) == 0 && w.Tick >= u.NextRoute) {
		u.Path = w.route(position(u.State), position(goal), stop, lane, u, units)
		u.Goal = goal.ID
		u.GoalPoint = position(goal)
		u.NextRoute = w.Tick + 20
	}
	remaining := definition.Speed * w.speedFactor(u) * w.Map.terrainFactor(position(u.State)) / tickRate
	if u.State.Charging {
		remaining *= 1.45
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
		if distanceToGoal <= stop+guardRadius*3 && w.wallClear(p, position(goal)) && w.clearSegment(p, perimeter, u, units) {
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
			if !w.walkable(v) {
				return false
			}
			for _, other := range units {
				if other != u && other.State.Seat != u.State.Seat && other.State.HP > 0 && distance(v, position(other.State)) < guardRadius*2-.015 {
					return false
				}
			}
			return true
		}
		if !clear(candidate) {
			if w.Tick >= u.NextRoute {
				u.Path = w.route(p, position(goal), stop, lane, u, units)
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
