package main

import "math"

type unitSightHeight struct {
	Kind   string  `json:"kind"`
	Height float64 `json:"height"`
}

func (m mapDefinition) sightHeight(kind string) float64 {
	for _, h := range m.UnitSightHeights {
		if h.Kind == kind {
			return h.Height
		}
	}
	return 3
}

func (w *match) wallClear(from, to point) bool {
	if w.Map.WallHalfWidth <= 0 || from.Z*to.Z >= 0 {
		return true
	}
	for _, z := range []float64{-w.Map.WallHalfWidth, w.Map.WallHalfWidth} {
		t := (z - from.Z) / (to.Z - from.Z)
		if t < 0 || t > 1 {
			continue
		}
		x := from.X + (to.X-from.X)*t
		gate := false
		for _, centre := range w.Map.Bridges {
			if math.Abs(x-centre) <= w.Map.BridgeHalfWidth {
				gate = true
			}
		}
		if !gate {
			return false
		}
	}
	return true
}

// Heights and the 60-degree camera are canonical map data. Units right behind
// solid wall are concealed; tall troops, gates and friendly scouts reveal them.
func (w *match) concealedFor(seat int, e entity) bool {
	if w.Map.WallHalfWidth <= 0 || e.Seat == seat {
		return false
	}
	forward := 1.0
	if seat == 1 {
		forward = -1
	}
	depth := e.Z * forward
	if depth <= 0 || w.Map.sightHeight(e.Kind)+depth*math.Sqrt(3) >= w.Map.WallHeight {
		return false
	}
	for _, x := range w.Map.Bridges {
		if math.Abs(e.X-x) <= w.Map.BridgeHalfWidth+e.Radius {
			return false
		}
	}
	for _, scout := range w.Units {
		if scout.State.Seat == seat && scout.State.HP > 0 && scout.State.Z*e.Z > 0 && distance(position(scout.State), position(e)) <= 2.5 && w.wallClear(position(scout.State), position(e)) {
			return false
		}
	}
	return true
}
