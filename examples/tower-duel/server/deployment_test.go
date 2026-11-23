package main

import (
	"fmt"
	"testing"
)

func TestDestroyedEnemyTowerUnlocksOnlyItsPocket(t *testing.T) {
	for seat := 0; seat < 2; seat++ {
		for _, lane := range testMap(t).Bridges {
			t.Run(fmt.Sprintf("seat%d/lane%g", seat, lane), func(t *testing.T) {
				w := playingMatch(t)
				forward := 1.0
				if seat == 1 {
					forward = -1
				}
				if r := w.deploy(seat, w.ID, 1, "guard", lane, 3*forward); r.Accepted || r.Reason != "invalid_placement" {
					t.Fatal("intact tower opened enemy territory", r)
				}
				for i := range w.Towers {
					if w.Towers[i].Seat == seat && w.Towers[i].Kind == "tower" && w.Towers[i].X == lane {
						w.Towers[i].HP = 0
					}
				}
				if r := w.deploy(seat, w.ID, 2, "guard", lane, 3*forward); r.Accepted {
					t.Fatal("losing own tower unlocked attacking territory")
				}
				for i := range w.Towers {
					if w.Towers[i].Seat != seat && w.Towers[i].Kind == "tower" && w.Towers[i].X == lane {
						w.Towers[i].HP = 0
					}
				}
				for i, p := range []point{{-lane, 3 * forward}, {0, 3 * forward}, {0, 0}, {lane, 0}, {lane, 6 * forward}, {11, 3 * forward}, {0, 11 * forward}} {
					if r := w.deploy(seat, w.ID, uint64(i+3), "guard", p.X, p.Z); r.Accepted || r.Reason != "invalid_placement" {
						t.Fatalf("pocket admitted forbidden position %+v: %+v", p, r)
					}
				}
				if w.Energy[seat] != 5 || len(w.Units) != 0 || w.HandVersion[seat] != 0 {
					t.Fatal("invalid pocket attempts changed energy, hand or units")
				}
				good := w.deploy(seat, w.ID, 20, "guard", lane, 3*forward)
				if !good.Accepted || w.Energy[seat] != 2 || len(w.Units) != 1 {
					t.Fatal("destroyed enemy tower did not unlock its pocket", good)
				}
				if again := w.deploy(seat, w.ID, 20, "guard", lane, 3*forward); again != good || len(w.Units) != 1 {
					t.Fatal("pocket deployment retry spawned twice")
				}
			})
		}
	}
}

func TestPocketFormationIsAtomicAndBothTowersOpenBothSides(t *testing.T) {
	w := playingMatch(t)
	w.Towers[4].HP = 0
	for action, p := range []point{{-.1, 3}, {-5, w.Map.barrierHalfWidth() + .6}, {-5, w.Map.barrierHalfWidth() + w.Map.PocketDepth - .3}} {
		r := w.deploy(0, w.ID, uint64(action+1), "swords", p.X, p.Z)
		if r.Accepted || w.Energy[0] != 5 || len(w.Units) != 0 {
			t.Fatal("formation crossed pocket boundary or charged on failure", p, r)
		}
	}
	w.Towers[5].HP = 0
	if r := w.deploy(0, w.ID, 4, "swords", 0, 3); !r.Accepted || r.Count != 3 || len(w.Units) != 3 || w.Energy[0] != 2 {
		t.Fatal("two destroyed towers did not open centre for whole squad", r)
	}
	reset := newMatch("next-match", w.Room, w.Map, w.Rules)
	reset.Phase = "playing"
	if r := reset.deploy(0, reset.ID, 1, "guard", -5, 3); r.Accepted {
		t.Fatal("new match inherited annexed territory")
	}
}
