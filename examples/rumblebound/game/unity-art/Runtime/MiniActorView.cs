using Rumblebound.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rumblebound.Presentation
{
    // Presentation only: the root follows the existing predicted/interpolated
    // position. Neither the rig nor its animation can move a gameplay entity.
    public sealed class MiniActorView : MonoBehaviour
    {
        public static string VisualHeroID(string heroID) { return heroID == "mage" ? "mage" : heroID == "ranger" ? "ranger" : "tank"; }

        public static MiniActorView Create(PlayerState player, Transform parent = null)
        {
            string resource = VisualHeroID(player.heroID) == "mage" ? "KayKit/Mage" : VisualHeroID(player.heroID) == "ranger" ? "KayKit/Ranger" : "KayKit/Knight";
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null) throw new System.InvalidOperationException("Missing character prefab: " + resource);
            var actor = Instantiate(prefab, parent);
            actor.name = player.entityID;
            return actor.GetComponent<MiniActorView>() ?? actor.AddComponent<MiniActorView>();
        }

        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Dead = Animator.StringToHash("Dead");
        private static readonly int Attack = Animator.StringToHash("Attack");
        private const float RingHeight = .07f;
        private Animator animator;
        private Renderer[] visualRenderers;
        private Material ringMaterial;
        private Mesh ringMesh;
        private bool initialized, hasController, hasSnapshot, hasPose, alive = true, online = true;
        private bool previousPrivate, previousOnline;
        private string previousEntity;
        private long previousFireTick, previousSnapshotTick;
        private double previousSnapshotTime, previousPoseTime;
        private Vector3 previousPosition;
        private Vector2 snapshotFacing;
        private int previousPoseFrame = -1;
        private MaterialPropertyBlock[] hitOriginal, hitWorking;
        private Color[] hitBaseColors;
        private bool hitFlashActive;
        private double hitFlashUntil;
        private static readonly int Tint = Shader.PropertyToID("_BaseColor");

        private void Awake() { Initialize(); }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            animator = GetComponentInChildren<Animator>(true);
            visualRenderers = GetComponentsInChildren<Renderer>(true);
            hasController = animator != null && animator.runtimeAnimatorController != null;
            if (animator != null) animator.applyRootMotion = false;
            CreateTeamRing();
        }

        public bool TryGetVisualBounds(out Bounds bounds)
        {
            Initialize();
            bounds = default;
            bool found = false;
            foreach (var renderer in visualRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        public int AttackStarts {get;private set;}
        private long previousLife;
        private void StartAttack(){animator.SetTrigger(Attack);AttackStarts++;}
        public void Present(PlayerState state, Color teamColor, bool privateMatch, long tick, int tickRate,bool suppressAttack=false,double sampleTime=double.NaN)
        {
            Initialize();
            if (state == null) return;
            double now = double.IsNaN(sampleTime)?Time.realtimeSinceStartupAsDouble:sampleTime;
            bool dead = privateMatch && (state.hp <= 0 || state.left);
            alive = !dead;
            online = state.online && !state.left;
            snapshotFacing = privateMatch ? new Vector2((float)state.facingX, (float)state.facingZ) : Vector2.zero;
            if (ringMaterial != null) ringMaterial.color = online && alive ? teamColor : Color.gray;
            if (hasController && animator != null)
            {
                animator.SetBool(Dead, dead);
                if (!online || dead)
                {
                    animator.SetFloat(Speed, 0);
                    animator.ResetTrigger(Attack);
                }
                // A welcome, mode change, respawn/rejoin, or stale snapshot is
                // a baseline. Never replay an old attack after reconnecting.
                bool fresh = hasSnapshot && previousPrivate == privateMatch && previousOnline &&
                    previousEntity == state.entityID && previousLife==state.life && tick > previousSnapshotTick &&
                    tick - previousSnapshotTick <= Mathf.Max(1, tickRate / 2) &&
                    now - previousSnapshotTime <= .5;
                if (fresh && privateMatch && online && alive && state.fireReadyTick > previousFireTick &&
                    state.fireReadyTick > tick && !suppressAttack)
                    StartAttack();
            }
            previousEntity = state.entityID;
            previousLife=state.life;
            previousPrivate = privateMatch;
            previousOnline = online;
            previousFireTick = state.fireReadyTick;
            previousSnapshotTick = tick;
            previousSnapshotTime = now;
            hasSnapshot = true;
        }

        public void RenderPose(Vector3 position, Vector2? ownAim = null,double sampleTime=double.NaN)
        {
            Initialize();
            double now = double.IsNaN(sampleTime)?Time.realtimeSinceStartupAsDouble:sampleTime;
            RenderHitFlash(now);
            transform.position = position;
            // Loading snapshots and Update can present the same actor in one
            // frame. Sample locomotion once so that a duplicate cannot zero it.
            if (previousPoseFrame == Time.frameCount) { previousPosition = position; return; }
            double elapsed = now - previousPoseTime;
            float poseSeconds=double.IsNaN(sampleTime)?Time.deltaTime:(float)System.Math.Max(0,System.Math.Min(.05,elapsed));
            Vector3 delta = position - previousPosition;
            delta.y = 0;
            bool continuous = hasPose && elapsed > .0001 && elapsed <= .25 && delta.sqrMagnitude <= 4;
            float speed = continuous ? delta.magnitude / (float)elapsed : 0;
            if (speed > 30 || !online || !alive) speed = 0; // Teleports and respawns are not locomotion.
            if (hasController && animator != null) animator.SetFloat(Speed, speed, .08f, poseSeconds);

            Vector3 facing = Vector3.zero;
            if (alive && online && ownAim.HasValue && ownAim.Value.sqrMagnitude > .001f)
                facing = new Vector3(ownAim.Value.x, 0, ownAim.Value.y);
            else if (alive && online && snapshotFacing.sqrMagnitude > .001f)
                facing = new Vector3(snapshotFacing.x, 0, snapshotFacing.y);
            else if (speed > .05f)
                facing = delta;
            if (facing.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(facing, Vector3.up), 900 * poseSeconds);

            previousPosition = position;
            previousPoseTime = now;
            previousPoseFrame = Time.frameCount;
            hasPose = true;
        }
        public void PredictAttack()
        {Initialize();if(hasController&&animator!=null&&alive&&online)StartAttack();}
        public void SetPlaybackSpeed(float speed){Initialize();if(animator!=null)animator.speed=speed;}
        public void RejectPredictedAttack()
        {Initialize();if(hasController&&animator!=null){animator.ResetTrigger(Attack);if(alive&&online&&animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))animator.CrossFade("Locomotion",.06f);}}

        // Call when an existing visual survives a transport change. A new
        // welcome should establish pose and action baselines without effects.
        public void ResetPresentation()
        {
            ClearHitFlash();
            hasSnapshot = false;
            hasPose = false;
            snapshotFacing = Vector2.zero;
            previousPoseFrame = -1;
            if (hasController && animator != null)
            {
                animator.SetFloat(Speed, 0);
                animator.ResetTrigger(Attack);
            }
        }

        public void FlashHit(double now)
        {
            Initialize();
            if (hitOriginal == null)
            {
                hitOriginal = new MaterialPropertyBlock[visualRenderers.Length];
                hitWorking = new MaterialPropertyBlock[visualRenderers.Length];
                hitBaseColors = new Color[visualRenderers.Length];
                for (int i = 0; i < visualRenderers.Length; i++)
                {
                    hitOriginal[i] = new MaterialPropertyBlock(); hitWorking[i] = new MaterialPropertyBlock();
                    var material = visualRenderers[i].sharedMaterial;
                    hitBaseColors[i] = material != null && material.HasProperty(Tint) ? material.GetColor(Tint) : Color.white;
                }
            }
            if (!hitFlashActive)
                for (int i = 0; i < visualRenderers.Length; i++)
                    if (visualRenderers[i] != null) visualRenderers[i].GetPropertyBlock(hitOriginal[i]);
            hitFlashActive = true; hitFlashUntil = now + .12;
            RenderHitFlash(now);
        }

        public void RenderHitFlash(double now)
        {
            if (!hitFlashActive) return;
            if (now >= hitFlashUntil) { ClearHitFlash(); return; }
            float intensity = Mathf.Clamp01((float)((hitFlashUntil - now) / .12));
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                var renderer = visualRenderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(hitWorking[i]);
                hitWorking[i].SetColor(Tint, Color.Lerp(hitBaseColors[i], new Color(1f, .35f, .22f), intensity));
                renderer.SetPropertyBlock(hitWorking[i]);
            }
        }

        public void ClearHitFlash()
        {
            if (!hitFlashActive) return;
            for (int i = 0; i < visualRenderers.Length; i++)
                if (visualRenderers[i] != null) visualRenderers[i].SetPropertyBlock(hitOriginal[i]);
            hitFlashActive = false;
        }

        private void CreateTeamRing()
        {
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = direction * .49f;
                vertices[i * 2 + 1] = direction * .39f;
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                int next = (i + 1) % segments * 2;
                int index = i * 6;
                triangles[index] = i * 2;
                triangles[index + 1] = i * 2 + 1;
                triangles[index + 2] = next;
                triangles[index + 3] = next;
                triangles[index + 4] = i * 2 + 1;
                triangles[index + 5] = next + 1;
            }
            ringMesh = new Mesh { name = "Mini actor team ring", hideFlags = HideFlags.DontSave };
            ringMesh.vertices = vertices;
            ringMesh.normals = normals;
            ringMesh.triangles = triangles;
            ringMesh.RecalculateBounds();
            var ring = new GameObject("Team ring");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Vector3.up * RingHeight;
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var renderer = ring.AddComponent<MeshRenderer>();
            var source = Resources.Load<Material>("G0Surface");
            ringMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ringMaterial.name = "Mini actor team color";
            ringMaterial.hideFlags = HideFlags.DontSave;
            ringMaterial.color = Color.white;
            if (ringMaterial.HasProperty("_Smoothness")) ringMaterial.SetFloat("_Smoothness", 0);
            renderer.sharedMaterial = ringMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            DisposeOwned(ringMesh);
            DisposeOwned(ringMaterial);
        }

        private static void DisposeOwned(Object resource)
        {
            if (resource == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(resource); return; }
#endif
            Destroy(resource);
        }
    }
}
