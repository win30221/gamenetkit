package main

import (
	"math"
	"testing"
)

func TestMeteorKnockbackAndWeight(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	light := testSoldier(201, 1, "griffin", 0, 1.2)
	heavy := testSoldier(202, 1, "guard", 1.5, 1.2)
	flying := testSoldier(203, 1, "falcon", -1.5, 1.2)
	flying.State.Airborne = true
	for _, u := range []*unit{light, heavy, flying} {
		u.State.HP = 250
		u.State.Radius = w.Rules.ByID[u.State.Kind].bodyRadius()
		u.State.ReadyTick = 9999
		w.Units[u.State.ID] = u
	}
	w.Hands[0][0] = "fireball"
	if result := w.deploy(0, w.ID, 1, "fireball", 0, 2); !result.Accepted {
		t.Fatal(result)
	}
	w.Tick = w.Effects[0].Impact - 1
	w.step()
	if heavy.State.PausedUntil != 0 || heavy.State.Z != 1.2 || heavy.State.HP != 140 {
		t.Fatal("threshold weight should only take damage", heavy.State)
	}
	if light.State.PausedUntil != w.Tick+9 || flying.State.PausedUntil != w.Tick+9 || light.State.HP != 140 || flying.State.HP != 140 {
		t.Fatal("light units in both layers must take damage and pause")
	}
	for range 8 {
		w.step()
	}
	if math.Abs(light.State.Z-(w.Map.WallHalfWidth+light.State.Radius)) > 1e-4 || w.Tick >= light.State.PausedUntil {
		t.Fatal("ground unit should meet the wall and retain its remaining pause", light.State)
	}
	wantZ := 1.2 - .8/math.Hypot(1.5, .8)
	if math.Abs(flying.State.Z-wantZ) > 1e-6 {
		t.Fatal("airborne knockback should travel its full distance", flying.State)
	}
}

func TestGriffinTransformPreservesPauseAndRetargetsGroundAttacker(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	g := testSoldier(201, 1, "griffin", 0, -4)
	g.State.HP, g.State.MaxHP = 70, 155
	g.State.TransformTick, g.State.PausedUntil = 300, 306
	b := testSoldier(202, 0, "bomber", 0, -5.5)
	b.LockedTarget, b.WindupTarget, b.WindupProgress = g.State.ID, g.State.ID, 20
	w.Units[g.State.ID], w.Units[b.State.ID] = g, b
	w.Tick = 299
	w.step()
	if !g.State.Airborne || g.State.HP != 140 || g.State.MaxHP != 310 || g.State.PausedUntil != 306 {
		t.Fatal("incorrect transformation", g.State)
	}
	if b.LockedTarget == g.State.ID || b.State.HitTarget == g.State.ID || b.WindupTarget == g.State.ID {
		t.Fatal("ground-only windup did not release the airborne target", b)
	}
}

func TestStandardAuraIncludesSelfAndDoesNotStack(t *testing.T) {
	w := playingMatch(t)
	a, b := testSoldier(201, 0, "standard", 0, -5), testSoldier(202, 0, "standard", 1, -5)
	f := testSoldier(203, 0, "falcon", 2, -5)
	f.State.Airborne = true
	units := []*unit{a, b, f}
	w.refreshUnitAuras(units)
	want := w.Rules.ByID["standard"].MoveMultiplier
	for _, u := range []*unit{a, f} {
		if got := w.movementFactor(u); math.Abs(got-want) > 1e-9 {
			t.Fatalf("expected one aura on %s, got %v", u.State.Kind, got)
		}
	}
}

func TestStandardAuraContactRefreshAndExpiry(t *testing.T) {
	w := playingMatch(t)
	w.Tick = 100
	card := w.Rules.ByID["standard"]
	a := testSoldier(201, 0, "standard", 0, -5)
	f := testSoldier(202, 0, "falcon", card.AuraRadius+.2, -5)
	f.State.Airborne = true
	f.State.Radius = .32
	enemy := testSoldier(203, 1, "guard", 0, -5)
	units := []*unit{a, f, enemy}
	check := func(u *unit, want float64) {
		t.Helper()
		if got := w.movementFactor(u); math.Abs(got-want) > 1e-9 {
			t.Fatalf("tick %d, %s: factor %v, want %v", w.Tick, u.State.Kind, got, want)
		}
	}
	a.State.ReadyTick = w.Tick + 1
	w.refreshUnitAuras(units)
	check(f, 1)
	w.Tick++
	w.refreshUnitAuras(units)
	check(f, card.MoveMultiplier)
	check(a, card.MoveMultiplier)
	check(enemy, 1)

	// Remaining in contact for longer than one second continuously renews it.
	for range 2 * tickRate {
		w.Tick++
		w.refreshUnitAuras(units)
		check(f, card.MoveMultiplier)
	}
	f.State.X = card.AuraRadius + f.State.Radius + .1
	w.refreshUnitAuras(units)
	w.Tick += tickRate - 1
	w.refreshUnitAuras(units)
	check(f, card.MoveMultiplier)
	w.Tick++
	w.refreshUnitAuras(units)
	check(f, 1)

	// Re-entry applies immediately; loss of the source does not cancel it.
	f.State.X = card.AuraRadius
	w.refreshUnitAuras(units)
	check(f, card.MoveMultiplier)
	a.State.HP = 0
	w.refreshUnitAuras(units)
	w.Tick += tickRate - 1
	w.refreshUnitAuras(units)
	check(f, card.MoveMultiplier)
	w.Tick++
	w.refreshUnitAuras(units)
	check(f, 1)
}

func TestMeteorDirectHitUsesSameDamageOnFortifications(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	tower := &w.Towers[1]
	w.Hands[1][0] = "fireball"
	if result := w.deploy(1, w.ID, 1, "fireball", tower.X, tower.Z); !result.Accepted {
		t.Fatal(result)
	}
	w.Tick = w.Effects[0].Impact - 1
	w.step()
	if tower.MaxHP-tower.HP != 110 {
		t.Fatal("meteor had different tower damage", tower.HP)
	}
}
