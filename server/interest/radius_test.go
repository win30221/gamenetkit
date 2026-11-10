package interest

import (
	"math"
	"testing"

	"github.com/win30221/gamenetkit/server/netcore"
)

func TestHysteresisAndOwner(t *testing.T) {
	r := New(4, 1, 10)
	objects := []netcore.InterestObject{{ID: "self", X: 100, Always: true}, {ID: "other", X: 3.9}}
	if v := r.Select(0, 0, objects); !v["self"] || !v["other"] {
		t.Fatal(v)
	}
	objects[1].X = 4.5
	if !r.Select(0, 0, objects)["other"] {
		t.Fatal("hysteresis")
	}
	objects[1].X = 5.1
	if r.Select(0, 0, objects)["other"] {
		t.Fatal("exit")
	}
	objects[1].X = 4.5
	if r.Select(0, 0, objects)["other"] {
		t.Fatal("reentry threshold")
	}
	objects[1].X = 3
	if !r.Select(0, 0, objects)["other"] {
		t.Fatal("reentry")
	}
	if v := r.Select(0, 0, nil); len(v) != 0 {
		t.Fatal("removed objects retained")
	}
}

func TestOverflowKeepsAlwaysAndNearestWithStableTies(t *testing.T) {
	objects := []netcore.InterestObject{
		{ID: "mandatory", X: 100, Always: true},
		{ID: "b", X: -3},
		{ID: "nearest", Z: 1},
		{ID: "a", X: 3},
		{ID: "outside", X: 11},
	}
	for offset := range len(objects) {
		for _, reverse := range []bool{false, true} {
			reordered := make([]netcore.InterestObject, len(objects))
			for i := range objects {
				index := (i + offset) % len(objects)
				if reverse {
					index = len(objects) - index - 1
				}
				reordered[i] = objects[index]
			}
			r := New(10, 1, 3)
			visible := r.Select(0, 0, reordered)
			if len(visible) != 3 || !visible["mandatory"] || !visible["nearest"] || !visible["a"] {
				t.Fatalf("offset=%d reverse=%v: %v", offset, reverse, visible)
			}
		}
	}
}

func TestOverflowNeverDropsAlwaysObjects(t *testing.T) {
	r := New(4, 1, 1)
	visible := r.Select(0, 0, []netcore.InterestObject{
		{ID: "nearest"},
		{ID: "self", X: 100, Always: true},
		{ID: "owned", X: -100, Always: true},
	})
	if len(visible) != 2 || !visible["self"] || !visible["owned"] {
		t.Fatalf("mandatory membership was capped: %v", visible)
	}
}

func TestOverflowPreservesRadiusAndHysteresis(t *testing.T) {
	r := New(4, 1, 2)
	objects := []netcore.InterestObject{
		{ID: "self", Always: true},
		{ID: "target", X: 3.9},
		{ID: "outside", X: 9},
	}
	for _, step := range []struct {
		x       float64
		visible bool
	}{
		{3.9, true},
		{4.5, true},
		{5.1, false},
		{4.5, false},
		{3, true},
	} {
		objects[1].X = step.x
		visible := r.Select(0, 0, objects)
		if !visible["self"] || visible["target"] != step.visible || visible["outside"] || len(visible) > r.Limit {
			t.Fatalf("target x=%v: %v", step.x, visible)
		}
	}
}

func TestOverflowCapacityEvictionResetsHysteresis(t *testing.T) {
	r := New(4, 1, 2)
	objects := []netcore.InterestObject{
		{ID: "self", Always: true},
		{ID: "target", X: 3},
		{ID: "other", X: 9},
	}
	if !r.Select(0, 0, objects)["target"] {
		t.Fatal("target did not enter")
	}
	objects[1].X, objects[2].X = 4.5, 1
	if visible := r.Select(0, 0, objects); visible["target"] || !visible["other"] {
		t.Fatalf("nearer object did not replace target: %v", visible)
	}
	objects[2].X = 9
	if visible := r.Select(0, 0, objects); visible["target"] || len(visible) != 1 {
		t.Fatalf("evicted target reentered outside enter radius: %v", visible)
	}
}

func TestOverflowCountsUniqueIDsAndIgnoresInvalidDistances(t *testing.T) {
	r := New(4, 1, 3)
	visible := r.Select(0, 0, []netcore.InterestObject{
		{ID: "self", X: 100, Always: true},
		{ID: "self", X: 200, Always: true},
		{ID: "near", X: 1},
		{ID: "near", X: 2},
		{ID: "other", X: 3},
		{ID: "nan", X: math.NaN()},
		{ID: "inf", X: math.Inf(1)},
	})
	if len(visible) != 3 || !visible["self"] || !visible["near"] || !visible["other"] {
		t.Fatalf("duplicates or invalid coordinates changed membership: %v", visible)
	}
}
