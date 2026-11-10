using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Rumblebound.Gameplay
{
    // Held input prediction follows the G0 timeline. Mapped matches add an
    // independent push overlay; remote interpolation never changes with range.
    public class GameMovement
    {
        public const double DefaultRemoteContactSmoothingSeconds = .08;
        public struct Drive { public double x, z; }
        public sealed class MotionState { public double X, Z; public CollisionBody[] Bodies = Array.Empty<CollisionBody>(); }
        private sealed class MotionModel : IPredictionModel<MotionState, Drive>
        {
            private readonly GameMovement owner;
            public MotionModel(GameMovement owner) { this.owner = owner; }
            public MotionState CreateState() => new MotionState();
            public void Copy(MotionState source, MotionState target)
            {
                target.X = source.X; target.Z = source.Z;
                EnsureBodies(ref target.Bodies, source.Bodies.Length);
                for (int i = 0; i < source.Bodies.Length; i++)
                {
                    var a = source.Bodies[i]; var b = target.Bodies[i];
                    b.ID = a.ID; b.X = a.X; b.Z = a.Z; b.DX = a.DX; b.DZ = a.DZ;
                    b.Speed = a.Speed; b.Dashing = a.Dashing; b.Blocked = a.Blocked; b.Stopped = a.Stopped;
                }
            }
            public void Step(MotionState state, Drive input, double seconds, double tick)
            { owner.PredictState(state, input.x, input.z, seconds, tick); }
        }
        private sealed class RemotePresentation
        {
            public string entityID;
            public long life;
            public double pushX, pushZ, errorX, errorZ, rawX, rawZ;
        }
        private readonly Rules rules;
        private readonly ArenaMapDefinition map;
        private CollisionBody[] proxies = Array.Empty<CollisionBody>();
        private string localID;
        private PlayerState localState;
        private long authorityTick;
        private readonly Dictionary<string, RemotePresentation> remotePresentation = new Dictionary<string, RemotePresentation>(StringComparer.Ordinal);
        private CollisionBody[] presentation = Array.Empty<CollisionBody>();
        private CollisionBody[] presentationNext = Array.Empty<CollisionBody>();
        private CollisionBody[] presentationFallback = Array.Empty<CollisionBody>();
        private readonly HashSet<string> aliveRemotes = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> removedRemotes = new List<string>();
        private readonly MotionState authorityState = new MotionState();
        private readonly bool wholeTickSimulation;
        public IPrediction<MotionState, Drive> Prediction { get; }
        public ICorrectionPolicy LocalCorrection { get; }
        // Independent of local prediction correction: this only settles remote
        // contact-proxy offsets when a new authority snapshot replaces them.
        public double RemoteContactSmoothingSeconds { get; }
        private readonly SnapshotBuffer<Snapshot> snapshots = new SnapshotBuffer<Snapshot>(32);
        private static readonly SnapshotSelector<Snapshot, PlayerState, PlayerState> selectPlayer = SelectPlayer;
        private static readonly SnapshotSelector<Snapshot, string, ProjectileState> selectProjectile = SelectProjectile;
        private bool hasLocal;
        private double x, z, offsetX, offsetZ, lastFrame = double.NaN;
        private double lastSnapshot = double.NegativeInfinity;
        private double renderTick => Interpolation.RenderTick;
        public IPresentationTimeline Interpolation { get; }
        public double Correction { get; private set; }
        public double RenderTick => renderTick;

        public GameMovement(Rules rules,ArenaMapDefinition map,IPresentationTimeline timeline,
            Func<IPredictionModel<MotionState,Drive>,IPrediction<MotionState,Drive>> predictionFactory,
            ICorrectionPolicy correctionPolicy,bool fixedTicks=false,
            double remoteContactSmoothingSeconds=DefaultRemoteContactSmoothingSeconds)
        {
            if(double.IsNaN(remoteContactSmoothingSeconds)||double.IsInfinity(remoteContactSmoothingSeconds)||remoteContactSmoothingSeconds<0||remoteContactSmoothingSeconds>1)
                throw new ArgumentOutOfRangeException(nameof(remoteContactSmoothingSeconds),"Remote contact smoothing must be finite and within 0..1 seconds");
            this.rules=rules??throw new ArgumentNullException(nameof(rules));this.map=map;
            RemoteContactSmoothingSeconds=remoteContactSmoothingSeconds;
            Interpolation=timeline??new LatestSnapshotTimeline();wholeTickSimulation=fixedTicks;
            Prediction=predictionFactory==null?new AuthorityPrediction<MotionState,Drive>(new MotionModel(this)):predictionFactory(new MotionModel(this));
            LocalCorrection=correctionPolicy??new SnapCorrection();
            if(!Prediction.Enabled&&LocalCorrection.Smooths)throw new ArgumentException("Smooth correction requires local prediction");
        }
        private static HeroDefinition FindHero(Rules rules,string id){if(rules.heroes!=null)foreach(var h in rules.heroes)if(h!=null&&h.heroID==id)return h;return null;}
        private void ApplyPredictionState()
        { var state = Prediction.State; x = state.X; z = state.Z; proxies = state.Bodies; }

        private static void EnsureBodies(ref CollisionBody[] bodies, int count)
        {
            if (bodies.Length == count) return;
            bodies = new CollisionBody[count];
            for (int i = 0; i < count; i++) bodies[i] = new CollisionBody();
        }

        private static void ResetBody(CollisionBody body, string id, double x, double z, double speed = 0)
        {
            body.ID = id; body.X = x; body.Z = z; body.Speed = speed;
            body.DX = body.DZ = 0;
            body.Dashing = body.Blocked = body.Stopped = false;
        }

        private static CollisionBody FindBody(CollisionBody[] bodies, string id)
        {
            foreach (var body in bodies) if (body.ID == id) return body;
            return null;
        }

        private static PlayerState FindPlayer(PlayerState[] players, string id)
        {
            foreach (var player in players)
                if (player != null && player.playerID == id) return player;
            return null;
        }

        private static PlayerState FindPlayer(PlayerState[] players, string id, string entityID)
        {
            foreach (var player in players)
                if (player != null && player.playerID == id && player.entityID == entityID) return player;
            return null;
        }

        private static ProjectileState FindProjectile(ProjectileState[] projectiles, string id)
        {
            foreach (var projectile in projectiles) if (projectile != null && projectile.id == id) return projectile;
            return null;
        }

        private static bool SelectPlayer(Snapshot snapshot, PlayerState player, out PlayerState state)
        {
            state = FindPlayer(snapshot.players, player.playerID, player.entityID);
            return state != null;
        }

        private static bool SelectProjectile(Snapshot snapshot, string id, out ProjectileState state)
        {
            state = snapshot.projectiles == null ? null : FindProjectile(snapshot.projectiles, id);
            return state != null;
        }

        private void ResetProxies(Snapshot snapshot)
        {
            int count = 0;
            foreach (var player in snapshot.players)
                if (player != null && player.hp > 0 && !player.left) count++;
            EnsureBodies(ref proxies, count);
            int index = 0;
            foreach (var player in snapshot.players)
                if (player != null && player.hp > 0 && !player.left)
                    ResetBody(proxies[index++], player.playerID, player.x, player.z,
                        FindHero(rules, player.heroID)?.speed ?? rules.speed);
        }

        private void PredictState(MotionState state, double dx, double dz, double seconds, double tick)
        {
            double stopUntil = localState?.attackStopUntilTick ?? 0;
            if (map == null)
            {
                double stoppedSeconds = Math.Max(0, (stopUntil - tick) / rules.tickRate);
                Movement.Advance(ref state.X, ref state.Z, dx, dz, Math.Max(0, seconds - stoppedSeconds), rules);
                return;
            }
            var self = FindBody(state.Bodies, localID);
            if (self == null) return;
            self.X = state.X; self.Z = state.Z;
            // Remote intent is unknown: these independent stationary proxies
            // never change an authoritative snapshot or invent remote input.
            double left = seconds;
            while (left > 1e-10)
            {
                double dt = wholeTickSimulation ? left : Math.Min(left, 1.0 / 120);
                bool stopped = tick < stopUntil - 1e-9;
                // Split the frame at recovery, so a long render frame neither
                // extends the stop nor moves early. Zero drive keeps the body
                // solid and pushable by other players.
                if (stopped) dt = Math.Min(dt, (stopUntil - tick) / rules.tickRate);
                self.Dashing = !stopped && localState != null && localState.dashEndTick > tick;
                self.DX = stopped ? 0 : self.Dashing ? localState.dashX : dx;
                self.DZ = stopped ? 0 : self.Dashing ? localState.dashZ : dz;
                self.Speed = self.Dashing ? rules.dashSpeed : rules.speed;
                ArenaCollision.ResolveCrowd(map, state.Bodies, dt, rules.playerRadius, rules.collision);
                left -= dt; tick += dt * rules.tickRate;
            }
            state.X = self.X; state.Z = self.Z;
        }

        private void SafePresentation(double fromX, double fromZ, ref double toX, ref double toZ)
        {
            if (map == null) { Movement.Advance(ref toX, ref toZ, 0, 0, 0, rules); return; }
            ArenaCollision.SweepMap(map, fromX, fromZ, toX - fromX, toZ - fromZ, rules.playerRadius, false,
                out double safeX, out double safeZ, out _);
            toX = safeX; toZ = safeZ;
        }

        private bool PresentationClear(CollisionBody[] bodies)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                if (!ArenaCollision.CircleClear(map, bodies[i].X, bodies[i].Z, rules.playerRadius)) return false;
                for (int j = i + 1; j < bodies.Length; j++)
                {
                    double dx = bodies[i].X - bodies[j].X, dz = bodies[i].Z - bodies[j].Z;
                    if (Math.Sqrt(dx * dx + dz * dz) < 2 * rules.playerRadius - 1e-6) return false;
                }
            }
            return true;
        }

        private void UpdatePresentation(bool rebase = false, double decay = 1)
        {
            if (map == null || snapshots.Count == 0) return;
            var latest = snapshots[snapshots.Count - 1].Value.players;
            EnsureBodies(ref presentationNext, proxies.Length);
            var next = presentationNext;
            aliveRemotes.Clear();
            int bodyIndex = 0;
            foreach (var proxy in proxies)
            {
                // Proxies were populated from this same authoritative roster.
                var player = FindPlayer(latest, proxy.ID);
                double px, pz;
                if (proxy.ID == localID)
                {
                    px = x + offsetX; pz = z + offsetZ;
                    SafePresentation(x, z, ref px, ref pz);
                }
                else
                {
                    aliveRemotes.Add(proxy.ID);
                    if (!remotePresentation.TryGetValue(proxy.ID, out var remote) || remote.entityID != player.entityID || remote.life != player.life)
                    {
                        remote = new RemotePresentation { entityID = player.entityID, life = player.life };
                        remotePresentation[proxy.ID] = remote;
                    }
                    // Every remote keeps the same interpolation clock. Add
                    // only displacement caused by our independent push proxy;
                    // proximity never switches to raw 15 Hz authority poses.
                    double pushX = proxy.X - player.x, pushZ = proxy.Z - player.z;
                    double addedPushX = pushX - remote.pushX, addedPushZ = pushZ - remote.pushZ;
                    if (rebase)
                    {
                        remote.errorX += remote.pushX - pushX;
                        remote.errorZ += remote.pushZ - pushZ;
                    }
                    InterpolatedPosition(player, out double baseX, out double baseZ);
                    double removeX = remote.errorX * (1 - decay), removeZ = remote.errorZ * (1 - decay);
                    double addedPush = Math.Sqrt(addedPushX * addedPushX + addedPushZ * addedPushZ);
                    if (!rebase && addedPush > 1e-9)
                    {
                        // Arrival jitter can pause the interpolation clock.
                        // While input is actively pushing, correction decay
                        // must not pull the remote backwards during that pause.
                        double nx = addedPushX / addedPush, nz = addedPushZ / addedPush;
                        double progress = (baseX + pushX - remote.rawX) * nx + (baseZ + pushZ - remote.rawZ) * nz;
                        double excess = removeX * nx + removeZ * nz - Math.Max(0, progress) * .9;
                        if (excess > 0) { removeX -= excess * nx; removeZ -= excess * nz; }
                    }
                    remote.errorX -= removeX; remote.errorZ -= removeZ;
                    remote.pushX = pushX; remote.pushZ = pushZ;
                    remote.rawX = baseX + pushX; remote.rawZ = baseZ + pushZ;
                    px = baseX + pushX + remote.errorX;
                    pz = baseZ + pushZ + remote.errorZ;
                    SafePresentation(baseX, baseZ, ref px, ref pz);
                }
                ResetBody(next[bodyIndex++], proxy.ID, px, pz);
            }
            removedRemotes.Clear();
            foreach (var pair in remotePresentation) if (!aliveRemotes.Contains(pair.Key)) removedRemotes.Add(pair.Key);
            foreach (string id in removedRemotes) remotePresentation.Remove(id);

            if (!PresentationClear(next))
            {
                // Position-only repair in rendered space: all drives/speeds
                // are zero, and this sub-event interval advances no simulation.
                // Never copy the repaired poses back into prediction or inputs.
                ArenaCollision.ResolveCrowd(map, next, ArenaCollision.ContactEpsilon * .01,
                    rules.playerRadius, rules.collision);
                if (!PresentationClear(next))
                {
                    // A crowded corner may exhaust bounded repair. Retain a
                    // legal previous layout, or use the legal authority layout
                    // after a roster change, instead of showing penetration.
                    EnsureBodies(ref presentationFallback, next.Length);
                    var previous = presentationFallback;
                    for (int i = 0; i < next.Length; i++)
                    {
                        var old = FindBody(presentation, next[i].ID);
                        var authority = FindPlayer(latest, next[i].ID);
                        ResetBody(previous[i], next[i].ID, old?.X ?? authority.x, old?.Z ?? authority.z);
                    }
                    if (PresentationClear(previous))
                        for (int i = 0; i < next.Length; i++)
                        { next[i].X = previous[i].X; next[i].Z = previous[i].Z; }
                    else for (int i = 0; i < next.Length; i++)
                    {
                        var authority = FindPlayer(latest, next[i].ID);
                        next[i].X = authority.x; next[i].Z = authority.z;
                    }
                }
            }
            // Preserve the last rendered layout throughout repair, then reuse
            // it as next frame's work buffer. Snapshot DTOs are never mutated.
            presentationNext = presentation;
            presentation = next;
        }

        private void UpdateRenderTick(double now)
        {
            Interpolation.Advance(now);
        }
        public void SentInput(long seq, double time)
        {
            Prediction.SentInput(seq, time);
        }
        public void Accept(Snapshot snapshot, string playerID, double now)
        {
            if (snapshot.type == "welcome")
            {
                remotePresentation.Clear(); presentation = Array.Empty<CollisionBody>();
                snapshots.Clear();
                lastFrame = double.NaN; hasLocal = false;
            }
            if (snapshot.type == "welcome" || snapshot.type == "start") Interpolation.Reset(snapshot.tick, now);
            else Interpolation.Observe(snapshot.tick, now);
            localID = playerID; authorityTick = snapshot.tick;
            if (map != null) ResetProxies(snapshot);
            foreach (var self in snapshot.players)
            {
                if (self == null || self.playerID != playerID) continue;
                bool newLife = self.hp > 0 && !self.left && localState != null &&
                    (localState.hp <= 0 || localState.left || localState.entityID != self.entityID || localState.life!=self.life);
                localState = self;
                authorityState.X = self.x; authorityState.Z = self.z; authorityState.Bodies = proxies;
                Prediction.Reconcile(authorityState, new PredictionAck {
                    Sequence = self.ack, ServerTick = snapshot.tick,
                    AppliedInputTicks = Math.Max(0, Math.Min(snapshot.tick - snapshot.inputTick, rules.inputTimeoutTicks - 1)),
                    ResetHistory = snapshot.type == "welcome" || newLife
                }, now);
                double targetX = Prediction.State.X, targetZ = Prediction.State.Z;
                var previous = map == null ? null : FindBody(presentation, playerID);
                double visibleX = previous?.X ?? x + offsetX, visibleZ = previous?.Z ?? z + offsetZ;
                SafePresentation(targetX, targetZ, ref visibleX, ref visibleZ);
                double errorX = hasLocal ? visibleX - targetX : 0, errorZ = hasLocal ? visibleZ - targetZ : 0;
                Correction = Math.Sqrt(errorX * errorX + errorZ * errorZ);
                // Attack recovery is a real movement stop. Blending a previous
                // walking error during it would make the attacker visibly slide.
                bool smooth = Prediction.Enabled && LocalCorrection.PreserveOffset(Correction,
                    snapshot.type == "welcome" || newLife || self.attackStopUntilTick > snapshot.tick);
                offsetX = smooth ? errorX : 0; offsetZ = smooth ? errorZ : 0;
                ApplyPredictionState(); hasLocal = true;
                break;
            }
            lastSnapshot = now;
            snapshots.Add(snapshot.tick, snapshot);
            UpdateRenderTick(now);
            UpdatePresentation(true, RemoteContactSmoothingSeconds == 0 ? 0 : 1);
        }
        public void Advance(double now, double dx, double dz)
        {
            double dt = double.IsNaN(lastFrame) ? 0 : Math.Max(0, Math.Min(.05, now - lastFrame));
            lastFrame = now;
            if (hasLocal)
            {
                Prediction.Advance(now, new Drive { x = dx, z = dz });
                ApplyPredictionState();
                double decay = LocalCorrection.Decay(dt);
                offsetX *= decay; offsetZ *= decay;
            }
            UpdateRenderTick(now);
            if (hasLocal) UpdatePresentation(false, RemoteContactSmoothingSeconds == 0 ? 0 : Math.Exp(-dt / RemoteContactSmoothingSeconds));
        }
        public void BufferPlaybackNext(Snapshot next,double now)
        {
            if(next==null||next.players==null)return;
            snapshots.Add(next.tick, next);
        }
        public void Position(PlayerState player, bool local, out double px, out double pz)
        {
            if (map != null)
            {
                var body = FindBody(presentation, player.playerID);
                if (body != null) { px = body.X; pz = body.Z; return; }
            }
            if (local && hasLocal)
            {
                px = x + offsetX; pz = z + offsetZ;
                SafePresentation(x, z, ref px, ref pz);
                return;
            }
            InterpolatedPosition(player, out px, out pz);
        }

        private void InterpolatedPosition(PlayerState player, out double px, out double pz)
        {
            if (!Interpolation.Interpolates) { px = player.x; pz = player.z; return; }
            int first = 0;
            if (map != null && player.hp > 0 && !player.left)
                for (int i = snapshots.Count - 2; i >= 0; i--)
                {
                    var old = FindPlayer(snapshots[i].Value.players, player.playerID, player.entityID);
                    if (old == null || old.hp <= 0 || old.left || old.life != player.life) { first = i + 1; break; }
                }
            bool found = snapshots.TryBracket(renderTick, player, selectPlayer, out var bracket, first);
            var before = found ? bracket.Before : player;
            px = before.x; pz = before.z;
            if (!found || !bracket.HasAfter || bracket.BeforeTick == bracket.AfterTick) return;
            px += (bracket.After.x - before.x) * bracket.Fraction; pz += (bracket.After.z - before.z) * bracket.Fraction;
            SafePresentation(before.x, before.z, ref px, ref pz);
        }
        public void ProjectilePosition(ProjectileState projectile, out double px, out double pz, ProjectileEnd ending = null)
        {
            if (ending != null && renderTick >= ending.tick)
            { px = ending.x; pz = ending.z; return; }
            bool found = snapshots.TryBracket(renderTick, projectile.id, selectProjectile, out var bracket);
            var before = found ? bracket.Before : projectile;
            px = before.x; pz = before.z;
            // A terminal event is the final authoritative sample, including
            // contacts between normal snapshots. Never extrapolate past it.
            if (ending != null && (!found || !bracket.HasAfter || ending.tick < bracket.AfterTick))
            {
                if (!found || ending.tick <= bracket.BeforeTick) return;
                double progress = Math.Max(0, Math.Min(1, (renderTick - bracket.BeforeTick) / (ending.tick - bracket.BeforeTick)));
                px += (ending.x - px) * progress; pz += (ending.z - pz) * progress;
                return;
            }
            if (!found || !bracket.HasAfter || bracket.BeforeTick == bracket.AfterTick) return;
            px += (bracket.After.x - before.x) * bracket.Fraction; pz += (bracket.After.z - before.z) * bracket.Fraction;
        }
    }
}
