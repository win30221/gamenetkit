package codec

import (
	"errors"
	"math"
)

func Quantize(value, step float64) (float64, error) {
	if step <= 0 || math.IsNaN(value) || math.IsInf(value, 0) || math.IsNaN(step) || math.IsInf(step, 0) || math.IsInf(value/step, 0) {
		return 0, errors.New("invalid_quantization")
	}
	return math.Round(value/step) * step, nil
}
