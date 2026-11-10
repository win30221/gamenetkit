// Package loop provides a bounded, single-owner fixed-step execution loop.
// It has no knowledge of game state, protocol envelopes or match rules.
package loop

import (
	"context"
	"errors"
	"time"
)

type Options[T any] struct {
	Interval      time.Duration
	CommandBudget int
	Commands      <-chan T
	Apply         func(T)
	Step          func()
	StopSignal    func() <-chan time.Time
	Measured      func(elapsed, interval time.Duration)
	// Scheduled observes dispatch lag, cumulative simulation lag and elapsed
	// wall time. It is diagnostic only: one notification still means one Step.
	Scheduled func(dispatchLag, simulationLag, elapsed time.Duration, steps uint64)
}

func Run[T any](ctx context.Context, options Options[T]) error {
	if options.Interval <= 0 || options.CommandBudget <= 0 || options.Apply == nil || options.Step == nil {
		return errors.New("invalid_loop_options")
	}
	origin := time.Now()
	var steps uint64
	ticker := time.NewTicker(options.Interval)
	defer ticker.Stop()
	commands := options.Commands
	for {
		var stop <-chan time.Time
		if options.StopSignal != nil {
			stop = options.StopSignal()
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-stop:
			return nil
		case scheduled := <-ticker.C:
			start := time.Now()
			steps++
			if options.Scheduled != nil {
				options.Scheduled(max(0, start.Sub(scheduled)), max(0, start.Sub(origin)-time.Duration(steps)*options.Interval), start.Sub(origin), steps)
			}
			for i := 0; i < options.CommandBudget; i++ {
				select {
				case command, ok := <-commands:
					if !ok {
						commands = nil
						i = options.CommandBudget
						break
					}
					options.Apply(command)
				default:
					i = options.CommandBudget
				}
			}
			options.Step()
			if options.Measured != nil {
				options.Measured(time.Since(start), options.Interval)
			}
		}
	}
}
