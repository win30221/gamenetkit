package main

import "testing"

// An exposed ranged unit must survive long enough to notice the engagement
// and cast a rescue spell, while an unanswered melee attack still wins.
func TestExposedArcherHasARescueWindow(t *testing.T) {
	for _, rescue := range []bool{false, true} {
		name := "unanswered"
		if rescue {
			name = "frost_after_two_seconds"
		}
		t.Run(name, func(t *testing.T) {
			w := playingMatch(t)
			w.Rules.TowerDamage = 0
			attacker := testSoldier(101, 0, "guard", 0, -5)
			archer := testSoldier(102, 1, "archer", 0, -4)
			archer.State.HP, archer.State.MaxHP = w.Rules.ByID["archer"].HP, w.Rules.ByID["archer"].HP
			archer.Cooldown = 10000
			w.Units[101], w.Units[102] = attacker, archer
			for range 2 * tickRate {
				w.step()
			}
			if w.Units[102] == nil || archer.State.HP >= archer.State.MaxHP {
				t.Fatal("archer did not survive two seconds of actual melee damage")
			}
			if rescue {
				w.Hands[1][0] = "frost"
				if result := w.deploy(1, w.ID, 1, "frost", 0, -5); !result.Accepted {
					t.Fatal("rescue spell was rejected", result)
				}
			}
			for range tickRate {
				w.step()
			}
			if alive := w.Units[102] != nil; alive != rescue {
				t.Fatalf("after three seconds: alive=%v rescue=%v", alive, rescue)
			}
		})
	}
}

func TestFrontTowerGivesTimeBetweenShots(t *testing.T) {
	w := playingMatch(t)
	guard := testSoldier(101, 0, "guard", -5, 5.9)
	guard.Cooldown = 10000
	w.Units[101] = guard
	w.step()
	firstHP := guard.State.HP
	if firstHP != guard.State.MaxHP-w.Rules.TowerDamage {
		t.Fatal("front tower did not fire its first shot")
	}
	for range tickRate {
		w.step()
	}
	if guard.State.HP != firstHP {
		t.Fatal("tower fired again within one second")
	}
	for range tickRate / 2 {
		w.step()
	}
	if guard.State.HP != firstHP-w.Rules.TowerDamage {
		t.Fatal("tower did not resume fire within one and a half seconds")
	}
}
