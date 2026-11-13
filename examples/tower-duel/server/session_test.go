package main

import (
	"context"
	"encoding/json"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func sendTest(t *testing.T, c *websocket.Conn, msg any) {
	t.Helper()
	data, err := json.Marshal(msg)
	if err != nil {
		t.Fatal(err)
	}
	ctx, done := context.WithTimeout(context.Background(), time.Second)
	defer done()
	if err = c.Write(ctx, websocket.MessageText, data); err != nil {
		t.Fatal(err)
	}
}
func readTest(t *testing.T, c *websocket.Conn, accept func([]byte) bool) []byte {
	t.Helper()
	ctx, done := context.WithTimeout(context.Background(), 6*time.Second)
	defer done()
	for {
		_, data, err := c.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		if accept(data) {
			return data
		}
	}
}
func readStateTest(t *testing.T, c *websocket.Conn, phase string) stateMessage {
	t.Helper()
	var result stateMessage
	readTest(t, c, func(data []byte) bool {
		var state stateMessage
		_ = json.Unmarshal(data, &state)
		if (state.Type == "snapshot" || state.Type == "welcome") && (phase == "" || state.Phase == phase) {
			result = state
			return true
		}
		return false
	})
	return result
}
func TestRealWebSocketRoomAndAction(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	m := testMap(t)
	config := defaultRules()
	app := newApplication(ctx, m, config, "")
	defer app.engine.close()
	srv := httptest.NewServer(app.handler)
	defer srv.Close()
	dial := func(mode, room string) *websocket.Conn {
		c, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(srv.URL, "http")+"/ws", nil)
		if err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() { _ = c.CloseNow() })
		sendTest(t, c, helloMessage{Type: "hello", Protocol: protocolVersion, Mode: mode, Room: room, Name: "Tester", MapHash: m.Hash, RulesHash: config.Hash})
		return c
	}
	a := dial("create", "")
	first := readStateTest(t, a, "")
	b := dial("join", first.Room)
	second := readStateTest(t, b, "")
	if first.Match != second.Match || first.Seat != 0 || second.Seat != 1 {
		t.Fatal("room/seat mismatch")
	}
	third := dial("join", first.Room)
	payload := readTest(t, third, func(data []byte) bool { return strings.Contains(string(data), "error") })
	if !strings.Contains(string(payload), "room_full") {
		t.Fatal(string(payload))
	}
	for _, c := range []*websocket.Conn{a, b} {
		sendTest(t, c, map[string]string{"type": "ready", "match": first.Match})
	}
	readStateTest(t, a, "playing")
	readStateTest(t, b, "playing")
	command := deployMessage{"deploy", first.Match, 1, "guard", -5, -2.2}
	sendTest(t, a, command)
	sendTest(t, a, command)
	var firstAck actionResult
	acks := 0
	readTest(t, a, func(data []byte) bool {
		var result actionResult
		_ = json.Unmarshal(data, &result)
		if result.Type != "action" {
			return false
		}
		if acks == 0 {
			firstAck = result
		} else if result != firstAck {
			t.Fatal("retry changed action result")
		}
		acks++
		return acks == 2
	})
	if !firstAck.Accepted {
		t.Fatal(firstAck)
	}
	state := readStateTest(t, b, "playing")
	for len(state.Units) == 0 {
		state = readStateTest(t, b, "playing")
	}
	if len(state.Units) != 1 || state.Units[0].ID != firstAck.UnitID || state.Energy < 5 {
		t.Fatal("replication/private energy failed")
	}
}
func TestStrictProtocol(t *testing.T) {
	for _, data := range []string{`{"type":"ready","Type":"ready","match":"m"}`, `{"type":"ready","match":"m","match":"n"}`, `{"type":"ready","match":null}`, `{"type":"ready"}`, `{"type":"ready","match":"m"} {}`} {
		var target struct {
			Type  string `json:"type"`
			Match string `json:"match"`
		}
		if decodeStrict([]byte(data), &target, "type", "match") == nil {
			t.Fatal("accepted malformed input", data)
		}
	}
}

func TestRulesHashAndOldProtocolAreRejected(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	m := testMap(t)
	config := defaultRules()
	app := newApplication(ctx, m, config, "")
	defer app.engine.close()
	srv := httptest.NewServer(app.handler)
	defer srv.Close()
	for _, test := range []struct {
		protocol     int
		hash, reason string
	}{{protocolVersion, "different-rules", "rules_mismatch"}, {1, config.Hash, "invalid_hello"}} {
		c, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(srv.URL, "http")+"/ws", nil)
		if err != nil {
			t.Fatal(err)
		}
		sendTest(t, c, helloMessage{Type: "hello", Protocol: test.protocol, Mode: "create", Name: "Tester", MapHash: m.Hash, RulesHash: test.hash})
		payload := readTest(t, c, func(data []byte) bool { return strings.Contains(string(data), "error") })
		_ = c.CloseNow()
		if !strings.Contains(string(payload), test.reason) {
			t.Fatal("incompatible configuration was not rejected", string(payload))
		}
	}
}
