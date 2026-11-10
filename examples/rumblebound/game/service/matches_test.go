package gateway

import (
	"bufio"
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

const roomOrigin = "https://allowed.test"

type roomIdentity struct{ token, user string }

func newRoomIdentity(t *testing.T, s *Server) roomIdentity {
	t.Helper()
	token, identity, err := s.Auth.NewSession(testKey)
	if err != nil {
		t.Fatal(err)
	}
	return roomIdentity{token: token, user: identity.UserID}
}

func readyRoom(t *testing.T, s *Server, server *httptest.Server, users []roomIdentity, teams []string) lobby.Snapshot {
	t.Helper()
	var room lobby.Snapshot
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, users[0].token, roomOrigin, 200, &room)
	for _, user := range users[1:] {
		roomRequest(t, server, "POST", "/api/room/join", fmt.Sprintf(`{"code":%q}`, room.Code), user.token, roomOrigin, 200, &room)
	}
	for i, user := range users {
		roomRequest(t, server, "POST", "/api/room/team", fmt.Sprintf(`{"teamID":%q}`, teams[i]), user.token, roomOrigin, 200, &room)
	}
	ready := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, shared.ArenaMapHash())
	for _, user := range users {
		roomRequest(t, server, "POST", "/api/room/ready", ready, user.token, roomOrigin, 200, &room)
	}
	if !room.CanStart {
		t.Fatalf("room not ready: %+v", room)
	}
	return room
}

func startRoomHTTP(t *testing.T, server *httptest.Server, host roomIdentity) lobby.Snapshot {
	t.Helper()
	var room lobby.Snapshot
	roomRequest(t, server, "POST", "/api/room/start", `{}`, host.token, roomOrigin, 200, &room)
	if !room.Started || room.MatchID == "" || room.CanStart {
		t.Fatalf("room not frozen: %+v", room)
	}
	return room
}

func roomTicketHTTP(t *testing.T, server *httptest.Server, user roomIdentity, takeover bool) (string, string) {
	t.Helper()
	var response struct {
		Ticket  string `json:"ticket"`
		MatchID string `json:"matchID"`
	}
	roomRequest(t, server, "POST", "/api/room/ticket", fmt.Sprintf(`{"takeover":%t}`, takeover), user.token, roomOrigin, 200, &response)
	if response.Ticket == "" || response.MatchID == "" {
		t.Fatalf("missing room ticket: %+v", response)
	}
	return response.Ticket, response.MatchID
}

func dialMatch(t *testing.T, server *httptest.Server, ticket, matchID string) (*websocket.Conn, context.Context, protocol.Server) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	t.Cleanup(cancel)
	c, _, err := websocket.Dial(ctx, "wss"+strings.TrimPrefix(server.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: server.Client()})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.CloseNow() })
	rules := shared.Rules()
	hello, _ := json.Marshal(protocol.Client{Type: "hello", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Ticket: ticket})
	if err := c.Write(ctx, websocket.MessageText, hello); err != nil {
		t.Fatal(err)
	}
	_, data, err := c.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	var result protocol.Server
	if err := json.Unmarshal(data, &result); err != nil {
		t.Fatal(err)
	}
	return c, ctx, result
}

func sendLoadedWS(t *testing.T, conn *websocket.Conn, ctx context.Context, welcome protocol.Server) {
	t.Helper()
	rules := shared.Rules()
	loaded, err := json.Marshal(protocol.Client{Type: "loaded", Protocol: rules.Protocol, Content: rules.Content,
		MatchID: welcome.MatchID, Generation: welcome.Generation, MapHash: welcome.MapHash})
	if err != nil {
		t.Fatal(err)
	}
	if err := conn.Write(ctx, websocket.MessageText, loaded); err != nil {
		t.Fatal(err)
	}
}

func waitStartWS(t *testing.T, conn *websocket.Conn, ctx context.Context) protocol.Server {
	t.Helper()
	for {
		_, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		if err := json.Unmarshal(data, &state); err != nil {
			t.Fatal(err)
		}
		if state.Type == "start" {
			if state.Tick != 0 || state.Match == nil || state.Match.Phase != "playing" {
				t.Fatalf("invalid tick-0 start: %+v", state)
			}
			return state
		}
		if state.Type != "snapshot" {
			t.Fatalf("unexpected message while loading: %+v", state)
		}
	}
}

func playerIn(state protocol.Server, playerID string) protocol.Player {
	for _, player := range state.Players {
		if player.PlayerID == playerID {
			return player
		}
	}
	return protocol.Player{}
}

