package netcore

import "encoding/json"

// Replay data is application-owned. The SDK persists a versioned, bounded
// journal; it never interprets simulation definitions or input payloads.
type ReplayHeader struct {
	Version     int             `json:"version"`
	Application string          `json:"application"`
	Model       string          `json:"model"`
	Definition  json.RawMessage `json:"definition"`
	InitialHash string          `json:"initialHash"`
}

type ReplayEntry struct {
	Sequence  uint64          `json:"sequence"`
	Kind      string          `json:"kind"`
	Tick      uint64          `json:"tick"`
	TimeNanos int64           `json:"timeNanos"`
	Payload   json.RawMessage `json:"payload,omitempty"`
	StateHash string          `json:"stateHash"`
}

// Append must copy its data and must not block the simulation on disk I/O.
// Failure makes the entire recording incomplete, never a successful prefix.
type ReplaySink interface {
	Append(ReplayEntry) bool
	Err() error
	Close() error
	Path() string
}

type ReplaySummary struct {
	Entries  uint64 `json:"entries"`
	Steps    uint64 `json:"steps"`
	LastTick uint64 `json:"lastTick"`
	Digest   string `json:"digest"`
}
