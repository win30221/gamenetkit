// Package battle is the remake's composition root for the shared game service.
package battle

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/identity"
	"net/http"
	"sync"
	"time"

	gateway "github.com/win30221/gamenetkit/examples/rumblebound/game/service"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

type Options struct {
	Origins         []string
	DevelopmentKey  string
	ReplayDirectory string
	GoogleClientID  string
}

type Application struct {
	Handler http.Handler
	API     *gateway.Server
	cancel  context.CancelFunc
	once    sync.Once
}

func New(parent context.Context, options Options) (*Application, error) {
	key := options.DevelopmentKey
	if key == "" {
		var bytes [32]byte
		if _, err := rand.Read(bytes[:]); err != nil {
			return nil, err
		}
		key = hex.EncodeToString(bytes[:])
	}
	auth, err := gateway.NewAuth(key)
	if err != nil {
		return nil, err
	}
	ctx, cancel := context.WithCancel(parent)
	ctx, err = sim.WithReplayDirectory(ctx, options.ReplayDirectory)
	if err != nil {
		cancel()
		return nil, err
	}
	engine := sim.NewEngine(ctx)
	if status := engine.ReplayStatus(); status.Error != "" {
		cancel()
		<-engine.Done
		return nil, &startupError{status.Error}
	}
	api := gateway.New(auth, engine, options.Origins)
	api.Map = shared.LargeArenaMap()
	api.MatchContext = ctx
	api.GuestLogin = true
	if options.GoogleClientID != "" {
		api.Google, err = identity.NewGoogle(options.GoogleClientID)
		if err != nil {
			cancel()
			<-engine.Done
			return nil, err
		}
	}
	router := gateway.NewRouter()
	api.Register(router)
	return &Application{Handler: router, API: api, cancel: cancel}, nil
}

type startupError struct{ message string }

func (e *startupError) Error() string { return e.message }
func (a *Application) Close() {
	a.once.Do(func() {
		a.cancel()
		ctx, stop := context.WithTimeout(context.Background(), 5*time.Second)
		defer stop()
		_ = a.API.ShutdownMatches(ctx)
		select {
		case <-a.API.Engine.Done:
		case <-ctx.Done():
		}
	})
}
