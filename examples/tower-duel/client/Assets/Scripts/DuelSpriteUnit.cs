using System;
using UnityEngine;

namespace TowerDuel
{
    public sealed class DuelSpriteUnit : MonoBehaviour
    {
        [SerializeField] private DuelSpriteSet set;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private DuelSpriteSet airborneSet;
        private Camera view;
        private MaterialPropertyBlock properties;
        private SpriteRenderer projectedShadow;
        private MaterialPropertyBlock shadowProperties;
        private double lastTick = double.NaN, walkTicks, idleTicks, deathStarted = double.NaN;
        private long attackSeen = -1, attackTarget, cancelledAttack = -1;
        private bool enemy, preview, airborne;
        private Color tint = Color.white;
        private bool buffed;
        private static readonly Color BuffOutline = new Color(1f,.78f,.22f,1f);
        private float alpha = 1;
        private float hit;
        private DuelSpriteClip shownClip;
        public DuelSpriteSet Set => airborne && airborneSet ? airborneSet : set;
        public SpriteRenderer Renderer => spriteRenderer;
        public SpriteRenderer ShadowRenderer => projectedShadow;
        public int DirectionIndex { get; private set; } = 4;
        public int FrameIndex { get; private set; }
        public DuelSpriteClip Clip { get; private set; }
        public float Opacity => alpha;
        public float PlaybackSpeed { get; private set; } = 1;
        public bool ShadowMatchesFrame=>projectedShadow&&projectedShadow.sprite==Set.ShadowFrame(shownClip,DirectionIndex,FrameIndex);

