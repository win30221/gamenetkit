using Rumblebound.Gameplay;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rumblebound.Presentation
{
    // Cosmetic, bounded bursts. Imported sword animation and fallback geometry
    // share event timing; positions never feed movement or collision.
    public sealed class CombatEffects : IDisposable
    {
        private const int MaxBursts = 32, PiecesPerBurst = 4;
        private enum Effect { Shot, Wall, Player, Swing }
        private sealed class Burst
        {
            public GameObject actor;
            public SlashFlipbook slash;
            public ParticleFeedback wallHit, playerHit, particles;
            public float arcDegrees;
            public readonly Transform[] pieces = new Transform[PiecesPerBurst];
            public readonly Renderer[] renderers = new Renderer[PiecesPerBurst];
            public readonly Vector3[] offsets = new Vector3[PiecesPerBurst];
            public readonly Vector3[] velocities = new Vector3[PiecesPerBurst];
            public readonly Vector3[] scales = new Vector3[PiecesPerBurst];
            public readonly Quaternion[] rotations = new Quaternion[PiecesPerBurst];
            public bool active;
            public long speculativeID;
            public double began;
            public float lifetime;
            public Effect effect;
        }

        private readonly Burst[] bursts = new Burst[MaxBursts];
        private readonly Transform holder;
        private readonly GameObject prefab, slashPrefab, wallHitPrefab, playerHitPrefab;
        private readonly Material cream, gold, coral, white;
        private int activeCount, allocatedCount, particleInstanceCount, variation;
        private bool disposed;
        public int ActiveCount => activeCount;
        public int AllocatedCount => allocatedCount;
        public int PieceCount => allocatedCount * PiecesPerBurst;
        public bool ImportedSlashAvailable => slashPrefab != null;
        public bool ImportedHitsAvailable => wallHitPrefab != null && playerHitPrefab != null;
        public int ParticleInstanceCount => particleInstanceCount;

        public CombatEffects(Transform parent)
        {
            prefab = Resources.Load<GameObject>("G0Floor");
            slashPrefab = Resources.Load<GameObject>("VFX/MeleeSlash");
            wallHitPrefab = Resources.Load<GameObject>("VFX/WallHit");
            playerHitPrefab = Resources.Load<GameObject>("VFX/PlayerHit");
            if (slashPrefab != null && slashPrefab.GetComponent<SlashFlipbook>() == null)
                throw new InvalidOperationException("Imported melee slash requires its flipbook presenter");
            var surface = Resources.Load<Material>("G0Surface");
            if (prefab == null || surface == null)
                throw new InvalidOperationException("Combat feedback requires the baked G0 visual resources");
            holder = new GameObject("Combat feedback effects").transform;
            holder.SetParent(parent, false);
            cream = MakeMaterial(surface, "Combat muzzle cream", new Color(1f, .94f, .57f));
            gold = MakeMaterial(surface, "Combat wall gold", new Color(1f, .64f, .12f));
            coral = MakeMaterial(surface, "Combat hit coral", new Color(1f, .25f, .22f));
            white = MakeMaterial(surface, "Combat impact white", new Color(1f, .98f, .88f));
        }

        private static Material MakeMaterial(Material source, string name, Color color)
        {
            var material = new Material(source) { name = name, color = color, hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * .6f);
                material.EnableKeyword("_EMISSION");
            }
            material.enableInstancing = true;
            return material;
        }

        public void Play(string kind, Vector3 position, Vector2 direction, double now, float reach = 1.25f, float arcDegrees = 120,long speculativeID=0)
        {
            if (disposed || double.IsNaN(now) || double.IsInfinity(now) ||
                !Finite(position.x) || !Finite(position.y) || !Finite(position.z)) return;
            Effect effect;
            if (kind == "shot" || kind == "fire") effect = Effect.Shot;
            else if (kind == "wall" || kind == "boundary") effect = Effect.Wall;
            else if (kind == "player" || kind == "hit") effect = Effect.Player;
            else if (kind == "melee_swing") effect = Effect.Swing;
            else return;

            // Validated hero rules own the range and sector. Reject malformed
            // presentation input instead of silently changing a legal attack.
            if (effect == Effect.Swing && (!Finite(reach) || reach <= 0 || !Finite(reach * 2) ||
                !Finite(arcDegrees) || arcDegrees <= 0 || arcDegrees > 180)) return;

            Vector3 forward = Finite(direction.x) && Finite(direction.y) && direction.sqrMagnitude > .0001f
                ? new Vector3(direction.x, 0, direction.y).normalized : Vector3.forward;
            Burst burst = Acquire(now);
            burst.began = now;
            burst.speculativeID=speculativeID;
            burst.effect = effect;
            burst.lifetime = effect == Effect.Shot ? .12f : effect == Effect.Swing ? (burst.slash != null ? .24f : .18f) : effect == Effect.Wall ? .22f : .20f;
            burst.actor.transform.SetPositionAndRotation(position, Quaternion.identity);
            if (effect == Effect.Wall && wallHitPrefab != null)
            {
                if (burst.wallHit == null)
                {
                    burst.wallHit = new ParticleFeedback(wallHitPrefab, burst.actor.transform, (uint)++particleInstanceCount);
                }
                burst.particles = burst.wallHit;
            }
            else if (effect == Effect.Player && playerHitPrefab != null)
            {
                if (burst.playerHit == null)
                {
                    burst.playerHit = new ParticleFeedback(playerHitPrefab, burst.actor.transform, (uint)++particleInstanceCount);
                }
                burst.particles = burst.playerHit;
            }
            if (burst.particles != null)
            {
                burst.lifetime = burst.particles.Lifetime;
                burst.particles.Begin();
            }
            bool importedSwing = effect == Effect.Swing && burst.slash != null;
            bool importedEffect = importedSwing || burst.particles != null;
            if (burst.slash != null)
            {
                burst.slash.gameObject.SetActive(importedSwing);
                if (importedSwing)
                {
                    float radius = reach;
                    burst.arcDegrees = arcDegrees;
                    burst.slash.transform.localPosition = Vector3.zero;
                    burst.slash.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                    burst.slash.transform.localScale = new Vector3(radius * 2, 1, radius * 2);
                }
            }
            float accent = (++variation % 3 - 1) * 7;
            for (int i = 0; i < PiecesPerBurst; i++)
            {
                burst.renderers[i].enabled = !importedEffect;
                if (importedEffect) continue;
                Vector3 ray;
                if (effect == Effect.Swing)
                {
                    float radius = reach * .78f;
                    float arc = arcDegrees;
                    Vector3 radial = Quaternion.Euler(0, (i - 1.5f) * arc / PiecesPerBurst, 0) * forward;
                    ray = Quaternion.Euler(0, 90, 0) * radial;
                    burst.offsets[i] = radial * radius;
                    burst.velocities[i] = Vector3.zero;
                    burst.scales[i] = new Vector3(.055f, .025f, radius * arc * Mathf.Deg2Rad / PiecesPerBurst * .98f);
                    burst.renderers[i].sharedMaterial = i == 0 || i == 3 ? cream : white;
                }
                else if (effect == Effect.Shot)
                {
                    ray = Quaternion.Euler(0, i == 0 ? 0 : i == 1 ? -52 : i == 2 ? 52 : 0, 0) * forward;
                    burst.offsets[i] = forward * .07f + ray * (i == 3 ? .025f : .09f);
                    burst.velocities[i] = ray * (i == 0 ? 1.2f : .45f);
                    burst.scales[i] = i == 3 ? new Vector3(.13f, .08f, .13f) : new Vector3(.055f, .05f, i == 0 ? .25f : .15f);
                    burst.renderers[i].sharedMaterial = i == 3 ? white : cream;
                }
                else if (effect == Effect.Wall)
                {
                    ray = Quaternion.Euler(0, (i - 1) * 55 + accent, 0) * -forward;
                    burst.offsets[i] = i == 3 ? Vector3.zero : ray * .035f;
                    burst.velocities[i] = i == 3 ? Vector3.zero : ray * (1.3f + i * .2f) + Vector3.up * (.7f + i * .2f);
                    burst.scales[i] = i == 3 ? new Vector3(.19f, .08f, .19f) : new Vector3(.055f, .055f, .20f);
                    burst.renderers[i].sharedMaterial = i == 3 ? white : gold;
                }
                else
                {
                    ray = Quaternion.Euler(0, 45 + i * 90 + accent, 0) * forward;
                    burst.offsets[i] = ray * .10f;
                    burst.velocities[i] = ray * 1.25f + Vector3.up * .15f;
                    burst.scales[i] = new Vector3(.085f, .075f, .27f);
                    burst.renderers[i].sharedMaterial = i % 2 == 0 ? coral : white;
                }
                burst.rotations[i] = Quaternion.LookRotation(ray, Vector3.up);
            }
            RenderBurst(burst, 0);
        }

        private Burst Acquire(double now)
        {
            int slot = -1;
            double oldest = double.PositiveInfinity;
            int oldestSlot = 0;
            for (int i = 0; i < bursts.Length; i++)
            {
                if (bursts[i] == null || !bursts[i].active) { slot = i; break; }
                if (bursts[i].began < oldest) { oldest = bursts[i].began; oldestSlot = i; }
            }
            if (slot < 0) slot = oldestSlot;
            var burst = bursts[slot];
            if (burst == null)
            {
                burst = new Burst { actor = new GameObject("Combat feedback burst") };
                burst.actor.transform.SetParent(holder, false);
                if (slashPrefab != null)
                {
                    var slashActor = UnityEngine.Object.Instantiate(slashPrefab, burst.actor.transform);
                    burst.slash = slashActor.GetComponent<SlashFlipbook>();
                    slashActor.SetActive(false);
                }
                for (int i = 0; i < PiecesPerBurst; i++)
                {
                    var piece = UnityEngine.Object.Instantiate(prefab, burst.actor.transform);
                    piece.name = "Combat sparkle";
                    burst.pieces[i] = piece.transform;
                    var renderer = piece.GetComponent<Renderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    burst.renderers[i] = renderer;
                }
                bursts[slot] = burst;
                allocatedCount++;
            }
            if (!burst.active) activeCount++;
            burst.wallHit?.Clear(); burst.playerHit?.Clear(); burst.particles = null;
            burst.active = true;
            burst.began = now;
            burst.actor.SetActive(true);
            return burst;
        }

        public void Render(double now)
        {
            if (disposed || double.IsNaN(now) || double.IsInfinity(now)) return;
            for (int i = 0; i < bursts.Length; i++)
            {
                var burst = bursts[i];
                if (burst == null || !burst.active) continue;
                float age = (float)Math.Max(0, now - burst.began);
                if (age >= burst.lifetime)
                {
                    burst.particles?.Clear();
                    burst.actor.SetActive(false);
                    burst.active = false;
                    activeCount--;
                }
                else RenderBurst(burst, age);
            }
        }

        private static void RenderBurst(Burst burst, float age)
        {
            if (burst.particles != null)
            {
                burst.particles.Present(age);
                return;
            }
            float t = Mathf.Clamp01(age / burst.lifetime);
            if (burst.effect == Effect.Swing && burst.slash != null)
            {
                burst.slash.Present(t, burst.arcDegrees);
                return;
            }
            float remaining = 1 - t;
            float scale = remaining * remaining;
            float expansion = 1 - remaining * remaining;
            for (int i = 0; i < PiecesPerBurst; i++)
            {
                Vector3 offset = burst.offsets[i] + burst.velocities[i] * (burst.lifetime * expansion * .65f);
                if (burst.effect == Effect.Wall && i < 3) offset.y -= 2 * age * age;
                burst.pieces[i].localPosition = offset;
                burst.pieces[i].localRotation = burst.rotations[i];
                burst.pieces[i].localScale = burst.scales[i] * scale;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < bursts.Length; i++)
                if (bursts[i] != null)
                {
                    bursts[i].wallHit?.Clear(); bursts[i].playerHit?.Clear();
                    bursts[i].particles = null;
                    bursts[i].actor.SetActive(false);
                    bursts[i].active = false;
                }
            activeCount = 0;
        }

        public void ResolveSpeculative(long id,bool accepted)
        {
            if(id<=0)return;
            foreach(var burst in bursts)if(burst!=null&&burst.active&&burst.speculativeID==id){
                burst.speculativeID=0;
                if(!accepted){burst.particles?.Clear();burst.actor.SetActive(false);burst.active=false;activeCount--;}
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
            Destroy(holder.gameObject);
            Destroy(cream); Destroy(gold); Destroy(coral); Destroy(white);
            Array.Clear(bursts, 0, bursts.Length);
            allocatedCount = 0;
            particleInstanceCount = 0;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void Destroy(UnityEngine.Object resource)
        {
            if (resource == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(resource); return; }
#endif
            UnityEngine.Object.Destroy(resource);
        }
    }
}
