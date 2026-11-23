package main

import "testing"

func TestWallBlocksExceptGatesAndAllowsAmbushDeployment(t *testing.T) {
	w := playingMatch(t)
	leftGate, rightGate := w.Map.Bridges[0], w.Map.Bridges[1]
	gateHalf := w.Map.BridgeHalfWidth
	if w.Map.Width != 20 || gateHalf != 1 || -leftGate-gateHalf != 5 || rightGate-gateHalf != 5 {
		t.Fatal("expected ten central cells and two equal outer wall spans")
	}
	if w.walkable(point{0, 0}) || w.walkable(point{4.9, 0}) || w.walkable(point{9, 0}) {
		t.Fatal("walked through wall")
	}
	for _, x := range w.Map.Bridges {
		if !w.walkable(point{x, 0}) {
			t.Fatal("gate blocked", x)
		}
	}
	for seat := 0; seat < 2; seat++ {
		z := -1.5
		if seat == 1 {
			z = 1.5
		}
		for _, x := range []float64{-9.5, 9.5} {
			if !w.deploymentPoint(seat, point{x, z}) {
				t.Fatal("outermost cell must accept a guard", seat, x)
			}
		}
		if !w.deploymentPoint(seat, point{0, z}) {
			t.Fatal("cannot deploy beside own wall", seat)
		}
		if w.deploymentPoint(seat, point{0, -z}) {
			t.Fatal("enemy wall side deploy allowed", seat)
		}
	}
}
