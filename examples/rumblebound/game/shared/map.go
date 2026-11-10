package shared

import (
	"crypto/sha256"
	_ "embed"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"math"
	"regexp"
	"sync"
)

//go:embed maps/arena-01.json
var arenaMapJSON string

//go:embed maps/arena-large-01.json
var largeArenaJSON string

func LargeArenaMap() *ArenaMapDefinition {
	var m ArenaMapDefinition
	if err := json.Unmarshal([]byte(largeArenaJSON), &m); err != nil {
		panic(err)
	}
	if err := ValidateArenaMap(&m, Rules().PlayerRadius); err != nil {
		panic(err)
	}
	return &m
}

// ArenaMapJSON is the exact UTF-8 source sent to clients and hashed for loading.
func ArenaMapJSON() string { return arenaMapJSON }

func ArenaMapHash() string {
	h := sha256.Sum256([]byte(arenaMapJSON))
	return hex.EncodeToString(h[:])
}

type Vec2 struct {
	X float64 `json:"x"`
	Z float64 `json:"z"`
}

type ArenaMapDefinition struct {
	MovementScale float64       `json:"movementScale,omitempty"`
	MapID         string        `json:"mapID"`
	Version       int           `json:"version"`
	ArenaHalfSize float64       `json:"arenaHalfSize"`
	ControlPoints []Point       `json:"controlPoints"`
	SpawnLayouts  []SpawnLayout `json:"spawnLayouts"`
	Obstacles     []Obstacle    `json:"obstacles"`
}

// RulesForMap is shared by admission/config and simulation. Scaling once here
// keeps prediction, bots and authority on identical walking speeds.
func RulesForMap(m *ArenaMapDefinition) Config {
	r := Rules()
	if m == nil {
		return r
	}
	r.ArenaHalfSize = m.ArenaHalfSize
	r.ControlPoints = append([]Point(nil), m.ControlPoints...)
	scale := m.MovementScale
	if scale == 0 {
		scale = 1
	}
	r.Speed *= scale
	for i := range r.Heroes {
		r.Heroes[i].Speed *= scale
	}
	return r
}

type SpawnLayout struct {
	TeamCount int     `json:"teamCount"`
	Anchors   []Point `json:"anchors"`
}

type Obstacle struct {
	ID                string  `json:"id"`
	X                 float64 `json:"x"`
	Z                 float64 `json:"z"`
	HalfX             float64 `json:"halfX"`
	HalfZ             float64 `json:"halfZ"`
	Height            float64 `json:"height"`
	BlocksMovement    bool    `json:"blocksMovement"`
	BlocksProjectiles bool    `json:"blocksProjectiles"`
	Visual            string  `json:"visual"`
}

// ArenaMap returns a fresh, validated definition: callers cannot mutate the next match.
var loadArenaMap = sync.OnceValue(func() ArenaMapDefinition {
	var m ArenaMapDefinition
	if err := json.Unmarshal([]byte(arenaMapJSON), &m); err != nil {
		panic(err)
	}
	if err := ValidateArenaMap(&m, Rules().PlayerRadius); err != nil {
		panic(err)
	}
	return m
})

func ArenaMap() *ArenaMapDefinition {
	m := loadArenaMap()
	return m.Clone()
}

// Clone makes all nested slices independent, preserving the source ordering
// used by map hashes. Callers may edit a map without changing later matches.
func (m *ArenaMapDefinition) Clone() *ArenaMapDefinition {
	if m == nil {
		return nil
	}
	copy := *m
	copy.ControlPoints = append([]Point(nil), m.ControlPoints...)
	copy.Obstacles = append([]Obstacle(nil), m.Obstacles...)
	copy.SpawnLayouts = append([]SpawnLayout(nil), m.SpawnLayouts...)
	for i := range copy.SpawnLayouts {
		copy.SpawnLayouts[i].Anchors = append([]Point(nil), m.SpawnLayouts[i].Anchors...)
	}
	return &copy
}

var mapIdentifier = regexp.MustCompile(`^[a-z0-9][a-z0-9_-]{0,63}$`)

func finite(v float64) bool { return !math.IsNaN(v) && !math.IsInf(v, 0) }

