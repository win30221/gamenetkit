package shared

import (
	_ "embed"
	"math"
	"slices"
	"strings"
)

// ContactEpsilon is shared by sweep, contact classification and the client port.
// It is a geometry tolerance, never an extra movement or push budget.
const ContactEpsilon = 1e-8

//go:embed collision-cases.json
var CollisionCases []byte

func clampCollision(v, lo, hi float64) float64 { return math.Max(lo, math.Min(hi, v)) }

// CircleClear accepts exact contact and rejects only actual penetration. A nil
// map deliberately leaves the isolated G0 transport laboratory unconstrained.
func CircleClear(m *ArenaMapDefinition, x, z, radius float64) bool {
	if !finite(x) || !finite(z) || !finite(radius) || radius < 0 {
		return false
	}
	if m == nil {
		return true
	}
	limit := m.ArenaHalfSize - radius
	if limit < 0 || math.Abs(x) > limit+ContactEpsilon || math.Abs(z) > limit+ContactEpsilon {
		return false
	}
	for _, o := range m.Obstacles {
		if !o.BlocksMovement {
			continue
		}
		cx := clampCollision(x, o.X-o.HalfX, o.X+o.HalfX)
		cz := clampCollision(z, o.Z-o.HalfZ, o.Z+o.HalfZ)
		dx, dz := x-cx, z-cz
		clearRadius := math.Max(0, radius-ContactEpsilon)
		if dx*dx+dz*dz < clearRadius*clearRadius {
			return false
		}
		// The zero-radius query must still reject the solid rectangle interior.
		if radius == 0 && x > o.X-o.HalfX && x < o.X+o.HalfX && z > o.Z-o.HalfZ && z < o.Z+o.HalfZ {
			return false
		}
	}
	return true
}

// FirstMapHit returns a unit normal pointing into the free space, and normalized
// segment time. Faces and circular corner quadrants are tested separately: an
// expanded square AABB alone would falsely block clear paths past the corners.
// Ties use boundary order (west/east/south/north), then obstacle ID, then face/
// corner order. This is independent of the serialized obstacle array ordering.
func FirstMapHit(m *ArenaMapDefinition, x, z, dx, dz, radius float64, projectile bool) (t, nx, nz float64, hit bool) {
	if m == nil || !finite(x) || !finite(z) || !finite(dx) || !finite(dz) || !finite(radius) || radius < 0 {
		return 1, 0, 0, false
	}
	t = 1
	bestKey := ""
	consider := func(candidate, cx, cz float64, key string) {
		if !finite(candidate) || candidate < -ContactEpsilon || candidate > 1+ContactEpsilon || dx*cx+dz*cz >= -ContactEpsilon {
			return
		}
		candidate = clampCollision(candidate, 0, 1)
		if !hit || candidate < t-ContactEpsilon || math.Abs(candidate-t) <= ContactEpsilon && key < bestKey {
			t, nx, nz, hit, bestKey = candidate, cx, cz, true, key
		}
	}
	limit := m.ArenaHalfSize - radius
	if dx < 0 {
		consider((-limit-x)/dx, 1, 0, "0w")
	}
	if dx > 0 {
		consider((limit-x)/dx, -1, 0, "1e")
	}
	if dz < 0 {
		consider((-limit-z)/dz, 0, 1, "2s")
	}
	if dz > 0 {
		consider((limit-z)/dz, 0, -1, "3n")
	}
	for _, o := range m.Obstacles {
		if projectile && !o.BlocksProjectiles || !projectile && !o.BlocksMovement {
			continue
		}
		minX, maxX, minZ, maxZ := o.X-o.HalfX, o.X+o.HalfX, o.Z-o.HalfZ, o.Z+o.HalfZ
		key := "4" + o.ID
		if dx > 0 {
			at := (minX - radius - x) / dx
			iz := z + dz*at
			if iz >= minZ-ContactEpsilon && iz <= maxZ+ContactEpsilon {
				consider(at, -1, 0, key+"0")
			}
		}
		if dx < 0 {
			at := (maxX + radius - x) / dx
			iz := z + dz*at
			if iz >= minZ-ContactEpsilon && iz <= maxZ+ContactEpsilon {
				consider(at, 1, 0, key+"1")
			}
		}
		if dz > 0 {
			at := (minZ - radius - z) / dz
			ix := x + dx*at
			if ix >= minX-ContactEpsilon && ix <= maxX+ContactEpsilon {
				consider(at, 0, -1, key+"2")
			}
		}
		if dz < 0 {
			at := (maxZ + radius - z) / dz
			ix := x + dx*at
			if ix >= minX-ContactEpsilon && ix <= maxX+ContactEpsilon {
				consider(at, 0, 1, key+"3")
			}
		}
		if radius <= 0 {
			continue
		}
		a := dx*dx + dz*dz
		if a <= ContactEpsilon*ContactEpsilon {
			continue
		}
		for index, c := range [][4]float64{{minX, minZ, -1, -1}, {minX, maxZ, -1, 1}, {maxX, minZ, 1, -1}, {maxX, maxZ, 1, 1}} {
			qx, qz := x-c[0], z-c[1]
			b := qx*dx + qz*dz
			cc := qx*qx + qz*qz - radius*radius
			disc := b*b - a*cc
			if disc < 0 {
				continue
			}
			at := (-b - math.Sqrt(math.Max(0, disc))) / a
			ix, iz := x+dx*at, z+dz*at
			if (ix-c[0])*c[2] < -ContactEpsilon || (iz-c[1])*c[3] < -ContactEpsilon {
				continue
			}
			vx, vz := (ix-c[0])/radius, (iz-c[1])/radius
			consider(at, vx, vz, key+string(rune('4'+index)))
		}
	}
	return
}

