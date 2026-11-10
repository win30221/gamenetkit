package main

import (
	"bytes"
	"context"
	"io"
	"mime"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestBootstrapServesJavaScriptMIMERegardlessOfHostMappings(t *testing.T) {
	// Windows file associations can map module sources to text/plain. A 200
	// response is insufficient: browsers reject module scripts with that type.
	for _, extension := range []string{".js", ".mjs"} {
		previous := mime.TypeByExtension(extension)
		t.Cleanup(func() { _ = mime.AddExtensionType(extension, previous) })
		if err := mime.AddExtensionType(extension, "text/plain; charset=utf-8"); err != nil {
			t.Fatal(err)
		}
	}
	app, err := newApplication(context.Background(), "../../../packages/unity/transport/Browser/transport.js", "../web", "../client/Builds/WebGL")
	if err != nil {
		t.Fatal(err)
	}
	defer app.close()
	for _, path := range []string{"/transport.js", "/app.js"} {
		for _, method := range []string{http.MethodGet, http.MethodHead} {
			response := httptest.NewRecorder()
			app.handler.ServeHTTP(response, httptest.NewRequest(method, path, nil))
			mediaType, _, err := mime.ParseMediaType(response.Header().Get("Content-Type"))
			if response.Code != http.StatusOK || err != nil || mediaType != "text/javascript" {
				t.Fatalf("%s %s: status=%d Content-Type=%q; module scripts require JavaScript MIME", method, path, response.Code, response.Header().Get("Content-Type"))
			}
			if method == http.MethodHead && response.Body.Len() != 0 {
				t.Fatalf("HEAD %s returned a body", path)
			}
		}
	}
}

func TestBootstrapServesCanonicalRuntimeAfterWorkingDirectoryChanges(t *testing.T) {
	source := "../../../packages/unity/transport/Browser/transport.js"
	want, err := os.ReadFile(source)
	if err != nil {
		t.Fatal(err)
	}
	app, err := newApplication(context.Background(), source, "../web", "../client/Builds/WebGL")
	if err != nil {
		t.Fatal(err)
	}
	defer app.close()
	t.Chdir(t.TempDir())
	for _, check := range []struct {
		path   string
		status int
		body   []byte
	}{
		{"/healthz", http.StatusOK, []byte("ok\n")},
		{"/transport.js", http.StatusOK, want},
		{"/ws", http.StatusUpgradeRequired, nil},
	} {
		response := httptest.NewRecorder()
		app.handler.ServeHTTP(response, httptest.NewRequest(http.MethodGet, check.path, nil))
		if response.Code != check.status || (check.body != nil && !bytes.Equal(response.Body.Bytes(), check.body)) {
			t.Fatalf("%s: unexpected status %d or content", check.path, response.Code)
		}
	}
}

func TestBootstrapRejectsUnavailableRuntime(t *testing.T) {
	dir := t.TempDir()
	for _, path := range []string{dir, filepath.Join(dir, "missing.js")} {
		if _, err := newApplication(context.Background(), path, "../web", "../client/Builds/WebGL"); err == nil {
			t.Fatalf("accepted unavailable runtime %s", path)
		}
	}
}

func TestBootstrapHTTPMethods(t *testing.T) {
	app, err := newApplication(context.Background(), "../../../packages/unity/transport/Browser/transport.js", "../web", "../client/Builds/WebGL")
	if err != nil {
		t.Fatal(err)
	}
	defer app.close()
	server := httptest.NewServer(app.handler)
	defer server.Close()
	for _, check := range []struct {
		method, path string
		status       int
	}{
		{"HEAD", "/healthz", 200}, {"HEAD", "/transport.js", 200},
		{"GET", "/", 200}, {"HEAD", "/", 200}, {"GET", "/app.js", 200}, {"GET", "/missing.js", 404},
		{"POST", "/healthz", 405}, {"POST", "/transport.js", 405},
		{"GET", "/ws", 426}, {"HEAD", "/ws", 405}, {"POST", "/ws", 405},
	} {
		req, _ := http.NewRequest(check.method, server.URL+check.path, nil)
		response, err := server.Client().Do(req)
		if err != nil {
			t.Fatal(err)
		}
		body, err := io.ReadAll(response.Body)
		response.Body.Close()
		if err != nil {
			t.Fatal(err)
		}
		if response.StatusCode != check.status {
			t.Fatalf("%s %s: %d", check.method, check.path, response.StatusCode)
		}
		if check.method == "HEAD" && len(body) != 0 {
			t.Fatalf("HEAD %s returned a body", check.path)
		}
	}
}
