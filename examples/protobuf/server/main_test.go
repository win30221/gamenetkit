package main

import (
	"context"
	"math"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	pb "github.com/win30221/gamenetkit/examples/protobuf/server/gen"
	typed "github.com/win30221/gamenetkit/server/protobuf"
	"github.com/win30221/gamenetkit/server/transport"
	"google.golang.org/protobuf/proto"
)

type testPeer = typed.Peer[*pb.ServerPacket, *pb.ClientPacket]

func testWorld(t *testing.T) (context.Context, string) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	w := NewWorld(ctx)
	srv := httptest.NewServer(w)
	t.Cleanup(func() { cancel(); srv.Close(); <-w.done })
	return ctx, "ws" + strings.TrimPrefix(srv.URL, "http")
}
func connect(t *testing.T, ctx context.Context, url string) (*testPeer, *websocket.Conn) {
	t.Helper()
	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		t.Fatal(err)
	}
	peer, err := typed.NewPeer[*pb.ServerPacket, *pb.ClientPacket](transport.NewBinaryWebSocket(conn, 0), func() *pb.ServerPacket { return new(pb.ServerPacket) }, 0)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(peer.Close)
	return peer, conn
}
func hello(t *testing.T, ctx context.Context, peer *testPeer) uint64 {
	t.Helper()
	if err := peer.Write(ctx, &pb.ClientPacket{Body: &pb.ClientPacket_Hello{Hello: &pb.Hello{ProtocolVersion: protocolVersion, Name: "test"}}}); err != nil {
		t.Fatal(err)
	}
	p, err := peer.Read(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if p.GetWelcome() == nil || p.GetWelcome().ProtocolVersion != protocolVersion || p.GetWelcome().PlayerId == 0 {
		t.Fatalf("welcome: %v", p)
	}
	return p.GetWelcome().PlayerId
}
func until(t *testing.T, ctx context.Context, peer *testPeer, predicate func(*pb.ServerPacket) bool) *pb.ServerPacket {
	t.Helper()
	for {
		p, err := peer.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		if predicate(p) {
			return p
		}
	}
}
func move(t *testing.T, ctx context.Context, peer *testPeer, seq uint64, x, z float32) {
	t.Helper()
	if err := peer.Write(ctx, &pb.ClientPacket{Body: &pb.ClientPacket_Move{Move: &pb.MoveInput{Sequence: seq, X: x, Z: z}}}); err != nil {
		t.Fatal(err)
	}
}

func TestSharedWorldLifecycle(t *testing.T) {
	ctx, url := testWorld(t)
	a, _ := connect(t, ctx, url)
	aID := hello(t, ctx, a)
	b, _ := connect(t, ctx, url)
	bID := hello(t, ctx, b)
	if aID == bID {
		t.Fatal("IDs reused")
	}
	until(t, ctx, a, func(p *pb.ServerPacket) bool { return p.GetSnapshot() != nil && len(p.GetSnapshot().Players) == 2 })
	move(t, ctx, a, 7, 1, -1)
	for _, peer := range []*testPeer{a, b} {
		p := until(t, ctx, peer, func(p *pb.ServerPacket) bool {
			for _, player := range p.GetSnapshot().GetPlayers() {
				if player.Id == aID && player.Ack == 7 {
					return true
				}
			}
			return false
		})
		if len(p.GetSnapshot().Players) != 2 {
			t.Fatal("snapshot must contain complete world")
		}
		for _, player := range p.GetSnapshot().Players {
			if player.Id == aID && (math.Abs(float64(player.X)-0.1) > 0.00001 || math.Abs(float64(player.Z)+0.1) > 0.00001) {
				t.Fatalf("movement: %v", player)
			}
		}
	}
	move(t, ctx, b, 1, 0, 1) // Sequence is per connection, not shared.
	until(t, ctx, b, func(p *pb.ServerPacket) bool {
		for _, player := range p.GetSnapshot().GetPlayers() {
			if player.Id == bID && player.Ack == 1 {
				return true
			}
		}
		return false
	})
	move(t, ctx, a, 7, 1, 1)
	until(t, ctx, a, func(p *pb.ServerPacket) bool { return p.GetError().GetCode() == "stale_sequence" })
	for _, value := range []float32{float32(math.NaN()), float32(math.Inf(1)), 1.01} {
		move(t, ctx, a, 8, value, 0)
		until(t, ctx, a, func(p *pb.ServerPacket) bool { return p.GetError().GetCode() == "invalid_move" })
	}
	p := until(t, ctx, a, func(p *pb.ServerPacket) bool { return p.GetSnapshot() != nil })
	for _, player := range p.GetSnapshot().Players {
		if player.Id == aID && (player.Ack != 7 || math.Abs(float64(player.X)-0.1) > 0.00001) {
			t.Fatalf("rejected move mutated world: %v", player)
		}
	}
	a.Close()
	until(t, ctx, b, func(p *pb.ServerPacket) bool {
		return p.GetSnapshot() != nil && len(p.GetSnapshot().Players) == 1 && p.GetSnapshot().Players[0].Id == bID
	})
}

func TestApplicationBodyAndVersionPolicy(t *testing.T) {
	ctx, url := testWorld(t)
	peer, conn := connect(t, ctx, url)
	hello(t, ctx, peer)
	// Field 99 is a future unknown oneof branch. Protobuf decodes it and the
	// explicit application switch rejects the absent known body.
	if err := conn.Write(ctx, websocket.MessageBinary, []byte{0x9a, 0x06, 0}); err != nil {
		t.Fatal(err)
	}
	until(t, ctx, peer, func(p *pb.ServerPacket) bool { return p.GetError().GetCode() == "unsupported_body" })
	if err := peer.Write(ctx, &pb.ClientPacket{}); err != nil {
		t.Fatal(err)
	}
	until(t, ctx, peer, func(p *pb.ServerPacket) bool { return p.GetError().GetCode() == "unsupported_body" })
	bad, _ := connect(t, ctx, url)
	if err := bad.Write(ctx, &pb.ClientPacket{Body: &pb.ClientPacket_Hello{Hello: &pb.Hello{ProtocolVersion: 999}}}); err != nil {
		t.Fatal(err)
	}
	p, err := bad.Read(ctx)
	if err != nil || p.GetError().GetCode() != "protocol_version_or_hello_required" {
		t.Fatalf("version: %v %v", p, err)
	}
}

func TestRejectInvalidBinaryConnections(t *testing.T) {
	ctx, url := testWorld(t)
	for _, tc := range []struct {
		name    string
		kind    websocket.MessageType
		payload []byte
	}{
		{"text", websocket.MessageText, []byte("hello")},
		{"invalid", websocket.MessageBinary, []byte{0x80}},
		{"oversize", websocket.MessageBinary, make([]byte, transport.DefaultBinaryLimit+1)},
	} {
		t.Run(tc.name, func(t *testing.T) {
			_, conn := connect(t, ctx, url)
			_ = conn.Write(ctx, tc.kind, tc.payload)
			if _, _, err := conn.Read(ctx); err == nil {
				t.Fatal("invalid connection accepted")
			}
		})
	}
}

func TestValidHelloAllowsUnknownFields(t *testing.T) {
	ctx, url := testWorld(t)
	peer, conn := connect(t, ctx, url)
	data, err := proto.Marshal(&pb.ClientPacket{Body: &pb.ClientPacket_Hello{Hello: &pb.Hello{ProtocolVersion: protocolVersion}}})
	if err != nil {
		t.Fatal(err)
	}
	data = append(data, 0x98, 0x06, 1)
	if err := conn.Write(ctx, websocket.MessageBinary, data); err != nil {
		t.Fatal(err)
	}
	p, err := peer.Read(ctx)
	if err != nil || p.GetWelcome() == nil {
		t.Fatalf("unknown additive field: %v %v", p, err)
	}
}
