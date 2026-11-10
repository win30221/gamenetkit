package replay

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"

	"github.com/win30221/gamenetkit/server/netcore"
)

func fixtureHeader() netcore.ReplayHeader {
	return netcore.ReplayHeader{Version: Version, Application: "independent-test", Model: "counter-v1", Definition: json.RawMessage(`{"initial":0}`), InitialHash: strings.Repeat("a", 64)}
}
func fixtureEntry() netcore.ReplayEntry {
	return netcore.ReplayEntry{Kind: "step", Tick: 1, TimeNanos: 17, Payload: json.RawMessage(`{"input":4}`), StateHash: strings.Repeat("b", 64)}
}
func verifyNoop(path string) (netcore.ReplaySummary, error) {
	return VerifyFile(path, DefaultLimits(), func(netcore.ReplayHeader) error { return nil }, func(netcore.ReplayEntry) error { return nil })
}
func TestJournalRoundTripOwnershipAndConcurrentClose(t *testing.T) {
	path := filepath.Join(t.TempDir(), "run.jsonl")
	h := fixtureHeader()
	j, e := New(path, h, DefaultLimits())
	if e != nil {
		t.Fatal(e)
	}
	h.Definition[2] = 'X'
	entry := fixtureEntry()
	if !j.Append(entry) {
		t.Fatal(j.Err())
	}
	entry.Payload[9] = '9'
	var wg sync.WaitGroup
	for i := 0; i < 3; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if err := j.Close(); err != nil {
				t.Error(err)
			}
		}()
	}
	wg.Wait()
	count := 0
	summary, e := VerifyFile(path, DefaultLimits(), func(h netcore.ReplayHeader) error {
		if string(h.Definition) != `{"initial":0}` {
			t.Fatal("retained mutable header")
		}
		return nil
	}, func(e netcore.ReplayEntry) error {
		count++
		if string(e.Payload) != `{"input":4}` || e.Sequence != 1 || e.TimeNanos != 17 {
			t.Fatalf("entry %+v", e)
		}
		return nil
	})
	if e != nil || summary.Entries != 1 || summary.Steps != 1 || count != 1 {
		t.Fatalf("%+v %v", summary, e)
	}
	if _, e = os.Stat(path + ".partial"); !os.IsNotExist(e) {
		t.Fatal("published recording left partial")
	}
	if j.Append(fixtureEntry()) {
		t.Fatal("appended after close")
	}
}

