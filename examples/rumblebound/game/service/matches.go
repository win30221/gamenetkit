package gateway

import (
	"context"
	"errors"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

const (
	maxActiveMatches   = 16
	maxRetainedMatches = 2 * maxActiveMatches
	matchDuration      = 10 * time.Minute // Fail-safe bound for stalled private matches and result retention.
)

type roomMatch struct {
	engine        *sim.Engine
	cancel        context.CancelFunc
	roomCode      string
	roster        map[string]string
	bots          map[string]bool
	practiceOwner string
	finished      bool
	finishedAt    time.Time
	ready         chan struct{}
}

func (s *Server) activeMatchCount() int {
	s.matchMu.Lock()
	defer s.matchMu.Unlock()
	return s.activeMatchCountLocked()
}

func (s *Server) activeMatchCountLocked() int {
	count := 0
	for _, active := range s.matches {
		if !active.finished {
			count++
		}
	}
	return count
}

// startRoom serializes transport admission with the frozen room roster. A player
// holding a retained G0 seat cannot enter a private match at the same time.
func (s *Server) startRoom(ctx context.Context, host string) (lobby.Snapshot, error) {
	ctx, stop := context.WithTimeout(ctx, 5*time.Second)
	defer stop()
	s.matchMu.Lock()
	s.refreshFinishedRoomsLocked()
	if s.activeMatchCountLocked() >= maxActiveMatches {
		s.matchMu.Unlock()
		return lobby.Snapshot{}, errors.New("match_capacity")
	}
	frozen, err := s.Lobby.Start(host)
	if err != nil {
		s.matchMu.Unlock()
		return lobby.Snapshot{}, err
	}
	roster := make(map[string]string, len(frozen.Members))
	heroes := make(map[string]string, len(frozen.Members))
	bots := make(map[string]bool)
	botUser := ""
	users := make([]string, 0, len(frozen.Members))
	for _, member := range frozen.Members {
		roster[member.UserID] = member.TeamID
		heroes[member.UserID] = member.HeroID
		if member.Bot {
			bots[member.UserID] = true
			botUser = member.UserID
		} else {
			users = append(users, member.UserID)
		}
	}
	active := &roomMatch{roomCode: frozen.Code, roster: roster, bots: bots, ready: make(chan struct{}), cancel: stop}
	if frozen.Practice {
		active.practiceOwner = host
	}
	// Reserve capacity and freeze membership before releasing the registry lock.
	// Ticket requests wait on ready; unrelated rooms and health checks stay live.
	s.matches[frozen.MatchID] = active
	s.matchMu.Unlock()
	committed := false
	defer func() {
		if !committed {
			s.matchMu.Lock()
			if s.matches[frozen.MatchID] == active {
				delete(s.matches, frozen.MatchID)
			}
			s.Lobby.End(frozen.MatchID)
			close(active.ready)
			s.matchMu.Unlock()
		}
	}()
	unlock, err := s.lockUsers(ctx, users)
	if err != nil {
		return lobby.Snapshot{}, err
	}
	defer unlock()
	for _, user := range users {
		s.matchMu.Lock()
		prior := s.claims[user]
		conflict := prior != "" && prior != sim.MatchID && s.matches[prior] != nil
		s.matchMu.Unlock()
		if conflict {
			return lobby.Snapshot{}, errors.New("already_in_match")
		}
		labSeat, err := s.Engine.InspectPlayer(ctx, user)
		if err != nil {
			return lobby.Snapshot{}, err
		}
		if labSeat.PlayerID != "" {
			return lobby.Snapshot{}, errors.New("already_in_match")
		}
		s.matchMu.Lock()
		if s.claims[user] == sim.MatchID {
			delete(s.claims, user)
		}
		s.matchMu.Unlock()
	}
	parent := s.MatchContext
	if parent == nil {
		parent = context.Background()
	}
	matchCtx, cancel := context.WithTimeout(parent, matchDuration)
	var engine *sim.Engine
	if frozen.Practice {
		engine, err = sim.NewPracticeMatchEngine(matchCtx, frozen.MatchID, roster, heroes, s.arenaMap(), host, botUser, frozen.BotMode)
	} else {
		engine, err = sim.NewMatchEngineWithMap(matchCtx, frozen.MatchID, roster, heroes, s.arenaMap())
	}
	if err != nil {
		cancel()
		return lobby.Snapshot{}, err
	}
	s.matchMu.Lock()
	if s.matches[frozen.MatchID] != active || ctx.Err() != nil {
		s.matchMu.Unlock()
		cancel()
		<-engine.Done
		return lobby.Snapshot{}, errors.New("match_not_found")
	}
	active.engine, active.cancel = engine, cancel
	committed = true
	close(active.ready)
	s.matchMu.Unlock()
	go s.watchMatch(frozen.MatchID, active)
	return frozen, nil
}

// ShutdownMatches is called after HTTP admission has stopped. Awaiting each
// engine also awaits its optional replay footer/fsync/publication.
func (s *Server) ShutdownMatches(ctx context.Context) error {
	s.matchMu.Lock()
	var done []<-chan struct{}
	for _, match := range s.matches {
		if match.engine != nil {
			match.cancel()
			done = append(done, match.engine.Done)
		}
	}
	s.matchMu.Unlock()
	for _, finished := range done {
		select {
		case <-finished:
		case <-ctx.Done():
			return ctx.Err()
		}
	}
	return nil
}

func (s *Server) watchMatch(matchID string, active *roomMatch) {
	loadTimeout := s.loadTimeout
	if loadTimeout <= 0 {
		seconds := shared.Rules().LoadTimeoutSeconds
		if seconds <= 0 {
			seconds = 45
		}
		loadTimeout = time.Duration(seconds) * time.Second
	}
	deadline := time.NewTimer(loadTimeout)
	defer deadline.Stop()
	started := false
	select {
	case <-s.Engine.Done:
		active.cancel()
		<-active.engine.Done
	case <-active.engine.Done:
	case <-active.engine.Started:
		started = true
	case <-deadline.C:
		// A simultaneous final acknowledgement wins if the engine had already
		// made the tick-0 transition before the deadline was observed here.
		select {
		case <-active.engine.Started:
			started = true
		default:
			active.cancel()
			<-active.engine.Done
		}
	}
	if started {
		if !s.Lobby.Begin(matchID) {
			s.matchMu.Lock()
			s.refreshFinishedRoomsLocked()
			finished := active.finished
			s.matchMu.Unlock()
			if !finished {
				active.cancel()
			}
		}
		finished := active.engine.Finished
	wait:
		for {
			select {
			case <-s.Engine.Done:
				active.cancel()
				<-active.engine.Done
				break wait
			case <-active.engine.Done:
				break wait
			case <-finished:
				s.matchMu.Lock()
				s.releaseFinishedRoomLocked(matchID, active)
				s.matchMu.Unlock()
				finished = nil
			}
		}
	}
	s.matchMu.Lock()
	if s.matches[matchID] == active {
		delete(s.matches, matchID)
		for user, claimed := range s.claims {
			if claimed == matchID {
				delete(s.claims, user)
			}
		}
	}
	s.matchMu.Unlock()
	s.Lobby.End(matchID)
	active.cancel()
}

// Finished results stay available for reconnect, independently of room readiness.
func (s *Server) releaseFinishedRoomLocked(matchID string, active *roomMatch) {
	if s.matches[matchID] != active || active.finished {
		return
	}
	active.finished = true
	active.finishedAt = time.Now()
	for user, claimed := range s.claims {
		if claimed == matchID {
			delete(s.claims, user)
		}
	}
	s.Lobby.End(matchID)
	s.trimRetainedMatchesLocked()
}

func (s *Server) refreshFinishedRoomsLocked() {
	for matchID, active := range s.matches {
		if active.engine == nil {
			continue
		}
		select {
		case <-active.engine.Finished:
			s.releaseFinishedRoomLocked(matchID, active)
		default:
		}
	}
	s.trimRetainedMatchesLocked()
}

// Retained results have an independent budget. Under an exceptional burst we
// retire the oldest result, never an active battle or a pending admission.
func (s *Server) trimRetainedMatchesLocked() {
	for {
		count, oldestID := 0, ""
		var oldest time.Time
		for id, active := range s.matches {
			if active.finished {
				count++
				if oldestID == "" || active.finishedAt.Before(oldest) || (active.finishedAt.Equal(oldest) && id < oldestID) {
					oldestID, oldest = id, active.finishedAt
				}
			}
		}
		if count <= maxRetainedMatches {
			return
		}
		old := s.matches[oldestID]
		delete(s.matches, oldestID)
		old.cancel()
	}
}

func (s *Server) refreshFinishedRooms() {
	s.matchMu.Lock()
	defer s.matchMu.Unlock()
	s.refreshFinishedRoomsLocked()
}

func (s *Server) roomMatchForUserLocked(user, matchID string) (*roomMatch, error) {
	active := s.matches[matchID]
	if active != nil && active.bots[user] {
		return nil, errors.New("bot_reserved")
	}
	if active == nil || active.roster[user] == "" {
		return nil, errors.New("not_in_match")
	}
	room, err := s.Lobby.Current(user)
	if err != nil || room.Code != active.roomCode {
		return nil, errors.New("not_in_match")
	}
	if room.Started {
		if room.MatchID != matchID {
			return nil, errors.New("not_in_match")
		}
	} else if !active.finished {
		return nil, errors.New("not_in_match")
	}
	return active, nil
}

func (s *Server) issueRoomTicket(user, raw string, takeover bool) (string, string, error) {
	return s.issueRequestedRoomTicket(user, raw, takeover, "")
}

func (s *Server) issueRequestedRoomTicket(user, raw string, takeover bool, requestedMatchID string) (string, string, error) {
	return s.issueRequestedRoomTicketContext(context.Background(), user, raw, takeover, requestedMatchID)
}

func (s *Server) waitMatchReady(ctx context.Context, matchID string) error {
	s.matchMu.Lock()
	active := s.matches[matchID]
	var ready <-chan struct{}
	if active != nil {
		ready = active.ready
	}
	s.matchMu.Unlock()
	if ready != nil {
		select {
		case <-ready:
		case <-ctx.Done():
			return ctx.Err()
		case <-s.Engine.Done:
			return errors.New("server_stopped")
		}
	}
	return nil
}

func (s *Server) issueRequestedRoomTicketContext(ctx context.Context, user, raw string, takeover bool, requestedMatchID string) (string, string, error) {
	matchID := requestedMatchID
	if matchID == "" {
		room, err := s.Lobby.Match(user)
		if err != nil {
			return "", "", err
		}
		matchID = room.MatchID
	}
	if err := s.waitMatchReady(ctx, matchID); err != nil {
		return "", "", err
	}
	s.matchMu.Lock()
	defer s.matchMu.Unlock()
	s.refreshFinishedRoomsLocked()
	if _, err := s.roomMatchForUserLocked(user, matchID); err != nil {
		return "", "", err
	}
	if prior := s.claims[user]; prior != "" && prior != matchID {
		return "", "", errors.New("already_in_match")
	}
	ticket, err := s.Auth.TicketForMatch(raw, matchID, takeover)
	return ticket, matchID, err
}

// Only this user's admission is locked while waiting for the owner goroutine.
// Registry locks cover validation/commit, never another match's simulation wait.
func (s *Server) attachMatch(ctx context.Context, identity session, ticket ticket) (sim.Connection, *sim.Engine, error) {
	if ticket.MatchID != sim.MatchID {
		if err := s.waitMatchReady(ctx, ticket.MatchID); err != nil {
			return sim.Connection{}, nil, err
		}
	}
	unlock, err := s.lockUsers(ctx, []string{identity.UserID})
	if err != nil {
		return sim.Connection{}, nil, err
	}
	defer unlock()
	s.matchMu.Lock()
	s.refreshFinishedRoomsLocked()
	var engine *sim.Engine
	if ticket.MatchID == sim.MatchID && ticket.Purpose == ticketPurposeLab {
		if _, err := s.Lobby.Match(identity.UserID); err == nil {
			s.matchMu.Unlock()
			return sim.Connection{}, nil, errors.New("already_in_match")
		}
		engine = s.Engine
	} else if ticket.MatchID != sim.MatchID && ticket.Purpose == ticketPurposeRoom {
		active, err := s.roomMatchForUserLocked(identity.UserID, ticket.MatchID)
		if err != nil {
			s.matchMu.Unlock()
			return sim.Connection{}, nil, err
		}
		engine = active.engine
	} else {
		s.matchMu.Unlock()
		return sim.Connection{}, nil, errors.New("invalid_ticket")
	}
	prior := s.claims[identity.UserID]
	conflict := prior != "" && prior != ticket.MatchID && prior != sim.MatchID && s.matches[prior] != nil
	s.matchMu.Unlock()
	if conflict {
		return sim.Connection{}, nil, errors.New("already_in_match")
	}
	if prior == sim.MatchID && prior != ticket.MatchID {
		seat, err := s.Engine.InspectPlayer(ctx, identity.UserID)
		if err != nil {
			return sim.Connection{}, nil, err
		}
		if seat.PlayerID != "" {
			return sim.Connection{}, nil, errors.New("already_in_match")
		}
	}
	if engine == nil {
		return sim.Connection{}, nil, errors.New("not_in_match")
	}
	connection, err := engine.Attach(ctx, identity.UserID, ticket.Takeover)
	if err != nil {
		return sim.Connection{}, nil, err
	}
	s.matchMu.Lock()
	defer s.matchMu.Unlock()
	// A practice leave or retention eviction can retire this engine during Attach.
	if ticket.MatchID != sim.MatchID && (s.matches[ticket.MatchID] == nil || s.matches[ticket.MatchID].engine != engine) {
		// Do not wait on the engine while holding the registry lock.
		go engine.Disconnect(identity.UserID, connection.Welcome.Generation)
		return sim.Connection{}, nil, errors.New("not_in_match")
	}
	if active := s.matches[ticket.MatchID]; active == nil || !active.finished {
		s.claims[identity.UserID] = ticket.MatchID
	}
	return connection, engine, nil
}
