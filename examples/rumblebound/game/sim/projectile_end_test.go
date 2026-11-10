package sim

import (
	"math"
	"strconv"
	"testing"
)

func requireProjectileEnd(t *testing.T, w *World, x, z float64) ProjectileEnd {
	t.Helper()
	if len(w.Projectiles) != 0 || len(w.ProjectileEnds) != 1 {
		t.Fatalf("live=%d ends=%+v", len(w.Projectiles), w.ProjectileEnds)
	}
	end := w.ProjectileEnds[0]
	if end.ID != "b1" || end.Tick != w.Tick || math.Abs(end.X-x) > 1e-12 || math.Abs(end.Z-z) > 1e-12 {
		t.Fatalf("wrong terminal pose: %+v want=(%.15f,%.15f) tick=%d", end, x, z, w.Tick)
	}
	return end
}

func TestProjectileEndPublishesExactWallContactAndRetainsForDelayedRendering(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	for len(w.Projectiles) > 0 && w.Tick < 10 {
		w.Step()
	}
	end := requireProjectileEnd(t, w, -4.1-w.Rules.ProjectileRadius, 0)
	owner := w.Seats["a"].State
	if end.Kind != "wall" || end.TargetID != "" || end.OwnerID != owner.PlayerID {
		t.Fatalf("wall feedback lost its classification or shooter: %+v", end)
	}
	observer := w.Seats["b"]
	first, second := w.Snapshot(observer, "snapshot"), w.Snapshot(observer, "snapshot")
	if len(first.ProjectileEnds) != 1 || first.ProjectileEnds[0].ID != end.ID || first.ProjectileEnds[0].Tick != end.Tick || first.ProjectileEnds[0].X != end.X {
		t.Fatalf("observer missing authoritative terminal pose: %+v", first.ProjectileEnds)
	}
	if first.ProjectileEnds[0].Kind != "wall" || first.ProjectileEnds[0].TargetID != "" || first.ProjectileEnds[0].OwnerID != owner.PlayerID {
		t.Fatalf("wire snapshot omitted wall feedback metadata: %+v", first.ProjectileEnds[0])
	}
	first.ProjectileEnds[0].X = 100
	if w.ProjectileEnds[0].X != end.X || second.ProjectileEnds[0].X != end.X {
		t.Fatal("terminal projection shared mutable world or snapshot storage")
	}
	for w.Tick < end.Tick+projectileEndRetentionTicks {
		w.Step()
	}
	if len(w.Snapshot(observer, "snapshot").ProjectileEnds) != 1 {
		t.Fatal("terminal pose expired before delayed rendering window")
	}
	w.Step()
	if len(w.ProjectileEnds) != 0 || len(w.Snapshot(observer, "snapshot").ProjectileEnds) != 0 {
		t.Fatal("terminal pose retained after bounded window with no active shots")
	}
}

func TestProjectileEndUsesCloserPlayerInsteadOfWall(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	b := w.Seats["b"]
	b.State.X = -4.9
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	end := requireProjectileEnd(t, w, b.State.X-w.Rules.PlayerRadius-w.Rules.ProjectileRadius, 0)
	if end.Kind != "player" || end.TargetID != b.State.PlayerID || end.OwnerID != w.Seats["a"].State.PlayerID {
		t.Fatalf("player feedback omitted target or shooter: %+v", end)
	}
	if b.State.HP != 75 {
		t.Fatal("terminal presentation changed authoritative damage")
	}
	for range 3 {
		w.Step()
	}
	if b.State.HP != 75 || len(w.ProjectileEnds) != 1 {
		t.Fatal("retained feedback damaged the player again or duplicated the event")
	}
	wire := w.Snapshot(w.Seats["a"], "snapshot").ProjectileEnds[0]
	if wire.Kind != "player" || wire.TargetID != b.State.PlayerID || wire.OwnerID != end.OwnerID {
		t.Fatalf("wire snapshot omitted player feedback metadata: %+v", wire)
	}
}

