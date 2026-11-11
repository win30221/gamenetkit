package transport

import (
	"bytes"
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
)

func TestBinaryWebSocket(t *testing.T) {
	for _, tc := range []struct {
		name string
		kind websocket.MessageType
		size int
		want error
	}{
		{"binary", websocket.MessageBinary, 32, nil},
		{"limit", websocket.MessageBinary, 64, nil},
		{"text", websocket.MessageText, 3, ErrBinaryRequired},
		{"oversize", websocket.MessageBinary, 65, ErrMessageTooLarge},
		{"huge", websocket.MessageBinary, 1024, ErrMessageTooLarge},
	} {
		t.Run(tc.name, func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
			defer cancel()
			result := make(chan error, 1)
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				conn, err := websocket.Accept(w, r, nil)
				if err != nil {
					result <- err
					return
				}
				wire := NewBinaryWebSocket(conn, 64)
				defer wire.Close()
				data, err := wire.Read(ctx)
				if err == nil {
					err = wire.Write(ctx, data)
				}
				result <- err
			}))
			defer server.Close()
			conn, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(server.URL, "http"), nil)
			if err != nil {
				t.Fatal(err)
			}
			defer conn.CloseNow()
			writer, err := conn.Writer(ctx, tc.kind)
			if err != nil {
				t.Fatal(err)
			}
			payload := bytes.Repeat([]byte{42}, tc.size)
			// Multiple writes exercise streaming reassembly and message boundary.
			_, _ = writer.Write(payload[:1])
			_, _ = writer.Write(payload[1:])
			_ = writer.Close()
			if tc.want == nil {
				kind, data, err := conn.Read(ctx)
				if err != nil || kind != websocket.MessageBinary || !bytes.Equal(data, payload) {
					t.Fatalf("echo: %v %v %v", kind, data, err)
				}
			}
			if err := <-result; !errors.Is(err, tc.want) {
				t.Fatalf("got %v, want %v", err, tc.want)
			}
		})
	}
}
