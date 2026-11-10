package sim

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"strings"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

const MaxPlaybackBytes = 32 * 1024 * 1024
const MaxPlaybackFrames = 120000

type PlaybackFrame struct {
	Tick    uint64 `json:"tick"`
	Payload string `json:"payload"`
}
type PlaybackClip struct {
	Version      int             `json:"version"`
	Model        string          `json:"model"`
	MatchID      string          `json:"matchID"`
	Content      string          `json:"content"`
	MapHash      string          `json:"mapHash"`
	TickRate     int             `json:"tickRate"`
	Protocol     int             `json:"protocol"`
	Config       string          `json:"config"`
	Frames       []PlaybackFrame `json:"frames"`
	SourceDigest string          `json:"sourceDigest"`
}

// ExportPlayback verifies every simulation event before publishing a bounded
// clip. Frames are post-step authority snapshots, independent of network AOI,
// delta baselines, reconnect generations and any client's view permissions.
func ExportPlayback(input, output string) (report ReplayReport) {
	clip := PlaybackClip{Version: 1, Model: replayModel}
	bytes := 0
	report = walkReplay(input, func(w *World) error {
		seats := w.sortedSeats()
		if len(seats) == 0 && clip.TickRate == 0 {
			return nil
		}
		if clip.TickRate == 0 {
			clip.MatchID = w.MatchID
			clip.Content = w.Rules.Content
			clip.Protocol = w.Rules.Protocol
			clip.TickRate = w.Rules.TickRate
			clip.MapHash = w.MapHash
			mapJSON := ""
			if w.Map != nil {
				b, e := json.Marshal(w.Map)
				if e != nil {
					return e
				}
				mapJSON = string(b)
				if w.MapHash == shared.ArenaMapHash() {
					mapJSON = shared.ArenaMapJSON()
				}
			}
			config, e := json.Marshal(struct {
				Rules   shared.Config `json:"rules"`
				MatchID string        `json:"matchID"`
				MapJSON string        `json:"mapJSON"`
				MapHash string        `json:"mapHash"`
			}{w.Rules, w.MatchID, mapJSON, w.MapHash})
			if e != nil {
				return e
			}
			clip.Config = string(config)
			bytes = len(config)
		}
		// The copy prevents per-client filtering from modifying the exported
		// roster. Playback uses a spectator, not the recorded connection's ack.
		viewer := Seat{}
		if len(seats) > 0 {
			viewer = *seats[0]
		}
		viewer.Net = nil
		viewer.Interest = nil
		snapshot := w.Snapshot(&viewer, "snapshot")
		snapshot.Generation = 1
		snapshot.Echo = 0
		snapshot.ActionAck = 0
		snapshot.ActionCode = ""
		data, e := json.Marshal(snapshot)
		if e != nil {
			return e
		}
		if len(data) > 16384 || len(clip.Frames) >= MaxPlaybackFrames || w.Tick > uint64(w.Rules.TickRate)*3600 {
			return errors.New("playback_frame_limit")
		}
		bytes += len(data) + 64
		if bytes > MaxPlaybackBytes {
			return errors.New("playback_file_limit")
		}
		clip.Frames = append(clip.Frames, PlaybackFrame{Tick: w.Tick, Payload: string(data)})
		return nil
	})
	if report.Status != "PASS" {
		return
	}
	fail := func(e error) ReplayReport { report.Status = "FAIL"; report.Error = e.Error(); return report }
	if len(clip.Frames) == 0 {
		return fail(errors.New("no_playable_frames"))
	}
	clip.SourceDigest = report.Summary.Digest
	data, e := json.Marshal(clip)
	if e != nil {
		return fail(e)
	}
	if len(data) > MaxPlaybackBytes {
		return fail(errors.New("playback_file_limit"))
	}
	path, e := filepath.Abs(output)
	if e != nil {
		return fail(e)
	}
	if strings.HasSuffix(path, ".partial") {
		return fail(errors.New("invalid_playback_destination"))
	}
	f, e := os.OpenFile(path+".partial", os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if e != nil {
		return fail(e)
	}
	if _, e = f.Write(data); e == nil {
		e = f.Sync()
	}
	closeErr := f.Close()
	if e == nil {
		e = closeErr
	}
	if e == nil {
		e = os.Link(path+".partial", path)
	}
	if e == nil {
		e = os.Remove(path + ".partial")
	}
	if e != nil {
		return fail(e)
	}
	return
}
