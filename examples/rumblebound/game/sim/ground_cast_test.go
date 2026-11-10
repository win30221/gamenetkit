package sim

import (
	"encoding/json"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"math"
	"strings"
	"testing"
)

func mageWorld(t *testing.T) (*World, *Seat, *Seat) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	w.Map = nil
	a, b := w.Seats["a"], w.Seats["b"]
	h, _ := w.Rules.Hero("mage")
	a.hero = &h
	a.State.HeroID = "mage"
	a.State.X, a.State.Z, b.State.X, b.State.Z = 0, 0, 3, 0
	a.State.ProtectedUntilTick, b.State.ProtectedUntilTick = 0, 0
	return w, a, b
}
func TestGroundSkillClampsAndExplosionHitsBeyondCastRange(t *testing.T) {
	w, a, b := mageWorld(t)
	b.State.X = a.hero.CastRange + .8
	hp := b.State.HP
	if code := w.resolveAction(a, actionIntent{Kind: "skill", Tick: w.Tick, HasGroundTarget: true, TargetX: b.State.X}); code != "cast" {
		t.Fatal(code)
	}
	cast := w.GroundCasts[0]
	if math.Abs(cast.X-a.hero.CastRange) > .0001 || cast.X > a.hero.CastRange || cast.Z != 0 {
		t.Fatalf("skill did not clamp radially: %+v", cast)
	}
	if code := w.resolveAction(a, actionIntent{Kind: "skill", Tick: w.Tick, HasGroundTarget: true, TargetX: b.State.X}); code != "cooldown" {
		t.Fatal("skill cooldown not enforced:", code)
	}
	for w.Tick <= cast.ImpactTick {
		w.Step()
	}
	if b.State.HP != hp-a.hero.GroundDamage {
		t.Fatal("edge explosion missed reachable enemy", b.State.HP)
	}
}
func TestGroundCastValidationAndFixedTarget(t *testing.T) {
	hero, _ := shared.Rules().Hero("mage")
	for _, tc := range []struct {
		name    string
		x, z    float64
		present bool
		want    string
	}{
		{"origin", 0, 0, true, "cast"}, {"boundary", hero.CastRange, 0, true, "cast"}, {"outside", hero.CastRange + .001, 0, true, "out_of_range"},
		{"arena", 11, 0, true, "invalid_target"}, {"missing", 0, 0, false, "invalid_target"}, {"NaN", math.NaN(), 0, true, "invalid_target"},
		{"infinity", 0, math.Inf(1), true, "invalid_target"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			w, a, _ := mageWorld(t)
			code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: tc.present, TargetX: tc.x, TargetZ: tc.z})
			if code != tc.want {
				t.Fatalf("got %s want %s", code, tc.want)
			}
			if code != "cast" && (a.State.FireReadyTick != 0 || len(w.GroundCasts) != 0) {
				t.Fatal("rejection consumed cast/cooldown")
			}
		})
	}
	w, a, b := mageWorld(t)
	intent := InputIntent{Generation: a.Generation, Seq: 1, Action: "fire", ActionID: 1, ActionTick: w.Tick, HasGroundTarget: true, TargetX: 3}
	if !w.Input("a", a.Generation, intent) {
		t.Fatal("input rejected")
	}
	w.Step()
	if len(w.GroundCasts) != 1 || a.ActionCode != "cast" {
		t.Fatal("cast not accepted")
	}
	impact := w.GroundCasts[0].ImpactTick
	intent.Seq = 2
	intent.TargetX = 5
	w.Input("a", a.Generation, intent)
	w.Step()
	a.State.X = -3
	a.State.HP = 0
	a.State.Online = false
	a.hero.GroundDamage = 999
	for w.Tick < impact {
		w.Step()
	}
	if b.State.HP != 65 || len(w.GroundCasts) != 0 || len(w.GroundImpacts) != 1 || w.GroundImpacts[0].X != 3 {
		t.Fatalf("fixed/captured cast failed: hp=%d casts=%+v", b.State.HP, w.GroundCasts)
	}
	w.Step()
	if b.State.HP != 65 {
		t.Fatal("duplicate damage")
	}
	snap := w.Snapshot(b, "snapshot")
	snap.GroundImpacts[0].Targets[0] = "mutated"
	if w.GroundImpacts[0].Targets[0] == "mutated" {
		t.Fatal("snapshot aliases target IDs")
	}
}
func TestGroundImpactUsesLandingPositionAndTargetEligibility(t *testing.T) {
	for _, kind := range []string{"enter", "exit", "friendly", "protected", "dead", "left", "offline", "grazing"} {
		t.Run(kind, func(t *testing.T) {
			w, a, b := mageWorld(t)
			b.State.X = 8
			if w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true, TargetX: 3}) != "cast" {
				t.Fatal("cast rejected")
			}
			b.State.X = 3
			want := 65
			switch kind {
			case "exit":
				b.State.X = 8
				want = 100
			case "friendly":
				b.State.TeamID = "t1"
				want = 100
			case "protected":
				b.State.ProtectedUntilTick = 100
				want = 100
			case "dead":
				b.State.HP = 0
				want = 0
			case "left":
				b.State.Left = true
				want = 100
			case "offline":
				b.State.Online = false
			case "grazing":
				b.State.X = 3 + a.hero.ImpactRadius + w.Rules.PlayerRadius - .0001
			}
			w.Tick = w.GroundCasts[0].ImpactTick
			w.advanceGroundCasts([]*Seat{a, b})
			if b.State.HP != want {
				t.Fatalf("hp=%d want %d", b.State.HP, want)
			}
		})
	}
}
func TestGroundCapacityCooldownAndFinish(t *testing.T) {
	w, a, _ := mageWorld(t)
	for i := 0; i < w.Rules.MaxActiveGroundCasts; i++ {
		a.State.FireReadyTick = 0
		if w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true}) != "cast" {
			t.Fatal("early capacity")
		}
	}
	a.State.FireReadyTick = 0
	if w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true}) != "cast_capacity" || a.State.FireReadyTick != 0 {
		t.Fatal("capacity rejection consumed cooldown")
	}
	w.finish("win", "score", "t1")
	if len(w.GroundCasts) != 0 || len(w.GroundImpacts) != 0 {
		t.Fatal("casts survive settlement")
	}
}
func TestMageMixedSnapshotBudget(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "team012345678901", "b": "team012345678902", "c": "team012345678901", "d": "team012345678902", "e": "team012345678901", "f": "team012345678902", "g": "team012345678901", "h": "team012345678902"})
	w.Tick = 9007199254740000
	for _, s := range w.Seats {
		s.State.AttackStopUntilTick = w.Tick + 12
	}
	for range w.Rules.MaxProjectiles {
		w.Projectiles = append(w.Projectiles, shot{State: ProjectileState{ID: "bffffffffffffff", OwnerID: "p8", TeamID: "team012345678901", X: 9.599999999999999, Z: -9.599999999999999, DX: .7071067811865476, DZ: -.7071067811865476, ExpiresTick: w.Tick + 100}})
	}
	for range maxCombatEvents {
		w.recordCombatEvent("melee_hit", w.Seats["a"], "p8", 9.599999999999999, -9.599999999999999, .7071067811865476, -.7071067811865476)
	}
	for range maxProjectileEnds {
		w.recordProjectileEnd(w.Projectiles[0].State, 9.599999999999999, -9.599999999999999, "player", "p8")
	}
	for range w.Rules.MaxActiveGroundCasts {
		w.GroundCasts = append(w.GroundCasts, groundCast{ID: "g" + strings.Repeat("f", 14), OwnerID: "p8", TeamID: "team012345678901", X: 9.599999999999999, Z: -9.599999999999999, Radius: 1.299999999999999, StartTick: w.Tick, FallStartTick: w.Tick + 6, ImpactTick: w.Tick + 24})
	}
	for range maxGroundImpacts {
		w.GroundImpacts = append(w.GroundImpacts, GroundImpact{ID: "g" + strings.Repeat("f", 14), OwnerID: "p8", X: 9.599999999999999, Z: -9.599999999999999, Radius: 1.299999999999999, Tick: w.Tick, Targets: []string{"p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8"}})
	}
	data, err := json.Marshal(w.Snapshot(w.Seats["a"], "snapshot"))
	if err != nil || len(data) > protocol.MaxSnapshot {
		t.Fatalf("snapshot bytes=%d err=%v", len(data), err)
	}
}

