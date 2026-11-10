package sim

import (
	"context"
	"errors"
	"math"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

type practiceBrain struct {
	path           []shared.Point
	patrol         int
	nextPlan       uint64
	state          string
	planCount      int
	point          int
	evadeX, evadeZ float64
	retreating     bool
	spacingRange   float64
	contesting     bool
}

// NewPracticeMatchEngine uses the ordinary mapped combat world, with exactly
// one reserved server-controlled opponent. Its bot never receives a transport.
func NewPracticeMatchEngine(ctx context.Context, id string, roster, heroes map[string]string, m *shared.ArenaMapDefinition, ownerUser, botUser, mode string) (*Engine, error) {
	w, err := newPracticeWorld(id, roster, heroes, m, ownerUser, botUser, mode)
	if err != nil {
		return nil, err
	}
	return newEngine(ctx, w), nil
}

func newPracticeWorld(id string, roster, heroes map[string]string, m *shared.ArenaMapDefinition, ownerUser, botUser, mode string) (*World, error) {
	if !shared.IsPracticeMode(mode) {
		return nil, errors.New("invalid_bot_mode")
	}
	if len(roster) != 2 || ownerUser == "" || botUser == "" || ownerUser == botUser || roster[ownerUser] == "" || roster[botUser] == "" || roster[ownerUser] == roster[botUser] {
		return nil, errors.New("invalid_roster")
	}
	w, err := newMatchWorldWithMap(id, roster, heroes, m)
	if err != nil {
		return nil, err
	}
	w.PracticeOwner, w.BotUser, w.BotMode = ownerUser, botUser, mode
	owner, bot := w.Seats[ownerUser], w.Seats[botUser]
	// A clear central lane makes the initial shot/push test immediately useful;
	// an alternate valid map still uses the bounded ordinary safe-spawn search.
	for i, s := range []*Seat{owner, bot} {
		s.SpawnX, s.SpawnZ = -2, 0
		if i == 1 {
			s.SpawnX = 1
		}
		x, z, ok := w.respawnPosition(s)
		if !ok {
			return nil, errors.New("invalid_map")
		}
		s.State.X, s.State.Z, s.SpawnX, s.SpawnZ = x, z, x, z
	}
	bot.State.Bot, bot.State.Online, bot.Loaded, bot.Generation = true, true, true, 1
	bot.State.ProtectedUntilTick = 2 * uint64(w.Rules.TickRate)
	w.practice = &practiceBrain{}
	return w, nil
}

// SetPracticeMode executes its authorization and mutation on the engine's owner
// goroutine, just like input. Cooldowns, health and already-fired shots persist.
func (e *Engine) SetPracticeMode(ctx context.Context, owner, mode string) error {
	reply := make(chan error, 1)
	select {
	case e.commands <- command{fn: func(w *World) {
		if err := ctx.Err(); err != nil {
			reply <- err
			return
		}
		reply <- w.setPracticeMode(owner, mode)
	}}:
	case <-ctx.Done():
		return ctx.Err()
	case <-e.Done:
		return errors.New("practice_inactive")
	}
	select {
	case err := <-reply:
		return err
	case <-e.Done:
		return errors.New("practice_inactive")
	}
}

func (w *World) setPracticeMode(owner, mode string) error {
	if w.recording != nil {
		defer w.recordEvent("practice", replayEventPayload{User: owner, Mode: mode})()
	}
	if w.PracticeOwner == "" || w.Result != nil || w.Phase == phaseEnded {
		return errors.New("practice_inactive")
	}
	if owner != w.PracticeOwner {
		return errors.New("host_required")
	}
	if !shared.IsPracticeMode(mode) {
		return errors.New("invalid_bot_mode")
	}
	bot := w.Seats[w.BotUser]
	if bot == nil || bot.State.Left {
		return errors.New("practice_inactive")
	}
	w.BotMode = mode
	bot.DX, bot.DZ, bot.DashEndTick = 0, 0, 0
	bot.Pending = nil
	w.practice = &practiceBrain{}
	return nil
}

func (w *World) advancePracticeBot() {
	if w.practice == nil {
		return
	}
	bot, owner := w.Seats[w.BotUser], w.Seats[w.PracticeOwner]
	if bot == nil {
		return
	}
	bot.LastInput = w.Tick
	bot.DX, bot.DZ = 0, 0
	if bot.State.HP <= 0 || bot.State.Left {
		w.practice.path = nil
		w.practice.nextPlan = 0
		w.practice.retreating = false
		w.practice.contesting = false
		return
	}
	behavior, _ := shared.PracticeBehaviors(w.BotMode)
	w.practice.spacingRange = 0
	w.practice.state = "idle"
	contesting := behavior&shared.PracticeAttack != 0 && behavior&shared.PracticeCapture != 0 &&
		owner != nil && owner.State.Online && owner.State.HP > 0 && !owner.State.Left &&
		w.insidePoint(bot) && w.insidePoint(owner)
	if behavior&shared.PracticeCapture != 0 {
		if w.practice.point != w.PointIndex || w.practice.contesting != contesting {
			w.practice.point, w.practice.contesting = w.PointIndex, contesting
			w.practice.path, w.practice.nextPlan = nil, 0
		}
		if !contesting {
			w.practiceCapture(bot, owner)
		}
	} else if behavior&shared.PracticePatrol != 0 {
		w.practicePatrol(bot, owner)
	}
	if behavior&shared.PracticeAttack != 0 {
		w.practiceAttack(bot, owner, contesting || behavior&(shared.PracticeCapture|shared.PracticePatrol) == 0)
	}
	if behavior&shared.PracticeKeepDistance != 0 {
		w.practiceKeepDistance(bot, owner)
	}
	if behavior&shared.PracticeDodge != 0 {
		w.practiceDodge(bot, owner)
	}
}

func (w *World) practiceCapture(bot, owner *Seat) {
	brain := w.practice
	brain.state = "capture"
	if w.insidePoint(bot) {
		brain.path = nil
		return
	}
	goal := w.Rules.ControlPoints[w.PointIndex]
	if owner != nil && owner.State.HP > 0 && !owner.State.Left &&
		math.Hypot(goal.X-owner.State.X, goal.Z-owner.State.Z) < 2*w.Rules.PlayerRadius+.1 {
		// Aim for a free place inside the point instead of pathing into the
		// occupied center. Scoring and contesting still use ordinary occupancy.
		for i := range 8 {
			angle := float64(i) * math.Pi / 4
			candidate := shared.Point{X: goal.X + math.Cos(angle)*w.Rules.ControlPointRadius*.6, Z: goal.Z + math.Sin(angle)*w.Rules.ControlPointRadius*.6}
			if shared.CircleClear(w.Map, candidate.X, candidate.Z, w.Rules.PlayerRadius) &&
				math.Hypot(candidate.X-owner.State.X, candidate.Z-owner.State.Z) >= 2*w.Rules.PlayerRadius+.1 {
				goal = candidate
				break
			}
		}
	}
	w.followPracticePath(bot, goal, owner)
	if length := math.Hypot(bot.DX, bot.DZ); length > 1e-6 {
		bot.State.FacingX, bot.State.FacingZ = bot.DX/length, bot.DZ/length
	}
}

func (w *World) practicePatrol(bot, owner *Seat) {
	w.practice.state = "patrol"
	waypoints := []shared.Point{{X: 0, Z: 5}, {X: -6, Z: 0}, {X: 0, Z: -5}, {X: 6, Z: 0}}
	for attempts := 0; attempts < len(waypoints); attempts++ {
		goal := waypoints[w.practice.patrol%len(waypoints)]
		if !shared.CircleClear(w.Map, goal.X, goal.Z, w.Rules.PlayerRadius) || math.Hypot(goal.X-bot.State.X, goal.Z-bot.State.Z) < .2 || owner != nil && owner.State.HP > 0 && !owner.State.Left && math.Hypot(goal.X-owner.State.X, goal.Z-owner.State.Z) < 2*w.Rules.PlayerRadius+.1 {
			w.practice.patrol++
			w.practice.path = nil
			continue
		}
		w.followPracticePath(bot, goal, owner)
		if length := math.Hypot(bot.DX, bot.DZ); length >= 1e-6 {
			bot.State.FacingX, bot.State.FacingZ = bot.DX/length, bot.DZ/length
		}
		return
	}
}

func (w *World) practiceAttack(bot, owner *Seat, chase bool) {
	if owner == nil || owner.State.HP <= 0 || owner.State.Left {
		if chase {
			w.practice.state = "wait"
			w.practice.path = nil
		}
		return
	}
	dx, dz := owner.State.X-bot.State.X, owner.State.Z-bot.State.Z
	distance := math.Hypot(dx, dz)
	if distance < 1e-6 {
		return
	}
	bot.State.FacingX, bot.State.FacingZ = dx/distance, dz/distance
	c := w.seatRules(bot)
	spawn := c.PlayerRadius + c.ProjectileRadius + .05
	rangeLimit := spawn + c.ProjectileSpeed*float64(c.ProjectileLifetimeTicks)/float64(c.TickRate)
	stopDistance := math.Max(2*c.PlayerRadius+.15, math.Min(5, rangeLimit*.72))
	losRadius := c.ProjectileRadius
	if bot.hero != nil && bot.hero.AttackKind == "melee" {
		rangeLimit = bot.hero.MeleeRange + c.PlayerRadius
		stopDistance = math.Max(2*c.PlayerRadius+.1, bot.hero.MeleeRange)
		losRadius = 0
	}
	_, _, _, wall := shared.FirstMapHit(w.Map, bot.State.X, bot.State.Z, dx, dz, losRadius, true)
	ground := bot.hero != nil && bot.hero.AttackKind == "ground"
	if ground {
		rangeLimit = bot.hero.CastRange
		stopDistance = rangeLimit * .75
		wall = !shared.CircleClear(w.Map, owner.State.X, owner.State.Z, 0)
	}
	if !wall && distance <= rangeLimit && w.Tick >= bot.State.FireReadyTick && bot.Pending == nil {
		bot.Pending = &actionIntent{Kind: "fire", ID: bot.ActionAck + 1, Tick: w.Tick, AimX: dx / distance, AimZ: dz / distance}
		if ground {
			bot.Pending.HasGroundTarget = true
			bot.Pending.TargetX = owner.State.X
			bot.Pending.TargetZ = owner.State.Z
		}
	}
	if !chase {
		return
	}
	if !wall && distance <= stopDistance {
		w.practice.state = "fire"
		w.practice.path = nil
		return
	}
	w.practice.state = "chase"
	w.followPracticePath(bot, shared.Point{X: owner.State.X, Z: owner.State.Z}, nil)
}

func (w *World) followPracticePath(bot *Seat, goal shared.Point, avoid *Seat) {
	brain := w.practice
	if w.Tick >= brain.nextPlan {
		// Always replan from the actual collision-resolved position: a push or
		// a moving blocker cannot leave the bot following a stale corner route.
		brain.path = w.practicePath(shared.Point{X: bot.State.X, Z: bot.State.Z}, goal, avoid)
		brain.planCount++
		brain.nextPlan = w.Tick + uint64(max(1, w.Rules.TickRate/2))
	}
	for len(brain.path) > 0 {
		next := brain.path[0]
		dx, dz := next.X-bot.State.X, next.Z-bot.State.Z
		distance := math.Hypot(dx, dz)
		if distance < .08 {
			brain.path = brain.path[1:]
			continue
		}
		step := w.seatRules(bot).Speed / float64(w.Rules.TickRate)
		bot.DX, bot.DZ = dx/distance, dz/distance
		if distance < step {
			bot.DX *= distance / step
			bot.DZ *= distance / step
		}
		return
	}
}
