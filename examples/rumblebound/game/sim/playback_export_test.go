//go:build !rf_no_replay

package sim

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestPlaybackExportVerifiedFullWorldAndMapIdentity(t *testing.T) {
	w, e := newMatchWorldWithMap("playback-export", map[string]string{"a": "red", "b": "blue"}, map[string]string{"a": "tank", "b": "ranger"}, shared.ArenaMap())
	if e != nil {
		t.Fatal(e)
	}
	path, finish := startFixtureReplay(t, w)
	a, _ := w.Attach("a", false)
	b, _ := w.Attach("b", false)
	if interestFactory != nil {
		w.configureNet("a", a.Generation, &protocol.NetOptions{Codec: "json", Interest: "radius", Radius: 4, Lag: "off"})
	}
	w.MarkLoaded("a", a.Generation)
	w.MarkLoaded("b", b.Generation)
	for i := 0; i < 8; i++ {
		w.Step()
	}
	finish()
	out := filepath.Join(t.TempDir(), "playback.json")
	report := ExportPlayback(path, out)
	if report.Status != "PASS" {
		t.Fatal(report)
	}
	data, e := os.ReadFile(out)
	if e != nil {
		t.Fatal(e)
	}
	var clip PlaybackClip
	if e = json.Unmarshal(data, &clip); e != nil {
		t.Fatal(e)
	}
	if len(clip.Frames) != 8 || clip.SourceDigest != report.Summary.Digest || clip.Version != 1 {
		t.Fatal("invalid playback metadata")
	}
	// The native integration harness requests a fresh fixture from this real
	// journal/export path instead of relying on the retired sample directory.
	if destination := os.Getenv("RF_PLAYBACK_FIXTURE"); destination != "" {
		if err := os.WriteFile(destination, data, 0600); err != nil {
			t.Fatal(err)
		}
	}
	var config struct {
		MapJSON string `json:"mapJSON"`
		MapHash string `json:"mapHash"`
	}
	if e = json.Unmarshal([]byte(clip.Config), &config); e != nil {
		t.Fatal(e)
	}
	h := sha256.Sum256([]byte(config.MapJSON))
	if hex.EncodeToString(h[:]) != config.MapHash || config.MapHash != shared.ArenaMapHash() {
		t.Fatal("lost exact source map hash")
	}
	for i, frame := range clip.Frames {
		var snapshot protocol.Server
		if e = json.Unmarshal([]byte(frame.Payload), &snapshot); e != nil {
			t.Fatal(e)
		}
		if frame.Tick != uint64(i+1) || len(snapshot.Players) != 2 || snapshot.Generation != 1 || snapshot.Tech != nil {
			t.Fatal("export leaked AOI/connection state")
		}
	}
	// A changed input/state hash cannot produce a seemingly valid playback.
	journal, _ := os.ReadFile(path)
	damaged := strings.Replace(string(journal), `"stateHash":"`, `"stateHash":"0`, 1)
	bad := filepath.Join(t.TempDir(), "bad.jsonl")
	_ = os.WriteFile(bad, []byte(damaged), 0600)
	badOut := filepath.Join(t.TempDir(), "bad-playback.json")
	if r := ExportPlayback(bad, badOut); r.Status != "FAIL" {
		t.Fatal("exported invalid journal")
	}
	if _, e = os.Stat(badOut); !os.IsNotExist(e) {
		t.Fatal("published invalid export")
	}
	if r := ExportPlayback(path, out); r.Status != "FAIL" {
		t.Fatal("overwrote existing export")
	}
	after, _ := os.ReadFile(out)
	if string(after) != string(data) {
		t.Fatal("changed existing playback")
	}
}
func TestPlaybackExportRetainsEmptyRosterAfterExpiry(t *testing.T) {
	w := New()
	path, finish := startFixtureReplay(t, w)
	now := time.Unix(1700000000, 0)
	w.Now = func() time.Time { return now }
	a, _ := w.Attach("a", false)
	w.Step()
	w.Disconnect("a", a.Generation)
	now = now.Add(40 * time.Second)
	w.Step()
	w.Step()
	_, _ = w.Attach("b", false)
	w.Step()
	finish()
	out := filepath.Join(t.TempDir(), "empty-roster.json")
	if r := ExportPlayback(path, out); r.Status != "PASS" {
		t.Fatal(r)
	}
	data, _ := os.ReadFile(out)
	var clip PlaybackClip
	_ = json.Unmarshal(data, &clip)
	if len(clip.Frames) != 4 {
		t.Fatal("dropped empty-roster ticks")
	}
	for i, count := range []int{1, 0, 0, 1} {
		var s protocol.Server
		_ = json.Unmarshal([]byte(clip.Frames[i].Payload), &s)
		if len(s.Players) != count {
			t.Fatalf("tick %d roster=%d", clip.Frames[i].Tick, len(s.Players))
		}
	}
}
