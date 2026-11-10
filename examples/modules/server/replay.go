package main

import (
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"github.com/win30221/gamenetkit/server/netcore"
	"github.com/win30221/gamenetkit/server/replay"
	"os"
	"path/filepath"
)

func counterHash(value int) string {
	return fmt.Sprintf("%x", sha256.Sum256([]byte(fmt.Sprint(value))))
}

func replayLesson() error {
	dir, err := os.MkdirTemp("", "rf-replay-lesson-")
	if err != nil {
		return err
	}
	path := filepath.Join(dir, "counter.jsonl")
	defer func() { _ = os.Remove(path); _ = os.Remove(path + ".partial"); _ = os.Remove(dir) }()
	limits := replay.DefaultLimits()
	header := netcore.ReplayHeader{Version: replay.Version, Application: "counter-lesson", Model: "add-v1", Definition: json.RawMessage(`{"initial":0}`), InitialHash: counterHash(0)}
	journal, err := replay.New(path, header, limits)
	if err != nil {
		return err
	}
	defer journal.Close()
	live := 0
	for i, increment := range []int{2, 3} {
		live += increment // Rules and hashes belong to the application.
		payload, _ := json.Marshal(increment)
		if !journal.Append(netcore.ReplayEntry{Kind: "step", Tick: uint64(i + 1), Payload: payload, StateHash: counterHash(live)}) {
			return fmt.Errorf("append failed: %v", journal.Err())
		}
	}
	if err := journal.Close(); err != nil {
		return err
	} // Publishes a complete file.
	playback := -1 // Isolated from live state; never apply an unverified prefix live.
	summary, err := replay.VerifyFile(path, limits, func(h netcore.ReplayHeader) error {
		if h.Application != header.Application || h.Model != header.Model {
			return fmt.Errorf("incompatible replay")
		}
		var definition struct {
			Initial int `json:"initial"`
		}
		if err := json.Unmarshal(h.Definition, &definition); err != nil {
			return err
		}
		playback = definition.Initial
		return require(counterHash(playback) == h.InitialHash, "initial hash mismatch")
	}, func(entry netcore.ReplayEntry) error {
		if entry.Kind != "step" {
			return fmt.Errorf("unknown event")
		}
		var increment int
		if err := json.Unmarshal(entry.Payload, &increment); err != nil {
			return err
		}
		playback += increment
		return require(counterHash(playback) == entry.StateHash, "state hash mismatch")
	})
	if err != nil {
		return err
	}
	if err := require(summary.Entries == 2 && playback == live && live == 5, "replay diverged"); err != nil {
		return err
	}
	fmt.Println("replay: recording off -> no journal; on -> 2 verified events, live=5, isolated playback=5")
	return nil
}
