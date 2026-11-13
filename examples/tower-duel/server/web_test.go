package main

import (
	"context"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestWebGLStaticHeadersAndAPIRoutes(t *testing.T) {
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "Build"), 0755); err != nil {
		t.Fatal(err)
	}
	files := map[string]string{"index.html": "<title>Game</title>", "Build/game.wasm.gz": "wasm payload", "Build/game.framework.js.gz": "javascript payload", "Build/game.data.gz": "data payload"}
	for name, body := range files {
		if err := os.WriteFile(filepath.Join(dir, filepath.FromSlash(name)), []byte(body), 0644); err != nil {
			t.Fatal(err)
		}
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	app := newApplication(ctx, testMap(t), defaultRules(), "", dir)
	defer app.engine.close()
	for path, mime := range map[string]string{"/Build/game.wasm.gz": "application/wasm", "/Build/game.framework.js.gz": "application/javascript", "/Build/game.data.gz": "application/octet-stream"} {
		response := httptest.NewRecorder()
		app.handler.ServeHTTP(response, httptest.NewRequest("GET", path, nil))
		if response.Code != 200 || response.Header().Get("Content-Type") != mime || response.Header().Get("Content-Encoding") != "gzip" {
			t.Fatal("wrong compressed WebGL response", path, response.Header())
		}
	}
	for _, path := range []string{"/", "/healthz", "/api/config"} {
		response := httptest.NewRecorder()
		app.handler.ServeHTTP(response, httptest.NewRequest("GET", path, nil))
		if response.Code != 200 || response.Header().Get("Content-Encoding") != "" {
			t.Fatal("static handler broke route", path)
		}
	}
}
