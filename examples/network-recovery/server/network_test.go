package main

import (
	"encoding/json"
	"testing"
)

func TestProbeTickOwnedByWorldAndReplyQueueBounded(t *testing.T) {
	p := &peer{controls: make(chan []byte, 4)}
	w := &world{tick: 42, players: map[*peer]*player{p: {peer: p, state: playerState{ID: 1}}}}
	w.apply(command{kind: "ping", peer: p, pingID: 7})
	var reply struct {
		Type     string `json:"type"`
		Protocol int    `json:"protocol"`
		ID       uint64 `json:"id"`
		Tick     uint64 `json:"tick"`
	}
	if err := json.Unmarshal(<-p.controls, &reply); err != nil {
		t.Fatal(err)
	}
	if reply.Type != "pong" || reply.Protocol != 1 || reply.ID != 7 || reply.Tick != 42 {
		t.Fatalf("wrong probe %+v", reply)
	}
	for i := 0; i < 100; i++ {
		w.apply(command{kind: "ping", peer: p, pingID: uint64(i + 1)})
	}
	if len(p.controls) != 4 || w.tick != 42 {
		t.Fatal("probes blocked or changed simulation")
	}
}

func TestStrictProbeShape(t *testing.T) {
	for _, input := range []string{`{"type":"ping"}`, `{"type":"ping","id":1,"tick":99}`, `{"type":"ping","id":1,"id":2}`, `{"type":"ping","ID":1}`} {
		var probe struct {
			Type string `json:"type"`
			ID   uint64 `json:"id"`
		}
		if decodeStrict([]byte(input), &probe, "type", "id") == nil {
			t.Fatalf("accepted probe %s", input)
		}
	}
}
