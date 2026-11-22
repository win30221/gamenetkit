package main

import "testing"

func TestWallBackedTroopsRemainInSnapshots(t *testing.T) {
	w := playingMatch(t)
	enemy := testSoldier(100, 1, "guard", 0, 1)
	w.Units[100] = enemy
	for _, seat := range []int{0, 1} {
		if got := w.snapshot(seat, "snapshot").Units[0]; got.Kind != enemy.State.Kind || got.HP != enemy.State.HP || got.Z != enemy.State.Z {
			t.Fatal("wall obscured authoritative state", got)
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
