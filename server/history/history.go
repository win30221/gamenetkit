package history

import (
	"github.com/win30221/gamenetkit/server/netcore"
	"sort"
)

type entry[T any] struct {
	tick  uint64
	value T
}
type Buffer[T any] struct {
	entries      []entry[T]
	capacity     int
	clone        func(T) T
	start, count int
}

func New[T any](capacity int, clone func(T) T) *Buffer[T] {
	if capacity < 2 || capacity > 4096 || clone == nil {
		panic("invalid history configuration")
	}
	return &Buffer[T]{capacity: capacity, clone: clone, entries: make([]entry[T], capacity)}
}
func (b *Buffer[T]) entry(index int) entry[T] { return b.entries[(b.start+index)%b.capacity] }
func (b *Buffer[T]) Add(tick uint64, state T) bool {
	n := b.count
	if n > 0 && tick < b.entry(n-1).tick {
		return false
	}
	e := entry[T]{tick, b.clone(state)}
	if n > 0 && tick == b.entry(n-1).tick {
		b.entries[(b.start+n-1)%b.capacity] = e
		return true
	}
	if n == b.capacity {
		b.entries[b.start] = e
		b.start = (b.start + 1) % b.capacity
	} else {
		b.entries[(b.start+n)%b.capacity] = e
		b.count++
	}
	return true
}
func (b *Buffer[T]) At(tick uint64) (netcore.Bracket[T], bool) {
	var out netcore.Bracket[T]
	if b.count == 0 || tick < b.entry(0).tick || tick > b.entry(b.count-1).tick {
		return out, false
	}
	i := sort.Search(b.count, func(i int) bool { return b.entry(i).tick >= tick })
	after := b.entry(i)
	before := after
	if after.tick != tick {
		before = b.entry(i - 1)
	}
	return netcore.Bracket[T]{Before: b.clone(before.value), After: b.clone(after.value), BeforeTick: before.tick, AfterTick: after.tick}, true
}
func (b *Buffer[T]) Len() int { return b.count }

// Bounds reports the actual retained tick range without exposing mutable entries.
func (b *Buffer[T]) Bounds() (first, last uint64, ok bool) {
	if b.count == 0 {
		return 0, 0, false
	}
	return b.entry(0).tick, b.entry(b.count - 1).tick, true
}
func (b *Buffer[T]) Clear() { clear(b.entries); b.start = 0; b.count = 0 }