func TestGroundCoverAndReconnectState(t *testing.T) {
	w, a, b := mageWorld(t)
	w.Map = shared.ArenaMap()
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true, TargetX: 3.5}); code != "invalid_target" || a.State.FireReadyTick != 0 {
		t.Fatal("cast allowed inside cover")
	}
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true, TargetX: 2.8}); code != "cast" {
		t.Fatal(code)
	}
	b.State.X = 4.2
	welcome := w.Snapshot(a, "welcome")
	if len(welcome.GroundCasts) != 1 || welcome.GroundCasts[0].ImpactTick != w.GroundCasts[0].ImpactTick {
		t.Fatal("reconnect lost active cast")
	}
	welcome.GroundCasts[0].X = 99
	if w.GroundCasts[0].X == 99 {
		t.Fatal("snapshot aliases active cast")
	}
	w.Tick = w.GroundCasts[0].ImpactTick
	w.advanceGroundCasts([]*Seat{a, b})
	if b.State.HP != 100 {
		t.Fatal("explosion damaged through cover")
	}
	a.State.FireReadyTick = 0
	if code := w.resolveAction(a, actionIntent{Kind: "fire", Tick: w.Tick, HasGroundTarget: true, TargetX: 4.2}); code != "cast" {
		t.Fatal("descending cast cannot cross cover: ", code)
	}
	w.Tick = w.GroundCasts[0].ImpactTick
	w.advanceGroundCasts([]*Seat{a, b})
	if b.State.HP != 65 {
		t.Fatal("open landing behind cover did not hit")
	}
	w.Tick += groundImpactRetentionTicks + 1
	w.advanceGroundCasts([]*Seat{a, b})
	if len(w.GroundImpacts) != 0 {
		t.Fatal("impact events retained indefinitely")
	}
}
