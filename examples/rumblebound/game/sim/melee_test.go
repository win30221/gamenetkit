package sim

import (
	"encoding/json"
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func meleeWorld(t *testing.T) *World {
	t.Helper()
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	tank, _ := w.Rules.Hero("tank")
	a, b := w.Seats["a"], w.Seats["b"]
	a.hero, a.State.HeroID, a.State.HP = &tank, "tank", tank.MaxHP
	a.State.X, a.State.Z, b.State.X, b.State.Z = 0, -4, 1, -4
	a.State.ProtectedUntilTick, b.State.ProtectedUntilTick = 0, 0
	return w
}

func TestMeleeCircleSectorRangeAngleAndGrazingBodies(t *testing.T) {
	for _, c := range []struct {
		name string
		x, z float64
		hit  bool
	}{
		{"front", 1, 0, true}, {"reach_edge", 1.65, 0, true}, {"outside_reach", 1.651, 0, false},
		{"side", 0, 1, false}, {"behind", -1, 0, false},
		{"circle_grazes_arc", math.Cos(70 * math.Pi / 180), math.Sin(70 * math.Pi / 180), true},
		{"outside_arc", math.Cos(95 * math.Pi / 180), math.Sin(95 * math.Pi / 180), false},
	} {
		t.Run(c.name, func(t *testing.T) {
			if got := circleIntersectsMeleeSector(c.x, c.z, 1, 0, 1.25, 120, .4); got != c.hit {
				t.Fatalf("hit=%v want=%v at (%f,%f)", got, c.hit, c.x, c.z)
			}
		})
	}
}

func TestMeleeDamagesOnceWithoutProjectileAndObeysInputDedupAndCooldown(t *testing.T) {
	w := meleeWorld(t)
	a, b := w.Seats["a"], w.Seats["b"]
	initialHP, damage := b.State.HP, a.hero.MeleeDamage
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	if a.ActionCode != "melee" || b.State.HP != initialHP-damage || len(w.Projectiles) != 0 || w.NextProjectileID != 0 ||
		a.State.FireReadyTick != w.Tick+a.hero.FireCooldownTicks || len(w.CombatEvents) != 2 || w.CombatEvents[0].Kind != "melee_swing" || w.CombatEvents[1].Kind != "melee_hit" {
		t.Fatalf("wrong first swing: action=%s hp=%d events=%+v", a.ActionCode, b.State.HP, w.CombatEvents)
	}
	combatAction(t, w, "a", 2, 1, "fire", 1, 0)
	if b.State.HP != initialHP-damage || w.NextCombatEventID != 2 {
		t.Fatal("repeated action damaged twice")
	}
	combatAction(t, w, "a", 3, 2, "fire", 1, 0)
	if a.ActionCode != "cooldown" || b.State.HP != initialHP-damage || w.NextCombatEventID != 2 {
		t.Fatal("cooldown did not reject melee")
	}
	for w.Tick < a.State.FireReadyTick {
		w.Step()
	}
	combatAction(t, w, "a", 4, 3, "fire", 1, 0)
	if b.State.HP != initialHP-2*damage || w.NextCombatEventID != 4 {
		t.Fatal("next melee did not reset per-swing hit eligibility")
	}
}

func TestMeleeMissesInvalidTargetsAndBlockedCover(t *testing.T) {
	for name, change := range map[string]func(*World, *Seat){
		"ally":      func(w *World, b *Seat) { b.State.TeamID = w.Seats["a"].State.TeamID },
		"offline":   func(_ *World, b *Seat) { b.State.Online = false },
		"left":      func(_ *World, b *Seat) { b.State.Left = true },
		"dead":      func(_ *World, b *Seat) { b.State.HP = 0 },
		"unjoined":  func(_ *World, b *Seat) { b.Generation = 0 },
		"protected": func(w *World, b *Seat) { b.State.ProtectedUntilTick = w.Tick + 1 },
		"distant": func(w *World, b *Seat) {
			b.State.X = w.Seats["a"].hero.MeleeRange + w.Rules.PlayerRadius + .01
		},
		"behind": func(_ *World, b *Seat) { b.State.X = -1 },
		// A close side target can graze the wider sector with its body. Keep
		// this miss wholly outside the arc while still within weapon reach.
		"side": func(_ *World, b *Seat) { b.State.X, b.State.Z = 0, -2.2 },
		"cover": func(w *World, b *Seat) {
			b.State.X = 1.5
			w.Map = w.Map.Clone()
			w.Map.Obstacles = []shared.Obstacle{{ID: "thin-cover", X: .75, Z: -4, HalfX: .05, HalfZ: .5, Height: 1, BlocksMovement: true, BlocksProjectiles: true, Visual: "wall"}}
		},
	} {
		t.Run(name, func(t *testing.T) {
			w := meleeWorld(t)
			a, b := w.Seats["a"], w.Seats["b"]
			change(w, b)
			hp := b.State.HP
			a.State.ProtectedUntilTick = 60
			if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, AimX: 1}); code != "melee" || b.State.HP != hp || len(w.CombatEvents) != 1 || a.State.ProtectedUntilTick != 0 {
				t.Fatalf("invalid target hit or miss failed cooldown/protection: code=%s hp=%d/%d events=%+v", code, b.State.HP, hp, w.CombatEvents)
			}
		})
	}
}

