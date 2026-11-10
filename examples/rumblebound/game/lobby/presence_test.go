package lobby

import (
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestMemberLeaseExpiresIndependentlyOfActiveRoom(t *testing.T) {
	m := New()
	now := time.Now()
	m.now = func() time.Time { return now }
	room, _ := m.Create("host", 2)
	m.Join("remaining", room.Code)
	now = now.Add(MemberIdleTimeout + time.Second)
	m.Touch("remaining")
	m.Current("remaining")
	m.PruneMembers(func(string) bool { return true })
	current, err := m.Current("remaining")
	if err != nil || current.HostID != "remaining" || len(current.Members) != 1 {
		t.Fatalf("active polling retained abandoned host: %+v, %v", current, err)
	}
	if _, err := m.Current("host"); err == nil {
		t.Fatal("expired member still owns room")
	}
}

func TestFrozenRosterSurvivesExpiredSessionsUntilSettlement(t *testing.T) {
	m := New()
	room, _ := m.Create("host", 2)
	m.Join("peer", room.Code)
	rules := shared.Rules()
	for user, team := range map[string]string{"host": "t1", "peer": "t2"} {
		m.ChooseTeam(user, team)
		m.SetReady(user, true, rules.Protocol, rules.Content)
	}
	frozen, err := m.Start("host")
	if err != nil {
		t.Fatal(err)
	}
	active := func(user string) bool { return user == "peer" }
	m.PruneMembers(active)
	current, _ := m.Current("peer")
	if len(current.Members) != 2 || current.HostID != "host" {
		t.Fatal("session expiry changed a frozen roster")
	}
	m.End(frozen.MatchID)
	m.PruneMembers(active)
	current, _ = m.Current("peer")
	if len(current.Members) != 1 || current.HostID != "peer" {
		t.Fatal("settlement retained expired room owner")
	}
}
