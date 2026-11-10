package gateway

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"testing"

	"github.com/coder/websocket"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/lobby"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

func TestConfigMapIsExactHashedSourceAndReadyRejectsWrongMap(t *testing.T) {
	s, server := setup(t)
	resp, err := server.Client().Get(server.URL + "/api/config")
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	var config struct {
		MapJSON string `json:"mapJSON"`
		MapHash string `json:"mapHash"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&config); err != nil {
		t.Fatal(err)
	}
	h := sha256.Sum256([]byte(config.MapJSON))
	if config.MapJSON != shared.ArenaMapJSON() || config.MapHash != shared.ArenaMapHash() || config.MapHash != hex.EncodeToString(h[:]) {
		t.Fatal("config map source and hash disagree")
	}
	user := newRoomIdentity(t, s)
	var room lobby.Snapshot
	roomRequest(t, server, "POST", "/api/room/create", `{"teamCount":2}`, user.token, roomOrigin, 200, &room)
	roomRequest(t, server, "POST", "/api/room/team", `{"teamID":"t1"}`, user.token, roomOrigin, 200, &room)
	for _, hash := range []string{"", "wrong"} {
		body := fmt.Sprintf(`{"ready":true,"protocol":%d,"content":%q,"mapHash":%q}`, shared.Rules().Protocol, shared.Rules().Content, hash)
		var rejected struct {
			Error string `json:"error"`
		}
		roomRequest(t, server, "POST", "/api/room/ready", body, user.token, roomOrigin, 409, &rejected)
		if rejected.Error != "map_mismatch" {
			t.Fatal("mismatched map did not report map_mismatch")
		}
	}
	current, err := s.Lobby.Current(user.user)
	if err != nil || current.Members[0].Ready {
		t.Fatal("mismatched map became ready")
	}
}

func TestPrivateLoadedRejectsMissingOrWrongFrozenMap(t *testing.T) {
	for _, hash := range []string{"", "wrong"} {
		t.Run("hash="+hash, func(t *testing.T) {
			s, server := setup(t)
			users := []roomIdentity{newRoomIdentity(t, s), newRoomIdentity(t, s)}
			readyRoom(t, s, server, users, []string{"t1", "t2"})
			startRoomHTTP(t, server, users[0])
			ticket, matchID := roomTicketHTTP(t, server, users[0], false)
			conn, ctx, welcome := dialMatch(t, server, ticket, matchID)
			if welcome.MapHash != shared.ArenaMapHash() {
				t.Fatal("welcome lacks frozen map identity")
			}
			rules := shared.Rules()
			loaded, _ := json.Marshal(protocol.Client{Type: "loaded", Protocol: rules.Protocol, Content: rules.Content, MatchID: matchID, Generation: welcome.Generation, MapHash: hash})
			if err := conn.Write(ctx, websocket.MessageText, loaded); err != nil {
				t.Fatal(err)
			}
			for {
				_, data, err := conn.Read(ctx)
				if err != nil {
					t.Fatal(err)
				}
				var state protocol.Server
				if err := json.Unmarshal(data, &state); err != nil {
					t.Fatal(err)
				}
				if state.Type == "error" {
					if state.Code != "map_mismatch" {
						t.Fatalf("unexpected rejection: %+v", state)
					}
					break
				}
				if state.Type == "start" {
					t.Fatal("wrong map crossed loading barrier")
				}
			}
		})
	}
}
