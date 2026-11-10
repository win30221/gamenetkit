package gateway

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestGuestsCanStartPrivateMatchAndMoveWithoutDevelopmentKey(t *testing.T) {
	s, server := setup(t)
	s.GuestLogin = true
	users := make([]roomIdentity, 2)
	for i := range users {
		var identity struct {
			Session string `json:"session"`
			UserID  string `json:"userID"`
			Guest   bool   `json:"guest"`
		}
		// Native and browser clients both enter without an Authorization header.
		origin := ""
		if i == 1 {
			origin = roomOrigin
		}
		roomRequest(t, server, "POST", "/api/session/guest", `{}`, "", origin, 200, &identity)
		if identity.Session == "" || !strings.HasPrefix(identity.UserID, "guest_") || !identity.Guest {
			t.Fatal("guest entry did not create a guest identity")
		}
		users[i] = roomIdentity{token: identity.Session, user: identity.UserID}
	}
	if users[0].token == users[1].token || users[0].user == users[1].user {
		t.Fatal("independent guest entries reused an identity")
	}

	readyRoom(t, s, server, users, []string{"t1", "t2"})
	room := startRoomHTTP(t, server, users[0])
	firstTicket, firstMatchID := roomTicketHTTP(t, server, users[0], false)
	secondTicket, secondMatchID := roomTicketHTTP(t, server, users[1], false)
	if firstMatchID != room.MatchID || secondMatchID != room.MatchID {
		t.Fatal("guest tickets do not target their private match")
	}
	first, firstCtx, firstWelcome := dialMatch(t, server, firstTicket, firstMatchID)
	second, secondCtx, secondWelcome := dialMatch(t, server, secondTicket, secondMatchID)
	if firstWelcome.Type != "welcome" || secondWelcome.Type != "welcome" || firstWelcome.PlayerID == secondWelcome.PlayerID {
		t.Fatal("guest clients did not receive distinct player seats")
	}
	if playerIn(firstWelcome, firstWelcome.PlayerID).TeamID != "t1" || playerIn(secondWelcome, secondWelcome.PlayerID).TeamID != "t2" {
		t.Fatal("guest players lost their selected teams")
	}
	sendLoadedWS(t, first, firstCtx, firstWelcome)
	sendLoadedWS(t, second, secondCtx, secondWelcome)
	firstStart := waitStartWS(t, first, firstCtx)
	secondStart := waitStartWS(t, second, secondCtx)
	if len(firstStart.Players) != 2 || len(secondStart.Players) != 2 {
		t.Fatal("private match did not start with both guests")
	}

	rules := shared.Rules()
	input, err := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content,
		MatchID: room.MatchID, Generation: firstWelcome.Generation, Seq: 1, X: 1})
	if err != nil {
		t.Fatal(err)
	}
	if err := first.Write(firstCtx, websocket.MessageText, input); err != nil {
		t.Fatal(err)
	}
	initial := playerIn(firstStart, firstWelcome.PlayerID)
	for {
		_, data, err := first.Read(firstCtx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		if err := json.Unmarshal(data, &state); err != nil {
			t.Fatal(err)
		}
		if state.Type == "error" {
			t.Fatalf("guest input rejected: %s", state.Code)
		}
		player := playerIn(state, firstWelcome.PlayerID)
		if player.Ack == 1 && player.X > initial.X {
			break
		}
	}
}
