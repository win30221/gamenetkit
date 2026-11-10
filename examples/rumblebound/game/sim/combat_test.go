package sim

import (
	"encoding/json"
	"math"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

// Gameplay fixtures use the same map/hero constructor as production. Tests
// that intentionally isolate an empty arena call newMatchWorld explicitly.
func productionWorld(t *testing.T, id string, roster map[string]string) *World {
	t.Helper()
	heroes := make(map[string]string, len(roster))
	for user := range roster {
		heroes[user] = "ranger"
	}
	w, err := newMatchWorldWithMap(id, roster, heroes, shared.ArenaMap())
	if err != nil {
		t.Fatal(err)
	}
	return w
}

func combatWorld(t *testing.T, roster map[string]string) *World {
	t.Helper()
	w := productionWorld(t, "m-combat", roster)
	for user := range roster {
		if _, err := w.Attach(user, false); err != nil {
			t.Fatal(err)
		}
	}
	for user := range roster {
		w.MarkLoaded(user, w.Seats[user].Generation)
	}
	if w.Phase != phasePlaying {
		t.Fatal("combat fixture did not start")
	}
	return w
}

func combatAction(t *testing.T, w *World, user string, seq, id uint64, kind string, aimX, aimZ float64) {
	t.Helper()
	s := w.Seats[user]
	if !w.Input(user, s.Generation, InputIntent{Generation: s.Generation, Seq: seq, Action: kind, ActionID: id, ActionTick: w.Tick, AimX: aimX, AimZ: aimZ}) {
		t.Fatal("combat input rejected")
	}
	w.Step()
}

func TestProjectileDamagesEnemyButNotAllyAndActionSurvivesReconnect(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t1", "c": "t2"})
	a, b, c := w.Seats["a"], w.Seats["b"], w.Seats["c"]
	// An open shooting lane in the real map isolates team filtering from cover.
	a.State.Z = -4
	b.State.X, b.State.Z = 0, -4 // Teammate on the shot's path.
	c.State.X, c.State.Z = 3, -4
	for range 60 {
		w.Step() // Initial spawn protection expires.
	}
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	if a.ActionAck != 1 || a.ActionCode != "fired" || len(w.Projectiles) != 1 {
		t.Fatalf("shot not acknowledged: %+v shots=%d", a, len(w.Projectiles))
	}
	for range 30 {
		w.Step()
	}
	if b.State.HP != w.Rules.MaxHP || c.State.HP != w.Rules.MaxHP-w.Rules.ProjectileDamage {
		t.Fatalf("wrong team collision: ally=%d enemy=%d", b.State.HP, c.State.HP)
	}
	w.Disconnect("a", a.Generation)
	if _, err := w.Attach("a", false); err != nil {
		t.Fatal(err)
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 2, Action: "fire", ActionID: 1, ActionTick: 60, AimX: 1}) {
		t.Fatal("repeated action input rejected")
	}
	w.Step()
	if a.ActionAck != 1 || len(w.Projectiles) != 0 || w.Snapshot(a, "welcome").ActionAck != 1 {
		t.Fatal("duplicate fire replayed or reconnect lost its acknowledgment")
	}
}

func TestDashDistanceCooldownAndExpiredActionAck(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a := w.Seats["a"]
	a.State.Z = -4 // Keep the full dash in a clear lane on the real map.
	initial := a.State.X
	combatAction(t, w, "a", 1, 1, "dash", 1, 0)
	if a.ActionAck != 1 || a.ActionCode != "dashed" || a.State.DashReadyTick != w.Tick+w.Rules.DashCooldownTicks {
		t.Fatalf("dash not acknowledged: %+v", a)
	}
	combatAction(t, w, "a", 2, 2, "dash", 1, 0)
	if a.ActionAck != 2 || a.ActionCode != "cooldown" {
		t.Fatal("cooldown rejection was not acknowledged")
	}
	for range int(w.Rules.DashTicks) - 2 {
		w.Step()
	}
	want := initial + w.Rules.DashSpeed*float64(w.Rules.DashTicks)/float64(w.Rules.TickRate)
	if math.Abs(a.State.X-want) > 1e-9 {
		t.Fatalf("dash travel got %.4f want %.4f", a.State.X, want)
	}
	w.Step()
	if math.Abs(a.State.X-want) > 1e-9 {
		t.Fatal("dash moved after its fixed duration")
	}
	for w.Tick < 110 {
		w.Step()
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 3, Action: "fire", ActionID: 3, ActionTick: 1, AimX: 1}) {
		t.Fatal("expired input dropped before action acknowledgment")
	}
	w.Step()
	if a.ActionAck != 3 || a.ActionCode != "expired" || len(w.Projectiles) != 0 {
		t.Fatal("stale action fired or was not acknowledged")
	}
	if !w.Input("a", a.Generation, InputIntent{Generation: a.Generation, Seq: 4, Action: "fire", ActionID: 4, ActionTick: w.Tick}) {
		t.Fatal("invalid aim input dropped before action acknowledgment")
	}
	w.Step()
	if a.ActionAck != 4 || a.ActionCode != "invalid_aim" || len(w.Projectiles) != 0 {
		t.Fatal("invalid aim fired or was not acknowledged")
	}
}

