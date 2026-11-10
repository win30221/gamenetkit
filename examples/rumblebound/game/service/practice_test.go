package gateway

import (
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func practiceBody(hero, botHero, mode string) string {
	rules := shared.Rules()
	return fmt.Sprintf(`{"heroID":%q,"botHeroID":%q,"mode":%q,"protocol":%d,"content":%q,"mapHash":%q}`, hero, botHero, mode, rules.Protocol, rules.Content, shared.ArenaMapHash())
}

func startPracticeHTTP(t *testing.T, server *httptest.Server, user roomIdentity) lobby.Snapshot {
	t.Helper()
	var room lobby.Snapshot
	roomRequest(t, server, "POST", "/api/practice/start", practiceBody("tank", "ranger", "idle"), user.token, roomOrigin, 200, &room)
	if !room.Practice || !room.Started || room.Phase != "loading" || room.MatchID == "" || room.BotMode != "idle" || len(room.Members) != 2 {
		t.Fatalf("invalid practice start: %+v", room)
	}
	return room
}

func TestPracticeSingleHumanLoadsModesAndImmediateLeaveReplay(t *testing.T) {
	s, server := setup(t)
	owner, visitor := newRoomIdentity(t, s), newRoomIdentity(t, s)
	room := startPracticeHTTP(t, server, owner)
	botUser := ""
	for _, member := range room.Members {
		if member.Bot {
			botUser = member.UserID
		}
	}
	if botUser == "" {
		t.Fatal("server did not reserve an NPC")
	}
	roomRequest(t, server, "POST", "/api/room/join", fmt.Sprintf(`{"code":%q}`, room.Code), visitor.token, roomOrigin, 403, nil)
	roomRequest(t, server, "POST", "/api/practice/mode", `{"mode":"move"}`, visitor.token, roomOrigin, 404, nil)
	roomRequest(t, server, "POST", "/api/practice/mode", `{"mode":"attack","userID":"forged"}`, owner.token, roomOrigin, 400, nil)
	ticket, id := roomTicketHTTP(t, server, owner, false)
	conn, ctx, welcome := dialMatch(t, server, ticket, id)
	if !welcome.Practice || welcome.BotMode != "idle" || welcome.MapHash != shared.ArenaMapHash() {
		t.Fatal("practice welcome lost metadata/map")
	}
	botPlayerID := ""
	for _, player := range welcome.Players {
		if player.Bot {
			botPlayerID = player.PlayerID
			if !player.Online || player.HeroID != "ranger" {
				t.Fatal("NPC is not the configured online opponent")
			}
		}
	}
	if botPlayerID == "" {
		t.Fatal("practice snapshot omitted NPC marker")
	}
	sendLoadedWS(t, conn, ctx, welcome)
	started := waitStartWS(t, conn, ctx)
	if !started.Practice || len(started.Players) != 2 {
		t.Fatal("single-human acknowledgement failed to start NPC match")
	}
	for _, mode := range []string{"move", "attack", "capture", "capture+attack+dodge", "capture+attack+dodge+kite", "kite", "move+dodge", "idle"} {
		var changed lobby.Snapshot
		roomRequest(t, server, "POST", "/api/practice/mode", fmt.Sprintf(`{"mode":%q}`, mode), owner.token, roomOrigin, 200, &changed)
		if changed.BotMode != mode || changed.MatchID != id {
			t.Fatal("mode change reset the run")
		}
		for {
			_, data, err := conn.Read(ctx)
			if err != nil {
				t.Fatal(err)
			}
			var state protocol.Server
			if err := json.Unmarshal(data, &state); err != nil {
				t.Fatal(err)
			}
			if state.BotMode == mode {
				break
			}
		}
	}
	roomRequest(t, server, "POST", "/api/practice/mode", `{"mode":"heal"}`, owner.token, roomOrigin, 400, nil)
	// Even an internally forged valid identity for the reserved bot cannot
	// obtain a room ticket or claim its transport seat.
	forged := token()
	s.Auth.mu.Lock()
	s.Auth.sessions[sha256.Sum256([]byte(forged))] = session{UserID: botUser, Expires: time.Now().Add(time.Hour)}
	s.Auth.mu.Unlock()
	roomRequest(t, server, "POST", "/api/room/ticket", fmt.Sprintf(`{"matchID":%q}`, id), forged, roomOrigin, 403, nil)
	botTicket, err := s.Auth.TicketForMatch(forged, id, false)
	if err != nil {
		t.Fatal(err)
	}
	_, _, rejected := dialMatch(t, server, botTicket, id)
	if rejected.Type != "error" || rejected.Code != "bot_reserved" {
		t.Fatalf("bot seat accepted external control: %+v", rejected)
	}
	s.matchMu.Lock()
	old := s.matches[id]
	botClaim := s.claims[botUser]
	s.matchMu.Unlock()
	if botClaim != "" {
		t.Fatal("NPC allocated a human admission claim")
	}
	roomRequest(t, server, "POST", "/api/practice/leave", `{}`, owner.token, roomOrigin, 200, nil)
	if _, err := s.Lobby.Current(owner.user); err == nil {
		t.Fatal("leave retained NPC room")
	}
	next := startPracticeHTTP(t, server, owner)
	if next.MatchID == id {
		t.Fatal("replay reused cancelled match")
	}
	select {
	case <-old.engine.Done:
	case <-time.After(2 * time.Second):
		t.Fatal("leave did not cancel old engine")
	}
	current, err := s.Lobby.Current(owner.user)
	if err != nil || current.MatchID != next.MatchID || !current.Started {
		t.Fatal("late old-engine cleanup reset new practice")
	}
	nextTicket, nextID := roomTicketHTTP(t, server, owner, false)
	nextConn, nextCtx, nextWelcome := dialMatch(t, server, nextTicket, nextID)
	sendLoadedWS(t, nextConn, nextCtx, nextWelcome)
	waitStartWS(t, nextConn, nextCtx)
	roomRequest(t, server, "POST", "/api/practice/leave", `{}`, owner.token, roomOrigin, 200, nil)
}

func TestPracticeStartVersionOriginPresetAndRollback(t *testing.T) {
	s, server := setup(t)
	owner := newRoomIdentity(t, s)
	rules := shared.Rules()
	for _, test := range []struct {
		body   string
		status int
	}{
		{`{}`, 409},
		{fmt.Sprintf(`{"protocol":%d,"content":%q,"mapHash":"wrong"}`, rules.Protocol, rules.Content), 409},
		{practiceBody("invalid", "ranger", "idle"), 400},
		{practiceBody("ranger", "invalid", "idle"), 400},
		{practiceBody("ranger", "ranger", "heal"), 400},
	} {
		roomRequest(t, server, "POST", "/api/practice/start", test.body, owner.token, roomOrigin, test.status, nil)
	}
	roomRequest(t, server, "POST", "/api/practice/start", practiceBody("ranger", "ranger", "idle"), owner.token, "https://evil.test", 403, nil)
	if _, err := s.Lobby.Current(owner.user); err == nil {
		t.Fatal("invalid start allocated a room")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	if _, err := s.Engine.Attach(ctx, owner.user, false); err != nil {
		t.Fatal(err)
	}
	roomRequest(t, server, "POST", "/api/practice/start", practiceBody("ranger", "ranger", "idle"), owner.token, roomOrigin, 409, nil)
	if _, err := s.Lobby.Current(owner.user); err == nil {
		t.Fatal("G0 admission rejection orphaned practice room")
	}
	var ordinary lobby.Snapshot
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, owner.token, roomOrigin, 200, &ordinary)
	roomRequest(t, server, "POST", "/api/practice/start", practiceBody("ranger", "ranger", "idle"), owner.token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/practice/mode", `{"mode":"move"}`, owner.token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/practice/leave", `{}`, owner.token, roomOrigin, 409, nil)
	current, _ := s.Lobby.Current(owner.user)
	if current.Code != ordinary.Code || current.Practice {
		t.Fatal("practice API altered ordinary room")
	}
}

func TestPracticeLoadingTimeoutBecomesInactiveAndCanLeave(t *testing.T) {
	s, server := setup(t)
	s.loadTimeout = 40 * time.Millisecond
	owner := newRoomIdentity(t, s)
	startPracticeHTTP(t, server, owner)
	deadline := time.Now().Add(2 * time.Second)
	for {
		room, err := s.Lobby.Current(owner.user)
		if err == nil && !room.Started {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("practice load timeout did not return preparation")
		}
		time.Sleep(5 * time.Millisecond)
	}
	roomRequest(t, server, "POST", "/api/practice/mode", `{"mode":"move"}`, owner.token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/practice/leave", `{}`, owner.token, roomOrigin, 200, nil)
	if _, err := s.Lobby.Current(owner.user); err == nil {
		t.Fatal("inactive practice leave retained NPC")
	}
}
