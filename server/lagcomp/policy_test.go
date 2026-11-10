package lagcomp

import "testing"

func TestBoundedUntrustedRequests(t *testing.T) {
	p := Bounded{6}
	for _, c := range []struct {
		now, want, seen uint64
		ok              bool
	}{{100, 94, 98, true}, {100, 93, 98, false}, {100, 101, 101, false}, {100, 99, 98, false}, {100, 98, 101, false}, {0, 0, 0, true}} {
		tick, ok := p.Target(c.now, c.want, c.seen)
		if ok != c.ok || ok && tick != c.want {
			t.Fatal(c, tick, ok)
		}
	}
}
