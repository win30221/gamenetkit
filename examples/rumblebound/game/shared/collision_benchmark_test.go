package shared

import "testing"

// Each iteration represents the same crowded 30 Hz frame. Resetting the input
// avoids measuring an increasingly empty arena after the characters disperse.
func BenchmarkResolveCrowdEight(b *testing.B) {
	m := ArenaMap()
	c := Rules()
	for _, scenario := range []struct {
		name string
		wall bool
	}{{"open", false}, {"wall", true}} {
		b.Run(scenario.name, func(b *testing.B) {
			initial := make([]CollisionBody, 8)
			for i := range initial {
				initial[i] = CollisionBody{ID: string(rune('a' + i)), X: -3 + float64(i)*.8, Z: -7, DX: 1, Speed: c.Speed}
				if scenario.wall {
					initial[i].X = m.ArenaHalfSize - c.PlayerRadius - float64(7-i)*.8
				}
			}
			bodies := make([]CollisionBody, len(initial))
			var workspace CollisionWorkspace
			workspace.ResolveCrowd(m, bodies, 1/float64(c.TickRate), c.PlayerRadius, c.Collision)
			b.ReportAllocs()
			b.ResetTimer()
			for b.Loop() {
				copy(bodies, initial)
				workspace.ResolveCrowd(m, bodies, 1/float64(c.TickRate), c.PlayerRadius, c.Collision)
			}
		})
	}
}
