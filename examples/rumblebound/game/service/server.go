package gateway

import (
	"context"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/identity"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
	"github.com/win30221/gamenetkit/server/netcore"
	rttransport "github.com/win30221/gamenetkit/server/transport"
	"golang.org/x/time/rate"
)

type Server struct {
	Map *shared.ArenaMapDefinition
	// Parent context carries optional recorder settings into newly created matches.
	MatchContext                                    context.Context
	Google                                          *identity.Google
	Auth                                            *Auth
	Engine                                          *sim.Engine
	Lobby                                           *lobby.Manager
	Origins                                         map[string]bool
	CertificateHash                                 string
	WTURL                                           string
	GuestLogin                                      bool
	guestLogins                                     guestLimiter
	Slots                                           chan struct{}
	handshakes                                      chan struct{}
	admissionMu                                     sync.Mutex
	admissions                                      map[string]*userAdmission
	BytesIn, BytesOut, Rejected, OversizedDatagrams atomic.Uint64
	matchMu                                         sync.Mutex
	matches                                         map[string]*roomMatch
	claims                                          map[string]string
	loadTimeout                                     time.Duration
}

func New(auth *Auth, e *sim.Engine, origins []string) *Server {
	s := &Server{Auth: auth, Engine: e, Lobby: lobby.New(), Origins: map[string]bool{}, Slots: make(chan struct{}, connectionCapacity()), handshakes: make(chan struct{}, maxPendingHandshakes), admissions: map[string]*userAdmission{}, matches: map[string]*roomMatch{}, claims: map[string]string{}}
	for _, o := range origins {
		s.Origins[o] = true
	}
	if e != nil {
		go s.maintainLobby()
	}
	return s
}
func (s *Server) Origin(r *http.Request) bool {
	return r.Header.Get("Origin") == "" || s.Origins[r.Header.Get("Origin")]
}
func reply(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
func fail(w http.ResponseWriter, status int, code string) {
	reply(w, status, map[string]string{"error": code})
}
func readJSON(w http.ResponseWriter, r *http.Request, v any) error {
	return readJSONLimit(w, r, v, protocol.MaxMessage)
}
func readJSONLimit(w http.ResponseWriter, r *http.Request, v any, limit int64) error {
	d := json.NewDecoder(http.MaxBytesReader(w, r.Body, limit))
	d.DisallowUnknownFields()
	if err := d.Decode(v); err != nil {
		return err
	}
	if d.Decode(new(any)) != io.EOF {
		return errors.New("trailing_data")
	}
	return nil
}
func bearer(r *http.Request) string {
	return strings.TrimPrefix(r.Header.Get("Authorization"), "Bearer ")
}
func sessionToken(r *http.Request) string {
	if strings.HasPrefix(r.Header.Get("Authorization"), "Bearer ") {
		return bearer(r)
	}
	c, err := r.Cookie("rb_g0")
	if err != nil {
		return ""
	}
	return c.Value
}
func (s *Server) Register(mux gin.IRouter) {
	s.registerGoogle(mux)
	s.registerLobby(mux)
	s.registerGuest(mux)
	mux.Match([]string{http.MethodGet, http.MethodHead}, "/api/config", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		clientID := ""
		if s.Google != nil {
			clientID = s.Google.ClientID
		}
		reply(w, 200, map[string]any{"stage": "G0 development lab", "rules": s.rules(), "matchID": sim.MatchID, "mapJSON": s.mapJSON(), "mapHash": s.mapHash(), "webTransportURL": s.WTURL, "certificateHash": s.CertificateHash, "googleClientID": clientID, "guestLogin": s.GuestLogin, "connectionProbe": true, "netFeatures": sim.NetFeatures()})
	}))
	mux.Match([]string{http.MethodGet, http.MethodHead}, "/healthz", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		reply(w, 200, s.health())
	}))
	mux.POST("/api/session", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !s.Origin(r) {
			fail(w, 403, "origin_rejected")
			return
		}
		raw, identity, err := s.Auth.NewSession(bearer(r))
		if err != nil {
			fail(w, 401, err.Error())
			return
		}
		http.SetCookie(w, &http.Cookie{Name: "rb_g0", Value: raw, Path: "/", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: 3600})
		reply(w, 200, map[string]any{"session": raw, "userID": identity.UserID, "expires": identity.Expires, "testIdentity": true})
	}))
	mux.POST("/api/ticket", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !s.Origin(r) || (r.Header.Get("Origin") == "" && !strings.HasPrefix(r.Header.Get("Authorization"), "Bearer ")) {
			fail(w, 403, "origin_required")
			return
		}
		var req struct {
			Takeover bool `json:"takeover"`
		}
		if readJSON(w, r, &req) != nil {
			fail(w, 400, "invalid_request")
			return
		}
		credential := sessionToken(r)
		identity, valid := s.Auth.Valid(sha256.Sum256([]byte(credential)))
		if !valid {
			fail(w, 401, "session_expired")
			return
		}
		if _, err := s.Lobby.Match(identity.UserID); err == nil {
			fail(w, 409, "already_in_match")
			return
		}
		raw, err := s.Auth.Ticket(credential, req.Takeover)
		if err != nil {
			fail(w, 401, err.Error())
			return
		}
		reply(w, 200, map[string]string{"ticket": raw, "matchID": sim.MatchID})
	}))
	mux.POST("/api/logout", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !s.Origin(r) || (r.Header.Get("Origin") == "" && !strings.HasPrefix(r.Header.Get("Authorization"), "Bearer ")) {
			fail(w, 403, "origin_required")
			return
		}
		s.Auth.Revoke(sessionToken(r))
		s.pruneLobby()
		http.SetCookie(w, &http.Cookie{Name: "rb_g0", Path: "/", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: -1})
		reply(w, 200, map[string]bool{"ok": true})
	}))
	mux.Match([]string{http.MethodGet, http.MethodHead}, "/ws", gin.WrapF(s.WebSocket))
}