// repairMap is used only for invalid initial positions (e.g. stale snapshots),
// separately from movement so penetration correction cannot become push force.
func repairMap(m *ArenaMapDefinition, x, z, radius float64) (float64, float64, bool) {
	if m == nil {
		return x, z, false
	}
	startX, startZ := x, z
	limit := math.Max(0, m.ArenaHalfSize-radius)
	x, z = clampCollision(x, -limit, limit), clampCollision(z, -limit, limit)
	obstacles := orderedObstacles(m.Obstacles)
	for pass := 0; pass < 16; pass++ {
		changed := false
		for _, o := range obstacles {
			if !o.BlocksMovement {
				continue
			}
			minX, maxX, minZ, maxZ := o.X-o.HalfX, o.X+o.HalfX, o.Z-o.HalfZ, o.Z+o.HalfZ
			cx, cz := clampCollision(x, minX, maxX), clampCollision(z, minZ, maxZ)
			dx, dz := x-cx, z-cz
			length := math.Hypot(dx, dz)
			if length >= radius-ContactEpsilon && length > ContactEpsilon {
				continue
			}
			if length > ContactEpsilon {
				x = cx + dx/length*radius
				z = cz + dz/length*radius
			} else {
				// Tie order matches FirstMapHit. The nearest legal face is bounded.
				best := x - (minX - radius)
				dir := 0
				for i, d := range []float64{maxX + radius - x, z - (minZ - radius), maxZ + radius - z} {
					if d < best-ContactEpsilon {
						best = d
						dir = i + 1
					}
				}
				switch dir {
				case 0:
					x = minX - radius
				case 1:
					x = maxX + radius
				case 2:
					z = minZ - radius
				case 3:
					z = maxZ + radius
				}
			}
			x, z = clampCollision(x, -limit, limit), clampCollision(z, -limit, limit)
			changed = true
		}
		if !changed || CircleClear(m, x, z, radius) {
			break
		}
	}
	return x, z, math.Abs(x-startX) > ContactEpsilon || math.Abs(z-startZ) > ContactEpsilon
}

