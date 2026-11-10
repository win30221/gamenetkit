package main

import (
	"fmt"
	"github.com/win30221/gamenetkit/server/interest"
	"github.com/win30221/gamenetkit/server/netcore"
)

func interestLesson() error {
	objects := []netcore.InterestObject{{ID: "near", X: 4}, {ID: "far", X: 20}, {ID: "score", X: 100, Always: true}}
	// One selector per recipient: hysteresis is recipient-specific state.
	selector := interest.New(5, 1, 16)
	visible := selector.Select(0, 0, objects)
	if err := require(len(visible) == 2 && visible["near"] && visible["score"], "unexpected projection"); err != nil {
		return err
	}
	objects[0].X = 5.5
	retained := selector.Select(0, 0, objects)["near"]
	objects[0].X = 6.1
	departed := !selector.Select(0, 0, objects)["near"]
	if err := require(retained && departed && len(objects) == 3, "hysteresis must not mutate the authoritative roster"); err != nil {
		return err
	}
	fmt.Println("interest: all=3 -> visible=2; near retained at 5.5, removed at 6.1; authority still has 3")
	return nil
}
