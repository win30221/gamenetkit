package protocol

import (
	"strings"
	"testing"
)

func TestRejectUntrustedPayloads(t *testing.T) {
	for _, b := range []string{`{"type":"input","health":100}`, `{"x":2}`, `{"x":1e999}`, `{"seq":9007199254740992}`, `{} {}`, strings.Repeat("x", MaxMessage+1)} {
		if _, err := Decode([]byte(b)); err == nil {
			t.Fatalf("accepted %s", b[:min(len(b), 60)])
		}
	}
}
func TestValidInput(t *testing.T) {
	if _, err := Decode([]byte(`{"type":"input","protocol":4,"content":"g1-load-1","matchID":"g0-lab","generation":1,"seq":1,"x":1,"z":0}`)); err != nil {
		t.Fatal(err)
	}
	if _, err := Decode([]byte(`{"type":"input","protocol":4,"content":"g1-load-1","matchID":"m-room","generation":1,"seq":2,"action":"fire","actionID":2,"actionTick":1,"aimX":0.8,"aimZ":0.6}`)); err != nil {
		t.Fatal(err)
	}
	// A zero vector reaches the simulation so it can acknowledge a rejected action.
	if _, err := Decode([]byte(`{"type":"input","action":"dash","actionID":3}`)); err != nil {
		t.Fatal(err)
	}
}

func TestRejectMalformedCombatActions(t *testing.T) {
	for _, payload := range []string{
		`{"type":"input","action":"fire","actionID":0,"aimX":1}`,
		`{"type":"input","action":"heal","actionID":1,"aimX":1}`,
		`{"type":"input","actionID":1}`,
		`{"type":"input","aimX":1}`,
		`{"type":"input","action":"fire","actionID":9007199254740992,"aimX":1}`,
		`{"type":"input","action":"fire","actionID":1,"actionTick":9007199254740992,"aimX":1}`,
		`{"type":"input","action":"fire","actionID":1,"aimX":1.1}`,
	} {
		if _, err := Decode([]byte(payload)); err == nil {
			t.Fatalf("accepted malformed combat action %s", payload)
		}
	}
}

func TestContinuousFacingIsIndependentOfCombatAction(t *testing.T) {
	for _, payload := range []string{
		`{"type":"input","facingX":0.6,"facingZ":0.8}`,
		`{"type":"input","facingX":1,"facingZ":1}`,
		`{"type":"input","action":"dash","actionID":1,"aimX":1,"facingZ":-1}`,
	} {
		if _, err := Decode([]byte(payload)); err != nil {
			t.Fatalf("rejected valid continuous facing %s: %v", payload, err)
		}
	}
	for _, payload := range []string{
		`{"type":"input","facingX":1.01}`,
		`{"type":"input","facingZ":-1.01}`,
		`{"type":"input","facingX":1e999}`,
		`{"type":"input","facingZ":"NaN"}`,
	} {
		if _, err := Decode([]byte(payload)); err == nil {
			t.Fatalf("accepted invalid facing %s", payload)
		}
	}
}

func TestGroundTargetPresenceAndBoundary(t *testing.T) {
	c, err := Decode([]byte(`{"type":"input","action":"fire","actionID":1,"hasGroundTarget":true,"targetX":0,"targetZ":0}`))
	if err != nil || !c.HasGroundTarget || c.TargetX != 0 || c.TargetZ != 0 {
		t.Fatal("origin ground target lost", err)
	}
	for _, payload := range []string{
		`{"action":"fire","actionID":1,"targetX":1}`,
		`{"action":"dash","actionID":1,"hasGroundTarget":true}`,
		`{"hasGroundTarget":true}`,
		`{"action":"fire","actionID":1,"hasGroundTarget":true,"targetX":101}`,
		`{"action":"fire","actionID":1,"hasGroundTarget":true,"targetZ":1e999}`,
	} {
		if _, err := Decode([]byte(payload)); err == nil {
			t.Fatalf("invalid ground target accepted %s", payload)
		}
	}
}
