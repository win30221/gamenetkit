package gateway

import (
	"mime"
	"net"
	"net/http"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"golang.org/x/time/rate"
)

const (
	guestLoginsPerMinute = 12
	guestLimiterCapacity = 256
)

type guestBucket struct {
	limiter  *rate.Limiter
	lastSeen time.Time
}

// The development guest entry uses actual peers, never forwarded headers.
// Burst 12 refills one token every five seconds; there is no fixed-window reset.
// Storage expires peers after one idle minute and caps at 256.
type guestLimiter struct {
	mu    sync.Mutex
	peers map[string]guestBucket
}

func (l *guestLimiter) allow(peer string, now time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.peers == nil {
		l.peers = make(map[string]guestBucket)
	}
	for ip, bucket := range l.peers {
		if now.Sub(bucket.lastSeen) >= time.Minute {
			delete(l.peers, ip)
		}
	}
	bucket, exists := l.peers[peer]
	if !exists {
		if len(l.peers) >= guestLimiterCapacity {
			return false
		}
		bucket.limiter = rate.NewLimiter(rate.Every(time.Minute/guestLoginsPerMinute), guestLoginsPerMinute)
	}
	bucket.lastSeen = now
	l.peers[peer] = bucket
	return bucket.limiter.AllowN(now, 1)
}

func guestPeer(r *http.Request) string {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		host = r.RemoteAddr
	}
	if ip := net.ParseIP(host); ip != nil {
		return ip.String()
	}
	return "unknown"
}

func (s *Server) registerGuest(mux gin.IRouter) {
	mux.POST("/api/session/guest", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !s.Origin(r) || r.Header.Get("Sec-Fetch-Site") == "cross-site" {
			fail(w, http.StatusForbidden, "origin_rejected")
			return
		}
		if !s.GuestLogin {
			fail(w, http.StatusForbidden, "guest_login_disabled")
			return
		}
		mediaType, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
		if err != nil || mediaType != "application/json" {
			fail(w, http.StatusUnsupportedMediaType, "json_required")
			return
		}
		var req *struct{}
		if readJSONLimit(w, r, &req, 1024) != nil || req == nil {
			fail(w, http.StatusBadRequest, "invalid_request")
			return
		}
		if !s.guestLogins.allow(guestPeer(r), time.Now()) {
			w.Header().Set("Retry-After", "60")
			fail(w, http.StatusTooManyRequests, "guest_rate_limited")
			return
		}
		raw, identity, err := s.Auth.GuestSession()
		if err != nil {
			fail(w, http.StatusTooManyRequests, err.Error())
			return
		}
		http.SetCookie(w, &http.Cookie{Name: "rb_g0", Value: raw, Path: "/", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: 3600})
		reply(w, http.StatusOK, map[string]any{"session": raw, "userID": identity.UserID, "expires": identity.Expires, "guest": true, "testIdentity": true})
	}))
}
