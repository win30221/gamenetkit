// A deliberately small shared world. Only World.run owns mutable player state.
package main

import (
	"context"
	"flag"
	"log"
	"math"
	"net/http"
	"os"
	"os/signal"
	"sort"
	"time"

	"github.com/coder/websocket"
	pb "github.com/win30221/gamenetkit/examples/protobuf/server/gen"
	typed "github.com/win30221/gamenetkit/server/protobuf"
	"github.com/win30221/gamenetkit/server/transport"
)

const protocolVersion = 1
const maxPlayers = 64

type client struct {
	out    chan *pb.ServerPacket
	cancel context.CancelFunc
}
type command struct {
	join  *client
	leave *client
	from  *client
	move  *pb.MoveInput
}
type World struct {
	ctx      context.Context
	commands chan command
	done     chan struct{}
}

func NewWorld(ctx context.Context) *World {
	w := &World{ctx: ctx, commands: make(chan command, 64), done: make(chan struct{})}
	go w.run(ctx)
	return w
}
func (w *World) submit(ctx context.Context, cmd command) bool {
	select {
	case w.commands <- cmd:
		return true
	case <-ctx.Done():
		return false
	case <-w.done:
		return false
	}
}
func errorPacket(code string) *pb.ServerPacket {
	return &pb.ServerPacket{Body: &pb.ServerPacket_Error{Error: &pb.ProtocolError{Code: code}}}
}
func send(c *client, packet *pb.ServerPacket) bool {
	select {
	case c.out <- packet:
		return true
	default:
		c.cancel()
		return false
	}
}
func validMove(m *pb.MoveInput) bool {
	return m != nil && m.Sequence > 0 && !math.IsNaN(float64(m.X)) && !math.IsNaN(float64(m.Z)) &&
		!math.IsInf(float64(m.X), 0) && !math.IsInf(float64(m.Z), 0) && math.Abs(float64(m.X)) <= 1 && math.Abs(float64(m.Z)) <= 1
}
func (w *World) run(ctx context.Context) {
	defer close(w.done)
	ticker := time.NewTicker(50 * time.Millisecond)
	defer ticker.Stop()
	players := make(map[*client]*pb.Player)
	var nextID, tick uint64
	for {
		select {
		case <-ctx.Done():
			for c := range players {
				c.cancel()
			}
			return
		case cmd := <-w.commands:
			switch {
			case cmd.join != nil:
				if len(players) >= maxPlayers {
					cmd.join.cancel()
					continue
				}
				nextID++
				players[cmd.join] = &pb.Player{Id: nextID}
				send(cmd.join, &pb.ServerPacket{Body: &pb.ServerPacket_Welcome{Welcome: &pb.Welcome{ProtocolVersion: protocolVersion, PlayerId: nextID}}})
			case cmd.leave != nil:
				delete(players, cmd.leave)
			case cmd.move != nil:
				p := players[cmd.from]
				if p == nil {
					continue
				}
				if cmd.move.Sequence <= p.Ack {
					send(cmd.from, errorPacket("stale_sequence"))
					continue
				}
				// Each accepted command is a bounded displacement. The sequence is
				// per connection; reconnect starts with a new player and ACK.
				p.X += cmd.move.X * 0.1
				p.Z += cmd.move.Z * 0.1
				p.Ack = cmd.move.Sequence
			}
		case <-ticker.C:
			tick++
			snapshot := &pb.WorldSnapshot{Tick: tick}
			for _, p := range players {
				snapshot.Players = append(snapshot.Players, &pb.Player{Id: p.Id, X: p.X, Z: p.Z, Ack: p.Ack})
			}
			sort.Slice(snapshot.Players, func(i, j int) bool { return snapshot.Players[i].Id < snapshot.Players[j].Id })
			packet := &pb.ServerPacket{Body: &pb.ServerPacket_Snapshot{Snapshot: snapshot}}
			for c := range players {
				if !send(c, packet) {
					delete(players, c)
				}
			}
		}
	}
}

func (w *World) ServeHTTP(rw http.ResponseWriter, r *http.Request) {
	conn, err := websocket.Accept(rw, r, nil)
	if err != nil {
		return
	}
	peer, err := typed.NewPeer[*pb.ClientPacket, *pb.ServerPacket](transport.NewBinaryWebSocket(conn, 0), func() *pb.ClientPacket { return new(pb.ClientPacket) }, 0)
	if err != nil {
		_ = conn.CloseNow()
		return
	}
	defer peer.Close()
	ctx, cancel := context.WithCancel(r.Context())
	defer cancel()
	stopWorldCancel := context.AfterFunc(w.ctx, cancel)
	defer stopWorldCancel()
	helloCtx, helloCancel := context.WithTimeout(ctx, 5*time.Second)
	packet, err := peer.Read(helloCtx)
	helloCancel()
	if err != nil {
		return
	}
	hello := packet.GetHello()
	if hello == nil || hello.ProtocolVersion != protocolVersion {
		writeCtx, stop := context.WithTimeout(ctx, time.Second)
		_ = peer.Write(writeCtx, errorPacket("protocol_version_or_hello_required"))
		stop()
		return
	}
	c := &client{out: make(chan *pb.ServerPacket, 64), cancel: cancel}
	if !w.submit(ctx, command{join: c}) {
		return
	}
	defer w.submit(context.Background(), command{leave: c})
	writerDone := make(chan struct{})
	go func() {
		defer close(writerDone)
		defer cancel()
		for {
			select {
			case <-ctx.Done():
				return
			case outgoing := <-c.out:
				writeCtx, stop := context.WithTimeout(ctx, time.Second)
				err := peer.Write(writeCtx, outgoing)
				stop()
				if err != nil {
					return
				}
			}
		}
	}()
	defer func() { cancel(); <-writerDone }()
	for {
		packet, err := peer.Read(ctx)
		if err != nil {
			return
		}
		switch body := packet.Body.(type) {
		case *pb.ClientPacket_Move:
			if !validMove(body.Move) {
				if !send(c, errorPacket("invalid_move")) {
					return
				}
				continue
			}
			if !w.submit(ctx, command{from: c, move: body.Move}) {
				return
			}
		default:
			// This includes successfully decoded future oneof fields (nil body).
			if !send(c, errorPacket("unsupported_body")) {
				return
			}
		}
	}
}

func main() {
	address := flag.String("listen", "127.0.0.1:18101", "HTTP listen address")
	webRoot := flag.String("web-root", "", "optional Unity Web build directory served at /unity/")
	transportScript := flag.String("transport-script", "../../../packages/unity/transport/Browser/transport.js", "path to the SDK browser transport script")
	flag.Parse()
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	mux := http.NewServeMux()
	mux.Handle("/ws-protobuf", NewWorld(ctx))
	mux.HandleFunc("/transport.js", func(w http.ResponseWriter, r *http.Request) { http.ServeFile(w, r, *transportScript) })
	mux.HandleFunc("/health", func(w http.ResponseWriter, _ *http.Request) { _, _ = w.Write([]byte("ok\n")) })
	if *webRoot != "" {
		mux.Handle("/unity/", http.StripPrefix("/unity/", http.FileServer(http.Dir(*webRoot))))
	}
	srv := &http.Server{Addr: *address, Handler: mux, ReadHeaderTimeout: 5 * time.Second}
	go func() {
		<-ctx.Done()
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = srv.Shutdown(shutdownCtx)
	}()
	log.Printf("protobuf shared world: http://%s/ws-protobuf", *address)
	if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		log.Fatal(err)
	}
}
