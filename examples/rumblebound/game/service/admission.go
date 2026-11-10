package gateway

import (
	"context"
	"sort"
	"sync"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

const maxPendingHandshakes = 32

// Every admitted battle and retained result has room for its entire roster.
// Extra transient connections allow authentication and explicit takeovers even
// when all legitimate seats are connected; unauthenticated handshakes have a
// separate, much smaller budget.
func connectionCapacity() int {
	return (maxActiveMatches+maxRetainedMatches)*lobby.MaxPlayersTotal + shared.Rules().MaxPlayers + maxPendingHandshakes
}

type userAdmission struct {
	permit chan struct{}
	refs   int
}

// User locks protect ownership across asynchronous Engine calls. They are
// acquired in sorted order for a frozen roster, without holding matchMu.
func (s *Server) lockUsers(ctx context.Context, users []string) (func(), error) {
	users = append([]string(nil), users...)
	sort.Strings(users)
	var release []func()
	unlock := func() {
		for i := len(release) - 1; i >= 0; i-- {
			release[i]()
		}
	}
	for i, user := range users {
		if i > 0 && users[i-1] == user {
			continue
		}
		s.admissionMu.Lock()
		entry := s.admissions[user]
		if entry == nil {
			entry = &userAdmission{permit: make(chan struct{}, 1)}
			entry.permit <- struct{}{}
			s.admissions[user] = entry
		}
		entry.refs++
		s.admissionMu.Unlock()
		dropRef := func() {
			s.admissionMu.Lock()
			entry.refs--
			if entry.refs == 0 {
				delete(s.admissions, user)
			}
			s.admissionMu.Unlock()
		}
		select {
		case <-entry.permit:
			release = append(release, func() { entry.permit <- struct{}{}; dropRef() })
		case <-ctx.Done():
			dropRef()
			unlock()
			return nil, ctx.Err()
		case <-s.Engine.Done:
			dropRef()
			unlock()
			return nil, context.Canceled
		}
	}
	var once sync.Once
	return func() { once.Do(unlock) }, nil
}

func (s *Server) maintainLobby() {
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-s.Engine.Done:
			return
		case <-ticker.C:
			s.pruneLobby()
		}
	}
}

func (s *Server) pruneLobby() {
	s.refreshFinishedRooms()
	s.Lobby.PruneMembers(s.Auth.UserActive)
	s.matchMu.Lock()
	for user, claim := range s.claims {
		if claim == sim.MatchID && !s.Auth.UserActive(user) {
			delete(s.claims, user)
		}
	}
	s.matchMu.Unlock()
}
