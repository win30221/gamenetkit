package sim

import (
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"sort"
)

func (w *World) Snapshot(s *Seat, kind string) protocol.Server {
	return w.recipientSnapshot(w.worldSnapshot(), s, kind)
}

// A publication owns this immutable view. It is never reused across publications,
// including transitions or configuration changes at the same tick.
func (w *World) worldSnapshot() protocol.Server {
	players := make([]protocol.Player, 0, len(w.Seats))
	for _, v := range w.Seats {
		state := playerSnapshot(v.State)
		state.FireActionID = v.LastFireActionID
		state.Life = v.Life
		if w.Tick < v.DashEndTick && state.HP > 0 && !state.Left {
			state.DashX, state.DashZ, state.DashEndTick = v.DashDX, v.DashDZ, v.DashEndTick
		}
		players = append(players, state)
	}
	sort.Slice(players, func(i, j int) bool { return players[i].PlayerID < players[j].PlayerID })
	projectiles := make([]protocol.Projectile, 0, len(w.Projectiles))
	for _, p := range w.Projectiles {
		projectiles = append(projectiles, projectileSnapshot(p.State))
	}
	groundCasts, groundImpacts := w.groundSnapshot()
	combatStates := w.CombatEvents
	if limit := max(0, maxMixedTerminalEvents-2*len(groundCasts)-2*len(groundImpacts)); len(combatStates) > limit {
		combatStates = combatStates[len(combatStates)-limit:]
	}
	if len(combatStates) > maxCombatEvents {
		combatStates = combatStates[len(combatStates)-maxCombatEvents:]
	}
	combatEvents := make([]protocol.CombatEvent, 0, len(combatStates))
	for _, event := range combatStates {
		combatEvents = append(combatEvents, protocol.CombatEvent{ID: event.ID, Kind: event.Kind,
			OwnerID: event.OwnerID, TargetID: event.TargetID, X: event.X, Z: event.Z, DX: event.DX, DZ: event.DZ, Tick: event.Tick, ActionID: event.ActionID})
	}
	endStates := w.ProjectileEnds
	if limit := maxMixedTerminalEvents - len(combatEvents) - 2*len(groundCasts) - 2*len(groundImpacts); (len(combatEvents)+len(groundCasts)+len(groundImpacts)) > 0 && len(endStates) > max(0, limit) {
		limit = max(0, limit)
		endStates = endStates[len(endStates)-limit:]
	}
	ends := make([]protocol.ProjectileEnd, 0, len(endStates))
	for _, end := range endStates {
		ends = append(ends, protocol.ProjectileEnd{ID: end.ID, X: end.X, Z: end.Z, Tick: end.Tick,
			Kind: end.Kind, TargetID: end.TargetID, OwnerID: end.OwnerID})
	}
	return protocol.Server{Protocol: w.Rules.Protocol, Content: w.Rules.Content, MatchID: w.MatchID, MapHash: w.MapHash, Practice: w.PracticeOwner != "", BotMode: w.BotMode, Tick: w.Tick, Players: players, Projectiles: projectiles, ProjectileEnds: ends, CombatEvents: combatEvents, GroundCasts: groundCasts, GroundImpacts: groundImpacts, Match: w.matchState()}
}

func (w *World) recipientSnapshot(base protocol.Server, s *Seat, kind string) protocol.Server {
	base.Type, base.Generation, base.PlayerID = kind, s.Generation, s.State.PlayerID
	base.Echo, base.InputTick, base.ActionAck, base.ActionCode = s.Echo, s.LastInput, s.ActionAck, s.ActionCode
	return w.projectNet(s, base)
}

// matchState builds a fresh, sorted public view. Only a private match has a
// point or score; the G0 transport lab keeps its original wire shape.
func (w *World) matchState() *protocol.MatchState {
	if w.roster == nil || len(w.Rules.ControlPoints) == 0 {
		return nil
	}
	state := &protocol.MatchState{
		Phase:         w.Phase,
		EndsAtTick:    w.Rules.MatchDurationTicks,
		Point:         w.controlPoint(w.PointIndex),
		RotationTick:  w.nextRotationTick(),
		PointStatus:   w.PointStatus,
		ScoringTeamID: w.ScoringTeamID,
		Scores:        make([]protocol.TeamScore, 0, len(w.TeamIDs)),
		Result:        resultSnapshot(w.Result),
	}
	if w.Result == nil && state.RotationTick <= w.Rules.MatchDurationTicks &&
		w.Tick >= state.RotationTick-w.Rules.PointWarningTicks {
		point := w.controlPoint((w.PointIndex + 1) % len(w.Rules.ControlPoints))
		state.NextPoint = &point
	}
	for _, team := range w.TeamIDs {
		ticks := w.ScoreTicks[team]
		state.Scores = append(state.Scores, protocol.TeamScore{
			TeamID: team, Score: ticks / uint64(w.Rules.TickRate), ScoreTicks: ticks,
			Forfeited: w.teamForfeited(team),
		})
	}
	return state
}

