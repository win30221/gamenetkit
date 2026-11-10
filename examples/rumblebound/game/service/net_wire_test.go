//go:build !rf_no_codec

package gateway

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/netcore"
	"strings"
	"testing"
	"time"
)

func TestNegotiatedDeltaAckLossAndFullRecovery(t *testing.T) {
	s, server := setup(t)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	raw, _, _ := s.Auth.NewSession(testKey)
	ticket, _ := s.Auth.Ticket(raw, false)
	ws, _, err := websocket.Dial(ctx, "wss"+strings.TrimPrefix(server.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: server.Client()})
	if err != nil {
		t.Fatal(err)
	}
	defer ws.CloseNow()
	var h protocol.Client
	json.Unmarshal(hello(ticket), &h)
	h.Net = &protocol.NetOptions{Codec: "delta", Interest: "all", Radius: 12, Lag: "off"}
	data, _ := json.Marshal(h)
	if err = ws.Write(ctx, websocket.MessageText, data); err != nil {
		t.Fatal(err)
	}
	_, data, err = ws.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	var welcome protocol.Server
	json.Unmarshal(data, &welcome)
	if welcome.Type != "welcome" || welcome.Tech == nil || welcome.Tech.Options.Codec != "delta" {
		t.Fatalf("negotiation: %s", data)
	}
	readFrame := func() protocol.FrameEnvelope {
		t.Helper()
		for {
			_, data, err = ws.Read(ctx)
			if err != nil {
				t.Fatal(err)
			}
			var f protocol.FrameEnvelope
			json.Unmarshal(data, &f)
			if f.Type == "frame" {
				return f
			}
		}
	}
	first := readFrame()
	if first.Base != 0 {
		t.Fatal("first frame requires a baseline")
	}
	base, err := codec.Decode(netcore.EncodedFrame{ID: first.ID, Base: first.Base, Data: first.Data}, nil)
	if err != nil {
		t.Fatal(err)
	}
	r := shared.Rules()
	seq := uint64(0)
	sendAck := func(ack uint64) {
		t.Helper()
		seq++
		input := protocol.Client{Type: "input", Protocol: r.Protocol, Content: r.Content, MatchID: welcome.MatchID, Generation: welcome.Generation, Seq: seq, FrameAck: ack, X: 1}
		data, _ := json.Marshal(input)
		if err = ws.Write(ctx, websocket.MessageText, data); err != nil {
			t.Fatal(err)
		}
	}
	sendAck(first.ID)
	var second protocol.FrameEnvelope
	for i := 0; i < 10; i++ {
		second = readFrame()
		if second.Base == first.ID {
			break
		}
	}
	if second.Base != first.ID {
		t.Fatal("acknowledged baseline not used")
	}
	decoded, err := codec.Decode(netcore.EncodedFrame{ID: second.ID, Base: second.Base, Data: second.Data}, base)
	if err != nil {
		t.Fatal(err)
	}
	// A snapshot queued between Attach and ConfigureNet can precede the
	// negotiated tech metadata. Compare the delta to its own full payload,
	// rather than a different earlier snapshot whose fields may be smaller.
	if len(second.Data) >= base64.StdEncoding.EncodedLen(len(decoded)) {
		t.Fatal("delta did not compress its equivalent full snapshot")
	}
	packet, _ := json.Marshal(second)
	if len(packet) >= len(decoded) {
		t.Fatal("delta envelope did not reduce actual payload bytes", len(packet), len(decoded))
	}
	var state protocol.Server
	json.Unmarshal(decoded, &state)
	if state.Type != "snapshot" || state.Generation != welcome.Generation {
		t.Fatal("decoded identity")
	}
	// Drop second: the server must continue referencing only the acknowledged first.
	third := readFrame()
	if third.Base != first.ID {
		t.Fatal("dropped frame became a baseline")
	}
	sendAck(0)
	full := readFrame()
	for i := 0; i < 10 && full.Base != 0; i++ {
		full = readFrame()
	}
	if full.Base != 0 {
		t.Fatal("full resynchronization ignored")
	}
}