// ValidateArenaMap bounds all geometry and flood-fills the navigable circle-center
// grid. The grid is deliberately conservative: valid samples are connected only
// when the complete segment between them is clear for a player circle.
func ValidateArenaMap(m *ArenaMapDefinition, radius float64) error {
	if m == nil || !mapIdentifier.MatchString(m.MapID) || m.Version < 1 || m.Version > 1000000 {
		return fmt.Errorf("invalid map identity")
	}
	if !finite(m.MovementScale) || m.MovementScale != 0 && (m.MovementScale < .25 || m.MovementScale > 2) {
		return fmt.Errorf("invalid movement scale")
	}
	if !finite(m.ArenaHalfSize) || m.ArenaHalfSize < 4 || m.ArenaHalfSize > 100 || !finite(radius) || radius <= 0 || radius > 2 {
		return fmt.Errorf("invalid map dimensions")
	}
	if len(m.Obstacles) > 128 || len(m.ControlPoints) != 3 || len(m.SpawnLayouts) != 3 {
		return fmt.Errorf("invalid map collection sizes")
	}
	ids := make(map[string]bool)
	for _, o := range m.Obstacles {
		if !mapIdentifier.MatchString(o.ID) || ids[o.ID] {
			return fmt.Errorf("invalid or duplicate obstacle id %q", o.ID)
		}
		ids[o.ID] = true
		if !finite(o.X) || !finite(o.Z) || !finite(o.HalfX) || !finite(o.HalfZ) || !finite(o.Height) || o.HalfX <= 0 || o.HalfZ <= 0 || o.Height <= 0 || o.Height > 20 || math.Abs(o.X)+o.HalfX > m.ArenaHalfSize || math.Abs(o.Z)+o.HalfZ > m.ArenaHalfSize || o.Visual != "wall" {
			return fmt.Errorf("invalid obstacle %q", o.ID)
		}
	}
	targets := append([]Point(nil), m.ControlPoints...)
	seenTeams := make(map[int]bool)
	for _, layout := range m.SpawnLayouts {
		if layout.TeamCount < 2 || layout.TeamCount > 4 || seenTeams[layout.TeamCount] || len(layout.Anchors) != layout.TeamCount {
			return fmt.Errorf("invalid spawn layout")
		}
		seenTeams[layout.TeamCount] = true
		for _, p := range layout.Anchors {
			for _, offset := range []float64{0, 1, -1, 2} {
				targets = append(targets, Point{p.X, p.Z + offset})
			}
		}
	}
	for _, p := range targets {
		if !finite(p.X) || !finite(p.Z) || !CircleClear(m, p.X, p.Z, radius) {
			return fmt.Errorf("blocked control point or spawn at %.3f,%.3f", p.X, p.Z)
		}
	}
	// A half-unit grid also detects wall-to-wall barriers while allowing exact
	// rounded corners; no diagonal grid edges can cut through a thin obstacle.
	const spacing = 0.5
	lo := -m.ArenaHalfSize + radius
	side := int(math.Floor((m.ArenaHalfSize-radius-lo)/spacing)) + 1
	visited := make([]bool, side*side)
	queue := make([]int, 0, side*side)
	start := -1
	for index := range visited {
		x, z := lo+float64(index%side)*spacing, lo+float64(index/side)*spacing
		if CircleClear(m, x, z, radius) {
			start = index
			break
		}
	}
	if start < 0 {
		return fmt.Errorf("map has no walkable space")
	}
	visited[start] = true
	queue = append(queue, start)
	for head := 0; head < len(queue); head++ {
		i := queue[head]
		col, row := i%side, i/side
		x, z := lo+float64(col)*spacing, lo+float64(row)*spacing
		for _, step := range [][2]int{{-1, 0}, {1, 0}, {0, -1}, {0, 1}} {
			nc, nr := col+step[0], row+step[1]
			if nc < 0 || nr < 0 || nc >= side || nr >= side {
				continue
			}
			ni := nr*side + nc
			if visited[ni] {
				continue
			}
			nx, nz := lo+float64(nc)*spacing, lo+float64(nr)*spacing
			if !CircleClear(m, nx, nz, radius) {
				continue
			}
			if t, _, _, hit := FirstMapHit(m, x, z, nx-x, nz-z, radius, false); hit && t < 1-ContactEpsilon {
				continue
			}
			visited[ni] = true
			queue = append(queue, ni)
		}
	}
	for _, p := range targets {
		connected := false
		for _, i := range queue {
			x, z := lo+float64(i%side)*spacing, lo+float64(i/side)*spacing
			if math.Abs(x-p.X) > spacing*1.5 || math.Abs(z-p.Z) > spacing*1.5 {
				continue
			}
			if t, _, _, hit := FirstMapHit(m, p.X, p.Z, x-p.X, z-p.Z, radius, false); !hit || t >= 1-ContactEpsilon {
				connected = true
				break
			}
		}
		if !connected {
			return fmt.Errorf("unreachable control point or spawn at %.3f,%.3f", p.X, p.Z)
		}
	}
	return nil
}
