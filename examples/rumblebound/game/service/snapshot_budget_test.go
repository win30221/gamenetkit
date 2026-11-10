package gateway

import (
	"bytes"
	"encoding/json"
	"errors"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"strings"
	"testing"
)

// Keep the previous implementation as a wire-equivalence oracle and benchmark.
func linearSnapshotBudget(message protocol.Server) ([]byte, error) {
	for {
		data, err := json.Marshal(message)
		if err != nil || len(data) <= protocol.MaxSnapshot {
			return data, err
		}
		if len(message.ProjectileEnds) > 0 {
			message.ProjectileEnds = message.ProjectileEnds[1:]
		} else if len(message.CombatEvents) > 0 {
			message.CombatEvents = message.CombatEvents[1:]
		} else if len(message.GroundImpacts) > 0 {
			message.GroundImpacts = message.GroundImpacts[1:]
		} else {
			return nil, errors.New("snapshot_too_large")
		}
	}
}
func budgetFixture(count int) protocol.Server {
	m := protocol.Server{Type: "snapshot", Players: []protocol.Player{{PlayerID: "self", HP: 100}}}
	for i := 0; i < count; i++ {
		id := strings.Repeat("雪", 30)
		m.ProjectileEnds = append(m.ProjectileEnds, protocol.ProjectileEnd{ID: id, Tick: uint64(i)})
		m.CombatEvents = append(m.CombatEvents, protocol.CombatEvent{ID: id, Tick: uint64(i)})
		m.GroundImpacts = append(m.GroundImpacts, protocol.GroundImpact{ID: id, Tick: uint64(i)})
	}
	return m
}
func TestSnapshotBudgetMatchesLinear(t *testing.T) {
	for _, count := range []int{0, 1, 20, 30, 40, 60, 100, 200} {
		m := budgetFixture(count)
		for _, padding := range []int{0, 8000, 16000, 20000} {
			m.MatchID = strings.Repeat("a", padding)
			got, err := boundedSnapshot(m)
			want, oldErr := linearSnapshotBudget(m)
			if (err == nil) != (oldErr == nil) || !bytes.Equal(got, want) {
				t.Fatalf("wire differs count=%d padding=%d: %v %v", count, padding, err, oldErr)
			}
		}
	}
}
func BenchmarkSnapshotBudget(b *testing.B) {
	m := budgetFixture(100)
	for _, test := range []struct {
		name string
		run  func(protocol.Server) ([]byte, error)
	}{{"linear", linearSnapshotBudget}, {"search", boundedSnapshot}} {
		b.Run(test.name, func(b *testing.B) {
			b.ReportAllocs()
			for i := 0; i < b.N; i++ {
				if _, err := test.run(m); err != nil {
					b.Fatal(err)
				}
			}
		})
	}
}