func TestPrivateRoomMatchesIsolateTicketsRosterAndMovement(t *testing.T) {
	s, server := setup(t)
	a := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	b := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	first := readyRoom(t, s, server, a, []string{"t1", "t2"})
	second := readyRoom(t, s, server, b, []string{"t1", "t2"})
	roomRequest(t, server, "POST", "/api/room/start", `{}`, a[1].token, roomOrigin, 403, nil)
	first = startRoomHTTP(t, server, a[0])
	second = startRoomHTTP(t, server, b[0])
	if first.MatchID == second.MatchID || first.MatchID == sim.MatchID {
		t.Fatal("matches share an ID")
	}
	_, _, codeOnly := dialMatch(t, server, first.Code, first.MatchID)
	if codeOnly.Type != "error" || codeOnly.Code != "invalid_ticket" {
		t.Fatalf("room code granted battle access: %+v", codeOnly)
	}
	roomRequest(t, server, "POST", "/api/room/join", fmt.Sprintf(`{"code":%q}`, first.Code), newRoomIdentity(t, s).token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/room/team", `{"teamID":"t2"}`, a[0].token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/room/leave", `{}`, a[0].token, roomOrigin, 409, nil)
	roomRequest(t, server, "POST", "/api/room/ticket", `{"takeover":false}`, newRoomIdentity(t, s).token, roomOrigin, 404, nil)
	roomRequest(t, server, "POST", "/api/ticket", `{"takeover":false}`, a[0].token, roomOrigin, 409, nil)
	wrongTicket, _ := roomTicketHTTP(t, server, a[0], false)
	_, _, wrong := dialMatch(t, server, wrongTicket, second.MatchID)
	if wrong.Type != "error" || wrong.Code != "version_or_match_mismatch" {
		t.Fatalf("cross-match ticket accepted: %+v", wrong)
	}
	_, _, replay := dialMatch(t, server, wrongTicket, first.MatchID)
	if replay.Type != "error" || replay.Code != "invalid_ticket" {
		t.Fatalf("consumed ticket replayed: %+v", replay)
	}
	firstTicket, firstID := roomTicketHTTP(t, server, a[0], false)
	secondTicket, secondID := roomTicketHTTP(t, server, b[0], false)
	firstConn, firstCtx, firstWelcome := dialMatch(t, server, firstTicket, firstID)
	_, _, secondWelcome := dialMatch(t, server, secondTicket, secondID)
	if firstWelcome.Type != "welcome" || secondWelcome.Type != "welcome" || firstWelcome.MatchID != first.MatchID || secondWelcome.MatchID != second.MatchID {
		t.Fatalf("wrong matches: %+v / %+v", firstWelcome, secondWelcome)
	}
	if len(firstWelcome.Players) != 2 || len(secondWelcome.Players) != 2 || playerIn(firstWelcome, firstWelcome.PlayerID).TeamID != "t1" {
		t.Fatalf("frozen roster not visible: %+v / %+v", firstWelcome, secondWelcome)
	}
	firstPeerTicket, _ := roomTicketHTTP(t, server, a[1], false)
	firstPeer, peerCtx, firstPeerWelcome := dialMatch(t, server, firstPeerTicket, firstID)
	sendLoadedWS(t, firstConn, firstCtx, firstWelcome)
	sendLoadedWS(t, firstPeer, peerCtx, firstPeerWelcome)
	waitStartWS(t, firstConn, firstCtx)
	waitStartWS(t, firstPeer, peerCtx)
	rules := shared.Rules()
	input, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content, MatchID: first.MatchID, Generation: firstWelcome.Generation, Seq: 1, X: 1})
	if err := firstConn.Write(firstCtx, websocket.MessageText, input); err != nil {
		t.Fatal(err)
	}
	for {
		_, data, err := firstConn.Read(firstCtx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		if err := json.Unmarshal(data, &state); err != nil {
			t.Fatal(err)
		}
		if playerIn(state, firstWelcome.PlayerID).X > playerIn(firstWelcome, firstWelcome.PlayerID).X {
			break
		}
	}
	s.matchMu.Lock()
	other := s.matches[second.MatchID]
	s.matchMu.Unlock()
	if other == nil {
		t.Fatal("second match vanished")
	}
	otherState, err := other.engine.InspectPlayer(firstCtx, b[0].user)
	if err != nil {
		t.Fatal(err)
	}
	if playerIn(otherState, secondWelcome.PlayerID).X != playerIn(secondWelcome, secondWelcome.PlayerID).X || otherState.MatchID != second.MatchID {
		t.Fatalf("movement leaked across rooms: %+v", otherState)
	}
}

