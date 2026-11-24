package main

import (
	"math"
	"testing"
)

func TestBuffSetRefreshExpiryAndComposition(t *testing.T) {
	var b buffSet
	strong := buffKey{Stat: buffMoveSpeed, SourceID: 1, Kind: "aura"}
	weak := buffKey{Stat: buffMoveSpeed, SourceID: 2, Kind: "aura"}
	frost := buffKey{Stat: buffMoveSpeed, SourceID: 3, Kind: "frost", Scope: buffZone}
	attack := buffKey{Stat: buffAttackSpeed, SourceID: 1, Kind: "haste"}
	check := func(stat buffStat, tick uint64, want float64) {
		t.Helper()
		if got := b.factor(stat, tick); math.Abs(got-want) > 1e-9 {
			t.Fatalf("%s at %d: got %v, want %v", stat, tick, got, want)
		}
	}
	check(buffMoveSpeed, 10, 1)
	b.apply(strong, 1.5, 20, 10)
	b.apply(weak, 1.2, 40, 10)
	b.apply(frost, .5, 50, 10)
	b.apply(attack, 2, 50, 10)
	check(buffMoveSpeed, 10, .75)
	check(buffAttackSpeed, 10, 2)
	b.apply(strong, 1.5, 30, 19)
	if len(b.active) != 4 {
		t.Fatal("refresh duplicated a source")
	}
	check(buffMoveSpeed, 29, .75)
	b.expire(30)
	check(buffMoveSpeed, 30, .6)
	if !b.hasPenalty(buffMoveSpeed, 30) {
		t.Fatal("active slow was hidden by a positive buff")
	}
	b.clearScope(buffZone)
	check(buffMoveSpeed, 30, 1.2)
	check(buffAttackSpeed, 30, 2)
	b.expire(40)
	check(buffMoveSpeed, 40, 1)
	b.expire(50)
	check(buffAttackSpeed, 50, 1)
	if len(b.active) != 0 {
		t.Fatal("expired effects were not removed")
	}
}

func TestBuffSetKeepsStrongestPenaltyAndRejectsInvalidEffects(t *testing.T) {
	var b buffSet
	strong := buffKey{Stat: buffDamageTaken, SourceID: 1}
	weak := buffKey{Stat: buffDamageTaken, SourceID: 2}
	b.apply(strong, .5, 20, 10)
	b.apply(weak, .75, 30, 10)
	b.apply(strong, math.NaN(), 40, 10)
	b.apply(strong, math.Inf(1), 40, 10)
	b.apply(strong, 0, 40, 10)
	b.apply(strong, .1, 10, 10)
	if b.factor(buffDamageTaken, 19) != .5 || b.factor(buffDamageTaken, 20) != .75 || b.factor(buffDamageTaken, 30) != 1 {
		t.Fatal("penalty composition or expiry changed")
	}
}

func TestStandardAndFrostShareBuffsWithoutChangingAttackSpeed(t *testing.T) {
	w := playingMatch(t)
	w.Tick = 10
	a := testSoldier(201, 0, "standard", 0, -5)
	u := testSoldier(202, 0, "guard", 1, -5)
	units := []*unit{a, u}
	w.Effects = []effectState{{ID: 900, Kind: "frost", Seat: 1, X: 1, Z: -5, Radius: 2.8, Impact: 1, End: 100, Slow: .5}}
	w.refreshUnitZoneBuffs(units)
	w.refreshUnitAuras(units)
	boost := w.Rules.ByID["standard"].MoveMultiplier
	if math.Abs(w.movementFactor(u)-boost*.5) > 1e-9 || w.attackSpeedFactor(u) != .5 || !u.State.Slowed {
		t.Fatal("aura and frost should compose on movement only")
	}
	u.State.X = 7
	w.refreshUnitZoneBuffs(units)
	w.refreshUnitAuras(units)
	if math.Abs(w.movementFactor(u)-boost) > 1e-9 || w.attackSpeedFactor(u) != 1 || u.State.Slowed {
		t.Fatal("leaving frost must clear only the zone effect")
	}
	w.Tick += tickRate
	w.refreshUnitZoneBuffs(units)
	w.refreshUnitAuras(units)
	if w.movementFactor(u) != 1 {
		t.Fatal("timed aura did not expire")
	}
}

func TestCombatConsumesAttackSpeedDamageAndDamageTakenBuffs(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	u := testSoldier(101, 0, "guard", w.Towers[4].X, 5.7)
	w.Units[u.State.ID] = u
	tower := &w.Towers[4]
	u.Buffs.apply(buffKey{Stat: buffAttackSpeed, SourceID: 1}, 2, 1000, w.Tick)
	u.Buffs.apply(buffKey{Stat: buffDamageDealt, SourceID: 1}, 2, 1000, w.Tick)
	w.towerBuffs(tower.ID).apply(buffKey{Stat: buffDamageTaken, SourceID: 2}, .75, 1000, w.Tick)
	card := w.Rules.ByID[u.State.Kind]
	w.step()
	if u.WindupTarget != tower.ID {
		t.Fatal("guard did not start its windup against the tower", u.State)
	}
	steps := (card.WindupTicks + 1) / 2
	for i := 1; i < steps; i++ {
		w.step()
		if u.State.HitTick != 0 {
			t.Fatal("attack finished before doubled windup speed allowed")
		}
	}
	w.step()
	want := int(math.Round(float64(card.Damage) * 1.5))
	if u.State.HitTick != w.Tick || tower.MaxHP-tower.HP != want {
		t.Fatalf("buffed attack: tick=%d, hit=%d, damage=%d, want=%d", w.Tick, u.State.HitTick, tower.MaxHP-tower.HP, want)
	}
}
