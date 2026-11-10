package gateway

import (
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"sync"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func TestFullActiveCapacityAllowsRematchWhileResultsAreRetained(t *testing.T) {
	s, server := setup(t)
	var first []roomIdentity
	for i := 0; i < maxActiveMatches; i++ {
		users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
		readyRoom(t, s, server, users, []string{"t1", "t2"})
		retainedRoomFixture(t, s, users)
		if first == nil {
			first = users
		}
		if i == maxActiveMatches-1 {
			// At capacity, the room must be rejected before allocating an engine.
			extra := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
			readyRoom(t, s, server, extra, []string{"t1", "t2"})
			roomRequest(t, server, "POST", "/api/room/start", `{}`, extra[0].token, roomOrigin, 503, nil)
		}
	}
	s.matchMu.Lock()
	for _, active := range s.matches {
		close(active.engine.Finished)
	}
	s.matchMu.Unlock()
	s.refreshFinishedRooms()
	ready := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, shared.ArenaMapHash())
	for _, user := range first {
		roomRequest(t, server, "POST", "/api/room/ready", ready, user.token, roomOrigin, 200, nil)
	}
	startRoomHTTP(t, server, first[0])
	health := s.health()
	if health["activeMatches"] != 1 || health["retainedMatches"] != maxActiveMatches {
		t.Fatalf("retained results consumed live match capacity: %+v", health)
	}
}

func TestFifthFullRoomCanConnectEveryAdmittedPlayer(t *testing.T) {
	s, server := setup(t)
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	var workers sync.WaitGroup
	t.Cleanup(func() { cancel(); workers.Wait() })
	rules := shared.Rules()
	var first roomIdentity
	for roomIndex := 0; roomIndex < 5; roomIndex++ {
		users := make([]roomIdentity, lobby.MaxPlayersTotal)
		teams := make([]string, len(users))
		for i := range users {
			users[i] = newRoomIdentity(t, s)
			teams[i] = fmt.Sprintf("t%d", 1+i/4)
		}
		readyRoom(t, s, server, users, teams)
		started := startRoomHTTP(t, server, users[0])
		for _, user := range users {
			ticket, _ := roomTicketHTTP(t, server, user, false)
			conn, _, welcome := dialMatch(t, server, ticket, started.MatchID)
			if welcome.Type != "welcome" {
				t.Fatalf("room %d admitted a roster it could not connect: %+v", roomIndex, welcome)
			}
			// Keep earlier rooms alive while the later rosters are admitted, even
			// on slower race-enabled builders. Drain snapshots and send idle input.
			idle, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content,
				MatchID: started.MatchID, Generation: welcome.Generation, Seq: 1})
			workers.Add(2)
			go func() {
				defer workers.Done()
				for {
					if _, _, err := conn.Read(ctx); err != nil {
						return
					}
				}
			}()
			go func() {
				defer workers.Done()
				tick := time.NewTicker(250 * time.Millisecond)
				defer tick.Stop()
				for {
					select {
					case <-ctx.Done():
						return
					case <-tick.C:
						if err := conn.Write(ctx, websocket.MessageText, idle); err != nil {
							return
						}
					}
				}
			}()
		}
		if roomIndex == 0 {
			first = users[0]
		}
	}
	if got := len(s.Slots); got != 5*lobby.MaxPlayersTotal {
		t.Fatalf("admitted connections did not remain live: got %d", got)
	}
	ticket, matchID := roomTicketHTTP(t, server, first, true)
	_, _, welcome := dialMatch(t, server, ticket, matchID)
	if welcome.Type != "welcome" || welcome.Generation != 2 {
		t.Fatalf("connected rosters blocked an explicit takeover: %+v", welcome)
	}
}

func TestAuthenticationReleasesOnlyHandshakeBudget(t *testing.T) {
	s, _ := setup(t)
	confirmed, release, ok := s.acquireTransport()
	if !ok {
		t.Fatal("first handshake rejected")
	}
	confirmed()
	confirmed() // Completion and failure cleanup may both confirm authentication.
	if len(s.handshakes) != 0 || len(s.Slots) != 1 {
		t.Fatal("authentication did not retain only the live transport slot")
	}
	release()
	if len(s.handshakes) != 0 || len(s.Slots) != 0 {
		t.Fatal("transport cleanup leaked a budget")
	}
	_, release, ok = s.acquireTransport()
	if !ok {
		t.Fatal("released handshake was not reusable")
	}
	release()
	if len(s.handshakes) != 0 || len(s.Slots) != 0 {
		t.Fatal("failed handshake leaked a budget")
	}
}

