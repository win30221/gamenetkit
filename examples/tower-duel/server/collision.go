package main

import "math"

// Resolve soldier circles in stable ID order. Terrain and living buildings
// stay immovable; allies can push each other instead of blocking deployment.
func (w *match) resolvePush(units []*unit) {
	const gap = guardRadius * 2
	for range 16 {
		changed := false
		for i, a := range units {
			if a.State.HP <= 0 {
				continue
			}
			for _, b := range units[i+1:] {
				if b.State.HP <= 0 {
					continue
				}
				dx, dz := b.State.X-a.State.X, b.State.Z-a.State.Z
				d2 := dx*dx + dz*dz
				if d2 >= (gap-.00001)*(gap-.00001) {
					continue
				}
				d := math.Sqrt(d2)
				if d < .000001 {
					angle := float64((a.State.ID*31+b.State.ID*17)%360) * math.Pi / 180
					dx, dz = math.Cos(angle), math.Sin(angle)
				} else {
					dx, dz = dx/d, dz/d
				}
				push := math.Min(.2, (gap-d)/2+.00001)
				pa, pb := point{a.State.X - dx*push, a.State.Z - dz*push}, point{b.State.X + dx*push, b.State.Z + dz*push}
				canA, canB := w.walkable(pa), w.walkable(pb)
				if canA && canB {
					a.State.X, a.State.Z = pa.X, pa.Z
					b.State.X, b.State.Z = pb.X, pb.Z
					changed = true
				} else if canA {
					full := point{a.State.X - dx*push*2, a.State.Z - dz*push*2}
					if w.walkable(full) {
						pa = full
					}
					a.State.X, a.State.Z = pa.X, pa.Z
					changed = true
				} else if canB {
					full := point{b.State.X + dx*push*2, b.State.Z + dz*push*2}
					if w.walkable(full) {
						pb = full
					}
					b.State.X, b.State.Z = pb.X, pb.Z
					changed = true
				}
			}
		}
		if !changed {
			return
		}
	}
}
