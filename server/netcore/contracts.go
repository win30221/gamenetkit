// Package netcore contains optional server-module contracts without gameplay types.
package netcore

type Bracket[T any] struct {
	Before, After         T
	BeforeTick, AfterTick uint64
}
type History[T any] interface {
	Add(uint64, T) bool
	At(uint64) (Bracket[T], bool)
	Len() int
	Clear()
}
type InterestObject struct {
	ID     string
	X, Z   float64
	Always bool
}
type Interest interface {
	Select(x, z float64, objects []InterestObject) map[string]bool
}
type RewindPolicy interface {
	Target(now, requested, observed uint64) (uint64, bool)
}
type EncodedFrame struct {
	ID   uint64 `json:"id"`
	Base uint64 `json:"base"`
	Data string `json:"data"`
}
type FrameEncoder interface {
	Encode([]byte, uint64) (EncodedFrame, error)
	Reset()
}