// SweepMap continuously moves a circle, optionally sliding the unused movement
// along contact surfaces. Dash callers pass slide=false and stop at first hit.
func SweepMap(m *ArenaMapDefinition, x, z, dx, dz, radius float64, slide bool) (float64, float64, bool) {
	if !finite(x) || !finite(z) || !finite(dx) || !finite(dz) || !finite(radius) || radius < 0 {
		return x, z, true
	}
	if m == nil {
		return x + dx, z + dz, false
	}
	x, z, repaired := repairMap(m, x, z, radius)
	blocked := repaired
	if !CircleClear(m, x, z, radius) {
		return x, z, true
	}
	for iteration := 0; iteration < 8; iteration++ {
		if math.Abs(dx)+math.Abs(dz) < ContactEpsilon {
			break
		}
		at, nx, nz, hit := FirstMapHit(m, x, z, dx, dz, radius, false)
		if !hit {
			x += dx
			z += dz
			break
		}
		x += dx * at
		z += dz * at
		blocked = true
		if !slide {
			break
		}
		dx *= 1 - at
		dz *= 1 - at
		into := dx*nx + dz*nz
		if into < 0 {
			dx -= into * nx
			dz -= into * nz
		}
	}
	return x, z, blocked
}

type CollisionBody struct {
	ID      string  `json:"id"`
	X       float64 `json:"x"`
	Z       float64 `json:"z"`
	DX      float64 `json:"dx"`
	DZ      float64 `json:"dz"`
	Speed   float64 `json:"speed"`
	Dashing bool    `json:"dashing"`
	Blocked bool    `json:"blocked"`
	Stopped bool    `json:"stopped"`
}

type crowdPair struct {
	a, b   int
	nx, nz float64
}
type crowdWall struct {
	body   int
	nx, nz float64
}

type crowdLimit struct {
	rank                   int
	nx, nz, low, high, cap float64
}

func collisionSettings(c CollisionConfig) CollisionConfig {
	if !finite(c.PushSpeed) || c.PushSpeed <= 0 {
		c.PushSpeed = 3
	}
	if !finite(c.DashPushSpeed) || c.DashPushSpeed <= 0 {
		c.DashPushSpeed = 6
	}
	if !finite(c.PushStrength) || c.PushStrength <= 0 {
		c.PushStrength = 1
	}
	if !finite(c.BodyMass) || c.BodyMass <= 0 {
		c.BodyMass = 1
	}
	if c.Iterations < 1 {
		c.Iterations = 12
	}
	if c.Iterations > 32 {
		c.Iterations = 32
	}
	return c
}

// ResolveCrowd integrates continuous relative circle contacts. Only X/Z/Blocked
// change. Drive is an intent (usually unit length, zero for idle/offline players),
// not momentum: releasing input cannot leave someone coasting after a push.
// Geometry-only position repair runs before movement and has no force budget.
func ResolveCrowd(m *ArenaMapDefinition, bodies []CollisionBody, seconds, radius float64, cfg CollisionConfig) {
	var scratch CollisionWorkspace
	scratch.ResolveCrowd(m, bodies, seconds, radius, cfg)
}

