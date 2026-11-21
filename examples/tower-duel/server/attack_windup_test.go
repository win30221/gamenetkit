package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"math"
	"os"
	"path/filepath"
	"testing"
)

func TestEveryTroopWaitsForWindupAndKeepsAttackInterval(t *testing.T) {
	for _, card := range defaultRules().Cards {
		if card.Type != "troop" {
			continue
		}
		t.Run(card.ID, func(t *testing.T) {
			w := playingMatch(t)
			w.Rules.TowerDamage = 0
			u := testSoldier(101, 0, card.ID, -5, 5.9)
			w.Units[u.State.ID] = u
			tower := &w.Towers[4]
			w.step()
			start := w.Tick
			if u.State.AttackTick != start || u.WindupTarget != tower.ID || tower.HP != tower.MaxHP || u.State.HitTick != 0 {
				t.Fatal("attack did not begin with a harmless windup", u, tower)
			}
			for i := 1; i < card.WindupTicks; i++ {
				w.step()
				if tower.HP != tower.MaxHP || u.State.HitTick != 0 {
					t.Fatal("damage landed before windup ended", w.Tick, u.State, tower)
				}
			}
			w.step()
			if tower.HP != tower.MaxHP-card.Damage || u.State.HitTick != start+uint64(card.WindupTicks) || u.State.HitTarget != tower.ID || u.WindupTarget != 0 {
				t.Fatal("windup did not produce exactly one hit", u, tower)
			}
			for w.Tick < start+uint64(card.Cooldown+card.WindupTicks) {
				w.step()
				if w.Tick < start+uint64(card.Cooldown+card.WindupTicks) && tower.HP != tower.MaxHP-card.Damage {
					t.Fatal("recovery produced another hit early", w.Tick, tower)
				}
			}
			if tower.HP != tower.MaxHP-2*card.Damage || u.State.AttackTick != start+uint64(card.Cooldown) || u.State.HitTick != w.Tick {
				t.Fatal("windup changed the interval between attacks", u.State, tower)
			}
		})
	}
}

func TestDeathDuringWindupCancelsEveryTroopAttack(t *testing.T) {
	for _, card := range defaultRules().Cards {
		if card.Type != "troop" {
			continue
		}
		for _, killer := range []string{"tower", "fireball"} {
			t.Run(card.ID+"/"+killer, func(t *testing.T) {
				w := playingMatch(t)
				u := testSoldier(101, 0, card.ID, -5, 5.9)
				u.State.HP = w.Rules.TowerDamage
				u.Travelled = 3 // A charged strike must be cancelled too.
				w.Units[u.State.ID] = u
				if killer == "fireball" {
					w.Rules.TowerDamage = 0
					impact := uint64(card.WindupTicks) // Last tick before the scheduled hit.
					w.Effects = []effectState{{ID: 900, Kind: "fireball", Seat: 1, X: -5, Z: 5.9, Radius: 2.2, Start: 1, Impact: impact, End: impact + 18, Damage: 110}}
				}
				for range card.Cooldown + card.WindupTicks + 1 {
					w.step()
				}
				if w.Units[u.State.ID] != nil || u.State.AttackTick == 0 || u.State.HitTick != 0 || u.WindupTarget != 0 {
					t.Fatal("dead soldier completed its pending attack", u)
				}
				for _, tower := range w.Towers {
					if tower.HP != tower.MaxHP {
						t.Fatal("cancelled attack still damaged a building", tower)
					}
				}
			})
		}
	}
}

func TestEarlierSoldierHitCancelsRetaliationRegardlessOfIDOrder(t *testing.T) {
	for _, id := range []uint64{101, 301} {
		w := playingMatch(t)
		w.Rules.TowerDamage = 0
		archer := testSoldier(id, 0, "archer", 0, -5)
		archer.State.HP = w.Rules.ByID["guard"].Damage
		guard := testSoldier(201, 1, "guard", 0, -4)
		w.Units[id], w.Units[201] = archer, guard
		for range w.Rules.ByID["archer"].Cooldown + 1 {
			w.step()
		}
		if w.Units[id] != nil || archer.State.AttackTick == 0 || archer.State.HitTick != 0 || guard.State.HP != guard.State.MaxHP {
			t.Fatal("slower windup retaliated after death", id, archer.State, guard.State)
		}
	}
}

