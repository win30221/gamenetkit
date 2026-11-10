package gateway

import (
	"encoding/json"
	"math"
	"testing"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestPrivateMatchWSSActionAckDamageAndDuplicate(t *testing.T) {
	s, server := setup(t)
	users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
	readyRoom(t, s, server, users, []string{"t1", "t2"})
	started := startRoomHTTP(t, server, users[0])
	shooterTicket, matchID := roomTicketHTTP(t, server, users[0], false)
	targetTicket, _ := roomTicketHTTP(t, server, users[1], false)
	shooter, ctx, welcome := dialMatch(t, server, shooterTicket, matchID)
	targetConn, targetCtx, target := dialMatch(t, server, targetTicket, matchID)
	if welcome.Type != "welcome" || target.Type != "welcome" || welcome.MatchID != started.MatchID {
		t.Fatalf("combat clients failed to join: shooter=%+v target=%+v", welcome, target)
	}
	sendLoadedWS(t, shooter, ctx, welcome)
	sendLoadedWS(t, targetConn, targetCtx, target)
	waitStartWS(t, shooter, ctx)
	waitStartWS(t, targetConn, targetCtx)
	read := func() protocol.Server {
		t.Helper()
		_, data, err := shooter.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var state protocol.Server
		if err := json.Unmarshal(data, &state); err != nil {
			t.Fatal(err)
		}
		return state
	}
	// The target's first connection grants spawn protection. Wait for its
	// authoritative end tick before testing damage.
	var state protocol.Server
	for {
		state = read()
		victim := playerIn(state, target.PlayerID)
		if victim.Online && victim.ProtectedUntilTick != 0 && state.Tick >= victim.ProtectedUntilTick {
			break
		}
	}
	if playerIn(state, welcome.PlayerID).X >= playerIn(state, target.PlayerID).X {
		t.Fatal("test spawn no longer places shooter west of target")
	}
	rules := shared.Rules()
	// Production cover now blocks the direct spawn-to-spawn lane. Both players
	// walk to the open northern route before checking real network damage.
	seq := uint64(0)
	move := func(x, z float64) {
		t.Helper()
		seq++
		for _, peer := range []struct {
			c       *websocket.Conn
			welcome protocol.Server
		}{{shooter, welcome}, {targetConn, target}} {
			direction := x
			if peer.welcome.PlayerID == target.PlayerID {
				direction = -x
			}
			input, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Generation: peer.welcome.Generation, Seq: seq, X: direction, Z: z})
			if err := peer.c.Write(ctx, websocket.MessageText, input); err != nil {
				t.Fatal(err)
			}
		}
	}
	for playerIn(state, welcome.PlayerID).Z < 3 {
		move(0, 1)
		state = read()
	}
	// Spawn separation exceeds the current ranger's projectile reach. Move
	// into the actual weapon range rather than waiting for an impossible hit.
	hero, _ := rules.Hero(playerIn(state, welcome.PlayerID).HeroID)
	reach := rules.PlayerRadius + rules.ProjectileRadius + .05 + hero.ProjectileSpeed*float64(hero.ProjectileLifetimeTicks)/float64(rules.TickRate)
	for math.Abs(playerIn(state, target.PlayerID).X-playerIn(state, welcome.PlayerID).X) > reach*.8 {
		move(.75, 0)
		state = read()
	}
	move(0, 0)
	for playerIn(state, welcome.PlayerID).Ack < seq || playerIn(state, target.PlayerID).Ack < seq {
		state = read()
	}
	self, enemy := playerIn(state, welcome.PlayerID), playerIn(state, target.PlayerID)
	aimX, aimZ := enemy.X-self.X, enemy.Z-self.Z
	aimLength := math.Hypot(aimX, aimZ)
	seq++
	fire, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content,
		MatchID: matchID, Generation: welcome.Generation, Seq: seq, Action: "fire", ActionID: 1, ActionTick: state.Tick, AimX: aimX / aimLength, AimZ: aimZ / aimLength})
	if err := shooter.Write(ctx, websocket.MessageText, fire); err != nil {
		t.Fatal(err)
	}
	acked, hit := false, false
	for !acked || !hit {
		state = read()
		if state.ActionAck == 1 {
			if state.ActionCode != "fired" {
				t.Fatalf("fire was not accepted: %+v", state)
			}
			acked = true
		}
		if playerIn(state, target.PlayerID).HP == rules.MaxHP-rules.ProjectileDamage {
			hit = true
		}
	}
	// Repeating the same action ID after the cooldown must not produce another
	// projectile, even though the input sequence advances.
	duplicate, _ := json.Marshal(protocol.Client{Type: "input", Protocol: rules.Protocol, Content: rules.Content,
		MatchID: matchID, Generation: welcome.Generation, Seq: seq + 1, Action: "fire", ActionID: 1, ActionTick: state.Tick, AimX: 1})
	if err := shooter.Write(ctx, websocket.MessageText, duplicate); err != nil {
		t.Fatal(err)
	}
	until := state.Tick + uint64(rules.ProjectileLifetimeTicks+rules.FireCooldownTicks)
	for state.Tick < until {
		state = read()
		if hp := playerIn(state, target.PlayerID).HP; hp != rules.MaxHP-rules.ProjectileDamage {
			t.Fatalf("duplicate action changed target HP: %d", hp)
		}
	}
}
