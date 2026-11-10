package codec

import (
	"bytes"
	"encoding/base64"
	"github.com/win30221/gamenetkit/server/netcore"
	"testing"
)

func TestAckedLossRecoveryAndBounds(t *testing.T) {
	e := New()
	first := []byte(`{"entities":[{"id":"a","x":100.11,"z":200.21}],"padding":"abcdefghijklmnopqrstuvwxyz"}`)
	a, _ := e.Encode(first, 0)
	decoded, err := Decode(a, nil)
	if err != nil || !bytes.Equal(decoded, first) || a.Base != 0 {
		t.Fatal(a, err)
	}
	next := bytes.Replace(first, []byte("100.11"), []byte("100.12"), 1)
	b, _ := e.Encode(next, a.ID)
	if b.Base != a.ID || len(b.Data) >= len(a.Data) {
		t.Fatal("not a useful delta", a, b)
	}
	decoded, err = Decode(b, first)
	if err != nil || !bytes.Equal(decoded, next) {
		t.Fatal(err, string(decoded))
	}
	// A dropped b never becomes a baseline without its acknowledgement.
	c, _ := e.Encode(next, a.ID)
	if c.Base != a.ID {
		t.Fatal("unacknowledged baseline")
	}
	full, _ := e.Encode(next, 0)
	if full.Base != 0 {
		t.Fatal("resync did not produce full frame")
	}
	for i := 0; i < 65; i++ {
		e.Encode(next, 0)
	}
	evicted, _ := e.Encode(next, a.ID)
	if evicted.Base != 0 {
		t.Fatal("unbounded cache")
	}
	if _, err = e.Encode(make([]byte, MaxBytes+1), 0); err == nil {
		t.Fatal("oversized input")
	}
	for _, raw := range [][]byte{{0}, {1, 2, 0, 9}, {0, 0, 0, 0, 0}, {2}, {0, 255, 255, 1, 0}} {
		if _, err = Decode(netcore.EncodedFrame{Base: 1, Data: base64.StdEncoding.EncodeToString(raw)}, first); err == nil {
			t.Fatal("malformed patch", raw)
		}
	}
}
