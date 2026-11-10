package codec

import (
	"math"
	"testing"
)

func TestQuantizeErrorAndInvalidInput(t *testing.T) {
	for _, value := range []float64{-9.9999, -1.23456, 0, .123456, 9.9999} {
		got, err := Quantize(value, .001)
		if err != nil || math.Abs(got-value) > .0005000001 {
			t.Fatal(value, got, err)
		}
	}
	for _, value := range []float64{math.NaN(), math.Inf(1)} {
		if _, err := Quantize(value, .001); err == nil {
			t.Fatal("nonfinite")
		}
	}
	if _, err := Quantize(1, 0); err == nil {
		t.Fatal("zero step")
	}
}
