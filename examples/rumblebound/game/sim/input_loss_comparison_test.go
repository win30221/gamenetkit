package sim

import (
	"encoding/json"
	"math"
	"math/rand/v2"
	"reflect"
	"sort"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
)

// This experiment deliberately leaves the production protocol unchanged. The
// candidate carries the previous two inputs in the SAME datagram, oldest first.
// All entries arrive before one simulation step, as in Engine.run. It does not
// invent extra simulation steps or independently deliver the historical entries.
// The 60 Hz sender model mirrors BattleSession's 30 Hz heartbeat / 60 Hz change
// limit, without probes, render-frame jitter, backpressure or client prediction.
type lossPacket struct {
	frame, due int
	drop       bool
	input      protocol.Client
}

type lossProfile struct {
	name       string
	percent    int
	burstTicks int
	jitter     bool
	duplicate  bool
}

type lossSample struct {
	x, z, dx, dz float64
	ack          uint64
}

func lossDirection(frame int, pattern string) (float64, float64) {
	f := frame % 60
	switch pattern {
	case "short-tap":
		if f == 8 || f == 9 {
			return 1, 0
		}
	case "hold-release":
		if f >= 8 && f < 30 {
			return 1, 0
		}
	case "zigzag":
		if f >= 8 && f < 30 {
			if (f/5)%2 == 0 {
				return -1, 0
			}
			return 1, 0
		}
	case "diagonal-turn":
		if f >= 8 && f < 18 {
			return 1, 1
		}
		if f >= 18 && f < 30 {
			return -1, 1
		}
	}
	return 0, 0
}

func lossTrace(pattern string, p lossProfile, seed uint64) []lossPacket {
	rng := rand.New(rand.NewPCG(seed, 0x1a2b3c))
	var trace []lossPacket
	lastX, lastZ, next := 0.0, 0.0, 0
	for frame := 0; frame < 600; frame++ { // Ten seconds; fixed frames, no wall clock.
		x, z := lossDirection(frame, pattern)
		if frame < next && x == lastX && z == lastZ {
			continue
		}
		lastX, lastZ, next = x, z, frame+2
		// Loss and jitter draws are consumed even if the packet is dropped.
		// Both variants replay this immutable trace rather than reseeding a
		// conditioner whose random consumption depends on accepted messages.
		drop, jitter := rng.IntN(100) < p.percent, rng.IntN(7)
		if p.burstTicks > 0 {
			f := frame % 120
			drop = drop || (f >= 28 && f < 28+2*p.burstTicks)
		}
		due := frame + 3 // 50 ms upstream delay.
		if p.jitter {
			due += jitter // 0..100 ms extra; intentionally permits reordering.
		}
		trace = append(trace, lossPacket{frame, due, drop, protocol.Client{
			Type: "input", Protocol: 5, Content: "g2-targeted-combat-2", MatchID: MatchID,
			Generation: 1, Seq: uint64(len(trace) + 1), X: x, Z: z,
		}})
	}
	return trace
}

func lossRun(t *testing.T, trace []lossPacket, history, phase int, clearLoss, duplicate bool) ([]lossSample, int) {
	t.Helper()
	w := New() // Isolate movement from cover, attacks, scoring and other players.
	// Avoid wall collisions masking accumulated authority error on long holds.
	w.Rules.ArenaHalfSize = 1000
	s, err := w.Attach("loss-player", false)
	if err != nil {
		t.Fatal(err)
	}
	type arrival struct {
		due, index int
		inputs     []protocol.Client
	}
	var arrivals []arrival
	bytes := 0
	for i, packet := range trace {
		inputs := make([]protocol.Client, 0, history+1)
		for j := max(0, i-history); j <= i; j++ {
			inputs = append(inputs, trace[j].input)
		}
		// Measure a concrete TEST encoding, not a claimed Unity wire size.
		var payload []byte
		if history == 0 {
			payload, err = json.Marshal(packet.input)
		} else {
			payload, err = json.Marshal(struct {
				Inputs []protocol.Client `json:"inputs"`
			}{inputs})
		}
		if err != nil {
			t.Fatal(err)
		}
		bytes += len(payload) // Sender bytes include packets lost in transit.
		if packet.drop && !clearLoss {
			continue
		}
		arrivals = append(arrivals, arrival{packet.due, i, inputs})
		if duplicate && i%7 == 0 {
			// A network duplicate; not an extra transmission by the sender.
			arrivals = append(arrivals, arrival{packet.due + 5, i, inputs})
		}
	}
	sort.SliceStable(arrivals, func(i, j int) bool {
		if arrivals[i].due != arrivals[j].due {
			return arrivals[i].due < arrivals[j].due
		}
		return arrivals[i].index < arrivals[j].index
	})
	var samples []lossSample
	cursor := 0
	for frame := 0; frame < 630; frame++ { // Include a drain / input-timeout tail.
		for cursor < len(arrivals) && arrivals[cursor].due <= frame {
			for _, input := range arrivals[cursor].inputs {
				w.Input("loss-player", s.Generation, InputIntent{
					Generation: input.Generation, Seq: input.Seq, X: input.X, Z: input.Z,
				})
			}
			cursor++
		}
		if frame%2 == phase {
			w.Step()
			samples = append(samples, lossSample{s.State.X, s.State.Z, s.DX, s.DZ, s.State.Ack})
		}
	}
	return samples, bytes
}

