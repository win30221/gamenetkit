package main

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"sync"
	"time"
)

type card struct {
	ID, Type string
	Cost     float64
}

type gameConfig struct {
	Protocol           int
	MapHash, RulesHash string
	Cards              []card
}

type companion struct {
	ctx       context.Context
	server    string
	config    gameConfig
	mu        sync.Mutex
	rooms     map[string]*bot
	creating  int
	webOrigin string
	owners    map[string]string
}

func newCompanion(ctx context.Context, server string) (*companion, error) {
	endpoint, err := url.Parse(server)
	if err != nil || endpoint.Scheme != "http" || endpoint.Hostname() != "127.0.0.1" || endpoint.Port() == "" {
		return nil, errors.New("solo companion requires an explicit http://127.0.0.1:port backend")
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, server+"/api/config", nil)
	if err != nil {
		return nil, err
	}
	response, err := (&http.Client{Timeout: 5 * time.Second}).Do(request)
	if err != nil {
		return nil, err
	}
	defer response.Body.Close()
	var config gameConfig
	if response.StatusCode != http.StatusOK || json.NewDecoder(io.LimitReader(response.Body, 64*1024)).Decode(&config) != nil || config.Protocol != 3 || config.MapHash == "" || config.RulesHash == "" || len(config.Cards) != 8 {
		return nil, errors.New("backend config is incompatible")
	}
	return &companion{ctx: ctx, server: server, config: config, rooms: make(map[string]*bot)}, nil
}

func reply(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}

func (c *companion) handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /solo/config", func(w http.ResponseWriter, r *http.Request) {
		reply(w, http.StatusOK, map[string]any{"plugin": "tower-duel-solo", "server": c.clientServer(), "modes": []string{"practice", "sandbox"}})
	})
	mux.HandleFunc("POST /solo/rooms", func(w http.ResponseWriter, r *http.Request) {
		var request struct {
			Mode string `json:"mode"`
		}
		decoder := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1024))
		decoder.DisallowUnknownFields()
		if decoder.Decode(&request) != nil || decoder.Decode(new(any)) != io.EOF || (request.Mode != "practice" && request.Mode != "sandbox") {
			reply(w, http.StatusBadRequest, map[string]string{"error": "invalid_mode"})
			return
		}
		owner, err := c.owner(w, r, true)
		if err != nil {
			http.Error(w, "session unavailable", http.StatusInternalServerError)
			return
		}
		c.mu.Lock()
		if len(c.rooms)+c.creating >= 4 {
			c.mu.Unlock()
			reply(w, http.StatusTooManyRequests, map[string]string{"error": "solo_room_limit"})
			return
		}
		c.creating++
		c.mu.Unlock()
		b, err := connectBot(c.ctx, c.server, c.config, request.Mode)
		c.mu.Lock()
		c.creating--
		if err == nil {
			c.rooms[b.room] = b
			if c.webOrigin != "" {
				c.owners[b.room] = owner
			}
		}
		c.mu.Unlock()
		if err != nil {
			log.Printf("create solo room: %v", err)
			reply(w, http.StatusBadGateway, map[string]string{"error": "backend_unavailable"})
			return
		}
		go func() {
			b.run(c.ctx)
			c.mu.Lock()
			delete(c.rooms, b.room)
			delete(c.owners, b.room)
			c.mu.Unlock()
		}()
		reply(w, http.StatusCreated, map[string]string{"room": b.room, "mode": b.mode, "server": c.clientServer()})
	})
	mux.HandleFunc("GET /solo/rooms/{room}", func(w http.ResponseWriter, r *http.Request) {
		if !c.owns(r, r.PathValue("room")) {
			http.NotFound(w, r)
			return
		}
		c.mu.Lock()
		b := c.rooms[r.PathValue("room")]
		c.mu.Unlock()
		if b == nil {
			http.NotFound(w, r)
			return
		}
		reply(w, http.StatusOK, b.status())
	})
	mux.HandleFunc("DELETE /solo/rooms/{room}", func(w http.ResponseWriter, r *http.Request) {
		owner, _ := c.owner(nil, r, false)
		c.mu.Lock()
		b := c.rooms[r.PathValue("room")]
		expected := c.owners[r.PathValue("room")]
		c.mu.Unlock()
		if b != nil && c.webOrigin != "" && (owner == "" || expected == "" || subtle.ConstantTimeCompare([]byte(owner), []byte(expected)) != 1) {
			http.NotFound(w, r)
			return
		}
		if b != nil {
			b.stop()
		}
		w.WriteHeader(http.StatusNoContent)
	})
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if !c.allowOrigin(r) {
			http.Error(w, "origin forbidden", http.StatusForbidden)
			return
		}
		mux.ServeHTTP(w, r)
	})
}

func (c *companion) close() {
	c.mu.Lock()
	bots := make([]*bot, 0, len(c.rooms))
	for _, b := range c.rooms {
		bots = append(bots, b)
	}
	c.mu.Unlock()
	for _, b := range bots {
		b.stop()
	}
}

func serve(ctx context.Context, server, readyFile, address, webRoot, origin string) error {
	c, err := newCompanion(ctx, server)
	if err != nil {
		return err
	}
	defer c.close()
	handler := c.handler()
	if webRoot != "" {
		handler, err = c.webHandler(webRoot, origin)
		if err != nil {
			return err
		}
	} else if origin != "" {
		return errors.New("-origin requires -web")
	}
	listener, err := net.Listen("tcp", address)
	if err != nil {
		return err
	}
	defer listener.Close()
	if readyFile != "" {
		if err = os.WriteFile(readyFile, []byte("http://"+listener.Addr().String()), 0600); err != nil {
			return err
		}
		defer os.Remove(readyFile)
	}
	srv := &http.Server{Handler: handler, ReadHeaderTimeout: 3 * time.Second}
	stop := context.AfterFunc(ctx, func() {
		shutdown, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = srv.Shutdown(shutdown)
	})
	defer stop()
	log.Printf("optional solo companion: http://%s -> %s", listener.Addr(), server)
	if err := srv.Serve(listener); !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}

func main() {
	server := flag.String("server", "", "existing authoritative backend at http://127.0.0.1:port")
	ready := flag.String("ready-file", "", "file receiving this companion's local API URL")
	address := flag.String("http", "127.0.0.1:0", "companion listen address")
	webRoot := flag.String("web", "", "optional standalone solo WebGL directory")
	origin := flag.String("origin", "", "exact browser origin for the standalone web site")
	flag.Parse()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if err := serve(ctx, *server, *ready, *address, *webRoot, *origin); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