type wire = rttransport.Wire

func (s *Server) serve(parent context.Context, wire wire) {
	s.serveAdmitted(parent, wire, nil)
}

func (s *Server) serveAdmitted(parent context.Context, wire wire, authenticated func()) {
	defer wire.Close()
	ctx, cancel := context.WithCancel(parent)
	defer cancel()
	errorReply := func(code string) {
		s.Rejected.Add(1)
		b, _ := json.Marshal(map[string]string{"type": "error", "code": code})
		c, stop := context.WithTimeout(ctx, 2*time.Second)
		defer stop()
		_ = wire.Reject(c, b)
	}
	deadline, stop := context.WithTimeout(ctx, 5*time.Second)
	defer stop()
	b, err := wire.ReadHello(deadline)
	if err != nil {
		return
	}
	hello, err := protocol.Decode(b)
	if err != nil {
		errorReply(err.Error())
		return
	}
	c := shared.Rules()
	if hello.Type != "hello" || hello.Protocol != c.Protocol || hello.Content != c.Content {
		errorReply("version_or_match_mismatch")
		return
	}
	if err := sim.ValidateNetOptions(hello.Net); err != nil {
		errorReply(err.Error())
		return
	}
	identity, t, err := s.Auth.Consume(hello.Ticket)
	if err != nil {
		errorReply(err.Error())
		return
	}
	if hello.MatchID != t.MatchID {
		errorReply("version_or_match_mismatch")
		return
	}
	// The handshake deadline also bounds waiting for this user's admission or a
	// room reservation; a peer must not occupy a pending slot indefinitely.
	connection, engine, err := s.attachMatch(deadline, identity, t)
	stop()
	if err != nil {
		errorReply(err.Error())
		return
	}
	if hello.Net != nil {
		configureContext, configureCancel := context.WithTimeout(ctx, 2*time.Second)
		configured, configureErr := engine.ConfigureNet(configureContext, identity.UserID, connection.Welcome.Generation, hello.Net)
		configureCancel()
		if configureErr != nil {
			engine.Disconnect(identity.UserID, connection.Welcome.Generation)
			errorReply(configureErr.Error())
			return
		}
		connection.Welcome = configured
	}
	if authenticated != nil {
		authenticated()
	}
	var writerDone chan struct{}
	defer func() {
		// Seat ownership must end before waiting for transport cleanup, which can
		// itself be delayed by congestion or an unresponsive peer.
		engine.Disconnect(identity.UserID, connection.Welcome.Generation)
		cancel()
		wire.Close()
		if writerDone != nil {
			<-writerDone
		}
	}()
	b, err = boundedSnapshot(connection.Welcome)
	if err != nil {
		return
	}
	writeCtx, writeStop := context.WithTimeout(ctx, 2*time.Second)
	err = wire.Write(writeCtx, b, true)
	writeStop()
	if err != nil {
		return
	}
	s.BytesOut.Add(uint64(len(b)))
	// Control replies bypass the simulation queue, but share its single writer.
	// A slow client can retain at most one pending probe reply.
	probes := make(chan protocol.Server, 1)
	var encoder netcore.FrameEncoder
	if hello.Net != nil && hello.Net.Codec == "delta" {
		encoder = sim.NewFrameEncoder()
	}
	var frameAck atomic.Uint64
	var latestInputSequence uint64
	for _, p := range connection.Welcome.Players {
		if p.PlayerID == connection.Welcome.PlayerID {
			latestInputSequence = p.Ack
		}
	}
	writerDone = make(chan struct{})
	go func() {
		defer close(writerDone)
		defer cancel()
		defer wire.Close()
		for {
			var message protocol.Server
			select {
			case <-ctx.Done():
				return
			case message = <-probes:
			case update, ok := <-connection.Updates:
				if !ok {
					return
				}
				message = update
			}
			if _, ok := s.Auth.Valid(t.SessionHash); !ok {
				return
			}
			if err := s.writeNetUpdate(ctx, wire, message, encoder, frameAck.Load()); err != nil {
				return
			}
		}
	}()
	window := time.Now()
	count := 0
	probeLimit := rate.NewLimiter(rate.Every(500*time.Millisecond), 2)
	for {
		readCtx, done := context.WithTimeout(ctx, 5*time.Second)
		data, err := wire.ReadInput(readCtx)
		done()
		if err != nil {
			return
		}
		s.BytesIn.Add(uint64(len(data)))
		if time.Since(window) >= time.Second {
			window = time.Now()
			count = 0
		}
		count++
		if count > 120 {
			s.Rejected.Add(1)
			errorReply("input_rate_limited")
			return
		}
		input, err := protocol.Decode(data)
		if err != nil || input.Net != nil || (input.Type != "input" && input.Type != "loaded" && input.Type != "ping") || input.Protocol != c.Protocol || input.Content != c.Content || input.MatchID != connection.Welcome.MatchID || input.Generation != connection.Welcome.Generation {
			s.Rejected.Add(1)
			errorReply("invalid_input")
			return
		}
		if _, ok := s.Auth.Valid(t.SessionHash); !ok {
			errorReply("session_expired")
			return
		}
		if input.Type == "ping" {
			allowed := probeLimit.Allow()
			if !allowed || !validProbe(input) {
				s.Rejected.Add(1)
				if !allowed {
					errorReply("probe_rate_limited")
				} else {
					errorReply("invalid_probe")
				}
				return
			}
			pong := protocol.Server{Type: "pong", Protocol: c.Protocol, Content: c.Content,
				MatchID: connection.Welcome.MatchID, MapHash: connection.Welcome.MapHash,
				Generation: connection.Welcome.Generation, Echo: input.Echo}
			if hello.Net != nil {
				pong.ClockValid = true
				pong.ClockTick = engine.CurrentTick()
			}
			select {
			case probes <- pong:
			default: // Drop a probe rather than queue behind an unresponsive client.
			}
		} else if input.Type == "loaded" {
			if connection.Welcome.MapHash != "" && input.MapHash != connection.Welcome.MapHash {
				errorReply("map_mismatch")
				return
			}
			if connection.Welcome.MatchID == sim.MatchID || input.Seq != 0 || input.X != 0 || input.Z != 0 || input.Echo != 0 || input.Action != "" || input.ActionID != 0 || input.ActionTick != 0 || input.AimX != 0 || input.AimZ != 0 || input.HasGroundTarget || input.TargetX != 0 || input.TargetZ != 0 || input.Ticket != "" {
				s.Rejected.Add(1)
				return
			}
			engine.Loaded(identity.UserID, connection.Welcome.Generation)
		} else {
			if input.Seq > latestInputSequence {
				latestInputSequence = input.Seq
				frameAck.Store(input.FrameAck)
			}
			engine.Input(identity.UserID, connection.Welcome.Generation, input)
		}
	}
}

func validProbe(c protocol.Client) bool {
	// Decode has already checked numeric bounds. Probes cannot carry gameplay.
	return c.FrameAck == 0 && c.ViewTick == 0 && c.ObservedTick == 0 && c.Net == nil && c.Echo > 0 && c.Seq == 0 && c.X == 0 && c.Z == 0 && c.Ticket == "" && c.MapHash == "" &&
		c.Action == "" && c.ActionID == 0 && c.ActionTick == 0 && c.AimX == 0 && c.AimZ == 0 &&
		!c.HasGroundTarget && c.TargetX == 0 && c.TargetZ == 0 && c.FacingX == 0 && c.FacingZ == 0
}

func (s *Server) writeUpdate(ctx context.Context, wire wire, message protocol.Server) error {
	data, err := boundedSnapshot(message)
	if err != nil {
		return err
	}
	c, done := context.WithTimeout(ctx, time.Second)
	defer done()
	// Match start and settlement must use the reliable stream on WebTransport.
	// A dropped datagram could strand a client in loading or hide its result.
	if err := wire.Write(c, data, message.Type == "result" || message.Type == "start"); err != nil {
		return err
	}
	s.BytesOut.Add(uint64(len(data)))
	return nil
}
