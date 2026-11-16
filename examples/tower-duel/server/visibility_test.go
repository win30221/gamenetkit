package main

import "testing"

func TestWallConcealsSmallTroopsButNotSiegeOrGates(t *testing.T) {
	for seat := 0; seat < 2; seat++ {
		w := playingMatch(t)
		z := .5
		if seat == 1 {
			z = -.5
		}
		enemy := entity{ID: 100, Seat: 1 - seat, Kind: "guard", X: 0, Z: z, Radius: guardRadius, HP: 220, MaxHP: 220}
		if !w.concealedFor(seat, enemy) || w.concealedFor(1-seat, enemy) {
			t.Fatal("wall concealment team mismatch", seat)
		}
		w.Units[100] = &unit{State: enemy}
		hidden := w.snapshot(seat, "snapshot").Units[0]
		if !hidden.Concealed || hidden.Kind != "" || hidden.HP != 0 || hidden.X != 0 || hidden.Z != 0 || hidden.Target != 0 {
			t.Fatal("concealed snapshot leaked unit details", hidden)
		}
		enemy.Kind = "siege"
		if w.concealedFor(seat, enemy) {
			t.Fatal("tall siege hidden")
		}
		enemy.Kind = "guard"
		enemy.X = 5
		if w.concealedFor(seat, enemy) {
			t.Fatal("open gate hid soldier")
		}
		enemy.X = 0
		enemy.Z = z * 4
		if w.concealedFor(seat, enemy) {
			t.Fatal("soldier away from wall hidden")
		}
		enemy.Z = z
		w.Units[101] = &unit{State: entity{ID: 101, Seat: seat, Kind: "guard", X: 1, Z: z, HP: 220}}
		if w.concealedFor(seat, enemy) {
			t.Fatal("friendly scout failed to reveal enemy")
		}
	}
}

func TestSolidWallBlocksAttacksButGateAllowsThem(t *testing.T) {
	w := playingMatch(t)
	if w.wallClear(point{0, -1}, point{0, 1}) {
		t.Fatal("shot passed through wall")
	}
	if !w.wallClear(point{5, -1}, point{5, 1}) {
		t.Fatal("open gate blocked shot")
	}
	if !w.wallClear(point{0, -1}, point{3, -1}) {
		t.Fatal("same side shot blocked")
	}
}
