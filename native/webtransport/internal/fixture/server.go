// Package fixture supplies a loopback HTTP/3 echo peer for transport tests only.
package fixture

import (
	"bufio"
	"context"
	"crypto/sha256"
	"crypto/tls"
	"encoding/hex"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"

	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
)

type Server struct {
	URL   string `json:"url"`
	Hash  string `json:"hash"`
	close func()
}

func (s *Server) Close() { s.close() }

func New() (*Server, error) {
	cert := httptest.NewTLSServer(http.NotFoundHandler())
	udp, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		cert.Close()
		return nil, err
	}
	ctx, cancel := context.WithCancel(context.Background())
	mux := http.NewServeMux()
	server := &webtransport.Server{H3: &http3.Server{TLSConfig: http3.ConfigureTLSConfig(&tls.Config{Certificates: cert.TLS.Certificates, MinVersion: tls.VersionTLS13}), Handler: mux}, CheckOrigin: func(*http.Request) bool { return true }}
	var sessions sync.WaitGroup
	mux.HandleFunc("/wt", func(w http.ResponseWriter, r *http.Request) {
		session, err := server.Upgrade(w, r)
		if err != nil {
			return
		}
		sessions.Add(1)
		defer sessions.Done()
		defer session.CloseWithError(0, "")
		stream, err := session.AcceptStream(ctx)
		if err != nil {
			return
		}
		hello, err := bufio.NewReaderSize(stream, 32770).ReadString('\n')
		if err != nil {
			return
		}
		payload := "reliable:" + strings.TrimSuffix(hello, "\n")
		if hello == "boundary\n" {
			payload = strings.Repeat("x", 32768)
		}
		if _, err = stream.Write([]byte(payload + "\n")); err != nil {
			return
		}
		stream.Close() // welcome stream can finish without ending datagrams
		for {
			data, err := session.ReceiveDatagram(ctx)
			if err != nil {
				return
			}
			if err = session.SendDatagram(data); err != nil {
				return
			}
		}
	})
	done := make(chan struct{})
	go func() { defer close(done); server.Serve(udp) }()
	hash := sha256.Sum256(cert.Certificate().Raw)
	return &Server{URL: "https://" + udp.LocalAddr().String() + "/wt", Hash: hex.EncodeToString(hash[:]), close: func() {
		cancel()
		server.Close()
		udp.Close()
		<-done
		sessions.Wait()
		cert.Close()
	}}, nil
}
