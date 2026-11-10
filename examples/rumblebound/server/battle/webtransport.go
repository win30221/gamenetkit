package battle

import (
	"crypto/sha256"
	"crypto/tls"
	"encoding/hex"
	"github.com/quic-go/quic-go/http3"
	webtransport "github.com/quic-go/webtransport-go"
	"net/http"
)

// HTTP/3 needs the native writer interfaces, so its CONNECT endpoint stays
// outside Gin. Both transport routes still use the same admissions and engine.
func (a *Application) WebTransport(address, certFile, keyFile, publicURL string) (*webtransport.Server, error) {
	certificate, err := tls.LoadX509KeyPair(certFile, keyFile)
	if err != nil {
		return nil, err
	}
	hash := sha256.Sum256(certificate.Certificate[0])
	a.API.CertificateHash = hex.EncodeToString(hash[:])
	a.API.WTURL = publicURL
	mux := http.NewServeMux()
	server := &webtransport.Server{H3: &http3.Server{Addr: address, TLSConfig: http3.ConfigureTLSConfig(&tls.Config{Certificates: []tls.Certificate{certificate}, MinVersion: tls.VersionTLS13}), Handler: mux}, CheckOrigin: a.API.Origin}
	mux.HandleFunc("/wt", a.API.WebTransport(server))
	return server, nil
}