func TestProjectileDeathRespawnAndDisconnectedBody(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = -1, 0
	b.State.X, b.State.Z = 1, 0
	for range 60 {
		w.Step()
	}
	w.Disconnect("b", b.Generation)
	for id := uint64(1); id <= 4; id++ {
		for w.Tick < a.State.FireReadyTick {
			w.Step()
		}
		combatAction(t, w, "a", id, id, "fire", 1, 0)
		for range 4 {
			w.Step()
		}
	}
	if b.State.HP != 0 || b.State.RespawnTick <= w.Tick {
		t.Fatalf("disconnected player not killed authoritatively: %+v", b.State)
	}
	if _, err := w.Attach("b", false); err != nil {
		t.Fatal(err)
	}
	if b.State.HP != 0 || b.State.RespawnTick == 0 {
		t.Fatal("reconnect reset health or respawn timer")
	}
	for w.Tick < b.State.RespawnTick {
		w.Step()
	}
	if b.State.HP != w.Rules.MaxHP || b.State.RespawnTick != 0 || b.State.X != b.SpawnX || b.State.ProtectedUntilTick <= w.Tick {
		t.Fatalf("respawn did not restore protected spawn: %+v", b.State)
	}
}

func TestExpiredPrivateSeatLeavesCombatAndCannotRespawn(t *testing.T) {
	w := combatWorld(t, map[string]string{"a": "t1", "b": "t2", "c": "t2"})
	a, b := w.Seats["a"], w.Seats["b"]
	a.State.X, a.State.Z = -1, 0
	b.State.X, b.State.Z = 1, 0
	b.State.ProtectedUntilTick = 0
	now := time.Now()
	w.Now = func() time.Time { return now }
	w.Disconnect("b", b.Generation)
	now = now.Add(time.Duration(w.Rules.ReconnectTicks) * time.Second / time.Duration(w.Rules.TickRate))
	w.Step()
	if !b.State.Left || b.State.Online {
		t.Fatalf("expired private player was not marked left: %+v", b.State)
	}
	if _, err := w.Attach("b", false); err == nil || err.Error() != "seat_expired" {
		t.Fatalf("expired player rejoined: %v", err)
	}
	combatAction(t, w, "a", 1, 1, "fire", 1, 0)
	for range 6 {
		w.Step()
	}
	if b.State.HP != w.Rules.MaxHP {
		t.Fatalf("projectile damaged a departed player: %+v", b.State)
	}
	dead := combatWorld(t, map[string]string{"a": "t1", "b": "t2", "c": "t2"})
	departed := dead.Seats["b"]
	departed.State.HP, departed.State.RespawnTick = 0, dead.Tick+90
	deadNow := time.Now()
	dead.Now = func() time.Time { return deadNow }
	dead.Disconnect("b", departed.Generation)
	deadNow = deadNow.Add(time.Duration(dead.Rules.ReconnectTicks) * time.Second / time.Duration(dead.Rules.TickRate))
	for range 100 {
		dead.Step()
	}
	if !departed.State.Left || departed.State.HP != 0 || departed.State.RespawnTick != 0 {
		t.Fatalf("departed player respawned: %+v", departed.State)
	}
}

func TestWorstCombatSnapshotFitsReliableBound(t *testing.T) {
	roster := map[string]string{}
	for i := range 8 {
		team := "team012345678901"
		if i >= 4 {
			team = "team012345678902"
		}
		roster[string(rune('a'+i))] = team
	}
	w, err := newMatchWorld("m-"+strings.Repeat("x", 62), roster)
	if err != nil {
		t.Fatal(err)
	}
	var last *Seat
	for user := range roster {
		last, err = w.Attach(user, false)
		if err != nil {
			t.Fatal(err)
		}
		last.State.X, last.State.Z, last.State.Ack = 9.599999999999999, -9.599999999999999, 1<<53-1
		last.State.FacingX, last.State.FacingZ = 0.7071067811865476, -0.7071067811865476
		last.State.FireReadyTick, last.State.DashReadyTick, last.State.RespawnTick, last.State.ProtectedUntilTick = 9007199254740991, 9007199254740991, 9007199254740991, 9007199254740991
		last.State.Left = true
		last.ActionAck, last.ActionCode = 1<<53-1, "projectile_capacity"
	}
	w.Tick = 1<<53 - 1
	for i := 0; i < w.Rules.MaxProjectiles; i++ {
		w.Projectiles = append(w.Projectiles, shot{State: ProjectileState{ID: "b" + strings.Repeat("f", 14), OwnerID: "p8", TeamID: "team012345678901", X: 9.599999999999999, Z: -9.599999999999999, DX: 0.7071067811865476, DZ: -0.7071067811865476, ExpiresTick: 9007199254740991}})
	}
	for range maxProjectileEnds {
		w.recordProjectileEnd(ProjectileState{ID: "b" + strings.Repeat("f", 14), OwnerID: "p8", TeamID: "team012345678901"}, 9.599999999999999, -9.599999999999999, "player", "p8")
	}
	data, err := json.Marshal(w.Snapshot(last, "snapshot"))
	if err != nil {
		t.Fatal(err)
	}
	if len(data) > protocol.MaxSnapshot {
		t.Fatalf("maximum combat snapshot %d > %d", len(data), protocol.MaxSnapshot)
	}
	t.Logf("maximum combat snapshot including %d terminal endpoints: %d bytes / %d", maxProjectileEnds, len(data), protocol.MaxSnapshot)
}
