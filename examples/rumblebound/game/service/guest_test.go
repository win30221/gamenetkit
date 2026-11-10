package gateway

import (
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func guestRequest(t *testing.T, server *httptest.Server, origin, body string) *http.Response {
	t.Helper()
	r, err := http.NewRequest(http.MethodPost, server.URL+"/api/session/guest", strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	r.Header.Set("Content-Type", "application/json")
	r.Header.Set("Origin", origin)
	resp, err := server.Client().Do(r)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { resp.Body.Close() })
	return resp
}

func TestGuestHTTPConfigurationAndRequestValidation(t *testing.T) {
	s, server := setup(t)
	checkConfig := func(want bool) {
		t.Helper()
		resp, err := server.Client().Get(server.URL + "/api/config")
		if err != nil {
			t.Fatal(err)
		}
		defer resp.Body.Close()
		var config struct{ GuestLogin bool }
		if json.NewDecoder(resp.Body).Decode(&config) != nil || config.GuestLogin != want {
			t.Fatalf("guestLogin configuration should be %v", want)
		}
	}
	checkConfig(false)
	if resp := guestRequest(t, server, "https://allowed.test", "{}"); resp.StatusCode != http.StatusForbidden {
		t.Fatalf("disabled guest entry returned %d", resp.StatusCode)
	}
	s.GuestLogin = true
	checkConfig(true)
	for _, tc := range []struct {
		origin, body string
		status       int
	}{
		{"https://evil.test", "{}", http.StatusForbidden},
		{"https://allowed.test", `{"userID":"victim"}`, http.StatusBadRequest},
		{"https://allowed.test", `{"session":"forged"}`, http.StatusBadRequest},
		{"https://allowed.test", "null", http.StatusBadRequest},
		{"https://allowed.test", "[]", http.StatusBadRequest},
		{"https://allowed.test", "{} {}", http.StatusBadRequest},
		{"https://allowed.test", "{", http.StatusBadRequest},
		{"https://allowed.test", "{\"padding\":\"" + strings.Repeat("x", 1100) + "\"}", http.StatusBadRequest},
	} {
		resp := guestRequest(t, server, tc.origin, tc.body)
		if resp.StatusCode != tc.status {
			t.Fatalf("origin %q body %q: got %d want %d", tc.origin, tc.body, resp.StatusCode, tc.status)
		}
	}
	for _, tc := range []struct {
		contentType, fetchSite string
		status                 int
	}{
		{"text/plain", "", http.StatusUnsupportedMediaType},
		{"application/json", "cross-site", http.StatusForbidden},
	} {
		r, _ := http.NewRequest(http.MethodPost, server.URL+"/api/session/guest", strings.NewReader("{}"))
		r.Header.Set("Content-Type", tc.contentType)
		r.Header.Set("Sec-Fetch-Site", tc.fetchSite)
		resp, err := server.Client().Do(r)
		if err != nil {
			t.Fatal(err)
		}
		resp.Body.Close()
		if resp.StatusCode != tc.status {
			t.Fatalf("request headers returned %d want %d", resp.StatusCode, tc.status)
		}
	}
	// Enabling guests does not turn the development-key endpoint into a bypass.
	r, _ := http.NewRequest(http.MethodPost, server.URL+"/api/session", strings.NewReader("{}"))
	resp, err := server.Client().Do(r)
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusUnauthorized {
		t.Fatalf("keyless development session returned %d", resp.StatusCode)
	}
}

func TestGuestHTTPIdentityCookieAndTickets(t *testing.T) {
	s, server := setup(t)
	s.GuestLogin = true
	type identityReply struct {
		Session, UserID     string
		Expires             time.Time
		Guest, TestIdentity bool
	}
	var identities []identityReply
	for _, origin := range []string{"https://allowed.test", ""} {
		resp := guestRequest(t, server, origin, "{}")
		if resp.StatusCode != http.StatusOK {
			t.Fatalf("guest login returned %d", resp.StatusCode)
		}
		var identity identityReply
		if err := json.NewDecoder(resp.Body).Decode(&identity); err != nil {
			t.Fatal(err)
		}
		if !identity.Guest || !identity.TestIdentity || !strings.HasPrefix(identity.UserID, "guest_") || len(identity.Session) != 43 || time.Until(identity.Expires) < 59*time.Minute {
			t.Fatal("guest response is not a temporary random identity")
		}
		cookies := resp.Cookies()
		if len(cookies) != 1 || cookies[0].Name != "rb_g0" || cookies[0].Value != identity.Session || !cookies[0].HttpOnly || !cookies[0].Secure || cookies[0].SameSite != http.SameSiteStrictMode || cookies[0].MaxAge != 3600 {
			t.Fatal("guest session cookie differs from existing session policy")
		}
		identities = append(identities, identity)
	}
	if identities[0].Session == identities[1].Session || identities[0].UserID == identities[1].UserID {
		t.Fatal("independent guest logins reused an identity")
	}
	for _, identity := range identities {
		r, _ := http.NewRequest(http.MethodPost, server.URL+"/api/ticket", strings.NewReader(`{"takeover":false}`))
		r.Header.Set("Authorization", "Bearer "+identity.Session)
		resp, err := server.Client().Do(r)
		if err != nil {
			t.Fatal(err)
		}
		var result struct{ Ticket string }
		err = json.NewDecoder(resp.Body).Decode(&result)
		resp.Body.Close()
		if err != nil || resp.StatusCode != http.StatusOK || result.Ticket == "" {
			t.Fatal("guest could not use the existing native ticket flow")
		}
		got, _, err := s.Auth.Consume(result.Ticket)
		if err != nil || got.UserID != identity.UserID {
			t.Fatal("ticket did not resolve to its guest")
		}
		s.Auth.Revoke(identity.Session)
		if _, err := s.Auth.Ticket(identity.Session, false); err == nil {
			t.Fatal("revoked guest can issue tickets")
		}
	}
}

func TestGuestHTTPRateLimitIgnoresForwardedAddress(t *testing.T) {
	s, server := setup(t)
	s.GuestLogin = true
	for i := 0; i <= guestLoginsPerMinute; i++ {
		r, _ := http.NewRequest(http.MethodPost, server.URL+"/api/session/guest", strings.NewReader("{}"))
		r.Header.Set("Content-Type", "application/json")
		r.Header.Set("X-Forwarded-For", fmt.Sprintf("192.0.2.%d", i))
		resp, err := server.Client().Do(r)
		if err != nil {
			t.Fatal(err)
		}
		resp.Body.Close()
		if i < guestLoginsPerMinute && resp.StatusCode != http.StatusOK {
			t.Fatalf("guest %d returned %d", i, resp.StatusCode)
		}
		if i == guestLoginsPerMinute && (resp.StatusCode != http.StatusTooManyRequests || resp.Header.Get("Retry-After") != "60") {
			t.Fatal("guest rate limit could be bypassed with forwarded address")
		}
	}
}

func TestGuestSharedSessionCapacityAndExpiry(t *testing.T) {
	a, _ := NewAuth(testKey)
	now := time.Now()
	a.now = func() time.Time { return now }
	first, identity, err := a.GuestSession()
	if err != nil || identity.Expires != now.Add(time.Hour) {
		t.Fatal("guest expiry differs from session expiry")
	}
	for i := 1; i < 256; i++ {
		if i%2 == 0 {
			_, _, err = a.GuestSession()
		} else {
			_, _, err = a.NewSession(testKey)
		}
		if err != nil {
			t.Fatal(err)
		}
	}
	if _, _, err := a.GuestSession(); err == nil || err.Error() != "session_capacity" {
		t.Fatal("guest bypassed the shared session cap")
	}
	if _, _, err := a.NewSession(testKey); err == nil || err.Error() != "session_capacity" {
		t.Fatal("development sessions do not share guest capacity")
	}
	now = now.Add(time.Hour)
	if _, valid := a.Valid(sha256.Sum256([]byte(first))); valid {
		t.Fatal("expired guest remains valid")
	}
	if _, _, err := a.GuestSession(); err != nil {
		t.Fatalf("expired sessions did not release capacity: %v", err)
	}
}

func TestGuestLimiterConcurrentCapacityAndIdleExpiry(t *testing.T) {
	var limiter guestLimiter
	now := time.Now()
	var accepted atomic.Int32
	var wg sync.WaitGroup
	for range 64 {
		wg.Go(func() {
			if limiter.allow("127.0.0.1", now) {
				accepted.Add(1)
			}
		})
	}
	wg.Wait()
	if accepted.Load() != guestLoginsPerMinute {
		t.Fatalf("concurrent issuance accepted %d", accepted.Load())
	}
	for i := 1; i < guestLimiterCapacity; i++ {
		if !limiter.allow(fmt.Sprintf("peer%d", i), now) {
			t.Fatal("limiter filled before its bound")
		}
	}
	if limiter.allow("overflow", now) || len(limiter.peers) != guestLimiterCapacity {
		t.Fatal("limiter storage exceeded bound")
	}
	if !limiter.allow("127.0.0.1", now.Add(time.Minute)) || len(limiter.peers) != 1 {
		t.Fatal("limiter did not release idle peers")
	}
	if guestPeer(&http.Request{RemoteAddr: "[::ffff:127.0.0.1]:1234"}) != "127.0.0.1" {
		t.Fatal("equivalent IP representation splits guest limits")
	}
}

func TestGuestLimiterRefillsWithoutWindowReset(t *testing.T) {
	var limiter guestLimiter
	now := time.Unix(1000, 0)
	for range guestLoginsPerMinute {
		if !limiter.allow("peer", now) {
			t.Fatal("initial burst was unavailable")
		}
	}
	if limiter.allow("peer", now.Add(5*time.Second-time.Nanosecond)) {
		t.Fatal("token refilled before five seconds")
	}
	if !limiter.allow("peer", now.Add(5*time.Second)) || limiter.allow("peer", now.Add(5*time.Second)) {
		t.Fatal("five seconds should refill exactly one token")
	}
	// Consume the ten tokens accumulated since the first refill. Keep the peer
	// active across the minute boundary so idle expiry cannot recreate a bucket.
	for range 10 {
		if !limiter.allow("peer", now.Add(55*time.Second)) {
			t.Fatal("expected continuously refilled token")
		}
	}
	if limiter.allow("peer", now.Add(time.Minute-time.Nanosecond)) {
		t.Fatal("unexpected token before the next refill")
	}
	if !limiter.allow("peer", now.Add(time.Minute)) || limiter.allow("peer", now.Add(time.Minute)) {
		t.Fatal("minute boundary reset the burst instead of refilling one token")
	}
}

func TestGuestLimiterRejectedActivityExtendsIdleExpiry(t *testing.T) {
	var limiter guestLimiter
	now := time.Unix(1000, 0)
	for range guestLoginsPerMinute {
		limiter.allow("busy", now)
	}
	if limiter.allow("busy", now.Add(time.Second)) {
		t.Fatal("exhausted bucket accepted a request")
	}
	limiter.allow("other", now.Add(time.Minute))
	if _, exists := limiter.peers["busy"]; !exists {
		t.Fatal("rejected activity did not retain the active peer")
	}
	limiter.allow("other", now.Add(time.Minute+time.Second))
	if _, exists := limiter.peers["busy"]; exists {
		t.Fatal("idle peer was not expired at one minute")
	}
}
