package history

import "testing"

func TestBoundsFollowReplacementEvictionAndClear(t *testing.T) {
	b := New[int](2, func(value int) int { return value })
	if _, _, ok := b.Bounds(); ok {
		t.Fatal("empty history has bounds")
	}
	b.Add(5, 1)
	b.Add(8, 2)
	b.Add(8, 3)
	first, last, ok := b.Bounds()
	if !ok || first != 5 || last != 8 {
		t.Fatal(first, last, ok)
	}
	b.Add(11, 4)
	first, last, ok = b.Bounds()
	if !ok || first != 8 || last != 11 {
		t.Fatal("eviction bounds", first, last)
	}
	b.Add(7, 0)
	first, last, _ = b.Bounds()
	if first != 8 || last != 11 {
		t.Fatal("stale write changed bounds")
	}
	b.Clear()
	if _, _, ok := b.Bounds(); ok {
		t.Fatal("clear retained bounds")
	}
}
