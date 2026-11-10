using Rumblebound.Gameplay;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rumblebound.Presentation
{
    public static class GroundTargeting
    {
        public static bool Valid(ArenaMapDefinition map, Rules rules, HeroDefinition hero, Vector3 caster, Vector3 target)
        {
            if (hero == null || !HeroRules.IsGround(hero) || rules == null || float.IsNaN(target.x) || float.IsNaN(target.z) ||
                float.IsInfinity(target.x) || float.IsInfinity(target.z)) return false;
            double dx = (double)target.x - caster.x, dz = (double)target.z - caster.z;
            return Math.Abs(target.x) <= rules.arenaHalfSize && Math.Abs(target.z) <= rules.arenaHalfSize &&
                dx * dx + dz * dz <= hero.castRange * hero.castRange && ArenaCollision.CircleClear(map, target.x, target.z, 0);
        }
    }

    // Bounded, presentation-only instances. Active casts restore on reconnect;
    // repeated/old impact events cannot replay explosions or cause damage.
    public sealed class GroundCastPresentation : IDisposable
    {
        private sealed class CastVisual
        {
            public GroundCastState state;
            public LineRenderer circle;
            public GameObject ball;
            public bool falling;
        }
        private sealed class ImpactVisual { public GameObject root; public double until; }
        private readonly Dictionary<string, CastVisual> active = new Dictionary<string, CastVisual>();
        private readonly Stack<CastVisual> free = new Stack<CastVisual>();
        private readonly List<ImpactVisual> impacts = new List<ImpactVisual>();
        private readonly Stack<GameObject> freeImpacts = new Stack<GameObject>();
        private readonly Dictionary<string, long> seen = new Dictionary<string, long>();
        private readonly List<string> removed = new List<string>();
        private GameObject ballPrefab, impactPrefab;
        private int capacity = 8, rate = 30;
        private long tick, silentThrough;
        private double receivedAt;
        private string match;
        private bool initialized;
        public event Action<GroundImpactState> ImpactShown;
        public int ActiveCount => active.Count;
        public int ImpactCount => impacts.Count;

        public static LineRenderer Circle(string name, Color color, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            var source = Resources.Load<Material>("Mage/TargetCircle");
            line.sharedMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            line.sharedMaterial.color = color;
            line.loop = true; line.useWorldSpace = true; line.widthMultiplier = width; line.positionCount = 80;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        public static void PlaceCircle(LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, .055f, Mathf.Sin(angle) * radius));
            }
        }
        public void Accept(Snapshot snapshot, int tickRate, int maxCasts, string ownTeam,double presentationTime=double.NaN)
        {
            double now = double.IsNaN(presentationTime)?Time.realtimeSinceStartupAsDouble:presentationTime;
            bool fresh = initialized && match == snapshot.matchID && snapshot.type != "welcome" && snapshot.type != "start" &&
                snapshot.tick > tick && snapshot.tick - tick <= Math.Max(1, tickRate / 2) && now - receivedAt <= .5;
            if (!fresh) { Clear(); silentThrough = snapshot.tick; }
            match = snapshot.matchID; tick = snapshot.tick; rate = Math.Max(1, tickRate); receivedAt = now; initialized = true;
            capacity = Math.Max(1, Math.Min(8, maxCasts));
            if (snapshot.match?.phase == "ended") { Clear(); return; }
            ballPrefab = ballPrefab != null ? ballPrefab : Resources.Load<GameObject>("Mage/FireballDrop");
            impactPrefab = impactPrefab != null ? impactPrefab : Resources.Load<GameObject>("Mage/GroundImpact");
            var present = new HashSet<string>();
            foreach (var state in snapshot.groundCasts ?? Array.Empty<GroundCastState>())
            {
                if (state == null || string.IsNullOrEmpty(state.id) || !(state.radius > 0) || state.impactTick <= snapshot.tick ||
                    state.fallStartTick < state.startTick || state.impactTick <= state.fallStartTick) continue;
                present.Add(state.id);
                if (!active.TryGetValue(state.id, out var visual))
                {
                    if (active.Count >= capacity || ballPrefab == null) continue;
                    visual = free.Count > 0 ? free.Pop() : new CastVisual {
                        circle = Circle("Fireball warning", Color.yellow, .035f), ball = UnityEngine.Object.Instantiate(ballPrefab)
                    };
                    visual.falling = false; visual.ball.SetActive(false); visual.circle.gameObject.SetActive(true);
                    visual.circle.sharedMaterial.color = state.teamID == ownTeam ? new Color(.35f,.78f,1f) : new Color(1f,.35f,.12f);
                    active.Add(state.id, visual);
                }
                visual.state = state;
                PlaceCircle(visual.circle, new Vector3((float)state.x, 0, (float)state.z), (float)state.radius);
            }
            removed.Clear(); foreach (var pair in active) if (!present.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (string id in removed) Release(id);
            foreach (var impact in snapshot.groundImpacts ?? Array.Empty<GroundImpactState>())
            {
                if (impact == null || string.IsNullOrEmpty(impact.id) || impact.tick <= silentThrough ||
                    impact.tick > tick || tick - impact.tick > 6 || seen.ContainsKey(impact.id)) continue;
                seen[impact.id] = impact.tick;
                if (impactPrefab == null || !(impact.radius > 0)) continue;
                if (impacts.Count >= 8) ReleaseImpact(0);
                var root = freeImpacts.Count > 0 ? freeImpacts.Pop() : UnityEngine.Object.Instantiate(impactPrefab);
                root.transform.position = new Vector3((float)impact.x, .23f, (float)impact.z);
                root.transform.localScale = impactPrefab.transform.localScale * ((float)impact.radius / 1.25f);
                root.SetActive(true); ResetParticles(root, (float)(tick - impact.tick) / rate, false);
                impacts.Add(new ImpactVisual { root = root, until = now + 2.2 - (double)(tick-impact.tick)/rate });
                ImpactShown?.Invoke(impact);
            }
            removed.Clear(); foreach (var pair in seen) if (tick - pair.Value > rate) removed.Add(pair.Key);
            foreach (string id in removed) seen.Remove(id);
        }
        public double RenderTick {get;private set;}
        public void Render(double presentationTime=double.NaN)
        {
            double now = double.IsNaN(presentationTime)?Time.realtimeSinceStartupAsDouble:presentationTime;
            double renderTick = tick + Math.Min(.3, Math.Max(0, now-receivedAt)) * rate;
            RenderTick=renderTick;
            foreach (var visual in active.Values)
            {
                var state = visual.state;
                if (renderTick < state.fallStartTick) continue;
                float progress = Mathf.Clamp01((float)((renderTick-state.fallStartTick)/(state.impactTick-state.fallStartTick)));
                visual.ball.transform.position = new Vector3((float)state.x, Mathf.Lerp(4.2f,.18f,progress*progress), (float)state.z);
                if (!visual.falling)
                {
                    visual.falling = true; visual.ball.SetActive(true); ResetParticles(visual.ball, .15f, true);
                }
                if (renderTick > state.impactTick + rate * .25) visual.ball.SetActive(false);
            }
            for (int i = impacts.Count-1; i >= 0; i--) if (now >= impacts[i].until) ReleaseImpact(i);
        }
        private readonly Dictionary<GameObject,ParticleSystem[]> particleCache=new Dictionary<GameObject,ParticleSystem[]>();
        private readonly Dictionary<GameObject,float[]> particleSpeeds=new Dictionary<GameObject,float[]>();
        private float playbackSpeed=1;
        private ParticleSystem[] Particles(GameObject root)
        {
            if(particleCache.TryGetValue(root,out var systems))return systems;
            systems=root.GetComponentsInChildren<ParticleSystem>(true);var speeds=new float[systems.Length];
            for(int i=0;i<systems.Length;i++){var main=systems[i].main;speeds[i]=main.simulationSpeed;main.simulationSpeed=speeds[i]*playbackSpeed;}
            particleCache.Add(root,systems);particleSpeeds.Add(root,speeds);return systems;
        }
        public void SetPlaybackSpeed(float speed)
        {
            if(playbackSpeed==speed)return;playbackSpeed=speed;
            foreach(var pair in particleCache)for(int i=0;i<pair.Value.Length;i++){var main=pair.Value[i].main;main.simulationSpeed=particleSpeeds[pair.Key][i]*speed;}
        }
        private void ResetParticles(GameObject root, float age, bool seedCore)
        {
            foreach (var ps in Particles(root))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false);
                if (seedCore && ps.main.simulationSpace == ParticleSystemSimulationSpace.Local) ps.Emit(1);
                if (age > 0) ps.Simulate(age, false, false, true);
                ps.Play(false);
            }
        }
        private void Stop(GameObject root)
        {
            if (root == null) return;
            foreach (var ps in Particles(root)) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.SetActive(false);
        }
        private void Release(string id)
        {
            var visual = active[id]; Stop(visual.ball); if (visual.circle != null) visual.circle.gameObject.SetActive(false); free.Push(visual); active.Remove(id);
        }
        private void ReleaseImpact(int index)
        { var root = impacts[index].root; Stop(root); freeImpacts.Push(root); impacts.RemoveAt(index); }
        public void Clear()
        {
            removed.Clear(); removed.AddRange(active.Keys); foreach (string id in removed) Release(id);
            while (impacts.Count > 0) ReleaseImpact(impacts.Count-1);
            seen.Clear(); initialized = false;
        }
        public void Dispose()
        {
            Clear();
            while (free.Count > 0) { var visual = free.Pop(); UnityEngine.Object.Destroy(visual.ball); if (visual.circle != null) { UnityEngine.Object.Destroy(visual.circle.sharedMaterial); UnityEngine.Object.Destroy(visual.circle.gameObject); } }
            while (freeImpacts.Count > 0) UnityEngine.Object.Destroy(freeImpacts.Pop());
        }
    }
}
