package sim

import (
	"encoding/json"
	"fmt"
	"math"
	"testing"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func mappedWorld(t *testing.T, roster map[string]string) *World {
	t.Helper()
	heroes := make(map[string]string, len(roster))
	for user := range roster {
		heroes[user] = "ranger"
	}
	w, err := newMatchWorldWithMap("m-collision", roster, heroes, shared.ArenaMap())
	if err != nil {
		t.Fatal(err)
	}
	for user := range roster {
		if _, err := w.Attach(user, false); err != nil {
			t.Fatal(err)
		}
	}
	for user := range roster {
		w.MarkLoaded(user, w.Seats[user].Generation)
	}
	w.Rules.InputTimeoutTicks = 100000
	for _, s := range w.Seats {
		s.State.ProtectedUntilTick = 0
	}
	return w
}

func TestMappedWorldFreezesMapAndRetainsDiagnosticEmptyArena(t *testing.T) {
	m := shared.ArenaMap()
	w, err := newMatchWorldWithMap("m-frozen", map[string]string{"a": "t1", "b": "t2"}, map[string]string{"a": "tank", "b": "ranger"}, m)
	if err != nil {
		t.Fatal(err)
	}
	oldX := w.Map.Obstacles[0].X
	m.Obstacles[0].X = 8
	m.ControlPoints[0].X = 8
	if w.Map.Obstacles[0].X != oldX || w.Rules.ControlPoints[0].X != 0 || w.MapHash != shared.ArenaMapHash() {
		t.Fatal("map was not copied and identified before admission")
	}
	if snap := w.Snapshot(w.Seats["a"], "welcome"); snap.MapHash != shared.ArenaMapHash() {
		t.Fatal("welcome lost frozen map hash")
	}
	if New().Map != nil {
		t.Fatal("G0 diagnostic unexpectedly gained obstacles")
	}
	legacy, err := newMatchWorld("m-legacy", map[string]string{"a": "t1", "b": "t2"})
	if err != nil || legacy.Map != nil {
		t.Fatal("legacy fixture unexpectedly gained map")
	}
}

func TestMappedWalkingSlidesAndDashStopsAtCover(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.X, a.State.Z = -4.6, 0
	a.DX, a.DZ = 1, .2
	for range 10 {
		w.Step()
	}
	if a.State.X > -4.5+1e-5 || a.State.Z < .1 || !shared.CircleClear(w.Map, a.State.X, a.State.Z, w.Rules.PlayerRadius) {
		t.Fatalf("walking crossed cover or failed to slide: %+v", a.State)
	}
	a.State.X, a.State.Z = -4.6, 0
	a.DX, a.DZ = 0, 0
	combatAction(t, w, "a", 1, 1, "dash", 1, 0)
	if a.State.X > -4.5+1e-5 || a.DashEndTick > w.Tick {
		t.Fatalf("dash did not stop on wall: %+v end=%d tick=%d", a.State, a.DashEndTick, w.Tick)
	}
	if state := w.Snapshot(a, "snapshot"); state.Players[0].DashEndTick != 0 {
		t.Fatal("stopped dash advertised ongoing motion")
	}
}

func TestMappedAlliesEnemiesOfflineAndDeadSolidity(t *testing.T) {
	for _, team := range []string{"t1", "t2"} {
		t.Run(team, func(t *testing.T) {
			w := mappedWorld(t, map[string]string{"a": "t1", "b": team, "c": "t2"})
			a, b, c := w.Seats["a"], w.Seats["b"], w.Seats["c"]
			a.State.X, a.State.Z = 0, 3
			b.State.X, b.State.Z = .8, 3
			c.State.X, c.State.Z = 0, 8
			w.Disconnect("b", b.Generation)
			a.DX = 1
			for range 30 {
				w.Step()
			}
			if b.State.X < 1 || math.Hypot(a.State.X-b.State.X, a.State.Z-b.State.Z) < .8-1e-5 {
				t.Fatalf("offline %s body was not pushed solidly: a=%+v b=%+v", team, a.State, b.State)
			}
			pushedX := b.State.X
			if _, err := w.Attach("b", false); err != nil {
				t.Fatal(err)
			}
			if b.State.X != pushedX {
				t.Fatal("reconnect reset pushed authority position")
			}
			b.State.HP, b.State.RespawnTick = 0, 0
			a.State.X, a.State.Z = 0, 3
			b.State.X, b.State.Z = .8, 3
			for range 10 {
				w.Step()
			}
			if a.State.X < 1.5 {
				t.Fatal("dead body obstructed walking")
			}
		})
	}
}

func TestMappedEqualCounterdriveIndependentOfHeroSpeed(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	tank, _ := w.Rules.Hero("tank")
	a.hero = &tank
	a.State.X, a.State.Z = 0, 3
	b.State.X, b.State.Z = .8, 3
	a.DX, b.DX = 1, -1
	for range 30 {
		w.Step()
	}
	if math.Abs(a.State.X) > .001 || math.Abs(b.State.X-.8) > .001 {
		t.Fatalf("equal force privileged faster hero: tank=%f ranger=%f", a.State.X, b.State.X)
	}
}

func TestMappedThreeTeamChainCannotEnterBoundary(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2", "c": "t3"})
	a, b, c := w.Seats["a"], w.Seats["b"], w.Seats["c"]
	a.State.X, a.State.Z = 8, 3
	b.State.X, b.State.Z = 8.8, 3
	c.State.X, c.State.Z = 9.6, 3
	a.DX = 1
	for range 60 {
		w.Step()
	}
	if c.State.X > 9.600001 || a.State.X > 8.00001 || b.State.X > 8.80001 {
		t.Fatalf("wall backed queue compressed: %f %f %f", a.State.X, b.State.X, c.State.X)
	}
}

func TestMappedDashPushContinuesWhileMovingAndStopsAtBackedQueue(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = 0, 3
	b.State.X, b.State.Z = .8, 3
	combatAction(t, w, "a", 1, 1, "dash", 1, 0)
	if b.State.X <= .8 || a.DashEndTick <= w.Tick {
		t.Fatal("successful dash push ended on the first contact")
	}
	for range int(w.Rules.DashTicks) - 1 {
		w.Step()
	}
	if b.State.X <= 1 || b.State.X > .8+w.Rules.Collision.DashPushSpeed*float64(w.Rules.DashTicks)/float64(w.Rules.TickRate)+.00001 {
		t.Fatalf("dash push ignored duration/speed cap: %f", b.State.X)
	}
	a.State.X, a.State.Z = 8.8, 3
	b.State.X, b.State.Z = 9.6, 3
	a.State.DashReadyTick = 0
	combatAction(t, w, "a", 2, 2, "dash", 1, 0)
	if a.DashEndTick > w.Tick || a.State.X > 8.80001 || b.State.X > 9.60001 {
		t.Fatal("dash continued against an immovable wall-backed body")
	}
}

func TestMappedRespawnFindsFreeSpotOrWaits(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t1", "c": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.HP, a.State.RespawnTick = 0, w.Tick+1
	b.State.X, b.State.Z = a.SpawnX, a.SpawnZ
	w.Step()
	if a.State.HP <= 0 || math.Hypot(a.State.X-b.State.X, a.State.Z-b.State.Z) < .8 {
		t.Fatal("respawn created overlapping live bodies")
	}
	a.State.HP, a.State.RespawnTick = 0, w.Tick+1
	w.Map.Obstacles = append(w.Map.Obstacles, shared.Obstacle{ID: "blocked-spawn", X: a.SpawnX, Z: a.SpawnZ, HalfX: 3, HalfZ: 3, Height: 1, BlocksMovement: true})
	w.Step()
	if a.State.HP != 0 || a.State.RespawnTick <= w.Tick {
		t.Fatal("fully occupied spawn did not defer respawn")
	}
}

func TestMappedProjectilesRespectCoverAndMuzzleSweep(t *testing.T) {
	w := mappedWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = -5, 0
	b.State.X, b.State.Z = -2, 0
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	for range 12 {
		w.Step()
	}
	if b.State.HP != 100 || len(w.Projectiles) != 0 {
		t.Fatal("shot crossed cover")
	}
	// A thin wall can lie entirely between the actor and a nominal muzzle.
	w.Map.Obstacles[0].HalfX = .05
	a.State.X = -3.95
	for w.Tick < a.State.FireReadyTick {
		w.Step()
	}
	combatAction(t, w, "a", 2, 2, "fire", 1, 0)
	if a.ActionCode != "fired" || len(w.Projectiles) != 0 || a.State.FireReadyTick <= w.Tick {
		t.Fatal("muzzle spawned beyond thin wall or omitted cooldown")
	}
	// A closer enemy is still hit before the farther wall.
	a.State.X, a.State.Z = -6, 0
	b.State.X, b.State.Z = -4.9, 0
	for w.Tick < a.State.FireReadyTick {
		w.Step()
	}
	combatAction(t, w, "a", 3, 3, "fire", 1, 0)
	for range 5 {
		w.Step()
	}
	if b.State.HP != 75 {
		t.Fatalf("wall wrongly took priority over closer enemy: hp=%d", b.State.HP)
	}
}

func TestMappedFullPrivateSnapshotFitsReliableBudget(t *testing.T) {
	roster, heroes := map[string]string{}, map[string]string{}
	for i := range 8 {
		user := fmt.Sprintf("user%d", i)
		roster[user], heroes[user] = fmt.Sprintf("team%012d", i/2+1), "ranger"
	}
	// Match, team, player, entity and projectile IDs use the production formats;
	// the sequence acknowledgements still exercise JSON's maximum safe integer.
	w, err := newMatchWorldWithMap("m_0123456789abcdef0123456789abcdef", roster, heroes, shared.ArenaMap())
	if err != nil {
		t.Fatal(err)
	}
	for i := range 8 {
		if _, err := w.Attach(fmt.Sprintf("user%d", i), false); err != nil {
			t.Fatal(err)
		}
	}
	for i := range 8 {
		w.MarkLoaded(fmt.Sprintf("user%d", i), w.Seats[fmt.Sprintf("user%d", i)].Generation)
	}
	w.Tick = 4700 // Includes the next-point warning as well as four team scores.
	w.PointStatus, w.ScoringTeamID = "scoring", "team000000000004"
	const maximumSequence = uint64(1<<53 - 1)
	seats := w.sortedSeats()
	for i, s := range seats {
		s.State.X, s.State.Z = s.SpawnX+.123456789012345, s.SpawnZ-.123456789012345
		s.State.FacingX, s.State.FacingZ = .7071067811865476, -.7071067811865476
		s.State.Ack, s.ActionAck, s.ActionCode = maximumSequence, maximumSequence, "projectile_capacity"
		s.LastInput, s.Echo = w.Tick, 999999999999999
		s.State.FireReadyTick, s.State.DashReadyTick, s.State.ProtectedUntilTick = w.Tick+12, w.Tick+90, w.Tick+60
		s.DashDX, s.DashDZ, s.DashEndTick = .7071067811865476, -.7071067811865476, w.Tick+w.Rules.DashTicks
		if i%2 != 0 {
			s.DashDX, s.DashDZ = -s.DashDX, -s.DashDZ
		}
	}
	for i, team := range w.TeamIDs {
		w.ScoreTicks[team] = uint64(1100 + i)
	}
	w.NextProjectileID = 3000 // Reachable before this tick with eight ranger fire rates.
	for i := range w.Rules.MaxProjectiles {
		owner := seats[i%len(seats)]
		w.Projectiles = append(w.Projectiles, shot{State: ProjectileState{
			ID:      "b" + idString(w.NextProjectileID-uint64(w.Rules.MaxProjectiles)+uint64(i)+1),
			OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID,
			X:  -9.599999999999998 + float64(i)*.123456789012345,
			Z:  9.599999999999998 - float64(i)*.123456789012345,
			DX: .7071067811865476, DZ: -.7071067811865476, ExpiresTick: w.Tick + w.Rules.ProjectileLifetimeTicks,
		}, speed: w.Rules.ProjectileSpeed, damage: w.Rules.ProjectileDamage})
	}
	for i := range maxProjectileEnds {
		owner := seats[i%len(seats)]
		w.recordProjectileEnd(ProjectileState{ID: "b" + idString(w.NextProjectileID-uint64(i)), OwnerID: owner.State.PlayerID, TeamID: owner.State.TeamID}, -9.599999999999998+float64(i)*.123456789012345, 9.599999999999998-float64(i)*.123456789012345, "player", "p8")
	}
	snapshot := w.Snapshot(seats[0], "snapshot")
	if snapshot.MapHash != shared.ArenaMapHash() || len(snapshot.Players) != 8 || len(snapshot.Projectiles) != 24 || len(snapshot.ProjectileEnds) != maxProjectileEnds || snapshot.Match == nil || len(snapshot.Match.Scores) != 4 || snapshot.Match.NextPoint == nil {
		t.Fatal("fixture omitted a required mapped private snapshot field")
	}
	for _, p := range snapshot.Players {
		if p.Ack != maximumSequence || p.DashEndTick <= w.Tick || p.DashX == 0 || p.DashZ == 0 || p.FacingX == 0 || p.FacingZ == 0 {
			t.Fatal("fixture omitted full-width acknowledgement, facing or active dash metadata")
		}
	}
	data, err := json.Marshal(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	if len(data) > protocol.MaxSnapshot {
		t.Fatalf("mapped private snapshot %d > reliable budget %d", len(data), protocol.MaxSnapshot)
	}
	if len(data) <= protocol.MaxDatagram {
		t.Fatalf("full mapped private snapshot %d did not exercise reliable fallback above %d", len(data), protocol.MaxDatagram)
	}
	t.Logf("mapped private snapshot: %d bytes, %d players, %d teams, %d projectiles; datagram=%d reliable=%d", len(data), len(snapshot.Players), len(snapshot.Match.Scores), len(snapshot.Projectiles), protocol.MaxDatagram, protocol.MaxSnapshot)
	// Simultaneous melee and arrow feedback also carries the full active-dash,
	// point-warning and four-team metadata above. Keep the original 16 KiB cap.
	w.NextCombatEventID = maximumSequence - maxCombatEvents
	for range maxCombatEvents {
		w.recordCombatEvent("melee_hit", seats[0], "p8", -9.599999999999998, 9.599999999999998, .7071067811865476, -.7071067811865476)
	}
	mixed := w.Snapshot(seats[0], "snapshot")
	mixedData, err := json.Marshal(mixed)
	if err != nil {
		t.Fatal(err)
	}
	if len(mixed.CombatEvents) != maxCombatEvents || len(mixed.ProjectileEnds)+len(mixed.CombatEvents) != maxMixedTerminalEvents || len(mixedData) > protocol.MaxSnapshot {
		t.Fatalf("mapped mixed snapshot budget: %d bytes, ends=%d melee=%d", len(mixedData), len(mixed.ProjectileEnds), len(mixed.CombatEvents))
	}
	t.Logf("mapped mixed snapshot: %d bytes / %d", len(mixedData), protocol.MaxSnapshot)
	// Match completion can retain this same feedback backlog. Its result and
	// the longest accepted match ID must also fit, not just an active warning.
	w.MatchID = fmt.Sprintf("m_%062d", 0)
	w.finish("win", "forfeit", w.TeamIDs[0])
	resultData, err := json.Marshal(w.Snapshot(seats[0], "result"))
	if err != nil || len(resultData) > protocol.MaxSnapshot {
		t.Fatalf("terminal mixed snapshot budget: bytes=%d max=%d error=%v", len(resultData), protocol.MaxSnapshot, err)
	}
	t.Logf("terminal mixed snapshot: %d bytes / %d", len(resultData), protocol.MaxSnapshot)
}
