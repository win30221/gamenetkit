package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"math"
	"net/http"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/server/transport"
)

func decodeStrict(data []byte, target any, required ...string) error {
	// Reject duplicate keys and nulls as well as missing/unknown fields. All
	// client envelopes are flat, so a second decoder remains simple and strict.
	fields := json.NewDecoder(bytes.NewReader(data))
	token, err := fields.Token()
	if err != nil || token != json.Delim('{') {
		return errors.New("object required")
	}
	seen := make(map[string]bool)
	allowed := make(map[string]bool, len(required))
	for _, key := range required {
		allowed[key] = true
	}
	for fields.More() {
		token, err = fields.Token()
		key, ok := token.(string)
		if err != nil || !ok || seen[key] || !allowed[key] {
			return errors.New("duplicate or invalid field")
		}
		seen[key] = true
		var value json.RawMessage
		if fields.Decode(&value) != nil || bytes.Equal(bytes.TrimSpace(value), []byte("null")) {
			return errors.New("null or invalid field")
		}
	}
	for _, key := range required {
		if !seen[key] {
			return errors.New("missing field")
		}
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	if err := decoder.Decode(new(any)); err != io.EOF {
		return errors.New("one JSON object required")
	}
	return nil
}
func (a *application) websocket(w http.ResponseWriter, r *http.Request) {
	a.mu.Lock()
	if a.closing {
		a.mu.Unlock()
		http.Error(w, "server closing", http.StatusServiceUnavailable)
		return
	}
	a.sessions.Add(1)
	a.mu.Unlock()
	defer a.sessions.Done()
	select {
	case a.slots <- struct{}{}:
		defer func() { <-a.slots }()
	default:
		http.Error(w, "connection capacity", http.StatusServiceUnavailable)
		return
	}
	conn, err := websocket.Accept(w, r, nil)
	if err != nil {
		return
	}
	conn.SetReadLimit(1024)
	wire := &transport.WebSocket{Conn: conn}
	defer wire.Close()
	ctx, cancel := context.WithCancel(a.world.ctx)
	defer cancel()
	stop := context.AfterFunc(ctx, wire.Close)
	defer stop()
	reject := func(code string) {
		payload, _ := json.Marshal(map[string]string{"type": "error", "code": code})
		writeCtx, done := context.WithTimeout(ctx, time.Second)
		defer done()
		_ = wire.Reject(writeCtx, payload)
	}
	helloCtx, helloCancel := context.WithTimeout(ctx, 3*time.Second)
	data, err := wire.ReadHello(helloCtx)
	helloCancel()
	var hello struct {
		Type     string `json:"type"`
		Protocol int    `json:"protocol"`
	}
	if err != nil || decodeStrict(data, &hello, "type", "protocol") != nil || hello.Type != "hello" || hello.Protocol != 1 {
		reject("invalid_hello")
		return
	}
	p := &peer{snapshots: make(chan []byte, 1), controls: make(chan []byte, 16), failed: make(chan struct{}, 1), joined: make(chan joinResult, 1)}
	// Removal is queued even if cancellation races the join reply. FIFO command
	// order ensures a canceled request cannot strand a player in the world.
	defer func() { a.world.submit(a.world.ctx, command{kind: "remove", peer: p}) }()
	if !a.world.submit(ctx, command{kind: "join", peer: p, ctx: ctx}) {
		return
	}
	var joined joinResult
	select {
	case joined = <-p.joined:
	case <-ctx.Done():
		return
	}
	if joined.err != "" {
		reject(joined.err)
		return
	}
	write := func(data []byte) error {
		writeCtx, done := context.WithTimeout(ctx, time.Second)
		defer done()
		return wire.Write(writeCtx, data, true)
	}
	if write(joined.welcome) != nil {
		return
	}
	readResult := make(chan string, 1)
	go func() {
		result := ""
		defer func() { readResult <- result }()
		for {
			data, err := wire.ReadInput(ctx)
			if err != nil {
				return
			}
			var input inputMessage
			var header struct {
				Type string `json:"type"`
			}
			if json.Unmarshal(data, &header) == nil && header.Type == "codec" {
				var selection struct {
					Type string  `json:"type"`
					Mode string  `json:"mode"`
					Step float64 `json:"step"`
				}
				if decodeStrict(data, &selection, "type", "mode", "step") != nil || (selection.Mode != "json" && selection.Mode != "delta") || !validStep(selection.Step) {
					result = "invalid_message"
					return
				}
				if !a.world.submit(ctx, command{kind: "codec", peer: p, mode: selection.Mode, step: selection.Step}) {
					return
				}
				continue
			}
			if json.Unmarshal(data, &header) == nil && header.Type == "frame-ack" {
				var acknowledgement struct {
					Type   string `json:"type"`
					Stream uint32 `json:"stream"`
					ID     uint64 `json:"id"`
				}
				if decodeStrict(data, &acknowledgement, "type", "stream", "id") != nil || acknowledgement.Stream == 0 || acknowledgement.ID > 9007199254740991 {
					result = "invalid_message"
					return
				}
				if !a.world.submit(ctx, command{kind: "frame-ack", peer: p, stream: acknowledgement.Stream, frameAck: acknowledgement.ID}) {
					return
				}
				continue
			}
			if json.Unmarshal(data, &header) == nil && header.Type == "interest" {
				var selection struct {
					Type   string  `json:"type"`
					Mode   string  `json:"mode"`
					Radius float64 `json:"radius"`
				}
				if decodeStrict(data, &selection, "type", "mode", "radius") != nil || (selection.Mode != "all" && selection.Mode != "aoi") || math.IsNaN(selection.Radius) || math.IsInf(selection.Radius, 0) || selection.Radius < 2 || selection.Radius > 6 {
					result = "invalid_message"
					return
				}
				if !a.world.submit(ctx, command{kind: "interest", peer: p, mode: selection.Mode, radius: selection.Radius}) {
					return
				}
				continue
			}
			if json.Unmarshal(data, &header) == nil && header.Type == "action" {
				var action actionMessage
				if decodeStrict(data, &action, "type", "id", "kind") != nil || action.ID == 0 || action.ID > 9007199254740991 || (action.Kind != "spawn" && action.Kind != "remove" && action.Kind != "rebirth" && action.Kind != "deny" && action.Kind != "timeout") {
					result = "invalid_message"
					return
				}
				if !a.world.submit(ctx, command{kind: "action", peer: p, action: action}) {
					return
				}
				continue
			}
			if json.Unmarshal(data, &header) == nil && header.Type == "bump" {
				if decodeStrict(data, &header, "type") != nil {
					result = "invalid_message"
					return
				}
				if !a.world.submit(ctx, command{kind: "bump", peer: p}) {
					return
				}
				continue
			}
			if decodeStrict(data, &input, "type", "seq", "x", "z") != nil || input.Type != "input" || input.Seq == 0 || input.Seq > 9007199254740991 || math.IsNaN(input.X) || math.IsNaN(input.Z) || math.IsInf(input.X, 0) || math.IsInf(input.Z, 0) || input.X < -1 || input.X > 1 || input.Z < -1 || input.Z > 1 {
				result = "invalid_message"
				return
			}
			if !a.world.submit(ctx, command{kind: "input", peer: p, input: input}) {
				return
			}
		}
	}()
	// Only this goroutine writes, including errors; cancellation closes the wire
	// and releases a blocked reader or writer during application shutdown.
	for {
		select {
		case <-ctx.Done():
			return
		case <-p.failed:
			return
		case code := <-readResult:
			if code != "" {
				reject(code)
			}
			return
		case data := <-p.snapshots:
			if write(data) != nil {
				return
			}
		case data := <-p.controls:
			if write(data) != nil {
				return
			}
		}
	}
}
