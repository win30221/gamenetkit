package main

import "testing"

func testSoldier(id uint64, seat int, kind string, x, z float64) *unit {
	return &unit{State: entity{ID: id, Seat: seat, Kind: kind, X: x, Z: z, Radius: guardRadius, HP: 10000, MaxHP: 10000}}
}

func TestSoldierSwitchesToCloserEnemyDuringWindup(t *testing.T) {
	for _, kind := range []string{"guard", "archer", "swords", "bomber", "charger"} {
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
			if u.State.Target != 202 || u.LockedTarget != 0 || u.WindupTarget != 0 {
				t.Fatal("closer enemy did not replace the previous target and cancel its windup", u.State)
			}
			for range w.Rules.ByID[kind].WindupTicks {
				w.step()
			}
			if old.State.HP != 10000 || closer.State.HP != 10000 || u.State.HitTick != 0 {
				t.Fatal("cancelled windup should not damage either target", u.State)
			}
		})
	}
}

func TestSoldierSwitchesFromTowerToCloserDefender(t *testing.T) {
	w := playingMatch(t)
	w.Rules.TowerDamage = 0
	u := testSoldier(101, 0, "guard", w.Towers[4].X, 5.7)
	w.Units[101] = u
	w.step()
	if u.LockedTarget != 5 {
		t.Fatal("did not lock the front tower", u.State)
	}
	enemy := testSoldier(201, 1, "guard", u.State.X+.8, 5.7)
	enemy.Cooldown = 10000
	w.Units[201] = enemy
	w.step()
	if u.State.Target != 201 || u.WindupTarget != 0 {
		t.Fatal("closer defender did not replace the tower target", u.State)
	}
	delete(w.Units, 201)
	w.step()
	if u.State.Target != 5 {
		t.Fatal("did not reacquire the nearest tower", u.State)
	}
}

func TestFrontTowerShootsNearestEnemyInRange(t *testing.T) {
	w := playingMatch(t)
	a := testSoldier(201, 1, "archer", w.Towers[1].X, -4.7)
	a.Cooldown = 10000
	w.Units[201] = a
	w.step()
	tower := &w.Towers[1]
	if tower.Target != 201 {
		t.Fatal("tower did not acquire first soldier", tower)
	}
	b := testSoldier(202, 1, "archer", w.Towers[1].X, -5.7)
	b.Cooldown = 10000
	w.Units[202] = b
	w.TowerCooldown[tower.ID] = 0
	w.step()
	if tower.Target != 202 {
		t.Fatal("tower did not switch to the closer soldier", tower)
	}
	b.State.X, b.State.Z = 8, 6
	w.TowerCooldown[tower.ID] = 0
	w.step()
	if tower.Target != 201 || tower.AttackTick != w.Tick {
		t.Fatal("tower did not fire at the remaining enemy in range", tower)
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
			if trigger == "damage" {
				if keep.Active || keep.HP != keep.MaxHP {
					t.Fatal("windup activated the keep before damage")
				}
				for range w.Rules.ByID[enemy.State.Kind].WindupTicks {
					w.step()
				}
			}
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

func TestIndependentBuildingRanges(t *testing.T) {
	for _, kind := range []string{"tower", "keep"} {
		t.Run(kind, func(t *testing.T) {
			w := playingMatch(t)
			w.Rules.TowerRange, w.Rules.KeepRange = 3, 6
			building := w.Towers[1]
			expected := 3.0
			if kind == "keep" {
				building = w.Towers[0]
				expected = 6
			}
			building.Active = true
			w.Towers = []entity{building}
			enemy := testSoldier(201, 1, "archer", building.X, building.Z)
			enemy.State.ReadyTick = w.Tick + 100
			w.Units[201] = enemy
			edge := building.Z + building.Radius + enemy.State.Radius + expected
			enemy.State.Z = edge + 0.01
			w.step()
			if w.Towers[0].Target != 0 {
				t.Fatal("acquired outside configured range")
			}
			enemy.State.Z = edge - 0.01
			w.step()
			if w.Towers[0].Target != enemy.State.ID || enemy.State.HP != enemy.State.MaxHP-w.Rules.TowerDamage {
				t.Fatal("did not acquire and hit inside configured range")
			}
			enemy.State.Z = edge + 0.01
			w.TowerCooldown[building.ID] = 0
			lastAttack := w.Towers[0].AttackTick
			w.step()
			if w.Towers[0].AttackTick != lastAttack {
				t.Fatal("fired on a locked target outside configured range")
			}
		})
	}
}
