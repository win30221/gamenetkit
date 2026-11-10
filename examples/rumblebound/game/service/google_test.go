package gateway

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/identity"
)

func TestGoogleIdentityConcurrentMapping(t *testing.T) {
	a, _ := NewAuth(testKey)
	ids := make(chan string, 24)
	var wg sync.WaitGroup
	for range 24 {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, s, err := a.GoogleSession("same-verified-subject")
			if err != nil {
				t.Error(err)
				return
			}
			ids <- s.UserID
		}()
	}
	wg.Wait()
	close(ids)
	first := ""
	for id := range ids {
		if first == "" {
			first = id
		}
		if id != first {
			t.Fatal("duplicate account")
		}
	}
	_, different, _ := a.GoogleSession("different-subject")
	if different.UserID == first {
		t.Fatal("subjects merged")
	}
}
func TestGoogleAttemptsSingleUseAndExpiry(t *testing.T) {
	a, _ := NewAuth(testKey)
	now := time.Now()
	a.now = func() time.Time { return now }
	raw, nonce, _ := a.BeginGoogle()
	got, err := a.ConsumeGoogle(raw)
	if err != nil || got != nonce {
		t.Fatal(err)
	}
	if _, err = a.ConsumeGoogle(raw); err == nil {
		t.Fatal("replay allowed")
	}
	raw, _, _ = a.BeginGoogle()
	now = now.Add(2 * time.Minute)
	if _, err = a.ConsumeGoogle(raw); err == nil {
		t.Fatal("expired attempt accepted")
	}
}
func TestGoogleHTTPOriginCSRFAndReplay(t *testing.T) {
	a, _ := NewAuth(testKey)
	s := New(a, nil, []string{"https://lab.test"})
	s.Google, _ = identity.NewGoogle("fixture.apps.googleusercontent.com")
	mux := NewRouter()
	s.registerGoogle(mux)
	post := func(path, origin, body string, cookie *http.Cookie) *httptest.ResponseRecorder {
		r := httptest.NewRequest("POST", path, bytes.NewBufferString(body))
		r.Header.Set("Origin", origin)
		if cookie != nil {
			r.AddCookie(cookie)
		}
		w := httptest.NewRecorder()
		mux.ServeHTTP(w, r)
		return w
	}
	if w := post("/api/auth/google/start", "https://evil.test", "{}", nil); w.Code != 403 {
		t.Fatal(w.Code)
	}
	if w := post("/api/auth/google/start", "", "{}", nil); w.Code != 403 {
		t.Fatal(w.Code)
	}
	w := post("/api/auth/google/start", "https://lab.test", "{}", nil)
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	var start struct {
		AttemptID string `json:"attemptID"`
		Nonce     string `json:"nonce"`
	}
	json.Unmarshal(w.Body.Bytes(), &start)
	if len(start.Nonce) != 43 {
		t.Fatal("missing nonce")
	}
	cookie := w.Result().Cookies()[0]
	body, _ := json.Marshal(map[string]string{"attemptID": start.AttemptID, "idToken": "invalid"})
	if w = post("/api/auth/google/exchange", "https://lab.test", string(body), nil); w.Code != 403 {
		t.Fatal("missing CSRF accepted")
	}
	if w = post("/api/auth/google/exchange", "https://lab.test", string(body), cookie); w.Code != 401 || !strings.Contains(w.Body.String(), "google_login_failed") {
		t.Fatal(w.Body.String())
	}
	if w = post("/api/auth/google/exchange", "https://lab.test", string(body), cookie); w.Code != 401 || !strings.Contains(w.Body.String(), "login_attempt_expired") {
		t.Fatal("replayed attempt", w.Body.String())
	}
	s.Google = nil
	if w = post("/api/auth/google/start", "https://lab.test", "{}", nil); w.Code != 503 {
		t.Fatal("unconfigured login enabled")
	}
}