func TestLabSeatAndOldLabTicketCannotCrossIntoRoomMatch(t *testing.T) {
	s, server := setup(t)
	a := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, a, []string{"t1", "t2"})
	labTicket, err := s.Auth.Ticket(a[0].token, false)
	if err != nil {
		t.Fatal(err)
	}
	labConn, _, labWelcome := dialMatch(t, server, labTicket, sim.MatchID)
	if labWelcome.Type != "welcome" {
		t.Fatalf("lab connection failed: %+v", labWelcome)
	}
	roomRequest(t, server, "POST", "/api/room/start", `{}`, a[0].token, roomOrigin, 409, nil)
	room, err := s.Lobby.Current(a[0].user)
	if err != nil || room.Started || room.MatchID != "" || s.activeMatchCount() != 0 {
		t.Fatalf("rejected start left a frozen match: room=%+v active=%d err=%v", room, s.activeMatchCount(), err)
	}
	labConn.CloseNow()
	roomRequest(t, server, "POST", "/api/room/start", `{}`, a[0].token, roomOrigin, 409, nil)
	b := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, b, []string{"t1", "t2"})
	oldLabTicket, err := s.Auth.Ticket(b[0].token, false)
	if err != nil {
		t.Fatal(err)
	}
	startRoomHTTP(t, server, b[0])
	_, _, oldResult := dialMatch(t, server, oldLabTicket, sim.MatchID)
	if oldResult.Type != "error" || oldResult.Code != "already_in_match" {
		t.Fatalf("preissued lab ticket crossed into room: %+v", oldResult)
	}
	roomRequest(t, server, "POST", "/api/ticket", `{"takeover":false}`, b[0].token, roomOrigin, 409, nil)
}

func TestRoomReconnectKeepsPlayerAndRevocationStopsTraffic(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2"})
	started := startRoomHTTP(t, server, users[0])
	ticket, matchID := roomTicketHTTP(t, server, users[0], false)
	conn, ctx, first := dialMatch(t, server, ticket, matchID)
	if first.Type != "welcome" {
		t.Fatalf("first connect: %+v", first)
	}
	peerTicket, _ := roomTicketHTTP(t, server, users[1], false)
	peer, peerCtx, peerWelcome := dialMatch(t, server, peerTicket, matchID)
	sendLoadedWS(t, conn, ctx, first)
	sendLoadedWS(t, peer, peerCtx, peerWelcome)
	waitStartWS(t, conn, ctx)
	waitStartWS(t, peer, peerCtx)
	rules := shared.Rules()
	input, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Generation: first.Generation, Seq: 1, X: 1})
	if err := conn.Write(ctx, websocket.MessageText, input); err != nil {
		t.Fatal(err)
	}
	var moved protocol.Server
	for {
		_, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		if err := json.Unmarshal(data, &moved); err != nil {
			t.Fatal(err)
		}
		if playerIn(moved, first.PlayerID).X > playerIn(first, first.PlayerID).X {
			break
		}
	}
	conn.CloseNow()
	s.matchMu.Lock()
	active := s.matches[started.MatchID]
	s.matchMu.Unlock()
	if active == nil {
		t.Fatal("match vanished")
	}
	deadline := time.Now().Add(2 * time.Second)
	for {
		state, err := active.engine.InspectPlayer(ctx, users[0].user)
		if err != nil {
			t.Fatal(err)
		}
		if !playerIn(state, first.PlayerID).Online {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("disconnect not observed")
		}
		time.Sleep(20 * time.Millisecond)
	}
	ticket, _ = roomTicketHTTP(t, server, users[0], false)
	rejoined, rejoinCtx, welcome := dialMatch(t, server, ticket, matchID)
	if welcome.Type != "welcome" || welcome.PlayerID != first.PlayerID || welcome.Generation <= first.Generation || playerIn(welcome, first.PlayerID).X < playerIn(moved, first.PlayerID).X {
		t.Fatalf("reconnect lost identity or position: %+v", welcome)
	}
	s.Auth.Revoke(users[0].token)
	for {
		_, _, err := rejoined.Read(rejoinCtx)
		if err != nil {
			break
		}
	}
	if rejoinCtx.Err() != nil {
		t.Fatal("revoked room connection did not close promptly")
	}
}

