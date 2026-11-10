package lobby

import (
	"fmt"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func requireCode(t *testing.T, err error, want string) {
	t.Helper()
	if err == nil || err.Error() != want {
		t.Fatalf("error = %v, want %s", err, want)
	}
}

func TestSupportedTeamLayoutsAndReadyGate(t *testing.T) {
	for _, tc := range []struct {
		name  string
		sizes []int
	}{
		{"1V1", []int{1, 1}},
		{"1V2", []int{1, 2}},
		{"1V1V1", []int{1, 1, 1}},
		{"2V2V2", []int{2, 2, 2}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			m := New()
			users := []string{"p0"}
			first, err := m.Create(users[0], len(tc.sizes))
			if err != nil {
				t.Fatal(err)
			}
			if first.CanStart || len(first.Code) != codeLength || first.HostID != users[0] {
				t.Fatalf("bad initial room: %+v", first)
			}
			for i := 1; i < total(tc.sizes); i++ {
				user := fmt.Sprintf("p%d", i)
				users = append(users, user)
				if _, err := m.Join(user, strings.ToLower(first.Code)); err != nil {
					t.Fatal(err)
				}
			}
			index := 0
			for team, size := range tc.sizes {
				for i := 0; i < size; i++ {
					if _, err := m.ChooseTeam(users[index], first.Teams[team].ID); err != nil {
						t.Fatal(err)
					}
					index++
				}
			}
			for _, user := range users[:len(users)-1] {
				state, err := m.SetReady(user, true, shared.Rules().Protocol, shared.Rules().Content)
				if err != nil {
					t.Fatal(err)
				}
				if state.CanStart {
					t.Fatal("started before every player was ready")
				}
			}
			state, err := m.SetReady(users[len(users)-1], true, shared.Rules().Protocol, shared.Rules().Content)
			if err != nil || !state.CanStart || len(state.Members) != len(users) {
				t.Fatalf("ready result = %+v, %v", state, err)
			}
		})
	}
}

func total(sizes []int) int {
	n := 0
	for _, size := range sizes {
		n += size
	}
	return n
}

func TestCapacityIdentityAndHostAuthority(t *testing.T) {
	m := New()
	r, err := m.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := m.Join("guest", r.Code); err != nil {
		t.Fatal(err)
	}
	_, err = m.SetCapacity("guest", "t1", 1)
	requireCode(t, err, "host_required")
	if _, err := m.SetCapacity("host", "t1", 1); err != nil {
		t.Fatal(err)
	}
	if _, err := m.AssignTeam("host", "guest", "t1"); err != nil {
		t.Fatal(err)
	}
	_, err = m.AssignTeam("guest", "host", "t2")
	requireCode(t, err, "host_required")
	_, err = m.AssignTeam("host", "outsider", "t2")
	requireCode(t, err, "member_not_found")
	_, err = m.ChooseTeam("host", "t1")
	requireCode(t, err, "team_full")
	if _, err := m.ChooseTeam("host", "t2"); err != nil {
		t.Fatal(err)
	}
	state, err := m.SetReady("guest", true, shared.Rules().Protocol, shared.Rules().Content)
	if err != nil || state.CanStart {
		t.Fatalf("not everyone ready: %+v, %v", state, err)
	}
	state, err = m.SetReady("host", true, shared.Rules().Protocol, shared.Rules().Content)
	if err != nil || !state.CanStart {
		t.Fatalf("ready room: %+v, %v", state, err)
	}
	_, err = m.SetCapacity("host", "t1", 0)
	requireCode(t, err, "invalid_capacity")
	state, err = m.SetCapacity("host", "t1", 2)
	if err != nil || state.CanStart {
		t.Fatalf("changed capacity retained readiness: %+v, %v", state, err)
	}
	if state.Members[0].UserID != "guest" || state.Members[0].Ready {
		t.Fatalf("affected guest still ready: %+v", state.Members)
	}
	if !state.Members[1].Ready {
		t.Fatalf("unaffected host lost readiness: %+v", state.Members)
	}
	_, err = m.SetCapacity("host", "t1", 0)
	requireCode(t, err, "invalid_capacity")
	_, err = m.SetCapacity("host", "t1", 4)
	if err != nil {
		t.Fatal(err)
	}
}

