package sim

import (
	"context"
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func heroWorld(t *testing.T) *World {
	t.Helper()
	w, err := newMatchWorldWithMap("m-heroes", map[string]string{"a": "t1", "b": "t2"}, map[string]string{"a": "tank", "b": "ranger"}, shared.ArenaMap())
	if err != nil {
		t.Fatal(err)
	}
	for _, user := range []string{"a", "b"} {
		if _, err := w.Attach(user, false); err != nil {
			t.Fatal(err)
		}
	}
	for _, user := range []string{"a", "b"} {
		w.MarkLoaded(user, w.Seats[user].Generation)
	}
	if w.Phase != phasePlaying {
		t.Fatal("hero fixture did not load")
	}
	return w
}

func TestHeroSelectionIsFrozenAndAppearsInSnapshots(t *testing.T) {
	roster := map[string]string{"a": "t1", "b": "t2"}
	heroes := map[string]string{"a": "tank", "b": "ranger"}
	w, err := newMatchWorldWithHeroes("m-frozen-heroes", roster, heroes)
	if err != nil {
		t.Fatal(err)
	}
	heroes["a"] = "ranger"
	delete(heroes, "b")
	roster["a"] = "t4"
	w.Rules.Heroes[0].MaxHP = 999
	w.Rules.Heroes[1].Speed = 99
	if w.Seats["a"].State.HeroID != "tank" || w.Seats["a"].State.HP != 160 || w.seatRules(w.Seats["a"]).Speed != 5 ||
		w.Seats["b"].State.HeroID != "ranger" || w.seatRules(w.Seats["b"]).MaxHP != 100 {
		t.Fatal("live seats retained mutable hero input/catalog")
	}
	state := w.Snapshot(w.Seats["a"], "welcome")
	if state.Players[0].HeroID != "tank" || state.Players[1].HeroID != "ranger" || state.Players[0].TeamID != "t1" {
		t.Fatalf("snapshot lost frozen hero/team: %+v", state.Players)
	}
}

func TestHeroConstructorRejectsUnknownAndMismatchedSelections(t *testing.T) {
	roster := map[string]string{"a": "t1", "b": "t2"}
	for _, heroes := range []map[string]string{
		nil, {"a": "tank"}, {"a": "tank", "b": "ranger", "outsider": "tank"},
		{"a": "tank", "outsider": "ranger"}, {"a": "unknown", "b": "ranger"}, {"a": "", "b": "ranger"},
	} {
		if _, err := newMatchWorldWithHeroes("m-invalid-heroes", roster, heroes); err == nil {
			t.Fatalf("invalid selections accepted: %v", heroes)
		}
	}
	ctx, cancel := context.WithCancel(context.Background())
	engine, err := NewMatchEngineWithHeroes(ctx, "m-engine-heroes", roster, map[string]string{"a": "tank", "b": "ranger"})
	if err != nil {
		cancel()
		t.Fatal(err)
	}
	t.Cleanup(func() { cancel(); <-engine.Done })
	state, err := engine.InspectPlayer(ctx, "a")
	if err != nil || state.Players[0].HeroID != "tank" || state.Players[0].HP != 160 {
		t.Fatalf("engine constructor did not apply heroes: %+v, %v", state, err)
	}
}

func TestHeroMovementAndDashUseSelectedRules(t *testing.T) {
	w := heroWorld(t)
	a, b := w.Seats["a"], w.Seats["b"]
	ax, bx := a.State.X, b.State.X
	for _, user := range []string{"a", "b"} {
		s := w.Seats[user]
		if !w.Input(user, s.Generation, InputIntent{Generation: s.Generation, Seq: 1, X: 1}) {
			t.Fatal("move rejected")
		}
	}
	w.Step()
	if math.Abs(a.State.X-ax-5.0/30) > 1e-9 || math.Abs(b.State.X-bx-6.0/30) > 1e-9 {
		t.Fatal("hero walking speeds were ignored")
	}
	for _, user := range []string{"a", "b"} {
		s := w.Seats[user]
		w.Input(user, s.Generation, InputIntent{Generation: s.Generation, Seq: 2})
	}
	ax, bx = a.State.X, b.State.X
	for _, user := range []string{"a", "b"} {
		s := w.Seats[user]
		w.Input(user, s.Generation, InputIntent{Generation: s.Generation, Seq: 3, Action: "dash", ActionID: 1, ActionTick: w.Tick, AimZ: 1})
	}
	w.Step()
	if a.State.DashReadyTick != w.Tick+105 || b.State.DashReadyTick != w.Tick+90 {
		t.Fatal("hero dash cooldown ignored")
	}
	for range int(w.Rules.DashTicks) - 1 {
		w.Step()
	}
	if a.State.X != ax || b.State.X != bx || math.Abs(a.State.Z-12*6.0/30) > 1e-9 || math.Abs(b.State.Z-15*6.0/30) > 1e-9 {
		t.Fatal("hero dash travel ignored selected speed")
	}
}

func TestHeroAttacksUseMeleeOrCapturedRangedStatsAndOwnCooldown(t *testing.T) {
	w := heroWorld(t)
	a, b := w.Seats["a"], w.Seats["b"]
	for _, user := range []string{"a", "b"} {
		s := w.Seats[user]
		w.Input(user, s.Generation, InputIntent{Generation: s.Generation, Seq: 1, Action: "fire", ActionID: 1, ActionTick: w.Tick, AimZ: 1})
	}
	w.Step()
	if len(w.Projectiles) != 1 || a.State.FireReadyTick != w.Tick+a.hero.FireCooldownTicks || b.State.FireReadyTick != w.Tick+b.hero.FireCooldownTicks || a.ActionCode != "melee" || b.ActionCode != "fired" {
		t.Fatal("hero fire cooldown ignored")
	}
	rangerShot := w.Projectiles[0]
	if rangerShot.State.OwnerID != b.State.PlayerID || rangerShot.speed != 12 || rangerShot.damage != 25 || rangerShot.State.ExpiresTick != w.Tick+b.hero.ProjectileLifetimeTicks {
		t.Fatal("projectile did not capture selected stats")
	}
	if len(w.CombatEvents) != 1 || w.CombatEvents[0].Kind != "melee_swing" || w.CombatEvents[0].OwnerID != a.State.PlayerID {
		t.Fatal("tank did not publish a swing independently of projectiles")
	}
	for w.Tick < rangerShot.State.ExpiresTick {
		w.Step()
	}
	if len(w.Projectiles) != 0 {
		t.Fatal("ranger projectile survived its own range")
	}

	w = heroWorld(t)
	a, b = w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z, b.State.X, b.State.Z = 2, -4, -1, -4
	a.State.ProtectedUntilTick, b.State.ProtectedUntilTick = 0, 0
	combatAction(t, w, "b", 1, 1, "fire", 1, 0)
	if len(w.Projectiles) != 1 {
		t.Fatal("shot missing before impact")
	}
	initialX := w.Projectiles[0].State.X
	b.hero.ProjectileSpeed, b.hero.ProjectileDamage = 99, 99
	w.Rules.ProjectileSpeed, w.Rules.ProjectileDamage = 99, 99
	w.Step()
	if len(w.Projectiles) != 1 || math.Abs(w.Projectiles[0].State.X-initialX-12.0/30) > 1e-9 {
		t.Fatal("in-flight speed changed with owner/world rules")
	}
	for range 10 {
		w.Step()
	}
	if a.State.HP != 160-25 {
		t.Fatalf("in-flight shot did not preserve ranger damage: HP=%d", a.State.HP)
	}
}

func TestHeroReconnectAndRespawnKeepIdentityAndMaximumHP(t *testing.T) {
	w := heroWorld(t)
	a := w.Seats["a"]
	a.State.HP = 47
	a.State.FireReadyTick = 60
	id, generation := a.State.PlayerID, a.Generation
	w.Disconnect("a", a.Generation)
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	if a.State.HeroID != "tank" || a.State.PlayerID != id || a.State.HP != 47 || a.Generation != generation+1 || a.State.FireReadyTick != 60 {
		t.Fatal("reconnect changed frozen hero or combat state")
	}
	a.State.HP, a.State.RespawnTick = 0, w.Tick+2
	for range 2 {
		w.Step()
	}
	if a.State.HP != 160 || a.State.RespawnTick != 0 || a.State.HeroID != "tank" || a.State.X != a.SpawnX || a.State.Z != a.SpawnZ {
		t.Fatalf("tank respawn lost selected maximum/hero: %+v", a.State)
	}
}
