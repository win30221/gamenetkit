package sim

import (
	"context"
	"reflect"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

func pointWorld(t *testing.T, roster map[string]string) *World {
	t.Helper()
	w := productionWorld(t, "m-point", roster)
	for user := range roster {
		if _, err := w.Attach(user, false); err != nil {
			t.Fatal(err)
		}
		w.Seats[user].State.ProtectedUntilTick = 0
	}
	for user := range roster {
		w.MarkLoaded(user, w.Seats[user].Generation)
	}
	if w.Phase != phasePlaying {
		t.Fatal("point fixture did not start")
	}
	return w
}

func TestControlPointSingleTeamRateContestedOfflineAndDead(t *testing.T) {
	w := pointWorld(t, map[string]string{"a": "t1", "b": "t1", "c": "t2"})
	a, b, c := w.Seats["a"], w.Seats["b"], w.Seats["c"]
	a.State.X, a.State.Z = 0, 0
	b.State.X, b.State.Z = .8, 0
	c.State.X, c.State.Z = 6, 0
	w.Step()
	if w.ScoreTicks["t1"] != 1 || w.PointStatus != "scoring" || w.ScoringTeamID != "t1" {
		t.Fatalf("same-team occupancy multiplied score or lost owner: %+v", w.matchState())
	}
	c.State.X, c.State.Z = 0, .8
	w.Step()
	if w.ScoreTicks["t1"] != 1 || w.ScoreTicks["t2"] != 0 || w.PointStatus != "contested" {
		t.Fatalf("contested point scored: %+v", w.matchState())
	}
	w.Disconnect("c", c.Generation)
	w.Step()
	if w.ScoreTicks["t1"] != 2 || w.PointStatus != "scoring" {
		t.Fatalf("offline body contested: %+v", w.matchState())
	}
	if _, err := w.Attach("c", false); err != nil {
		t.Fatal(err)
	}
	c.State.HP = 0
	c.State.RespawnTick = w.Tick + w.Rules.RespawnTicks
	w.Step()
	if w.ScoreTicks["t1"] != 3 || w.PointStatus != "scoring" {
		t.Fatalf("dead player contested: %+v", w.matchState())
	}
	if w.ScoreTicks["t2"] != 0 || a.State.HP != w.Rules.MaxHP {
		t.Fatal("point score affected combat health or other team")
	}
	c.State.X = 6
	for range 27 {
		w.Step()
	}
	if w.ScoreTicks["t1"] != 30 || w.matchState().Scores[0].Score != 1 {
		t.Fatalf("1V2 team did not earn exactly one point per second: %+v", w.matchState().Scores)
	}
}

func TestPointEntryProtectionRemovedBeforeProjectileDeathAndScore(t *testing.T) {
	w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = -1, 0
	b.State.X, b.State.Z = 0, 0
	b.State.HP = w.Rules.ProjectileDamage
	b.State.ProtectedUntilTick = 60
	w.Projectiles = []shot{{State: ProjectileState{
		ID: "b1", OwnerID: a.State.PlayerID, TeamID: "t1",
		X: -.6, Z: 0, DX: 1, DZ: 0, ExpiresTick: 30,
	}}}
	w.Step()
	if b.State.ProtectedUntilTick != 0 || b.State.HP != 0 || b.State.RespawnTick != 1+w.Rules.RespawnTicks {
		t.Fatalf("point entry did not expose entrant to same-tick projectile: %+v", b.State)
	}
	if w.ScoreTicks["t1"] != 1 || w.ScoreTicks["t2"] != 0 || w.PointStatus != "scoring" {
		t.Fatalf("killed defender still contested the scoring phase: %+v", w.matchState())
	}
}

func TestPointRotationWarningAndOldPointScoresAtBoundary(t *testing.T) {
	w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.X, a.State.Z = 0, 0
	w.Tick = w.Rules.PointRotationTicks - w.Rules.PointWarningTicks - 1
	w.Step() // Tick 1050: warning begins.
	view := w.Snapshot(a, "snapshot")
	if view.Match.Point.Index != 0 || view.Match.NextPoint == nil || view.Match.NextPoint.Index != 1 || view.Match.RotationTick != 1200 {
		t.Fatalf("wrong warning: %+v", view.Match)
	}
	w.Tick = w.Rules.PointRotationTicks - 1
	before := w.ScoreTicks["t1"]
	w.Step() // Tick 1200 scores point 0, then rotates to point 1.
	view = w.Snapshot(a, "snapshot")
	if w.ScoreTicks["t1"] != before+1 || view.Match.Point.Index != 1 || view.Match.Point.Z != 5 || view.Match.NextPoint != nil || view.Match.RotationTick != 2400 || view.Match.PointStatus != "empty" {
		t.Fatalf("rotation boundary scored wrong point: %+v", view.Match)
	}
	a.State.X, a.State.Z = 0, 5
	w.Step()
	if w.ScoreTicks["t1"] != before+2 || w.PointStatus != "scoring" {
		t.Fatalf("new point did not score after rotation: %+v", w.matchState())
	}
}

func TestMatchResultPriorityTimeTieAndFractionalLead(t *testing.T) {
	t.Run("target", func(t *testing.T) {
		w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
		w.Rules.ScoreGoal = 1
		w.ScoreTicks["t1"] = uint64(w.Rules.TickRate) - 1
		w.Seats["a"].State.X = 0
		w.Step()
		if *w.Result != (BattleResult{Outcome: "win", Reason: "score", WinnerTeamID: "t1", EndedTick: 1}) {
			t.Fatalf("target result: %+v", w.Result)
		}
	})
	t.Run("forfeit_before_target", func(t *testing.T) {
		w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
		w.Rules.ScoreGoal = 1
		w.ScoreTicks["t1"] = uint64(w.Rules.TickRate) - 1
		w.Seats["a"].State.X = 0
		w.Seats["b"].State.Left = true
		w.Step()
		if w.Result == nil || w.Result.Reason != "forfeit" || w.Result.WinnerTeamID != "t1" {
			t.Fatalf("forfeit precedence: %+v", w.Result)
		}
	})
	t.Run("abort", func(t *testing.T) {
		w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
		for _, s := range w.Seats {
			s.State.Left = true
		}
		w.Step()
		if w.Result == nil || w.Result.Outcome != "abort" || w.Result.Reason != "no_teams" || w.Result.WinnerTeamID != "" {
			t.Fatalf("abort result: %+v", w.Result)
		}
	})
	t.Run("deadline_draw", func(t *testing.T) {
		w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
		w.Rules.MatchDurationTicks = 1
		w.ScoreTicks["t1"], w.ScoreTicks["t2"] = 31, 31
		w.Step()
		if w.Result == nil || w.Result.Outcome != "draw" || w.Result.Reason != "time" {
			t.Fatalf("time draw: %+v", w.Result)
		}
	})
	t.Run("deadline_partial_lead", func(t *testing.T) {
		w := pointWorld(t, map[string]string{"a": "t1", "b": "t2"})
		w.Rules.MatchDurationTicks = 1
		w.ScoreTicks["t1"], w.ScoreTicks["t2"] = 31, 30
		w.Step()
		if w.Result == nil || w.Result.Outcome != "win" || w.Result.Reason != "time" || w.Result.WinnerTeamID != "t1" {
			t.Fatalf("partial-point lead: %+v", w.Result)
		}
	})
	for _, tc := range []struct {
		name    string
		scores  [3]uint64
		outcome string
		winner  string
	}{
		{"three_team_tie", [3]uint64{40, 40, 10}, "draw", ""},
		{"three_team_leader", [3]uint64{40, 41, 10}, "win", "t2"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			w := pointWorld(t, map[string]string{"a": "t1", "b": "t2", "c": "t3"})
			w.Rules.MatchDurationTicks = 1
			for i, team := range w.TeamIDs {
				w.ScoreTicks[team] = tc.scores[i]
			}
			w.Step()
			if w.Result == nil || w.Result.Outcome != tc.outcome || w.Result.Reason != "time" || w.Result.WinnerTeamID != tc.winner {
				t.Fatalf("three-team deadline: %+v", w.Result)
			}
		})
	}
}