func (w *World) controlPoint(index int) protocol.ControlPoint {
	p := w.Rules.ControlPoints[index]
	return protocol.ControlPoint{Index: index, X: p.X, Z: p.Z, Radius: w.Rules.ControlPointRadius}
}

func playerSnapshot(p PlayerState) protocol.Player {
	return protocol.Player{PlayerID: p.PlayerID, EntityID: p.EntityID, TeamID: p.TeamID, HeroID: p.HeroID,
		BasicReadyTick: p.BasicReadyTick, AttackReleaseTick: p.AttackReleaseTick, AttackTargetID: p.AttackTargetID, AttackTargetLife: p.AttackTargetLife, BasicActionID: p.BasicActionID,
		X: p.X, Z: p.Z, FacingX: p.FacingX, FacingZ: p.FacingZ, Ack: p.Ack, Online: p.Online, Left: p.Left, Bot: p.Bot, HP: p.HP,
		FireReadyTick: p.FireReadyTick, DashReadyTick: p.DashReadyTick, AttackStopUntilTick: p.AttackStopUntilTick,
		RespawnTick: p.RespawnTick, ProtectedUntilTick: p.ProtectedUntilTick}
}
func projectileSnapshot(p ProjectileState) protocol.Projectile {
	return protocol.Projectile{ID: p.ID, OwnerID: p.OwnerID, TeamID: p.TeamID, X: p.X, Z: p.Z, DX: p.DX, DZ: p.DZ, ExpiresTick: p.ExpiresTick, ActionID: p.ActionID}
}
func resultSnapshot(r *BattleResult) *protocol.MatchResult {
	if r == nil {
		return nil
	}
	return &protocol.MatchResult{Outcome: r.Outcome, Reason: r.Reason, WinnerTeamID: r.WinnerTeamID, EndedTick: r.EndedTick}
}
func inputIntent(c protocol.Client) InputIntent {
	return InputIntent{TargetID: c.TargetID, TargetLife: c.TargetLife, Generation: c.Generation, Seq: c.Seq, X: c.X, Z: c.Z, Echo: c.Echo, Action: c.Action, ActionID: c.ActionID, ActionTick: c.ActionTick, ViewTick: c.ViewTick, ObservedTick: c.ObservedTick, AimX: c.AimX, AimZ: c.AimZ, FacingX: c.FacingX, FacingZ: c.FacingZ, HasGroundTarget: c.HasGroundTarget, TargetX: c.TargetX, TargetZ: c.TargetZ}
}

// Step is the single-owner orchestration boundary. Gameplay advances first;
// publication is a separate concern and snapshots cannot mutate domain state.
func (w *World) Step() {
	if w.Result != nil || (w.roster != nil && w.Phase != phasePlaying) {
		return
	}
	if w.recording != nil {
		defer w.recordEvent("step", replayEventPayload{})()
	}
	w.advance()
	w.recordHistory()
	w.publishUpdates()
}
func (w *World) publishStart() {
	base := w.worldSnapshot()
	for _, seat := range w.Seats {
		if seat.State.Bot {
			continue
		}
		message := w.recipientSnapshot(base, seat, "start")
		select {
		case <-seat.Updates:
		default:
		}
		seat.Updates <- message
	}
}
func (w *World) publishUpdates() {
	regular := w.Result != nil || w.Tick%uint64(w.Rules.TickRate/w.Rules.SnapshotRate) == 0
	var base protocol.Server
	prepared := false
	for _, s := range w.Seats {
		if !s.State.Online || s.State.Bot {
			continue
		}
		// A serial client cannot send its next action until it sees this result.
		// Publish the owner's complete post-step state immediately, without
		// increasing idle/other-player cadence or introducing a second protocol.
		if !regular && s.ActionAck <= s.publishedActionAck {
			continue
		}
		kind := "snapshot"
		if w.Result != nil {
			kind = "result"
		}
		if !prepared {
			base = w.worldSnapshot()
			prepared = true
		}
		message := w.recipientSnapshot(base, s, kind)
		select {
		case pending := <-s.Updates:
			// The transition is a one-time control message. Keep it queued
			// until the writer consumes it, even if snapshots are faster.
			if pending.Type == "start" && kind == "snapshot" {
				s.Updates <- pending
				continue
			}
		default:
		}
		s.Updates <- message
		s.publishedActionAck = s.ActionAck
	}
}
