package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"time"
	"unicode"
	"unicode/utf8"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/server/transport"
)

// All client messages are flat objects; reject duplicate/case-variant/unknown
// keys, missing values, nulls and trailing JSON before assigning any authority.
func decodeStrict(data []byte, target any, keys ...string) error {
	d := json.NewDecoder(bytes.NewReader(data))
	token, err := d.Token()
	if err != nil || token != json.Delim('{') {
		return errors.New("object required")
	}
	allowed := make(map[string]bool)
	for _, k := range keys {
		allowed[k] = true
	}
	seen := make(map[string]bool)
	for d.More() {
		t, err := d.Token()
		k, ok := t.(string)
		if err != nil || !ok || !allowed[k] || seen[k] {
			return errors.New("invalid field")
		}
		seen[k] = true
		var value json.RawMessage
		if d.Decode(&value) != nil || bytes.Equal(bytes.TrimSpace(value), []byte("null")) {
			return errors.New("invalid value")
		}
	}
	for _, k := range keys {
		if !seen[k] {
			return errors.New("missing field")
		}
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	if decoder.Decode(new(any)) != io.EOF {
		return errors.New("trailing JSON")
	}
	return nil
}
func (a *application) websocket(w http.ResponseWriter, r *http.Request) {
	select {
	case a.slots <- struct{}{}:
		defer func() { <-a.slots }()
	default:
		http.Error(w, "server full", http.StatusServiceUnavailable)
		return
	}
	conn, err := websocket.Accept(w, r, nil)
	if err != nil {
		return
	}
	conn.SetReadLimit(1024)
	wire := &transport.WebSocket{Conn: conn}
	defer wire.Close()
	ctx, cancel := context.WithCancel(a.engine.Ctx)
	defer cancel()
	stop := context.AfterFunc(ctx, wire.Close)
	defer stop()
	write := func(data []byte) error {
		send, done := context.WithTimeout(ctx, 2*time.Second)
		defer done()
		return wire.Write(send, data, true)
	}
	reject := func(code string) {
		data, _ := json.Marshal(map[string]string{"type": "error", "code": code})
		_ = write(data)
	}
	helloCtx, done := context.WithTimeout(ctx, 4*time.Second)
	data, err := wire.ReadHello(helloCtx)
	done()
	var hello helloMessage
	if err != nil || decodeStrict(data, &hello, "type", "protocol", "mode", "room", "name", "mapHash", "rulesHash", "token") != nil || hello.Type != "hello" || hello.Protocol != protocolVersion || (hello.Mode != "create" && hello.Mode != "join" && hello.Mode != "resume") {
		reject("invalid_hello")
		return
	}
	hello.Room = strings.ToUpper(strings.TrimSpace(hello.Room))
	hello.Name = strings.TrimSpace(hello.Name)
	if hello.Name == "" {
		hello.Name = "訪客"
	}
	if !utf8.ValidString(hello.Name) || utf8.RuneCountInString(hello.Name) > 20 || strings.IndexFunc(hello.Name, unicode.IsControl) >= 0 {
		reject("invalid_name")
		return
	}
	if hello.Mode != "create" && len(hello.Room) != 6 {
		reject("invalid_room")
		return
	}
	p := &peer{Snapshots: make(chan []byte, 1), Reliable: make(chan []byte, 16), Joined: make(chan joinResult, 1), Cancel: cancel}
	if !a.engine.submit(ctx, command{Kind: "join", Peer: p, Hello: hello, Ctx: ctx}) {
		return
	}
	defer a.engine.submit(a.engine.Ctx, command{Kind: "remove", Peer: p})
	var joined joinResult
	select {
	case <-ctx.Done():
		return
	case joined = <-p.Joined:
	}
	if joined.Error != "" {
		reject(joined.Error)
		return
	}
	if write(joined.Welcome) != nil {
		return
	}
	readResult := make(chan string, 1)
	go func() {
		code := ""
		defer func() { readResult <- code }()
		window := time.Now()
		count := 0
		for {
			data, err := wire.ReadInput(ctx)
			if err != nil {
				return
			}
			if time.Since(window) >= time.Second {
				window = time.Now()
				count = 0
			}
			count++
			if count > 40 {
				code = "rate_limited"
				return
			}
			var head struct {
				Type string `json:"type"`
			}
			if json.Unmarshal(data, &head) != nil {
				code = "invalid_message"
				return
			}
			c := command{Peer: p}
			switch head.Type {
			case "ready", "rematch", "leave":
				var m struct {
					Type  string `json:"type"`
					Match string `json:"match"`
				}
				if decodeStrict(data, &m, "type", "match") != nil {
					code = "invalid_message"
					return
				}
				c.Kind = head.Type
				c.Match = m.Match
			case "deploy":
				if decodeStrict(data, &c.Deploy, "type", "match", "action", "card", "x", "z") != nil {
					code = "invalid_message"
					return
				}
				c.Kind = "deploy"
			default:
				code = "invalid_message"
				return
			}
			if !a.engine.submit(ctx, c) {
				return
			}
		}
	}()
	for {
		select {
		case <-ctx.Done():
			return
		case code := <-readResult:
			if code != "" {
				reject(code)
			}
			return
		case data := <-p.Reliable:
			if write(data) != nil {
				return
			}
		case data := <-p.Snapshots:
			if write(data) != nil {
				return
			}
		}
	}
}
