package shared

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"math"
	"math/rand"
	"testing"
)

type sweepFixture struct {
	Name                 string
	Map                  *ArenaMapDefinition
	X, Z, DX, DZ, Radius float64
	Slide                bool
	ExpectedX, ExpectedZ float64
	Blocked              bool
	Tolerance            float64
}
type hitFixture struct {
	Name                        string
	Map                         *ArenaMapDefinition
	X, Z, DX, DZ, Radius        float64
	Projectile                  bool
	ExpectedT, NormalX, NormalZ float64
	Hit                         bool
	Tolerance                   float64
}
type crowdFixture struct {
	Name            string
	Map             *ArenaMapDefinition
	Bodies          []CollisionBody
	Seconds, Radius float64
	Steps           int
	FrameRates      []int
	SwapIDs         bool
	Expected        []CollisionBody
	Tolerance       float64
}
type collisionFixtures struct {
	Version int
	Sweeps  []sweepFixture
	Hits    []hitFixture
	Crowds  []crowdFixture
}

func readCollisionFixtures(t *testing.T) collisionFixtures {
	t.Helper()
	var f collisionFixtures
	if err := json.Unmarshal(CollisionCases, &f); err != nil {
		t.Fatal(err)
	}
	if f.Version != 1 || len(f.Sweeps) < 10 || len(f.Hits) < 5 || len(f.Crowds) < 12 {
		t.Fatal("incomplete shared collision fixture")
	}
	return f
}

func TestSharedStaticCollisionFixtures(t *testing.T) {
	f := readCollisionFixtures(t)
	for _, c := range f.Sweeps {
		t.Run(c.Name, func(t *testing.T) {
			x, z, blocked := SweepMap(c.Map, c.X, c.Z, c.DX, c.DZ, c.Radius, c.Slide)
			if math.Abs(x-c.ExpectedX) > c.Tolerance || math.Abs(z-c.ExpectedZ) > c.Tolerance || blocked != c.Blocked {
				t.Fatalf("got (%.12f,%.12f) blocked=%v; want (%.12f,%.12f) blocked=%v", x, z, blocked, c.ExpectedX, c.ExpectedZ, c.Blocked)
			}
			if c.Map != nil && !CircleClear(c.Map, x, z, c.Radius) {
				t.Fatal("sweep ended inside solid geometry")
			}
		})
	}
	for _, c := range f.Hits {
		t.Run(c.Name, func(t *testing.T) {
			at, nx, nz, hit := FirstMapHit(c.Map, c.X, c.Z, c.DX, c.DZ, c.Radius, c.Projectile)
			if hit != c.Hit || math.Abs(at-c.ExpectedT) > c.Tolerance || math.Abs(nx-c.NormalX) > c.Tolerance || math.Abs(nz-c.NormalZ) > c.Tolerance {
				t.Fatalf("got t=%.12f normal=(%.12f,%.12f) hit=%v; want t=%.12f normal=(%.12f,%.12f) hit=%v", at, nx, nz, hit, c.ExpectedT, c.NormalX, c.NormalZ, c.Hit)
			}
		})
	}
}

func TestSharedCrowdFixtures(t *testing.T) {
	for _, c := range readCollisionFixtures(t).Crowds {
		t.Run(c.Name, func(t *testing.T) {
			partitions := []int{c.Steps}
			for _, fps := range c.FrameRates {
				partitions = append(partitions, int(math.Round(c.Seconds*float64(fps))))
			}
			for _, steps := range partitions {
				if steps < 1 {
					steps = 1
				}
				for variant := 0; variant < 3; variant++ {
					if variant == 2 && !c.SwapIDs {
						continue
					}
					b := append([]CollisionBody(nil), c.Bodies...)
					if variant == 1 {
						for i, j := 0, len(b)-1; i < j; i, j = i+1, j-1 {
							b[i], b[j] = b[j], b[i]
						}
					}
					if variant == 2 {
						for i := range b {
							b[i].ID = c.Bodies[len(b)-1-i].ID
						}
					}
					var workspace CollisionWorkspace
					for step := 0; step < steps; step++ {
						workspace.ResolveCrowd(c.Map, b, c.Seconds/float64(steps), c.Radius, Rules().Collision)
					}
					for i, original := range c.Bodies {
						id := original.ID
						if variant == 2 {
							id = c.Bodies[len(b)-1-i].ID
						}
						var actual CollisionBody
						for _, candidate := range b {
							if candidate.ID == id {
								actual = candidate
								break
							}
						}
						want := c.Expected[i]
						if math.Abs(actual.X-want.X) > c.Tolerance || math.Abs(actual.Z-want.Z) > c.Tolerance {
							t.Fatalf("steps=%d variant=%d body=%s got=(%.12f,%.12f) want=(%.12f,%.12f)", steps, variant, id, actual.X, actual.Z, want.X, want.Z)
						}
						if actual.Blocked != want.Blocked || actual.Stopped != want.Stopped {
							t.Fatalf("steps=%d variant=%d body=%s blocked/stopped=%v/%v want=%v/%v", steps, variant, id, actual.Blocked, actual.Stopped, want.Blocked, want.Stopped)
						}
						if !CircleClear(c.Map, actual.X, actual.Z, c.Radius) {
							t.Fatalf("body %s penetrated terrain", id)
						}
					}
					for i := range b {
						for j := i + 1; j < len(b); j++ {
							if gap := math.Hypot(b[j].X-b[i].X, b[j].Z-b[i].Z); gap < 2*c.Radius-1e-6 {
								t.Fatalf("body overlap %.12f", gap)
							}
						}
					}
				}
			}
		})
	}
}