func TestNeverAttachedRosterWaitsWhileLoading(t *testing.T) {
	w, err := newMatchWorld("m-unattached", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	w.MarkLoaded("a", w.Seats["a"].Generation)
	for range w.Rules.ReconnectTicks + 1 {
		w.Step()
	}
	if w.Tick != 0 || w.Seats["b"].State.Left || w.Result != nil || w.Phase != phaseLoading {
		t.Fatalf("loading advanced or forfeited a no-show: %+v", w.Snapshot(w.Seats["a"], "welcome"))
	}
}

func TestEngineEmitsAndRetainsFrozenResultBeforeDone(t *testing.T) {
	w, err := newMatchWorld("m-result", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	w.Rules.MatchDurationTicks = 3
	ctx, cancel := context.WithCancel(context.Background())
	e := newEngineWithRetention(ctx, w, 350*time.Millisecond)
	defer func() { cancel(); <-e.Done }()
	a, err := e.Attach(ctx, "a", false)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := e.Attach(ctx, "b", false); err != nil {
		t.Fatal(err)
	}
	e.Loaded("a", a.Welcome.Generation)
	b, err := e.InspectPlayer(ctx, "b")
	if err != nil {
		t.Fatal(err)
	}
	e.Loaded("b", b.Generation)
	select {
	case <-e.Started:
	case <-time.After(2 * time.Second):
		t.Fatal("loaded roster did not start")
	}
	var terminal protocol.Server
	deadline := time.After(2 * time.Second)
	for terminal.Type != "result" {
		select {
		case terminal = <-a.Updates:
		case <-deadline:
			t.Fatal("terminal result was not broadcast")
		}
	}
	if terminal.Type != "result" || terminal.Match == nil || terminal.Match.Result == nil || terminal.Match.Result.Outcome != "draw" {
		t.Fatalf("wrong terminal message: %+v", terminal)
	}
	select {
	case <-e.Finished:
	case <-time.After(time.Second):
		t.Fatal("immutable result did not signal finished")
	}
	select {
	case <-e.Done:
		t.Fatal("finished signal waited for result retention")
	default:
	}
	e.Disconnect("a", a.Welcome.Generation)
	rejoined, err := e.Attach(ctx, "a", false)
	if err != nil {
		t.Fatal(err)
	}
	if rejoined.Welcome.Type != "result" || rejoined.Welcome.Tick != terminal.Tick || !reflect.DeepEqual(rejoined.Welcome.Match, terminal.Match) || !reflect.DeepEqual(rejoined.Welcome.Players, terminal.Players) {
		t.Fatalf("reconnect changed frozen result: original=%+v rejoined=%+v", terminal, rejoined.Welcome)
	}
	e.Input("a", rejoined.Welcome.Generation, protocol.Client{Generation: rejoined.Welcome.Generation, Seq: 1, X: 1})
	state, err := e.InspectPlayer(ctx, "a")
	if err != nil || state.Tick != terminal.Tick || !reflect.DeepEqual(state.Match, terminal.Match) {
		t.Fatalf("post-result input changed state: %+v %v", state, err)
	}
	select {
	case <-e.Done:
	case <-time.After(2 * time.Second):
		t.Fatal("settled engine did not close after retention")
	}
}
