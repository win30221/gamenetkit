package main

import (
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/server/netcore"
	"github.com/win30221/gamenetkit/server/replay"
)

func testFrame(tick uint64, x float64) stateMessage {
	return stateMessage{Type: "snapshot", Protocol: 1, Generation: 1, Tick: tick, Players: []playerState{{ID: 1, X: x}}, Objects: []objectState{}}
}
func waitRecording(t *testing.T, r *recording) {
	t.Helper()
	select {
	case <-r.done:
	case <-time.After(5 * time.Second):
		t.Fatal("recording worker did not finish")
	}
}
func TestRecordingPublishesOnlyVerifiedCompleteFrames(t *testing.T) {
	store, err := newRecordingStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	r, err := store.begin(1, testFrame(10, 0))
	if err != nil {
		t.Fatal(err)
	}
	if _, ok := store.get(r.id); ok {
		t.Fatal("active recording published")
	}
	if _, err := os.Stat(r.path); !os.IsNotExist(err) {
		t.Fatal("active file exposed as complete")
	}
	frame := testFrame(12, 1)
	frame.Objects = []objectState{{ID: "object-1", Owner: 1, Life: 1, X: 2}}
	r.capture(frame)
	frame.Objects[0].Life = 2
	frame.Tick = 14
	r.capture(frame)
	r.capture(testFrame(16, 3))
	r.stop()
	waitRecording(t, r)
	if r.result.err != nil {
		t.Fatal(r.result.err)
	}
	data, ok := store.get(r.id)
	if !ok {
		t.Fatal("completed recording missing")
	}
	var file playbackFile
	if err := json.Unmarshal(data, &file); err != nil {
		t.Fatal(err)
	}
	if file.LocalPlayerID != 1 || len(file.Frames) != 4 || len(file.SourceDigest) != 64 || file.Frames[1].Objects[0].Life != 1 || file.Frames[2].Objects[0].Life != 2 || len(file.Frames[3].Objects) != 0 {
		t.Fatal("recorded frames mutated or incomplete")
	}
	raw, err := os.ReadFile(r.path)
	if err != nil {
		t.Fatal(err)
	}
	corrupt := filepath.Join(t.TempDir(), "corrupt.jsonl")
	raw[len(raw)/2] ^= 1
	if err := os.WriteFile(corrupt, raw, 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := exportRecording(corrupt); err == nil {
		t.Fatal("corrupt journal accepted")
	}
	partial := filepath.Join(t.TempDir(), "recording.partial")
	if err := os.WriteFile(partial, []byte("prefix\n"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := exportRecording(partial); err == nil {
		t.Fatal("partial journal accepted")
	}
	store.close()
}
func TestCaptureOverflowCannotPublishSuccessfulPrefix(t *testing.T) {
	r := &recording{id: "overflow", path: filepath.Join(t.TempDir(), "overflow.jsonl"), frames: make(chan []byte, 1), failure: make(chan error, 1), first: 0, last: 0, count: 1}
	r.capture(testFrame(2, 1))
	r.capture(testFrame(4, 2))
	if !r.closed {
		t.Fatal("overflow kept capturing")
	}
	result := writeRecording(r, 1, testFrame(0, 0))
	if result.err == nil {
		t.Fatal("overflow reported success")
	}
	if _, err := os.Stat(r.path); !os.IsNotExist(err) {
		t.Fatal("successful prefix published")
	}
	if _, err := os.Stat(r.path + ".partial"); err != nil {
		t.Fatal("failure must remain partial", err)
	}
}
func TestRecordedStateHashIsApplicationValidated(t *testing.T) {
	initial := testFrame(0, 0)
	definition, _ := json.Marshal(replayDefinition{TickRate: 30, LocalPlayerID: 1, Initial: initial})
	payload, _ := json.Marshal(initial)
	path := filepath.Join(t.TempDir(), "wrong-state.jsonl")
	journal, err := replay.New(path, netcore.ReplayHeader{Version: 1, Application: replayApplication, Model: replayModel, Definition: definition, InitialHash: hashFrame(payload)}, recordingLimits())
	if err != nil {
		t.Fatal(err)
	}
	next, _ := json.Marshal(testFrame(2, 1))
	if !journal.Append(netcore.ReplayEntry{Kind: "step", Tick: 2, Payload: next, StateHash: strings.Repeat("0", 64)}) {
		t.Fatal(journal.Err())
	}
	if err := journal.Close(); err != nil {
		t.Fatal(err)
	}
	if _, err := exportRecording(path); err == nil {
		t.Fatal("SDK file checks alone cannot validate application state")
	}
}
func TestRecordingBoundsAndInvalidModels(t *testing.T) {
	r := &recording{frames: make(chan []byte, maxFrames), failure: make(chan error, 1), count: 1}
	for tick := uint64(2); tick <= 1000; tick += 2 {
		r.capture(testFrame(tick, 0))
	}
	if !r.closed || r.count != maxFrames || r.last != 900 {
		t.Fatal("recording did not stop at thirty seconds", r.count, r.last)
	}
	initial := testFrame(0, 0)
	initial.Objects = []objectState{{ID: "object-1", Owner: 2, Life: 1}}
	if validateRecordedFrame(initial, 1) == nil {
		t.Fatal("foreign object owner accepted")
	}
	initial = testFrame(0, 0)
	initial.Players = append(initial.Players, initial.Players[0])
	if validateRecordedFrame(initial, 1) == nil {
		t.Fatal("duplicate player accepted")
	}
	initial = testFrame(0, 0)
	if validateRecordedFrame(initial, 2) == nil {
		t.Fatal("missing recording owner accepted")
	}
	store, err := newRecordingStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 16; i++ {
		r, err := store.begin(1, testFrame(0, 0))
		if err != nil {
			t.Fatal(err)
		}
		r.stop()
	}
	if _, err := store.begin(1, testFrame(0, 0)); err == nil {
		t.Fatal("unbounded recordings")
	}
	store.close()
}
func TestActiveRecordingFinishesOnApplicationShutdown(t *testing.T) {
	app, server := testApplication(t)
	c := dial(t, server)
	join(t, c)
	send(t, c, `{"type":"record","command":"start"}`)
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	var id string
	for id == "" {
		_, data, err := c.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var m recordMessage
		_ = json.Unmarshal(data, &m)
		if m.Type == "record-state" {
			id = m.RecordingID
		}
	}
	app.close()
	if _, ok := app.recordings.get(id); !ok {
		t.Fatal("graceful shutdown left a valid active capture incomplete")
	}
}
func TestRecordingCommandsRejectClientPathsAndForeignOwnership(t *testing.T) {
	_, server := testApplication(t)
	for _, body := range []string{`{"type":"record","command":"start","path":"other.jsonl"}`, `{"type":"record","command":"stop","owner":2}`, `{"type":"record","command":"unknown"}`, `{"type":"record","command":"start","command":"stop"}`} {
		c := dial(t, server)
		join(t, c)
		send(t, c, body)
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		rejected := false
		for {
			_, data, err := c.Read(ctx)
			if err != nil {
				break
			}
			var m struct{ Type, Code string }
			_ = json.Unmarshal(data, &m)
			if m.Type == "error" && m.Code == "invalid_message" {
				rejected = true
				break
			}
		}
		cancel()
		c.CloseNow()
		if !rejected {
			t.Fatal("invalid recording command accepted", body)
		}
	}
}
