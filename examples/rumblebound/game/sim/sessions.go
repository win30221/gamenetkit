package sim

import (
	"errors"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/server/netcore"
	"time"
)

type Seat struct {
	Net                                             *protocol.NetOptions
	Interest                                        netcore.Interest
	Life                                            uint64
	ExecutingActionID, LastFireActionID, RewindTick uint64
	RewindUsed                                      bool
	UserID                                          string
	State                                           PlayerState
	Generation                                      uint32
	Loaded                                          bool
	LastInput                                       uint64
	LeftAt                                          time.Time
	DX, DZ, Echo                                    float64
	Updates                                         chan protocol.Server
	SpawnX, SpawnZ                                  float64
	DashDX, DashDZ                                  float64
	DashEndTick                                     uint64
	ActionAck                                       uint64
	ActionCode                                      string
	// Writer-queue bookkeeping only; acknowledgement truth remains ActionAck.
	// Excluded from replay state because transport consumption is nondeterministic.
	publishedActionAck uint64
	Pending            *actionIntent
	hero               *shared.HeroConfig
}

func (w *World) Attach(user string, takeover bool) (*Seat, error) {
	if w.recording != nil {
		defer w.recordEvent("attach", replayEventPayload{User: user, Takeover: takeover})()
	}
	if w.BotUser != "" && user == w.BotUser {
		return nil, errors.New("bot_reserved")
	}
	if w.roster != nil {
		if _, allowed := w.roster[user]; !allowed {
			return nil, errors.New("not_in_match")
		}
	}
	if _, expired := w.Expired[user]; expired && w.Result == nil {
		return nil, errors.New("seat_expired")
	}
	s := w.Seats[user]
	if w.roster != nil && s == nil {
		return nil, errors.New("seat_missing")
	}
	if s != nil && !s.State.Online && !s.LeftAt.IsZero() && w.seatExpired(s) && w.Result == nil && w.Phase != phaseLoading {
		return nil, errors.New("seat_expired")
	}
	if s != nil && s.State.Online && !takeover && w.Result == nil {
		return nil, errors.New("takeover_required")
	}
	if s == nil {
		if len(w.Seats) >= w.Rules.MaxPlayers {
			return nil, errors.New("arena_full")
		}
		w.NextID++
		id := idString(w.NextID)
		s = &Seat{UserID: user, State: PlayerState{PlayerID: "p" + id, EntityID: "e" + id}}
		w.Seats[user] = s
	}
	if s.Updates != nil {
		close(s.Updates)
	}
	firstConnection := s.Generation == 0
	w.cancelBasic(s)
	s.Generation++
	if w.Phase == phaseLoading {
		s.Loaded = false
	}
	if w.Result == nil {
		s.State.Online = true
	}
	if firstConnection && w.roster != nil && w.Result == nil {
		s.State.ProtectedUntilTick = w.Tick + 2*uint64(w.Rules.TickRate)
	}
	s.DX, s.DZ = 0, 0
	s.Echo = 0
	s.LastInput = w.Tick
	s.Pending = nil
	s.publishedActionAck = s.ActionAck // Welcome already carries the retained result.
	s.Net = nil
	s.Interest = nil
	s.RewindTick = 0
	s.RewindUsed = false
	s.Updates = make(chan protocol.Server, 1)
	return s, nil
}

func (w *World) Disconnect(user string, g uint32) {
	if w.recording != nil {
		defer w.recordEvent("disconnect", replayEventPayload{User: user, Generation: g})()
	}
	if w.BotUser != "" && user == w.BotUser {
		return
	}
	s := w.Seats[user]
	if w.Result != nil {
		if s != nil && s.Generation == g && s.Updates != nil {
			close(s.Updates)
			s.Updates = nil
		}
		return
	}
	if s == nil || s.Generation != g || !s.State.Online {
		return
	}
	w.cancelBasic(s)
	s.State.Online = false
	if w.Phase == phaseLoading {
		s.Loaded = false
	}
	s.LeftAt = w.Now()
	s.DX, s.DZ = 0, 0
	s.Pending = nil
	if w.Map != nil {
		s.DashEndTick = 0
	}
	if s.Updates != nil {
		close(s.Updates)
		s.Updates = nil
	}
}

// MarkLoaded accepts only the current connection's acknowledgement. The
// private match clock stays at tick zero until every frozen seat is online and
// loaded; a reconnect or explicit takeover requires another acknowledgement.
func (w *World) MarkLoaded(user string, generation uint32) bool {
	if w.recording != nil {
		defer w.recordEvent("loaded", replayEventPayload{User: user, Generation: generation})()
	}
	if w.BotUser != "" && user == w.BotUser {
		return false
	}
	if w.roster == nil || w.Phase != phaseLoading {
		return false
	}
	s := w.Seats[user]
	if s == nil || !s.State.Online || s.State.Left || s.Generation != generation {
		return false
	}
	s.Loaded = true
	for _, seat := range w.Seats {
		if !seat.State.Online || !seat.Loaded {
			return false
		}
	}
	w.Phase = phasePlaying
	w.publishStart()
	return true
}

func (w *World) seatExpired(s *Seat) bool {
	return w.Now().Sub(s.LeftAt) >= time.Duration(w.Rules.ReconnectTicks)*time.Second/time.Duration(w.Rules.TickRate)
}
