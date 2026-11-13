package main

import "testing"

func testSoldier(id uint64, seat int, kind string, x, z float64) *unit {
	return &unit{State: entity{ID: id, Seat: seat, Kind: kind, X: x, Z: z, Radius: guardRadius, HP: 10000, MaxHP: 10000}}
}

func TestSoldierKeepsAttackedTargetUntilDeath(t *testing.T) {
	for _, kind := range []string{"guard", "archer", "swords"} {
		t.Run(kind, func(t *testing.T) {
			w := playingMatch(t)
			w.Rules.TowerDamage = 0
			u := testSoldier(101, 0, kind, 0, -5)
			old := testSoldier(201, 1, "guard", 0, -3.85)
			old.Cooldown = 10000
			w.Units[101], w.Units[201] = u, old
			w.step()
			if u.LockedTarget != 201 || u.State.AttackTick == 0 {
				t.Fatal("did not start attacking", u.State)
			}
			closer := testSoldier(202, 1, "guard", .75, -5)
			closer.Cooldown = 10000
			w.Units[202] = closer
			w.step()
			if u.State.Target != 201 || closer.State.HP != 10000 {
				t.Fatal("switched to the closer enemy", u.State)
			}
			old.State.X, old.State.Z = 8, 5
			w.step()
			if u.State.Target != 201 {
				t.Fatal("dropped living target outside aggro range", u.State)
			}
			delete(w.Units, 201)
			w.step()
			if u.State.Target != 202 {
				t.Fatal("did not acquire a new enemy after death", u.State)
			}
		})
	}
}

func TestSoldierKeepsAttackingTowerWhenDefenderArrives(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	u := testSoldier(101, 0, "guard", -5, 5.7)
	w.Units[101] = u
	w.step()
	if u.LockedTarget != 5 {
		t.Fatal("did not lock the front tower", u.State)
	}
	enemy := testSoldier(201, 1, "guard", -4.2, 5.7)
	enemy.Cooldown = 10000
	w.Units[201] = enemy
	w.step()
	if u.State.Target != 5 {
		t.Fatal("defender pulled attacker off its tower", u.State)
	}
	w.Towers[4].HP = 0
	w.step()
	if u.State.Target != 201 {
		t.Fatal("did not retarget after the tower fell", u.State)
	}
}

func TestFrontTowerKeepsTargetUntilDeath(t *testing.T) {
	w := playingMatch(t)
	a := testSoldier(201, 1, "archer", -5, -4.7)
	a.Cooldown = 10000
	w.Units[201] = a
	w.step()
	tower := &w.Towers[1]
	if tower.Target != 201 {
		t.Fatal("tower did not acquire first soldier", tower)
	}
	b := testSoldier(202, 1, "archer", -5, -5.7)
	b.Cooldown = 10000
	w.Units[202] = b
	w.TowerCooldown[tower.ID] = 0
	w.step()
	if tower.Target != 201 {
		t.Fatal("tower switched to closer soldier", tower)
	}
	a.State.X, a.State.Z = 8, 6
	w.TowerCooldown[tower.ID] = 0
	tick := tower.AttackTick
	w.step()
	if tower.Target != 201 || tower.AttackTick != tick {
		t.Fatal("tower changed a living out-of-range lock", tower)
	}
	delete(w.Units, 201)
	w.step()
	if tower.Target != 202 {
		t.Fatal("tower failed to acquire after target death", tower)
	}
}

func TestKeepSleepsAndWakesOnOwnFrontTowerOrDamage(t *testing.T) {
	for _, trigger := range []string{"left", "right", "damage"} {
		t.Run(trigger, func(t *testing.T) {
			w := playingMatch(t)
			enemy := testSoldier(201, 1, "guard", 0, -7)
			enemy.Cooldown = 10000
			w.Units[201] = enemy
			w.step()
			keep := &w.Towers[0]
			if keep.Active || keep.AttackTick != 0 {
				t.Fatal("keep fired before activation", keep)
			}
			switch trigger {
			case "left":
				w.Towers[1].HP = 0
			case "right":
				w.Towers[2].HP = 0
			case "damage":
				enemy.State.X, enemy.State.Z = 0, -9
				enemy.Cooldown = 0
			}
			w.step()
			if !keep.Active || keep.AttackTick == 0 || keep.Target != 201 {
				t.Fatal("keep did not activate and retaliate", keep)
			}
			if trigger == "damage" && keep.HP == keep.MaxHP {
				t.Fatal("damage activation was not tested")
			}
			if w.Towers[3].Active {
				t.Fatal("other player's keep activated")
			}
			delete(w.Units, 201)
			w.step()
			if !keep.Active {
				t.Fatal("keep went back to sleep")
			}
		})
	}
}
