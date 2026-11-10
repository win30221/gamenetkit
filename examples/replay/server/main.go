package main

import (
	"context"
	"errors"
	"flag"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
)

type application struct {
	handler    http.Handler
	world      *world
	mu         sync.Mutex
	closing    bool
	sessions   sync.WaitGroup
	slots      chan struct{}
	recordings *recordingStore
}

// This private service owns authority only. The C# teaching host serves the page.
func newApplication(ctx context.Context, directory string) (*application, error) {
	store, err := newRecordingStore(directory)
	if err != nil {
		return nil, err
	}
	a := &application{world: newWorld(ctx, store), slots: make(chan struct{}, 32), recordings: store}
	router := gin.New()
	router.Use(gin.Recovery())
	router.HandleMethodNotAllowed = true
	_ = router.SetTrustedProxies(nil)
	router.GET("/healthz", func(c *gin.Context) { c.Data(http.StatusOK, "text/plain; charset=utf-8", []byte("ok\n")) })
	router.HEAD("/healthz", func(c *gin.Context) { c.Header("Content-Type", "text/plain; charset=utf-8"); c.Status(http.StatusOK) })
	router.GET("/ws", gin.WrapF(a.websocket))
	router.GET("/recordings/:id", func(c *gin.Context) {
		id := c.Param("id")
		if len(id) != 32 {
			c.Status(http.StatusNotFound)
			return
		}
		for _, letter := range id {
			if !(letter >= '0' && letter <= '9' || letter >= 'a' && letter <= 'f') {
				c.Status(http.StatusNotFound)
				return
			}
		}
		data, ok := store.get(id)
		if !ok {
			c.Status(http.StatusNotFound)
			return
		}
		c.Header("Cache-Control", "no-store")
		c.Data(http.StatusOK, "application/json; charset=utf-8", data)
	})
	a.handler = router
	return a, nil
}
func (a *application) close() {
	a.mu.Lock()
	a.closing = true
	a.mu.Unlock()
	a.world.close()
	a.sessions.Wait()
	a.recordings.close()
}
func run(ctx context.Context, address, directory string) error {
	a, err := newApplication(ctx, directory)
	if err != nil {
		return err
	}
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
	slog.Info("Replay authority", "address", address, "tickRate", tickRate, "snapshotRate", snapshotRate)
	err = server.ListenAndServe()
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
func main() {
	address := flag.String("http", "127.0.0.1:18139", "private authority HTTP address")
	directory := flag.String("record-dir", filepath.Join("..", "..", "..", ".local", "replay", "run", "recordings"), "lesson journal directory")
	flag.Parse()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := run(ctx, *address, *directory); err != nil {
		slog.Error("HTTP server failed", "error", err)
		os.Exit(1)
	}
}
