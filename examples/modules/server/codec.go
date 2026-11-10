package main

import (
	"bytes"
	"fmt"
	"github.com/win30221/gamenetkit/server/codec"
	"math"
)

func codecLesson() error {
	encoder := codec.New() // Per connection, paired with the receiver's decoded ack.
	original := bytes.Repeat([]byte("unchanged-state;"), 30)
	full, err := encoder.Encode(original, 0)
	if err != nil {
		return err
	}
	baseline, err := codec.Decode(full, nil)
	if err != nil {
		return err
	}
	next := append([]byte(nil), original...)
	next[len(next)-1] = '!'
	delta, err := encoder.Encode(next, full.ID) // Ack only after successful decoding.
	if err != nil {
		return err
	}
	decoded, err := codec.Decode(delta, baseline)
	if err != nil {
		return err
	}
	if err := require(delta.Base == full.ID && bytes.Equal(decoded, next), "delta did not round-trip"); err != nil {
		return err
	}
	_, missing := codec.Decode(delta, nil)
	recovery, err := encoder.Encode(next, 0) // Receiver requests a full baseline.
	if err != nil {
		return err
	}
	quantized, err := codec.Quantize(1.234, .1)
	if err != nil {
		return err
	}
	if err := require(missing != nil && recovery.Base == 0 && math.Abs(quantized-1.2) < 1e-9, "recovery or quantization failed"); err != nil {
		return err
	}
	fmt.Printf("codec: full base64=%d, delta base64=%d bytes; missing baseline -> full; projected 1.234 -> %.1f\n", len(full.Data), len(delta.Data), quantized)
	encoder.Reset()
	return nil
}
