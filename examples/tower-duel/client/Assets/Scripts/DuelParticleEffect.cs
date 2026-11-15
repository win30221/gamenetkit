using System;
using UnityEngine;

namespace TowerDuel
{
    // Uses the same paused-particle technique as Rumblebound's ParticleFeedback.
    // Only the shared SDK render clock advances particles, including during recovery.
    public sealed class DuelParticleEffect : IDisposable
    {
        private readonly ParticleSystem[] systems;
        private readonly Renderer[] renderers;
        private readonly MeshRenderer slash;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private float age;
        private bool started;
        public Transform Root { get; }
        public float Lifetime { get; }
        public int ParticleCount { get { int count = 0; foreach (var system in systems) count += system.particleCount; return count; } }
        public bool Active => Root.gameObject.activeSelf;

        public DuelParticleEffect(GameObject prefab, Transform parent, uint seed)
        {
            if (!prefab) throw new InvalidOperationException("Prepare reused Tower Duel VFX before playing.");
            Root = UnityEngine.Object.Instantiate(prefab, parent, false).transform;
            Root.gameObject.SetActive(false);
            systems = Root.GetComponentsInChildren<ParticleSystem>(true);
            renderers = Root.GetComponentsInChildren<Renderer>(true);
            slash = systems.Length == 0 ? Root.GetComponent<MeshRenderer>() : null;
            float duration = slash ? .20f : 0;
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = unchecked(seed * 747796405u + (uint)i * 2891336453u + 1u);
                duration = Mathf.Max(duration, (Maximum(main.startDelay) + main.duration + Maximum(main.startLifetime)) / Mathf.Max(.01f, main.simulationSpeed));
            }
            Lifetime = Mathf.Clamp(duration, .20f, 1.8f);
            Clear();
        }

        public void Place(Vector3 position, Quaternion rotation, float scale)
        {
            Root.SetPositionAndRotation(position, rotation);
            Root.localScale = Vector3.one * scale;
        }

        public void Present(float elapsed)
        {
            elapsed = Mathf.Max(0, elapsed);
            if (!started)
            {
                Root.gameObject.SetActive(true);
                foreach (var renderer in renderers)
                {
                    renderer.GetPropertyBlock(properties);
                    properties.SetVector("_GameObjectWorldPosition", Root.position);
                    renderer.SetPropertyBlock(properties); properties.Clear();
                }
            }
            if (slash)
            {
                int frame = Mathf.Min(9, Mathf.FloorToInt(Mathf.Clamp01(elapsed / Lifetime) * 10));
                properties.SetVector("_MainTex_ST", new Vector4(.25f, .25f, (frame % 4) * .25f, 1 - (frame / 4 + 1) * .25f));
                properties.SetFloat("_ArcCos", Mathf.Cos(65 * Mathf.Deg2Rad));
                slash.SetPropertyBlock(properties); properties.Clear();
            }
            bool restart = !started || elapsed < age;
            float delta = restart ? elapsed : elapsed - age;
            if (restart || delta > 0)
                foreach (var system in systems)
                {
                    system.Simulate(delta, false, restart, false);
                    system.Pause(false);
                }
            age = elapsed; started = true;
        }

        public void Clear()
        {
            foreach (var system in systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Root.gameObject.SetActive(false); age = 0; started = false;
        }
        private static float Maximum(ParticleSystem.MinMaxCurve curve)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant) return Mathf.Max(0, curve.constant);
            if (curve.mode == ParticleSystemCurveMode.TwoConstants) return Mathf.Max(0, Mathf.Max(curve.constantMin, curve.constantMax));
            float maximum = 0;
            void Sample(AnimationCurve value) { if (value != null) foreach (var key in value.keys) maximum = Mathf.Max(maximum, key.value); }
            Sample(curve.curve); Sample(curve.curveMin); Sample(curve.curveMax);
            return maximum * curve.curveMultiplier;
        }
        public void Dispose()
        {
            Clear();
            if(Application.isPlaying)UnityEngine.Object.Destroy(Root.gameObject);
            else UnityEngine.Object.DestroyImmediate(Root.gameObject);
        }
    }

    public sealed class DuelFeedbackPool : IDisposable
    {
        private sealed class Slot { public DuelParticleEffect Effect; public string Kind; public double Tick,Until; public bool Used; }
        public const int Capacity = 48;
        private readonly Slot[] slots = new Slot[Capacity];
        private readonly Transform root;
        private readonly System.Collections.Generic.Dictionary<string, GameObject> prefabs = new System.Collections.Generic.Dictionary<string, GameObject>();
        private uint seed;
        public int AllocatedCount { get; private set; }
        public int PlayedCount { get; private set; }
        public int ActiveCount { get { int count = 0; foreach (var slot in slots) if (slot != null && slot.Used) count++; return count; } }
        public int ParticleCount { get { int count = 0; foreach (var slot in slots) if (slot != null && slot.Used) count += slot.Effect.ParticleCount; return count; } }
        public DuelFeedbackPool(Transform root) { this.root = root; }
        public static GameObject Load(string name) => Resources.Load<GameObject>("TowerDuel/BattleFX/" + name)
            ?? throw new InvalidOperationException("Missing reused VFX " + name + "; run Tower Duel > Prepare reused combat effects.");

        public void Play(string kind, Vector3 position, Vector3 direction, float scale, double tick, double renderTick, double until=double.PositiveInfinity)
        {
            if (!prefabs.TryGetValue(kind, out var prefab)) { prefab = Load(kind); prefabs.Add(kind, prefab); }
            int slotIndex = -1, empty = -1, oldest = -1, kindCount = 0;
            double first = double.PositiveInfinity;
            for (int i = 0; i < slots.Length; i++)
            {
                var candidate = slots[i];
                if (candidate == null) { if (empty < 0) empty = i; continue; }
                if (candidate.Kind != kind) continue;
                kindCount++;
                if (!candidate.Used) { slotIndex = i; break; }
                if (candidate.Tick < first) { first = candidate.Tick; oldest = i; }
            }
            int limit = kind == "Hit" || kind == "Slash" ? 12 : kind == "Collapse" ? 4 : kind == "Death" ? 8 : 6;
            if (slotIndex < 0) slotIndex = kindCount < limit && empty >= 0 ? empty : oldest;
            if (slotIndex < 0) return;
            var slot = slots[slotIndex];
            if (slot == null) { slot = new Slot(); slots[slotIndex] = slot; AllocatedCount++; }
            if (slot.Kind != kind)
            {
                slot.Effect = new DuelParticleEffect(prefab, root, ++seed); slot.Kind = kind;
            }
            slot.Effect.Clear(); slot.Tick = tick; slot.Until=until; slot.Used = true; PlayedCount++;
            var rotation = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;
            slot.Effect.Place(position, rotation, scale);
            Present(slot, renderTick);
        }
        private static void Present(Slot slot, double tick)
        {
            float age = (float)((tick - slot.Tick) / 30);
            if (age < 0) return;
            if (tick>=slot.Until||age > slot.Effect.Lifetime) { slot.Effect.Clear(); slot.Used = false; return; }
            slot.Effect.Present(age);
        }
        public void Update(double renderTick) { foreach (var slot in slots) if (slot != null && slot.Used) Present(slot, renderTick); }
        public void Reset() { foreach (var slot in slots) if (slot != null) { slot.Effect.Clear(); slot.Used = false; } PlayedCount = 0; }
        public void Dispose() { foreach (var slot in slots) slot?.Effect?.Dispose(); }
    }
}