func TestFinishedMatchClearsClaimsAndReturnsRoomToPreparation(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2"})
	started := startRoomHTTP(t, server, users[0])
	ticket, matchID := roomTicketHTTP(t, server, users[0], false)
	_, _, welcome := dialMatch(t, server, ticket, matchID)
	if welcome.Type != "welcome" {
		t.Fatalf("match did not admit host: %+v", welcome)
	}
	staleTicket, _ := roomTicketHTTP(t, server, users[1], false)
	s.matchMu.Lock()
	active := s.matches[started.MatchID]
	s.matchMu.Unlock()
	if active == nil {
		t.Fatal("match missing before cleanup")
	}
	active.cancel()
	select {
	case <-active.engine.Done:
	case <-time.After(2 * time.Second):
		t.Fatal("match engine did not stop")
	}
	deadline := time.Now().Add(2 * time.Second)
	for {
		state, err := s.Lobby.Current(users[0].user)
		if err != nil {
			t.Fatal(err)
		}
		if !state.Started {
			for _, member := range state.Members {
				if member.Ready {
					t.Fatal("finished match retained readiness")
				}
			}
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("room was not returned to preparation")
		}
		time.Sleep(10 * time.Millisecond)
	}
	s.matchMu.Lock()
	claim := s.claims[users[0].user]
	s.matchMu.Unlock()
	if claim != "" {
		t.Fatalf("finished match retained claim %s", claim)
	}
	_, _, stale := dialMatch(t, server, staleTicket, started.MatchID)
	if stale.Type != "error" || stale.Code != "not_in_match" {
		t.Fatalf("old match ticket survived completion: %+v", stale)
	}
	roomRequest(t, server, "POST", "/api/room/ticket", `{"takeover":false}`, users[0].token, roomOrigin, 409, nil)
}

func TestPrivateMatchWebTransportUsesFrozenRoster(t *testing.T) {
	s, tlsServer := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, tlsServer, users, []string{"t1", "t2"})
	started := startRoomHTTP(t, tlsServer, users[0])
	ticket, matchID := roomTicketHTTP(t, tlsServer, users[0], false)
	pool := x509.NewCertPool()
	pool.AddCert(tlsServer.Certificate())
	pc, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	mux := http.NewServeMux()
	wt := &webtransport.Server{H3: &http3.Server{TLSConfig: http3.ConfigureTLSConfig(&tls.Config{Certificates: tlsServer.TLS.Certificates}), Handler: mux}, CheckOrigin: s.Origin}
	mux.HandleFunc("/wt", s.WebTransport(wt))
	done := make(chan error, 1)
	go func() { done <- wt.Serve(pc) }()
	t.Cleanup(func() { wt.Close(); pc.Close(); <-done })
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	transport := &webtransport.Transport{TLSClientConfig: &tls.Config{RootCAs: pool}}
	_, session, err := transport.Dial(ctx, "https://"+pc.LocalAddr().String()+"/wt", nil)
	if err != nil {
		t.Fatal(err)
	}
	defer session.CloseWithError(0, "")
	stream, err := session.OpenStreamSync(ctx)
	if err != nil {
		t.Fatal(err)
	}
	deadline, _ := ctx.Deadline()
	stream.SetDeadline(deadline)
	rules := shared.Rules()
	hello, _ := json.Marshal(protocol.Client{Type: "hello", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Ticket: ticket})
	if _, err := stream.Write(append(hello, '\n')); err != nil {
		t.Fatal(err)
	}
	streamReader := bufio.NewReader(stream)
	data, err := streamReader.ReadBytes('\n')
	if err != nil {
		t.Fatal(err)
	}
	var welcome protocol.Server
	if err := json.Unmarshal(data, &welcome); err != nil {
		t.Fatal(err)
	}
	if welcome.Type != "welcome" || welcome.MatchID != started.MatchID || len(welcome.Players) != 2 || playerIn(welcome, welcome.PlayerID).TeamID != "t1" {
		t.Fatalf("WebTransport entered wrong match: %+v", welcome)
	}
	peerTicket, _ := roomTicketHTTP(t, tlsServer, users[1], false)
	peer, peerCtx, peerWelcome := dialMatch(t, tlsServer, peerTicket, matchID)
	loaded, _ := json.Marshal(protocol.Client{Type: "loaded", Protocol: rules.Protocol, Content: rules.Content,
		MatchID: matchID, Generation: welcome.Generation, MapHash: welcome.MapHash})
	if err := session.SendDatagram(loaded); err != nil {
		t.Fatal(err)
	}
	sendLoadedWS(t, peer, peerCtx, peerWelcome)
	waitStartWS(t, peer, peerCtx)
	data, err = streamReader.ReadBytes('\n')
	if err != nil {
		t.Fatal(err)
	}
	var start protocol.Server
	if err := json.Unmarshal(data, &start); err != nil || start.Type != "start" || start.Tick != 0 || start.Match == nil || start.Match.Phase != "playing" {
		t.Fatalf("WebTransport missed reliable match start: %+v, %v", start, err)
	}
	input, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Generation: welcome.Generation, Seq: 1, X: 1})
	if err := session.SendDatagram(input); err != nil {
		t.Fatal(err)
	}
	for {
		data, err := session.ReceiveDatagram(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		if err := json.Unmarshal(data, &state); err != nil {
			t.Fatal(err)
		}
		if playerIn(state, welcome.PlayerID).X > playerIn(welcome, welcome.PlayerID).X {
			if state.MatchID != matchID {
				t.Fatalf("wrong match snapshot: %+v", state)
			}
			break
		}
	}
}