func TestArenaMapIdentityValidationAndDetachedCopy(t *testing.T) {
	m := ArenaMap()
	if err := ValidateArenaMap(m, Rules().PlayerRadius); err != nil {
		t.Fatal(err)
	}
	h := sha256.Sum256([]byte(ArenaMapJSON()))
	if ArenaMapHash() != hex.EncodeToString(h[:]) {
		t.Fatal("map hash is not exact embedded bytes")
	}
	for _, b := range []byte(ArenaMapJSON()) {
		if b > 127 {
			t.Fatal("map source must remain ASCII for portable hash")
		}
	}
	m.Obstacles[0].X = 100
	m.ControlPoints[0].Z = 99
	m.SpawnLayouts[0].Anchors[0].X = 99
	fresh := ArenaMap()
	if fresh.Obstacles[0].X == 100 || fresh.ControlPoints[0].Z == 99 || fresh.SpawnLayouts[0].Anchors[0].X == 99 {
		t.Fatal("map mutated across matches")
	}
	for name, modify := range map[string]func(*ArenaMapDefinition){
		"nan":           func(m *ArenaMapDefinition) { m.Obstacles[0].X = math.NaN() },
		"duplicate-id":  func(m *ArenaMapDefinition) { m.Obstacles[1].ID = m.Obstacles[0].ID },
		"out-of-bounds": func(m *ArenaMapDefinition) { m.Obstacles[0].X = -10 },
		"blocked-point": func(m *ArenaMapDefinition) { m.Obstacles[0].X = 0 },
		"blocked-spawn": func(m *ArenaMapDefinition) { m.Obstacles[0].X = -6 },
		"sealed-routes": func(m *ArenaMapDefinition) {
			m.Obstacles = []Obstacle{{ID: "barrier", X: -1, HalfX: .1, HalfZ: 10, Height: 1, BlocksMovement: true, Visual: "wall"}}
		},
	} {
		t.Run(name, func(t *testing.T) {
			bad := ArenaMap()
			modify(bad)
			if ValidateArenaMap(bad, Rules().PlayerRadius) == nil {
				t.Fatal("invalid map accepted")
			}
		})
	}
}

func TestCrowdStressKeepsPlayersOutsideTerrainAndEachOther(t *testing.T) {
	m := ArenaMap()
	rng := rand.New(rand.NewSource(7481))
	radius := Rules().PlayerRadius
	for sample := 0; sample < 40; sample++ {
		b := make([]CollisionBody, 0, 8)
		for len(b) < 8 {
			x, z := rng.Float64()*16-8, rng.Float64()*16-8
			if !CircleClear(m, x, z, radius) {
				continue
			}
			clear := true
			for _, other := range b {
				if math.Hypot(x-other.X, z-other.Z) < 2*radius {
					clear = false
				}
			}
			if !clear {
				continue
			}
			angle := rng.Float64() * 2 * math.Pi
			b = append(b, CollisionBody{ID: string(rune('a' + len(b))), X: x, Z: z, DX: math.Cos(angle), DZ: math.Sin(angle), Speed: 15, Dashing: true})
		}
		for tick := 0; tick < 8; tick++ {
			ResolveCrowd(m, b, 1.0/30, radius, Rules().Collision)
			for i := range b {
				if !CircleClear(m, b[i].X, b[i].Z, radius) {
					t.Fatalf("sample%d body%d penetrated terrain", sample, i)
				}
				for j := i + 1; j < len(b); j++ {
					if d := math.Hypot(b[i].X-b[j].X, b[i].Z-b[j].Z); d < 2*radius-1e-6 {
						t.Fatalf("sample%d tick%d bodies%d,%d gap%.12f", sample, tick, i, j, d)
					}
				}
			}
		}
	}
}

func TestFullEightBodyQueueAgainstWall(t *testing.T) {
	m := &ArenaMapDefinition{ArenaHalfSize: 10}
	b := make([]CollisionBody, 8)
	for i := range b {
		b[i] = CollisionBody{ID: string(rune('a' + i)), X: 4 + float64(i)*.8, Speed: 15, Dashing: i == 0}
		if i == 0 {
			b[i].DX = 1
		}
	}
	for tick := 0; tick < 30; tick++ {
		ResolveCrowd(m, b, 1.0/30, .4, Rules().Collision)
	}
	for i := range b {
		want := 4 + float64(i)*.8
		if math.Abs(b[i].X-want) > 1e-6 {
			t.Fatalf("body%d got%.12f want%.12f", i, b[i].X, want)
		}
		if i > 0 && b[i].X-b[i-1].X < .8-1e-6 {
			t.Fatal("wall queue compressed")
		}
	}
}

func TestContinuousMapSweepsRemainClearAtRandomAngles(t *testing.T) {
	m := ArenaMap()
	rng := rand.New(rand.NewSource(8172))
	for sample := 0; sample < 10000; sample++ {
		x, z := rng.Float64()*19.2-9.6, rng.Float64()*19.2-9.6
		if !CircleClear(m, x, z, .4) {
			continue
		}
		dx, dz := rng.Float64()*40-20, rng.Float64()*40-20
		nx, nz, _ := SweepMap(m, x, z, dx, dz, .4, sample%2 == 0)
		if !CircleClear(m, nx, nz, .4) {
			t.Fatalf("sample%d from(%.15f,%.15f) delta(%.15f,%.15f) ended(%.15f,%.15f) in solid", sample, x, z, dx, dz, nx, nz)
		}
	}
}
