package main

import (
	"context"
	"errors"
	"flag"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
)

type application struct {
	handler  http.Handler
	world    *world
	mu       sync.Mutex
	closing  bool
	sessions sync.WaitGroup
	slots    chan struct{}
}

// This private service owns authority only. The C# teaching host serves the page.
func newApplication(ctx context.Context) *application {
	a := &application{world: newWorld(ctx), slots: make(chan struct{}, 32)}
	router := gin.New()
	router.Use(gin.Recovery())
	router.HandleMethodNotAllowed = true
	_ = router.SetTrustedProxies(nil)
	router.GET("/healthz", func(c *gin.Context) { c.Data(http.StatusOK, "text/plain; charset=utf-8", []byte("ok\n")) })
	router.HEAD("/healthz", func(c *gin.Context) { c.Header("Content-Type", "text/plain; charset=utf-8"); c.Status(http.StatusOK) })
	router.GET("/ws", gin.WrapF(a.websocket))
	a.handler = router
	return a
}
func (a *application) close() {
	a.mu.Lock()
	a.closing = true
	a.mu.Unlock()
	a.world.close()
	a.sessions.Wait()
}
func run(ctx context.Context, address string) error {
	a := newApplication(ctx)
	defer a.close()
	server := &http.Server{Addr: address, Handler: a.handler, ReadHeaderTimeout: 5 * time.Second}
	stopped := make(chan struct{})
	defer close(stopped)
	go func() {
		select {
		case <-ctx.Done():
			shutdown, cancel := context.WithTimeout(context.Background(), 5*time.Second)
			defer cancel()
			if server.Shutdown(shutdown) != nil {
				_ = server.Close()
			}
		case <-stopped:
		}
	}()
	slog.Info("Interest authority", "address", address, "tickRate", tickRate, "snapshotRate", snapshotRate)
	err := server.ListenAndServe()
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
func main() {
	address := flag.String("http", "127.0.0.1:18149", "private authority HTTP address")
	flag.Parse()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := run(ctx, *address); err != nil {
		slog.Error("HTTP server failed", "error", err)
		os.Exit(1)
	}
}
