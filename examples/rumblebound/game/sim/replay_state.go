package sim

import "time"

// Include future-affecting private fields, not just the public snapshot. Socket
// channels, AOI membership and collision scratch space are presentation/I/O or
// recomputed work buffers and intentionally excluded.
type replaySeat struct {
	User                                            string
	State                                           PlayerState
	Generation                                      uint32
	Loaded                                          bool
	Life, LastInput                                 uint64
	LeftAt                                          time.Time
	DX, DZ, Echo, SpawnX, SpawnZ, DashDX, DashDZ    float64
	DashEndTick, ActionAck                          uint64
	ActionCode                                      string
	Pending                                         *actionIntent
	ExecutingActionID, LastFireActionID, RewindTick uint64
	RewindUsed                                      bool
	Net                                             any
}
type replayShot struct {
	State          ProjectileState
	Speed          float64
	Damage         int
	BirthX, BirthZ float64
	MuzzlePending  bool
}
type replayBrain struct {
	Path             any
	Patrol           int
	NextPlan         uint64
	State            string
	PlanCount, Point int
	EvadeX, EvadeZ   float64
	Retreating       bool
	SpacingRange     float64
	Contesting       bool
}
type replayHistory struct {
	Tick  uint64
	Poses poseFrame
}

func (w *World) replayStateHash() (string, error) {
	seats := make([]replaySeat, 0, len(w.Seats))
	for _, s := range w.sortedSeats() {
		seats = append(seats, replaySeat{User: s.UserID, State: s.State, Generation: s.Generation, Loaded: s.Loaded, Life: s.Life, LastInput: s.LastInput, LeftAt: s.LeftAt.UTC(), DX: s.DX, DZ: s.DZ, Echo: s.Echo, SpawnX: s.SpawnX, SpawnZ: s.SpawnZ, DashDX: s.DashDX, DashDZ: s.DashDZ, DashEndTick: s.DashEndTick, ActionAck: s.ActionAck, ActionCode: s.ActionCode, Pending: s.Pending, ExecutingActionID: s.ExecutingActionID, LastFireActionID: s.LastFireActionID, RewindTick: s.RewindTick, RewindUsed: s.RewindUsed, Net: s.Net})
	}
	shots := make([]replayShot, 0, len(w.Projectiles))
	for _, p := range w.Projectiles {
		shots = append(shots, replayShot{p.State, p.speed, p.damage, p.birthX, p.birthZ, p.muzzlePending})
	}
	var brain *replayBrain
	if p := w.practice; p != nil {
		brain = &replayBrain{p.path, p.patrol, p.nextPlan, p.state, p.planCount, p.point, p.evadeX, p.evadeZ, p.retreating, p.spacingRange, p.contesting}
	}
	var history []replayHistory
	if w.history != nil {
		start := uint64(0)
		if w.Tick > 31 {
			start = w.Tick - 31
		}
		for tick := start; tick <= w.Tick; tick++ {
			if h, ok := w.history.At(tick); ok && h.BeforeTick == tick {
				history = append(history, replayHistory{tick, h.Before})
			}
			if tick == w.Tick {
				break
			}
		}
	}
	return replayHash(struct {
		Tick                                       uint64
		Phase                                      string
		Seats                                      []replaySeat
		Expired                                    map[string]time.Time
		NextID                                     uint64
		Projectiles                                []replayShot
		Ends                                       []ProjectileEnd
		Combat                                     []CombatEvent
		Casts                                      []groundCast
		Impacts                                    []GroundImpact
		NextCastID, NextCombatID, NextProjectileID uint64
		Scores                                     map[string]uint64
		PointIndex                                 int
		PointStatus, ScoringTeam                   string
		Result                                     *BattleResult
		BotMode                                    string
		Brain                                      *replayBrain
		HistoryEnabled                             bool
		History                                    []replayHistory
	}{w.Tick, w.Phase, seats, w.Expired, w.NextID, shots, w.ProjectileEnds, w.CombatEvents, w.GroundCasts, w.GroundImpacts, w.NextGroundCastID, w.NextCombatEventID, w.NextProjectileID, w.ScoreTicks, w.PointIndex, w.PointStatus, w.ScoringTeamID, w.Result, w.BotMode, brain, w.history != nil, history})
}
