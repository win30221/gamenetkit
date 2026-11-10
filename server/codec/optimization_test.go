package codec

import (
	"bytes"
	"testing"
)

func TestEncoderRingEvictionResetAndOwnership(t *testing.T) {
	e := New()
	payload := bytes.Repeat([]byte("snapshot-player-position,"), 100)
	first, _ := e.Encode(payload, 0)
	original := bytes.Clone(payload)
	payload[0] = 'X'
	next, _ := e.Encode(payload, first.ID)
	decoded, err := Decode(next, original)
	if err != nil || !bytes.Equal(decoded, payload) {
		t.Fatal("baseline ownership", err)
	}
	for i := 0; i < 63; i++ {
		e.Encode(payload, 0)
	}
	lost, _ := e.Encode(payload, first.ID)
	if lost.Base != 0 {
		t.Fatal("expired baseline retained")
	}
	e.Reset()
	reset, _ := e.Encode(payload, next.ID)
	if reset.ID != 1 || reset.Base != 0 {
		t.Fatal("reset", reset)
	}
}

func BenchmarkEncoderAcknowledged(b *testing.B) {
	payload := bytes.Repeat([]byte("snapshot-player-position,"), 200)
	e := New()
	frame, _ := e.Encode(payload, 0)
	b.ReportAllocs()
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		payload[0] = byte(i%26 + 'a')
		frame, _ = e.Encode(payload, frame.ID)
	}
}
