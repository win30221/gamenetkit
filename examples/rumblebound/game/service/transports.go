package gateway

import (
	"bufio"
	"context"
	"errors"
	"net/http"
	"sync"
	"time"

	"github.com/coder/websocket"
	quic "github.com/quic-go/quic-go"
	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	rttransport "github.com/win30221/gamenetkit/server/transport"
)

func (s *Server) acquireTransport() (authenticated func(), release func(), ok bool) {
	select {
	case s.handshakes <- struct{}{}:
	default:
		return nil, nil, false
	}
	select {
	case s.Slots <- struct{}{}:
		var once sync.Once
		authenticated = func() { once.Do(func() { <-s.handshakes }) }
		return authenticated, func() { authenticated(); <-s.Slots }, true
	default:
		<-s.handshakes
		return nil, nil, false
	}
}
func (s *Server) WebSocket(w http.ResponseWriter, r *http.Request) {
	if !s.Origin(r) {
		fail(w, 403, "origin_rejected")
		return
	}
	authenticated, release, ok := s.acquireTransport()
	if !ok {
		fail(w, 503, "connection_capacity")
		return
	}
	defer release()
	c, err := websocket.Accept(w, r, &websocket.AcceptOptions{InsecureSkipVerify: true}) // exact allowlist checked above
	if err != nil {
		return
	}
	c.SetReadLimit(protocol.MaxMessage)
	s.serveAdmitted(r.Context(), &wsWire{Conn: c}, authenticated)
}

type wsWire = rttransport.WebSocket

type quicConnectionKey struct{}

func (s *Server) WebTransport(wt *webtransport.Server) http.HandlerFunc {
	// Install before Serve, preserving any existing connection context hook.
	// Session closure alone cannot unblock quic-go's connection-wide datagram
	// send queue. A timed-out write must abort that stalled QUIC connection.
	previous := wt.H3.ConnContext
	wt.H3.ConnContext = func(ctx context.Context, conn *quic.Conn) context.Context {
		if previous != nil {
			ctx = previous(ctx, conn)
		}
		return context.WithValue(ctx, quicConnectionKey{}, conn)
	}
	return func(w http.ResponseWriter, r *http.Request) {
		if !s.Origin(r) {
			fail(w, 403, "origin_rejected")
			return
		}
		authenticated, release, ok := s.acquireTransport()
		if !ok {
			fail(w, 503, "connection_capacity")
			return
		}
		defer release()
		conn, ok := r.Context().Value(quicConnectionKey{}).(*quic.Conn)
		if !ok {
			fail(w, 503, "missing_quic_connection")
			return
		}
		session, err := wt.Upgrade(w, r)
		if err != nil {
			return
		}
		defer session.CloseWithError(0, "")
		ctx, cancel := context.WithTimeout(session.Context(), 5*time.Second)
		stream, err := session.AcceptStream(ctx)
		cancel()
		if err != nil {
			return
		}
		s.serveAdmitted(session.Context(), &wtWire{s: session, stream: stream, reader: bufio.NewReaderSize(stream, protocol.MaxMessage+1), server: s,
			abort: func() {
				_ = conn.CloseWithError(quic.ApplicationErrorCode(http3.ErrCodeInternalError), "datagram_write_timeout")
			}}, authenticated)
	}
}

type wtWire struct {
	s      *webtransport.Session
	stream *webtransport.Stream
	reader *bufio.Reader
	server *Server
	abort  func()
}

func (w *wtWire) ReadHello(ctx context.Context) ([]byte, error) {
	deadline, _ := ctx.Deadline()
	_ = w.stream.SetReadDeadline(deadline)
	b, e := w.reader.ReadSlice('\n')
	return b, e
}
func (w *wtWire) ReadInput(ctx context.Context) ([]byte, error) {
	b, e := w.s.ReceiveDatagram(ctx)
	if len(b) > protocol.MaxDatagram {
		return nil, errors.New("datagram_too_large")
	}
	return b, e
}
func (w *wtWire) Write(ctx context.Context, b []byte, reliable bool) error {
	delivery, err := (rttransport.Limits{Input: protocol.MaxMessage, Datagram: protocol.MaxDatagram, Snapshot: protocol.MaxSnapshot}).Reliable(len(b), reliable)
	if err != nil {
		w.server.OversizedDatagrams.Add(1)
		return err
	}
	if delivery {
		return rttransport.WriteLine(ctx, w.stream, b)
	}
	return boundedDatagram(ctx, w.abort, func() error { return w.s.SendDatagram(b) })
}

func boundedDatagram(ctx context.Context, abort func(), send func() error) error {
	return rttransport.BoundedDatagram(ctx, abort, send)
}

func (w *wtWire) Reject(ctx context.Context, b []byte) error {
	if err := w.Write(ctx, b, true); err != nil {
		return err
	}
	if err := w.stream.Close(); err != nil {
		return err
	}
	// Closing the session immediately resets its streams and can erase even a
	// successful Write. Keep the reliable stream alive until the client handles
	// the rejection and closes, with a bounded deadline for unresponsive peers.
	select {
	case <-w.s.Context().Done():
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}
func (w *wtWire) Close() { _ = w.s.CloseWithError(0, "") }
