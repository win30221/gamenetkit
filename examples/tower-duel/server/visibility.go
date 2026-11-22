package main

import "math"

func (w *match) wallClear(from, to point) bool {
	half := w.Map.WallHalfWidth
	if half <= 0 {
		return true
	}
	start, end := 0.0, 1.0
	dz := to.Z - from.Z
	if math.Abs(dz) < 1e-9 {
		if math.Abs(from.Z) >= half {
			return true
		}
	} else {
		a, b := (-half-from.Z)/dz, (half-from.Z)/dz
		start, end = math.Max(0, math.Min(a, b)), math.Min(1, math.Max(a, b))
		if start >= end {
			return true
		}
	}
	dx := to.X - from.X
	x0, x1 := from.X+dx*start, from.X+dx*end
	for _, gate := range w.Map.Bridges {
		if math.Abs(x0-gate) <= w.Map.BridgeHalfWidth && math.Abs(x1-gate) <= w.Map.BridgeHalfWidth {
			return true
		}
	}
	return false
}

func (w *match) attackLine(from, to entity, start, end point) bool {
	return from.Airborne || to.Airborne || w.wallClear(start, end)
}
