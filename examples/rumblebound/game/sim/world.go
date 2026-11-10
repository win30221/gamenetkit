// Package sim is transport-independent and owned by one Engine goroutine.
package sim

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"github.com/win30221/gamenetkit/server/netcore"
	"math"
	"sort"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

const MatchID = "g0-lab"

const (
	phaseLoading = "loading"
	phasePlaying = "playing"
	phaseEnded   = "ended"
)

type actionIntent struct {
	TargetID               string
	TargetLife             uint64
	ViewTick, ObservedTick uint64
	Kind                   string
	ID, Tick               uint64
	AimX, AimZ             float64
	HasGroundTarget        bool
	TargetX, TargetZ       float64
}

type shot struct {
	State  ProjectileState
	speed  float64
	damage int
	// The first collision sweep includes the segment from the shooter to the
	// visible muzzle position; otherwise close grazing targets can be skipped.
	birthX, birthZ float64
	muzzlePending  bool
}

type World struct {
	recording          netcore.ReplaySink
	history            netcore.History[poseFrame]
	MatchID            string
	Phase              string
	Rules              shared.Config
	Map                *shared.ArenaMapDefinition
	MapHash            string
	Tick               uint64
	Seats              map[string]*Seat
	Expired            map[string]time.Time
	Now                func() time.Time
	NextID             uint64
	roster             map[string]string
	Projectiles        []shot
	ProjectileEnds     []ProjectileEnd
	CombatEvents       []CombatEvent
	GroundCasts        []groundCast
	GroundImpacts      []GroundImpact
	NextGroundCastID   uint64
	NextCombatEventID  uint64
	NextProjectileID   uint64
	TeamIDs            []string
	ScoreTicks         map[string]uint64
	PointIndex         int
	PointStatus        string
	ScoringTeamID      string
	Result             *BattleResult
	PracticeOwner      string
	BotUser            string
	BotMode            string
	practice           *practiceBrain
	collisionWorkspace shared.CollisionWorkspace
}

func New() *World {
	return &World{MatchID: MatchID, Rules: shared.Rules(), Seats: map[string]*Seat{}, Expired: map[string]time.Time{}, Now: time.Now}
}

// newMatchWorld copies and freezes the roster before any transport is attached.
// IDs are allocated in UserID order so reconnects never depend on join order.
func newMatchWorld(matchID string, roster map[string]string) (*World, error) {
	if matchID == "" || matchID == MatchID || len(matchID) > 64 {
		return nil, errors.New("invalid_match_id")
	}
	w := New()
	if len(roster) < 2 || len(roster) > w.Rules.MaxPlayers {
		return nil, errors.New("invalid_roster")
	}
	teamSizes := map[string]int{}
	users := make([]string, 0, len(roster))
	for user, team := range roster {
		if user == "" || team == "" || len(team) > 16 {
			return nil, errors.New("invalid_roster")
		}
		teamSizes[team]++
		if teamSizes[team] > 4 {
			return nil, errors.New("invalid_roster")
		}
		users = append(users, user)
	}
	if len(teamSizes) < 2 || len(teamSizes) > 4 {
		return nil, errors.New("invalid_roster")
	}
	sort.Strings(users)
	teams := make([]string, 0, len(teamSizes))
	for team := range teamSizes {
		teams = append(teams, team)
	}
	sort.Strings(teams)
	teamSlots := make(map[string]int, len(teams))
	w.MatchID = matchID
	w.Phase = phaseLoading
	w.roster = make(map[string]string, len(roster))
	w.TeamIDs = teams
	w.ScoreTicks = make(map[string]uint64, len(teams))
	w.PointStatus = "empty"
	for _, team := range teams {
		w.ScoreTicks[team] = 0
	}
	for _, user := range users {
		team := roster[user]
		teamIndex := sort.SearchStrings(teams, team)
		x, z := spawnPosition(teamIndex, len(teams), teamSlots[team])
		teamSlots[team]++
		w.roster[user] = team
		w.NextID++
		id := idString(w.NextID)
		w.Seats[user] = &Seat{UserID: user, SpawnX: x, SpawnZ: z, State: PlayerState{PlayerID: "p" + id, EntityID: "e" + id, TeamID: team, X: x, Z: z, HP: w.Rules.MaxHP}}
	}
	return w, nil
}

func newMatchWorldWithHeroes(matchID string, roster, heroes map[string]string) (*World, error) {
	w, err := newMatchWorld(matchID, roster)
	if err != nil {
		return nil, err
	}
	if len(heroes) != len(roster) {
		return nil, errors.New("invalid_heroes")
	}
	for user, seat := range w.Seats {
		id, present := heroes[user]
		if !present {
			return nil, errors.New("invalid_heroes")
		}
		hero, ok := w.Rules.Hero(id)
		if !ok {
			return nil, errors.New("invalid_hero")
		}
		seat.hero = &hero
		seat.State.HeroID = hero.HeroID
		seat.State.HP = hero.MaxHP
	}
	return w, nil
}

