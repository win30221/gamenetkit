using Rumblebound.Gameplay;
using System;
using UnityEngine;

namespace Rumblebound.Presentation
{
    // One artist-authored particle prefab owned by a CombatEffects pool slot.
    // Unity's automatic playback remains paused; the presentation clock also
    // drives native previews, so a preview and a live hit use identical timing.
    public sealed class ParticleFeedback
    {
        private readonly GameObject actor;
        private readonly ParticleSystem[] systems;
        private readonly Renderer[] renderers;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private float renderedAge;
        private bool started;
        private static readonly int WorldPosition = Shader.PropertyToID("_GameObjectWorldPosition");
        public float Lifetime { get; }

        public ParticleFeedback(GameObject prefab, Transform parent, uint seed)
        {
            actor = UnityEngine.Object.Instantiate(prefab, parent);
            actor.SetActive(false);
            systems = actor.GetComponentsInChildren<ParticleSystem>(true);
            renderers = actor.GetComponentsInChildren<Renderer>(true);
            if (systems.Length == 0) throw new InvalidOperationException("Imported combat effect has no particle systems: " + prefab.name);
            float lifetime = 0;
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = systems[i].main;
                main.playOnAwake = false;
                main.loop = false;
                main.stopAction = ParticleSystemStopAction.None;
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = unchecked(seed * 747796405u + (uint)i * 2891336453u + 1u);
                lifetime = Mathf.Max(lifetime, (Maximum(main.startDelay) + main.duration + Maximum(main.startLifetime)) /
                    Mathf.Max(.01f, main.simulationSpeed));
            }
            Lifetime = Mathf.Clamp(lifetime, .12f, 3f);
            Clear();
        }

        public void Begin()
        {
            Clear();
            actor.SetActive(true);
            // The package shader can use the effect origin for its authored
            // dissolve coordinates. Keep the shared material unchanged.
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(properties);
                properties.SetVector(WorldPosition, actor.transform.position);
                renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
            Present(0);
        }

        public void Present(float age)
        {
            age = Mathf.Clamp(age, 0, Lifetime);
            bool restart = !started || age < renderedAge;
            float elapsed = restart ? age : age - renderedAge;
            if (restart || elapsed > 0)
                foreach (var system in systems)
                {
                    // Every child is advanced once, including its start delay.
                    // Simulate pauses afterwards; no automatic Update adds time.
                    system.Simulate(elapsed, false, restart, false);
                    system.Pause(false);
                }
            renderedAge = age;
            started = true;
        }

        public void Clear()
        {
            foreach (var system in systems)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            actor.SetActive(false);
            renderedAge = 0;
            started = false;
        }

        private static float Maximum(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant: return Mathf.Max(0, curve.constant);
                case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(0, Mathf.Max(curve.constantMin, curve.constantMax));
                case ParticleSystemCurveMode.Curve: return CurveMaximum(curve.curve) * curve.curveMultiplier;
                default: return Mathf.Max(CurveMaximum(curve.curveMin), CurveMaximum(curve.curveMax)) * curve.curveMultiplier;
            }
        }

        private static float CurveMaximum(AnimationCurve curve)
        {
            float maximum = 0;
            if (curve != null) foreach (var key in curve.keys) maximum = Mathf.Max(maximum, key.value);
            return maximum;
        }
    }
}
