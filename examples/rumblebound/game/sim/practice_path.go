package sim

import (
	"container/heap"
	"math"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

type practiceNode struct {
	index int
	g, f  float64
}
type practiceQueue []practiceNode

func (q practiceQueue) Len() int { return len(q) }
func (q practiceQueue) Less(i, j int) bool {
	if math.Abs(q[i].f-q[j].f) > 1e-9 {
		return q[i].f < q[j].f
	}
	return q[i].index < q[j].index
}
func (q practiceQueue) Swap(i, j int) { q[i], q[j] = q[j], q[i] }
func (q *practiceQueue) Push(v any)   { *q = append(*q, v.(practiceNode)) }
func (q *practiceQueue) Pop() any     { old := *q; v := old[len(old)-1]; *q = old[:len(old)-1]; return v }

func (w *World) practiceSegmentClear(from, to shared.Point, radius float64, avoid *Seat) bool {
	dx, dz := to.X-from.X, to.Z-from.Z
	if at, _, _, hit := shared.FirstMapHit(w.Map, from.X, from.Z, dx, dz, radius, false); hit && at < 1-shared.ContactEpsilon {
		return false
	}
	if avoid == nil || avoid.State.HP <= 0 || avoid.State.Left {
		return true
	}
	separation := radius + w.Rules.PlayerRadius + .08
	qx, qz := from.X-avoid.State.X, from.Z-avoid.State.Z
	// A pusher may start in the navigation buffer around another legal body.
	// Allow an outward escape rather than making every outgoing edge invalid.
	if qx*qx+qz*qz <= separation*separation && qx*dx+qz*dz >= 0 {
		return true
	}
	_, hit := segmentHit(from.X, from.Z, to.X, to.Z, avoid.State.X, avoid.State.Z, separation)
	return !hit
}

// practicePath is a bounded A* on at most 64x64 nodes. Exact circle sweeps
// validate every edge (including diagonals), so the grid cannot cut corners or
// manufacture traversal through thin cover. The result is string-pulled once;
// steering consumes cached waypoints between the twice-per-second replans.
func (w *World) practicePath(start, goal shared.Point, avoid *Seat) []shared.Point {
	radius := w.Rules.PlayerRadius
	if w.practiceSegmentClear(start, goal, radius, avoid) {
		return []shared.Point{goal}
	}
	margin := radius + .04
	limit := w.Map.ArenaHalfSize - margin
	spacing := math.Max(.5, 2*limit/63)
	side := int(math.Floor(2*limit/spacing)) + 1
	if side < 2 || side > 64 {
		return nil
	}
	points := make([]shared.Point, side*side)
	walkable := make([]bool, len(points))
	for i := range points {
		p := shared.Point{X: -limit + float64(i%side)*spacing, Z: -limit + float64(i/side)*spacing}
		points[i] = p
		walkable[i] = shared.CircleClear(w.Map, p.X, p.Z, margin)
		if avoid != nil && avoid.State.HP > 0 && !avoid.State.Left && math.Hypot(p.X-avoid.State.X, p.Z-avoid.State.Z) < margin+radius+.08 {
			walkable[i] = false
		}
	}
	closest := func(target shared.Point) int {
		index := -1
		best := math.Inf(1)
		for i, p := range points {
			if !walkable[i] {
				continue
			}
			distance := math.Hypot(p.X-target.X, p.Z-target.Z)
			if distance < best-1e-9 && w.practiceSegmentClear(target, p, radius, avoid) {
				best, index = distance, i
			}
		}
		return index
	}
	first, last := closest(start), closest(goal)
	if first < 0 || last < 0 {
		return nil
	}
	cost := make([]float64, len(points))
	previous := make([]int, len(points))
	closed := make([]bool, len(points))
	for i := range cost {
		cost[i] = math.Inf(1)
		previous[i] = -1
	}
	cost[first] = 0
	queue := &practiceQueue{{index: first, f: math.Hypot(points[first].X-points[last].X, points[first].Z-points[last].Z)}}
	heap.Init(queue)
	for queue.Len() > 0 {
		node := heap.Pop(queue).(practiceNode)
		i := node.index
		if closed[i] || node.g > cost[i]+1e-9 {
			continue
		}
		closed[i] = true
		if i == last {
			break
		}
		col, row := i%side, i/side
		for _, step := range [][2]int{{-1, 0}, {1, 0}, {0, -1}, {0, 1}, {-1, -1}, {-1, 1}, {1, -1}, {1, 1}} {
			nc, nr := col+step[0], row+step[1]
			if nc < 0 || nr < 0 || nc >= side || nr >= side {
				continue
			}
			next := nr*side + nc
			if !walkable[next] || closed[next] || !w.practiceSegmentClear(points[i], points[next], margin, avoid) {
				continue
			}
			distance := math.Hypot(points[i].X-points[next].X, points[i].Z-points[next].Z)
			nextCost := cost[i] + distance
			if nextCost >= cost[next]-1e-9 {
				continue
			}
			cost[next] = nextCost
			previous[next] = i
			heuristic := math.Hypot(points[next].X-points[last].X, points[next].Z-points[last].Z)
			heap.Push(queue, practiceNode{index: next, g: nextCost, f: nextCost + heuristic})
		}
	}
	if !closed[last] {
		return nil
	}
	reversed := make([]shared.Point, 0)
	for i := last; i >= 0; i = previous[i] {
		reversed = append(reversed, points[i])
		if i == first {
			break
		}
	}
	raw := make([]shared.Point, 0, len(reversed)+1)
	for i := len(reversed) - 1; i >= 0; i-- {
		raw = append(raw, reversed[i])
	}
	raw = append(raw, goal)
	path := make([]shared.Point, 0)
	from := start
	for i := 0; i < len(raw); {
		chosen := i
		for j := len(raw) - 1; j > i; j-- {
			if w.practiceSegmentClear(from, raw[j], radius, avoid) {
				chosen = j
				break
			}
		}
		path = append(path, raw[chosen])
		from = raw[chosen]
		i = chosen + 1
	}
	return path
}
