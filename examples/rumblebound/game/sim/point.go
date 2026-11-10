package sim

func (w *World) nextRotationTick() uint64 {
	period := w.Rules.PointRotationTicks
	return (w.Tick/period + 1) * period
}

func (w *World) insidePoint(s *Seat) bool {
	p := w.Rules.ControlPoints[w.PointIndex]
	dx, dz := s.State.X-p.X, s.State.Z-p.Z
	radius := w.Rules.ControlPointRadius
	return dx*dx+dz*dz <= radius*radius
}

// Entry is resolved before projectile collisions. An entrant can be hit on
// this tick and only scores if still alive after every projectile resolves.
func (w *World) removePointEntryProtection(seats []*Seat) {
	if w.roster == nil {
		return
	}
	for _, s := range seats {
		if s.State.Online && !s.State.Left && s.State.HP > 0 &&
			w.Tick < s.State.ProtectedUntilTick && w.insidePoint(s) {
			s.State.ProtectedUntilTick = 0
		}
	}
}

func (w *World) pointOccupancy(seats []*Seat) (string, string) {
	team := ""
	for _, s := range seats {
		if !s.State.Online || s.State.Left || s.State.HP == 0 ||
			w.Tick < s.State.ProtectedUntilTick || !w.insidePoint(s) {
			continue
		}
		if team != "" && team != s.State.TeamID {
			return "contested", ""
		}
		team = s.State.TeamID
	}
	if team == "" {
		return "empty", ""
	}
	return "scoring", team
}

func (w *World) teamForfeited(team string) bool {
	for _, s := range w.Seats {
		if s.State.TeamID == team && !s.State.Left {
			return false
		}
	}
	return true
}

func (w *World) finish(outcome, reason, winner string) {
	w.Result = &BattleResult{Outcome: outcome, Reason: reason, WinnerTeamID: winner, EndedTick: w.Tick}
	w.Phase = phaseEnded
	w.GroundCasts = nil
	w.GroundImpacts = nil
	for _, seat := range w.Seats {
		seat.State.AttackStopUntilTick = 0
	}
}

func (w *World) advanceMatch(seats []*Seat) {
	if w.roster == nil {
		return
	}
	w.PointStatus, w.ScoringTeamID = w.pointOccupancy(seats)
	if w.ScoringTeamID != "" {
		w.ScoreTicks[w.ScoringTeamID]++
	}
	active, sole := 0, ""
	for _, team := range w.TeamIDs {
		if !w.teamForfeited(team) {
			active++
			sole = team
		}
	}
	switch active {
	case 0:
		w.finish("abort", "no_teams", "")
	case 1:
		w.finish("win", "forfeit", sole)
	}
	if w.Result == nil {
		goalTicks := w.Rules.ScoreGoal * uint64(w.Rules.TickRate)
		if w.ScoringTeamID != "" && w.ScoreTicks[w.ScoringTeamID] >= goalTicks {
			w.finish("win", "score", w.ScoringTeamID)
		}
		if w.Result == nil && w.Tick >= w.Rules.MatchDurationTicks {
			// ScoreTicks is exact earned progress, including the partial point
			// that can separate two teams at the deadline.
			var best uint64
			winner, tied := "", false
			for _, team := range w.TeamIDs {
				if w.teamForfeited(team) {
					continue
				}
				score := w.ScoreTicks[team]
				if winner == "" || score > best {
					best, winner, tied = score, team, false
				} else if score == best {
					tied = true
				}
			}
			if tied {
				w.finish("draw", "time", "")
			} else {
				w.finish("win", "time", winner)
			}
		}
	}
	// Rotation happens after the old point has scored this tick. The outgoing
	// point is visible through the warning period, and the next snapshot shows
	// the new point at the rotation tick.
	if w.Tick%w.Rules.PointRotationTicks == 0 {
		w.PointIndex = (w.PointIndex + 1) % len(w.Rules.ControlPoints)
		w.PointStatus, w.ScoringTeamID = w.pointOccupancy(seats)
	}
}