func (w *World) seatRules(s *Seat) shared.Config {
	c := w.Rules
	if s.hero != nil {
		h := s.hero
		c.MaxHP, c.Speed = h.MaxHP, h.Speed
		c.ProjectileDamage, c.ProjectileSpeed = h.ProjectileDamage, h.ProjectileSpeed
		c.ProjectileLifetimeTicks, c.FireCooldownTicks = h.ProjectileLifetimeTicks, h.FireCooldownTicks
		c.DashSpeed, c.DashCooldownTicks = h.DashSpeed, h.DashCooldownTicks
	}
	return c
}

func newMatchWorldWithMap(matchID string, roster, heroes map[string]string, m *shared.ArenaMapDefinition) (*World, error) {
	w, err := newMatchWorldWithHeroes(matchID, roster, heroes)
	if err != nil {
		return nil, err
	}
	if err := shared.ValidateArenaMap(m, w.Rules.PlayerRadius); err != nil {
		return nil, errors.New("invalid_map")
	}
	data, err := json.Marshal(m)
	if err != nil {
		return nil, errors.New("invalid_map")
	}
	frozen := m.Clone()
	w.Map = frozen
	w.Rules = shared.RulesForMap(frozen)
	for _, seat := range w.Seats {
		hero, ok := w.Rules.Hero(seat.State.HeroID)
		if ok {
			seat.hero = &hero
		}
	}
	h := sha256.Sum256(data)
	w.MapHash = hex.EncodeToString(h[:])
	// The public default is hashed as its exact UTF-8 source, including formatting.
	defaultData, _ := json.Marshal(shared.ArenaMap())
	if string(defaultData) == string(data) {
		w.MapHash = shared.ArenaMapHash()
	}
	var anchors []shared.Point
	for _, layout := range frozen.SpawnLayouts {
		if layout.TeamCount == len(w.TeamIDs) {
			anchors = layout.Anchors
			break
		}
	}
	if len(anchors) != len(w.TeamIDs) {
		return nil, errors.New("invalid_map")
	}
	seats := w.sortedSeats()
	slots := map[string]int{}
	offsets := [...]float64{0, 1, -1, 2}
	for _, s := range seats {
		p := anchors[sort.SearchStrings(w.TeamIDs, s.State.TeamID)]
		s.SpawnX, s.SpawnZ = p.X, p.Z+offsets[slots[s.State.TeamID]]
		slots[s.State.TeamID]++
		s.State.X, s.State.Z = s.SpawnX, s.SpawnZ
	}
	for _, s := range seats {
		x, z, ok := w.respawnPosition(s)
		if !ok {
			return nil, errors.New("invalid_map")
		}
		s.State.X, s.State.Z = x, z
	}
	return w, nil
}

func (w *World) sortedSeats() []*Seat {
	seats := make([]*Seat, 0, len(w.Seats))
	for _, s := range w.Seats {
		seats = append(seats, s)
	}
	sort.Slice(seats, func(i, j int) bool { return seats[i].State.PlayerID < seats[j].State.PlayerID })
	return seats
}

// A respawn waits briefly if no nearby circle is free. Protection affects damage,
// never solidity, so a crowded spawn cannot create overlapping live bodies.
func (w *World) respawnPosition(s *Seat) (float64, float64, bool) {
	if w.Map == nil {
		return s.SpawnX, s.SpawnZ, true
	}
	clear := func(x, z float64) bool {
		if !shared.CircleClear(w.Map, x, z, w.Rules.PlayerRadius) {
			return false
		}
		for _, other := range w.Seats {
			if other == s || other.State.Left || other.State.HP <= 0 {
				continue
			}
			if math.Hypot(x-other.State.X, z-other.State.Z) < 2*w.Rules.PlayerRadius+1e-5 {
				return false
			}
		}
		return true
	}
	if clear(s.SpawnX, s.SpawnZ) {
		return s.SpawnX, s.SpawnZ, true
	}
	spacing := 2*w.Rules.PlayerRadius + .05
	for ring := 1; ring <= 3; ring++ {
		for iz := -ring; iz <= ring; iz++ {
			for ix := -ring; ix <= ring; ix++ {
				if absInt(ix) != ring && absInt(iz) != ring {
					continue
				}
				x, z := s.SpawnX+float64(ix)*spacing, s.SpawnZ+float64(iz)*spacing
				if clear(x, z) {
					return x, z, true
				}
			}
		}
	}
	return 0, 0, false
}

