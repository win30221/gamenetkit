package interest

import (
	"math"
	"sort"

	"github.com/win30221/gamenetkit/server/netcore"
)

type Radius struct {
	Enter, Margin float64
	visible       map[string]bool
	Limit         int
}

func New(radius, margin float64, limit int) *Radius {
	if radius <= 0 || margin < 0 || math.IsNaN(radius+margin) || math.IsInf(radius+margin, 0) || limit < 1 {
		panic("invalid interest configuration")
	}
	return &Radius{Enter: radius, Margin: margin, Limit: limit, visible: map[string]bool{}}
}
func (r *Radius) Select(x, z float64, objects []netcore.InterestObject) map[string]bool {
	next := make(map[string]bool)
	if math.IsNaN(x+z) || math.IsInf(x+z, 0) {
		return next
	}
	// Overflow caps ordinary membership, never mandatory objects. Only this
	// exceptional path sorts: in-budget selection retains its existing behavior.
	overflow := len(objects) > r.Limit
	type candidate struct {
		id       string
		distance float64
	}
	var candidates []candidate
	for _, o := range objects {
		if o.Always {
			next[o.ID] = true
			continue
		}
		distance := r.Enter
		if r.visible[o.ID] {
			distance += r.Margin
		}
		separation := math.Hypot(o.X-x, o.Z-z)
		if separation <= distance {
			if overflow {
				candidates = append(candidates, candidate{o.ID, separation})
			} else {
				next[o.ID] = true
			}
		}
	}
	if overflow && len(next) < r.Limit {
		sort.Slice(candidates, func(i, j int) bool {
			if candidates[i].distance == candidates[j].distance {
				return candidates[i].id < candidates[j].id
			}
			return candidates[i].distance < candidates[j].distance
		})
		for _, c := range candidates {
			if len(next) >= r.Limit {
				break
			}
			next[c.id] = true
		}
	}
	r.visible = next
	return next
}
