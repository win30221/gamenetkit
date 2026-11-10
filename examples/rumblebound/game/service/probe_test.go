package gateway

import (
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func TestConnectionProbe(t *testing.T) {
	for _, scenario := range []string{"echo", "wrong-generation", "wrong-match", "gameplay", "flood", "revoked"} {
		t.Run(scenario, func(t *testing.T) {
			s, server := setup(t)
			ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
			defer cancel()
			raw, _, _ := s.Auth.NewSession(testKey)
			ticket, _ := s.Auth.Ticket(raw, false)
			ws, _, err := websocket.Dial(ctx, "wss"+strings.TrimPrefix(server.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: server.Client()})
			if err != nil {
				t.Fatal(err)
			}
			defer ws.CloseNow()
			if err := ws.Write(ctx, websocket.MessageText, hello(ticket)); err != nil {
				t.Fatal(err)
			}
			_, data, err := ws.Read(ctx)
			if err != nil {
				t.Fatal(err)
			}
			var welcome protocol.Server
			if err := json.Unmarshal(data, &welcome); err != nil || welcome.Type != "welcome" {
				t.Fatalf("welcome: %s %v", data, err)
			}
			rules := shared.Rules()
			ping := protocol.Client{Type: "ping", Protocol: rules.Protocol, Content: rules.Content, MatchID: sim.MatchID, Generation: welcome.Generation, Echo: 123.5}
			switch scenario {
			case "wrong-generation":
				ping.Generation++
			case "wrong-match":
				ping.MatchID = "other"
			case "gameplay":
				ping.X = 1
			case "revoked":
				s.Auth.Revoke(raw)
			}
			count := 1
			if scenario == "flood" {
				count = 3
			}
			for i := 0; i < count; i++ {
				b, _ := json.Marshal(ping)
				if err := ws.Write(ctx, websocket.MessageText, b); err != nil {
					t.Fatal(err)
				}
			}
			for {
				_, data, err = ws.Read(ctx)
				if err != nil {
					if scenario == "echo" || ctx.Err() != nil {
						t.Fatalf("unexpected timeout/close: %v", err)
					}
					return
				}
				var message protocol.Server
				if err := json.Unmarshal(data, &message); err != nil {
					t.Fatal(err)
				}
				if message.Type == "pong" && scenario == "echo" {
					if message.Echo != ping.Echo || message.Generation != ping.Generation || message.MatchID != ping.MatchID || message.Protocol != ping.Protocol || message.Content != ping.Content || message.Players != nil || message.Tick != 0 {
						t.Fatalf("probe changed identity or included simulated state: %+v", message)
					}
					// The ping must not become an input or contaminate snapshot echo.
					for {
						_, data, err = ws.Read(ctx)
						if err != nil {
							t.Fatal(err)
						}
						if err := json.Unmarshal(data, &message); err != nil {
							t.Fatal(err)
						}
						if message.Type == "snapshot" {
							if message.Echo != 0 || message.Players[0].Ack != 0 || message.Players[0].X != 0 {
								t.Fatal("probe reached simulation")
							}
							return
						}
					}
				}
				if message.Type == "pong" && scenario != "flood" {
					t.Fatal("invalid or revoked probe answered")
				}
			}
		})
	}
}
