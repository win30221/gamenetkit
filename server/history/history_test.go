package history

import "testing"

func TestBoundedClonedBrackets(t *testing.T) {
	b := New(2, func(x []int) []int { return append([]int(nil), x...) })
	original := []int{1}
	b.Add(10, original)
	original[0] = 99
	b.Add(12, []int{2})
	q, ok := b.At(11)
	if !ok || q.Before[0] != 1 || q.After[0] != 2 || q.BeforeTick != 10 {
		t.Fatal(q, ok)
	}
	q.Before[0] = 9
	q, _ = b.At(10)
	if q.Before[0] != 1 {
		t.Fatal("query mutated history")
	}
	if b.Add(9, []int{0}) {
		t.Fatal("accepted old tick")
	}
	b.Add(14, []int{3})
	if _, ok = b.At(10); ok {
		t.Fatal("capacity")
	}
	if _, ok = b.At(15); ok {
		t.Fatal("extrapolated history")
	}
	b.Clear()
	if b.Len() != 0 {
		t.Fatal("clear")
	}
}
