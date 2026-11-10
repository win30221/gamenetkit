using Rumblebound.Gameplay;
using System;
using UnityEngine;

namespace Rumblebound.Presentation
{
    // Optional artist-authored resources. Prefabs use radius 1 and local-space
    // particles; gameplay continues to use the server's point radius.
    public sealed class PointEffects : IDisposable
    {
        private sealed class Effect
        {
            public GameObject actor;
            public ParticleSystem[] particles;
            public ParticleSystem.MinMaxGradient[] colors;
            public ParticleSystem.Particle[] colorBuffer;
            public bool[] emissions;
            public float[] speeds;
            public bool draining;
        }

        private readonly Transform parent;
        private readonly GameObject auraPrefab, capturePrefab, occupiedPrefab;
        private Effect aura, capture, occupied;
        private PointLocation location;
        private Color presentedColor;
        private string presentedStatus;
        private bool hasPresentation, disposed;
        private float playbackSpeed=1;
        public void SetPlaybackSpeed(float speed)
        {playbackSpeed=speed;ApplyPlaybackSpeed(aura,speed);ApplyPlaybackSpeed(capture,speed);ApplyPlaybackSpeed(occupied,speed);}
        private static void ApplyPlaybackSpeed(Effect effect,float speed)
        {if(effect==null)return;for(int i=0;i<effect.particles.Length;i++){var main=effect.particles[i].main;main.simulationSpeed=effect.speeds[i]*speed;}}
        public bool HasAura => auraPrefab != null;
        public bool HasCapture => capturePrefab != null;
        public bool HasOccupied => occupiedPrefab != null;
        public bool OccupiedActive => occupied != null && occupied.actor.activeSelf && !occupied.draining;
        public bool OccupiedFading => occupied != null && occupied.actor.activeSelf && occupied.draining;
        public int InstanceCount => (aura != null ? 1 : 0) + (capture != null ? 1 : 0) + (occupied != null ? 1 : 0);

        public PointEffects(Transform parent) : this(parent,
            Resources.Load<GameObject>("VFX/PointAura"), Resources.Load<GameObject>("VFX/PointCapture"),
            Resources.Load<GameObject>("VFX/PointOccupied")) { }

        public PointEffects(Transform parent, GameObject auraPrefab, GameObject capturePrefab, GameObject occupiedPrefab = null)
        {
            this.parent = parent;
            this.auraPrefab = auraPrefab;
            this.capturePrefab = capturePrefab;
            this.occupiedPrefab = occupiedPrefab;
        }

        public void Present(PointLocation point, string status, Color teamColor,
            float radiusFallback, bool ended, bool captured)
        {
            if (disposed) return;
            float radius = (float)(point != null && point.radius > 0 ? point.radius : radiusFallback);
            if (ended || point == null || !Finite(point.x) || !Finite(point.z) || !Finite(radius) || radius <= 0)
            { Clear(); return; }

            bool relocated = !hasPresentation || location.index != point.index ||
                location.x != point.x || location.z != point.z || location.radius != point.radius;
            bool recolor = relocated || teamColor != presentedColor || status != presentedStatus;
            if (relocated) { Stop(aura); Stop(capture); Stop(occupied); }
            if (aura == null && auraPrefab != null) aura = Create(auraPrefab, "Control point aura");
            if (aura != null)
            {
                Place(aura, point, radius);
                if (recolor) Tint(aura, teamColor);
                if (relocated || !aura.actor.activeSelf) Play(aura);
            }
            bool hasOccupants = status == "scoring" || status == "contested";
            if (hasOccupants && occupiedPrefab != null)
            {
                if (occupied == null) occupied = Create(occupiedPrefab, "Control point occupied loop");
                Place(occupied, point, radius);
                // Occupancy is a persistent state, including a welcome baseline.
                // Repeated snapshots must not restart the authored animation.
                if (relocated || !occupied.actor.activeSelf) Play(occupied);
                else if (occupied.draining) Resume(occupied);
            }
            else
            {
                Drain(occupied);
                if (!hasOccupants) Drain(capture);
            }
            if (capture != null)
            {
                Place(capture, point, radius);
            }
            if (captured && hasOccupants && capturePrefab != null)
            {
                if (capture == null) capture = Create(capturePrefab, "Control point capture");
                Place(capture, point, radius);
                // The user's selected capture celebration is authored gold.
                // Ownership hue belongs to the persistent transparent rune.
                Play(capture);
            }
            // Do not retain the mutable protocol object as the location baseline.
            location = new PointLocation { index = point.index, x = point.x, z = point.z, radius = point.radius };
            presentedColor = teamColor; presentedStatus = status; hasPresentation = true;
        }

