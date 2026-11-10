package lobby

import (
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func memberIn(room Snapshot, user string) Member {
	for _, member := range room.Members {
		if member.UserID == user {
			return member
		}
	}
	return Member{}
}

func TestHeroSelectionDefaultsReadyResetAndFreeze(t *testing.T) {
	manager := New()
	room, err := manager.Create("host", 2)
	if err != nil {
		t.Fatal(err)
	}
	room, err = manager.Join("guest", room.Code)
	if err != nil {
		t.Fatal(err)
	}
	for _, user := range []string{"host", "guest"} {
		if memberIn(room, user).HeroID != "ranger" {
			t.Fatal("new member did not default to ranger")
		}
	}
	if _, err = manager.ChooseTeam("host", "t1"); err != nil {
		t.Fatal(err)
	}
	if _, err = manager.ChooseTeam("guest", "t2"); err != nil {
		t.Fatal(err)
	}
	rules := shared.Rules()
	for _, user := range []string{"host", "guest"} {
		if room, err = manager.SetReady(user, true, rules.Protocol, rules.Content); err != nil {
			t.Fatal(err)
		}
	}
	room, err = manager.SetHero("host", "tank")
	if err != nil || memberIn(room, "host").HeroID != "tank" || memberIn(room, "host").Ready || !memberIn(room, "guest").Ready || room.CanStart {
		t.Fatalf("hero change did not reset only its owner's readiness: %+v %v", room, err)
	}
	if room, err = manager.SetReady("host", true, rules.Protocol, rules.Content); err != nil {
		t.Fatal(err)
	}
	room, err = manager.SetHero("host", "tank")
	if err != nil || !room.CanStart || !memberIn(room, "host").Ready {
		t.Fatal("unchanged hero reset readiness")
	}
	for _, heroID := range []string{"", "unknown", "TANK", " tank "} {
		_, err = manager.SetHero("host", heroID)
		requireCode(t, err, "invalid_hero")
	}
	_, err = manager.SetHero("outsider", "tank")
	requireCode(t, err, "not_in_room")
	frozen, err := manager.Start("host")
	if err != nil {
		t.Fatal(err)
	}
	if memberIn(frozen, "host").HeroID != "tank" || memberIn(frozen, "guest").HeroID != "ranger" {
		t.Fatal("frozen roster lost selected heroes")
	}
	_, err = manager.SetHero("host", "ranger")
	requireCode(t, err, "room_started")
	frozen.Members[0].HeroID = "forged"
	current, err := manager.Current("host")
	if err != nil || memberIn(current, "host").HeroID != "tank" || memberIn(current, "guest").HeroID != "ranger" {
		t.Fatal("detached snapshot mutated room hero choices")
	}
	manager.End(frozen.MatchID)
	current, err = manager.Current("host")
	if err != nil || current.Started || memberIn(current, "host").HeroID != "tank" || memberIn(current, "guest").HeroID != "ranger" {
		t.Fatal("return to room lost hero selection")
	}
	for _, member := range current.Members {
		if member.Ready {
			t.Fatal("return retained readiness")
		}
	}
}
