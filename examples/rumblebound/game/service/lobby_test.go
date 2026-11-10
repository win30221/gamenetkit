package gateway

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func roomRequest(t *testing.T, server *httptest.Server, method, path, body, token, origin string, want int, output any) {
	t.Helper()
	req, err := http.NewRequest(method, server.URL+path, bytes.NewBufferString(body))
	if err != nil {
		t.Fatal(err)
	}
	if body != "" {
		req.Header.Set("Content-Type", "application/json")
	}
	if token != "" {
		req.Header.Set("Authorization", "Bearer "+token)
	}
	if origin != "" {
		req.Header.Set("Origin", origin)
	}
	response, err := server.Client().Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	data, err := io.ReadAll(response.Body)
	if err != nil {
		t.Fatal(err)
	}
	if response.StatusCode != want {
		t.Fatalf("%s %s: HTTP %d, want %d: %s", method, path, response.StatusCode, want, data)
	}
	if response.Header.Get("Cache-Control") != "no-store" {
		t.Fatal("room response must not be cached")
	}
	if output != nil && json.Unmarshal(data, output) != nil {
		t.Fatalf("invalid JSON: %s", data)
	}
}

func TestRoomHTTPAuthorizationAndReadyFlow(t *testing.T) {
	s, server := setup(t)
	hostToken, hostIdentity, err := s.Auth.NewSession(testKey)
	if err != nil {
		t.Fatal(err)
	}
	guestToken, guestIdentity, err := s.Auth.NewSession(testKey)
	if err != nil {
		t.Fatal(err)
	}
	const origin = "https://allowed.test"
	var room lobby.Snapshot
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, "", origin, 401, nil)
	roomRequest(t, server, "POST", "/api/room/join", `{"code":"KNOWN"}`, "", origin, 401, nil)
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, hostToken, "https://evil.test", 403, nil)
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2,"userID":"forged"}`, hostToken, origin, 400, nil)
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, hostToken, origin, 200, &room)
	if room.HostID != hostIdentity.UserID {
		t.Fatalf("host UserID came from request: %+v", room)
	}
	roomRequest(t, server, "POST", "/api/room/join", fmt.Sprintf(`{"code":%q}`, room.Code), guestToken, origin, 200, &room)
	if len(room.Members) != 2 {
		t.Fatalf("join failed: %+v", room)
	}
	roomRequest(t, server, "POST", "/api/room/assign", fmt.Sprintf(`{"memberID":%q,"teamID":"t2"}`, hostIdentity.UserID), guestToken, origin, 403, nil)
	roomRequest(t, server, "POST", "/api/room/assign", fmt.Sprintf(`{"memberID":%q,"teamID":"t2"}`, guestIdentity.UserID), hostToken, origin, 200, &room)
	roomRequest(t, server, "POST", "/api/room/team", `{"teamID":"t1"}`, hostToken, origin, 200, &room)
	ready := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, shared.ArenaMapHash())
	roomRequest(t, server, "POST", "/api/room/ready", ready, hostToken, origin, 200, &room)
	if room.CanStart {
		t.Fatal("room started before guest ready")
	}
	roomRequest(t, server, "POST", "/api/room/ready", ready, guestToken, origin, 200, &room)
	if !room.CanStart {
		t.Fatalf("room not ready: %+v", room)
	}
	roomRequest(t, server, "GET", "/api/room/current", "", guestToken, "", 200, &room)
	if !room.CanStart {
		t.Fatal("room read differs from mutation")
	}
	var view roomView
	roomRequest(t, server, "GET", "/api/room/current", "", guestToken, "", 200, &view)
	if view.YouID != guestIdentity.UserID {
		t.Fatalf("current room revealed wrong identity: %s", view.YouID)
	}
	roomRequest(t, server, "POST", "/api/room/leave", `{"userID":"host"}`, guestToken, origin, 400, nil)
	roomRequest(t, server, "POST", "/api/room/leave", `{}`, guestToken, origin, 200, nil)
	roomRequest(t, server, "GET", "/api/room/current", "", guestToken, "", 404, nil)
	s.Auth.Revoke(hostToken)
	roomRequest(t, server, "GET", "/api/room/current", "", hostToken, "", 401, nil)
}

func TestRoomMutationRejectsCookieWithoutOrigin(t *testing.T) {
	s, server := setup(t)
	token, _, err := s.Auth.NewSession(testKey)
	if err != nil {
		t.Fatal(err)
	}
	req, err := http.NewRequest("POST", server.URL+"/api/room/create", bytes.NewBufferString(`{"teamCount":2}`))
	if err != nil {
		t.Fatal(err)
	}
	req.Header.Set("Content-Type", "application/json")
	req.AddCookie(&http.Cookie{Name: "rb_g0", Value: token})
	response, err := server.Client().Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	if response.StatusCode != 403 {
		t.Fatalf("cookie mutation without Origin: %d", response.StatusCode)
	}
}
