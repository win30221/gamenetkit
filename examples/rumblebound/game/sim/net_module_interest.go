//go:build !rf_no_interest

package sim

import (
	"github.com/win30221/gamenetkit/server/interest"
	"github.com/win30221/gamenetkit/server/netcore"
)

func init() {
	interestFactory = func(radius float64) netcore.Interest { return interest.New(radius, 2, 1024) }
}