func TestUserAdmissionCancellationReleasesEarlierLocksAndRegistryEntries(t *testing.T) {
	s, _ := setup(t)
	hold, err := s.lockUsers(context.Background(), []string{"b"})
	if err != nil {
		t.Fatal(err)
	}
	defer hold()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() {
		unlock, err := s.lockUsers(ctx, []string{"b", "a", "a"})
		if err == nil {
			unlock()
		}
		done <- err
	}()
	deadline := time.Now().Add(time.Second)
	for {
		s.admissionMu.Lock()
		waiting := s.admissions["b"].refs == 2
		s.admissionMu.Unlock()
		if waiting {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("second admission did not wait on the shared user")
		}
		time.Sleep(time.Millisecond)
	}
	// A disjoint user progresses while the shared user is blocked.
	unrelated, err := s.lockUsers(context.Background(), []string{"c"})
	if err != nil {
		t.Fatal(err)
	}
	unrelated()
	cancel()
	select {
	case err := <-done:
		if err != context.Canceled {
			t.Fatalf("cancelled admission returned %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("cancelled admission remained blocked")
	}
	hold()
	s.admissionMu.Lock()
	remaining := len(s.admissions)
	s.admissionMu.Unlock()
	if remaining != 0 {
		t.Fatalf("admission locks leaked %d registry entries", remaining)
	}
}

func TestHealthIncludesPrivateEngineCostsAndExcludesRetainedResults(t *testing.T) {
	s, _ := setup(t)
	a, b, finished := &sim.Engine{}, &sim.Engine{}, &sim.Engine{}
	a.TickNanos.Store(11)
	a.Overruns.Store(2)
	a.DroppedInputs.Store(3)
	b.TickNanos.Store(17)
	b.Overruns.Store(5)
	b.DroppedInputs.Store(7)
	finished.TickNanos.Store(999)
	finished.Overruns.Store(999)
	s.matchMu.Lock()
	s.matches["a"] = &roomMatch{engine: a}
	s.matches["b"] = &roomMatch{engine: b}
	s.matches["pending"] = &roomMatch{}
	s.matches["result"] = &roomMatch{engine: finished, finished: true}
	s.matchMu.Unlock()
	health := s.health()
	private := health["privateMatches"].(sim.EngineMetrics)
	if private.TickNanos != 17 || private.Overruns != 7 || private.DroppedInputs != 10 {
		t.Fatalf("private cost aggregation missing or included retained result: %+v", private)
	}
	if health["activeMatches"] != 3 || health["initializingMatches"] != 1 || health["retainedMatches"] != 1 {
		t.Fatalf("private lifecycle counts incorrect: %+v", health)
	}
}

func TestRetentionBudgetRetiresOldestResultAndPreservesActiveMatch(t *testing.T) {
	s, _ := setup(t)
	var retired []int
	s.matchMu.Lock()
	for i := 0; i <= maxRetainedMatches; i++ {
		index := i
		s.matches[fmt.Sprint(i)] = &roomMatch{finished: true, finishedAt: time.Unix(int64(i), 0), cancel: func() { retired = append(retired, index) }}
	}
	s.matches["live"] = &roomMatch{}
	s.trimRetainedMatchesLocked()
	remaining, live := len(s.matches), s.matches["live"] != nil
	s.matchMu.Unlock()
	if remaining != maxRetainedMatches+1 || !live || len(retired) != 1 || retired[0] != 0 {
		t.Fatalf("wrong retention eviction: count=%d live=%v retired=%v", remaining, live, retired)
	}
}

func TestTransportBudgetSupportsEveryRosterAndTakeoverAtCapacity(t *testing.T) {
	s, _ := setup(t)
	var releases []func()
	t.Cleanup(func() {
		for _, release := range releases {
			release()
		}
	})
	// All 16 full matches, all retained results and the diagnostic arena remain
	// connected. A replacement connection must still reach authentication.
	seats := maxActiveMatches*lobby.MaxPlayersTotal + maxRetainedMatches*lobby.MaxPlayersTotal + shared.Rules().MaxPlayers
	for i := 0; i < seats; i++ {
		authenticated, release, ok := s.acquireTransport()
		if !ok {
			t.Fatalf("legitimate seat %d rejected", i)
		}
		authenticated()
		releases = append(releases, release)
	}
	for i := 0; i < maxPendingHandshakes; i++ {
		_, release, ok := s.acquireTransport()
		if !ok {
			t.Fatalf("takeover headroom %d rejected", i)
		}
		releases = append(releases, release)
	}
	if _, _, ok := s.acquireTransport(); ok {
		t.Fatal("handshake flood exceeded bounded capacity")
	}
}

func TestLogoutTransfersHostOnlyAfterLastSessionAndExpiryRemovesMember(t *testing.T) {
	s, server := setup(t)
	raw, identity, _ := s.Auth.GoogleSession("same-host")
	otherRaw, _, _ := s.Auth.GoogleSession("same-host")
	users := []roomIdentity{{raw, identity.UserID}, newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2", "t1"})
	roomRequest(t, server, "POST", "/api/logout", `{}`, raw, roomOrigin, 200, nil)
	var room lobby.Snapshot
	roomRequest(t, server, "GET", "/api/room/current", "", users[1].token, roomOrigin, 200, &room)
	if room.HostID != identity.UserID || len(room.Members) != 3 {
		t.Fatal("one-device logout evicted signed-in host")
	}
	roomRequest(t, server, "POST", "/api/logout", `{}`, otherRaw, roomOrigin, 200, nil)
	roomRequest(t, server, "GET", "/api/room/current", "", users[1].token, roomOrigin, 200, &room)
	if room.HostID == identity.UserID || len(room.Members) != 2 {
		t.Fatal("last logout retained orphan host")
	}
	// Expiry uses the same cleanup path without requiring another logout request.
	s.Auth.mu.Lock()
	hash := sha256.Sum256([]byte(users[2].token))
	expired := s.Auth.sessions[hash]
	expired.Expires = time.Now().Add(-time.Second)
	s.Auth.sessions[hash] = expired
	s.Auth.mu.Unlock()
	roomRequest(t, server, "GET", "/api/room/current", "", users[1].token, roomOrigin, 200, &room)
	if room.HostID != users[1].user || len(room.Members) != 1 {
		t.Fatal("expiry retained stale member")
	}
}

func TestPendingRoomAdmissionDoesNotBlockHealthAndCancellationRollsBack(t *testing.T) {
	rootDone := make(chan struct{})
	engine := &sim.Engine{Done: rootDone}
	auth, _ := NewAuth(testKey)
	s := New(auth, engine, nil)
	t.Cleanup(func() { close(rootDone) })
	_, host, _ := auth.NewSession(testKey)
	_, peer, _ := auth.NewSession(testKey)
	room, _ := s.Lobby.Create(host.UserID, 2)
	s.Lobby.Join(peer.UserID, room.Code)
	rules := shared.Rules()
	for user, team := range map[string]string{host.UserID: "t1", peer.UserID: "t2"} {
		s.Lobby.ChooseTeam(user, team)
		s.Lobby.SetReady(user, true, rules.Protocol, rules.Content)
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	go func() { _, err := s.startRoom(ctx, host.UserID); done <- err }()
	deadline := time.Now().Add(time.Second)
	for s.activeMatchCount() == 0 {
		if time.Now().After(deadline) {
			t.Fatal("admission did not reserve capacity")
		}
		time.Sleep(time.Millisecond)
	}
	responded := make(chan struct{})
	go func() { s.health(); close(responded) }()
	select {
	case <-responded:
	case <-time.After(200 * time.Millisecond):
		t.Fatal("stalled engine blocked unrelated health request")
	}
	cancel()
	select {
	case err := <-done:
		if err == nil {
			t.Fatal("cancelled admission succeeded")
		}
	case <-time.After(time.Second):
		t.Fatal("cancelled admission stayed blocked")
	}
	current, _ := s.Lobby.Current(host.UserID)
	if current.Started || s.activeMatchCount() != 0 {
		t.Fatal("cancelled reservation leaked room or capacity")
	}
}