// ResolveCrowd reuses this owner-local workspace without changing contact order
// or any numerical solver steps.
func (scratch *CollisionWorkspace) ResolveCrowd(m *ArenaMapDefinition, bodies []CollisionBody, seconds, radius float64, cfg CollisionConfig) {
	if !finite(seconds) || seconds <= 0 || !finite(radius) || radius <= 0 || len(bodies) == 0 {
		return
	}
	cfg = collisionSettings(cfg)
	m = scratch.prepareMap(m)
	order := resetScratch(&scratch.order, len(bodies))
	start := resetScratch(&scratch.start, len(bodies))
	drive := resetScratch(&scratch.drive, len(bodies))
	for i := range bodies {
		order[i] = i
		b := &bodies[i]
		b.Blocked = false
		b.Stopped = false
		if !finite(b.X) || !finite(b.Z) || !finite(b.DX) || !finite(b.DZ) || !finite(b.Speed) || b.Speed < 0 {
			b.Blocked = true
			continue
		}
		start[i] = Vec2{b.X, b.Z}
		drive[i] = Vec2{b.DX, b.DZ}
		if length := math.Hypot(drive[i].X, drive[i].Z); length > 1 {
			drive[i].X /= length
			drive[i].Z /= length
		}
		b.X, b.Z, _ = repairMap(m, b.X, b.Z, radius)
	}
	slices.SortStableFunc(order, func(i, j int) int { return strings.Compare(bodies[i].ID, bodies[j].ID) })
	repairCrowd(scratch, m, bodies, order, radius, cfg.Iterations)
	// A bounded 120 Hz geometry horizon keeps the changing contact normal around
	// circles accurate. Within each horizon, exact time-of-impact events prevent
	// high-speed or opposing dashes from passing through one another.
	steps := int(math.Ceil(seconds*120 - ContactEpsilon))
	if steps < 1 {
		steps = 1
	}
	for step := 0; step < steps; step++ {
		advanceCrowd(scratch, m, bodies, order, drive, seconds/float64(steps), radius, cfg)
	}
	for i := range bodies {
		wantX, wantZ := start[i].X+drive[i].X*bodies[i].Speed*seconds, start[i].Z+drive[i].Z*bodies[i].Speed*seconds
		if math.Hypot(bodies[i].X-wantX, bodies[i].Z-wantZ) > 1e-6 {
			bodies[i].Blocked = true
		}
	}
}

func crowdContacts(scratch *CollisionWorkspace, m *ArenaMapDefinition, b []CollisionBody, order []int, radius float64) ([]crowdPair, []crowdWall) {
	const slop = 1e-7
	pairs := scratch.pairs[:0]
	walls := scratch.walls[:0]
	for ai, i := range order {
		for _, j := range order[ai+1:] {
			dx, dz := b[j].X-b[i].X, b[j].Z-b[i].Z
			length := math.Hypot(dx, dz)
			if length <= 2*radius+slop && length > ContactEpsilon {
				pairs = append(pairs, crowdPair{i, j, dx / length, dz / length})
			}
		}
		if m == nil {
			continue
		}
		limit := m.ArenaHalfSize - radius
		if b[i].X <= -limit+slop {
			walls = append(walls, crowdWall{i, 1, 0})
		}
		if b[i].X >= limit-slop {
			walls = append(walls, crowdWall{i, -1, 0})
		}
		if b[i].Z <= -limit+slop {
			walls = append(walls, crowdWall{i, 0, 1})
		}
		if b[i].Z >= limit-slop {
			walls = append(walls, crowdWall{i, 0, -1})
		}
		obstacles := orderedObstacles(m.Obstacles)
		for _, o := range obstacles {
			if !o.BlocksMovement {
				continue
			}
			cx, cz := clampCollision(b[i].X, o.X-o.HalfX, o.X+o.HalfX), clampCollision(b[i].Z, o.Z-o.HalfZ, o.Z+o.HalfZ)
			dx, dz := b[i].X-cx, b[i].Z-cz
			length := math.Hypot(dx, dz)
			if length <= radius+slop && length > ContactEpsilon {
				walls = append(walls, crowdWall{i, dx / length, dz / length})
			}
		}
	}
	scratch.pairs, scratch.walls = pairs, walls
	return pairs, walls
}

