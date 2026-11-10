package main

import (
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"sync"

	"github.com/win30221/gamenetkit/server/netcore"
	"github.com/win30221/gamenetkit/server/replay"
)

const replayApplication = "replay-lesson"
const replayModel = "authoritative-frames-v1"
const maxFrames = 451
const maxRecordingTicks = 30 * tickRate

type recordMessage struct {
	Type        string  `json:"type"`
	Protocol    int     `json:"protocol"`
	Status      string  `json:"status"`
	RecordingID string  `json:"recordingID"`
	FrameCount  int     `json:"frameCount"`
	Duration    float64 `json:"duration"`
	Reason      string  `json:"reason"`
}
type replayDefinition struct {
	TickRate      int          `json:"tickRate"`
	LocalPlayerID uint64       `json:"localPlayerID"`
	Initial       stateMessage `json:"initial"`
}
type playbackFile struct {
	Version       int            `json:"version"`
	Application   string         `json:"application"`
	TickRate      int            `json:"tickRate"`
	LocalPlayerID uint64         `json:"localPlayerID"`
	SourceDigest  string         `json:"sourceDigest"`
	Frames        []stateMessage `json:"frames"`
}
type recordingResult struct {
	data []byte
	err  error
}

// Capture/stop belong to the simulation owner. Disk work and verification belong
// to the worker; result is read only after done closes.
type recording struct {
	id, path         string
	frames           chan []byte
	failure          chan error
	done             chan struct{}
	result           recordingResult
	first, last      uint64
	count            int
	closed, reported bool
}
type recordingStore struct {
	directory string
	mu        sync.Mutex
	items     []*recording
	exports   map[string][]byte
}

func recordingLimits() replay.Limits {
	return replay.Limits{Queue: 128, RecordBytes: 16 * 1024, FileBytes: 8 * 1024 * 1024, Entries: maxFrames}
}
func newRecordingStore(directory string) (*recordingStore, error) {
	if err := os.MkdirAll(directory, 0700); err != nil {
		return nil, err
	}
	return &recordingStore{directory: directory, exports: make(map[string][]byte)}, nil
}
func (s *recordingStore) begin(owner uint64, initial stateMessage) (*recording, error) {
	if err := validateRecordedFrame(initial, owner); err != nil {
		return nil, err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if len(s.items) >= 16 {
		return nil, errors.New("recording_limit")
	}
	var token [16]byte
	if _, err := rand.Read(token[:]); err != nil {
		return nil, err
	}
	id := hex.EncodeToString(token[:])
	r := &recording{id: id, path: filepath.Join(s.directory, id+".jsonl"), frames: make(chan []byte, 128), failure: make(chan error, 1), done: make(chan struct{}), first: initial.Tick, last: initial.Tick, count: 1}
	s.items = append(s.items, r)
	go func() {
		defer close(r.done)
		r.result = writeRecording(r, owner, initial)
		if r.result.err == nil {
			s.mu.Lock()
			s.exports[id] = r.result.data
			s.mu.Unlock()
		}
	}()
	return r, nil
}
func (s *recordingStore) get(id string) ([]byte, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	data, ok := s.exports[id]
	return data, ok
}
func (s *recordingStore) close() {
	// World shutdown/disconnect has already closed every recording input.
	s.mu.Lock()
	items := append([]*recording(nil), s.items...)
	s.mu.Unlock()
	for _, r := range items {
		<-r.done
	}
}
func (r *recording) stop() {
	if !r.closed {
		r.closed = true
		close(r.frames)
	}
}
func (r *recording) capture(frame stateMessage) {
	if r.closed {
		return
	}
	if frame.Tick-r.first > maxRecordingTicks || r.count >= maxFrames {
		r.stop()
		return
	}
	payload, _ := json.Marshal(frame)
	select {
	case r.frames <- payload:
		r.count++
		r.last = frame.Tick
	default:
		r.failure <- errors.New("capture_overflow")
		r.stop()
		return
	}
	if r.last-r.first >= maxRecordingTicks || r.count >= maxFrames {
		r.stop()
	}
}
func (r *recording) message(status, reason string) recordMessage {
	return recordMessage{Type: "record-state", Protocol: 1, Status: status, RecordingID: r.id, FrameCount: r.count, Duration: float64(r.last-r.first) / tickRate, Reason: reason}
}
func hashFrame(payload []byte) string { return fmt.Sprintf("%x", sha256.Sum256(payload)) }
func writeRecording(r *recording, owner uint64, initial stateMessage) recordingResult {
	definition, _ := json.Marshal(replayDefinition{TickRate: tickRate, LocalPlayerID: owner, Initial: initial})
	initialPayload, _ := json.Marshal(initial)
	header := netcore.ReplayHeader{Version: replay.Version, Application: replayApplication, Model: replayModel, Definition: definition, InitialHash: hashFrame(initialPayload)}
	journal, err := replay.New(r.path, header, recordingLimits())
	if err != nil {
		return recordingResult{err: err}
	}
	for payload := range r.frames {
		var frame stateMessage
		if json.Unmarshal(payload, &frame) != nil {
			err = errors.New("invalid_capture")
			continue
		}
		if !journal.Append(netcore.ReplayEntry{Kind: "step", Tick: frame.Tick, Payload: payload, StateHash: hashFrame(payload)}) {
			err = journal.Err()
		}
	}
	select {
	case failure := <-r.failure:
		err = failure
	default:
	}
	if err != nil {
		// Poison the journal so an application capture failure cannot publish a
		// valid-looking prefix with a completion footer.
		journal.Append(netcore.ReplayEntry{Kind: "invalid", StateHash: ""})
	}
	if closeErr := journal.Close(); err == nil {
		err = closeErr
	}
	if err != nil {
		return recordingResult{err: err}
	}
	data, err := exportRecording(r.path)
	return recordingResult{data: data, err: err}
}
func decodeRecordingJSON(data []byte, target any) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	if decoder.Decode(new(any)) != io.EOF {
		return errors.New("trailing_recording_data")
	}
	return nil
}