func TestMeleeIgnoresProjectileCapacityAndUsesNormalDeathRespawn(t *testing.T) {
	w := meleeWorld(t)
	a, b := w.Seats["a"], w.Seats["b"]
	w.Projectiles = make([]shot, w.Rules.MaxProjectiles)
	b.State.HP = 30
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, AimX: 1}); code != "melee" || len(w.Projectiles) != w.Rules.MaxProjectiles || w.NextProjectileID != 0 || b.State.HP != 0 || b.State.RespawnTick != w.Tick+w.Rules.RespawnTicks {
		t.Fatal("melee consumed projectile capacity or failed normal death")
	}
	for w.Tick < w.Rules.RespawnTicks {
		w.Step()
	}
	if b.State.HP != 100 || b.State.RespawnTick != 0 || b.State.X != b.SpawnX || b.State.Z != b.SpawnZ || b.State.ProtectedUntilTick <= w.Tick {
		t.Fatal("melee death did not use ordinary respawn and protection")
	}
}

func TestMeleeCleaveHitsEachEnemyOnceInDeterministicPlayerOrder(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2", "c": "t2", "d": "t1"})
	tank, _ := w.Rules.Hero("tank")
	a, b, c, ally := w.Seats["a"], w.Seats["b"], w.Seats["c"], w.Seats["d"]
	a.hero, a.State.HeroID = &tank, "tank"
	a.State.X, a.State.Z = 0, -4
	b.State.X, b.State.Z, c.State.X, c.State.Z = 1.05, -3.4, 1.05, -4.6
	ally.State.X, ally.State.Z = .8, -4
	for _, seat := range w.Seats {
		seat.State.ProtectedUntilTick = 0
	}
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, AimX: 1}); code != "melee" || b.State.HP != 100-tank.MeleeDamage || c.State.HP != 100-tank.MeleeDamage || ally.State.HP != 100 || len(w.CombatEvents) != 3 {
		t.Fatalf("wrong cleave: code=%s hp=%d,%d ally=%d events=%+v", code, b.State.HP, c.State.HP, ally.State.HP, w.CombatEvents)
	}
	if w.CombatEvents[1].TargetID != b.State.PlayerID || w.CombatEvents[2].TargetID != c.State.PlayerID || w.CombatEvents[0].ID == w.CombatEvents[1].ID || w.CombatEvents[1].ID == w.CombatEvents[2].ID {
		t.Fatal("cleave event ordering or per-event IDs were nondeterministic")
	}
}

func TestMeleeEventsAreUniqueBoundedRetainedAndSnapshotCopied(t *testing.T) {
	w := meleeWorld(t)
	a := w.Seats["a"]
	for i := 0; i < maxCombatEvents+5; i++ {
		w.recordCombatEvent("melee_swing", a, "", 0, -4, 1, 0)
	}
	if len(w.CombatEvents) != maxCombatEvents || w.CombatEvents[0].ID != "m6" || w.CombatEvents[maxCombatEvents-1].ID != "m25" {
		t.Fatalf("event IDs or cap incorrect: %+v", w.CombatEvents)
	}
	first, second := w.Snapshot(a, "snapshot"), w.Snapshot(a, "snapshot")
	first.CombatEvents[0].X = 99
	if w.CombatEvents[0].X != 0 || second.CombatEvents[0].X != 0 {
		t.Fatal("wire melee events share mutable simulation state")
	}
	for range combatEventRetentionTicks {
		w.Step()
	}
	if len(w.CombatEvents) != maxCombatEvents {
		t.Fatal("melee event disappeared before retention window")
	}
	w.Step()
	if len(w.CombatEvents) != 0 {
		t.Fatal("melee event survived bounded retention")
	}
}

func TestWorstMixedMeleeAndProjectileSnapshotFitsReliableBound(t *testing.T) {
	roster := map[string]string{"a": "t1", "b": "t1", "c": "t2", "d": "t2", "e": "t3", "f": "t3", "g": "t4", "h": "t4"}
	w := combatWorld(t, roster)
	w.Tick = 1<<53 - 1
	w.NextCombatEventID = w.Tick - 64
	a := w.Seats["a"]
	for _, s := range w.Seats {
		s.State.X, s.State.Z = 9.599999999999999, -9.599999999999999
		s.State.Ack, s.ActionAck, s.ActionCode = w.Tick, w.Tick, "projectile_capacity"
		s.State.FireReadyTick, s.State.DashReadyTick, s.State.ProtectedUntilTick = w.Tick, w.Tick, w.Tick
		s.State.AttackStopUntilTick = w.Tick
		s.State.FacingX, s.State.FacingZ = .7071067811865476, -.7071067811865476
	}
	for range w.Rules.MaxProjectiles {
		w.Projectiles = append(w.Projectiles, shot{State: ProjectileState{ID: "bffffffffffffff", OwnerID: "p8", TeamID: "t4", X: 9.599999999999999, Z: -9.599999999999999, DX: .7071067811865476, DZ: -.7071067811865476, ExpiresTick: w.Tick}})
	}
	for range maxProjectileEnds {
		w.recordProjectileEnd(ProjectileState{ID: "bffffffffffffff", OwnerID: "p8"}, 9.599999999999999, -9.599999999999999, "player", "p8")
	}
	for range maxCombatEvents {
		w.recordCombatEvent("melee_hit", a, "p8", 9.599999999999999, -9.599999999999999, .7071067811865476, -.7071067811865476)
	}
	snapshot := w.Snapshot(a, "snapshot")
	data, err := json.Marshal(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	if len(snapshot.CombatEvents) != maxCombatEvents || len(snapshot.ProjectileEnds)+len(snapshot.CombatEvents) != maxMixedTerminalEvents || len(data) > protocol.MaxSnapshot {
		t.Fatalf("mixed event snapshot budget: ends=%d combat=%d bytes=%d max=%d", len(snapshot.ProjectileEnds), len(snapshot.CombatEvents), len(data), protocol.MaxSnapshot)
	}
	t.Logf("worst mixed snapshot: %d bytes / %d", len(data), protocol.MaxSnapshot)
}