// projectCrowd is a bounded, symmetric sequential-impulse solve with accumulated
// nonnegative contact impulses. Reverse sweeps remove a fixed first-player
// advantage; accumulation permits an earlier impulse to be released when a
// different constraint changes. No iteration adds new drive or push distance.
func projectCrowd(scratch *CollisionWorkspace, v []Vec2, b []CollisionBody, pairs []crowdPair, walls []crowdWall, cfg CollisionConfig, limits []crowdLimit) ([]Vec2, []bool) {
	copy(resetScratch(&scratch.velocity, len(v)), v)
	v = scratch.velocity
	lambda := resetScratch(&scratch.lambda, len(pairs))
	wl := resetScratch(&scratch.wallLambda, len(walls))
	active := resetScratch(&scratch.solved, len(pairs))
	maxChange := 0.0
	pairStep := func(k int) {
		p := pairs[k]
		closing := (v[p.a].X-v[p.b].X)*p.nx + (v[p.a].Z-v[p.b].Z)*p.nz
		next := math.Max(0, lambda[k]+closing*0.5)
		delta := next - lambda[k]
		maxChange = math.Max(maxChange, math.Abs(delta))
		lambda[k] = next
		v[p.a].X -= delta * p.nx
		v[p.a].Z -= delta * p.nz
		v[p.b].X += delta * p.nx
		v[p.b].Z += delta * p.nz
	}
	wallStep := func(k int) {
		w := walls[k]
		into := v[w.body].X*w.nx + v[w.body].Z*w.nz
		next := math.Max(0, wl[k]-into)
		delta := next - wl[k]
		maxChange = math.Max(maxChange, math.Abs(delta))
		wl[k] = next
		v[w.body].X += delta * w.nx
		v[w.body].Z += delta * w.nz
	}
	for iteration := 0; iteration < cfg.Iterations*32; iteration++ {
		maxChange = 0
		for k := range pairs {
			pairStep(k)
		}
		for k := range walls {
			wallStep(k)
		}
		for k := len(pairs) - 1; k >= 0; k-- {
			pairStep(k)
		}
		for k := len(walls) - 1; k >= 0; k-- {
			wallStep(k)
		}
		if limits != nil {
			for i := range v {
				l := limits[i]
				if l.rank == 1 {
					normal := v[i].X*l.nx + v[i].Z*l.nz
					delta := clampCollision(normal, l.low, l.high) - normal
					maxChange = math.Max(maxChange, math.Abs(delta))
					v[i].X += delta * l.nx
					v[i].Z += delta * l.nz
				}
				if l.rank == 2 {
					if speed := math.Hypot(v[i].X, v[i].Z); speed > l.cap {
						maxChange = math.Max(maxChange, speed-l.cap)
						v[i].X *= l.cap / speed
						v[i].Z *= l.cap / speed
					}
				}
			}
		}
		if maxChange <= ContactEpsilon*.01 {
			break
		}
	}
	for k := range pairs {
		active[k] = lambda[k] > ContactEpsilon
	}
	return v, active
}