func TestProjectileEndUsesRoundedCornerAndExactArenaBoundary(t *testing.T) {
	t.Run("rounded_corner", func(t *testing.T) {
		w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
		// The existing west cover's southwest rounded corner is approached
		// diagonally; the endpoint must lie on its circular offset, not an AABB.
		direction := 1 / math.Sqrt(2)
		w.Projectiles = []shot{{State: ProjectileState{ID: "b1", X: -5, Z: -2.4, DX: direction, DZ: direction, ExpiresTick: 10}, speed: 60}}
		w.Step()
		contactX, contactZ := -4.1-w.Rules.ProjectileRadius/math.Sqrt(2), -1.5-w.Rules.ProjectileRadius/math.Sqrt(2)
		end := requireProjectileEnd(t, w, contactX, contactZ)
		if end.Kind != "wall" || end.TargetID != "" {
			t.Fatalf("rounded cover contact misclassified: %+v", end)
		}
	})
	for _, mapped := range []bool{false, true} {
		t.Run("boundary_mapped_"+strconv.FormatBool(mapped), func(t *testing.T) {
			w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
			wantX := w.Rules.ArenaHalfSize - w.Rules.ProjectileRadius
			if !mapped {
				w.Map = nil
				wantX = w.Rules.ArenaHalfSize // Preserve the unmapped laboratory rule.
			}
			w.Projectiles = []shot{{State: ProjectileState{ID: "b1", X: 9.8, Z: -4, DX: 1, ExpiresTick: 10}, speed: 12}}
			w.Step()
			end := requireProjectileEnd(t, w, wantX, -4)
			if end.Kind != "boundary" || end.TargetID != "" {
				t.Fatalf("arena contact misclassified: %+v", end)
			}
		})
	}
}

func TestProjectileEndExpiryUsesLastValidPoseAndResultRetainsEndpoint(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	w.Projectiles = []shot{{State: ProjectileState{ID: "b1", X: 1, Z: -4, DX: 1, ExpiresTick: 1}}}
	w.Step()
	end := requireProjectileEnd(t, w, 1, -4)
	if end.Kind != "expired" || end.TargetID != "" {
		t.Fatalf("lifetime expiry misclassified: %+v", end)
	}
	w.finish("draw", "time", "")
	w.Step()
	result := w.Snapshot(w.Seats["a"], "result")
	if w.Tick != end.Tick || len(result.ProjectileEnds) != 1 || result.ProjectileEnds[0].X != end.X || result.ProjectileEnds[0].Tick != end.Tick {
		t.Fatal("settlement lost the final authoritative endpoint")
	}
}

func TestProjectileEndCacheIsBoundedWithoutConsumingLiveCapacity(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	for i := 0; i < maxProjectileEnds+20; i++ {
		w.recordProjectileEnd(ProjectileState{ID: "end" + strconv.Itoa(i)}, 0, 0, "wall", "")
	}
	if len(w.ProjectileEnds) != maxProjectileEnds || w.ProjectileEnds[0].ID != "end20" || w.ProjectileEnds[maxProjectileEnds-1].ID != "end83" {
		t.Fatal("terminal cache did not retain the newest bounded events in order")
	}
	a := w.Seats["a"]
	if code := w.resolveAction(a, actionIntent{Kind: "fire", ID: 1, Tick: w.Tick, AimX: 1}); code != "fired" || len(w.Projectiles) != 1 {
		t.Fatalf("terminal events consumed live projectile capacity: %s", code)
	}
	// Ends cannot allow an over-capacity live shot either.
	a.State.FireReadyTick = 0
	w.Projectiles = make([]shot, w.Rules.MaxProjectiles)
	if code := w.resolveAction(a, actionIntent{Kind: "fire", ID: 2, Tick: w.Tick, AimX: 1}); code != "projectile_capacity" {
		t.Fatalf("terminal cache changed active capacity rejection: %s", code)
	}
}