func TestJournalSummaryTracksMixedEventsAndLastAppendedTick(t *testing.T) {
	path := filepath.Join(t.TempDir(), "mixed.jsonl")
	j, err := New(path, fixtureHeader(), DefaultLimits())
	if err != nil {
		t.Fatal(err)
	}
	for _, event := range []struct {
		tick uint64
		kind string
	}{{4, "step"}, {4, "action"}, {9, "step"}, {12, "input"}} {
		entry := fixtureEntry()
		entry.Tick, entry.Kind = event.tick, event.kind
		if !j.Append(entry) {
			t.Fatal(j.Err())
		}
		entry.Tick, entry.Kind = 999, "changed"
		entry.Payload[9] = '9'
	}
	if err := j.Close(); err != nil {
		t.Fatal(err)
	}
	summary, err := verifyNoop(path)
	if err != nil || summary.Entries != 4 || summary.Steps != 2 || summary.LastTick != 12 {
		t.Fatalf("summary=%+v error=%v", summary, err)
	}
}
func TestJournalRejectsCorruptionTruncationAndTrailingData(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "valid.jsonl")
	j, e := New(path, fixtureHeader(), DefaultLimits())
	if e != nil {
		t.Fatal(e)
	}
	j.Append(fixtureEntry())
	if e = j.Close(); e != nil {
		t.Fatal(e)
	}
	good, e := os.ReadFile(path)
	if e != nil {
		t.Fatal(e)
	}
	lines := strings.SplitAfter(string(good), "\n")
	cases := map[string]string{
		"truncated":   strings.Join(lines[:2], ""),
		"tampered":    strings.Replace(string(good), `"input":4`, `"input":5`, 1),
		"trailing":    string(good) + lines[1],
		"new-version": strings.Replace(string(good), `"version":1`, `"version":2`, 1),
		"duplicate":   lines[0] + lines[1] + lines[1] + lines[2],
		"unknown":     strings.Replace(string(good), `"type":"event"`, `"type":"unknown"`, 1),
	}
	for name, data := range cases {
		t.Run(name, func(t *testing.T) {
			file := filepath.Join(root, name+".jsonl")
			if e := os.WriteFile(file, []byte(data), 0600); e != nil {
				t.Fatal(e)
			}
			if _, e := verifyNoop(file); e == nil {
				t.Fatal("accepted damaged recording")
			}
		})
	}
	partial := path + ".partial"
	_ = os.WriteFile(partial, good, 0600)
	if _, e := verifyNoop(partial); e == nil {
		t.Fatal("accepted partial file")
	}
}
func TestJournalLimitsFailWholeRecordingAndNeverOverwrite(t *testing.T) {
	path := filepath.Join(t.TempDir(), "limited.jsonl")
	limits := DefaultLimits()
	limits.Entries = 1
	j, e := New(path, fixtureHeader(), limits)
	if e != nil {
		t.Fatal(e)
	}
	if !j.Append(fixtureEntry()) || j.Append(fixtureEntry()) {
		t.Fatal("entry budget")
	}
	if j.Close() == nil {
		t.Fatal("overflow reported success")
	}
	if _, e = os.Stat(path); !os.IsNotExist(e) {
		t.Fatal("published incomplete prefix")
	}
	path = filepath.Join(t.TempDir(), "sentinel.jsonl")
	j, e = New(path, fixtureHeader(), DefaultLimits())
	if e != nil {
		t.Fatal(e)
	}
	if e = os.WriteFile(path, []byte("keep"), 0600); e != nil {
		t.Fatal(e)
	}
	if j.Close() == nil {
		t.Fatal("clobbered destination")
	}
	content, _ := os.ReadFile(path)
	if string(content) != "keep" {
		t.Fatal("destroyed existing file")
	}
	if _, e = New(path, fixtureHeader(), DefaultLimits()); e == nil {
		t.Fatal("opened existing destination")
	}
}
func TestJournalSlowStorageQueueIsBounded(t *testing.T) {
	// Simulate a writer that has stopped making progress. No disk latency is
	// needed to prove the owner's enqueue has a fixed bound and fails closed.
	j := &Journal{limits: DefaultLimits(), queue: make(chan queuedRecord, 1)}
	if !j.Append(fixtureEntry()) || j.Append(fixtureEntry()) || j.Err() == nil {
		t.Fatal("queue did not fail closed")
	}
	if len(j.queue) != 1 {
		t.Fatal("queue exceeded budget")
	}
}

func TestJournalStorageFailureDoesNotPublish(t *testing.T) {
	path := filepath.Join(t.TempDir(), "failed.jsonl")
	if e := os.WriteFile(path+".partial", nil, 0600); e != nil {
		t.Fatal(e)
	}
	readonly, e := os.Open(path + ".partial")
	if e != nil {
		t.Fatal(e)
	}
	j := &Journal{path: path, partial: path + ".partial", limits: DefaultLimits(), queue: make(chan queuedRecord), done: make(chan struct{})}
	header, e := line(record{Type: "header", Header: func() *netcore.ReplayHeader { h := fixtureHeader(); return &h }()}, j.limits.RecordBytes)
	if e != nil {
		t.Fatal(e)
	}
	go j.write(readonly, header)
	if j.Close() == nil {
		t.Fatal("write failure reported success")
	}
	if _, e = os.Stat(path); !os.IsNotExist(e) {
		t.Fatal("published failed write")
	}
}

func TestJournalRejectsOversizedHeaderAndReadBudget(t *testing.T) {
	path := filepath.Join(t.TempDir(), "large.jsonl")
	l := DefaultLimits()
	l.RecordBytes = 1024
	l.FileBytes = 1024
	if _, e := New(path, fixtureHeader(), l); e == nil {
		t.Fatal("header/footer exceeded file budget")
	}
	if e := os.WriteFile(path, []byte(strings.Repeat("x", 2048)), 0600); e != nil {
		t.Fatal(e)
	}
	if _, e := VerifyFile(path, l, func(netcore.ReplayHeader) error { return nil }, func(netcore.ReplayEntry) error { return nil }); e == nil {
		t.Fatal("accepted file larger than configured limit")
	}
}
