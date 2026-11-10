package sim

import (
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestMuzzleSweepHitsCloseGrazingEnemy(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = 0, 0
	b.State.X, b.State.Z = .555, .579 // Legal nonoverlapping player circles.
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	if b.State.HP != w.Rules.MaxHP-w.Rules.ProjectileDamage || len(w.Projectiles) != 0 {
		t.Fatalf("muzzle skipped close target: hp=%d projectiles=%d", b.State.HP, len(w.Projectiles))
	}
}

func TestMuzzleSweepUsesEarliestCollisionAndNormalTargetRules(t *testing.T) {
	for _, tc := range []struct {
		name                string
		wallX               float64
		protected, friendly bool
		wantDamage          bool
	}{
		{name: "wall_before_target", wallX: .7},
		{name: "target_before_wall", wallX: .8, wantDamage: true},
		{name: "protected_target", wallX: .8, protected: true},
		{name: "friendly_target", wallX: .8, friendly: true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
			a, b := w.Seats["a"], w.Seats["b"]
			// A projectile-only obstacle isolates earliest-hit ordering without
			// changing player positions through a movement collision.
			w.Map.Obstacles = []shared.Obstacle{{ID: "projectile-screen", X: tc.wallX, HalfX: .01, HalfZ: 1, Height: 1, BlocksProjectiles: true, Visual: "wall"}}
			a.State.X, a.State.Z = 0, -4
			b.State.X, b.State.Z = .555, -4+.579
			w.Map.Obstacles[0].Z = -4
			if tc.protected {
				b.State.ProtectedUntilTick = 60
			}
			if tc.friendly {
				b.State.TeamID = a.State.TeamID
			}
			combatAction(t, w, "a", 1, 1, "fire", 1, 0)
			wantHP := w.Rules.MaxHP
			if tc.wantDamage {
				wantHP -= w.Rules.ProjectileDamage
			}
			if b.State.HP != wantHP || len(w.Projectiles) != 0 {
				t.Fatalf("wrong muzzle collision: hp=%d want=%d projectiles=%d", b.State.HP, wantHP, len(w.Projectiles))
			}
		})
	}
}
