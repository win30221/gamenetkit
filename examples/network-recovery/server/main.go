package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"mime"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
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

func newApplication(ctx context.Context, transportPath, webDir, unityDir string) (*application, error) {
	source, err := filepath.Abs(transportPath)
	if err != nil {
		return nil, err
	}
	info, err := os.Stat(source)
	if err != nil {
		return nil, fmt.Errorf("transport runtime: %w", err)
	}
	if !info.Mode().IsRegular() {
		return nil, fmt.Errorf("transport runtime is not a regular file: %s", source)
	}
	webDir, err = filepath.Abs(webDir)
	if err != nil {
		return nil, err
	}
	unityDir, err = filepath.Abs(unityDir)
	if err != nil {
		return nil, err
	}
	// Browser modules require JavaScript MIME. Host file associations (notably
	// Windows .mjs registrations) can otherwise override Go's defaults.
	for _, extension := range []string{".js", ".mjs"} {
		if err := mime.AddExtensionType(extension, "text/javascript; charset=utf-8"); err != nil {
			return nil, err
		}
	}
	a := &application{world: newWorld(ctx), slots: make(chan struct{}, 32)}
	router := gin.New()
	router.Use(gin.Recovery())
	router.HandleMethodNotAllowed = true
	_ = router.SetTrustedProxies(nil)
	router.GET("/healthz", func(c *gin.Context) { c.Data(http.StatusOK, "text/plain; charset=utf-8", []byte("ok\n")) })
	router.HEAD("/healthz", func(c *gin.Context) { c.Header("Content-Type", "text/plain; charset=utf-8"); c.Status(http.StatusOK) })
	runtime := func(c *gin.Context) { http.ServeFile(c.Writer, c.Request, source) }
	router.GET("/transport.js", runtime)
	router.HEAD("/transport.js", runtime)
	router.GET("/ws", gin.WrapF(a.websocket))
	// NoRoute avoids a root wildcard conflicting with API routes in Gin.
	webFiles := http.FileServer(http.Dir(webDir))
	unityFiles := http.StripPrefix("/unity/", http.FileServer(http.Dir(unityDir)))
	router.Match([]string{http.MethodGet, http.MethodHead}, "/unity", func(c *gin.Context) { c.Redirect(http.StatusMovedPermanently, "/unity/") })
	router.GET("/unity/*path", gin.WrapH(unityFiles))
	router.HEAD("/unity/*path", gin.WrapH(unityFiles))
	router.NoRoute(func(c *gin.Context) {
		if c.Request.URL.Path == "/ws" || c.Request.URL.Path == "/healthz" || c.Request.URL.Path == "/transport.js" {
			c.Status(http.StatusMethodNotAllowed)
			return
		}
		if c.Request.Method != http.MethodGet && c.Request.Method != http.MethodHead {
			c.Status(http.StatusMethodNotAllowed)
			return
		}
		c.Status(http.StatusOK)
		webFiles.ServeHTTP(c.Writer, c.Request)
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
}
func run(ctx context.Context, address, transportPath, webDir, unityDir string) error {
	a, err := newApplication(ctx, transportPath, webDir, unityDir)
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
	slog.Info("Network recovery lesson world", "address", address, "tickRate", tickRate, "snapshotRate", snapshotRate)
	err = server.ListenAndServe()
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
func main() {
	address := flag.String("http", "127.0.0.1:18119", "HTTP listen address")
	transportPath := flag.String("transport-runtime", "../../../packages/unity/transport/Browser/transport.js", "canonical SDK runtime path")
	webDir := flag.String("web-dir", "../web", "browser diagnostics directory")
	unityDir := flag.String("unity-dir", "../client/Builds/WebGL", "Unity WebGL build directory")
	flag.Parse()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := run(ctx, *address, *transportPath, *webDir, *unityDir); err != nil {
		slog.Error("HTTP server failed", "error", err)
		os.Exit(1)
	}
}
