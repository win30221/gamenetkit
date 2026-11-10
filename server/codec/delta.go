// Package codec encodes opaque payloads against acknowledged immutable baselines.
// Binary delta: opcode 0 + uint16 offset/length (copy); opcode 1 + uint16 length + literals.
package codec

import (
	"encoding/base64"
	"encoding/binary"
	"errors"
	"github.com/win30221/gamenetkit/server/netcore"
)

const MaxBytes = 16384

type Encoder struct {
	next   uint64
	frames map[uint64][]byte
	order  [64]uint64
	cursor int
}

func New() *Encoder       { return &Encoder{frames: map[uint64][]byte{}} }
func (e *Encoder) Reset() { e.next = 0; clear(e.frames); clear(e.order[:]); e.cursor = 0 }
func (e *Encoder) Encode(payload []byte, ack uint64) (netcore.EncodedFrame, error) {
	if len(payload) > MaxBytes {
		return netcore.EncodedFrame{}, errors.New("payload_too_large")
	}
	e.next++
	out := netcore.EncodedFrame{ID: e.next}
	encoded := payload
	if baseline, ok := e.frames[ack]; ok && ack > 0 {
		patch := delta(baseline, payload)
		// Require a useful saving after base64 and envelope overhead.
		if len(patch)+16 < len(payload) {
			out.Base = ack
			encoded = patch
		}
	}
	out.Data = base64.StdEncoding.EncodeToString(encoded)
	delete(e.frames, e.order[e.cursor])
	e.frames[out.ID] = append([]byte(nil), payload...)
	e.order[e.cursor] = out.ID
	e.cursor = (e.cursor + 1) % len(e.order)
	return out, nil
}
func key(p []byte) uint64 { return binary.LittleEndian.Uint64(p) }
func delta(base, target []byte) []byte {
	index := map[uint64][]int{}
	for i := 0; i+8 <= len(base); i++ {
		k := key(base[i:])
		if len(index[k]) < 8 {
			index[k] = append(index[k], i)
		}
	}
	out := make([]byte, 0, len(target))
	start := 0
	literal := func(end int) {
		if end <= start {
			return
		}
		n := end - start
		out = append(out, 1, byte(n), byte(n>>8))
		out = append(out, target[start:end]...)
	}
	for i := 0; i < len(target); {
		best, at := 0, 0
		if i+8 <= len(target) {
			for _, candidate := range index[key(target[i:])] {
				length := 0
				for candidate+length < len(base) && i+length < len(target) && base[candidate+length] == target[i+length] {
					length++
				}
				if length > best {
					best, at = length, candidate
				}
			}
		}
		if best >= 8 {
			literal(i)
			out = append(out, 0, byte(at), byte(at>>8), byte(best), byte(best>>8))
			i += best
			start = i
		} else {
			i++
		}
	}
	literal(len(target))
	return out
}
func Decode(frame netcore.EncodedFrame, baseline []byte) ([]byte, error) {
	if len(frame.Data) > 4*((MaxBytes+2)/3) {
		return nil, errors.New("encoded_too_large")
	}
	data, err := base64.StdEncoding.DecodeString(frame.Data)
	if err != nil {
		return nil, err
	}
	if frame.Base == 0 {
		if len(data) > MaxBytes {
			return nil, errors.New("decoded_too_large")
		}
		return data, nil
	}
	out := make([]byte, 0, len(baseline))
	for i := 0; i < len(data); {
		code := data[i]
		i++
		if code == 0 {
			if i+4 > len(data) {
				return nil, errors.New("truncated_copy")
			}
			at := int(binary.LittleEndian.Uint16(data[i:]))
			n := int(binary.LittleEndian.Uint16(data[i+2:]))
			i += 4
			if n == 0 || at+n > len(baseline) || len(out)+n > MaxBytes {
				return nil, errors.New("invalid_copy")
			}
			out = append(out, baseline[at:at+n]...)
		} else if code == 1 {
			if i+2 > len(data) {
				return nil, errors.New("truncated_literal")
			}
			n := int(binary.LittleEndian.Uint16(data[i:]))
			i += 2
			if n == 0 || i+n > len(data) || len(out)+n > MaxBytes {
				return nil, errors.New("invalid_literal")
			}
			out = append(out, data[i:i+n]...)
			i += n
		} else {
			return nil, errors.New("invalid_opcode")
		}
	}
	return out, nil
}