// TestInputLossComparison is both a repeatable A/B experiment and a guard on its
// conclusion. If held-state semantics change, parity failing requires revisiting
// the decision rather than shipping a stale "redundancy is useless" assumption.
func TestInputLossComparison(t *testing.T) {
	profiles := []lossProfile{
		{name: "clean"},
		{name: "loss-1", percent: 1},
		{name: "loss-5", percent: 5},
		{name: "loss-10", percent: 10},
		{name: "loss-20", percent: 20},
		{name: "burst-3", burstTicks: 3},
		{name: "burst-7", burstTicks: 7},
		{name: "burst-8", burstTicks: 8},
		{name: "burst-9", burstTicks: 9},
		{name: "loss-10-reorder-duplicate", percent: 10, jitter: true, duplicate: true},
	}
	for _, profile := range profiles {
		t.Run(profile.name, func(t *testing.T) {
			var distanceSum, maxDistance float64
			var samples, changed, baselineBytes, candidateBytes, comparisons int
			seeds := uint64(1) // Do not inflate the count with identical deterministic traces.
			if profile.percent > 0 || profile.jitter {
				seeds = 32
			}
			for _, pattern := range []string{"hold-release", "short-tap", "zigzag", "diagonal-turn"} {
				for seed := uint64(1); seed <= seeds; seed++ {
					trace := lossTrace(pattern, profile, seed)
					for phase := 0; phase < 2; phase++ {
						baseline, b := lossRun(t, trace, 0, phase, false, profile.duplicate)
						candidate, c := lossRun(t, trace, 2, phase, false, profile.duplicate)
						ideal, _ := lossRun(t, trace, 0, phase, true, profile.duplicate)
						if !reflect.DeepEqual(baseline, candidate) {
							t.Fatalf("A/B diverged: pattern=%s seed=%d phase=%d; revisit decision", pattern, seed, phase)
						}
						for i, value := range baseline {
							d := math.Hypot(value.x-ideal[i].x, value.z-ideal[i].z)
							distanceSum += d
							maxDistance = max(maxDistance, d)
							if value.dx != ideal[i].dx || value.dz != ideal[i].dz {
								changed++
							}
							samples++
						}
						baselineBytes += b
						candidateBytes += c
						comparisons++
					}
				}
			}
			if profile.name != "clean" && changed == 0 {
				t.Fatal("loss had no observable movement effect; experiment is vacuous")
			}
			t.Logf("pairs=%d samples=%d A/B-identical=true mean-authority-error=%.6f max=%.6f held-mismatch=%.3f%% test-payload-ratio=%.3fx",
				comparisons, samples, distanceSum/float64(samples), maxDistance, 100*float64(changed)/float64(samples), float64(candidateBytes)/float64(baselineBytes))
		})
	}
}

func TestInputLossTransitions(t *testing.T) {
	for _, kind := range []string{"press", "release", "short-tap"} {
		t.Run(kind, func(t *testing.T) {
			// One input per simulation tick, zero transport delay. Seq 2 is
			// the press (or release). Only that packet is lost; seq 3 follows.
			xs := []float64{0, 1, 1, 0}
			if kind == "release" {
				xs = []float64{1, 0, 0, 0}
			} else if kind == "short-tap" {
				xs = []float64{0, 1, 0, 0}
			}
			var trace []lossPacket
			for i, x := range xs {
				trace = append(trace, lossPacket{frame: i * 2, due: i * 2, drop: i == 1,
					input: protocol.Client{Generation: 1, Seq: uint64(i + 1), X: x}})
			}
			baseline, _ := lossRun(t, trace, 0, 0, false, false)
			candidate, _ := lossRun(t, trace, 2, 0, false, false)
			ideal, _ := lossRun(t, trace, 0, 0, true, false)
			if !reflect.DeepEqual(baseline, candidate) || baseline[1].x == ideal[1].x {
				t.Fatal("expected measurable loss impact and identical A/B behavior")
			}
			if kind == "short-tap" && (baseline[3].x != 0 || ideal[3].x <= 0) {
				t.Fatal("lost short tap should remain lost under held-state batching")
			}
			if baseline[2].dx != xs[2] {
				t.Fatal("next delivered input did not recover current held state")
			}
			t.Logf("A/B tick-2 displacement error=%.3f; both recover latest held state at tick 3", math.Abs(baseline[1].x-ideal[1].x))
		})
	}
}
