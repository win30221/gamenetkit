using System;
using UnityEngine;

namespace TowerDuel
{
    public sealed class DuelSpriteUnit : MonoBehaviour
    {
        [SerializeField] private DuelSpriteSet set;
        [SerializeField] private SpriteRenderer spriteRenderer;
        private Camera view;
        private MaterialPropertyBlock properties;
        private SpriteRenderer projectedShadow;
        private MaterialPropertyBlock shadowProperties;
        private double lastTick = double.NaN, walkTicks, deathStarted = double.NaN;
        private long attackSeen = -1, attackTarget, cancelledAttack = -1;
        private bool enemy, preview;
        private Color tint = Color.white;
        private float alpha = 1;
        private float hit;
        private DuelSpriteClip shownClip;
        public DuelSpriteSet Set => set;
        public SpriteRenderer Renderer => spriteRenderer;
        public SpriteRenderer ShadowRenderer => projectedShadow;
        public int DirectionIndex { get; private set; } = 4;
        public int FrameIndex { get; private set; }
        public DuelSpriteClip Clip { get; private set; }
        public float Opacity => alpha;
        public float PlaybackSpeed { get; private set; } = 1;
        public bool ShadowMatchesFrame=>projectedShadow&&projectedShadow.sprite==set.ShadowFrame(shownClip,DirectionIndex,FrameIndex);

