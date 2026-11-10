// A separate toy application: no Rumblebound imports, assets or game DTOs.
package main

import (
	"context"
	"encoding/json"
	"github.com/coder/websocket"
	"github.com/gin-gonic/gin"
	"github.com/win30221/gamenetkit/server/loop"
	"github.com/win30221/gamenetkit/server/transport"
	"log/slog"
	"net/http"
	"os"
	"time"
)

type input struct {
	Type string  `json:"type"`
	X    float64 `json:"x"`
}
type snapshot struct {
	Type string  `json:"type"`
	Tick int64   `json:"tick"`
	X    float64 `json:"x"`
}

func newHandler() http.Handler {
	router := gin.New()
	router.Use(gin.Recovery())
	router.HandleMethodNotAllowed = true
	_ = router.SetTrustedProxies(nil)
	health := func(c *gin.Context) { c.Data(http.StatusOK, "text/plain; charset=utf-8", []byte("ok")) }
	router.GET("/healthz", health)
	router.HEAD("/healthz", func(c *gin.Context) { c.Header("Content-Type", "text/plain; charset=utf-8"); c.Status(http.StatusOK) })
	runtime := func(c *gin.Context) {
		http.ServeFile(c.Writer, c.Request, "../../packages/unity/transport/Browser/transport.js")
	}
	router.GET("/transport.js", runtime)
	router.HEAD("/transport.js", runtime)
	router.Static("/unity", "../minimal-unity/Builds/WebGL")
	router.Match([]string{http.MethodGet, http.MethodHead}, "/ws", func(c *gin.Context) {
		w, r := c.Writer, c.Request
		conn, err := websocket.Accept(w, r, nil)
		if err != nil {
			return
		}
		conn.SetReadLimit(1024)
		wire := &transport.WebSocket{Conn: conn}
		defer wire.Close()
		ctx, cancel := context.WithCancel(r.Context())
		defer cancel()
		helloCtx, stop := context.WithTimeout(ctx, 3*time.Second)
		_, err = wire.ReadHello(helloCtx)
		stop()
		if err != nil {
			return
		}
		commands := make(chan input, 32)
		updates := make(chan []byte, 4)
		done := make(chan struct{})
		go func() {
			defer close(done)
			var tick int64
			var x, drive float64
			_ = loop.Run(ctx, loop.Options[input]{Interval: time.Second / 30, CommandBudget: 32, Commands: commands,
				Apply: func(i input) {
					if i.X >= -1 && i.X <= 1 {
						drive = i.X
					}
				}, Step: func() {
					tick++
					x += drive / 30
					if tick%2 == 0 {
						b, _ := json.Marshal(snapshot{Type: "snapshot", Tick: tick, X: x})
						select {
						case updates <- b:
						default:
						}
					}
				}})
		}()
		go func() {
			defer cancel()
			for {
				select {
				case <-ctx.Done():
					return
				case b := <-updates:
					if wire.Write(ctx, b, false) != nil {
						return
					}
				}
			}
		}()
		for {
			b, err := wire.ReadInput(ctx)
			if err != nil {
				break
			}
			var i input
			if json.Unmarshal(b, &i) != nil {
				break
			}
			select {
			case commands <- i:
			case <-ctx.Done():
				break
			}
		}
		cancel()
		<-done
	})
	return router
}

func main() {
	slog.Info("Minimal framework example", "url", "http://127.0.0.1:18091")
	if err := http.ListenAndServe("127.0.0.1:18091", newHandler()); err != nil {
		slog.Error("HTTP server failed", "error", err)
		os.Exit(1)
	}
}
