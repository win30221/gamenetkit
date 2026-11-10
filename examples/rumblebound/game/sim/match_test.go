package sim

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

func TestPrivateMatchPreallocatesImmutableRoster(t *testing.T) {
	roster := map[string]string{"c": "t2", "a": "t1", "b": "t2"}
	w, err := newMatchWorld("m-room-one", roster)
	if err != nil {
		t.Fatal(err)
	}
	roster["a"] = "t4"
	delete(roster, "b")
	if w.MatchID != "m-room-one" || len(w.Seats) != 3 {
		t.Fatalf("bad roster: %+v", w)
	}
	for _, tc := range []struct{ user, player, entity, team string }{
		{"a", "p1", "e1", "t1"}, {"b", "p2", "e2", "t2"}, {"c", "p3", "e3", "t2"},
	} {
		s := w.Seats[tc.user]
		if s == nil || s.State.PlayerID != tc.player || s.State.EntityID != tc.entity || s.State.TeamID != tc.team || s.State.Online {
			t.Fatalf("frozen seat %s: %+v", tc.user, s)
		}
	}
	// Players that have not connected have no reconnect deadline.
	w.Now = func() time.Time { return time.Unix(10_000_000, 0) }
	w.Step()
	if len(w.Expired) != 0 {
		t.Fatal("unconnected roster seats expired")
	}
	if _, err := w.Attach("outsider", false); err == nil || err.Error() != "not_in_match" {
		t.Fatalf("outsider admission: %v", err)
	}
	s, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	state := w.Snapshot(s, "welcome")
	if state.MatchID != "m-room-one" || len(state.Players) != 3 || state.Players[0].TeamID != "t1" || state.Players[1].Online {
		t.Fatalf("wrong private match snapshot: %+v", state)
	}
	if len(w.Seats) != 3 {
		t.Fatal("attach allocated a new seat")
	}
}

