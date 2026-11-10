package gateway

import (
	"context"
	"errors"
	"net/http"

	"github.com/gin-gonic/gin"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func (s *Server) registerPractice(mux gin.IRouter) {
	mux.POST("/api/practice/start", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			HeroID    string `json:"heroID"`
			BotHeroID string `json:"botHeroID"`
			Mode      string `json:"mode"`
			Protocol  int    `json:"protocol"`
			Content   string `json:"content"`
			MapHash   string `json:"mapHash"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) {
			rules := shared.Rules()
			if req.Protocol != rules.Protocol || req.Content != rules.Content {
				return nil, errors.New("version_mismatch")
			}
			if req.MapHash != s.mapHash() {
				return nil, errors.New("map_mismatch")
			}
			return s.startPractice(r.Context(), user, req.HeroID, req.BotHeroID, req.Mode)
		})
	}))
	mux.POST("/api/practice/mode", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Mode string `json:"mode"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.setPracticeMode(r.Context(), user, req.Mode) })
	}))
	mux.POST("/api/practice/leave", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct{}
		s.roomPost(w, r, &req, func(user string) (any, error) {
			if err := s.leavePractice(user); err != nil {
				return nil, err
			}
			return map[string]bool{"ok": true}, nil
		})
	}))
}

func (s *Server) startPractice(ctx context.Context, user, hero, botHero, mode string) (lobby.Snapshot, error) {
	created, err := s.Lobby.CreatePractice(user, hero, botHero, mode)
	if err != nil {
		return lobby.Snapshot{}, err
	}
	started, err := s.startRoom(ctx, user)
	if err != nil {
		_ = s.Lobby.RemovePractice(user, created.Code)
		return lobby.Snapshot{}, err
	}
	return started, nil
}

func (s *Server) setPracticeMode(ctx context.Context, user, mode string) (lobby.Snapshot, error) {
	if !shared.IsPracticeMode(mode) {
		return lobby.Snapshot{}, errors.New("invalid_bot_mode")
	}
	room, err := s.Lobby.Current(user)
	if err != nil {
		return lobby.Snapshot{}, err
	}
	if !room.Practice {
		return lobby.Snapshot{}, errors.New("not_practice")
	}
	if room.HostID != user {
		return lobby.Snapshot{}, errors.New("host_required")
	}
	if !room.Started {
		return lobby.Snapshot{}, errors.New("practice_inactive")
	}
	if err := s.waitMatchReady(ctx, room.MatchID); err != nil {
		return lobby.Snapshot{}, err
	}
	unlock, err := s.lockUsers(ctx, []string{user})
	if err != nil {
		return lobby.Snapshot{}, err
	}
	defer unlock()
	s.matchMu.Lock()
	s.refreshFinishedRoomsLocked()
	active, err := s.roomMatchForUserLocked(user, room.MatchID)
	if err != nil || active.practiceOwner != user || active.engine == nil {
		s.matchMu.Unlock()
		return lobby.Snapshot{}, errors.New("practice_inactive")
	}
	engine := active.engine
	s.matchMu.Unlock()
	if err := engine.SetPracticeMode(ctx, user, mode); err != nil {
		return lobby.Snapshot{}, err
	}
	return s.Lobby.SetPracticeMode(user, room.MatchID, mode)
}

func (s *Server) leavePractice(user string) error {
	s.matchMu.Lock()
	defer s.matchMu.Unlock()
	s.refreshFinishedRoomsLocked()
	room, err := s.Lobby.Current(user)
	if err != nil {
		return err
	}
	if !room.Practice {
		return errors.New("not_practice")
	}
	if room.HostID != user {
		return errors.New("host_required")
	}
	// Cancel both the current run and any retained result belonging to this
	// exact room. Admission/claims are released before the engine writer exits.
	for id, active := range s.matches {
		if active.roomCode != room.Code || active.practiceOwner != user {
			continue
		}
		delete(s.matches, id)
		for owner, claimed := range s.claims {
			if claimed == id {
				delete(s.claims, owner)
			}
		}
		s.Lobby.End(id)
		active.cancel()
	}
	if room.MatchID != "" {
		s.Lobby.End(room.MatchID)
	}
	return s.Lobby.RemovePractice(user, room.Code)
}