// Verification stages the entire clip away from the live world. Nothing is
// published to HTTP until both SDK footer checks and application checks pass.
func exportRecording(path string) ([]byte, error) {
	file := playbackFile{Version: 1, Application: replayApplication, TickRate: tickRate}
	summary, err := replay.VerifyFile(path, recordingLimits(), func(h netcore.ReplayHeader) error {
		if h.Application != replayApplication || h.Model != replayModel {
			return errors.New("incompatible_recording")
		}
		var definition replayDefinition
		if err := decodeRecordingJSON(h.Definition, &definition); err != nil {
			return err
		}
		payload, _ := json.Marshal(definition.Initial)
		if definition.TickRate != tickRate || hashFrame(payload) != h.InitialHash {
			return errors.New("invalid_initial_frame")
		}
		if err := validateRecordedFrame(definition.Initial, definition.LocalPlayerID); err != nil {
			return err
		}
		file.LocalPlayerID = definition.LocalPlayerID
		file.Frames = append(file.Frames, definition.Initial)
		return nil
	}, func(entry netcore.ReplayEntry) error {
		if entry.Kind != "step" || len(file.Frames) >= maxFrames || hashFrame(entry.Payload) != entry.StateHash {
			return errors.New("invalid_recorded_frame")
		}
		var frame stateMessage
		if err := decodeRecordingJSON(entry.Payload, &frame); err != nil {
			return err
		}
		if err := validateRecordedFrame(frame, file.LocalPlayerID); err != nil {
			return err
		}
		if frame.Tick != entry.Tick || frame.Tick <= file.Frames[len(file.Frames)-1].Tick || frame.Tick-file.Frames[0].Tick > maxRecordingTicks {
			return errors.New("invalid_recorded_tick")
		}
		file.Frames = append(file.Frames, frame)
		return nil
	})
	if err != nil {
		return nil, err
	}
	file.SourceDigest = summary.Digest
	data, err := json.Marshal(file)
	if len(data) > 8*1024*1024 {
		return nil, errors.New("playback_size_limit")
	}
	return data, err
}
func validateRecordedFrame(f stateMessage, owner uint64) error {
	if f.Type != "snapshot" || f.Protocol != 1 || f.Generation != 1 || f.Players == nil || f.Objects == nil || len(f.Players) > 8 || len(f.Objects) > 32 || f.Tick > 1000000000000 {
		return errors.New("invalid_recording_envelope")
	}
	players := map[uint64]bool{}
	objects := map[string]bool{}
	counts := map[uint64]int{}
	position := func(x, z float64) bool {
		return !math.IsNaN(x+z) && !math.IsInf(x+z, 0) && math.Abs(x) <= 8 && math.Abs(z) <= 8
	}
	for _, p := range f.Players {
		if p.ID == 0 || players[p.ID] || !position(p.X, p.Z) || p.InputTick > f.Tick+1 {
			return errors.New("invalid_recording_player")
		}
		players[p.ID] = true
	}
	if owner == 0 || !players[owner] {
		return errors.New("recording_owner_missing")
	}
	for _, o := range f.Objects {
		if o.ID == "" || len(o.ID) > 32 || objects[o.ID] || !players[o.Owner] || o.Life == 0 || !position(o.X, o.Z) {
			return errors.New("invalid_recording_object")
		}
		objects[o.ID] = true
		counts[o.Owner]++
		if counts[o.Owner] > 4 {
			return errors.New("recording_object_capacity")
		}
	}
	return nil
}

