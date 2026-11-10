package gateway

import "github.com/win30221/gamenetkit/examples/rumblebound/game/sim"

func (s *Server) health() map[string]any {
	s.matchMu.Lock()
	s.refreshFinishedRoomsLocked()
	active, retained, initializing := 0, 0, 0
	var engines []*sim.Engine
	for _, match := range s.matches {
		if match.finished {
			retained++
			continue
		}
		active++
		if match.engine == nil {
			initializing++
		} else {
			engines = append(engines, match.engine)
		}
	}
	s.matchMu.Unlock()
	var private sim.EngineMetrics
	for _, engine := range engines {
		metrics := engine.Metrics()
		private.TickCount += metrics.TickCount
		if metrics.TickNanos > private.TickNanos {
			private.TickNanos = metrics.TickNanos
		}
		private.Overruns += metrics.Overruns
		private.DroppedInputs += metrics.DroppedInputs
		private.QueueDepth += metrics.QueueDepth
		private.QueueCapacity += metrics.QueueCapacity
		for i, count := range metrics.TickHistogram {
			private.TickHistogram[i] += count
		}
	}
	lab := s.Engine.Metrics()
	return map[string]any{
		"status": "ok", "tickNanos": lab.TickNanos, "overruns": lab.Overruns, "droppedInputs": lab.DroppedInputs,
		"activeMatches": active, "retainedMatches": retained, "initializingMatches": initializing,
		"rooms": s.Lobby.Count(), "connections": len(s.Slots), "pendingHandshakes": len(s.handshakes),
		"capacity": map[string]int{"activeMatches": maxActiveMatches, "retainedMatches": maxRetainedMatches, "connections": cap(s.Slots), "pendingHandshakes": cap(s.handshakes)},
		"lab":      lab, "privateMatches": private,
		"bytesIn": s.BytesIn.Load(), "bytesOut": s.BytesOut.Load(), "rejected": s.Rejected.Load(), "oversizedDatagrams": s.OversizedDatagrams.Load(),
	}
}
