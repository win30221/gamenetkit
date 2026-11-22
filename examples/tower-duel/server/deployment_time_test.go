package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func TestEveryTroopWaitsBeforeMovingAndSharesSquadReadyTime(t *testing.T) {
	for _, card := range defaultRules().Cards {
		if card.Type != "troop" {
			continue
		}
		for seat := range 2 {
			t.Run(card.ID+string(rune('0'+seat)), func(t *testing.T) {
				w := playingMatch(t)
				w.Tick = 100
				w.Rules.TowerDamage = 0
				w.Hands[seat][0] = card.ID
				w.Energy[seat] = 10
				z := -4.0
				if seat == 1 {
					z = -z
				}
				ack := w.deploy(seat, w.ID, 1, card.ID, -5, z)
				if !ack.Accepted || ack.Energy != 10-card.Cost || ack.HandVersion != 1 {
					t.Fatal("deployment did not charge and cycle immediately", ack)
				}
				initial := make(map[uint64]point)
				readyTick := uint64(0)
				if card.DeployTicks > 0 {
					readyTick = 100 + uint64(card.DeployTicks)
				}
				for id, u := range w.Units {
					initial[id] = position(u.State)
					if u.State.ReadyTick != readyTick {
						t.Fatal("troop/squad did not receive the one-second ready tick", u.State)
					}
				}
				for range max(0, card.DeployTicks-1) {
					w.step()
					for id, u := range w.Units {
						if position(u.State) != initial[id] || u.State.Target != 0 || u.State.AttackTick != 0 || u.State.HitTick != 0 || u.Travelled != 0 {
							t.Fatal("troop moved, acquired a target or attacked during deployment", u)
						}
					}
				}
				if duplicate := w.deploy(seat, w.ID, 1, card.ID, 5, z); duplicate != ack {
					t.Fatal("retry changed deployment or charged again", duplicate)
				}
				for _, state := range w.snapshot(1-seat, "welcome").Units {
					if state.ReadyTick != readyTick {
						t.Fatal("reconnect/replication lost the original ready time", state)
					}
				}
				w.step()
				for id, u := range w.Units {
					if position(u.State) == initial[id] || u.State.Target == 0 {
						t.Fatal("troop did not begin moving on its ready tick", u)
					}
				}
			})
		}
	}
}

func TestDeploymentTimeIsPerCardAndPrecedesAttackWindup(t *testing.T) {
	for _, ticks := range []int{0, 15, 30, 75} {
		w := playingMatch(t)
		w.Tick = 100
		w.Rules.TowerDamage = 0
		guard := w.Rules.ByID["guard"]
		guard.DeployTicks = ticks
		w.Rules.ByID["guard"] = guard
		ack := w.deploy(0, w.ID, 1, "guard", 0, -5)
		if !ack.Accepted {
			t.Fatal(ack)
		}
		u := w.Units[ack.UnitID]
		enemy := testSoldier(900, 1, "guard", 0, -4)
		enemy.Cooldown = 10000
		w.Units[900] = enemy
		ready := uint64(100 + max(1, ticks))
		for w.Tick+1 < ready {
			w.step()
			if u.State.AttackTick != 0 || u.WindupTarget != 0 || enemy.State.HP != enemy.State.MaxHP {
				t.Fatal("attack or windup began before deployment completed", ticks, u)
			}
		}
		w.step()
		if u.State.AttackTick != ready || enemy.State.HP != enemy.State.MaxHP {
			t.Fatal("deployment readiness did not start a fresh harmless windup", ticks, u)
		}
		for range guard.WindupTicks {
			w.step()
		}
		if u.State.HitTick != ready+uint64(guard.WindupTicks) || enemy.State.HP != enemy.State.MaxHP-guard.Damage {
			t.Fatal("first hit did not wait for deployment plus windup", ticks, u)
		}
	}
}

func TestDeployingTroopsRemainVulnerableAndFrostDoesNotDelayReadiness(t *testing.T) {
	w := playingMatch(t)
	w.Tick = 100
	w.Rules.TowerDamage = 0
	guard := w.Rules.ByID["guard"]
	guard.DeployTicks = 30
	w.Rules.ByID["guard"] = guard
	ack := w.deploy(0, w.ID, 1, "guard", 0, -5)
	u := w.Units[ack.UnitID]
	enemy := testSoldier(900, 1, "guard", 0, -4)
	w.Units[900] = enemy
	w.Hands[1][0] = "frost"
	if result := w.deploy(1, w.ID, 1, "frost", 0, -5); !result.Accepted {
		t.Fatal(result)
	}
	for range 29 {
		w.step()
	}
	if u.State.HP >= u.State.MaxHP || !u.State.Slowed || u.State.AttackTick != 0 || u.State.ReadyTick != 130 {
		t.Fatal("deploying troop was invulnerable, acted early or had its timer slowed", u)
	}
	w.step()
	if u.State.AttackTick != 130 {
		t.Fatal("frost delayed completion of deployment", u)
	}

	w = playingMatch(t)
	w.Hands[1][0] = "fireball"
	if result := w.deploy(1, w.ID, 1, "fireball", -5, -5); !result.Accepted {
		t.Fatal(result)
	}
	for range w.Rules.ByID["fireball"].DelayTicks {
		w.step()
	}
	w.Hands[0][0] = "archer"
	ack = w.deploy(0, w.ID, 1, "archer", -5, -5)
	u = w.Units[ack.UnitID]
	u.State.HP = min(u.State.HP, w.Rules.ByID["fireball"].Damage)
	w.step()
	if w.Units[ack.UnitID] != nil || u.State.AttackTick != 0 || u.State.HitTick != 0 {
		t.Fatal("a troop killed during deployment survived or attacked", u)
	}
}

func TestDeploymentRulesRejectInvalidTimesAndLeaveSpellsIndependent(t *testing.T) {
	data, err := os.ReadFile("../client/Assets/Resources/TowerDuel/rules.json")
	if err != nil {
		t.Fatal(err)
	}
	for _, ticks := range []int{-1, 301} {
		var config rules
		if err := json.Unmarshal(data, &config); err != nil {
			t.Fatal(err)
		}
		config.Cards[0].DeployTicks = ticks
		encoded, _ := json.Marshal(config)
		path := filepath.Join(t.TempDir(), "rules.json")
		if err := os.WriteFile(path, encoded, 0600); err != nil {
			t.Fatal(err)
		}
		if _, err := loadRules(path); err == nil {
			t.Fatal("invalid deployment time accepted", ticks)
		}
	}
	w := playingMatch(t)
	w.Tick = 100
	w.Hands[0][0] = "frost"
	if ack := w.deploy(0, w.ID, 1, "frost", 0, -5); !ack.Accepted || len(w.Effects) != 1 || w.Effects[0].Impact != 101 || len(w.Units) != 0 {
		t.Fatal("troop deployment delay changed frost casting", ack)
	}
}
