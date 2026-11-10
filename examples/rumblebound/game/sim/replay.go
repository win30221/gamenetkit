package sim

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/server/netcore"
)

const replayModel = "rumblebound-sim-v1"

type replayDirectoryKey struct{}

var openReplay func(string, netcore.ReplayHeader) (netcore.ReplaySink, error)
var readReplay func(string, func(netcore.ReplayHeader) error, func(netcore.ReplayEntry) error) (netcore.ReplaySummary, error)

// WithReplayDirectory enables recording from engine creation. It does not
// change other contexts or require a database. Empty path explicitly disables.
func WithReplayDirectory(ctx context.Context, dir string) (context.Context, error) {
	if dir == "" {
		return context.WithValue(ctx, replayDirectoryKey{}, ""), nil
	}
	if openReplay == nil {
		return nil, errors.New("replay_module_not_installed")
	}
	abs, e := filepath.Abs(dir)
	if e != nil {
		return nil, e
	}
	if e = os.MkdirAll(abs, 0700); e != nil {
		return nil, e
	}
	return context.WithValue(ctx, replayDirectoryKey{}, abs), nil
}

type replayDefinition struct {
	MatchID             string                     `json:"matchID"`
	Rules               shared.Config              `json:"rules"`
	Map                 *shared.ArenaMapDefinition `json:"map,omitempty"`
	Roster              map[string]string          `json:"roster,omitempty"`
	Heroes              map[string]string          `json:"heroes,omitempty"`
	Owner, Bot, BotMode string
	RandomSource        string `json:"randomSource"`
	Seed                uint64 `json:"seed"`
}

func replayDefinitionFor(w *World) replayDefinition {
	d := replayDefinition{MatchID: w.MatchID, Rules: w.Rules, Map: w.Map, Roster: w.roster, Owner: w.PracticeOwner, Bot: w.BotUser, BotMode: w.BotMode, RandomSource: "none"}
	for user, s := range w.Seats {
		if s.hero != nil {
			if d.Heroes == nil {
				d.Heroes = map[string]string{}
			}
			d.Heroes[user] = s.hero.HeroID
		}
	}
	return d
}
func restoreReplayDefinition(d replayDefinition) (*World, error) {
	if d.RandomSource != "none" || d.Seed != 0 {
		return nil, errors.New("unsupported_replay_random_source")
	}
	var w *World
	var e error
	switch {
	case d.Owner != "":
		w, e = newPracticeWorld(d.MatchID, d.Roster, d.Heroes, d.Map, d.Owner, d.Bot, d.BotMode)
	case d.Map != nil:
		w, e = newMatchWorldWithMap(d.MatchID, d.Roster, d.Heroes, d.Map)
	case d.Heroes != nil:
		w, e = newMatchWorldWithHeroes(d.MatchID, d.Roster, d.Heroes)
	case d.Roster != nil:
		w, e = newMatchWorld(d.MatchID, d.Roster)
	default:
		w = New()
		if d.MatchID != MatchID {
			return nil, errors.New("invalid_replay_lab")
		}
	}
	if e != nil {
		return nil, e
	}
	a, _ := json.Marshal(w.Rules)
	b, _ := json.Marshal(d.Rules)
	if !bytes.Equal(a, b) {
		return nil, errors.New("replay_rules_mismatch")
	}
	return w, nil
}
func replayHash(value any) (string, error) {
	b, e := json.Marshal(value)
	if e != nil {
		return "", e
	}
	h := sha256.Sum256(b)
	return hex.EncodeToString(h[:]), nil
}

// ReplayStatus is local diagnostics, not a public replay download API. A file
// becomes Complete only after its asynchronous writer publishes the footer.
type ReplayStatus struct {
	Enabled  bool   `json:"enabled"`
	Path     string `json:"path,omitempty"`
	Complete bool   `json:"complete"`
	Error    string `json:"error,omitempty"`
}

func (e *Engine) ReplayStatus() ReplayStatus {
	s := ReplayStatus{Enabled: e.recording != nil, Error: e.recordingError}
	if e.recording != nil {
		s.Path = e.recording.Path()
		if err := e.recording.Err(); err != nil {
			s.Error = err.Error()
		}
		select {
		case <-e.Done:
			s.Complete = s.Error == ""
		default:
		}
	}
	return s
}
func (w *World) startReplay(ctx context.Context) (netcore.ReplaySink, error) {
	dir, _ := ctx.Value(replayDirectoryKey{}).(string)
	if dir == "" {
		return nil, nil
	}
	if openReplay == nil {
		return nil, errors.New("replay_module_not_installed")
	}
	d := replayDefinitionFor(w)
	initial, e := w.replayStateHash()
	if e != nil {
		return nil, e
	}
	probe, e := restoreReplayDefinition(d)
	if e != nil {
		return nil, e
	}
	check, e := probe.replayStateHash()
	if e != nil || check != initial {
		return nil, errors.New("replay_requires_pristine_engine")
	}
	b, e := json.Marshal(d)
	if e != nil {
		return nil, e
	}
	r, e := openReplay(dir, netcore.ReplayHeader{Version: 1, Application: "rumblebound", Model: replayModel, Definition: b, InitialHash: initial})
	if e == nil {
		w.recording = r
	}
	return r, e
}

