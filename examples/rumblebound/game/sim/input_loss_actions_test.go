package sim

import "testing"

// Test-only transport model: one input packet per simulation tick, an envelope
// containing up to two prior immutable frames followed by the current frame,
// and identical deterministic whole-packet loss for both variants. It bypasses
// JSON/admission/engine queues and models ACK delay after real publication;
// it does not claim to measure wire cost, render timing, or prediction quality.
type actionLossCase struct {
	name                 string
	burst, ackDelay      int
	ackDrops             int
	duplicate, reordered bool
}

type actionLossResult struct {
	execution, acknowledged, attempts int
	code                              string
	shots                             uint64
	damage                            int
}

func actionLossRun(t *testing.T, test actionLossCase, history bool) actionLossResult {
	t.Helper()
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = -3, -4
	b.State.X, b.State.Z = 3, -4
	// Drain the start transition and expire protection in the real arena.
	for range 60 {
		for _, s := range w.Seats {
			select {
			case <-s.Updates:
			default:
			}
		}
		w.Step()
	}
	for _, s := range w.Seats {
		select {
		case <-s.Updates:
		default:
		}
	}
	initialShots, initialHP := w.NextProjectileID, b.State.HP
	result := actionLossResult{execution: -1, acknowledged: -1}
	type ackDelivery struct {
		due int
		id  uint64
	}
	var replies []ackDelivery
	var frames []InputIntent
	var held []InputIntent
	clientAck := uint64(0)
	ackDrops := test.ackDrops
	deliver := func(packet []InputIntent) {
		for _, frame := range packet {
			w.Input("a", a.Generation, frame)
		}
	}
	for tick := 0; tick < 50; tick++ {
		for len(replies) > 0 && replies[0].due <= tick {
			if replies[0].id > clientAck {
				clientAck = replies[0].id
			}
			replies = replies[1:]
			if clientAck == 1 && result.acknowledged < 0 {
				result.acknowledged = tick
			}
		}
		frame := InputIntent{Generation: a.Generation, Seq: uint64(tick + 1)}
		if clientAck < 1 {
			// BattleSession repeats the head ID and refreshes actionTick each send.
			frame.Action, frame.ActionID, frame.ActionTick, frame.AimX = "fire", 1, w.Tick, 1
			result.attempts++
		}
		packet := []InputIntent{frame}
		if history {
			start := max(0, len(frames)-2)
			packet = append(append([]InputIntent(nil), frames[start:]...), frame)
		}
		frames = append(frames, frame)
		if tick >= test.burst {
			if test.reordered && tick == 0 {
				held = packet
			} else {
				deliver(packet)
				if test.duplicate {
					deliver(packet)
				}
				// Current packet arrives before the delayed earlier packet.
				if tick == 1 && held != nil {
					deliver(held)
				}
			}
		}
		w.Step()
		if a.ActionAck == 1 && result.execution < 0 {
			result.execution, result.code = tick+1, a.ActionCode
		}
		for _, s := range w.Seats {
			select {
			case snapshot := <-s.Updates:
				if s == a && snapshot.ActionAck == 1 {
					if ackDrops > 0 {
						ackDrops--
					} else {
						replies = append(replies, ackDelivery{tick + 1 + test.ackDelay, snapshot.ActionAck})
					}
				}
			default:
			}
		}
	}
	result.shots, result.damage = w.NextProjectileID-initialShots, initialHP-b.State.HP
	return result
}

func TestInputLossActionHistoryComparison(t *testing.T) {
	for _, test := range []actionLossCase{
		{name: "no_loss"},
		{name: "first_action_packet_lost", burst: 1},
		{name: "lost_and_delayed_ack", ackDrops: 2, ackDelay: 5},
		{name: "seven_tick_burst", burst: 7},
		{name: "eight_tick_burst", burst: 8},
		{name: "nine_tick_burst", burst: 9},
		{name: "duplicate_packets", duplicate: true},
		{name: "reordered_packets", reordered: true},
	} {
		t.Run(test.name, func(t *testing.T) {
			baseline := actionLossRun(t, test, false)
			candidate := actionLossRun(t, test, true)
			t.Logf("baseline=%+v history2=%+v (latency in simulation ticks)", baseline, candidate)
			for _, result := range []actionLossResult{baseline, candidate} {
				if result.execution < 1 || result.acknowledged < result.execution || result.code != "fired" || result.shots != 1 || result.damage <= 0 {
					t.Fatalf("fire did not execute exactly once and resolve through a snapshot: %+v", result)
				}
			}
			if baseline != candidate {
				t.Fatalf("action retry outcomes differ: baseline=%+v history=%+v", baseline, candidate)
			}
			want := test.burst + 1
			if test.reordered {
				want = 2
			}
			if baseline.execution != want {
				t.Fatalf("execution latency=%d want=%d", baseline.execution, want)
			}
			if test.ackDrops > 0 && baseline.attempts <= baseline.execution {
				t.Fatal("ACK loss did not exercise retransmission after execution")
			}
		})
	}
}
