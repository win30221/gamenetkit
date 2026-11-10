package sim

import (
	"context"
	"encoding/json"
	"math"
	"strings"
	"testing"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func practiceWorld(t *testing.T, mode string) *World {
	t.Helper()
	w, err := newPracticeWorld("m_practice", map[string]string{"human": "t1", "npc": "t2"}, map[string]string{"human": "ranger", "npc": "ranger"}, shared.ArenaMap(), "human", "npc", mode)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := w.Attach("human", false); err != nil {
		t.Fatal(err)
	}
	if !w.MarkLoaded("human", w.Seats["human"].Generation) {
		t.Fatal("one human did not complete loading")
	}
	return w
}

func TestPracticeLoadingAndReservedBotControl(t *testing.T) {
	w, err := newPracticeWorld("m_practice", map[string]string{"human": "t1", "npc": "t2"}, map[string]string{"human": "tank", "npc": "ranger"}, shared.ArenaMap(), "human", "npc", "idle")
	if err != nil {
		t.Fatal(err)
	}
	bot := w.Seats["npc"]
	if !bot.State.Bot || !bot.State.Online || !bot.Loaded || bot.Generation != 1 || bot.Updates != nil {
		t.Fatal("bot was not reserved and preloaded")
	}
	for _, takeover := range []bool{false, true} {
		if _, err := w.Attach("npc", takeover); err == nil || err.Error() != "bot_reserved" {
			t.Fatal("bot accepted an external transport")
		}
	}
	if w.MarkLoaded("npc", bot.Generation) || w.Input("npc", bot.Generation, InputIntent{Generation: bot.Generation, Seq: 1, X: 1}) {
		t.Fatal("external client controlled the bot")
	}
	w.Disconnect("npc", bot.Generation)
	w.Step()
	if !bot.State.Online || bot.State.Left || w.Tick != 0 {
		t.Fatal("bot disconnect or absent human started/expired practice")
	}
	owner, err := w.Attach("human", false)
	if err != nil {
		t.Fatal(err)
	}
	w.Step()
	if w.Tick != 0 {
		t.Fatal("unloaded human did not retain loading barrier")
	}
	if !w.MarkLoaded("human", owner.Generation) || w.Phase != phasePlaying {
		t.Fatal("practice required a second human acknowledgement")
	}
	if state := <-owner.Updates; state.Type != "start" || !state.Practice || state.BotMode != "idle" {
		t.Fatal("practice start lacks metadata")
	}
	w.Step()
	w.Step()
	snapshot := w.Snapshot(owner, "snapshot")
	bots := 0
	for _, p := range snapshot.Players {
		if p.Bot {
			bots++
		}
	}
	if !snapshot.Practice || snapshot.BotMode != "idle" || bots != 1 || snapshot.MapHash != shared.ArenaMapHash() {
		t.Fatal("practice snapshot omitted reserved opponent identity")
	}
	if math.Hypot(owner.State.X-bot.State.X, owner.State.Z-bot.State.Z) < 2*w.Rules.PlayerRadius || !shared.CircleClear(w.Map, owner.State.X, owner.State.Z, w.Rules.PlayerRadius) || !shared.CircleClear(w.Map, bot.State.X, bot.State.Z, w.Rules.PlayerRadius) {
		t.Fatal("practice central spawns overlap map or each other")
	}
	encoded, _ := json.Marshal(snapshot)
	if !strings.Contains(string(encoded), `"practice":true`) || !strings.Contains(string(encoded), `"bot":true`) {
		t.Fatal("practice flags did not reach the wire")
	}
	legacy, _ := json.Marshal(New().Snapshot(&Seat{}, "snapshot"))
	if strings.Contains(string(legacy), `"practice"`) || strings.Contains(string(legacy), `"botMode"`) {
		t.Fatal("practice metadata leaked into ordinary G0 wire format")
	}
}

func TestPracticeModeSwitchAuthorizationAndIntentReset(t *testing.T) {
	w := practiceWorld(t, "move")
	bot := w.Seats["npc"]
	for range 15 {
		w.Step()
	}
	if bot.DX == 0 && bot.DZ == 0 {
		t.Fatal("move mode produced no drive")
	}
	ready := w.Tick + 25
	bot.State.FireReadyTick = ready
	bot.DashEndTick = w.Tick + 10
	bot.Pending = &actionIntent{Kind: "fire", ID: 1, Tick: w.Tick, AimX: 1}
	if err := w.setPracticeMode("intruder", "idle"); err == nil || err.Error() != "host_required" {
		t.Fatal("non-owner changed mode")
	}
	if err := w.setPracticeMode("human", "unknown"); err == nil || err.Error() != "invalid_bot_mode" {
		t.Fatal("unknown mode was accepted")
	}
	if err := w.setPracticeMode("human", "idle"); err != nil {
		t.Fatal(err)
	}
	if bot.DX != 0 || bot.DZ != 0 || bot.DashEndTick != 0 || bot.Pending != nil || len(w.practice.path) != 0 || bot.State.FireReadyTick != ready {
		t.Fatal("mode switch left old intents or reset a real cooldown")
	}
	x, z := bot.State.X, bot.State.Z
	for range 30 {
		w.Step()
	}
	if math.Hypot(bot.State.X-x, bot.State.Z-z) > 1e-8 || w.NextProjectileID != 0 || w.BotMode != "idle" {
		t.Fatal("idle continued former movement or attack")
	}
	if err := w.setPracticeMode("human", "attack"); err != nil || w.Snapshot(w.Seats["human"], "snapshot").BotMode != "attack" {
		t.Fatal("new mode was not reflected in next snapshot")
	}
	w.finish("draw", "time", "")
	if err := w.setPracticeMode("human", "move"); err == nil || err.Error() != "practice_inactive" {
		t.Fatal("finished practice accepted a mode change")
	}
	if err := New().setPracticeMode("human", "idle"); err == nil || err.Error() != "practice_inactive" {
		t.Fatal("ordinary match accepted practice control")
	}
}

func TestPracticeModesUseOrdinaryCollisionAndIdleIsPushable(t *testing.T) {
	for _, mode := range []string{"idle", "move", "attack"} {
		t.Run(mode, func(t *testing.T) {
			w := practiceWorld(t, mode)
			bot := w.Seats["npc"]
			distance := 0.0
			damaged := false
			for range 180 {
				x, z := bot.State.X, bot.State.Z
				w.Step()
				distance += math.Hypot(bot.State.X-x, bot.State.Z-z)
				if w.Seats["human"].State.HP < 100 {
					damaged = true
				}
				for _, s := range w.Seats {
					if s.State.HP > 0 && !shared.CircleClear(w.Map, s.State.X, s.State.Z, w.Rules.PlayerRadius) {
						t.Fatal("NPC mode crossed map geometry")
					}
				}
				owner := w.Seats["human"]
				if owner.State.HP > 0 && bot.State.HP > 0 && math.Hypot(owner.State.X-bot.State.X, owner.State.Z-bot.State.Z) < 2*w.Rules.PlayerRadius-1e-6 {
					t.Fatal("NPC mode crossed a living body")
				}
			}
			if !bot.State.Online || bot.State.Left {
				t.Fatal("bot expired without input packets")
			}
			if mode == "idle" && (distance > 1e-8 || w.NextProjectileID != 0) {
				t.Fatal("idle moved or fired autonomously")
			}
			if mode == "move" && (distance < 10 || w.NextProjectileID != 0) {
				t.Fatalf("patrol failed or fired: distance=%f shots=%d", distance, w.NextProjectileID)
			}
			if mode == "attack" && (!damaged || w.NextProjectileID == 0) {
				t.Fatal("attack did not use damaging projectiles")
			}
		})
	}
	w := practiceWorld(t, "idle")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = .2, 0
	for range 30 {
		owner.DX = 1
		owner.LastInput = w.Tick
		w.Step()
	}
	if bot.State.X < 2 || math.Hypot(owner.State.X-bot.State.X, owner.State.Z-bot.State.Z) < .8-1e-6 {
		t.Fatal("idle opponent did not participate in ordinary pushing")
	}
}

func TestPracticePatrolRoutesAroundCoverAndHuman(t *testing.T) {
	w := practiceWorld(t, "move")
	bot := w.Seats["npc"]
	bot.State.X, bot.State.Z = -6, 0
	w.practice.patrol = 3
	reached, detoured := false, false
	for range 240 {
		w.Step()
		if math.Abs(bot.State.Z) > 1.85 {
			detoured = true
		}
		if bot.State.X > 5.6 && math.Abs(bot.State.Z) < .4 {
			reached = true
			break
		}
	}
	if !reached || !detoured {
		t.Fatalf("NPC failed wall route: pose=(%f,%f) patrol=%d path=%v", bot.State.X, bot.State.Z, w.practice.patrol, w.practice.path)
	}
	if w.practice.planCount > int(w.Tick)/max(1, w.Rules.TickRate/2)+1 {
		t.Fatal("NPC replanned more than twice per second")
	}
}

func TestPracticeAttackRoutesBlockedSightAndUsesHeroRange(t *testing.T) {
	w := practiceWorld(t, "attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X, owner.State.Z = 6, 0
	bot.State.X, bot.State.Z = -6, 0
	owner.State.ProtectedUntilTick, bot.State.ProtectedUntilTick = 0, 0
	detoured, damaged := false, false
	for range 300 {
		w.Step()
		if math.Abs(bot.State.Z) > 1.85 {
			detoured = true
		}
		if owner.State.HP < 100 {
			damaged = true
			break
		}
	}
	if !detoured || !damaged {
		t.Fatalf("attack ground into cover or fired through it: bot=(%f,%f) hp=%d shots=%d", bot.State.X, bot.State.Z, owner.State.HP, w.NextProjectileID)
	}

	w = practiceWorld(t, "attack")
	owner, bot = w.Seats["human"], w.Seats["npc"]
	tank, _ := w.Rules.Hero("tank")
	bot.hero = &tank
	bot.State.HeroID = "tank"
	bot.State.HP = tank.MaxHP
	bot.State.X, bot.State.Z = 0, 3
	owner.State.X, owner.State.Z = 8, 3
	w.Step()
	if w.NextProjectileID != 0 || bot.DX <= 0 {
		t.Fatal("tank fired outside its own melee range instead of chasing")
	}
	for range 120 + int(tank.FireCooldownTicks) {
		w.Step()
		if owner.State.HP < 100 {
			break
		}
	}
	if owner.State.HP >= 100 || w.NextProjectileID != 0 || w.NextCombatEventID == 0 || math.Hypot(owner.State.X-bot.State.X, owner.State.Z-bot.State.Z) > tank.MeleeRange+w.Rules.PlayerRadius+.01 {
		t.Fatalf("tank did not approach into real weapon range: hp=%d distance=%f events=%d bot=%+v", owner.State.HP, math.Hypot(owner.State.X-bot.State.X, owner.State.Z-bot.State.Z), w.NextCombatEventID, bot.State)
	}
}

func TestPracticeBotDamageCooldownHumanDeathAndRespawn(t *testing.T) {
	w := practiceWorld(t, "attack")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	lastFire := uint64(0)
	shots := uint64(0)
	for step := uint64(0); step < bot.hero.FireCooldownTicks*4+bot.hero.ProjectileLifetimeTicks+90 && owner.State.HP > 0; step++ {
		w.Step()
		if w.NextProjectileID != shots {
			if shots > 0 && w.Tick-lastFire < w.seatRules(bot).FireCooldownTicks {
				t.Fatal("bot bypassed ordinary fire cooldown")
			}
			shots, lastFire = w.NextProjectileID, w.Tick
		}
	}
	if owner.State.HP != 0 || shots != 4 || owner.State.RespawnTick != w.Tick+w.Rules.RespawnTicks {
		t.Fatalf("real bot shots did not kill/queue ordinary respawn: hp=%d shots=%d", owner.State.HP, shots)
	}
	respawn := owner.State.RespawnTick
	for w.Tick+1 < respawn {
		w.Step()
		if w.NextProjectileID != shots {
			t.Fatal("bot kept shooting a dead human")
		}
	}
	w.Step()
	if owner.State.HP != 100 || owner.State.RespawnTick != 0 || owner.State.X != owner.SpawnX || owner.State.Z != owner.SpawnZ {
		t.Fatal("human did not respawn through ordinary match rules")
	}
}

func TestPracticeHumanCanKillBotAndBotRespawns(t *testing.T) {
	w := practiceWorld(t, "idle")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	owner.State.X = -1
	for action := uint64(1); action <= 4; action++ {
		for w.Tick < owner.State.FireReadyTick {
			w.Step()
		}
		if !w.Input("human", owner.Generation, InputIntent{Generation: owner.Generation, Seq: action, Action: "fire", ActionID: action, ActionTick: w.Tick, AimX: 1}) {
			t.Fatal("human input rejected")
		}
		w.Step()
		for range 5 {
			w.Step()
		}
	}
	if bot.State.HP != 0 || bot.State.RespawnTick == 0 || bot.DX != 0 || bot.DZ != 0 {
		t.Fatalf("practice NPC ignored ordinary lethal damage: hp=%d", bot.State.HP)
	}
	for bot.State.HP == 0 {
		w.Step()
	}
	if bot.State.HP != 100 || !bot.State.Bot || !bot.State.Online || bot.State.X != bot.SpawnX || bot.State.Z != bot.SpawnZ || w.BotMode != "idle" {
		t.Fatal("NPC respawn lost reserved identity, mode or safe spawn")
	}
}

func TestPracticeScoringResultsAndReconnectKeepOrdinaryRules(t *testing.T) {
	w := practiceWorld(t, "idle")
	owner, bot := w.Seats["human"], w.Seats["npc"]
	w.Step()
	bot.State.HP = 75
	oldID, oldX, oldZ := owner.State.PlayerID, owner.State.X, owner.State.Z
	w.Disconnect("human", owner.Generation)
	for range 10 {
		w.Step()
	}
	if !bot.State.Online || bot.Generation != 1 || bot.State.Left {
		t.Fatal("human reconnect wait disconnected the NPC")
	}
	if _, err := w.Attach("human", false); err != nil {
		t.Fatal(err)
	}
	if owner.State.PlayerID != oldID || owner.State.X != oldX || owner.State.Z != oldZ || bot.State.HP != 75 || w.BotMode != "idle" || w.Phase != phasePlaying {
		t.Fatal("reconnect reset practice state")
	}
	bot.State.X, bot.State.Z = 1, 5
	w.Rules.ScoreGoal = 1
	for w.Result == nil && w.Tick < 60 {
		w.Step()
	}
	if w.Result == nil || w.Result.Reason != "score" || w.Result.WinnerTeamID != owner.State.TeamID {
		t.Fatal("practice bypassed ordinary control-point settlement")
	}
	state := w.Snapshot(owner, "result")
	if !state.Practice || state.BotMode != "idle" || state.Match.Result == nil {
		t.Fatal("practice settlement lost metadata")
	}
}

func TestPracticeEngineModeCommandsAndCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	e, err := NewPracticeMatchEngine(ctx, "m_enginepractice", map[string]string{"human": "t1", "npc": "t2"}, map[string]string{"human": "ranger", "npc": "ranger"}, shared.ArenaMap(), "human", "npc", "idle")
	if err != nil {
		t.Fatal(err)
	}
	commandCtx, stop := context.WithTimeout(context.Background(), 3*time.Second)
	defer stop()
	connection, err := e.Attach(commandCtx, "human", false)
	if err != nil {
		t.Fatal(err)
	}
	e.Loaded("human", connection.Welcome.Generation)
	select {
	case <-e.Started:
	case <-commandCtx.Done():
		t.Fatal("practice engine loading barrier stalled")
	}
	if err := e.SetPracticeMode(commandCtx, "npc", "move"); err == nil || err.Error() != "host_required" {
		t.Fatal("bot could change its own mode externally")
	}
	for _, mode := range []string{"move", "attack", "idle"} {
		if err := e.SetPracticeMode(commandCtx, "human", mode); err != nil {
			t.Fatal(err)
		}
		state, err := e.InspectPlayer(commandCtx, "human")
		if err != nil || state.BotMode != mode || !state.Practice {
			t.Fatal("serialized mode command missing from next snapshot")
		}
	}
	cancel()
	select {
	case <-e.Done:
	case <-commandCtx.Done():
		t.Fatal("practice engine did not stop with owner cancellation")
	}
	if err := e.SetPracticeMode(commandCtx, "human", "move"); err == nil {
		t.Fatal("cancelled engine accepted mode change")
	}
}