        public void Configure(DuelSpriteSet definition, SpriteRenderer renderer)
        {
            set = definition; spriteRenderer = renderer;
        }
        private void Awake()
        {
            if (!set || !spriteRenderer) throw new InvalidOperationException("B3 unit is missing its sprite set or renderer");
            properties = new MaterialPropertyBlock();
            view = Camera.main;
            Show(DuelSpriteClip.Idle, DirectionIndex, 0);
        }
        public void SetTeam(int seat) { enemy = seat == 1; ApplyProperties(); }
        public static int Direction(Vector3 heading, Camera camera, int previous = 4)
        {
            if (!camera || heading.sqrMagnitude < .0001f) return previous;
            var front = -camera.transform.forward; front.y = 0; front.Normalize();
            var right = camera.transform.right; right.y = 0; right.Normalize();
            float degrees = Mathf.Atan2(-Vector3.Dot(heading, right), Vector3.Dot(heading, front)) * Mathf.Rad2Deg;
            return (Mathf.FloorToInt((degrees + 22.5f) / 45) + 8) % 8;
        }
        public void Present(EntityState entity, CardDefinition definition, double tick, float movement,
                            bool deploying, float slow, float reaction, double? confirmedReleaseTick = null)
        {
            if (preview || !double.IsNaN(deathStarted)) return;
            PlaybackSpeed = slow;
            if (!view) view = Camera.main;
            var heading = new Vector3(entity.dx, 0, entity.dz);
            if (heading.sqrMagnitude < .0001f) heading = entity.seat == 0 ? Vector3.forward : Vector3.back;
            DirectionIndex = Direction(heading, view, DirectionIndex);
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
                int frame;
                double contact = confirmedReleaseTick ?? entity.hitTick;
                if (entity.hitTick >= entity.attackTick && entity.hitTick > 0 && tick >= contact)
                {
                    double recovery = Math.Max(1, cycle - (contact - entity.attackTick));
                    frame = set.ImpactFrame + Mathf.FloorToInt((float)((tick - contact) / recovery * (set.AttackFrames - set.ImpactFrame)));
                }
                else frame = Mathf.Clamp(Mathf.FloorToInt((float)(elapsed / Math.Max(1, windup) * set.ImpactFrame)), 0, set.ImpactFrame - 1);
                Show(DuelSpriteClip.Attack, DirectionIndex, Mathf.Clamp(frame, 0, set.AttackFrames - 1));
            }
            else if (!deploying && movement > .06f)
            {
                walkTicks += step * Mathf.Clamp(movement, .15f, 2.5f);
                Show(DuelSpriteClip.Walk, DirectionIndex, (int)(walkTicks * 8 / 30) % set.WalkFrames);
            }
            else { walkTicks = 0; Show(DuelSpriteClip.Idle, DirectionIndex, 0); }
            tint = Color.white; hit = Mathf.Clamp01(reaction);
            alpha = 1; ApplyProperties();
        }
        public void BeginDeath(double now)
        {
            deathStarted = now; Clip = DuelSpriteClip.Death; alpha = 1; hit = 0;
        }
        public void PresentDeath(double now)
        {
            if (double.IsNaN(deathStarted)) return;
            alpha = 1 - Mathf.SmoothStep(0, 1, (float)((now - deathStarted) / .8));
            tint = Color.Lerp(Color.white, new Color(.8f, .9f, 1), 1-alpha); ApplyProperties();
        }
        public void PresentPreview(int seat, bool valid)
        {
            preview = true; enemy = seat == 1;
            if (!view) view = Camera.main;
            Show(DuelSpriteClip.Idle, Direction(seat == 0 ? Vector3.forward : Vector3.back, view), 0);
            tint = valid ? new Color(.78f, 1, 1) : new Color(1, .36f, .3f);
            alpha = .42f; ApplyProperties();
        }
        private void Show(DuelSpriteClip clip, int direction, int frame)
        {
            Clip = clip; shownClip=clip; DirectionIndex = direction; FrameIndex = frame;
            spriteRenderer.sprite = set.Frame(clip, direction, frame);
        }
        private void ApplyProperties()
        {
            if (!spriteRenderer) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", tint); properties.SetFloat("_Opacity", alpha);
            properties.SetFloat("_Enemy", enemy ? 1 : 0);
            properties.SetFloat("_Baked",set.Baked?1:0);
            properties.SetFloat("_TeamMaskTop", set.TeamMaskTop);
            float height=set.ReferenceHeight/(float)set.TileSize;
            properties.SetVector("_SpriteVolume",new Vector4(height*.31f,height*.46f,height*.55f,0));
            properties.SetFloat("_Hit", hit); spriteRenderer.SetPropertyBlock(properties);
        }
        private void LateUpdate() => AlignToCamera();
        public void AlignToCamera()
        {
            if (!view) view = Camera.main;
            if (!view || !set || !spriteRenderer) return;
            var billboard = spriteRenderer.transform;
            var forward = view.transform.forward; forward.y = 0;
            billboard.rotation = Quaternion.LookRotation(forward.normalized);
            float scale = set.WorldHeight * set.TileSize / Mathf.Max(1, set.ReferenceHeight);
            if (Clip == DuelSpriteClip.Idle && !preview && !double.IsNaN(lastTick))
                scale *= 1 + .004f * Mathf.Sin((float)(lastTick / 30 * Math.PI * 2));
            if (Clip == DuelSpriteClip.Death) scale *= Mathf.Lerp(.84f, 1, alpha);
            // Stand the sprite upright for correct 3D occlusion, while preserving
            // its authored on-screen proportions at the fixed elevated camera.
            billboard.localScale = new Vector3(scale, scale / Mathf.Max(.15f, view.transform.up.y), scale);
            if (!preview) AlignShadow();
        }
        private void AlignShadow()
        {
            if (!projectedShadow)
            {
                var go=new GameObject("Animated ground shadow");go.transform.SetParent(transform,false);
                projectedShadow=go.AddComponent<SpriteRenderer>();
                projectedShadow.sharedMaterial=Resources.Load<Material>(set.Baked?"TowerDuel/B3/BakedShadow":"TowerDuel/B3/ProjectedShadow");
                if(!projectedShadow.sharedMaterial)throw new InvalidOperationException("Prepare the projected sprite shadow material before building");
                projectedShadow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                projectedShadow.receiveShadows=false;shadowProperties=new MaterialPropertyBlock();
            }
            projectedShadow.sprite=set.ShadowFrame(shownClip,DirectionIndex,FrameIndex);
            if(set.Baked)
            {
                var groundForward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
                projectedShadow.transform.SetPositionAndRotation(transform.position+Vector3.up*.002f,Quaternion.LookRotation(Vector3.down,groundForward));
                float groundScale=spriteRenderer.transform.localScale.x*2;
                projectedShadow.transform.localScale=new Vector3(groundScale,groundScale,1);
                shadowProperties.SetFloat("_Opacity",alpha);projectedShadow.SetPropertyBlock(shadowProperties);
                return;
            }
            projectedShadow.transform.SetPositionAndRotation(spriteRenderer.transform.position,spriteRenderer.transform.rotation);
            projectedShadow.transform.localScale=spriteRenderer.transform.localScale;
            shadowProperties.SetFloat("_GroundHeight",transform.position.y+.002f);
            shadowProperties.SetFloat("_Opacity",alpha);projectedShadow.SetPropertyBlock(shadowProperties);
            // The shader flattens and shears the card beyond its upright bounds.
            // Keep its ground footprint inside the culling bounds as well.
            var bounds=spriteRenderer.bounds;
            var sun=DuelLighting.Sun;
            if(sun)
            {
                var ray=sun.transform.forward;
                var offset=ray*Mathf.Max(0,bounds.max.y-transform.position.y)/Mathf.Max(.1f,-ray.y);
                bounds.Encapsulate(bounds.min+offset);bounds.Encapsulate(bounds.max+offset);
            }
            projectedShadow.bounds=bounds;
        }
    }
}
