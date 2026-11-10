package gateway

import (
	"bufio"
	"bytes"
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func setup(t *testing.T) (*Server, *httptest.Server) {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	e := sim.NewEngine(ctx)
	a, _ := NewAuth(testKey)
	s := New(a, e, []string{"https://allowed.test"})
	mux := NewRouter()
	s.Register(mux)
	server := httptest.NewTLSServer(mux)
	t.Cleanup(func() { cancel(); <-e.Done; server.Close() })
	return s, server
}
func hello(tk string) []byte {
	c := shared.Rules()
	b, _ := json.Marshal(protocol.Client{Type: "hello", Protocol: c.Protocol, Content: c.Content, MatchID: sim.MatchID, Ticket: tk})
	return b
}
func input(g uint32) []byte {
	c := shared.Rules()
	b, _ := json.Marshal(protocol.Client{Type: "input", Protocol: c.Protocol, Content: c.Content, MatchID: sim.MatchID, Generation: g, Seq: 1, X: 1, Echo: 10})
	return b
}
func TestWSSMovementTakeoverAndRevocation(t *testing.T) {
	s, server := setup(t)
	raw, _, _ := s.Auth.NewSession(testKey)
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	dial := func(takeover bool) (*websocket.Conn, protocol.Server) {
		tk, err := s.Auth.Ticket(raw, takeover)
		if err != nil {
			t.Fatal(err)
		}
		c, _, err := websocket.Dial(ctx, "wss"+strings.TrimPrefix(server.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: server.Client()})
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { c.CloseNow() })
		if err = c.Write(ctx, websocket.MessageText, hello(tk)); err != nil {
			t.Fatal(err)
		}
		_, b, err := c.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var v protocol.Server
		if err = json.Unmarshal(b, &v); err != nil {
			t.Fatal(err)
		}
		if v.Type != "welcome" {
			t.Fatalf("%s", b)
		}
		return c, v
	}
	c, w := dial(false)
	if err := c.Write(ctx, websocket.MessageText, input(w.Generation)); err != nil {
		t.Fatal(err)
	}
	for {
		_, b, err := c.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var v protocol.Server
		json.Unmarshal(b, &v)
		if v.Players[0].X > 0 {
			break
		}
	}
	newConn, newWelcome := dial(true)
	if newWelcome.PlayerID != w.PlayerID || newWelcome.Generation <= w.Generation || newWelcome.Players[0].X <= 0 {
		t.Fatal("takeover lost state")
	}
	// Old connection must close, even if one already-buffered snapshot arrives.
	for {
		_, _, err := c.Read(ctx)
		if err != nil {
			break
		}
	}
	s.Auth.Revoke(raw)
	for {
		_, _, err := newConn.Read(ctx)
		if err != nil {
			break
		}
	}
	if ctx.Err() != nil {
		t.Fatal("revocation or takeover did not close promptly")
	}
}
func TestHTTPRejectsOriginAndInvalidBody(t *testing.T) {
	_, server := setup(t)
	for _, tc := range []struct {
		origin, path, body string
		status             int
	}{{"https://evil.test", "/api/session", "{}", 403}, {"https://allowed.test", "/api/ticket", `{"takeover":true,"userID":"victim"}`, 400}, {"", "/api/ticket", "{}", 403}} {
		r, _ := http.NewRequest("POST", server.URL+tc.path, bytes.NewBufferString(tc.body))
		r.Header.Set("Origin", tc.origin)
		resp, err := server.Client().Do(r)
		if err != nil {
			t.Fatal(err)
		}
		resp.Body.Close()
		if resp.StatusCode != tc.status {
			t.Fatalf("%s: %d", tc.path, resp.StatusCode)
		}
	}
}
func TestWebTransportReliableHelloAndDatagrams(t *testing.T) {
	s, tlsServer := setup(t)
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
	raw, _, _ := s.Auth.NewSession(testKey)
	tk, _ := s.Auth.Ticket(raw, false)
	if _, err = stream.Write(append(hello(tk), '\n')); err != nil {
		t.Fatal(err)
	}
	b, err := bufio.NewReader(stream).ReadBytes('\n')
	if err != nil {
		t.Fatal(err)
	}
	var welcome protocol.Server
	if err = json.Unmarshal(b, &welcome); err != nil || welcome.Type != "welcome" {
		t.Fatalf("%s %v", b, err)
	}
	if err = session.SendDatagram(input(welcome.Generation)); err != nil {
		t.Fatal(err)
	}
	rules := shared.Rules()
	probe, _ := json.Marshal(protocol.Client{Type: "ping", Protocol: rules.Protocol, Content: rules.Content, MatchID: welcome.MatchID, Generation: welcome.Generation, Echo: 123.5})
	if err = session.SendDatagram(probe); err != nil {
		t.Fatal(err)
	}
	pong, moved := false, false
	for {
		b, err = session.ReceiveDatagram(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		json.Unmarshal(b, &state)
		if state.Type == "pong" {
			if state.Echo != 123.5 || state.Generation != welcome.Generation || state.MatchID != welcome.MatchID {
				t.Fatal("invalid datagram pong")
			}
			pong = true
		} else if len(state.Players) > 0 && state.Players[0].X > 0 {
			if state.Players[0].Ack != 1 {
				t.Fatal("unacknowledged movement")
			}
			moved = true
		}
		if pong && moved {
			break
		}
	}
}