func absInt(x int) int {
	if x < 0 {
		return -x
	}
	return x
}

// Separate starting teams for the combat prototype; stable roster order also
// gives each member a deterministic slot within its team.
func spawnPosition(teamIndex, teamCount, slot int) (float64, float64) {
	var anchors [][2]float64
	switch teamCount {
	case 2:
		anchors = [][2]float64{{-6, 0}, {6, 0}}
	case 3:
		anchors = [][2]float64{{-6, 0}, {3, -5}, {3, 5}}
	default:
		anchors = [][2]float64{{-6, -6}, {6, -6}, {6, 6}, {-6, 6}}
	}
	zOffsets := [...]float64{0, 1, -1, 2}
	return anchors[teamIndex][0], anchors[teamIndex][1] + zOffsets[slot]
}

func Move(x, z, dx, dz float64, c shared.Config) (float64, float64) {
	length := math.Hypot(dx, dz)
	if length > 1 {
		dx /= length
		dz /= length
	}
	limit := c.ArenaHalfSize - c.PlayerRadius
	return math.Max(-limit, math.Min(limit, x+dx*c.Speed/float64(c.TickRate))), math.Max(-limit, math.Min(limit, z+dz*c.Speed/float64(c.TickRate)))
}

func idString(n uint64) string {
	const alphabet = "0123456789abcdef"
	if n == 0 {
		return "0"
	}
	b := make([]byte, 0, 16)
	for n > 0 {
		b = append([]byte{alphabet[n%16]}, b...)
		n /= 16
	}
	return string(b)
}

func (w *World) Input(user string, g uint32, c InputIntent) bool {
	if w.recording != nil {
		defer w.recordEvent("input", replayEventPayload{User: user, Generation: g, Input: &c})()
	}
	if w.BotUser != "" && user == w.BotUser {
		return false
	}
	if w.Result != nil || (w.roster != nil && w.Phase != phasePlaying) {
		return false
	}
	s := w.Seats[user]
	if s == nil || !s.State.Online || s.Generation != g || c.Generation != g || c.Seq <= s.State.Ack {
		return false
	}
	if math.IsNaN(c.FacingX) || math.IsNaN(c.FacingZ) || math.IsInf(c.FacingX, 0) || math.IsInf(c.FacingZ, 0) || math.Abs(c.FacingX) > 1 || math.Abs(c.FacingZ) > 1 {
		return false
	}
	s.State.Ack = c.Seq
	s.DX = c.X
	s.DZ = c.Z
	s.Echo = c.Echo
	s.LastInput = w.Tick
	// Facing is continuous presentation state, independent of one-shot action
	// aim (a sideways dash must not turn the character away from its target).
	if w.roster != nil && s.State.HP > 0 && !s.State.Left {
		if length := math.Hypot(c.FacingX, c.FacingZ); length >= 1e-6 {
			s.State.FacingX, s.State.FacingZ = c.FacingX/length, c.FacingZ/length
		}
	}
	if c.Action != "" && c.ActionID > s.ActionAck && s.Pending == nil {
		s.Pending = &actionIntent{Kind: c.Action, ID: c.ActionID, Tick: c.ActionTick, ViewTick: c.ViewTick, ObservedTick: c.ObservedTick, AimX: c.AimX, AimZ: c.AimZ, HasGroundTarget: c.HasGroundTarget, TargetX: c.TargetX, TargetZ: c.TargetZ, TargetID: c.TargetID, TargetLife: c.TargetLife}
	}
	return true
}

