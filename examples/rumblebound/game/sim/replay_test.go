//go:build !rf_no_replay

package sim

import (
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func startFixtureReplay(t *testing.T, w *World) (string, func()) {
	t.Helper()
	ctx, e := WithReplayDirectory(context.Background(), t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	sink, e := w.startReplay(ctx)
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(func() {
		if err := sink.Close(); err != nil {
			t.Error(err)
		}
	})
	return sink.Path(), func() {
		if e := sink.Close(); e != nil {
			t.Fatal(e)
		}
	}
}

func TestReplayLockedAttackWindupCancelAndSkill(t *testing.T) {
	m := shared.ArenaMap()
	m.MovementScale = .8
	m.Obstacles = nil
	for i := range m.SpawnLayouts {
		if m.SpawnLayouts[i].TeamCount == 2 {
			m.SpawnLayouts[i].Anchors = []shared.Point{{X: -2}, {X: 2}}
		}
	}
	w, err := newMatchWorldWithMap("replay-basic", map[string]string{"a": "blue", "b": "red"}, map[string]string{"a": "ranger", "b": "ranger"}, m)
	if err != nil {
		t.Fatal(err)
	}
	path, closeFile := startFixtureReplay(t, w)
	a, _ := w.Attach("a", false)
	b, _ := w.Attach("b", false)
	w.MarkLoaded("a", a.Generation)
	w.MarkLoaded("b", b.Generation)
	w.Step()
	attack := func(seq uint64) {
		w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: seq, Action: "attack", ActionID: seq, ActionTick: w.Tick, TargetID: b.State.PlayerID, TargetLife: b.Life})
		w.Step()
	}
	attack(1)
	w.Step()
	w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 2, Action: "cancel", ActionID: 2, ActionTick: w.Tick})
	w.Step()
	if b.State.HP != 100 {
		t.Fatal("cancelled attack hit")
	}
	attack(3)
	for i := 0; i < 10; i++ {
		w.Step()
	}
	if b.State.HP != 100-a.hero.BasicDamage {
		t.Fatal("locked attack not committed")
	}
	w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 4, Action: "skill", ActionID: 4, ActionTick: w.Tick, AimZ: 1})
	w.Step()
	for i := 0; i < 25; i++ {
		w.Step()
	}
	closeFile()
	if report := VerifyReplay(path); report.Status != "PASS" {
		t.Fatalf("new combat replay diverged: %+v", report)
	}
}
func TestReplayFullSimulationAndExternalEvents(t *testing.T) {
	for _, mode := range []string{"lab", "match", "practice", "ground"} {
		t.Run(mode, func(t *testing.T) {
			var w *World
			var e error
			roster := map[string]string{"alice": "red", "bob": "blue"}
			heroes := map[string]string{"alice": "tank", "bob": "ranger"}
			switch mode {
			case "lab":
				w = New()
			case "match":
				w, e = newMatchWorldWithMap("replay-match", roster, heroes, shared.ArenaMap())
			case "practice":
				w, e = newPracticeWorld("replay-practice", roster, heroes, shared.ArenaMap(), "alice", "bob", "capture+attack+dodge+kite")
			case "ground":
				heroes["alice"] = "mage"
				w, e = newMatchWorldWithMap("replay-ground", roster, heroes, shared.ArenaMap())
			}
			if e != nil {
				t.Fatal(e)
			}
			path, closeFile := startFixtureReplay(t, w)
			now := time.Unix(1700000000, 0)
			w.Now = func() time.Time { return now }
			a, e := w.Attach("alice", false)
			if e != nil {
				t.Fatal(e)
			}
			if mode != "practice" {
				if _, e = w.Attach("bob", false); e != nil {
					t.Fatal(e)
				}
			}
			if historyFactory != nil && rewindFactory != nil {
				w.configureNet("alice", a.Generation, netFixtureOptions())
			}
			w.MarkLoaded("alice", a.Generation)
			if mode != "practice" {
				w.MarkLoaded("bob", w.Seats["bob"].Generation)
			}
			for tick := 0; tick < 420; tick++ {
				now = now.Add(time.Second / 30)
				input := InputIntent{Generation: a.Generation, Seq: uint64(tick + 1), X: float64((tick/40)%3 - 1), Z: float64((tick/70)%3 - 1), Echo: float64(tick) * .125, FacingX: 1}
				if tick%20 == 0 {
					input.Action = "fire"
					input.ActionID = input.Seq
					input.ActionTick = w.Tick
					input.AimX = 1
					input.ViewTick = w.Tick
					input.ObservedTick = w.Tick
				}
				if mode == "ground" && input.Action != "" {
					input.HasGroundTarget = true
					input.TargetX = 0
					input.TargetZ = 0
				}
				if tick%50 == 10 {
					input.Action = "dash"
					input.ActionID = input.Seq
					input.ActionTick = w.Tick
					input.AimZ = 1
				}
				w.Input("alice", a.Generation, input)
				if tick%31 == 0 {
					w.Input("alice", a.Generation, input)
				} // Duplicate input must stay rejected.
				if tick == 130 {
					w.Disconnect("alice", a.Generation)
				}
				if tick == 133 {
					if _, e = w.Attach("alice", true); e != nil {
						t.Fatal(e)
					}
					if historyFactory != nil && rewindFactory != nil {
						w.configureNet("alice", a.Generation, netFixtureOptions())
					}
				}
				if mode == "practice" && tick == 190 {
					if e = w.setPracticeMode("alice", "move+attack+dodge+kite"); e != nil {
						t.Fatal(e)
					}
				}
				w.Step()
			}
			// Reproduce wall-time expiry, including the lab's one-hour tombstone.
			w.Disconnect("alice", a.Generation)
			now = now.Add(40 * time.Second)
			w.Step()
			_, _ = w.Attach("alice", false)
			closeFile()
			report := VerifyReplay(path)
			if report.Status != "PASS" || report.Summary.Steps < 400 || report.Summary.Entries < 800 {
				b, _ := json.Marshal(report)
				t.Fatal(string(b))
			}
		})
	}
}
func TestReplayDetectsFirstSimulationDivergenceAndIncompatibleContent(t *testing.T) {
	w := New()
	path, closeFile := startFixtureReplay(t, w)
	s, _ := w.Attach("alice", false)
	w.Input("alice", s.Generation, InputIntent{Generation: s.Generation, Seq: 1, X: 1})
	w.Step()
	closeFile()
	data, e := os.ReadFile(path)
	if e != nil {
		t.Fatal(e)
	}
	tampered := strings.Replace(string(data), `"X":1`, `"X":-1`, 1)
	if tampered == string(data) {
		t.Fatal("fixture input not found")
	}
	file := filepath.Join(t.TempDir(), "changed.jsonl")
	_ = os.WriteFile(file, []byte(tampered), 0600)
	report := VerifyReplay(file)
	if report.Status != "FAIL" || report.Divergence == nil || report.Divergence.Sequence != 2 || report.Divergence.Kind != "input" {
		t.Fatalf("did not localize drift %+v", report)
	}
	badRules := strings.Replace(string(data), `"tickRate":30`, `"tickRate":20`, 1)
	_ = os.WriteFile(file, []byte(badRules), 0600)
	if report = VerifyReplay(file); report.Status != "FAIL" || report.Error != "replay_rules_mismatch" {
		t.Fatalf("accepted incompatible rules %+v", report)
	}
}
func TestReplayRealEngineQueueAndShutdown(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	ctx, e := WithReplayDirectory(ctx, t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	engine := NewEngine(ctx)
	status := engine.ReplayStatus()
	if !status.Enabled || status.Error != "" {
		cancel()
		t.Fatal(status)
	}
	deadline, stop := context.WithTimeout(context.Background(), 3*time.Second)
	defer stop()
	c, e := engine.Attach(deadline, "engine-player", false)
	if e != nil {
		cancel()
		t.Fatal(e)
	}
	engine.Input("engine-player", c.Welcome.Generation, protocol.Client{Generation: c.Welcome.Generation, Seq: 1, X: 1})
	for engine.CurrentTick() < 4 {
		select {
		case <-deadline.Done():
			cancel()
			t.Fatal("engine timeout")
		case <-time.After(time.Millisecond):
		}
	}
	engine.Disconnect("engine-player", c.Welcome.Generation)
	// Barrier confirms preceding commands actually executed before cancellation.
	if _, e = engine.InspectPlayer(deadline, "engine-player"); e != nil {
		cancel()
		t.Fatal(e)
	}
	cancel()
	<-engine.Done
	if s := engine.ReplayStatus(); !s.Complete || s.Error != "" {
		t.Fatal(s)
	}
	if report := VerifyReplay(status.Path); report.Status != "PASS" {
		t.Fatalf("%+v", report)
	}
}

func TestReplayProjectileAndGroundDamageDeathAndRespawn(t *testing.T) {
	for _, hero := range []string{"ranger", "mage"} {
		t.Run(hero, func(t *testing.T) {
			w, e := newPracticeWorld("replay-combat-"+hero, map[string]string{"shooter": "red", "target": "blue"}, map[string]string{"shooter": hero, "target": "tank"}, shared.ArenaMap(), "shooter", "target", "idle")
			if e != nil {
				t.Fatal(e)
			}
			path, closeFile := startFixtureReplay(t, w)
			now := time.Unix(1700000000, 0)
			w.Now = func() time.Time { return now }
			a, e := w.Attach("shooter", false)
			if e != nil {
				t.Fatal(e)
			}
			w.MarkLoaded("shooter", a.Generation)
			damaged, dead := false, false
			limit := int(w.Seats["shooter"].hero.FireCooldownTicks*7 + w.Rules.RespawnTicks + 120)
			for tick := 0; tick < limit; tick++ {
				now = now.Add(time.Second / 30)
				c := InputIntent{Generation: a.Generation, Seq: uint64(tick + 1), Action: "fire", ActionID: uint64(tick + 1), ActionTick: w.Tick, AimX: 1, FacingX: 1}
				if hero == "mage" {
					c.HasGroundTarget = true
					c.TargetX = 1
					c.TargetZ = 0
				}
				w.Input("shooter", a.Generation, c)
				w.Step()
				target := w.Seats["target"]
				damaged = damaged || target.State.HP < target.hero.MaxHP
				dead = dead || target.State.HP == 0
			}
			if !damaged || !dead || w.Seats["target"].Life == 0 {
				t.Fatalf("fixture missed combat lifecycle: damage=%v dead=%v life=%d", damaged, dead, w.Seats["target"].Life)
			}
			if hero == "ranger" && w.NextProjectileID == 0 || hero == "mage" && w.NextGroundCastID == 0 {
				t.Fatal("fixture never created its attack")
			}
			closeFile()
			if r := VerifyReplay(path); r.Status != "PASS" {
				t.Fatalf("combat replay: %+v", r)
			}
		})
	}
}