func crowdVelocity(scratch *CollisionWorkspace, m *ArenaMapDefinition, b []CollisionBody, order []int, drive []Vec2, radius float64, cfg CollisionConfig) []Vec2 {
	free := resetScratch(&scratch.free, len(b))
	for i := range b {
		free[i] = Vec2{drive[i].X * b[i].Speed, drive[i].Z * b[i].Speed}
	}
	pairs, walls := crowdContacts(scratch, m, b, order, radius)
	_, initialActive := projectCrowd(scratch, free, b, pairs, walls, cfg, nil)
	active := resetScratch(&scratch.active, len(initialActive))
	copy(active, initialActive)
	velocity := free
	// Normalized drive replaces only the constrained normal component. Thus a
	// tank moving5 and ranger moving6 exert equal opposing force, while free
	// tangential motion keeps its hero speed. A sole pusher and idle equal-mass
	// target share 2*PushSpeed intent, yielding PushSpeed3 rather than overlap
	// separation or repeatedly accumulated impulses.
	for expansion := 0; expansion < 4; expansion++ {
		parent := resetScratch(&scratch.parent, len(b))
		for i := range parent {
			parent[i] = i
		}
		var find func(int) int
		find = func(i int) int {
			for parent[i] != i {
				i = parent[i]
			}
			return i
		}
		for k, p := range pairs {
			if active[k] {
				a, c := find(p.a), find(p.b)
				parent[c] = a
			}
		}
		groupCap := resetScratch(&scratch.groupCap, len(b))
		for i := range b {
			root := find(i)
			cap := cfg.PushSpeed
			if b[i].Dashing {
				cap = cfg.DashPushSpeed
			}
			groupCap[root] = math.Max(groupCap[root], cap)
		}
		basis := resetScratch(&scratch.basis, len(b))
		rank := resetScratch(&scratch.rank, len(b))
		for k, p := range pairs {
			if !active[k] {
				continue
			}
			for _, i := range []int{p.a, p.b} {
				if rank[i] == 0 {
					basis[i] = Vec2{p.nx, p.nz}
					rank[i] = 1
				} else if math.Abs(basis[i].X*p.nz-basis[i].Z*p.nx) > 1e-6 {
					rank[i] = 2
				}
			}
		}
		want := resetScratch(&scratch.want, len(b))
		copy(want, free)
		limits := resetScratch(&scratch.limits, len(b))
		for i := range b {
			cap := groupCap[find(i)]
			motor := 2 * cap * cfg.PushStrength / cfg.BodyMass
			limits[i] = crowdLimit{rank: rank[i], nx: basis[i].X, nz: basis[i].Z, cap: math.Max(cap, math.Hypot(free[i].X, free[i].Z))}
			if rank[i] == 2 {
				want[i] = Vec2{drive[i].X * motor, drive[i].Z * motor}
			} else if rank[i] == 1 {
				n := basis[i]
				freeNormal := free[i].X*n.X + free[i].Z*n.Z
				limits[i].low = math.Min(-cap, freeNormal)
				limits[i].high = math.Max(cap, freeNormal)
				delta := (drive[i].X*motor-free[i].X)*n.X + (drive[i].Z*motor-free[i].Z)*n.Z
				want[i].X += delta * n.X
				want[i].Z += delta * n.Z
			}
		}
		var solved []bool
		velocity, solved = projectCrowd(scratch, want, b, pairs, walls, cfg, limits)
		expanded := false
		for k := range active {
			if solved[k] && !active[k] {
				active[k] = true
				expanded = true
			}
		}
		if !expanded {
			break
		}
	}
	return velocity
}

