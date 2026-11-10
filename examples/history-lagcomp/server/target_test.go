package main

import (
	"context"
	"encoding/json"
	"github.com/win30221/gamenetkit/server/history"
	"testing"
	"time"
)

func TestShotAndHistoryEnvelopesRejectClientAuthority(t *testing.T) {
	_, server := testApplication(t)
	for _, body := range []string{
		`{"type":"shot","id":1,"tick":0,"observed":0,"life":1,"x":0,"z":3,"compensate":true,"originX":0}`,
		`{"type":"shot","id":1,"tick":null,"observed":0,"life":1,"x":0,"z":3,"compensate":true}`,
		`{"type":"shot","id":1,"tick":0,"observed":0,"life":0,"x":0,"z":3,"compensate":true}`,
		`{"type":"history","id":1,"tick":0,"owner":2}`,
		`{"type":"history","id":1,"tick":9007199254740992}`,
		`{"type":"history","id":1,"id":2,"tick":0}`,
	} {
		c := dial(t, server)
		join(t, c)
		send(t, c, body)
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		rejected := false
		for {
			_, data, err := c.Read(ctx)
			if err != nil {
				break
			}
			var m struct{ Type, Code string }
			_ = json.Unmarshal(data, &m)
			if m.Type == "error" && m.Code == "invalid_message" {
				rejected = true
				break
			}
		}
		cancel()
		c.CloseNow()
		if !rejected {
			t.Fatal("client authority accepted", body)
		}
	}
}

func shotWorld() (*world, *player) {
	connection := &peer{controls: make(chan []byte, 64)}
	actor := &player{peer: connection, state: playerState{ID: 1, Z: -5}, lastSnapshot: 20}
	w := &world{tick: 20, target: targetState{Life: 1, HP: 5, X: 2, Z: 3}, history: history.New[targetState](90, func(value targetState) targetState { return value }), players: map[*peer]*player{connection: actor}}
	w.history.Add(10, targetState{Life: 1, HP: 5, X: 0, Z: 3})
	w.history.Add(12, targetState{Life: 1, HP: 5, X: .4, Z: 3})
	w.history.Add(20, w.target)
	return w, actor
}
func readShot(t *testing.T, p *player) shotOutcome {
	t.Helper()
	select {
	case data := <-p.peer.controls:
		var result actionResult
		if json.Unmarshal(data, &result) != nil || result.Shot == nil {
			t.Fatal("missing shot result")
		}
		return *result.Shot
	default:
		t.Fatal("missing control reply")
		return shotOutcome{}
	}
}
func TestHistoryAndCompensationNeverRewindLivePosition(t *testing.T) {
	w, p := shotWorld()
	before := w.target
	pose, ok := w.poseAt(11)
	if !ok || pose.X != .2 {
		t.Fatal("application bracket interpolation", pose, ok)
	}
	pose.X = 99
	if w.target != before {
		t.Fatal("historical query mutated authority")
	}
	s := shotMessage{ID: 1, Tick: 10, Observed: 12, Life: 1, X: 0, Z: 3}
	w.shoot(p, s)
	current := readShot(t, p)
	if !current.Accepted || current.CurrentHit || !current.HistoricalHit || current.Hit || w.target != before {
		t.Fatal("current verdict should miss without changing state", current)
	}
	s.ID = 2
	s.Compensate = true
	w.shoot(p, s)
	rewound := readShot(t, p)
	if !rewound.Accepted || !rewound.Hit || w.target.HP != 4 || w.target.X != before.X || w.target.Z != before.Z {
		t.Fatal("compensated hit should only change live HP", rewound)
	}
	w.shoot(p, s)
	duplicate := readShot(t, p)
	if duplicate != rewound || w.target.HP != 4 {
		t.Fatal("duplicate shot applied damage again")
	}
	w.inspect(p.peer, 10, 11)
	<-p.peer.controls
	if w.target.HP != 4 || w.target.X != before.X {
		t.Fatal("inspection restored historical HP/position")
	}
}
func TestShotTimeLifeAndHistoryWindow(t *testing.T) {
	w, p := shotWorld()
	for _, test := range []struct {
		shot   shotMessage
		reason string
	}{
		{shotMessage{ID: 1, Tick: 0, Observed: 20, Life: 1, Z: 3, Compensate: true}, "outside_window"},
		{shotMessage{ID: 2, Tick: 21, Observed: 21, Life: 1, Z: 3, Compensate: true}, "unobserved_tick"},
		{shotMessage{ID: 3, Tick: 10, Observed: 12, Life: 2, Z: 3, Compensate: true}, "life_changed"},
		{shotMessage{ID: 4, Tick: 8, Observed: 20, Life: 1, Z: 3, Compensate: true}, "missing_history"},
	} {
		w.shoot(p, test.shot)
		out := readShot(t, p)
		if out.Accepted || out.Hit || out.Reason != test.reason || w.target.HP != 5 {
			t.Fatal(out, test.reason)
		}
	}
	w.target.HP = 1
	w.shoot(p, shotMessage{ID: 5, Tick: 10, Observed: 12, Life: 1, Z: 3, Compensate: true})
	out := readShot(t, p)
	if !out.Hit || w.target.Life != 2 || w.target.HP != 5 || w.history.Len() != 1 {
		t.Fatal("death did not establish a fresh life/history")
	}
	if _, ok := w.poseAt(10); ok {
		t.Fatal("old-life history survived reset")
	}
}
func TestTargetHistoryBoundsAndHitscanGeometry(t *testing.T) {
	w := &world{players: map[*peer]*player{}}
	w.ensureTarget()
	for i := 0; i < 200; i++ {
		w.step(time.Now())
	}
	info := w.historyInfo()
	if info.Count != 90 || info.Last != 200 || info.First != 22 {
		t.Fatal(info)
	}
	if _, ok := w.poseAt(info.First - 1); ok {
		t.Fatal("evicted history was extrapolated")
	}
	before := w.target
	w.resetTarget()
	info = w.historyInfo()
	if info.Count != 1 || info.First != w.tick || w.target.Life != before.Life+1 {
		t.Fatal(info)
	}
	if rayHits(playerState{Z: -5}, 0, 3, targetState{X: 0, Z: 3}) != true || rayHits(playerState{Z: -5}, 0, 3, targetState{X: 1, Z: 3}) || rayHits(playerState{Z: -5}, 0, -6, targetState{X: 0, Z: 3}) {
		t.Fatal("ray origin/direction/radius checks")
	}
}
