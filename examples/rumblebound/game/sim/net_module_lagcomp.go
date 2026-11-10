//go:build !rf_no_lagcomp

package sim

import (
	"github.com/win30221/gamenetkit/server/lagcomp"
	"github.com/win30221/gamenetkit/server/netcore"
	"time"
)

// This is a gameplay fairness window, expressed in time so a simulation-rate
// change cannot silently shorten or lengthen the permitted historical query.
const maxMeleeRewind = 200 * time.Millisecond

func meleeRewindTicks(tickRate int) uint64 {
	if tickRate <= 0 {
		return 0
	}
	// Round down: a whole historical tick may never exceed the time budget.
	// Split the product to avoid overflow even for an invalid enormous rate;
	// maxMeleeRewind is below a second, so both terms remain bounded by rate.
	rate, second, window := uint64(tickRate), uint64(time.Second), uint64(maxMeleeRewind)
	return rate/second*window + rate%second*window/second
}

func init() {
	rewindFactory = func(tickRate int) netcore.RewindPolicy {
		return lagcomp.Bounded{MaxTicks: meleeRewindTicks(tickRate)}
	}
}
