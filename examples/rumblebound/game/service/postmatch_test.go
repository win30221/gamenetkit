package gateway

import (
	"fmt"
	"sync"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

// The sim suite verifies real result emission/retention. Here manually signalled
// engine lifetime channels isolate room release from the 30-second retention.
func retainedRoomFixture(t *testing.T, s *Server, users []roomIdentity) (lobby.Snapshot, *roomMatch) {
	t.Helper()
	frozen, err := s.Lobby.Start(users[0].user)
	if err != nil {
		t.Fatal(err)
	}
	engine := &sim.Engine{Done: make(chan struct{}), Started: make(chan struct{}), Finished: make(chan struct{})}
	close(engine.Started)
	var stopped sync.Once
	active := &roomMatch{engine: engine, cancel: func() { stopped.Do(func() { close(engine.Done) }) }, roomCode: frozen.Code, roster: map[string]string{}}
	for _, member := range frozen.Members {
		active.roster[member.UserID] = member.TeamID
	}
	s.matchMu.Lock()
	s.matches[frozen.MatchID] = active
	for _, member := range frozen.Members {
		s.claims[member.UserID] = frozen.MatchID
	}
	s.matchMu.Unlock()
	go s.watchMatch(frozen.MatchID, active)
	t.Cleanup(active.cancel)
	return frozen, active
}

func TestFinishedRoomImmediatelyAllowsReturnLeaveAndRetainedTicket(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	original := readyRoom(t, s, server, users, []string{"t1", "t2"})
	frozen, active := retainedRoomFixture(t, s, users)
	close(active.engine.Finished)

	var returned lobby.Snapshot
	roomRequest(t, server, "GET", "/api/room/current", "", users[0].token, roomOrigin, 200, &returned)
	if returned.Code != original.Code || returned.Started || returned.MatchID != "" {
		t.Fatalf("result kept room locked: %+v", returned)
	}
	for _, member := range returned.Members {
		if member.Ready {
			t.Fatal("result retained readiness")
		}
	}
	s.matchMu.Lock()
	for _, user := range users {
		if s.claims[user.user] != "" {
			t.Error("result retained a live claim")
		}
	}
	s.matchMu.Unlock()
	select {
	case <-active.engine.Done:
		t.Fatal("return waited for retention or killed retained result")
	default:
	}

	var retained struct{ Ticket, MatchID string }
	body := fmt.Sprintf(`{"takeover":false,"matchID":%q}`, frozen.MatchID)
	roomRequest(t, server, "POST", "/api/room/ticket", body, users[1].token, roomOrigin, 200, &retained)
	identity, ticket, err := s.Auth.Consume(retained.Ticket)
	if err != nil || identity.UserID != users[1].user || ticket.MatchID != frozen.MatchID {
		t.Fatal("retained result ticket lost frozen membership")
	}
	outsider := newRoomIdentity(t, s)
	roomRequest(t, server, "POST", "/api/room/ticket", body, outsider.token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/room/leave", `{}`, users[1].token, roomOrigin, 200, nil)
	roomRequest(t, server, "POST", "/api/room/ticket", body, users[1].token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/room/ticket", `{"takeover":false}`, users[0].token, roomOrigin, 409, nil)
}

func TestRetainedResultCannotResetOrJoinNextMatch(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2"})
	frozen, old := retainedRoomFixture(t, s, users)
	close(old.engine.Finished)
	var room lobby.Snapshot
	roomRequest(t, server, "GET", "/api/room/current", "", users[0].token, roomOrigin, 200, &room)
	body := fmt.Sprintf(`{"takeover":false,"matchID":%q}`, frozen.MatchID)
	var stale struct{ Ticket string }
	roomRequest(t, server, "POST", "/api/room/ticket", body, users[0].token, roomOrigin, 200, &stale)
	ready := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, shared.ArenaMapHash())
	for _, user := range users {
		roomRequest(t, server, "POST", "/api/room/ready", ready, user.token, roomOrigin, 200, &room)
	}
	next := startRoomHTTP(t, server, users[0])
	if next.MatchID == frozen.MatchID {
		t.Fatal("rematch reused match identity")
	}
	nextTicket, _ := roomTicketHTTP(t, server, users[0], false)
	_, _, welcome := dialMatch(t, server, nextTicket, next.MatchID)
	if welcome.Type != "welcome" {
		t.Fatalf("immediate rematch failed: %+v", welcome)
	}
	roomRequest(t, server, "POST", "/api/room/ticket", body, users[0].token, roomOrigin, 409, nil)
	_, _, rejected := dialMatch(t, server, stale.Ticket, frozen.MatchID)
	if rejected.Type != "error" || rejected.Code != "not_in_match" {
		t.Fatalf("old result ticket crossed into rematch: %+v", rejected)
	}

	old.cancel()
	deadline := time.Now().Add(time.Second)
	for {
		s.matchMu.Lock()
		gone := s.matches[frozen.MatchID] == nil
		claim := s.claims[users[0].user]
		s.matchMu.Unlock()
		if gone {
			if claim != next.MatchID {
				t.Fatal("old result cleanup removed new match claim")
			}
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("old result did not clean up")
		}
		time.Sleep(time.Millisecond)
	}
	roomRequest(t, server, "GET", "/api/room/current", "", users[0].token, roomOrigin, 200, &room)
	if !room.Started || room.MatchID != next.MatchID {
		t.Fatalf("old result cleanup reset new room: %+v", room)
	}
}
