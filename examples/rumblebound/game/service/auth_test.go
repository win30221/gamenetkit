package gateway

import (
	"crypto/sha256"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

const testKey = "test-only-key-at-least-24-characters"

func TestTicketSingleUseAndSessionRevocation(t *testing.T) {
	a, _ := NewAuth(testKey)
	raw, _, _ := a.NewSession(testKey)
	ticket, _ := a.Ticket(raw, false)
	var accepted atomic.Int32
	var wg sync.WaitGroup
	for range 20 {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if _, _, err := a.Consume(ticket); err == nil {
				accepted.Add(1)
			}
		}()
	}
	wg.Wait()
	if accepted.Load() != 1 {
		t.Fatal(accepted.Load())
	}
	ticket, _ = a.Ticket(raw, true)
	a.Revoke(raw)
	if _, _, err := a.Consume(ticket); err == nil {
		t.Fatal("revoked ticket accepted")
	}
	if _, ok := a.Valid(sha256.Sum256([]byte(raw))); ok {
		t.Fatal("revoked session accepted")
	}
}
func TestExpiredAndInvalidCredentials(t *testing.T) {
	a, _ := NewAuth(testKey)
	now := time.Now()
	a.now = func() time.Time { return now }
	if _, _, err := a.NewSession(strings.Repeat("x", 32)); err == nil {
		t.Fatal("invalid key")
	}
	raw, _, _ := a.NewSession(testKey)
	tk, _ := a.Ticket(raw, false)
	now = now.Add(15 * time.Second)
	if _, _, err := a.Consume(tk); err == nil {
		t.Fatal("expired ticket")
	}
	now = now.Add(time.Hour)
	if _, err := a.Ticket(raw, false); err == nil {
		t.Fatal("expired session")
	}
}
