package shared

import (
	"slices"
	"strings"
)

// CollisionWorkspace belongs to one simulation/prediction owner. Its zero value
// is ready to use. Reuse it between frames; do not share it across goroutines or
// retain references to its internal scratch slices.
type CollisionWorkspace struct {
	order, parent, rank                                   []int
	start, drive, free, basis, want, velocity, correction []Vec2
	pairs                                                 []crowdPair
	walls                                                 []crowdWall
	limits                                                []crowdLimit
	active, solved                                        []bool
	lambda, wallLambda, groupCap, mapTimes                []float64
	sourceObstacles, sortedObstacles                      []Obstacle
	mapCopy                                               ArenaMapDefinition
}

func resetScratch[T any](buffer *[]T, n int) []T {
	if cap(*buffer) < n {
		*buffer = make([]T, n)
	} else {
		*buffer = (*buffer)[:n]
		clear(*buffer)
	}
	return *buffer
}

func compareObstacle(a, b Obstacle) int { return strings.Compare(a.ID, b.ID) }

// Prepared maps keep source order for serialization/hashes and an independent,
// ID-sorted collision view. Comparing the source also supports editing fixtures
// between frames without a stale pointer-identity cache.
func (s *CollisionWorkspace) prepareMap(m *ArenaMapDefinition) *ArenaMapDefinition {
	if m == nil {
		return nil
	}
	if !slices.Equal(s.sourceObstacles, m.Obstacles) {
		s.sourceObstacles = append(s.sourceObstacles[:0], m.Obstacles...)
		s.sortedObstacles = append(s.sortedObstacles[:0], m.Obstacles...)
		slices.SortFunc(s.sortedObstacles, compareObstacle)
	}
	s.mapCopy = *m
	s.mapCopy.Obstacles = s.sortedObstacles
	return &s.mapCopy
}

// Standalone sweeps still accept unsorted caller-owned maps. The crowd path
// prepares one sorted view, so this fast path performs no sorting or allocation.
func orderedObstacles(obstacles []Obstacle) []Obstacle {
	if slices.IsSortedFunc(obstacles, compareObstacle) {
		return obstacles
	}
	copy := append([]Obstacle(nil), obstacles...)
	slices.SortFunc(copy, compareObstacle)
	return copy
}
