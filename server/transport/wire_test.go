package transport

import (
	"context"
	"errors"
	"testing"
	"time"
)

func TestDeliveryBudgets(t *testing.T) {
	limits := Limits{Input: 4096, Datagram: 1200, Snapshot: 16384}
	for _, tc := range []struct {
		size           int
		reliable, fail bool
	}{{1200, false, false}, {1201, true, false}, {16384, true, false}, {16385, false, true}} {
		reliable, err := limits.Reliable(tc.size, false)
		if (err != nil) != tc.fail || err == nil && reliable != tc.reliable {
			t.Fatalf("budget %d: %v %v", tc.size, reliable, err)
		}
	}
}
func TestCancelledDatagramUnblocks(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), time.Millisecond)
	defer cancel()
	released := make(chan struct{})
	err := BoundedDatagram(ctx, func() { close(released) }, func() error { <-released; return errors.New("closed") })
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatal(err)
	}
}