func TestInvalidAndDuplicateMembership(t *testing.T) {
	m := New()
	_, err := m.Create("host", 1)
	requireCode(t, err, "invalid_team_count")
	_, err = m.Create("host", MaxTeams+1)
	requireCode(t, err, "invalid_team_count")
	r, err := m.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	_, err = m.Create("host", 2)
	requireCode(t, err, "already_in_room")
	other, err := m.Create("other", 2)
	if err != nil {
		t.Fatal(err)
	}
	_, err = m.Join("host", other.Code)
	requireCode(t, err, "already_in_room")
	for i := 1; i < MaxPlayersTotal; i++ {
		if _, err := m.Join(fmt.Sprintf("p%d", i), r.Code); err != nil {
			t.Fatal(err)
		}
	}
	_, err = m.Join("overflow", r.Code)
	requireCode(t, err, "room_full")
	_, err = m.SetReady("p1", true, shared.Rules().Protocol, shared.Rules().Content)
	requireCode(t, err, "team_required")
	if _, err := m.ChooseTeam("p1", "t1"); err != nil {
		t.Fatal(err)
	}
	_, err = m.SetReady("p1", true, shared.Rules().Protocol+1, shared.Rules().Content)
	requireCode(t, err, "version_mismatch")
	_, err = m.SetReady("p1", true, shared.Rules().Protocol, "old-content")
	requireCode(t, err, "version_mismatch")
	_, err = m.ChooseTeam("p1", "invalid")
	requireCode(t, err, "invalid_team")
	if _, err := m.Join("p1", r.Code); err != nil {
		t.Fatal("same room join should be idempotent:", err)
	}
	_, err = m.Current("stranger")
	requireCode(t, err, "not_in_room")
}

func TestCapacityCannotDisplaceExistingTeamMembers(t *testing.T) {
	m := New()
	r, err := m.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := m.Join("guest", r.Code); err != nil {
		t.Fatal(err)
	}
	if _, err := m.ChooseTeam("host", "t1"); err != nil {
		t.Fatal(err)
	}
	if _, err := m.ChooseTeam("guest", "t1"); err != nil {
		t.Fatal(err)
	}
	_, err = m.SetCapacity("host", "t1", 1)
	requireCode(t, err, "team_over_capacity")
	state, err := m.Current("host")
	if err != nil || state.Teams[0].Capacity != MaxPlayersPerTeam || len(state.Members) != 2 {
		t.Fatalf("rejected change mutated room: %+v, %v", state, err)
	}
}

func TestHostTransferReadyResetAndRoomExpiry(t *testing.T) {
	m := New()
	now := time.Unix(1000, 0)
	m.now = func() time.Time { return now }
	r, _ := m.Create("host", 2)
	m.Join("z", r.Code)
	m.Join("a", r.Code)
	m.ChooseTeam("host", "t1")
	m.ChooseTeam("a", "t2")
	m.SetReady("a", true, shared.Rules().Protocol, shared.Rules().Content)
	state, err := m.AssignTeam("host", "a", "t1")
	if err != nil || state.Members[0].Ready {
		t.Fatalf("host move must clear readiness: %+v, %v", state, err)
	}
	if err := m.Leave("host"); err != nil {
		t.Fatal(err)
	}
	state, err = m.Current("a")
	if err != nil || state.HostID != "a" {
		t.Fatalf("host not transferred: %+v, %v", state, err)
	}
	if _, err := m.SetCapacity("a", "t2", 2); err != nil {
		t.Fatal(err)
	}
	now = now.Add(59 * time.Minute)
	if _, err := m.Current("a"); err != nil {
		t.Fatal("active room expired:", err)
	}
	now = now.Add(59 * time.Minute)
	if _, err := m.Current("a"); err != nil {
		t.Fatal("active room expired:", err)
	}
	now = now.Add(time.Hour + time.Second)
	_, err = m.Current("a")
	requireCode(t, err, "not_in_room")
}

func TestJoinAttemptLimitAndReset(t *testing.T) {
	m := New()
	now := time.Unix(1000, 0)
	m.now = func() time.Time { return now }
	r, _ := m.Create("host", 2)
	for i := 0; i < 10; i++ {
		_, err := m.Join("guest", "bad")
		requireCode(t, err, "room_not_found")
	}
	_, err := m.Join("guest", r.Code)
	requireCode(t, err, "join_rate_limited")
	now = now.Add(time.Minute + time.Second)
	if _, err := m.Join("guest", r.Code); err != nil {
		t.Fatal(err)
	}
}

