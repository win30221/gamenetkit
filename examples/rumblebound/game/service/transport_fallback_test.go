package gateway

import (
	"bufio"
	"bytes"
	"context"
	"crypto/tls"
	"crypto/x509"
	"net"
	"net/http"
	"testing"
	"time"

	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

func TestWebTransportLargeSnapshotUsesBoundedReliableStream(t *testing.T) {
	s, tlsServer := setup(t)
	pool := x509.NewCertPool()
	pool.AddCert(tlsServer.Certificate())
	packetConn, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer packetConn.Close()
	mux := http.NewServeMux()
	wt := &webtransport.Server{H3: &http3.Server{TLSConfig: http3.ConfigureTLSConfig(&tls.Config{Certificates: tlsServer.TLS.Certificates}), Handler: mux}}
	defer wt.Close()
	payload := bytes.Repeat([]byte("p"), protocol.MaxDatagram+1)
	wrote := make(chan error, 1)
	clientRead := make(chan struct{})
	defer close(clientRead)
	mux.HandleFunc("/wt", func(w http.ResponseWriter, r *http.Request) {
		session, err := wt.Upgrade(w, r)
		if err != nil {
			wrote <- err
			return
		}
		defer session.CloseWithError(0, "")
		stream, err := session.AcceptStream(r.Context())
		if err != nil {
			wrote <- err
			return
		}
		ctx, cancel := context.WithTimeout(r.Context(), 3*time.Second)
		defer cancel()
		wire := &wtWire{s: session, stream: stream, server: s}
		wrote <- wire.Write(ctx, payload, false)
		select {
		case <-clientRead:
		case <-ctx.Done():
		}
	})
	serverDone := make(chan error, 1)
	go func() { serverDone <- wt.Serve(packetConn) }()
	defer func() { wt.Close(); <-serverDone }()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	transport := &webtransport.Transport{TLSClientConfig: &tls.Config{RootCAs: pool}}
	_, session, err := transport.Dial(ctx, "https://"+packetConn.LocalAddr().String()+"/wt", nil)
	if err != nil {
		t.Fatal(err)
	}
	defer session.CloseWithError(0, "")
	stream, err := session.OpenStreamSync(ctx)
	if err != nil {
		t.Fatal(err)
	}
	// A QUIC stream becomes visible to AcceptStream after its first frame.
	if _, err := stream.Write([]byte("ready")); err != nil {
		t.Fatal(err)
	}
	deadline, _ := ctx.Deadline()
	if err := stream.SetReadDeadline(deadline); err != nil {
		t.Fatal(err)
	}
	got, err := bufio.NewReader(stream).ReadBytes('\n')
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(got, append(payload, '\n')) {
		t.Fatalf("reliable snapshot changed: got %d bytes", len(got))
	}
	if err := <-wrote; err != nil {
		t.Fatal(err)
	}
	if s.OversizedDatagrams.Load() != 0 {
		t.Fatal("bounded reliable fallback counted as a dropped datagram")
	}
}

func TestWebTransportRejectsSnapshotBeyondReliableParserBudget(t *testing.T) {
	s, _ := setup(t)
	wire := &wtWire{server: s}
	err := wire.Write(context.Background(), bytes.Repeat([]byte("p"), protocol.MaxSnapshot+1), false)
	if err == nil || err.Error() != "snapshot_exceeds_stream_budget" {
		t.Fatalf("oversized snapshot was accepted: %v", err)
	}
	if s.OversizedDatagrams.Load() != 1 {
		t.Fatalf("oversized snapshot was not counted: %d", s.OversizedDatagrams.Load())
	}
}
