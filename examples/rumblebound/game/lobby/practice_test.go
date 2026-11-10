package lobby

import (
	"fmt"
	"strings"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestPracticeRoomReservesOnePrivateImmutableBot(t *testing.T) {
	m := New()
	r, err := m.CreatePractice("owner", "tank", "ranger", "idle")
	if err != nil {
		t.Fatal(err)
	}
	if !r.Practice || r.BotMode != "idle" || !r.CanStart || len(r.Members) != 2 || len(r.Teams) != 2 {
		t.Fatalf("invalid practice: %+v", r)
	}
	bot := ""
	for _, member := range r.Members {
		if !member.Ready {
			t.Fatal("practice did not prepare both fixed seats")
		}
		if member.Bot {
			bot = member.UserID
			if member.TeamID != "t2" || member.HeroID != "ranger" {
				t.Fatal("bot lost preset")
			}
		}
	}
	if !strings.HasPrefix(bot, "bot_") || len(m.memberRoom) != 1 {
		t.Fatal("bot became an authenticated lobby member")
	}
	_, err = m.Current(bot)
	requireCode(t, err, "not_in_room")
	_, err = m.Join("visitor", r.Code)
	requireCode(t, err, "practice_private")
	_, err = m.AssignTeam("owner", bot, "t1")
	requireCode(t, err, "bot_reserved")
	_, err = m.ChooseTeam("owner", "t2")
	requireCode(t, err, "practice_fixed_teams")
	_, err = m.SetCapacity("owner", "t1", 2)
	requireCode(t, err, "practice_fixed_teams")
	_, err = m.SetPracticeMode("owner", "", "move")
	requireCode(t, err, "practice_inactive")
	started, err := m.Start("owner")
	if err != nil {
		t.Fatal(err)
	}
	changed, err := m.SetPracticeMode("owner", started.MatchID, "move")
	if err != nil || changed.BotMode != "move" {
		t.Fatalf("mode change: %+v %v", changed, err)
	}
	// Public snapshots must not expose a mutable room or NPC record.
	changed.BotMode = "attack"
	changed.Members[0].Bot = false
	current, _ := m.Current("owner")
	if current.BotMode != "move" {
		t.Fatal("snapshot mutated stored bot mode")
	}
	m.End(started.MatchID)
	current, _ = m.Current("owner")
	for _, member := range current.Members {
		if member.Ready != member.Bot {
			t.Fatal("result did not retain only bot readiness")
		}
	}
	if current.CanStart {
		t.Fatal("human readiness skipped after results")
	}
	if _, err := m.SetReady("owner", true, shared.Rules().Protocol, shared.Rules().Content); err != nil {
		t.Fatal(err)
	}
	next, err := m.Start("owner")
	if err != nil || next.MatchID == started.MatchID {
		t.Fatal("practice replay did not allocate new match")
	}
	m.End(started.MatchID)
	current, _ = m.Current("owner")
	if !current.Started || current.MatchID != next.MatchID {
		t.Fatal("late cleanup reset the new practice")
	}
	m.End(next.MatchID)
	if err := m.Leave("owner"); err != nil {
		t.Fatal(err)
	}
	if len(m.rooms) != 0 || len(m.memberRoom) != 0 || len(m.matches) != 0 {
		t.Fatal("leaving practice orphaned an NPC room")
	}
	if _, err := m.Create("owner", 2); err != nil {
		t.Fatal("practice owner could not return to normal lobby")
	}
}

func TestPracticeInvalidPresetsAndCapacityDoNotAllocateRooms(t *testing.T) {
	for _, preset := range [][3]string{{"invalid", "ranger", "idle"}, {"ranger", "invalid", "idle"}, {"ranger", "ranger", "heal"}} {
		m := New()
		_, err := m.CreatePractice("owner", preset[0], preset[1], preset[2])
		if err == nil || len(m.rooms) != 0 || len(m.memberRoom) != 0 {
			t.Fatal("invalid preset allocated a room")
		}
	}
	m := New()
	r, err := m.CreatePractice("owner", "", "", "")
	if err != nil || r.BotMode != "idle" {
		t.Fatal("default practice unavailable")
	}
	if err := m.RemovePractice("owner", "wrong-code"); err == nil {
		t.Fatal("rollback removed a different room")
	}
	_, err = m.CreatePractice("owner", "ranger", "ranger", "idle")
	requireCode(t, err, "already_in_room")
	if err := m.RemovePractice("owner", r.Code); err != nil {
		t.Fatal(err)
	}
	for i := 0; i < MaxRooms; i++ {
		if _, err := m.Create(fmt.Sprintf("host-%d", i), 2); err != nil {
			t.Fatal(err)
		}
	}
	_, err = m.CreatePractice("overflow", "ranger", "ranger", "idle")
	requireCode(t, err, "room_capacity")
	if _, ok := m.memberRoom["overflow"]; ok {
		t.Fatal("capacity rejection retained a member")
	}
}