func TestPrivateMatchReconnectRetainsSeatAndExpiry(t *testing.T) {
	w, err := newMatchWorld("m-reconnect", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	now := time.Unix(1000, 0)
	w.Now = func() time.Time { return now }
	s, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	b, err := w.Attach("b", false)
	if err != nil {
		t.Fatal(err)
	}
	w.MarkLoaded("a", s.Generation)
	w.MarkLoaded("b", b.Generation)
	if !w.Input("a", s.Generation, InputIntent{Generation: s.Generation, Seq: 1, X: 1}) {
		t.Fatal("input rejected")
	}
	w.Step()
	id, entity, team, x := s.State.PlayerID, s.State.EntityID, s.State.TeamID, s.State.X
	w.Disconnect("a", s.Generation)
	now = now.Add(29 * time.Second)
	rejoined, err := w.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	if rejoined != s || rejoined.Generation != 2 || rejoined.State.PlayerID != id || rejoined.State.EntityID != entity || rejoined.State.TeamID != team || rejoined.State.X != x {
		t.Fatalf("reconnect changed frozen identity: %+v", rejoined)
	}
	w.Disconnect("a", rejoined.Generation)
	now = now.Add(30 * time.Second)
	w.Step()
	if w.Result == nil || w.Result.Outcome != "win" || w.Result.Reason != "forfeit" || w.Result.WinnerTeamID != "t2" {
		t.Fatalf("expired seat did not settle match: %+v", w.Result)
	}
	endedTick := w.Tick
	if _, err := w.Attach("a", false); err != nil {
		t.Fatalf("ended seat could not retrieve result: %v", err)
	}
	if got := w.Snapshot(s, "result"); got.Type != "result" || got.Match == nil || got.Match.Result == nil || got.Match.Result.EndedTick != endedTick || got.Players[0].Online {
		t.Fatalf("reconnect changed frozen result: %+v", got)
	}
	if len(w.Seats) != 2 || w.Seats["a"] != s {
		t.Fatal("frozen roster identity was discarded")
	}
	now = now.Add(2 * time.Hour)
	w.Step()
	if w.Tick != endedTick || w.Result.EndedTick != endedTick {
		t.Fatal("settled world advanced")
	}
}

func TestPrivateMatchWorldsDoNotShareMovementOrSeats(t *testing.T) {
	first, err := newMatchWorld("m-first", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	second, err := newMatchWorld("m-second", map[string]string{"c": "t1", "d": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	a, err := first.Attach("a", false)
	if err != nil {
		t.Fatal(err)
	}
	c, err := second.Attach("c", false)
	if err != nil {
		t.Fatal(err)
	}
	b, err := first.Attach("b", false)
	if err != nil {
		t.Fatal(err)
	}
	d, err := second.Attach("d", false)
	if err != nil {
		t.Fatal(err)
	}
	first.MarkLoaded("a", a.Generation)
	first.MarkLoaded("b", b.Generation)
	second.MarkLoaded("c", c.Generation)
	second.MarkLoaded("d", d.Generation)
	firstX, secondX := a.State.X, c.State.X
	if !first.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 1, X: 1}) {
		t.Fatal("input rejected")
	}
	first.Step()
	second.Step()
	if a.State.X <= firstX || c.State.X != secondX {
		t.Fatal("movement crossed match worlds")
	}
	if first.Snapshot(a, "snapshot").MatchID != "m-first" || second.Snapshot(c, "snapshot").MatchID != "m-second" {
		t.Fatal("match identifiers crossed worlds")
	}
	if _, err := first.Attach("c", false); err == nil {
		t.Fatal("second-room member entered first match")
	}
}

func TestPrivateMatchLimitsAndDatagramBudget(t *testing.T) {
	for _, tc := range []struct {
		name, id string
		roster   map[string]string
	}{
		{"lab-id", MatchID, map[string]string{"a": "t1", "b": "t2"}},
		{"one-player", "m-one", map[string]string{"a": "t1"}},
		{"one-team", "m-one-team", map[string]string{"a": "t1", "b": "t1"}},
		{"empty-user", "m-empty", map[string]string{"": "t1", "b": "t2"}},
		{"empty-team", "m-empty-team", map[string]string{"a": "", "b": "t2"}},
		{"over-team-cap", "m-cap", map[string]string{"a": "t1", "b": "t1", "c": "t1", "d": "t1", "e": "t1", "f": "t2"}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if _, err := newMatchWorld(tc.id, tc.roster); err == nil {
				t.Fatal("invalid roster accepted")
			}
		})
	}
	roster := map[string]string{}
	for i := range 8 {
		team := "t1"
		if i >= 4 {
			team = "t2"
		}
		roster[fmt.Sprintf("user%d", i)] = team
	}
	w, err := newMatchWorld("m-"+strings.Repeat("x", 62), roster)
	if err != nil {
		t.Fatal(err)
	}
	var last *Seat
	for i := range 8 {
		last, err = w.Attach(fmt.Sprintf("user%d", i), false)
		if err != nil {
			t.Fatal(err)
		}
		last.State.X, last.State.Z, last.State.Ack = 9.599999999999999, -9.599999999999999, 9007199254740991
	}
	data, err := json.Marshal(w.Snapshot(last, "snapshot"))
	if err != nil {
		t.Fatal(err)
	}
	if len(data) > protocol.MaxSnapshot {
		t.Fatalf("private match snapshot %d > %d", len(data), protocol.MaxSnapshot)
	}
}

func TestPrivateMatchEngineUsesOwnWorld(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	e, err := NewMatchEngine(ctx, "m-engine", map[string]string{"a": "t1", "b": "t2"})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { cancel(); <-e.Done })
	connection, err := e.Attach(ctx, "a", false)
	if err != nil {
		t.Fatal(err)
	}
	if connection.Welcome.MatchID != "m-engine" || len(connection.Welcome.Players) != 2 || connection.Welcome.Players[0].TeamID != "t1" {
		t.Fatalf("bad welcome: %+v", connection.Welcome)
	}
	if _, err := e.Attach(ctx, "outsider", false); err == nil || err.Error() != "not_in_match" {
		t.Fatalf("outsider accepted: %v", err)
	}
	e.Disconnect("a", connection.Welcome.Generation)
	if _, err := NewMatchEngine(ctx, MatchID, map[string]string{"a": "t1", "b": "t2"}); err == nil {
		t.Fatal("lab match ID accepted")
	}
}
