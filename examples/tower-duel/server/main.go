package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"time"
)

type application struct {
	engine  *engine
	slots   chan struct{}
	handler http.Handler
}

func newApplication(ctx context.Context, m mapDefinition, config rules, runtimePath string, webRoots ...string) *application {
	a := &application{engine: newEngine(ctx, m, config), slots: make(chan struct{}, 64)}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		fmt.Fprintln(w, "ok")
	})
	mux.HandleFunc("GET /api/config", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]any{"protocol": protocolVersion, "mapHash": m.Hash, "rulesHash": config.Hash, "tickRate": tickRate, "snapshotRate": snapshotRate, "cards": config.Cards, "maxUnits": maxUnits})
	})
	mux.HandleFunc("GET /ws", a.websocket)
	mux.HandleFunc("GET /transport.js", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		http.ServeFile(w, r, runtimePath)
	})
	mux.HandleFunc("GET /", func(w http.ResponseWriter, r *http.Request) {
		if len(webRoots) > 0 && webRoots[0] != "" {
			if strings.HasSuffix(r.URL.Path, ".gz") {
				w.Header().Set("Content-Encoding", "gzip")
				switch {
				case strings.HasSuffix(r.URL.Path, ".wasm.gz"):
					w.Header().Set("Content-Type", "application/wasm")
				case strings.HasSuffix(r.URL.Path, ".js.gz"):
					w.Header().Set("Content-Type", "application/javascript")
				default:
					w.Header().Set("Content-Type", "application/octet-stream")
				}
			}
			w.Header().Set("Cache-Control", "no-cache")
			w.Header().Set("X-Content-Type-Options", "nosniff")
			http.FileServer(http.Dir(webRoots[0])).ServeHTTP(w, r)
			return
		}
		if r.URL.Path != "/" {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		fmt.Fprint(w, `<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>Tower Duel</title><body style="background:#132233;color:#f7ecd5;font:20px sans-serif;padding:40px"><h1>雙橋對決 · Tower Duel</h1><p>伺服器已啟動。請開啟兩個 Unity 客戶端，以房間碼加入對戰。</p><p>選擇盾兵卡，點擊己方半場部署；能量會隨時間回復。</p></body></html>`)
	})
	a.handler = mux
	return a
}
func run(ctx context.Context, address, mapPath, rulesPath, runtimePath, webRoot string) error {
	m, err := loadMap(mapPath)
	if err != nil {
		return err
	}
	config, err := loadRules(rulesPath)
	if err != nil {
		return err
	}
	runtimePath, err = filepath.Abs(runtimePath)
	if err != nil {
		return err
	}
	a := newApplication(ctx, m, config, runtimePath, webRoot)
	defer a.engine.close()
	srv := &http.Server{Addr: address, Handler: a.handler, ReadHeaderTimeout: 5 * time.Second}
	stopped := make(chan struct{})
	defer close(stopped)
	go func() {
		select {
		case <-ctx.Done():
			shutdown, cancel := context.WithTimeout(context.Background(), 3*time.Second)
			defer cancel()
			_ = srv.Shutdown(shutdown)
		case <-stopped:
		}
	}()
	log.Printf("Tower Duel: http://%s · SDK loop %d Hz / snapshots %d Hz", address, tickRate, snapshotRate)
	if err = srv.ListenAndServe(); errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
func main() {
	address := flag.String("http", "127.0.0.1:18110", "HTTP and WebSocket address")
	mapPath := flag.String("map", "../protocol/map.json", "canonical map")
	rulesPath := flag.String("rules", "../protocol/rules.json", "canonical card rules")
	runtimePath := flag.String("transport-runtime", "../../../packages/unity/transport/Browser/transport.js", "SDK browser transport")
	webRoot := flag.String("web", "", "optional built Unity WebGL directory")
	flag.Parse()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := run(ctx, *address, *mapPath, *rulesPath, *runtimePath, *webRoot); err != nil {
		log.Fatal(err)
	}
}