func TestStartFreezesRosterAndRejectsMutations(t *testing.T) {
	m := New()
	r, err := m.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	for _, user := range []string{"a", "b"} {
		if _, err := m.Join(user, r.Code); err != nil {
			t.Fatal(err)
		}
	}
	_, err = m.Start("a")
	requireCode(t, err, "host_required")
	_, err = m.Start("host")
	requireCode(t, err, "room_not_ready")
	_, err = m.Match("a")
	requireCode(t, err, "match_not_started")
	for _, user := range []string{"host", "a", "b"} {
		team := "t2"
		if user == "host" {
			team = "t1"
		}
		if _, err := m.ChooseTeam(user, team); err != nil {
			t.Fatal(err)
		}
		if _, err := m.SetReady(user, true, shared.Rules().Protocol, shared.Rules().Content); err != nil {
			t.Fatal(err)
		}
	}
	started, err := m.Start("host")
	if err != nil {
		t.Fatal(err)
	}
	if !started.Started || started.MatchID == "" || started.CanStart || len(started.Members) != 3 {
		t.Fatalf("bad frozen match: %+v", started)
	}
	if started.Members[0].UserID != "a" || started.Members[0].TeamID != "t2" || started.Members[1].UserID != "b" || started.Members[1].TeamID != "t2" || started.Members[2].UserID != "host" || started.Members[2].TeamID != "t1" {
		t.Fatalf("wrong frozen 1V2 roster: %+v", started.Members)
	}
	memberMatch, err := m.Match("a")
	if err != nil || memberMatch.MatchID != started.MatchID {
		t.Fatalf("member match: %+v, %v", memberMatch, err)
	}
	_, err = m.Match("outside")
	requireCode(t, err, "not_in_room")
	started.Members[0].TeamID = "forged"
	started.Teams[0].Capacity = 0
	current, err := m.Current("host")
	if err != nil || current.Members[0].TeamID != "t2" || current.Teams[0].Capacity != MaxPlayersPerTeam {
		t.Fatalf("returned roster aliased mutable room: %+v, %v", current, err)
	}
	_, err = m.Start("host")
	requireCode(t, err, "room_started")
	_, err = m.Join("new", r.Code)
	requireCode(t, err, "room_started")
	_, err = m.Join("a", r.Code)
	requireCode(t, err, "room_started")
	_, err = m.ChooseTeam("a", "t1")
	requireCode(t, err, "room_started")
	_, err = m.AssignTeam("host", "a", "t1")
	requireCode(t, err, "room_started")
	_, err = m.SetCapacity("host", "t1", 1)
	requireCode(t, err, "room_started")
	_, err = m.SetReady("a", false, 0, "")
	requireCode(t, err, "room_started")
	requireCode(t, m.Leave("a"), "room_started")
	after, err := m.Match("b")
	if err != nil || after.MatchID != memberMatch.MatchID || !after.Started || len(after.Members) != 3 {
		t.Fatalf("frozen match mutated: %+v, %v", after, err)
	}
}

func TestEndResetsExactMatchAndAllowsNextStart(t *testing.T) {
	m := New()
	now := time.Unix(1000, 0)
	m.now = func() time.Time { return now }
	r, err := m.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := m.Join("guest", r.Code); err != nil {
		t.Fatal(err)
	}
	for user, team := range map[string]string{"host": "t1", "guest": "t2"} {
		if _, err := m.ChooseTeam(user, team); err != nil {
			t.Fatal(err)
		}
		if _, err := m.SetReady(user, true, shared.Rules().Protocol, shared.Rules().Content); err != nil {
			t.Fatal(err)
		}
	}
	first, err := m.Start("host")
	if err != nil {
		t.Fatal(err)
	}
	if first.Phase != "loading" || !first.Started || m.Begin("wrong") || !m.Begin(first.MatchID) || !m.Begin(first.MatchID) {
		t.Fatalf("loading-to-playing transition failed: %+v", first)
	}
	if playing, err := m.Match("guest"); err != nil || playing.Phase != "playing" || playing.MatchID != first.MatchID {
		t.Fatalf("playing room changed frozen roster: %+v, %v", playing, err)
	}
	now = now.Add(2 * time.Hour)
	if current, err := m.Current("guest"); err != nil || current.MatchID != first.MatchID {
		t.Fatalf("running room pruned: %+v, %v", current, err)
	}
	m.End("wrong")
	if current, err := m.Match("guest"); err != nil || current.MatchID != first.MatchID {
		t.Fatalf("wrong end reset match: %+v, %v", current, err)
	}
	m.End(first.MatchID)
	m.End(first.MatchID)
	reset, err := m.Current("guest")
	if err != nil || reset.Started || reset.Phase != "" || reset.MatchID != "" || reset.CanStart || len(reset.Members) != 2 {
		t.Fatalf("bad reset: %+v, %v", reset, err)
	}
	for _, member := range reset.Members {
		if member.Ready {
			t.Fatalf("ready retained after match: %+v", reset.Members)
		}
	}
	_, err = m.Match("guest")
	requireCode(t, err, "match_not_started")
	for _, user := range []string{"host", "guest"} {
		if _, err := m.SetReady(user, true, shared.Rules().Protocol, shared.Rules().Content); err != nil {
			t.Fatal(err)
		}
	}
	second, err := m.Start("host")
	if err != nil || second.MatchID == first.MatchID || second.Phase != "loading" {
		t.Fatalf("second start: %+v, %v", second, err)
	}
	m.End(first.MatchID)
	if current, err := m.Match("guest"); err != nil || current.MatchID != second.MatchID {
		t.Fatalf("stale end reset newer match: %+v, %v", current, err)
	}
	m.End(second.MatchID)
	if err := m.Leave("guest"); err != nil {
		t.Fatal("cannot leave after match:", err)
	}
}
