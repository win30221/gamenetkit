//go:build !rf_no_history

package sim

import (
	"github.com/win30221/gamenetkit/server/history"
	"github.com/win30221/gamenetkit/server/netcore"
)

func init() {
	historyFactory = func() netcore.History[poseFrame] { return history.New(32, clonePoseFrame) }
}
