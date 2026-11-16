package main

import (
	"errors"
	"math"
)

type terrainZone struct {
	ID         string  `json:"id"`
	Kind       string  `json:"kind"`
	X          float64 `json:"x"`
	Z          float64 `json:"z"`
	HalfWidth  float64 `json:"halfWidth"`
	HalfLength float64 `json:"halfLength"`
	MoveFactor float64 `json:"moveFactor"`
}

func (m mapDefinition) barrierHalfWidth() float64 {
	if m.WallHalfWidth > 0 {
		return m.WallHalfWidth
	}
	return m.RiverHalfWidth
}

func (m mapDefinition) validateTerrain() error {
	if !finite(m.WallHalfWidth) || !finite(m.WallHeight) || m.WallHalfWidth < 0 || m.WallHeight < 0 {
		return errors.New("invalid wall geometry")
	}
	if m.ID == "wall-gates-mud-1" && (m.WallHalfWidth <= 0 || m.WallHeight <= 0 || len(m.TerrainZones) == 0) {
		return errors.New("wall map requires wall geometry and terrain")
	}
	for _, z := range m.TerrainZones {
		if z.ID == "" || z.Kind != "mud" || !finite(z.X) || !finite(z.Z) || !finite(z.HalfWidth) || !finite(z.HalfLength) ||
			!finite(z.MoveFactor) || z.HalfWidth <= 0 || z.HalfLength <= 0 || z.MoveFactor <= 0 || z.MoveFactor > 1 ||
			math.Abs(z.X)+z.HalfWidth > m.Width/2 || math.Abs(z.Z)+z.HalfLength > m.Length/2 {
			return errors.New("invalid movement terrain zone")
		}
	}
	return nil
}

// Foot centres determine entry and exit, including the entire gate tunnel.
// This multiplier affects walking only; frost owns attack/windup slowdowns.
func (m mapDefinition) terrainFactor(p point) float64 {
	factor := 1.0
	for _, z := range m.TerrainZones {
		if math.Abs(p.X-z.X) <= z.HalfWidth+1e-6 && math.Abs(p.Z-z.Z) <= z.HalfLength+1e-6 {
			factor = math.Min(factor, z.MoveFactor)
		}
	}
	return factor
}