func offerRecord(p *peer, message recordMessage) {
	data, _ := json.Marshal(message)
	offerControl(p, data)
}
func (w *world) recordControl(p *player, operation string) {
	if operation == "stop" {
		if p.recording == nil {
			return
		}
		p.recording.stop()
		select {
		case <-p.recording.done:
			if p.recording.result.err == nil {
				offerRecord(p.peer, p.recording.message("ready", ""))
			} else {
				offerRecord(p.peer, p.recording.message("error", "recording_failed"))
			}
		default:
			offerRecord(p.peer, p.recording.message("finalizing", ""))
		}
		return
	}
	if p.recording != nil {
		select {
		case <-p.recording.done:
		default:
			status := "recording"
			if p.recording.closed {
				status = "finalizing"
			}
			offerRecord(p.peer, p.recording.message(status, ""))
			return
		}
	}
	if w.recordings == nil {
		offerRecord(p.peer, recordMessage{Type: "record-state", Protocol: 1, Status: "error", Reason: "recording_unavailable"})
		return
	}
	r, err := w.recordings.begin(p.state.ID, w.frame())
	if err != nil {
		offerRecord(p.peer, recordMessage{Type: "record-state", Protocol: 1, Status: "error", Reason: err.Error()})
		return
	}
	p.recording = r
	offerRecord(p.peer, r.message("recording", ""))
}
func (w *world) pollRecordings(frame *stateMessage) {
	for _, p := range w.players {
		r := p.recording
		if r == nil {
			continue
		}
		if frame != nil && !r.closed {
			r.capture(*frame)
			if r.closed {
				offerRecord(p.peer, r.message("finalizing", ""))
			} else if w.tick%30 == 0 {
				offerRecord(p.peer, r.message("recording", ""))
			}
		}
		if r.reported {
			continue
		}
		select {
		case <-r.done:
			// An early storage failure may finish the worker before stop.
			r.stop()
			r.reported = true
			if r.result.err != nil {
				offerRecord(p.peer, r.message("error", "recording_failed"))
			} else {
				offerRecord(p.peer, r.message("ready", ""))
			}
		default:
		}
	}
}
