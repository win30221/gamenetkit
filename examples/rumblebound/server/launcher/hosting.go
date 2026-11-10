package launcher

import (
	"crypto/tls"
	"errors"
	"net"
	"net/http"
	"net/url"
	"strings"
)

func validateEnvironment(environment, origins, wt string) error {
	if environment == "local" {
		return nil
	}
	if environment != "vm" {
		return errors.New("environment must be local or vm")
	}
	if wt != "" {
		return errors.New("VM deployment uses WSS; development WebTransport is local-only")
	}
	for _, origin := range strings.Split(origins, ",") {
		u, err := url.Parse(origin)
		if err != nil || u.Scheme != "https" || u.Host == "" || u.User != nil || u.Path != "" || u.RawQuery != "" || u.Fragment != "" {
			return errors.New("VM origins must be exact HTTPS origins without paths")
		}
	}
	return nil
}

// VM mode keeps the listener on loopback. Caddy overwrites forwarding headers;
// only that local proxy may supply TLS and peer metadata. Local mode retains
// the existing policy of ignoring forwarding headers entirely.
func proxyRequests(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		peer, _, err := net.SplitHostPort(r.RemoteAddr)
		if err != nil || net.ParseIP(peer) == nil || !net.ParseIP(peer).IsLoopback() || r.Header.Get("X-Forwarded-Proto") != "https" {
			http.Error(w, "HTTPS proxy required", http.StatusForbidden)
			return
		}
		ip := net.ParseIP(strings.TrimSpace(r.Header.Get("X-Forwarded-For")))
		if ip == nil {
			http.Error(w, "single proxy client address required", http.StatusBadRequest)
			return
		}
		r = r.Clone(r.Context())
		r.RemoteAddr = net.JoinHostPort(ip.String(), "0")
		r.TLS = &tls.ConnectionState{}
		next.ServeHTTP(w, r)
	})
}