// Called only on the world owner. A fixed clock is used for all reads in one
// operation, so wall-clock disconnect expiry is reproducible down to the ns.
func (w *World) recordEvent(kind string, payload any) func() {
	clock := w.Now
	stamp := clock().UTC()
	w.Now = func() time.Time { return stamp }
	return func() {
		w.Now = clock
		if w.recording.Err() != nil {
			return
		}
		data, err := json.Marshal(payload)
		if err != nil {
			data = []byte("null")
		}
		h, hashErr := w.replayStateHash()
		if err != nil || hashErr != nil {
			h = "invalid"
		}
		if !w.recording.Append(netcore.ReplayEntry{Kind: kind, Tick: w.Tick, TimeNanos: stamp.UnixNano(), Payload: data, StateHash: h}) {
			log.Printf("Replay stopped recording %s: %v", w.recording.Path(), w.recording.Err())
		}
	}
}

type replayEventPayload struct {
	User       string               `json:"user,omitempty"`
	Generation uint32               `json:"generation,omitempty"`
	Takeover   bool                 `json:"takeover,omitempty"`
	Input      *InputIntent         `json:"input,omitempty"`
	Options    *protocol.NetOptions `json:"options,omitempty"`
	Mode       string               `json:"mode,omitempty"`
}

type ReplayReport struct {
	Status     string                `json:"status"`
	MatchID    string                `json:"matchID,omitempty"`
	Summary    netcore.ReplaySummary `json:"summary"`
	Divergence *ReplayDivergence     `json:"divergence,omitempty"`
	Error      string                `json:"error,omitempty"`
}
type ReplayDivergence struct {
	Sequence   uint64 `json:"sequence"`
	Kind       string `json:"kind"`
	Tick       uint64 `json:"tick"`
	ActualTick uint64 `json:"actualTick"`
	Expected   string `json:"expected"`
	Actual     string `json:"actual"`
}

func (d *ReplayDivergence) Error() string {
	return fmt.Sprintf("replay_diverged: event %d %s tick %d (actual tick %d)", d.Sequence, d.Kind, d.Tick, d.ActualTick)
}
func decodeReplay(data []byte, value any) error {
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if e := d.Decode(value); e != nil {
		return e
	}
	if d.Decode(new(any)) != io.EOF {
		return errors.New("trailing_replay_payload")
	}
	return nil
}

// VerifyReplay never binds sockets, modifies live worlds, or trusts the replay
// as an online authority. It reconstructs a fresh world and stops on first drift.
func VerifyReplay(path string) (report ReplayReport) {
	return walkReplay(path, nil)
}
func walkReplay(path string, afterStep func(*World) error) (report ReplayReport) {
	report.Status = "FAIL"
	if readReplay == nil {
		report.Error = "replay_module_not_installed"
		return
	}
	var w *World
	summary, err := readReplay(path, func(h netcore.ReplayHeader) error {
		if h.Application != "rumblebound" || h.Model != replayModel {
			return errors.New("incompatible_replay_model")
		}
		var d replayDefinition
		if e := decodeReplay(h.Definition, &d); e != nil {
			return e
		}
		var e error
		w, e = restoreReplayDefinition(d)
		if e != nil {
			return e
		}
		report.MatchID = w.MatchID
		actual, e := w.replayStateHash()
		if e != nil {
			return e
		}
		if actual != h.InitialHash {
			return errors.New("replay_initial_state_mismatch")
		}
		return nil
	}, func(entry netcore.ReplayEntry) error {
		w.Now = func() time.Time { return time.Unix(0, entry.TimeNanos).UTC() }
		var p replayEventPayload
		if e := decodeReplay(entry.Payload, &p); e != nil {
			return e
		}
		switch entry.Kind {
		case "attach":
			_, _ = w.Attach(p.User, p.Takeover)
		case "disconnect":
			w.Disconnect(p.User, p.Generation)
		case "loaded":
			w.MarkLoaded(p.User, p.Generation)
		case "input":
			if p.Input == nil {
				return errors.New("missing_replay_input")
			}
			w.Input(p.User, p.Generation, *p.Input)
		case "net":
			if e := ValidateNetOptions(p.Options); e != nil {
				return e
			}
			w.configureNet(p.User, p.Generation, p.Options)
		case "practice":
			_ = w.setPracticeMode(p.User, p.Mode)
		case "step":
			w.Step()
		default:
			return errors.New("unknown_replay_event")
		}
		actual, e := w.replayStateHash()
		if e != nil {
			return e
		}
		if actual != entry.StateHash || w.Tick != entry.Tick {
			return &ReplayDivergence{Sequence: entry.Sequence, Kind: entry.Kind, Tick: entry.Tick, ActualTick: w.Tick, Expected: entry.StateHash, Actual: actual}
		}
		if entry.Kind == "step" && afterStep != nil {
			return afterStep(w)
		}
		return nil
	})
	report.Summary = summary
	if err != nil {
		report.Error = err.Error()
		errors.As(err, &report.Divergence)
		return
	}
	report.Status = "PASS"
	return
}
