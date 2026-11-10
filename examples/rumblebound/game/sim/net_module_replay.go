//go:build !rf_no_replay

package sim

import (
	"crypto/rand"
	"encoding/hex"
	"github.com/win30221/gamenetkit/server/netcore"
	"github.com/win30221/gamenetkit/server/replay"
	"path/filepath"
)

func init() {
	openReplay = func(dir string, h netcore.ReplayHeader) (netcore.ReplaySink, error) {
		var id [16]byte
		if _, e := rand.Read(id[:]); e != nil {
			return nil, e
		}
		return replay.New(filepath.Join(dir, hex.EncodeToString(id[:])+".replay.jsonl"), h, replay.DefaultLimits())
	}
	readReplay = func(path string, start func(netcore.ReplayHeader) error, apply func(netcore.ReplayEntry) error) (netcore.ReplaySummary, error) {
		return replay.VerifyFile(path, replay.DefaultLimits(), start, apply)
	}
}
