// Development-only WebSocket message impairment proxy. This does not emulate IP packet loss.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"github.com/coder/websocket"
	"log"
	"math/rand/v2"
	"net/http"
	"net/http/httputil"
	"net/url"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type packet struct {
	data []byte
	due  time.Time
}

func main() {
	listen := flag.String("http", "127.0.0.1:18111", "local test listener")
	upstream := flag.String("upstream", "http://127.0.0.1:18110", "upstream game server")
	latency := flag.Int("delay", 120, "one-way message delay in milliseconds")
	jitter := flag.Int("jitter", 80, "one-way jitter in milliseconds")
	flag.Parse()
	target, err := url.Parse(*upstream)
	if err != nil {
		log.Fatal(err)
	}
	reverse := httputil.NewSingleHostReverseProxy(target)
	var dropped, duplicated, connections atomic.Uint64
	http.HandleFunc("GET /__qa/stats", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]uint64{"dropped": dropped.Load(), "duplicated": duplicated.Load(), "connections": connections.Load()})
	})
	http.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/ws" {
			reverse.ServeHTTP(w, r)
			return
		}
		client, err := websocket.Accept(w, r, nil)
		if err != nil {
			return
		}
		defer client.CloseNow()
		client.SetReadLimit(32 * 1024)
		ctx, cancel := context.WithCancel(r.Context())
		defer cancel()
		server, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(*upstream, "http")+"/ws", nil)
		if err != nil {
			return
		}
		defer server.CloseNow()
		server.SetReadLimit(32 * 1024)
		connection := connections.Add(1)
		log.Printf("connection %d: delay=%dms jitter=+-%dms", connection, *latency, *jitter)
		var group sync.WaitGroup
		forward := func(from, to *websocket.Conn, input bool) {
			defer group.Done()
			defer cancel()
			reliable := make(chan packet, 32)
			snapshots := make(chan packet, 1)
			readerDone := make(chan struct{})
			go func() {
				defer close(readerDone)
				var deployments, acks int
				for {
					_, data, err := from.Read(ctx)
					if err != nil {
						return
					}
					var head struct {
						Type string `json:"type"`
					}
					if json.Unmarshal(data, &head) != nil {
						return
					}
					copies := 1
					if input && head.Type == "deploy" {
						deployments++
						if deployments%3 == 0 {
							dropped.Add(1)
							continue
						}
						if deployments%4 == 0 {
							copies = 2
							duplicated.Add(1)
						}
					}
					if !input && head.Type == "action" {
						acks++
						if acks%5 == 0 {
							dropped.Add(1)
							continue
						}
					}
					delay := *latency
					if *jitter > 0 {
						delay += rand.IntN(*jitter*2+1) - *jitter
					}
					if delay < 0 {
						delay = 0
					}
					p := packet{data: data, due: time.Now().Add(time.Duration(delay) * time.Millisecond)}
					for i := 0; i < copies; i++ {
						if !input && head.Type == "snapshot" {
							select {
							case snapshots <- p:
							default:
								select {
								case <-snapshots:
								default:
								}
								select {
								case snapshots <- p:
								default:
								}
							}
						} else {
							select {
							case reliable <- p:
							case <-ctx.Done():
								return
							}
						}
					}
				}
			}()
			for {
				var p packet
				select {
				case <-ctx.Done():
					return
				case <-readerDone:
					return
				case p = <-reliable:
				case p = <-snapshots:
				}
				timer := time.NewTimer(time.Until(p.due))
				select {
				case <-timer.C:
				case <-ctx.Done():
					timer.Stop()
					return
				}
				writeCtx, done := context.WithTimeout(ctx, 2*time.Second)
				err := to.Write(writeCtx, websocket.MessageText, p.data)
				done()
				if err != nil {
					return
				}
			}
		}
		group.Add(2)
		go forward(client, server, true)
		go forward(server, client, false)
		group.Wait()
		log.Printf("connection %d closed: dropped messages=%d duplicate deployments=%d", connection, dropped.Load(), duplicated.Load())
	})
	fmt.Printf("Weak-network message proxy at http://%s -> %s\n", *listen, *upstream)
	log.Fatal(http.ListenAndServe(*listen, nil))
}
