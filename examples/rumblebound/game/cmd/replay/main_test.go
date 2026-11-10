package main

import (
	"bytes"
	"encoding/json"
	"path/filepath"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func TestRunRequiresRecording(t *testing.T) {
	var stdout, stderr bytes.Buffer
	if code := run(nil, &stdout, &stderr); code != 2 || stdout.Len() != 0 || stderr.Len() == 0 {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout.String(), stderr.String())
	}
}

func TestRunReportsInvalidRecordingAsFailure(t *testing.T) {
	var stdout, stderr bytes.Buffer
	file := filepath.Join(t.TempDir(), "missing.replay.jsonl")
	if code := run([]string{"-file", file}, &stdout, &stderr); code != 1 {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout.String(), stderr.String())
	}
	var report sim.ReplayReport
	if err := json.Unmarshal(stdout.Bytes(), &report); err != nil || report.Status != "FAIL" || report.Error == "" {
		t.Fatalf("report=%+v error=%v", report, err)
	}
}
