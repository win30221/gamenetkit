//go:build !rf_no_lagcomp

package sim

import (
	"math"
	"testing"
)

func TestMeleeRewindWindowUsesTimeAtEveryTickRate(t *testing.T) {
	for _, rate := range []int{-1, 0, 1, 4, 5, 30, 60, 64, 120, 128, math.MaxInt} {
		t.Run(idString(uint64(max(0, rate))), func(t *testing.T) {
			want := uint64(max(0, rate)) / 5 // 200ms is exactly one fifth of a second.
			if got := meleeRewindTicks(rate); got != want {
				t.Fatalf("rate %d: %d ticks, want %d", rate, got, want)
			}
			policy := rewindFactory(rate)
			now := want + 10
			if tick, ok := policy.Target(now, now-want, now); ok != (want > 0) || ok && tick != now-want {
				t.Fatalf("rate %d: boundary tick=%d accepted=%v", rate, tick, ok)
			}
			if _, ok := policy.Target(now, now-want-1, now); ok {
				t.Fatalf("rate %d: one tick beyond 200ms was authorized", rate)
			}
		})
	}
}

func TestMeleeHistoricalHitUsesConfiguredTickRate(t *testing.T) {
	if historyFactory == nil {
		t.Skip("optional history removed")
	}
	for _, rate := range []int{30, 60, 64, 120} {
		for _, beyond := range []bool{false, true} {
			w := meleeWorld(t)
			w.Rules.TickRate = rate
			a, b := w.Seats["a"], w.Seats["b"]
			a.Net = netFixtureOptions()
			w.history = historyFactory()
			age := meleeRewindTicks(rate)
			if beyond {
				age++
			}
			w.Tick = 1000 - age
			b.State.X = 1
			w.recordHistory()
			w.Tick = 1000
			b.State.X = 3
			hp := b.State.HP
			code := w.resolveAction(a, actionIntent{Kind: "fire", ID: 1, Tick: w.Tick, ViewTick: w.Tick - age, ObservedTick: w.Tick, AimX: 1})
			if code != "melee" || (b.State.HP < hp) == beyond {
				t.Fatalf("rate %d age %d beyond=%v: code=%s hp=%d/%d rewind=%d", rate, age, beyond, code, b.State.HP, hp, a.RewindTick)
			}
		}
	}
}