        private Effect Create(GameObject prefab, string name)
        {
            var actor = UnityEngine.Object.Instantiate(prefab, parent);
            actor.name = name;
            actor.SetActive(false);
            var particles = actor.GetComponentsInChildren<ParticleSystem>(true);
            var effect = new Effect { actor = actor, particles = particles,
                colors = new ParticleSystem.MinMaxGradient[particles.Length], emissions = new bool[particles.Length],speeds=new float[particles.Length] };
            int largest = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                var main = particles[i].main;
                largest = Mathf.Max(largest, main.maxParticles);
                effect.colors[i] = main.startColor;
                effect.emissions[i] = particles[i].emission.enabled;
                effect.speeds[i]=main.simulationSpeed;
                // Resource instances are reused for the entire match.
                main.stopAction = ParticleSystemStopAction.None;
            }
            effect.colorBuffer = new ParticleSystem.Particle[largest];
            Stop(effect);
            ApplyPlaybackSpeed(effect,playbackSpeed);
            return effect;
        }

        private static void Place(Effect effect, PointLocation point, float radius)
        {
            effect.actor.transform.SetPositionAndRotation(new Vector3((float)point.x, .025f, (float)point.z), Quaternion.identity);
            effect.actor.transform.localScale = Vector3.one * radius;
        }

        private static void Play(Effect effect)
        {
            Stop(effect);
            effect.actor.SetActive(true);
            foreach (var system in effect.particles) system.Play(false);
        }

        private static void Stop(Effect effect)
        {
            if (effect == null) return;
            foreach (var system in effect.particles)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.actor.SetActive(false);
            Resume(effect);
        }

        private static void Drain(Effect effect)
        {
            if (effect == null || !effect.actor.activeSelf || effect.draining) return;
            effect.draining = true;
            // Keep the simulation and authored lifetime fade running. Play()
            // after StopEmitting would reset time and repeat prewarm on re-entry.
            foreach (var system in effect.particles)
            {
                var emission = system.emission;
                emission.enabled = false;
            }
        }

        private static void Resume(Effect effect)
        {
            for (int i = 0; i < effect.particles.Length; i++)
            {
                var emission = effect.particles[i].emission;
                emission.enabled = effect.emissions[i];
            }
            effect.draining = false;
        }

        public void Tick()
        {
            if (disposed) return;
            FinishDrain(occupied); FinishDrain(capture);
        }

        private static void FinishDrain(Effect effect)
        {
            if (effect == null || !effect.draining) return;
            foreach (var system in effect.particles)
                if (system.particleCount > 0) return;
            Stop(effect);
        }

        private static void Tint(Effect effect, Color tint)
        {
            for (int i = 0; i < effect.particles.Length; i++)
            {
                var main = effect.particles[i].main;
                var source = effect.colors[i];
                switch (source.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        Color color = TintColor(source.color, tint);
                        main.startColor = new ParticleSystem.MinMaxGradient(color);
                        // The rotating rune survives for a full revolution.
                        // Update its existing particle on ownership changes,
                        // rather than waiting for the next emission or restart.
                        int count = effect.particles[i].GetParticles(effect.colorBuffer);
                        for (int index = 0; index < count; index++) effect.colorBuffer[index].startColor = color;
                        effect.particles[i].SetParticles(effect.colorBuffer, count);
                        break;
                    case ParticleSystemGradientMode.TwoColors:
                        main.startColor = new ParticleSystem.MinMaxGradient(TintColor(source.colorMin, tint), TintColor(source.colorMax, tint)); break;
                    case ParticleSystemGradientMode.Gradient:
                        main.startColor = new ParticleSystem.MinMaxGradient(TintGradient(source.gradient, tint)); break;
                    case ParticleSystemGradientMode.TwoGradients:
                        main.startColor = new ParticleSystem.MinMaxGradient(TintGradient(source.gradientMin, tint), TintGradient(source.gradientMax, tint)); break;
                    case ParticleSystemGradientMode.RandomColor:
                        var random = new ParticleSystem.MinMaxGradient(TintGradient(source.gradient, tint));
                        random.mode = ParticleSystemGradientMode.RandomColor;
                        main.startColor = random; break;
                }
            }
        }

        private static Color TintColor(Color source, Color tint)
        {
            // Keep authored intensity and alpha while making team ownership legible.
            float value = Mathf.Max(source.r, source.g, source.b);
            return new Color(value * tint.r, value * tint.g, value * tint.b, source.a * tint.a);
        }

        private static Gradient TintGradient(Gradient source, Color tint)
        {
            if (source == null) return new Gradient();
            var colors = source.colorKeys;
            for (int i = 0; i < colors.Length; i++) colors[i].color = TintColor(colors[i].color, tint);
            var alpha = source.alphaKeys;
            for (int i = 0; i < alpha.Length; i++) alpha[i].alpha *= tint.a;
            var result = new Gradient { mode = source.mode };
            result.SetKeys(colors, alpha);
            return result;
        }

        public void Clear()
        {
            Stop(aura); Stop(capture); Stop(occupied);
            hasPresentation = false; location = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear(); disposed = true;
            if (aura != null) Destroy(aura.actor);
            if (capture != null) Destroy(capture.actor);
            if (occupied != null) Destroy(occupied.actor);
            aura = capture = occupied = null;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Destroy(UnityEngine.Object resource)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(resource); return; }
#endif
            UnityEngine.Object.Destroy(resource);
        }
    }
}