func advanceCrowd(scratch *CollisionWorkspace, m *ArenaMapDefinition, b []CollisionBody, order []int, drive []Vec2, seconds, radius float64, cfg CollisionConfig) {
	remaining := seconds
	for event := 0; event < 32 && remaining > ContactEpsilon; event++ {
		for _, i := range order {
			if !b[i].Dashing {
				continue
			}
			at, _, _, hit := FirstMapHit(m, b[i].X, b[i].Z, drive[i].X*b[i].Speed*remaining, drive[i].Z*b[i].Speed*remaining, radius, false)
			if hit && at <= ContactEpsilon {
				drive[i] = Vec2{}
				b[i].Blocked = true
				b[i].Stopped = true
			}
		}
		v := crowdVelocity(scratch, m, b, order, drive, radius, cfg)
		for _, i := range order {
			if b[i].Dashing && drive[i].X*drive[i].X+drive[i].Z*drive[i].Z > ContactEpsilon && v[i].X*drive[i].X+v[i].Z*drive[i].Z <= ContactEpsilon {
				b[i].Stopped = true
				b[i].Blocked = true
			}
		}
		earliest := 1.0
		mapTimes := resetScratch(&scratch.mapTimes, len(b))
		for i := range mapTimes {
			mapTimes[i] = 2
		}
		for _, i := range order {
			if at, _, _, hit := FirstMapHit(m, b[i].X, b[i].Z, v[i].X*remaining, v[i].Z*remaining, radius, false); hit {
				mapTimes[i] = at
				if at < earliest {
					earliest = at
				}
			}
		}
		for ai, i := range order {
			for _, j := range order[ai+1:] {
				px, pz := b[j].X-b[i].X, b[j].Z-b[i].Z
				dx, dz := (v[j].X-v[i].X)*remaining, (v[j].Z-v[i].Z)*remaining
				a := dx*dx + dz*dz
				if a <= ContactEpsilon*ContactEpsilon {
					continue
				}
				bb := px*dx + pz*dz
				if bb >= -ContactEpsilon {
					continue
				}
				c := px*px + pz*pz - 4*radius*radius
				// Touching pairs already constrain velocity. Residual solver error
				// below tolerance must not consume the entire event horizon at t=0.
				if c <= ContactEpsilon {
					continue
				}
				disc := bb*bb - a*c
				if disc < 0 {
					continue
				}
				at := (-bb - math.Sqrt(math.Max(0, disc))) / a
				if at >= -ContactEpsilon && at < earliest {
					earliest = math.Max(0, at)
				}
			}
		}
		duration := remaining * earliest
		for _, i := range order {
			oldX, oldZ := b[i].X, b[i].Z
			b[i].X, b[i].Z, _ = SweepMap(m, oldX, oldZ, v[i].X*duration, v[i].Z*duration, radius, !b[i].Dashing)
			if b[i].Dashing && mapTimes[i] <= earliest+ContactEpsilon {
				drive[i] = Vec2{}
				b[i].Blocked = true
				b[i].Stopped = true
			}
		}
		remaining -= duration
		if earliest >= 1-ContactEpsilon {
			break
		}
		if duration < ContactEpsilon {
			// A genuinely stuck contact has no legal progress; terminating is a
			// bounded safe fallback rather than advancing through the blocker.
			break
		}
	}
	// Numerical cleanup only; normal simulation reaches nonpenetrating contact
	// by exact sweep, so this normally changes no positions at all.
	repairCrowd(scratch, m, b, order, radius, cfg.Iterations)
}

func repairCrowd(scratch *CollisionWorkspace, m *ArenaMapDefinition, b []CollisionBody, order []int, radius float64, iterations int) {
	for pass := 0; pass < iterations*4; pass++ {
		correction := resetScratch(&scratch.correction, len(b))
		overlap := false
		for ai, i := range order {
			for _, j := range order[ai+1:] {
				dx, dz := b[j].X-b[i].X, b[j].Z-b[i].Z
				length := math.Hypot(dx, dz)
				if length >= 2*radius-ContactEpsilon {
					continue
				}
				overlap = true
				if length <= ContactEpsilon {
					dx, dz, length = 1, 0, 1
				}
				nx, nz := dx/length, dz/length
				amount := (2*radius - math.Hypot(b[j].X-b[i].X, b[j].Z-b[i].Z)) * 0.5
				ix, iz, _ := SweepMap(m, b[i].X, b[i].Z, -nx*amount, -nz*amount, radius, false)
				jx, jz, _ := SweepMap(m, b[j].X, b[j].Z, nx*amount, nz*amount, radius, false)
				movedI := (b[i].X-ix)*nx + (b[i].Z-iz)*nz
				movedJ := (jx-b[j].X)*nx + (jz-b[j].Z)*nz
				// If one endpoint is against a wall, let the other endpoint take
				// the unused half of the bounded separation, never into the wall.
				if movedI < amount-ContactEpsilon {
					jx, jz, _ = SweepMap(m, jx, jz, nx*(amount-movedI), nz*(amount-movedI), radius, false)
				}
				if movedJ < amount-ContactEpsilon {
					ix, iz, _ = SweepMap(m, ix, iz, -nx*(amount-movedJ), -nz*(amount-movedJ), radius, false)
				}
				correction[i].X += ix - b[i].X
				correction[i].Z += iz - b[i].Z
				correction[j].X += jx - b[j].X
				correction[j].Z += jz - b[j].Z
			}
		}
		if !overlap {
			break
		}
		for _, i := range order {
			b[i].X, b[i].Z, _ = SweepMap(m, b[i].X, b[i].Z, correction[i].X, correction[i].Z, radius, false)
		}
	}
}
