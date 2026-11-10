package loop

import (
	"context"
	"testing"
	"time"
)

func TestBudgetOrderingAndCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	commands := make(chan int, 3)
	commands <- 1
	commands <- 2
	commands <- 3
	var applied []int
	steps := 0
	err := Run(ctx, Options[int]{Interval: time.Millisecond, CommandBudget: 2, Commands: commands, Apply: func(i int) { applied = append(applied, i) }, Step: func() {
		steps++
		if steps == 1 && len(applied) != 2 {
			t.Error("command budget exceeded")
		}
		if steps == 2 {
			cancel()
		}
	}})
	if err != context.Canceled || len(applied) != 3 || applied[2] != 3 {
		t.Fatalf("ordering/cancellation: %v %v", applied, err)
	}
}
func TestClosedQueueAndStopSignal(t *testing.T) {
	commands := make(chan int)
	close(commands)
	stop := make(chan time.Time, 1)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	steps := 0
	err := Run(ctx, Options[int]{Interval: time.Millisecond, CommandBudget: 4, Commands: commands, Apply: func(int) { t.Error("closed queue produced work") }, Step: func() {
		steps++
		if steps == 1 {
			stop <- time.Now()
		}
	}, StopSignal: func() <-chan time.Time { return stop }})
	if err != nil || steps < 1 {
		t.Fatalf("stop signal: %d %v", steps, err)
	}
}

func TestSchedulingMetricsObserveStallWithoutCatchUp(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	steps := 0
	var lag time.Duration
	err := Run(ctx, Options[int]{Interval: time.Millisecond, CommandBudget: 1, Apply: func(int) {}, Step: func() {
		steps++
		if steps == 1 {
			time.Sleep(25 * time.Millisecond)
		}
		if steps == 2 {
			cancel()
		}
	}, Scheduled: func(dispatch, behind, elapsed time.Duration, count uint64) {
		if dispatch < 0 || behind < 0 || elapsed <= 0 || count != uint64(steps+1) {
			t.Error("invalid scheduling metrics")
		}
		if count == 2 {
			lag = behind
		}
	}})
	if err != context.Canceled || steps < 2 || lag < 15*time.Millisecond {
		t.Fatalf("stall unobserved: %v steps=%d lag=%v", err, steps, lag)
	}
}
