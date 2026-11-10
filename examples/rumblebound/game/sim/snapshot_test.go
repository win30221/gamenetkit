package sim

import (
	"bytes"
	"encoding/json"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"testing"
	"time"
)

func TestPublicationProjectionIsolation(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X = 1.23456
	b.State.X = 30
	base := w.worldSnapshot()
	before, _ := json.Marshal(base)
	a.Net = &protocol.NetOptions{Quantize: quantizePosition != nil}
	if interestFactory != nil {
		a.Interest = interestFactory(4)
	}
	projected := w.recipientSnapshot(base, a, "snapshot")
	other := w.recipientSnapshot(base, b, "snapshot")
	after, _ := json.Marshal(base)
	if !bytes.Equal(before, after) {
		t.Fatal("recipient projection mutated shared world view")
	}
	expected := w.Snapshot(b, "snapshot")
	got, _ := json.Marshal(other)
	want, _ := json.Marshal(expected)
	if !bytes.Equal(got, want) {
		t.Fatal("recipient output differs from independent snapshot")
	}
	if a.Interest != nil && len(projected.Players) >= len(other.Players) {
		t.Fatal("AOI fixture did not filter")
	}
	w.Seats["a"].State.X = 8
	fresh := w.worldSnapshot()
	if fresh.Players[0].X == base.Players[0].X {
		t.Fatal("same tick publication reused stale world")
	}
	preserved, _ := json.Marshal(base)
	if !bytes.Equal(before, preserved) {
		t.Fatal("world mutation changed queued view")
	}
}

func BenchmarkPublicationViews(b *testing.B) {
	// Setup uses the same domain admission as the snapshot ownership tests.
	w := New()
	for _, id := range []string{"a", "b", "c", "d", "e", "f", "g", "h"} {
		if _, err := w.Attach(id, false); err != nil {
			b.Fatal(err)
		}
	}
	for _, shared := range []bool{false, true} {
		name := "independent"
		if shared {
			name = "shared"
		}
		b.Run(name, func(b *testing.B) {
			b.ReportAllocs()
			for i := 0; i < b.N; i++ {
				var base protocol.Server
				if shared {
					base = w.worldSnapshot()
				}
				for _, seat := range w.Seats {
					if shared {
						_ = w.recipientSnapshot(base, seat, "snapshot")
					} else {
						_ = w.Snapshot(seat, "snapshot")
					}
				}
			}
		})
	}
}

func TestWireSnapshotsCannotMutateBattleStateOrEarlierSnapshots(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	w.Projectiles = []shot{{State: ProjectileState{ID: "b1", OwnerID: a.State.PlayerID, TeamID: "t1", X: 1, DX: 1, ExpiresTick: 30}}}
	w.Result = &BattleResult{Outcome: "win", Reason: "score", WinnerTeamID: "t1", EndedTick: 10}
	first := w.Snapshot(a, "result")
	second := w.Snapshot(a, "result")
	first.Players[0].HP = -1
	first.Projectiles[0].X = 99
	first.Match.Result.Reason = "changed"
	first.Match.Scores[0].ScoreTicks = 999
	if a.State.HP <= 0 || w.Projectiles[0].State.X != 1 || w.Result.Reason != "score" || w.ScoreTicks["t1"] != 0 {
		t.Fatal("wire projection retained mutable domain references")
	}
	if second.Players[0].HP <= 0 || second.Projectiles[0].X != 1 || second.Match.Result.Reason != "score" || second.Match.Scores[0].ScoreTicks != 0 {
		t.Fatal("two snapshots share mutable projection data")
	}
	w.Seats["a"].State.HP = 1
	w.Projectiles[0].State.X = 5
	w.Result.Reason = "later"
	if second.Players[0].HP == 1 || second.Projectiles[0].X != 1 || second.Match.Result.Reason != "score" {
		t.Fatal("queued wire snapshot changed with the live battle")
	}
}

func TestEngineMetricsClassifyEveryTickAndQueuePressure(t *testing.T) {
	e := &Engine{commands: make(chan command, 4)}
	for _, elapsed := range []time.Duration{
		250 * time.Microsecond, 500 * time.Microsecond, time.Millisecond,
		2 * time.Millisecond, 5 * time.Millisecond, 10 * time.Millisecond,
		25 * time.Millisecond, 40 * time.Millisecond,
	} {
		e.recordTick(elapsed, time.Second/30)
	}
	e.commands <- command{}
	e.DroppedInputs.Add(2)
	m := e.Metrics()
	if m.TickCount != 8 || m.TickNanos != (40*time.Millisecond).Nanoseconds() || m.Overruns != 1 || m.DroppedInputs != 2 || m.QueueDepth != 1 || m.QueueCapacity != 4 {
		t.Fatalf("incomplete engine diagnostics: %+v", m)
	}
	for bucket, count := range m.TickHistogram {
		if count != 1 {
			t.Fatalf("histogram bucket %d count=%d", bucket, count)
		}
	}
}
