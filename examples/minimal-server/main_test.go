package main

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func TestHTTPContracts(t *testing.T) {
	want, err := os.ReadFile("../../packages/unity/transport/Browser/transport.js")
	if err != nil {
		t.Fatal(err)
	}
	server := httptest.NewServer(newHandler())
	defer server.Close()
	client := server.Client()
	client.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	for _, check := range []struct {
		method, path string
		status       int
		body         []byte
	}{
		{"GET", "/healthz", 200, []byte("ok")},
		{"HEAD", "/healthz", 200, []byte{}},
		{"GET", "/transport.js", 200, want},
		{"HEAD", "/transport.js", 200, []byte{}},
		{"POST", "/healthz", 405, nil},
		{"POST", "/transport.js", 405, nil},
		{"POST", "/ws", 405, nil},
		{"GET", "/missing", 404, nil},
		{"GET", "/unity", 301, nil},
		{"GET", "/unity/missing-file", 404, nil},
	} {
		req, _ := http.NewRequest(check.method, server.URL+check.path, nil)
		response, err := client.Do(req)
		if err != nil {
			t.Fatal(err)
		}
		body, err := io.ReadAll(response.Body)
		response.Body.Close()
		if err != nil {
			t.Fatal(err)
		}
		if response.StatusCode != check.status || (check.body != nil && !bytes.Equal(body, check.body)) {
			t.Fatalf("%s %s: status %d, body %q", check.method, check.path, response.StatusCode, body)
		}
		if check.path == "/unity" && response.Header.Get("Location") != "/unity/" {
			t.Fatalf("unity redirect: %s", response.Header.Get("Location"))
		}
	}
}

func TestWebSocketWorldsAreIndependent(t *testing.T) {
	server := httptest.NewServer(newHandler())
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	connect := func() *websocket.Conn {
		t.Helper()
		conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http")+"/ws", nil)
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { conn.CloseNow() })
		if err := conn.Write(ctx, websocket.MessageText, []byte(`{"type":"hello"}`)); err != nil {
			t.Fatal(err)
		}
		return conn
	}
	read := func(conn *websocket.Conn) snapshot {
		t.Helper()
		kind, data, err := conn.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var s snapshot
		if kind != websocket.MessageText || json.Unmarshal(data, &s) != nil || s.Type != "snapshot" || s.Tick <= 0 {
			t.Fatalf("invalid snapshot: %q", data)
		}
		return s
	}
	writeDrive := func(conn *websocket.Conn, x string) {
		t.Helper()
		if err := conn.Write(ctx, websocket.MessageText, []byte(`{"type":"input","x":`+x+`}`)); err != nil {
			t.Fatal(err)
		}
	}
	first, second := connect(), connect()
	if s := read(first); s.X != 0 {
		t.Fatalf("first world starts at %v", s.X)
	}
	if s := read(second); s.X != 0 {
		t.Fatalf("second world starts at %v", s.X)
	}
	writeDrive(first, "1")
	var moved snapshot
	for moved.X <= 0 {
		moved = read(first)
	}
	if s := read(second); s.X != 0 {
		t.Fatalf("first input moved second world: %v", s.X)
	}
	writeDrive(first, "0")
	previous := read(first)
	for {
		current := read(first)
		if current.Tick <= previous.Tick {
			t.Fatal("ticks did not advance")
		}
		if current.X == previous.X {
			previous = current
			break
		}
		previous = current
	}
	if s := read(first); s.X != previous.X {
		t.Fatalf("zero drive did not stop: %v -> %v", previous.X, s.X)
	}
	first.CloseNow()
	writeDrive(second, "1")
	var survivor snapshot
	for survivor.X <= 0 {
		survivor = read(second)
	}
}

func TestUnityStaticFiles(t *testing.T) {
	root := t.TempDir()
	work := filepath.Join(root, "minimal-server")
	build := filepath.Join(root, "minimal-unity", "Builds", "WebGL")
	for _, dir := range []string{work, build} {
		if err := os.MkdirAll(dir, 0755); err != nil {
			t.Fatal(err)
		}
	}
	want := []byte("unity build fixture")
	if err := os.WriteFile(filepath.Join(build, "runtime.data"), want, 0644); err != nil {
		t.Fatal(err)
	}
	t.Chdir(work)
	server := httptest.NewServer(newHandler())
	defer server.Close()
	for _, method := range []string{http.MethodGet, http.MethodHead} {
		req, _ := http.NewRequest(method, server.URL+"/unity/runtime.data", nil)
		response, err := server.Client().Do(req)
		if err != nil {
			t.Fatal(err)
		}
		body, err := io.ReadAll(response.Body)
		response.Body.Close()
		if err != nil {
			t.Fatal(err)
		}
		if response.StatusCode != http.StatusOK {
			t.Fatalf("%s static file: %d", method, response.StatusCode)
		}
		if method == http.MethodGet && !bytes.Equal(body, want) {
			t.Fatalf("static content: %q", body)
		}
		if method == http.MethodHead && len(body) != 0 {
			t.Fatalf("HEAD static body: %q", body)
		}
	}
}
