package history

import "testing"

func TestRingMatchesOrderedHistory(t *testing.T) {
	b := New(7, func(v []int) []int { return append([]int(nil), v...) })
	for tick := uint64(0); tick < 200; tick += 2 {
		b.Add(tick, []int{int(tick)})
		first := uint64(0)
		if tick > 12 {
			first = tick - 12
		}
		for query := first; query <= tick; query++ {
			value, ok := b.At(query)
			if !ok || value.BeforeTick != query/2*2 || value.AfterTick != (query+1)/2*2 {
				t.Fatalf("query %d: %+v", query, value)
			}
			value.Before[0] = -1
			again, _ := b.At(query)
			if again.Before[0] < 0 {
				t.Fatal("mutable query")
			}
		}
		b.Add(tick, []int{123})
		q, _ := b.At(tick)
		if q.Before[0] != 123 {
			t.Fatal("same tick replacement")
		}
		// Restore the reference value for the next iteration.
		b.Add(tick, []int{int(tick)})
	}
	b.Clear()
	b.Add(0, []int{1})
	if b.Len() != 1 {
		t.Fatal("reset")
	}
}

func BenchmarkHistoryRing(b *testing.B) {
	h := New(4096, func(v int) int { return v })
	for i := 0; i < 4096; i++ {
		h.Add(uint64(i), i)
	}
	b.ReportAllocs()
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		h.Add(uint64(4096+i), i)
		h.At(uint64(2048 + i))
	}
}