        public void Configure(DuelSpriteSet definition, SpriteRenderer renderer, DuelSpriteSet airborneDefinition = null)
        {
            set = definition; spriteRenderer = renderer;
            airborneSet = airborneDefinition;
        }
        private void Awake()
        {
            if (!set || !spriteRenderer) throw new InvalidOperationException("B3 unit is missing its sprite set or renderer");
            properties = new MaterialPropertyBlock();
            view = Camera.main;
            Show(DuelSpriteClip.Idle, DirectionIndex, 0);
        }
        public void SetTeam(int seat) { enemy = seat == 1; ApplyProperties(); }
        public void SetBuffed(bool value) { buffed = value && !preview && double.IsNaN(deathStarted); }
        public static int Direction(Vector3 heading, Camera camera, int previous = 4)
        {
            if (!camera || heading.sqrMagnitude < .0001f) return previous;
            var front = -camera.transform.forward; front.y = 0; front.Normalize();
            var right = camera.transform.right; right.y = 0; right.Normalize();
            float degrees = Mathf.Atan2(-Vector3.Dot(heading, right), Vector3.Dot(heading, front)) * Mathf.Rad2Deg;
            return (Mathf.FloorToInt((degrees + 22.5f) / 45) + 8) % 8;
        }
        public void Present(EntityState entity, CardDefinition definition, double tick, float movement,
                            bool deploying, float slow, float reaction, double? confirmedReleaseTick = null, int viewSeat = 0)
        {
            if (preview || !double.IsNaN(deathStarted)) return;
            if (airborne != entity.airborne)
            {
                airborne = entity.airborne;
                walkTicks = idleTicks = 0;
                lastTick = double.NaN;
            }
            var currentSet = Set;
            PlaybackSpeed = slow;
            if (!view) view = Camera.main;
            var heading = new Vector3(entity.dx, 0, entity.dz);
            if (heading.sqrMagnitude < .0001f) heading = entity.seat == 0 ? Vector3.forward : Vector3.back;
            DirectionIndex = Direction(DuelPresentation.MirrorDepth(heading,viewSeat), view, DirectionIndex);
            double step = double.IsNaN(lastTick) ? 0 : Math.Max(0, tick - lastTick); lastTick = tick;
            if (entity.attackTick != attackSeen)
            {
                attackSeen = entity.attackTick; attackTarget = entity.target;
            }
            if (entity.target != attackTarget && entity.hitTick < entity.attackTick) cancelledAttack = entity.attackTick;
            double elapsed = tick - entity.attackTick;
            float cycle = definition.cooldown / Mathf.Max(.1f, slow);
            float windup = definition.windupTicks / Mathf.Max(.1f, slow);
            bool attacking = !deploying && entity.target != 0 && entity.attackTick > 0 &&
                entity.attackTick != cancelledAttack && elapsed >= 0 && elapsed < cycle &&
                (movement < .25f || elapsed < windup);
            if (attacking)
            {
                idleTicks = 0;
                int frame;
                double contact = confirmedReleaseTick ?? entity.hitTick;
                if (entity.hitTick >= entity.attackTick && entity.hitTick > 0 && tick >= contact)
                {
                    double recovery = Math.Max(1, cycle - (contact - entity.attackTick));
                    frame = currentSet.ImpactFrame + Mathf.FloorToInt((float)((tick - contact) / recovery * (currentSet.AttackFrames - currentSet.ImpactFrame)));
                }
                else frame = Mathf.Clamp(Mathf.FloorToInt((float)(elapsed / Math.Max(1, windup) * currentSet.ImpactFrame)), 0, currentSet.ImpactFrame - 1);
                Show(DuelSpriteClip.Attack, DirectionIndex, Mathf.Clamp(frame, 0, currentSet.AttackFrames - 1));
            }
            else if (!deploying && (movement > .06f ||
                     (airborne && currentSet.FrameCount(DuelSpriteClip.Idle) == 1)))
            {
                idleTicks = 0;
                walkTicks += step * (airborne ? slow : Mathf.Clamp(movement, .15f, 2.5f));
                Show(DuelSpriteClip.Walk, DirectionIndex, currentSet.LoopFrame(DuelSpriteClip.Walk, walkTicks / 30d));
            }
            else
            {
                walkTicks = 0;
                if (deploying || shownClip != DuelSpriteClip.Idle) idleTicks = 0;
                else idleTicks += step * Mathf.Max(0f, slow);
                Show(DuelSpriteClip.Idle, DirectionIndex,
                    deploying ? 0 : currentSet.LoopFrame(DuelSpriteClip.Idle, idleTicks / 30d));
            }
            tint = Color.white; hit = Mathf.Clamp01(reaction);
            alpha = 1; ApplyProperties();
        }
        public void BeginDeath(double now)
        {
            deathStarted = now; Clip = DuelSpriteClip.Death; alpha = 1; hit = 0; buffed = false; ApplyProperties();
        }
        public void PresentDeath(double now)
        {
            if (double.IsNaN(deathStarted)) return;
            alpha = 1 - Mathf.SmoothStep(0, 1, (float)((now - deathStarted) / .8));
            tint = Color.Lerp(Color.white, new Color(.8f, .9f, 1), 1-alpha); ApplyProperties();
        }
        public void PresentPreview(int seat, bool valid)
        {
            preview = true; enemy = false; buffed = false;
            if (!view) view = Camera.main;
            Show(DuelSpriteClip.Idle, Direction(seat == 0 ? Vector3.forward : Vector3.back, view), 0);
            tint = valid ? new Color(.78f, 1, 1) : new Color(1, .36f, .3f);
            alpha = .42f; ApplyProperties();
        }
        private void Show(DuelSpriteClip clip, int direction, int frame)
        {
            Clip = clip; shownClip=clip; DirectionIndex = direction; FrameIndex = frame;
            spriteRenderer.sprite = Set.Frame(clip, direction, frame);
        }
        private void ApplyProperties()
        {
            if (!spriteRenderer) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            // Preserve the independent spell aim outline set by presentation.
            spriteRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BuffOutline", buffed ? BuffOutline : Color.clear);
            properties.SetColor("_BaseColor", tint); properties.SetFloat("_Opacity", alpha);
            properties.SetFloat("_Enemy", enemy ? 1 : 0);
            var currentSet = Set;
            properties.SetFloat("_Baked",currentSet.Baked?1:0);
            properties.SetFloat("_TeamMaskTop", currentSet.TeamMaskTop);
            float height=currentSet.ReferenceHeight/(float)currentSet.TileSize;
            properties.SetVector("_SpriteVolume",new Vector4(height*.31f,height*.46f,height*.55f,0));
            properties.SetFloat("_Hit", hit); spriteRenderer.SetPropertyBlock(properties);
        }
        private void LateUpdate() => AlignToCamera();
        public void AlignToCamera()
        {
            if (!view) view = Camera.main;
            var currentSet = Set;
            if (!view || !currentSet || !spriteRenderer) return;
            var billboard = spriteRenderer.transform;
            var forward = view.transform.forward; forward.y = 0;
            billboard.rotation = Quaternion.LookRotation(forward.normalized);
            float scale = currentSet.WorldHeight * currentSet.TileSize / Mathf.Max(1, currentSet.ReferenceHeight);
            scale *= TowerDuelClient.VisualScaleForSize(view.orthographicSize);
            if (Clip == DuelSpriteClip.Idle && currentSet.FrameCount(DuelSpriteClip.Idle) == 1 && !preview && !double.IsNaN(lastTick))
                scale *= 1 + .004f * Mathf.Sin((float)(lastTick / 30 * Math.PI * 2));
            if (Clip == DuelSpriteClip.Death) scale *= Mathf.Lerp(.84f, 1, alpha);
            // Stand the sprite upright for correct 3D occlusion, while preserving
            // its authored on-screen proportions at the fixed elevated camera.
            billboard.localScale = new Vector3(scale, scale / Mathf.Max(.15f, view.transform.up.y), scale);
            if (!preview) AlignShadow(currentSet);
        }
        private void AlignShadow(DuelSpriteSet currentSet)
        {
            if (!projectedShadow)
            {
                var go=new GameObject("Animated ground shadow");go.transform.SetParent(transform,false);
                projectedShadow=go.AddComponent<SpriteRenderer>();
                projectedShadow.sharedMaterial=Resources.Load<Material>(currentSet.Baked?"TowerDuel/B3/BakedShadow":"TowerDuel/B3/ProjectedShadow");
                if(!projectedShadow.sharedMaterial)throw new InvalidOperationException("Prepare the projected sprite shadow material before building");
                projectedShadow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                projectedShadow.receiveShadows=false;shadowProperties=new MaterialPropertyBlock();
            }
            projectedShadow.sprite=currentSet.ShadowFrame(shownClip,DirectionIndex,FrameIndex);
            var ground = transform.position;
            if (airborne) ground.y = DuelMap.GroundHeight(ground.z);
            ground.y += .002f;
            shadowProperties.SetFloat("_Opacity", alpha * (airborne ? .55f : 1));
            if(currentSet.Baked)
            {
                var groundForward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
                projectedShadow.transform.SetPositionAndRotation(ground,Quaternion.LookRotation(Vector3.down,groundForward));
                float groundScale=spriteRenderer.transform.localScale.x*2;
                projectedShadow.transform.localScale=new Vector3(groundScale,groundScale,1);
                projectedShadow.SetPropertyBlock(shadowProperties);
                return;
            }
            projectedShadow.transform.SetPositionAndRotation(spriteRenderer.transform.position,spriteRenderer.transform.rotation);
            projectedShadow.transform.localScale=spriteRenderer.transform.localScale;
            shadowProperties.SetFloat("_GroundHeight",ground.y);
            projectedShadow.SetPropertyBlock(shadowProperties);
            // The shader flattens and shears the card beyond its upright bounds.
            // Keep its ground footprint inside the culling bounds as well.
            var bounds=spriteRenderer.bounds;
            var sun=DuelLighting.Sun;
            if(sun)
            {
                var ray=sun.transform.forward;
                var offset=ray*Mathf.Max(0,bounds.max.y-ground.y)/Mathf.Max(.1f,-ray.y);
                bounds.Encapsulate(bounds.min+offset);bounds.Encapsulate(bounds.max+offset);
            }
            projectedShadow.bounds=bounds;
        }
    }
}
