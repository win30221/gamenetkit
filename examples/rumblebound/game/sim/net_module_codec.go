//go:build !rf_no_codec

package sim

import (
	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/netcore"
)

func init() {
	encoderFactory = func() netcore.FrameEncoder { return codec.New() }
	quantizePosition = func(value float64) float64 { result, _ := codec.Quantize(value, .001); return result }
}
