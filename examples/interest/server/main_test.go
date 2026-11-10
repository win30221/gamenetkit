package main

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestAuthorityHTTPRoutes(t *testing.T) {
	app := newApplication(context.Background())
	defer app.close()
	server := httptest.NewServer(app.handler)
	defer server.Close()
	for _, check := range []struct {
		method, path string
		status       int
	}{
		{"GET", "/healthz", 200}, {"HEAD", "/healthz", 200}, {"POST", "/healthz", 405},
		{"GET", "/ws", 426}, {"HEAD", "/ws", 405}, {"POST", "/ws", 405},
		{"GET", "/", 404}, {"GET", "/transport.js", 404}, {"GET", "/unity/", 404},
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