func (w *World) advance() {
	if w.Result != nil || (w.roster != nil && w.Phase != phasePlaying) {
		return
	}
	w.Tick++
	w.pruneCombatEvents()
	if w.roster == nil {
		for user, until := range w.Expired {
			if !w.Now().Before(until) {
				delete(w.Expired, user)
			}
		}
	}
	seats := make([]*Seat, 0, len(w.Seats))
	for _, s := range w.Seats {
		seats = append(seats, s)
	}
	sort.Slice(seats, func(i, j int) bool { return seats[i].State.PlayerID < seats[j].State.PlayerID })
	for _, s := range seats {
		if s.State.Left {
			continue
		}
		if w.roster != nil && s.Generation == 0 && w.Tick >= w.Rules.ReconnectTicks {
			w.Expired[s.UserID] = time.Time{}
			s.State.Left = true
			continue
		}
		if !s.State.Online && !s.LeftAt.IsZero() && w.seatExpired(s) {
			if w.roster == nil {
				delete(w.Seats, s.UserID)
				// Test sessions live at most one hour. Tombstones can then be removed.
				w.Expired[s.UserID] = w.Now().Add(time.Hour)
			} else {
				// Frozen players remain visible in match snapshots, but their seats
				// cannot be reclaimed after the reconnect deadline.
				w.Expired[s.UserID] = time.Time{}
				s.State.Left = true
				s.State.RespawnTick = 0
				s.State.AttackStopUntilTick = 0
				s.DashEndTick = 0
				s.Pending = nil
			}
			continue
		}
		if w.roster != nil && !s.State.Left && s.State.HP == 0 && s.State.RespawnTick != 0 && w.Tick >= s.State.RespawnTick {
			if x, z, ok := w.respawnPosition(s); ok {
				s.Life++
				s.State.HP, s.State.RespawnTick = w.seatRules(s).MaxHP, 0
				s.State.AttackStopUntilTick = 0
				s.State.X, s.State.Z = x, z
				s.State.ProtectedUntilTick = w.Tick + 2*uint64(w.Rules.TickRate)
			} else {
				s.State.RespawnTick = w.Tick + uint64(w.Rules.TickRate/4+1)
			}
		}
		if !s.State.Bot && w.Tick-s.LastInput >= w.Rules.InputTimeoutTicks {
			s.DX, s.DZ = 0, 0
		}
	}
	w.advancePracticeBot()
	// Deterministic order: expiry/respawn, actions by PlayerID, movement,
	// active-point entry removes protection, projectile collision/death,
	// occupancy/scoring/settlement, and finally point rotation.
	for _, s := range seats {
		if action := s.Pending; action != nil {
			s.Pending = nil
			if action.ID > s.ActionAck {
				s.ActionCode = w.resolveAction(s, *action)
				if (action.Kind == "fire" || action.Kind == "skill") && (s.ActionCode == "fired" || s.ActionCode == "melee" || s.ActionCode == "cast") {
					s.LastFireActionID = action.ID
				}
				s.ActionAck = action.ID
			}
		}
	}
	w.advanceBasicAttacks(seats)
	if w.Map != nil {
		w.advanceMappedPlayers(seats)
	} else {
		for _, s := range seats {
			if s.State.Left || (w.roster != nil && s.State.HP == 0) || w.Tick < s.State.AttackStopUntilTick {
				continue
			}
			c := w.seatRules(s)
			if w.roster != nil && w.Tick < s.DashEndTick {
				s.State.X, s.State.Z = moveAtSpeed(s.State.X, s.State.Z, s.DashDX, s.DashDZ, c.DashSpeed, c)
			} else if s.State.Online {
				s.State.X, s.State.Z = Move(s.State.X, s.State.Z, s.DX, s.DZ, c)
			}
		}
	}
	w.removePointEntryProtection(seats)
	w.advanceProjectiles(seats)
	w.advanceGroundCasts(seats)
	w.advanceMatch(seats)
}

func (w *World) advanceMappedPlayers(seats []*Seat) {
	bodies := make([]shared.CollisionBody, 0, len(seats))
	solid := make([]*Seat, 0, len(seats))
	for _, s := range seats {
		if s.State.Left || s.State.HP <= 0 {
			continue
		}
		c := w.seatRules(s)
		b := shared.CollisionBody{ID: s.State.PlayerID, X: s.State.X, Z: s.State.Z, Speed: c.Speed}
		if s.State.Online && w.Tick >= s.State.AttackStopUntilTick && w.Tick < s.DashEndTick {
			b.DX, b.DZ, b.Speed, b.Dashing = s.DashDX, s.DashDZ, c.DashSpeed, true
		} else if s.State.Online && w.Tick >= s.State.AttackStopUntilTick {
			b.DX, b.DZ = s.DX, s.DZ
		}
		bodies = append(bodies, b)
		solid = append(solid, s)
	}
	w.collisionWorkspace.ResolveCrowd(w.Map, bodies, 1/float64(w.Rules.TickRate), w.Rules.PlayerRadius, w.Rules.Collision)
	for i, b := range bodies {
		s := solid[i]
		s.State.X, s.State.Z = b.X, b.Z
		if b.Dashing && b.Stopped {
			s.DashEndTick = w.Tick
		}
	}
}

func moveAtSpeed(x, z, dx, dz, speed float64, c shared.Config) (float64, float64) {
	length := math.Hypot(dx, dz)
	if length > 1 {
		dx /= length
		dz /= length
	}
	limit := c.ArenaHalfSize - c.PlayerRadius
	return math.Max(-limit, math.Min(limit, x+dx*speed/float64(c.TickRate))), math.Max(-limit, math.Min(limit, z+dz*speed/float64(c.TickRate)))
}
