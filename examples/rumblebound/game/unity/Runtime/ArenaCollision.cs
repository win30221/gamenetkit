using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Rumblebound.Gameplay
{
    [Serializable] public sealed class ArenaPoint { public double x, z; }
    [Serializable] public sealed class ArenaSpawnLayout { public int teamCount; public ArenaPoint[] anchors; }
    [Serializable] public sealed class ArenaObstacle
    {
        public string id, visual;
        public double x, z, halfX, halfZ, height;
        public bool blocksMovement, blocksProjectiles;
    }
    [Serializable] public sealed class ArenaMapDefinition
    {
        public string mapID;
        public int version;
        public double arenaHalfSize;
        public double movementScale;
        public ArenaPoint[] controlPoints;
        public ArenaSpawnLayout[] spawnLayouts;
        public ArenaObstacle[] obstacles;
    }
    [Serializable] public sealed class CollisionConfig
    {
        public double pushSpeed, dashPushSpeed, pushStrength, bodyMass;
        public int iterations;
    }
    public sealed class CollisionBody
    {
        public string ID;
        public double X, Z, DX, DZ, Speed;
        public bool Dashing, Blocked, Stopped;
    }

    // Double precision, planar geometry shared with Go. These proxies never use
    // Unity colliders or change a received authoritative snapshot.
    public static class ArenaCollision
    {
        public const double ContactEpsilon = 1e-8;
        private static readonly Regex Identifier = new Regex("\\A[a-z0-9][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant);
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp(double value, double lo, double hi) { return Math.Max(lo, Math.Min(hi, value)); }
        private static double Hypot(double x, double z) { return Math.Sqrt(x * x + z * z); }

        public static bool CircleClear(ArenaMapDefinition map, double x, double z, double radius)
        {
            if (!Finite(x) || !Finite(z) || !Finite(radius) || radius < 0) return false;
            if (map == null) return true;
            double limit = map.arenaHalfSize - radius;
            if (limit < 0 || Math.Abs(x) > limit + ContactEpsilon || Math.Abs(z) > limit + ContactEpsilon) return false;
            foreach (ArenaObstacle o in map.obstacles ?? Array.Empty<ArenaObstacle>())
            {
                if (!o.blocksMovement) continue;
                double cx = Clamp(x, o.x - o.halfX, o.x + o.halfX), cz = Clamp(z, o.z - o.halfZ, o.z + o.halfZ);
                double dx = x - cx, dz = z - cz;
                double clearRadius = Math.Max(0, radius - ContactEpsilon);
                if (dx * dx + dz * dz < clearRadius * clearRadius) return false;
                if (radius == 0 && x > o.x - o.halfX && x < o.x + o.halfX && z > o.z - o.halfZ && z < o.z + o.halfZ) return false;
            }
            return true;
        }

        public static bool FirstMapHit(ArenaMapDefinition map, double x, double z, double dx, double dz, double radius,
            bool projectile, out double t, out double nx, out double nz)
        {
            t = 1; nx = 0; nz = 0;
            if (map == null || !Finite(x) || !Finite(z) || !Finite(dx) || !Finite(dz) || !Finite(radius) || radius < 0) return false;
            double bestT = 1, bestNX = 0, bestNZ = 0;
            bool hit = false;
            int bestGroup = 0; string bestID = ""; char bestFeature = '\0';
            void Consider(double candidate, double cx, double cz, int group, string id, char feature)
            {
                if (!Finite(candidate) || candidate < -ContactEpsilon || candidate > 1 + ContactEpsilon || dx * cx + dz * cz >= -ContactEpsilon) return;
                candidate = Clamp(candidate, 0, 1);
                if (!hit || candidate < bestT - ContactEpsilon || Math.Abs(candidate - bestT) <= ContactEpsilon &&
                    (group < bestGroup || group == bestGroup && CompareFeature(id, feature, bestID, bestFeature) < 0))
                { bestT = candidate; bestNX = cx; bestNZ = cz; hit = true; bestGroup = group; bestID = id; bestFeature = feature; }
            }
            double limit = map.arenaHalfSize - radius;
            if (dx < 0) Consider((-limit - x) / dx, 1, 0, 0, "", 'w');
            if (dx > 0) Consider((limit - x) / dx, -1, 0, 1, "", 'e');
            if (dz < 0) Consider((-limit - z) / dz, 0, 1, 2, "", 's');
            if (dz > 0) Consider((limit - z) / dz, 0, -1, 3, "", 'n');
            foreach (ArenaObstacle o in map.obstacles ?? Array.Empty<ArenaObstacle>())
            {
                if (projectile && !o.blocksProjectiles || !projectile && !o.blocksMovement) continue;
                double minX = o.x - o.halfX, maxX = o.x + o.halfX, minZ = o.z - o.halfZ, maxZ = o.z + o.halfZ;
                if (dx > 0)
                {
                    double at = (minX - radius - x) / dx, iz = z + dz * at;
                    if (iz >= minZ - ContactEpsilon && iz <= maxZ + ContactEpsilon) Consider(at, -1, 0, 4, o.id, '0');
                }
                if (dx < 0)
                {
                    double at = (maxX + radius - x) / dx, iz = z + dz * at;
                    if (iz >= minZ - ContactEpsilon && iz <= maxZ + ContactEpsilon) Consider(at, 1, 0, 4, o.id, '1');
                }
                if (dz > 0)
                {
                    double at = (minZ - radius - z) / dz, ix = x + dx * at;
                    if (ix >= minX - ContactEpsilon && ix <= maxX + ContactEpsilon) Consider(at, 0, -1, 4, o.id, '2');
                }
                if (dz < 0)
                {
                    double at = (maxZ + radius - z) / dz, ix = x + dx * at;
                    if (ix >= minX - ContactEpsilon && ix <= maxX + ContactEpsilon) Consider(at, 0, 1, 4, o.id, '3');
                }
                if (radius <= 0) continue;
                double a = dx * dx + dz * dz;
                if (a <= ContactEpsilon * ContactEpsilon) continue;
                for (int index = 0; index < 4; ++index)
                {
                    double cornerX = index < 2 ? minX : maxX, cornerZ = index % 2 == 0 ? minZ : maxZ;
                    double signX = index < 2 ? -1 : 1, signZ = index % 2 == 0 ? -1 : 1;
                    double qx = x - cornerX, qz = z - cornerZ, b = qx * dx + qz * dz;
                    double cc = qx * qx + qz * qz - radius * radius, disc = b * b - a * cc;
                    if (disc < 0) continue;
                    double at = (-b - Math.Sqrt(Math.Max(0, disc))) / a, ix = x + dx * at, iz = z + dz * at;
                    if ((ix - cornerX) * signX < -ContactEpsilon || (iz - cornerZ) * signZ < -ContactEpsilon) continue;
                    Consider(at, (ix - cornerX) / radius, (iz - cornerZ) / radius, 4, o.id, (char)('4' + index));
                }
            }
            t = bestT; nx = bestNX; nz = bestNZ;
            return hit;
        }

        // Compare the original concatenated ID + feature keys without allocating.
        // Prefix IDs matter: comparing the IDs separately changes tie ordering.
        private static int CompareFeature(string a, char af, string b, char bf)
        {
            a = a ?? ""; b = b ?? "";
            int length = Math.Min(a.Length, b.Length) + 1;
            for (int i = 0; i < length; i++)
            {
                char ac = i < a.Length ? a[i] : af, bc = i < b.Length ? b[i] : bf;
                if (ac != bc) return ac.CompareTo(bc);
            }
            return a.Length.CompareTo(b.Length);
        }

        [ThreadStatic] private static ObstacleOrder obstacleOrder;
        private sealed class ObstacleOrder
        {
            public ArenaObstacle[] source = Array.Empty<ArenaObstacle>(), sorted = Array.Empty<ArenaObstacle>();
            public string[] ids = Array.Empty<string>();
        }
        private static ArenaObstacle[] OrderedObstacles(ArenaMapDefinition map)
        {
            var source = map.obstacles ?? Array.Empty<ArenaObstacle>();
            var cache = obstacleOrder ?? (obstacleOrder = new ObstacleOrder());
            bool changed = cache.source.Length != source.Length;
            if (!changed)
                for (int i = 0; i < source.Length; i++)
                    if (!ReferenceEquals(cache.source[i], source[i]) || cache.ids[i] != source[i].id) { changed = true; break; }
            if (changed)
            {
                if (cache.source.Length != source.Length)
                {
                    cache.source = new ArenaObstacle[source.Length]; cache.sorted = new ArenaObstacle[source.Length];
                    cache.ids = new string[source.Length];
                }
                Array.Copy(source, cache.source, source.Length); Array.Copy(source, cache.sorted, source.Length);
                for (int i = 0; i < source.Length; i++) cache.ids[i] = source[i].id;
                Array.Sort(cache.sorted, (a, b) => string.CompareOrdinal(a.id, b.id));
            }
            return cache.sorted;
        }

        private static void RepairMap(ArenaMapDefinition map, ref double x, ref double z, double radius, out bool repaired)
        {
            repaired = false;
            if (map == null) return;
            double startX = x, startZ = z, limit = Math.Max(0, map.arenaHalfSize - radius);
            x = Clamp(x, -limit, limit); z = Clamp(z, -limit, limit);
            var obstacles = OrderedObstacles(map);
            for (int pass = 0; pass < 16; ++pass)
            {
                bool changed = false;
                foreach (ArenaObstacle o in obstacles)
                {
                    if (!o.blocksMovement) continue;
                    double minX = o.x - o.halfX, maxX = o.x + o.halfX, minZ = o.z - o.halfZ, maxZ = o.z + o.halfZ;
                    double cx = Clamp(x, minX, maxX), cz = Clamp(z, minZ, maxZ), dx = x - cx, dz = z - cz, length = Hypot(dx, dz);
                    if (length >= radius - ContactEpsilon && length > ContactEpsilon) continue;
                    if (length > ContactEpsilon) { x = cx + dx / length * radius; z = cz + dz / length * radius; }
                    else
                    {
                        double best = x - (minX - radius); int dir = 0;
                        double distance = maxX + radius - x;
                        if (distance < best - ContactEpsilon) { best = distance; dir = 1; }
                        distance = z - (minZ - radius);
                        if (distance < best - ContactEpsilon) { best = distance; dir = 2; }
                        distance = maxZ + radius - z;
                        if (distance < best - ContactEpsilon) { dir = 3; }
                        switch (dir) { case 0: x = minX - radius; break; case 1: x = maxX + radius; break; case 2: z = minZ - radius; break; case 3: z = maxZ + radius; break; }
                    }
                    x = Clamp(x, -limit, limit); z = Clamp(z, -limit, limit); changed = true;
                }
                if (!changed || CircleClear(map, x, z, radius)) break;
            }
            repaired = Math.Abs(x - startX) > ContactEpsilon || Math.Abs(z - startZ) > ContactEpsilon;
        }

        public static void SweepMap(ArenaMapDefinition map, double x, double z, double dx, double dz, double radius,
            bool slide, out double nx, out double nz, out bool blocked)
        {
            nx = x; nz = z; blocked = true;
            if (!Finite(x) || !Finite(z) || !Finite(dx) || !Finite(dz) || !Finite(radius) || radius < 0) return;
            if (map == null) { nx = x + dx; nz = z + dz; blocked = false; return; }
            RepairMap(map, ref x, ref z, radius, out blocked);
            if (!CircleClear(map, x, z, radius)) { nx = x; nz = z; blocked = true; return; }
            for (int iteration = 0; iteration < 8; ++iteration)
            {
                if (Math.Abs(dx) + Math.Abs(dz) < ContactEpsilon) break;
                if (!FirstMapHit(map, x, z, dx, dz, radius, false, out double at, out double normalX, out double normalZ))
                { x += dx; z += dz; break; }
                x += dx * at; z += dz * at; blocked = true;
                if (!slide) break;
                dx *= 1 - at; dz *= 1 - at;
                double into = dx * normalX + dz * normalZ;
                if (into < 0) { dx -= into * normalX; dz -= into * normalZ; }
            }
            nx = x; nz = z;
        }

        private struct Vec { public double X, Z; public Vec(double x, double z) { X = x; Z = z; } }
        private struct Pair
        {
            public int A, B; public double NX, NZ;
            public Pair(int a, int b, double nx, double nz) { A = a; B = b; NX = nx; NZ = nz; }
        }
        private struct Wall
        {
            public int Body; public double NX, NZ;
            public Wall(int body, double nx, double nz) { Body = body; NX = nx; NZ = nz; }
        }
        private struct Limit { public int Rank; public double NX, NZ, Low, High, Cap; }

        // Synchronous solver calls have no callbacks or reentrant entry points.
        // Keep one workspace per calling thread; no buffers escape the solver.
        [ThreadStatic] private static Scratch scratch;
        private sealed class Scratch
        {
            public int[] order, parent, rank;
            public Vec[] start, drive, free, basis, want, velocity, correction;
            public double[] groupCap, mapTimes, lambda = Array.Empty<double>(), wallLambda = Array.Empty<double>();
            public Limit[] limits;
            public bool[] active = Array.Empty<bool>(), solved = Array.Empty<bool>();
            public readonly List<Pair> pairs = new List<Pair>();
            public readonly List<Wall> walls = new List<Wall>();
            public Scratch(int count)
            {
                order = new int[count]; parent = new int[count]; rank = new int[count];
                start = new Vec[count]; drive = new Vec[count]; free = new Vec[count]; basis = new Vec[count];
                want = new Vec[count]; velocity = new Vec[count]; correction = new Vec[count];
                groupCap = new double[count]; mapTimes = new double[count]; limits = new Limit[count];
            }
            public void Contacts()
            {
                if (lambda.Length < pairs.Count)
                { lambda = new double[pairs.Count]; active = new bool[pairs.Count]; solved = new bool[pairs.Count]; }
                if (wallLambda.Length < walls.Count) wallLambda = new double[walls.Count];
            }
        }
        private struct SolverSettings
        {
            public double pushSpeed, dashPushSpeed, pushStrength, bodyMass;
            public int iterations;
        }

        private static SolverSettings Settings(CollisionConfig c)
        {
            if (c == null) return new SolverSettings { pushSpeed = 3, dashPushSpeed = 6, pushStrength = 1, bodyMass = 1, iterations = 12 };
            return new SolverSettings {
                pushSpeed = !Finite(c.pushSpeed) || c.pushSpeed <= 0 ? 3 : c.pushSpeed,
                dashPushSpeed = !Finite(c.dashPushSpeed) || c.dashPushSpeed <= 0 ? 6 : c.dashPushSpeed,
                pushStrength = !Finite(c.pushStrength) || c.pushStrength <= 0 ? 1 : c.pushStrength,
                bodyMass = !Finite(c.bodyMass) || c.bodyMass <= 0 ? 1 : c.bodyMass,
                iterations = c.iterations < 1 ? 12 : Math.Min(32, c.iterations)
            };
        }

        public static void ResolveCrowd(ArenaMapDefinition map, CollisionBody[] bodies, double seconds, double radius, CollisionConfig config)
        {
            if (!Finite(seconds) || seconds <= 0 || !Finite(radius) || radius <= 0 || bodies == null || bodies.Length == 0) return;
            SolverSettings cfg = Settings(config);
            if (scratch == null || scratch.order.Length != bodies.Length) scratch = new Scratch(bodies.Length);
            var work = scratch;
            var order = work.order; var start = work.start; var drive = work.drive;
            Array.Clear(start, 0, start.Length); Array.Clear(drive, 0, drive.Length);
            for (int i = 0; i < bodies.Length; ++i)
            {
                order[i] = i; CollisionBody b = bodies[i]; b.Blocked = false; b.Stopped = false;
                if (!Finite(b.X) || !Finite(b.Z) || !Finite(b.DX) || !Finite(b.DZ) || !Finite(b.Speed) || b.Speed < 0)
                { b.Blocked = true; continue; }
                start[i] = new Vec(b.X, b.Z); drive[i] = new Vec(b.DX, b.DZ);
                double length = Hypot(drive[i].X, drive[i].Z);
                if (length > 1) { drive[i].X /= length; drive[i].Z /= length; }
                RepairMap(map, ref b.X, ref b.Z, radius, out _);
            }
            // Eight-player rosters: stable insertion order also avoids a comparer closure.
            for (int i = 1; i < order.Length; i++)
            {
                int value = order[i], j = i - 1;
                while (j >= 0 && string.CompareOrdinal(bodies[order[j]].ID, bodies[value].ID) > 0)
                { order[j + 1] = order[j]; j--; }
                order[j + 1] = value;
            }
            RepairCrowd(map, bodies, order, radius, cfg.iterations, work);
            int steps = Math.Max(1, (int)Math.Ceiling(seconds * 120 - ContactEpsilon));
            for (int step = 0; step < steps; ++step) AdvanceCrowd(map, bodies, order, drive, seconds / steps, radius, cfg, work);
            for (int i = 0; i < bodies.Length; ++i)
            {
                double wantX = start[i].X + drive[i].X * bodies[i].Speed * seconds, wantZ = start[i].Z + drive[i].Z * bodies[i].Speed * seconds;
                if (Hypot(bodies[i].X - wantX, bodies[i].Z - wantZ) > 1e-6) bodies[i].Blocked = true;
            }
        }

        private static void CrowdContacts(ArenaMapDefinition map, CollisionBody[] bodies, int[] order, double radius,
            List<Pair> pairs, List<Wall> walls)
        {
            const double slop = 1e-7;
            pairs.Clear(); walls.Clear();
            for (int ai = 0; ai < order.Length; ++ai)
            {
                int i = order[ai];
                for (int aj = ai + 1; aj < order.Length; ++aj)
                {
                    int j = order[aj];
                    double dx = bodies[j].X - bodies[i].X, dz = bodies[j].Z - bodies[i].Z, length = Hypot(dx, dz);
                    if (length <= 2 * radius + slop && length > ContactEpsilon) pairs.Add(new Pair(i, j, dx / length, dz / length));
                }
                if (map == null) continue;
                double limit = map.arenaHalfSize - radius;
                if (bodies[i].X <= -limit + slop) walls.Add(new Wall(i, 1, 0));
                if (bodies[i].X >= limit - slop) walls.Add(new Wall(i, -1, 0));
                if (bodies[i].Z <= -limit + slop) walls.Add(new Wall(i, 0, 1));
                if (bodies[i].Z >= limit - slop) walls.Add(new Wall(i, 0, -1));
                var obstacles = OrderedObstacles(map);
                foreach (ArenaObstacle o in obstacles)
                {
                    if (!o.blocksMovement) continue;
                    double cx = Clamp(bodies[i].X, o.x - o.halfX, o.x + o.halfX), cz = Clamp(bodies[i].Z, o.z - o.halfZ, o.z + o.halfZ);
                    double dx = bodies[i].X - cx, dz = bodies[i].Z - cz, length = Hypot(dx, dz);
                    if (length <= radius + slop && length > ContactEpsilon) walls.Add(new Wall(i, dx / length, dz / length));
                }
            }
        }

        private static Vec[] ProjectCrowd(Vec[] input, CollisionBody[] bodies, List<Pair> pairs, List<Wall> walls,
            SolverSettings cfg, Limit[] limits, bool[] active, Scratch work)
        {
            var v = work.velocity; var lambda = work.lambda; var wl = work.wallLambda;
            Array.Copy(input, v, input.Length);
            Array.Clear(lambda, 0, pairs.Count); Array.Clear(wl, 0, walls.Count);
            double maxChange = 0;
            void PairStep(int k)
            {
                Pair p = pairs[k];
                double closing = (v[p.A].X - v[p.B].X) * p.NX + (v[p.A].Z - v[p.B].Z) * p.NZ;
                double next = Math.Max(0, lambda[k] + closing * .5), delta = next - lambda[k]; lambda[k] = next;
                maxChange = Math.Max(maxChange, Math.Abs(delta));
                v[p.A].X -= delta * p.NX; v[p.A].Z -= delta * p.NZ;
                v[p.B].X += delta * p.NX; v[p.B].Z += delta * p.NZ;
            }
            void WallStep(int k)
            {
                Wall w = walls[k]; double into = v[w.Body].X * w.NX + v[w.Body].Z * w.NZ;
                double next = Math.Max(0, wl[k] - into), delta = next - wl[k]; wl[k] = next;
                maxChange = Math.Max(maxChange, Math.Abs(delta));
                v[w.Body].X += delta * w.NX; v[w.Body].Z += delta * w.NZ;
            }
            for (int iteration = 0; iteration < cfg.iterations * 32; ++iteration)
            {
                maxChange = 0;
                for (int k = 0; k < pairs.Count; ++k) PairStep(k);
                for (int k = 0; k < walls.Count; ++k) WallStep(k);
                for (int k = pairs.Count - 1; k >= 0; --k) PairStep(k);
                for (int k = walls.Count - 1; k >= 0; --k) WallStep(k);
                if (limits != null)
                    for (int i = 0; i < v.Length; ++i)
                    {
                        Limit l = limits[i];
                        if (l.Rank == 1)
                        {
                            double normal = v[i].X * l.NX + v[i].Z * l.NZ, delta = Clamp(normal, l.Low, l.High) - normal;
                            maxChange = Math.Max(maxChange, Math.Abs(delta));
                            v[i].X += delta * l.NX; v[i].Z += delta * l.NZ;
                        }
                        if (l.Rank == 2)
                        {
                            double speed = Hypot(v[i].X, v[i].Z);
                            if (speed > l.Cap) { maxChange = Math.Max(maxChange, speed - l.Cap); v[i].X *= l.Cap / speed; v[i].Z *= l.Cap / speed; }
                        }
                    }
                if (maxChange <= ContactEpsilon * .01) break;
            }
            for (int k = 0; k < pairs.Count; ++k) active[k] = lambda[k] > ContactEpsilon;
            return v;
        }

        private static Vec[] CrowdVelocity(ArenaMapDefinition map, CollisionBody[] bodies, int[] order, Vec[] drive, double radius, SolverSettings cfg, Scratch work)
        {
            var free = work.free;
            for (int i = 0; i < bodies.Length; ++i) free[i] = new Vec(drive[i].X * bodies[i].Speed, drive[i].Z * bodies[i].Speed);
            var pairs = work.pairs; var walls = work.walls;
            CrowdContacts(map, bodies, order, radius, pairs, walls);
            work.Contacts();
            var active = work.active;
            ProjectCrowd(free, bodies, pairs, walls, cfg, null, active, work);
            Vec[] velocity = free;
            for (int expansion = 0; expansion < 4; ++expansion)
            {
                var parent = work.parent;
                for (int i = 0; i < parent.Length; ++i) parent[i] = i;
                int Find(int i) { while (parent[i] != i) i = parent[i]; return i; }
                for (int k = 0; k < pairs.Count; ++k)
                    if (active[k]) { Pair p = pairs[k]; parent[Find(p.B)] = Find(p.A); }
                var groupCap = work.groupCap;
                Array.Clear(groupCap, 0, groupCap.Length);
                for (int i = 0; i < bodies.Length; ++i)
                {
                    int root = Find(i); double cap = bodies[i].Dashing ? cfg.dashPushSpeed : cfg.pushSpeed;
                    groupCap[root] = Math.Max(groupCap[root], cap);
                }
                var basis = work.basis; var rank = work.rank;
                Array.Clear(basis, 0, basis.Length); Array.Clear(rank, 0, rank.Length);
                for (int k = 0; k < pairs.Count; ++k)
                {
                    if (!active[k]) continue;
                    Pair p = pairs[k];
                    for (int endpoint = 0; endpoint < 2; endpoint++)
                    {
                        int i = endpoint == 0 ? p.A : p.B;
                        if (rank[i] == 0) { basis[i] = new Vec(p.NX, p.NZ); rank[i] = 1; }
                        else if (Math.Abs(basis[i].X * p.NZ - basis[i].Z * p.NX) > 1e-6) rank[i] = 2;
                    }
                }
                var want = work.want; Array.Copy(free, want, free.Length);
                var limits = work.limits;
                for (int i = 0; i < bodies.Length; ++i)
                {
                    double cap = groupCap[Find(i)], motor = 2 * cap * cfg.pushStrength / cfg.bodyMass;
                    limits[i] = new Limit { Rank = rank[i], NX = basis[i].X, NZ = basis[i].Z, Cap = Math.Max(cap, Hypot(free[i].X, free[i].Z)) };
                    if (rank[i] == 2) want[i] = new Vec(drive[i].X * motor, drive[i].Z * motor);
                    else if (rank[i] == 1)
                    {
                        Vec n = basis[i]; double freeNormal = free[i].X * n.X + free[i].Z * n.Z;
                        limits[i].Low = Math.Min(-cap, freeNormal); limits[i].High = Math.Max(cap, freeNormal);
                        double delta = (drive[i].X * motor - free[i].X) * n.X + (drive[i].Z * motor - free[i].Z) * n.Z;
                        want[i].X += delta * n.X; want[i].Z += delta * n.Z;
                    }
                }
                var solved = work.solved;
                velocity = ProjectCrowd(want, bodies, pairs, walls, cfg, limits, solved, work);
                bool expanded = false;
                for (int k = 0; k < pairs.Count; ++k)
                    if (solved[k] && !active[k]) { active[k] = true; expanded = true; }
                if (!expanded) break;
            }
            return velocity;
        }

        private static void AdvanceCrowd(ArenaMapDefinition map, CollisionBody[] bodies, int[] order, Vec[] drive, double seconds, double radius, SolverSettings cfg, Scratch work)
        {
            double remaining = seconds;
            for (int eventIndex = 0; eventIndex < 32 && remaining > ContactEpsilon; ++eventIndex)
            {
                foreach (int i in order)
                {
                    if (!bodies[i].Dashing) continue;
                    if (FirstMapHit(map, bodies[i].X, bodies[i].Z, drive[i].X * bodies[i].Speed * remaining,
                        drive[i].Z * bodies[i].Speed * remaining, radius, false, out double at, out _, out _) && at <= ContactEpsilon)
                    { drive[i] = new Vec(); bodies[i].Blocked = true; bodies[i].Stopped = true; }
                }
                Vec[] v = CrowdVelocity(map, bodies, order, drive, radius, cfg, work);
                foreach (int i in order)
                    if (bodies[i].Dashing && drive[i].X * drive[i].X + drive[i].Z * drive[i].Z > ContactEpsilon &&
                        v[i].X * drive[i].X + v[i].Z * drive[i].Z <= ContactEpsilon)
                    { bodies[i].Stopped = true; bodies[i].Blocked = true; }
                double earliest = 1;
                var mapTimes = work.mapTimes;
                for (int i = 0; i < mapTimes.Length; ++i) mapTimes[i] = 2;
                foreach (int i in order)
                    if (FirstMapHit(map, bodies[i].X, bodies[i].Z, v[i].X * remaining, v[i].Z * remaining, radius, false, out double at, out _, out _))
                    { mapTimes[i] = at; if (at < earliest) earliest = at; }
                for (int ai = 0; ai < order.Length; ++ai)
                {
                    int i = order[ai];
                    for (int aj = ai + 1; aj < order.Length; ++aj)
                    {
                        int j = order[aj];
                        double px = bodies[j].X - bodies[i].X, pz = bodies[j].Z - bodies[i].Z;
                        double dx = (v[j].X - v[i].X) * remaining, dz = (v[j].Z - v[i].Z) * remaining, a = dx * dx + dz * dz;
                        if (a <= ContactEpsilon * ContactEpsilon) continue;
                        double bb = px * dx + pz * dz;
                        if (bb >= -ContactEpsilon) continue;
                        double c = px * px + pz * pz - 4 * radius * radius;
                        if (c <= ContactEpsilon) continue;
                        double disc = bb * bb - a * c;
                        if (disc < 0) continue;
                        double at = (-bb - Math.Sqrt(Math.Max(0, disc))) / a;
                        if (at >= -ContactEpsilon && at < earliest) earliest = Math.Max(0, at);
                    }
                }
                double duration = remaining * earliest;
                foreach (int i in order)
                {
                    CollisionBody b = bodies[i];
                    SweepMap(map, b.X, b.Z, v[i].X * duration, v[i].Z * duration, radius, !b.Dashing, out b.X, out b.Z, out _);
                    if (b.Dashing && mapTimes[i] <= earliest + ContactEpsilon)
                    { drive[i] = new Vec(); b.Blocked = true; b.Stopped = true; }
                }
                remaining -= duration;
                if (earliest >= 1 - ContactEpsilon || duration < ContactEpsilon) break;
            }
            RepairCrowd(map, bodies, order, radius, cfg.iterations, work);
        }

        private static void RepairCrowd(ArenaMapDefinition map, CollisionBody[] bodies, int[] order, double radius, int iterations, Scratch work)
        {
            for (int pass = 0; pass < iterations * 4; ++pass)
            {
                var correction = work.correction; Array.Clear(correction, 0, correction.Length);
                bool overlap = false;
                for (int ai = 0; ai < order.Length; ++ai)
                {
                    int i = order[ai];
                    for (int aj = ai + 1; aj < order.Length; ++aj)
                    {
                        int j = order[aj];
                        double dx = bodies[j].X - bodies[i].X, dz = bodies[j].Z - bodies[i].Z, length = Hypot(dx, dz);
                        if (length >= 2 * radius - ContactEpsilon) continue;
                        overlap = true;
                        if (length <= ContactEpsilon) { dx = 1; dz = 0; length = 1; }
                        double nx = dx / length, nz = dz / length;
                        double amount = (2 * radius - Hypot(bodies[j].X - bodies[i].X, bodies[j].Z - bodies[i].Z)) * .5;
                        SweepMap(map, bodies[i].X, bodies[i].Z, -nx * amount, -nz * amount, radius, false, out double ix, out double iz, out _);
                        SweepMap(map, bodies[j].X, bodies[j].Z, nx * amount, nz * amount, radius, false, out double jx, out double jz, out _);
                        double movedI = (bodies[i].X - ix) * nx + (bodies[i].Z - iz) * nz;
                        double movedJ = (jx - bodies[j].X) * nx + (jz - bodies[j].Z) * nz;
                        if (movedI < amount - ContactEpsilon)
                            SweepMap(map, jx, jz, nx * (amount - movedI), nz * (amount - movedI), radius, false, out jx, out jz, out _);
                        if (movedJ < amount - ContactEpsilon)
                            SweepMap(map, ix, iz, -nx * (amount - movedJ), -nz * (amount - movedJ), radius, false, out ix, out iz, out _);
                        correction[i].X += ix - bodies[i].X; correction[i].Z += iz - bodies[i].Z;
                        correction[j].X += jx - bodies[j].X; correction[j].Z += jz - bodies[j].Z;
                    }
                }
                if (!overlap) break;
                foreach (int i in order)
                {
                    CollisionBody b = bodies[i];
                    SweepMap(map, b.X, b.Z, correction[i].X, correction[i].Z, radius, false, out b.X, out b.Z, out _);
                }
            }
        }

        public static string MapHash(string json)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var result = new StringBuilder(64);
                foreach (byte value in bytes) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        public static ArenaMapDefinition ParseVerifiedMap(string json, string hash, Func<string,ArenaMapDefinition> decode, double radius = .4)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1048576 || string.IsNullOrEmpty(hash) ||
                !string.Equals(MapHash(json), hash, StringComparison.Ordinal))
                throw new InvalidOperationException("Map content hash does not match the loaded source");
            if(decode==null)throw new ArgumentNullException(nameof(decode));
            ArenaMapDefinition map = decode(json);
            ValidateMap(map, radius);
            return map;
        }

        public static void ValidateMap(ArenaMapDefinition map, double radius)
        {
            if (map == null || string.IsNullOrEmpty(map.mapID) || !Identifier.IsMatch(map.mapID) || map.version < 1 || map.version > 1000000)
                throw new InvalidOperationException("Invalid map identity");
            if (!Finite(map.arenaHalfSize) || map.arenaHalfSize < 4 || map.arenaHalfSize > 100 || !Finite(radius) || radius <= 0 || radius > 2 || !Finite(map.movementScale) || map.movementScale!=0&&(map.movementScale<.25||map.movementScale>2))
                throw new InvalidOperationException("Invalid map dimensions");
            if (map.obstacles == null || map.obstacles.Length > 128 || map.controlPoints == null || map.controlPoints.Length != 3 ||
                map.spawnLayouts == null || map.spawnLayouts.Length != 3)
                throw new InvalidOperationException("Invalid map collection sizes");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArenaObstacle o in map.obstacles)
            {
                if (o == null || string.IsNullOrEmpty(o.id) || !Identifier.IsMatch(o.id) || !ids.Add(o.id))
                    throw new InvalidOperationException("Invalid or duplicate obstacle identity");
                if (!Finite(o.x) || !Finite(o.z) || !Finite(o.halfX) || !Finite(o.halfZ) || !Finite(o.height) ||
                    o.halfX <= 0 || o.halfZ <= 0 || o.height <= 0 || o.height > 20 ||
                    Math.Abs(o.x) + o.halfX > map.arenaHalfSize || Math.Abs(o.z) + o.halfZ > map.arenaHalfSize || o.visual != "wall")
                    throw new InvalidOperationException("Invalid obstacle geometry: " + o.id);
            }
            var targets = new List<ArenaPoint>(map.controlPoints);
            var teams = new HashSet<int>();
            foreach (ArenaSpawnLayout layout in map.spawnLayouts)
            {
                if (layout == null || layout.teamCount < 2 || layout.teamCount > 4 || !teams.Add(layout.teamCount) ||
                    layout.anchors == null || layout.anchors.Length != layout.teamCount)
                    throw new InvalidOperationException("Invalid spawn layout");
                foreach (ArenaPoint p in layout.anchors)
                {
                    if (p == null) throw new InvalidOperationException("Invalid spawn anchor");
                    foreach (double offset in new[] { 0.0, 1, -1, 2 }) targets.Add(new ArenaPoint { x = p.x, z = p.z + offset });
                }
            }
            foreach (ArenaPoint p in targets)
                if (p == null || !Finite(p.x) || !Finite(p.z) || !CircleClear(map, p.x, p.z, radius))
                    throw new InvalidOperationException("Blocked control point or spawn");

            const double spacing = .5;
            double lo = -map.arenaHalfSize + radius;
            int side = (int)Math.Floor((map.arenaHalfSize - radius - lo) / spacing) + 1;
            var visited = new bool[side * side];
            var queue = new List<int>(side * side);
            int start = -1;
            for (int i = 0; i < visited.Length; ++i)
            {
                if (CircleClear(map, lo + (i % side) * spacing, lo + (i / side) * spacing, radius)) { start = i; break; }
            }
            if (start < 0) throw new InvalidOperationException("Map has no walkable space");
            visited[start] = true; queue.Add(start);
            int[] stepX = { -1, 1, 0, 0 }, stepZ = { 0, 0, -1, 1 };
            for (int head = 0; head < queue.Count; ++head)
            {
                int i = queue[head], col = i % side, row = i / side;
                double x = lo + col * spacing, z = lo + row * spacing;
                for (int d = 0; d < 4; ++d)
                {
                    int nc = col + stepX[d], nr = row + stepZ[d];
                    if (nc < 0 || nr < 0 || nc >= side || nr >= side) continue;
                    int ni = nr * side + nc;
                    if (visited[ni]) continue;
                    double nx = lo + nc * spacing, nz = lo + nr * spacing;
                    if (!CircleClear(map, nx, nz, radius)) continue;
                    if (FirstMapHit(map, x, z, nx - x, nz - z, radius, false, out double t, out _, out _) && t < 1 - ContactEpsilon) continue;
                    visited[ni] = true; queue.Add(ni);
                }
            }
            foreach (ArenaPoint p in targets)
            {
                bool connected = false;
                foreach (int i in queue)
                {
                    double x = lo + (i % side) * spacing, z = lo + (i / side) * spacing;
                    if (Math.Abs(x - p.x) > spacing * 1.5 || Math.Abs(z - p.z) > spacing * 1.5) continue;
                    if (!FirstMapHit(map, p.x, p.z, x - p.x, z - p.z, radius, false, out double t, out _, out _) || t >= 1 - ContactEpsilon)
                    { connected = true; break; }
                }
                if (!connected) throw new InvalidOperationException("Unreachable control point or spawn");
            }
        }
    }
}
