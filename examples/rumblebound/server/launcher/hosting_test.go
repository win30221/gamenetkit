package launcher

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestVMConfiguration(t *testing.T) {
	for _, origin := range []string{"", "http://game.example", "https://game.example/path", "https://game.example?x=1", "https://user@game.example"} {
		if validateEnvironment("vm", origin, "") == nil {
			t.Fatalf("accepted invalid VM origin %q", origin)
		}
	}
	if validateEnvironment("vm", "https://game.example", "") != nil || validateEnvironment("local", "http://localhost:18093", "") != nil {
		t.Fatal("valid deployment configuration rejected")
	}
	if validateEnvironment("other", "https://game.example", "") == nil || validateEnvironment("vm", "https://game.example", "127.0.0.1:18493") == nil {
		t.Fatal("unsupported environment/transport accepted")
	}
}

func TestProxyTLSAndPeerBoundary(t *testing.T) {
	called := 0
	handler := proxyRequests(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		called++
		if r.TLS == nil || r.RemoteAddr != "198.51.100.7:0" {
			t.Fatalf("missing trusted TLS/peer metadata: %+v", r)
		}
		http.SetCookie(w, &http.Cookie{Name: "session", Value: "test", Secure: r.TLS != nil, HttpOnly: true})
	}))
	for _, tc := range []struct {
		peer, proto, forwarded string
		status                 int
	}{
		{"127.0.0.1:1234", "https", "198.51.100.7", 200},
		{"[::1]:1234", "https", "198.51.100.7", 200},
		{"198.51.100.8:1234", "https", "198.51.100.7", 403},
		{"127.0.0.1:1234", "http", "198.51.100.7", 403},
		{"127.0.0.1:1234", "https", "198.51.100.7, 198.51.100.8", 400},
		{"127.0.0.1:1234", "https", "", 400},
	} {
		r := httptest.NewRequest("GET", "http://localhost/", nil)
		r.RemoteAddr = tc.peer
		r.Header.Set("X-Forwarded-Proto", tc.proto)
		r.Header.Set("X-Forwarded-For", tc.forwarded)
		w := httptest.NewRecorder()
		handler.ServeHTTP(w, r)
		if w.Code != tc.status {
			t.Fatalf("%+v: got %d", tc, w.Code)
		}
		if w.Code == 200 && !w.Result().Cookies()[0].Secure {
			t.Fatal("insecure proxy cookie")
		}
	}
	if called != 2 {
		t.Fatalf("unexpected handler invocations: %d", called)
	}
}
