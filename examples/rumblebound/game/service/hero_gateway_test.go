package gateway

import (
	"fmt"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestGuestHeroSelectionAuthorityAndFrozenRuntime(t *testing.T) {
	s, server := setup(t)
	s.GuestLogin = true
	users := make([]roomIdentity, 2)
	for i := range users {
		var identity struct{ Session, UserID string }
		roomRequest(t, server, "POST", "/api/session/guest", `{}`, "", roomOrigin, 200, &identity)
		users[i] = roomIdentity{token: identity.Session, user: identity.UserID}
	}
	room := readyRoom(t, s, server, users, []string{"t1", "t2"})
	for _, member := range room.Members {
		if member.HeroID != "ranger" {
			t.Fatal("guest did not default to ranger")
		}
	}
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"tank"}`, "", roomOrigin, 401, nil)
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"tank"}`, users[0].token, "https://evil.test", 403, nil)
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"unknown"}`, users[0].token, roomOrigin, 400, nil)
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"mage"}`, users[0].token, roomOrigin, 200, &room)
	roomRequest(t, server, "POST", "/api/room/hero", fmt.Sprintf(`{"heroID":"tank","userID":%q}`, users[1].user), users[0].token, roomOrigin, 400, nil)
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"tank"}`, users[0].token, roomOrigin, 200, &room)
	for _, member := range room.Members {
		if member.UserID == users[0].user && (member.HeroID != "tank" || member.Ready) {
			t.Fatal("own hero did not change and reset ready")
		}
		if member.UserID == users[1].user && (member.HeroID != "ranger" || !member.Ready) {
			t.Fatal("selection changed another guest")
		}
	}
	ready := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, shared.ArenaMapHash())
	roomRequest(t, server, "POST", "/api/room/ready", ready, users[0].token, roomOrigin, 200, &room)
	started := startRoomHTTP(t, server, users[0])
	roomRequest(t, server, "POST", "/api/room/hero", `{"heroID":"ranger"}`, users[0].token, roomOrigin, 409, nil)
	for i, wantHero := range []string{"tank", "ranger"} {
		ticket, matchID := roomTicketHTTP(t, server, users[i], false)
		_, _, welcome := dialMatch(t, server, ticket, matchID)
		if welcome.Type != "welcome" || welcome.MatchID != started.MatchID {
			t.Fatalf("hero match admission failed: %+v", welcome)
		}
		self := playerIn(welcome, welcome.PlayerID)
		hero, exists := shared.Rules().Hero(wantHero)
		if !exists || self.HeroID != wantHero || self.HP != hero.MaxHP {
			t.Fatalf("selected hero not authoritative: %+v want %+v", self, hero)
		}
		if i == 0 {
			reconnectTicket, _ := roomTicketHTTP(t, server, users[i], true)
			_, _, rejoined := dialMatch(t, server, reconnectTicket, matchID)
			retained := playerIn(rejoined, rejoined.PlayerID)
			if retained.HeroID != self.HeroID || retained.HP != self.HP || rejoined.PlayerID != welcome.PlayerID {
				t.Fatal("takeover lost selected hero or HP")
			}
		}
	}
	var current lobby.Snapshot
	roomRequest(t, server, "GET", "/api/room/current", "", users[0].token, roomOrigin, 200, &current)
	for _, member := range current.Members {
		if member.UserID == users[0].user && member.HeroID != "tank" {
			t.Fatal("locked room changed selected hero")
		}
	}
}
