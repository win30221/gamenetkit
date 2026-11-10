package shared

import (
	"reflect"
	"testing"
)

func TestCollisionWorkspaceTracksRosterAndMapEdits(t *testing.T) {
	var workspace CollisionWorkspace
	c := Rules()
	m := ArenaMap()
	for frame := 0; frame < 80; frame++ {
		// Exercise growth, shrink, reorder, a different map, and in-place
		// geometry/ID changes. A stale cache must never change the result.
		if frame == 20 {
			m = &ArenaMapDefinition{ArenaHalfSize: 10}
		}
		if frame == 30 {
			m = ArenaMap()
		}
		if frame >= 40 {
			m.Obstacles[0].X += .001
			m.Obstacles[0].ID = "edited"
			m.Obstacles[0], m.Obstacles[1] = m.Obstacles[1], m.Obstacles[0]
		}
		original := m.Clone()
		bodies := make([]CollisionBody, 1+frame%8)
		for i := range bodies {
			bodies[i] = CollisionBody{ID: string(rune('a' + len(bodies) - i)), X: 2.4 + float64(i)*.8, Z: -6, DX: 1, Speed: 6, Dashing: frame%3 == 0}
		}
		fresh := append([]CollisionBody(nil), bodies...)
		ResolveCrowd(m, fresh, 1.0/30, c.PlayerRadius, c.Collision)
		workspace.ResolveCrowd(m, bodies, 1.0/30, c.PlayerRadius, c.Collision)
		if !reflect.DeepEqual(bodies, fresh) {
			t.Fatalf("workspace retained stale state on frame %d: got %+v want %+v", frame, bodies, fresh)
		}
		if !reflect.DeepEqual(m, original) {
			t.Fatal("collision sorted or changed the caller's map")
		}
	}
}
