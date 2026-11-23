package main

import "testing"

func TestWallBlocksExceptGatesAndAllowsAmbushDeployment(t *testing.T) {
	w := playingMatch(t)
	if w.walkable(point{0, 0}) || w.walkable(point{3, 0}) {
		t.Fatal("walked through wall")
	}
	for _, x := range []float64{-5, 5} {
		if !w.walkable(point{x, 0}) {
			t.Fatal("gate blocked", x)
		}
	}
	for seat := 0; seat < 2; seat++ {
		z := -1.5
		if seat == 1 {
			z = 1.5
		}
		if !w.deploymentPoint(seat, point{0, z}) {
			t.Fatal("cannot deploy beside own wall", seat)
		}
		if w.deploymentPoint(seat, point{0, -z}) {
			t.Fatal("enemy wall side deploy allowed", seat)
		}
	}
}
