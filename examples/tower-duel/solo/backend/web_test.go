package main

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/cookiejar"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func webFixture(t *testing.T) (fixture, *httptest.Server, *http.Client) {
	t.Helper()
	f := newFixture(t, false)
	root := t.TempDir()
	for name, value := range map[string]string{"index.html": "solo-plugin", "solo-build.txt": "solo-test-build", "test.wasm.gz": "compressed-fixture"} {
		if err := os.WriteFile(filepath.Join(root, name), []byte(value), 0600); err != nil {
			t.Fatal(err)
		}
	}
	web := httptest.NewUnstartedServer(nil)
	handler, err := f.c.webHandler(root, "http://"+web.Listener.Addr().String())
	if err != nil {
		t.Fatal(err)
	}
	web.Config.Handler = handler
	web.Start()
	t.Cleanup(web.Close)
	jar, _ := cookiejar.New(nil)
	return f, web, &http.Client{Jar: jar, Timeout: 5 * time.Second}
}

func webRequest(t *testing.T, client *http.Client, method, url, origin, body string) *http.Response {
	t.Helper()
	r, err := http.NewRequest(method, url, strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	if origin != "" {
		r.Header.Set("Origin", origin)
	}
	r.Header.Set("Content-Type", "application/json")
	response, err := client.Do(r)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { response.Body.Close() })
	return response
}

func TestWebSessionsProtectRoomControlAndKeepNativeAPIPrivate(t *testing.T) {
	_, web, owner := webFixture(t)
	for _, origin := range []string{"", "https://unrelated.example"} {
		response := webRequest(t, owner, "POST", web.URL+"/solo/rooms", origin, `{"mode":"sandbox"}`)
		if response.StatusCode != http.StatusForbidden {
			t.Fatal("unsafe browser origin accepted")
		}
	}
	created := webRequest(t, owner, "POST", web.URL+"/solo/rooms", web.URL, `{"mode":"sandbox"}`)
	var result struct{ Room, Mode, Server string }
	if json.NewDecoder(created.Body).Decode(&result) != nil || created.StatusCode != 201 || result.Server != web.URL || result.Room == "" {
		t.Fatal("browser create failed", result)
	}
	if cookies := created.Cookies(); len(cookies) != 1 || !cookies[0].HttpOnly || cookies[0].SameSite != http.SameSiteStrictMode {
		t.Fatal("browser session is not protected")
	}
	other := &http.Client{Timeout: time.Second}
	for _, method := range []string{"GET", "DELETE"} {
		response := webRequest(t, other, method, web.URL+"/solo/rooms/"+result.Room, web.URL, "")
		if response.StatusCode != 404 {
			t.Fatal("other browser can control a test room", method, response.StatusCode)
		}
	}
	if response := webRequest(t, owner, "GET", web.URL+"/solo/rooms/"+result.Room, "", ""); response.StatusCode != 200 {
		t.Fatal("own room status unavailable")
	}
	if response := webRequest(t, owner, "DELETE", web.URL+"/solo/rooms/"+result.Room, web.URL, ""); response.StatusCode != 204 {
		t.Fatal("own room cleanup failed")
	}
	if response := webRequest(t, owner, "DELETE", web.URL+"/solo/rooms/"+result.Room, web.URL, ""); response.StatusCode != 204 {
		t.Fatal("already-closed room cleanup was not idempotent")
	}
}

func TestWebProxyUsesRealGameHandshakeAndAuthoritativeDeployment(t *testing.T) {
	f, web, owner := webFixture(t)
	response := webRequest(t, owner, "POST", web.URL+"/solo/rooms", web.URL, `{"mode":"practice"}`)
	var created struct{ Room string }
	if json.NewDecoder(response.Body).Decode(&created) != nil || created.Room == "" {
		t.Fatal("create failed")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(web.URL, "http")+"/ws", &websocket.DialOptions{HTTPHeader: http.Header{"Origin": []string{web.URL}}})
	if err != nil {
		t.Fatal(err)
	}
	defer conn.CloseNow()
	send(t, conn, map[string]any{"type": "hello", "protocol": f.c.config.Protocol, "mode": "join", "room": created.Room, "token": "", "name": "Web solo tester", "mapHash": f.c.config.MapHash, "rulesHash": f.c.config.RulesHash})
	welcome := read(t, conn, func(s message) bool { return s.Type == "welcome" || s.Type == "error" })
	if welcome.Type != "welcome" || welcome.Seat != 1 {
		t.Fatal("web proxy did not preserve the normal join protocol", welcome.Code)
	}
	send(t, conn, map[string]string{"type": "ready", "match": welcome.Match})
	read(t, conn, func(s message) bool { return s.Phase == "playing" })
	send(t, conn, deployment{Type: "deploy", Match: welcome.Match, Action: 1, Card: "guard", X: -5.5, Z: 3.5})
	ack := read(t, conn, func(s message) bool { return s.Type == "action" })
	if !ack.Accepted || ack.HandVersion != 1 || ack.Energy >= 3 {
		t.Fatal("web proxy did not return real backend authority", ack)
	}
	send(t, conn, map[string]string{"type": "leave", "match": welcome.Match})
}

func TestWebAssetHeadersAndPublicConfig(t *testing.T) {
	_, web, client := webFixture(t)
	for path, want := range map[string]string{"/": "solo-plugin", "/solo/config": "tower-duel-solo", "/healthz": "solo-test-build", "/api/config": "rulesHash"} {
		response := webRequest(t, client, "GET", web.URL+path, "", "")
		var text strings.Builder
		_, _ = io.Copy(&text, response.Body)
		if response.StatusCode != 200 || !strings.Contains(text.String(), want) {
			t.Fatal("web route unavailable", path)
		}
	}
	request, _ := http.NewRequest("GET", web.URL+"/test.wasm.gz", nil)
	request.Header.Set("Accept-Encoding", "identity")
	response, err := client.Do(request)
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	if response.Header.Get("Content-Type") != "application/wasm" || response.Header.Get("Content-Encoding") != "gzip" {
		t.Fatal("WebAssembly compression headers incorrect")
	}
}

func TestPublicHTTPSCookieIsSecure(t *testing.T) {
	c := &companion{webOrigin: "https://solo.example.com"}
	w := httptest.NewRecorder()
	r := httptest.NewRequest("POST", "https://solo.example.com/solo/rooms", nil)
	value, err := c.owner(w, r, true)
	if err != nil || value == "" {
		t.Fatal(err)
	}
	cookies := w.Result().Cookies()
	if len(cookies) != 1 || !cookies[0].Secure || !cookies[0].HttpOnly {
		t.Fatal("HTTPS session cookie is not secure")
	}
}
