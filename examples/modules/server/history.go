package main

import (
	"fmt"
	"github.com/win30221/gamenetkit/server/history"
	"github.com/win30221/gamenetkit/server/lagcomp"
)

func historyLesson() error {
	// The application owns pose data and interpolation. The SDK stores clones.
	type pose struct{ X float64 }
	buffer := history.New(8, func(p pose) pose { return p })
	buffer.Add(10, pose{X: 0})
	buffer.Add(12, pose{X: 2})
	policy := lagcomp.Bounded{MaxTicks: 4}
	target, allowed := policy.Target(12, 11, 12)
	bracket, found := buffer.At(target)
	if err := require(allowed && found, "historical query should be authorized and retained"); err != nil {
		return err
	}
	alpha := float64(target-bracket.BeforeTick) / float64(bracket.AfterTick-bracket.BeforeTick)
	historicalX := bracket.Before.X + (bracket.After.X-bracket.Before.X)*alpha
	_, tooOld := policy.Target(12, 1, 12)
	if err := require(historicalX == 1 && !tooOld, "query interpolation or age bound failed"); err != nil {
		return err
	}
	fmt.Printf("history-lagcomp: current x=2, requested tick=%d -> historical x=%.1f; old query rejected\n", target, historicalX)
	// A new match owns a new history; querying never moves the live world.
	buffer.Clear()
	return require(buffer.Len() == 0, "history was not cleared")
}
