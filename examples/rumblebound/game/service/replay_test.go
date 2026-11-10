//go:build !rf_no_replay

package gateway

import (
	"context"
	"encoding/json"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func TestReplayRealWSSAndPracticeContextInheritance(t *testing.T) {
	root := t.TempDir()
	parent, stop := context.WithCancel(context.Background())
	defer stop()
	parent, e := sim.WithReplayDirectory(parent, root)
	if e != nil {
		t.Fatal(e)
	}
	engine := sim.NewEngine(parent)
	defer func() { stop(); <-engine.Done }()
	auth, e := NewAuth(testKey)
	if e != nil {
		t.Fatal(e)
	}
	s := New(auth, engine, []string{roomOrigin})
	s.MatchContext = parent
	mux := NewRouter()
	s.Register(mux)
	server := httptest.NewTLSServer(mux)
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	// Exercise actual WSS decoding/admission -> ordered owner commands.
	raw, _, e := auth.NewSession(testKey)
	if e != nil {
		t.Fatal(e)
	}
	ticket, e := auth.Ticket(raw, false)
	if e != nil {
		t.Fatal(e)
	}
	ws, _, e := websocket.Dial(ctx, "wss"+strings.TrimPrefix(server.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: server.Client()})
	if e != nil {
		t.Fatal(e)
	}
	defer ws.CloseNow()
	if e = ws.Write(ctx, websocket.MessageText, hello(ticket)); e != nil {
		t.Fatal(e)
	}
	_, data, e := ws.Read(ctx)
	if e != nil {
		t.Fatal(e)
	}
	var welcome protocol.Server
	if e = json.Unmarshal(data, &welcome); e != nil {
		t.Fatal(e)
	}
	if e = ws.Write(ctx, websocket.MessageText, input(welcome.Generation)); e != nil {
		t.Fatal(e)
	}
	for {
		_, data, e = ws.Read(ctx)
		if e != nil {
			t.Fatal(e)
		}
		var state protocol.Server
		_ = json.Unmarshal(data, &state)
		if len(state.Players) > 0 && state.Players[0].Ack == 1 && state.Players[0].X > 0 {
			break
		}
	}
	ws.CloseNow()
	// A new HTTP practice match must inherit recording; no manual sim hook.
	owner := newRoomIdentity(t, s)
	startPracticeHTTP(t, server, owner)
	ticket, id := roomTicketHTTP(t, server, owner, false)
	conn, matchCtx, matchWelcome := dialMatch(t, server, ticket, id)
	defer conn.CloseNow()
	sendLoadedWS(t, conn, matchCtx, matchWelcome)
	started := waitStartWS(t, conn, matchCtx)
	rules := shared.Rules()
	payload, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content, MatchID: id, Generation: started.Generation, Seq: 1, X: 1, FacingX: 1})
	if e = conn.Write(matchCtx, websocket.MessageText, payload); e != nil {
		t.Fatal(e)
	}
	var matchEngine *sim.Engine
	s.matchMu.Lock()
	matchEngine = s.matches[id].engine
	s.matchMu.Unlock()
	for matchEngine.CurrentTick() < 4 {
		select {
		case <-ctx.Done():
			t.Fatal("practice did not advance")
		case <-time.After(time.Millisecond):
		}
	}
	conn.CloseNow()
	if e = s.ShutdownMatches(ctx); e != nil {
		t.Fatal(e)
	}
	stop()
	<-engine.Done
	<-matchEngine.Done
	files, e := filepath.Glob(filepath.Join(root, "*.replay.jsonl"))
	if e != nil || len(files) != 2 {
		t.Fatalf("recorded files %v %v", files, e)
	}
	for _, file := range files {
		report := sim.VerifyReplay(file)
		if report.Status != "PASS" || report.Summary.Steps < 1 {
			b, _ := json.Marshal(report)
			t.Fatal(string(b))
		}
		t.Logf("REPLAY_WSS PASS match=%s entries=%d steps=%d", report.MatchID, report.Summary.Entries, report.Summary.Steps)
	}
}