func TestThreeClientOneVersusTwoRoomMatch(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2", "t2"})
	started := startRoomHTTP(t, server, users[0])
	if started.Phase != "loading" {
		t.Fatalf("match clock began before client loading: %+v", started)
	}
	counts := map[string]int{}
	var connections []*websocket.Conn
	var contexts []context.Context
	var welcomes []protocol.Server
	for _, user := range users {
		ticket, matchID := roomTicketHTTP(t, server, user, false)
		conn, ctx, welcome := dialMatch(t, server, ticket, matchID)
		if welcome.Type != "welcome" || welcome.MatchID != started.MatchID || len(welcome.Players) != 3 {
			t.Fatalf("1V2 roster unavailable: %+v", welcome)
		}
		connections = append(connections, conn)
		contexts = append(contexts, ctx)
		welcomes = append(welcomes, welcome)
		counts[playerIn(welcome, welcome.PlayerID).TeamID]++
	}
	if counts["t1"] != 1 || counts["t2"] != 2 {
		t.Fatalf("wrong team distribution: %+v", counts)
	}
	for i := 0; i < 2; i++ {
		sendLoadedWS(t, connections[i], contexts[i], welcomes[i])
	}
	s.matchMu.Lock()
	active := s.matches[started.MatchID]
	s.matchMu.Unlock()
	if active == nil {
		t.Fatal("loading match vanished")
	}
	time.Sleep(80 * time.Millisecond)
	before, err := active.engine.InspectPlayer(contexts[0], users[0].user)
	if err != nil || before.Tick != 0 || before.Match == nil || before.Match.Phase != "loading" {
		t.Fatalf("partial 1V2 roster started match: %+v, %v", before, err)
	}
	sendLoadedWS(t, connections[2], contexts[2], welcomes[2])
	for i := range connections {
		waitStartWS(t, connections[i], contexts[i])
	}
	deadline := time.Now().Add(time.Second)
	for {
		current, err := s.Lobby.Current(users[0].user)
		if err == nil && current.Phase == "playing" {
			break
		}
		if time.Now().After(deadline) {
			t.Fatalf("lobby never entered playing phase: %+v, %v", current, err)
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func TestPrivateMatchLoadingTimeoutReturnsRoomToPreparation(t *testing.T) {
	s, server := setup(t)
	s.loadTimeout = 120 * time.Millisecond
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2"})
	started := startRoomHTTP(t, server, users[0])
	if started.Phase != "loading" {
		t.Fatalf("room did not enter loading: %+v", started)
	}
	staleTicket, matchID := roomTicketHTTP(t, server, users[1], false)
	deadline := time.Now().Add(2 * time.Second)
	for {
		current, err := s.Lobby.Current(users[0].user)
		if err == nil && !current.Started && current.Phase == "" && current.MatchID == "" && s.activeMatchCount() == 0 {
			if current.CanStart {
				t.Fatalf("timed-out room remained ready: %+v", current)
			}
			for _, member := range current.Members {
				if member.Ready {
					t.Fatalf("timed-out room retained ready member: %+v", current)
				}
			}
			break
		}
		if time.Now().After(deadline) {
			t.Fatalf("loading timeout did not release room: %+v, %v", current, err)
		}
		time.Sleep(10 * time.Millisecond)
	}
	_, _, rejected := dialMatch(t, server, staleTicket, matchID)
	if rejected.Type != "error" || (rejected.Code != "not_in_match" && rejected.Code != "match_not_found") {
		t.Fatalf("timed-out match ticket still admitted: %+v", rejected)
	}
}
