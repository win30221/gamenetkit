package gateway

import (
	"crypto/sha256"
	"errors"
	"mime"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
)

type roomView struct {
	lobby.Snapshot
	YouID string `json:"youID"`
}

func (s *Server) roomUser(w http.ResponseWriter, r *http.Request) (string, bool) {
	identity, ok := s.Auth.Valid(sha256.Sum256([]byte(sessionToken(r))))
	if !ok {
		fail(w, http.StatusUnauthorized, "session_expired")
		return "", false
	}
	s.pruneLobby()
	s.Lobby.Touch(identity.UserID)
	return identity.UserID, true
}

func roomError(w http.ResponseWriter, err error) {
	code := err.Error()
	status := http.StatusBadRequest
	switch code {
	case "not_in_room", "room_not_found", "member_not_found", "match_not_found":
		status = http.StatusNotFound
	case "host_required", "practice_private", "bot_reserved":
		status = http.StatusForbidden
	case "already_in_room", "room_full", "team_full", "team_over_capacity", "version_mismatch", "map_mismatch", "room_not_ready", "room_started", "match_not_started", "already_in_match", "not_in_match", "not_practice", "practice_inactive", "practice_fixed_teams":
		status = http.StatusConflict
	case "join_rate_limited":
		status = http.StatusTooManyRequests
	case "room_capacity", "match_capacity":
		status = http.StatusServiceUnavailable
	}
	fail(w, status, code)
}

func (s *Server) roomPost(w http.ResponseWriter, r *http.Request, body any, action func(string) (any, error)) {
	if !s.Origin(r) || (r.Header.Get("Origin") == "" && !strings.HasPrefix(r.Header.Get("Authorization"), "Bearer ")) {
		fail(w, http.StatusForbidden, "origin_required")
		return
	}
	mediaType, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if err != nil || mediaType != "application/json" {
		fail(w, http.StatusUnsupportedMediaType, "json_required")
		return
	}
	user, ok := s.roomUser(w, r)
	if !ok {
		return
	}
	if err := readJSONLimit(w, r, body, 4096); err != nil {
		fail(w, http.StatusBadRequest, "invalid_request")
		return
	}
	result, err := action(user)
	if err != nil {
		roomError(w, err)
		return
	}
	if room, ok := result.(lobby.Snapshot); ok {
		reply(w, http.StatusOK, roomView{Snapshot: room, YouID: user})
		return
	}
	reply(w, http.StatusOK, result)
}

func (s *Server) registerLobby(mux gin.IRouter) {
	s.registerPractice(mux)
	mux.Match([]string{http.MethodGet, http.MethodHead}, "/api/room/current", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		user, ok := s.roomUser(w, r)
		if !ok {
			return
		}
		s.refreshFinishedRooms()
		room, err := s.Lobby.Current(user)
		if err != nil {
			roomError(w, err)
			return
		}
		reply(w, http.StatusOK, roomView{Snapshot: room, YouID: user})
	}))
	mux.POST("/api/room/create", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			TeamCount int `json:"teamCount"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.Create(user, req.TeamCount) })
	}))
	mux.POST("/api/room/join", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Code string `json:"code"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.Join(user, req.Code) })
	}))
	mux.POST("/api/room/team", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			TeamID string `json:"teamID"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.ChooseTeam(user, req.TeamID) })
	}))
	mux.POST("/api/room/hero", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			HeroID string `json:"heroID"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.SetHero(user, req.HeroID) })
	}))
	mux.POST("/api/room/assign", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			MemberID string `json:"memberID"`
			TeamID   string `json:"teamID"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.AssignTeam(user, req.MemberID, req.TeamID) })
	}))
	mux.POST("/api/room/capacity", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			TeamID   string `json:"teamID"`
			Capacity int    `json:"capacity"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.Lobby.SetCapacity(user, req.TeamID, req.Capacity) })
	}))
	mux.POST("/api/room/ready", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Ready    bool   `json:"ready"`
			Protocol int    `json:"protocol"`
			Content  string `json:"content"`
			MapHash  string `json:"mapHash"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) {
			if req.Ready && req.MapHash != s.mapHash() {
				return nil, errors.New("map_mismatch")
			}
			return s.Lobby.SetReady(user, req.Ready, req.Protocol, req.Content)
		})
	}))
	mux.POST("/api/room/start", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct{}
		s.roomPost(w, r, &req, func(user string) (any, error) { return s.startRoom(r.Context(), user) })
	}))
	mux.POST("/api/room/ticket", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct {
			Takeover bool   `json:"takeover"`
			MatchID  string `json:"matchID"`
		}
		s.roomPost(w, r, &req, func(user string) (any, error) {
			ticket, matchID, err := s.issueRequestedRoomTicketContext(r.Context(), user, sessionToken(r), req.Takeover, req.MatchID)
			if err != nil {
				return nil, err
			}
			return map[string]string{"ticket": ticket, "matchID": matchID}, nil
		})
	}))
	mux.POST("/api/room/leave", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		var req struct{} // Unknown fields, including a forged UserID, are rejected.
		s.roomPost(w, r, &req, func(user string) (any, error) {
			s.refreshFinishedRooms()
			if err := s.Lobby.Leave(user); err != nil {
				return nil, err
			}
			return map[string]bool{"ok": true}, nil
		})
	}))
}
