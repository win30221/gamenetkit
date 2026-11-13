package main

import (
	"context"
	"github.com/win30221/gamenetkit/server/loop"
	"net/http"
	"os"
	"testing"
	"time"
)

// Opt-in executable test fixture, excluded from production builds. Real rules run
// with zero attack damage to keep eighty living actors throughout rendering QA.
func TestRenderingLoadFixture(t *testing.T) {
	if os.Getenv("TD_RENDER_FIXTURE") != "1" {
		t.Skip("opt-in rendering fixture")
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	m, config := testMap(t), defaultRules()
	config.TowerDamage = 0
	for id, card := range config.ByID {
		card.Damage = 0
		config.ByID[id] = card
	}
	w := newMatch("render-load-80", "LOAD80", m, config)
	for seat := range 2 {
		count := 0
		for _, z := range []float64{3, 5, 7, 9, 11, 13} {
			if seat == 0 {
				z = -z
			}
			for _, x := range []float64{-8, -6, -4, -2, 0, 2, 4, 6, 8} {
				if count >= 40 || !w.walkable(point{x, z}) {
					continue
				}
				card := config.Cards[count%6]
				w.NextID++
				w.Units[w.NextID] = &unit{State: entity{ID: w.NextID, Seat: seat, Kind: card.ID, X: x, Z: z, Radius: guardRadius, HP: card.HP, MaxHP: card.HP, DZ: float64(1 - seat*2)}, LaneX: w.nearestBridge(seat, x)}
				count++
			}
		}
		if count != 40 {
			t.Fatalf("fixture has %d units on seat %d", count, seat)
		}
	}
	e := &engine{Ctx: ctx, cancel: cancel, done: make(chan struct{}), Commands: make(chan command, 256), Map: m, Rules: config, Rooms: map[string]*room{"LOAD80": {Game: w}}, Seats: map[*peer]seatRef{}, nextMatch: 1}
	go func() {
		defer close(e.done)
		_ = loop.Run(ctx, loop.Options[command]{Interval: time.Second / tickRate, CommandBudget: 128, Commands: e.Commands, Apply: e.apply, Step: func() {
			if len(e.Seats) > 0 {
				e.step()
			}
		}})
	}()
	defer e.close()
	app := &application{engine: e, slots: make(chan struct{}, 64)}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /ws", app.websocket)
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(200) })
	address := os.Getenv("TD_RENDER_ADDR")
	if address == "" {
		address = "127.0.0.1:18112"
	}
	t.Logf("Rendering QA fixture: %s room LOAD80, eighty actors, attack damage disabled", address)
	if err := http.ListenAndServe(address, mux); err != nil {
		t.Fatal(err)
	}
}
