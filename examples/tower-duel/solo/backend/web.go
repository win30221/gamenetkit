package main

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

const ownerCookie = "td_solo_session"

func (c *companion) clientServer() string {
	if c.webOrigin != "" {
		return c.webOrigin
	}
	return c.server
}

func (c *companion) allowOrigin(r *http.Request) bool {
	origin := r.Header.Get("Origin")
	if c.webOrigin == "" {
		return origin == ""
	}
	if origin != "" && origin != c.webOrigin {
		return false
	}
	return (r.Method == http.MethodGet || r.Method == http.MethodHead) || origin == c.webOrigin
}

func (c *companion) owner(w http.ResponseWriter, r *http.Request, create bool) (string, error) {
	if c.webOrigin == "" {
		return "", nil
	}
	if cookie, err := r.Cookie(ownerCookie); err == nil {
		if data, err := base64.RawURLEncoding.DecodeString(cookie.Value); err == nil && len(data) == 32 {
			return cookie.Value, nil
		}
	}
	if !create {
		return "", nil
	}
	var secret [32]byte
	if _, err := rand.Read(secret[:]); err != nil {
		return "", err
	}
	value := base64.RawURLEncoding.EncodeToString(secret[:])
	http.SetCookie(w, &http.Cookie{Name: ownerCookie, Value: value, Path: "/", HttpOnly: true,
		Secure: strings.HasPrefix(c.webOrigin, "https://"), SameSite: http.SameSiteStrictMode})
	return value, nil
}

func (c *companion) owns(r *http.Request, room string) bool {
	if c.webOrigin == "" {
		return true
	}
	owner, _ := c.owner(nil, r, false)
	if owner == "" {
		return false
	}
	c.mu.Lock()
	expected := c.owners[room]
	c.mu.Unlock()
	return expected != "" && subtle.ConstantTimeCompare([]byte(owner), []byte(expected)) == 1
}

// This proxy and the bot process are optional. The original game binary and
// wire protocol remain unchanged, and the public multiplayer site is separate.
func (c *companion) webHandler(root, origin string) (http.Handler, error) {
	endpoint, err := url.Parse(origin)
	if err != nil || endpoint.Host == "" || endpoint.User != nil || endpoint.RawQuery != "" || endpoint.Fragment != "" || (endpoint.Path != "" && endpoint.Path != "/") ||
		(endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.Hostname() == "127.0.0.1")) {
		return nil, errors.New("web mode requires an exact HTTPS origin (or loopback HTTP)")
	}
	if _, err := os.Stat(filepath.Join(root, "index.html")); err != nil {
		return nil, err
	}
	c.webOrigin = strings.TrimSuffix(origin, "/")
	c.owners = make(map[string]string)
	upstream, err := url.Parse(c.server)
	if err != nil {
		return nil, err
	}
	proxy := httputil.NewSingleHostReverseProxy(upstream)
	mux := http.NewServeMux()
	mux.Handle("/solo/", c.handler())
	mux.Handle("/ws", proxy)
	mux.Handle("/api/", proxy)
	mux.Handle("/transport.js", proxy)
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		marker, _ := os.ReadFile(filepath.Join(root, "solo-build.txt"))
		reply(w, http.StatusOK, map[string]any{"ok": true, "plugin": "tower-duel-solo", "protocol": c.config.Protocol,
			"mapHash": c.config.MapHash, "rulesHash": c.config.RulesHash, "cards": len(c.config.Cards), "build": strings.TrimSpace(string(marker))})
	})
	files := http.FileServer(http.Dir(root))
	mux.Handle("/", http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet && r.Method != http.MethodHead {
			w.WriteHeader(http.StatusMethodNotAllowed)
			return
		}
		w.Header().Set("Cache-Control", "no-cache")
		w.Header().Set("X-Content-Type-Options", "nosniff")
		if strings.HasSuffix(r.URL.Path, ".gz") {
			w.Header().Set("Content-Encoding", "gzip")
			switch {
			case strings.HasSuffix(r.URL.Path, ".wasm.gz"):
				w.Header().Set("Content-Type", "application/wasm")
			case strings.HasSuffix(r.URL.Path, ".js.gz"):
				w.Header().Set("Content-Type", "application/javascript")
			default:
				w.Header().Set("Content-Type", "application/octet-stream")
			}
		}
		files.ServeHTTP(w, r)
	}))
	return mux, nil
}