func TestWindupCannotTransferToReplacementOrOutOfRangeTarget(t *testing.T) {
	for _, change := range []string{"death", "out_of_range"} {
		t.Run(change, func(t *testing.T) {
			w := playingMatch(t)
			w.Rules.TowerDamage = 0
			u := testSoldier(101, 0, "archer", 0, -5)
			old := testSoldier(201, 1, "guard", 0, -3.85)
			old.Cooldown = 10000
			w.Units[101], w.Units[201] = u, old
			card := w.Rules.ByID[u.State.Kind]
			for range card.WindupTicks {
				w.step()
			}
			replacement := testSoldier(202, 1, "guard", .75, -5)
			replacement.Cooldown = 10000
			if change == "death" {
				delete(w.Units, 201)
				w.Units[202] = replacement
			} else {
				old.State.X = 8
			}
			w.step()
			if u.State.HitTick != 0 || u.WindupTarget != 0 || replacement.State.HP != replacement.State.MaxHP || old.State.HP != old.State.MaxHP {
				t.Fatal("invalid target inherited the previous windup", u, old.State, replacement.State)
			}
			if change == "out_of_range" {
				old.State.X = 0
			}
			for w.Tick < uint64(1+card.Cooldown+card.WindupTicks) {
				w.step()
				if w.Tick < uint64(1+card.Cooldown+card.WindupTicks) && u.State.HitTick != 0 {
					t.Fatal("resumed attack skipped its own windup", u.State)
				}
			}
			if u.State.HitTick != w.Tick {
				t.Fatal("soldier did not resume with a fresh windup", u)
			}
		})
	}
}

func TestFrostSlowsWindup(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	u := testSoldier(101, 0, "guard", -5, 5.7)
	w.Units[101] = u
	slow := w.Rules.ByID["frost"].Slow
	w.Effects = []effectState{{ID: 900, Kind: "frost", Seat: 1, X: -5, Z: 5.7, Radius: 2.8, Impact: 1, End: 200, Slow: slow}}
	w.step()
	card := w.Rules.ByID[u.State.Kind]
	steps := int(math.Ceil(float64(card.WindupTicks) / slow))
	for i := 1; i < steps; i++ {
		w.step()
		if u.State.HitTick != 0 || w.Towers[4].HP != w.Towers[4].MaxHP {
			t.Fatal("slowed attack finished its windup early", w.Tick, u)
		}
	}
	w.step()
	if !u.State.Slowed || u.State.HitTick != w.Tick || w.Towers[4].HP != w.Towers[4].MaxHP-card.Damage {
		t.Fatal("slowed windup did not finish on schedule", w.Tick, u)
	}
}

func TestCompletedWindupsCanKillEachOtherOnSameTick(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	a := testSoldier(101, 0, "guard", 0, -5)
	b := testSoldier(201, 1, "guard", 0, -4)
	a.State.HP, b.State.HP = 32, 32
	w.Units[101], w.Units[201] = a, b
	for range w.Rules.ByID["guard"].WindupTicks + 1 {
		w.step()
	}
	if len(w.Units) != 0 || a.State.HitTick != w.Tick || b.State.HitTick != w.Tick {
		t.Fatal("simultaneously completed attacks depended on iteration order", a.State, b.State)
	}
}

func TestWindupRulesAreRequiredAndSharedWithClient(t *testing.T) {
	canonicalPath := "../client/Assets/Resources/TowerDuel/rules.json"
	canonical, err := os.ReadFile(canonicalPath)
	if err != nil {
		t.Fatal(err)
	}
	sum := sha256.Sum256(canonical)
	expectedHash := hex.EncodeToString(sum[:])
	loaded, err := loadRules(canonicalPath)
	if err != nil {
		t.Fatal(err)
	}
	if loaded.Hash != expectedHash {
		t.Fatalf("rules hash mismatch: got %s, want %s", loaded.Hash, expectedHash)
	}
	cooldown := defaultRules().ByID["guard"].Cooldown
	for _, ticks := range []int{-1, 0, cooldown, cooldown + 1} {
		var config rules
		if err := json.Unmarshal(canonical, &config); err != nil {
			t.Fatal(err)
		}
		config.Cards[0].WindupTicks = ticks
		data, err := json.Marshal(config)
		if err != nil {
			t.Fatal(err)
		}
		path := filepath.Join(t.TempDir(), "rules.json")
		if err := os.WriteFile(path, data, 0600); err != nil {
			t.Fatal(err)
		}
		if _, err := loadRules(path); err == nil {
			t.Fatal("invalid windup accepted", ticks)
		}
	}
}
