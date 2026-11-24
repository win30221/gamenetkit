using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDuel
{
    // Snapshots own gameplay. Animator, corpses and arrows are presentation only.
    public sealed class DuelPresentation : IDisposable
    {
        private sealed class UnitVisual
        {
            public DuelUnitArt Art;
            public DuelContactShadow Contact;
            public LineRenderer AuraGlow,AuraRing;
            public SpriteRenderer AuraSprite;
            public Vector3 Previous;
            public Quaternion Leg,Arm,Hip;
            public long Attack,Hit;
            public long ReleaseHit;
            public double ReleaseTick=double.NaN;
            public double Until;
            public int Health;
            public double DamageTick=double.NegativeInfinity;
            public Renderer[] Renderers;
            public Transform Pivot;
            public Vector3 PivotPosition;
            public Sprite SpriteFrame;
            public float SpriteOpacity = 1;
        }
        private sealed class Bolt { public LineRenderer Line; public double Until; }
        private sealed class Arrow { public Transform Root; public Vector3 From,To; public double Started; public long Impact; public float Duration; public bool Bomb; }
        private static readonly int SpeedHash=Animator.StringToHash("Speed"),StrikeHash=Animator.StringToHash("Strike"),DeadHash=Animator.StringToHash("Dead");
        private static readonly int MoveState=Animator.StringToHash("Move"),AttackState=Animator.StringToHash("Attack"),DeathState=Animator.StringToHash("Death");
        private readonly Transform root;
        private readonly DuelRules rules;
        private readonly Dictionary<string,GameObject> prefabs=new Dictionary<string,GameObject>();
        private readonly Dictionary<long,UnitVisual> units=new Dictionary<long,UnitVisual>();
        private readonly Dictionary<long,GameObject> towers=new Dictionary<long,GameObject>();
        private readonly Dictionary<long,long> towerAttacks=new Dictionary<long,long>();
        private readonly Dictionary<long,int> towerHealth=new Dictionary<long,int>();
        private readonly List<UnitVisual> corpses=new List<UnitVisual>();
        private readonly List<Bolt> bolts=new List<Bolt>();
        private readonly List<Arrow> arrows=new List<Arrow>();
        private readonly Material lineMaterial;
        private readonly GameObject arrowPrefab;
        private readonly GameObject bombPrefab;
        private readonly DuelEffects effects;
        private readonly MaterialPropertyBlock flash=new MaterialPropertyBlock();
        private readonly Material trailMaterial;
        private readonly DuelAuraArt auraArt;
        public DuelEffects Effects=>effects;
        public DuelEnvironment Environment {get;}
        public int SpellsSeen=>effects.SeenSpells;
        public DuelDeploymentPreview DeploymentPreview {get;}
        public DuelDeploymentGrid DeploymentGrid {get;}
        public int RenderedKinds {get;private set;}
        public int SpriteKinds {get;private set;}
        public int SpriteShadowKinds {get;private set;}
        public int VisibleTeamDiscs
        {
            get
            {
                int count=0;
                foreach(var visual in units.Values)if(visual.Art.TeamMarker&&visual.Art.TeamMarker.enabled)count++;
                foreach(var visual in corpses)if(visual.Art.TeamMarker&&visual.Art.TeamMarker.enabled)count++;
                return count;
            }
        }
        public int WalkPoseSamples {get;private set;}
        public int AttackPoseSamples {get;private set;}
        public int DeathPoseSamples {get;private set;}
        public int DeathCount {get;private set;}
        public int ArrowsFired {get;private set;}
        public int WalkKinds {get;private set;}
        public int AttackKinds {get;private set;}
        public float UnitHeight(string kind)=>prefabs[kind].GetComponent<DuelUnitArt>().Height;
        public DuelPresentation(Transform parent,DuelMap map,DuelRules rules)
        {
            this.rules=rules;
            root=new GameObject("Battle visuals").transform;root.SetParent(parent,false);
            lineMaterial=Load("Bolt");
            foreach(var card in rules.cards)
            {
                if(card.IsSpell)continue;
                var prefab=DuelSpriteLibrary.Prefab(card.id);
                if(!prefab||!prefab.GetComponent<DuelUnitArt>())throw new InvalidOperationException("Missing equipped unit "+card.id);
                prefabs.Add(card.id,prefab);
            }
            arrowPrefab=Resources.Load<GameObject>("TowerDuel/Units/arrow");
            if(!arrowPrefab)throw new InvalidOperationException("Missing arrow prefab");
            bombPrefab=Resources.Load<GameObject>("TowerDuel/Units/bomb");
            effects=new DuelEffects(root,cue=>parent.GetComponent<TowerDuelClient>()?.Audio.Play(cue));
            trailMaterial=Resources.Load<Material>("TowerDuel/BattleFX/Trail");
            auraArt=new DuelAuraArt();
            Environment = new DuelEnvironment(root, map);
            foreach (var tower in map.towers)
                towers.Add(tower.id, Environment.Building(tower.id));
            DeploymentPreview=new DuelDeploymentPreview(root,prefabs);
            DeploymentGrid=new DuelDeploymentGrid(root,map);
        }
        // Both players see their own keep at the bottom of the fixed board.
        // Seat one rotates the canonical arena by 180 degrees, swapping both lanes.
        // This is its own inverse, so screen-space aiming maps back to server space.
        public static Vector3 MirrorDepth(Vector3 point,int seat)
        {
            if(seat==1){point.x=-point.x;point.z=-point.z;}
            return point;
        }
        private static Material Load(string name)
        {
            var material=Resources.Load<Material>("TowerDuel/Materials/"+name);
            if(!material)throw new InvalidOperationException("Missing battle material "+name);
            return material;
        }
        private DuelMessage previewState;
        private Vector3 spellPoint;
        private CardDefinition previewSpell;
        private bool outlineVisible;
        private readonly MaterialPropertyBlock spellOutline = new();
        private void UpdateSpellOutline(DuelMessage state)
        {
            if(previewSpell==null&&!outlineVisible)return;
            foreach(var e in state.units)
                if(units.TryGetValue(e.id,out var unit)) SetOutline(e,unit.Art.transform.position,unit.Renderers,state.seat);
            foreach(var e in state.towers)
                SetOutline(e,towers[e.id].transform.position,towers[e.id].GetComponentsInChildren<SpriteRenderer>(),state.seat);
            outlineVisible=previewSpell!=null;
        }
        private void SetOutline(EntityState entity,Vector3 renderedPosition,Renderer[] renderers,int seat)
        {
            float reach=(previewSpell?.radius??0)+entity.radius;
            float x=spellPoint.x-renderedPosition.x,z=spellPoint.z-renderedPosition.z;
            bool hit=previewSpell!=null&&entity.seat!=seat&&entity.hp>0&&(!entity.airborne||previewSpell.hitAir)&&x*x+z*z<=reach*reach+1e-5f;
            var tint=previewSpell?.id=="fireball"?new Color(1f,.56f,.15f,1):new Color(.25f,.85f,1f,1);
            foreach(var renderer in renderers)
            {
                if(!(renderer is SpriteRenderer)||!renderer.sharedMaterial.HasProperty("_AimOutline"))continue;
                renderer.GetPropertyBlock(spellOutline);
                spellOutline.SetColor("_AimOutline",hit?tint:Color.clear);
                renderer.SetPropertyBlock(spellOutline);
            }
        }
        private UnitVisual Create(EntityState e,int ownSeat)
        {
            var go=UnityEngine.Object.Instantiate(prefabs[e.kind],root,false);go.name=e.kind+" "+e.id;go.transform.position=MirrorDepth(e.Position,ownSeat);go.SetActive(true);
            var art=go.GetComponent<DuelUnitArt>();
            var contact=DuelContactShadow.Attach(art);
            if(art.Sprites)
            {
                art.Sprites.SetTeam(e.seat==ownSeat?0:1);SpriteKinds|=CardBit(e.kind);
            }
            RenderedKinds|=CardBit(e.kind);
            var pivot=art.transform.Find("Facing");
            return new UnitVisual{Art=art,Contact=contact,Previous=go.transform.position,Hit=e.hitTick,Health=e.hp,Renderers=art.GetComponentsInChildren<Renderer>(),Pivot=pivot,PivotPosition=pivot?pivot.localPosition:Vector3.zero,Leg=art.WalkProbe?art.WalkProbe.localRotation:Quaternion.identity,Arm=art.AttackProbe?art.AttackProbe.localRotation:Quaternion.identity,Hip=art.DeathProbe?art.DeathProbe.localRotation:Quaternion.identity};
        }
        public void Update(DuelSession session)
        {
            var state=session.State;if(state==null)return;
            previewState=state;
            effects.Update(session);
            var live=new HashSet<long>();
            foreach(var e in state.units)
            {
                live.Add(e.id);
                if(!units.TryGetValue(e.id,out var v)){v=Create(e,state.seat);units.Add(e.id,v);}
                var definition=rules.Find(e.kind);
                bool deploying=e.IsDeploying(session.RenderTick);
                bool paused=session.RenderTick<e.pausedUntil;
                Vector3 position=MirrorDepth(session.Sample(e),state.seat);
                float travel=Vector3.ProjectOnPlane(position-v.Previous,Vector3.up).magnitude;
                var direction=MirrorDepth(new Vector3(e.dx,0,e.dz),state.seat);
                v.Art.transform.position=position;v.Previous=position;
                PresentAura(v,definition,position,e.hp>0&&!deploying,session.RenderTick-e.readyTick);
                if(v.Contact.Renderer)v.Contact.Renderer.transform.position=new Vector3(position.x,DuelMap.GroundHeight(position.z)+.003f,position.z);
                if(v.Art.Animator&&!v.Art.Sprites&&direction.sqrMagnitude>.001f)v.Art.transform.rotation=Quaternion.Slerp(v.Art.transform.rotation,Quaternion.LookRotation(direction),15*Time.unscaledDeltaTime);
                float speed=travel/Mathf.Max(.001f,Time.unscaledDeltaTime)/definition.speed;
                float slow=session.SampleSlowed(e)?rules.Find("frost").slow:1;
                if(v.Art.Animator&&!v.Art.Sprites)
                {
                    v.Art.Animator.speed=session.RenderTick<e.pausedUntil?0:slow;
                    v.Art.Animator.SetFloat(SpeedHash,deploying?0:Mathf.Clamp01(speed),.08f,Time.unscaledDeltaTime);
                }
                if(e.hp<v.Health)v.DamageTick=session.RenderTick;
                v.Health=e.hp;
                float reaction=Mathf.Clamp01(1-(float)(session.RenderTick-v.DamageTick)/4f);
                flash.SetColor("_BaseColor",Color.Lerp(Color.white,new Color(2f,.2f,.16f),reaction));
                foreach(var renderer in v.Renderers)if(renderer!=v.Art.TeamMarker&&renderer!=v.Contact.Renderer&&!(renderer is SpriteRenderer))renderer.SetPropertyBlock(flash);
                if(v.Pivot)v.Pivot.localPosition=v.PivotPosition+(v.Art.Sprites?Vector3.zero:new Vector3(0,.045f*reaction,-.065f*reaction));
                if(v.Art.Sprites)
                {
                    v.Art.Sprites.SetBuffed(session.SampleBuffed(e));
                    if((e.kind=="archer"||e.kind=="bomber")&&e.hitTick>=e.attackTick&&e.hitTick>0&&e.hitTick!=v.ReleaseHit&&TryTarget(state,e.hitTarget,out var hitPoint))
                    {
                        // Match the confirmed projectile's presentation start,
                        // and retain it if its target disappears in a later snapshot.
                        v.ReleaseHit=e.hitTick;
                        v.ReleaseTick=e.hitTick-RangedFlightDuration(position+Vector3.up*v.Art.Height*.72f,hitPoint)*30;
                    }
                    v.Art.Sprites.Present(e,definition,paused?e.pausedUntil-rules.Find("fireball").pauseTicks:session.RenderTick,paused?0:speed,deploying,slow,reaction,
                        v.ReleaseHit==e.hitTick&&!double.IsNaN(v.ReleaseTick)?v.ReleaseTick:(double?)null,state.seat);
                }
                if(v.Art.Sprites&&v.Art.Sprites.ShadowMatchesFrame)SpriteShadowKinds|=CardBit(e.kind);
                if(!deploying&&e.attackTick>v.Attack)
                {
                    v.Attack=e.attackTick;if(v.Art.Animator&&!v.Art.Sprites)v.Art.Animator.SetTrigger(StrikeHash);
                }
                if(!deploying&&e.hitTick>v.Hit)
                {
                    v.Hit=e.hitTick;
                    if(TryTarget(state,e.hitTarget,out var target))
                    {
                        if(e.kind=="archer"||e.kind=="bomber")
                        {
                            Vector3 from=position+Vector3.up*v.Art.Height*.72f;
                            var arrow=UnityEngine.Object.Instantiate(e.kind=="bomber"?bombPrefab:arrowPrefab,root,false).transform;
                            if(e.kind=="archer")arrow.localScale=Vector3.one*.65f;
                            var trail=arrow.gameObject.AddComponent<TrailRenderer>();
                            trail.sharedMaterial=trailMaterial;trail.time=.12f;trail.minVertexDistance=.04f;
                            trail.startWidth=e.kind=="bomber"?.08f:.035f;trail.endWidth=0;
                            trail.startColor=new Color(1,.80f,.3f,.8f);trail.endColor=new Color(1,.4f,.1f,0);
                            trail.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                            float duration=RangedFlightDuration(from,target);
                            arrows.Add(new Arrow{Root=arrow,From=from,To=target+Vector3.up*.8f,Started=e.hitTick-duration*30,Impact=e.hitTick,Duration=duration,Bomb=e.kind=="bomber"});
                            arrow.gameObject.SetActive(false);ArrowsFired++;
                        }
                        else
                        {
                            bool heavy=e.kind=="charger"||e.kind=="siege";
                            effects.Slash(position,target-position,heavy,e.hitTick);
                            effects.Impact(target+Vector3.up*.8f,heavy,e.hitTick);
                        }
                    }
                }
                int kindBit=CardBit(e.kind);
                if(v.Art.Sprites)
                {
                    var frame=v.Art.Sprites.Renderer.sprite;
                    if(frame!=v.SpriteFrame&&v.Art.Sprites.Clip==DuelSpriteClip.Walk&&travel>.001f){WalkPoseSamples++;WalkKinds|=kindBit;}
                    if(frame!=v.SpriteFrame&&v.Art.Sprites.Clip==DuelSpriteClip.Attack){AttackPoseSamples++;AttackKinds|=kindBit;}
                    v.SpriteFrame=frame;
                }
                else if(v.Art.Animator)
                {
                    int animation=v.Art.Animator.isInitialized?v.Art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash:0;
                    if(animation==MoveState&&travel>.001f&&Quaternion.Angle(v.Leg,v.Art.WalkProbe.localRotation)>.15f){WalkPoseSamples++;WalkKinds|=kindBit;}
                    if(animation==AttackState&&e.attackTick>0&&state.tick-e.attackTick<definition.cooldown&&Quaternion.Angle(v.Arm,v.Art.AttackProbe.localRotation)>.15f){AttackPoseSamples++;AttackKinds|=kindBit;}
                    v.Leg=v.Art.WalkProbe.localRotation;v.Arm=v.Art.AttackProbe.localRotation;v.Hip=v.Art.DeathProbe.localRotation;
                }
            }
            var removed=new List<long>();foreach(var pair in units)if(!live.Contains(pair.Key))removed.Add(pair.Key);
            foreach(long id in removed)
            {
                var v=units[id];units.Remove(id);
                RemoveAura(v);
                if(v.Art.Sprites)v.Art.Sprites.BeginDeath(DuelSession.Now);
                else if(v.Art.Animator){v.Art.Animator.ResetTrigger(StrikeHash);v.Art.Animator.SetBool(DeadHash,true);v.Art.Animator.SetFloat(SpeedHash,0);v.Art.Animator.speed=1;}
                effects.Death(v.Art.transform.position);
                if(v.Art.TeamMarker)v.Art.TeamMarker.enabled=false;v.Until=DuelSession.Now+1.15;corpses.Add(v);DeathCount++;
            }
            for(int i=corpses.Count-1;i>=0;i--)
            {
                var v=corpses[i];
                if(v.Art.Sprites)
                {
                    v.Art.Sprites.PresentDeath(DuelSession.Now);
                    if(Mathf.Abs(v.Art.Sprites.Opacity-v.SpriteOpacity)>.002f)DeathPoseSamples++;
                    v.SpriteOpacity=v.Art.Sprites.Opacity;
                    v.Contact.SetOpacity(v.SpriteOpacity);
                }
                else if(v.Art.Animator)
                {
                    if(v.Art.Animator.isInitialized&&v.Art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==DeathState&&Quaternion.Angle(v.Hip,v.Art.DeathProbe.localRotation)>.15f)DeathPoseSamples++;
                    v.Hip=v.Art.DeathProbe.localRotation;
                    v.Contact.SetOpacity(Mathf.Clamp01((float)(v.Until-DuelSession.Now)/.4f));
                }
                if(!v.Art.Sprites&&!v.Art.Animator)
                {
                    var body=v.Art.transform.Find("Facing");
                    if(body)body.localScale=Vector3.one*Mathf.Clamp01((float)(v.Until-DuelSession.Now)/.35f);
                }
                if(DuelSession.Now>v.Until){UnityEngine.Object.Destroy(v.Art.gameObject);corpses.RemoveAt(i);}
            }
            foreach(var e in state.towers)
            {
                if(towerHealth.TryGetValue(e.id,out int priorHP)&&priorHP>e.hp)
                {
                    if(e.hp==0)effects.Collapse(towers[e.id].transform.position,e.kind=="keep");
                    else effects.Impact(towers[e.id].transform.position+Vector3.up*1.1f,false,(long)session.RenderTick);
                }
                towerHealth[e.id]=e.hp;
                Environment?.Present(e,session.RenderTick);
                towers[e.id].SetActive(e.hp>0);
                if(e.attackTick>0&&(!towerAttacks.TryGetValue(e.id,out long prior)||prior<e.attackTick))
                {
                    towerAttacks[e.id]=e.attackTick;
                    if(TryTarget(state,e.target,out var target))
                    {
                        var line=NewLine("Tower shot",lineMaterial,.055f);line.positionCount=2;
                        line.SetPosition(0,Environment.Muzzle(e.id));line.SetPosition(1,target+Vector3.up*.7f);
                        bolts.Add(new Bolt{Line=line,Until=DuelSession.Now+.12});
                    }
                }
            }
            for(int i=bolts.Count-1;i>=0;i--)if(DuelSession.Now>bolts[i].Until){UnityEngine.Object.Destroy(bolts[i].Line.gameObject);bolts.RemoveAt(i);}
            for(int i=arrows.Count-1;i>=0;i--)
            {
                var a=arrows[i];float t=(float)(session.RenderTick-a.Started)/(a.Duration*30);
                if(t<0)continue;
                if(t>=1){if(a.Bomb)effects.Burst(a.To,rules.Find("bomber").splash,a.Impact);else effects.Impact(a.To,false,a.Impact);UnityEngine.Object.Destroy(a.Root.gameObject);arrows.RemoveAt(i);continue;}
                a.Root.gameObject.SetActive(true);
                float arc=a.Bomb?1.6f:.18f;
                a.Root.position=Vector3.Lerp(a.From,a.To,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*arc;
                a.Root.rotation=Quaternion.LookRotation(a.To-a.From+Vector3.up*(Mathf.Cos(t*Mathf.PI)*arc*Mathf.PI));
            }
        }
        // Pure presentation: the radius comes from the same canonical card
        // definition the server uses. Never grant buffs from Unity colliders.
        private void PresentAura(UnitVisual visual,CardDefinition definition,Vector3 position,bool active,double elapsedTicks)
        {
            if(definition.auraRadius<=0||definition.moveMultiplier<=1)return;
            if(auraArt.Available)
            {
                if(!visual.AuraSprite)
                {
                    if(!active)return;
                    visual.AuraSprite=auraArt.Create(root);
                }
                visual.AuraSprite.enabled=active;
                if(active)auraArt.Present(visual.AuraSprite,position,definition.auraRadius,elapsedTicks);
                return;
            }
            // Keep gameplay readable if an asset is missing in a development build.
            const int segments=64;
            if(!visual.AuraRing)
            {
                var material=trailMaterial?trailMaterial:lineMaterial;
                visual.AuraGlow=NewLine("Aura glow",material,.22f);
                visual.AuraRing=NewLine("Aura boundary",material,.065f);
                foreach(var line in new[]{visual.AuraGlow,visual.AuraRing})
                {
                    line.transform.SetParent(visual.Art.transform,false);
                    line.loop=true;line.positionCount=segments;
                }
            }
            visual.AuraGlow.enabled=visual.AuraRing.enabled=active;
            if(!active)return;
            var color=new Color(1f,.78f,.28f,1);
            visual.AuraRing.startColor=visual.AuraRing.endColor=color;
            color.a=.20f+.04f*Mathf.Sin((float)(elapsedTicks/30d)*3f);
            visual.AuraGlow.startColor=visual.AuraGlow.endColor=color;
            for(int i=0;i<segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;
                float x=position.x+Mathf.Cos(angle)*definition.auraRadius;
                float z=position.z+Mathf.Sin(angle)*definition.auraRadius;
                float y=DuelMap.GroundHeight(z)+.055f;
                visual.AuraGlow.SetPosition(i,new Vector3(x,y,z));
                visual.AuraRing.SetPosition(i,new Vector3(x,y+.005f,z));
            }
        }
        private static void RemoveAura(UnitVisual visual)
        {
            if(visual.AuraGlow)visual.AuraGlow.enabled=false;
            if(visual.AuraRing)visual.AuraRing.enabled=false;
            if(visual.AuraSprite)
            {
                visual.AuraSprite.enabled=false;
                UnityEngine.Object.Destroy(visual.AuraSprite.gameObject);
                visual.AuraSprite=null;
            }
        }
        private static float RangedFlightDuration(Vector3 from,Vector3 target)=>Mathf.Clamp(Vector3.Distance(from,target)/24,.08f,.24f);
        private bool TryTarget(DuelMessage state,long id,out Vector3 point)
        {
            foreach(var e in state.units)if(e.id==id){point=MirrorDepth(e.Position,state.seat);return true;}
            foreach(var e in state.towers)if(e.id==id){point=MirrorDepth(e.Position,state.seat);return true;}
            if(units.TryGetValue(id,out var v)){point=v.Art.transform.position;return true;}
            point=Vector3.zero;return false;
        }
        private LineRenderer NewLine(string name,Material material,float width)
        {
            var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(root,false);
            line.sharedMaterial=material;line.useWorldSpace=true;line.widthMultiplier=width;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;line.numCapVertices=2;return line;
        }
        private int CardBit(string kind){for(int i=0;i<rules.cards.Length;i++)if(rules.cards[i].id==kind)return 1<<i;return 0;}
        public void Preview(Vector3 point,bool visible,bool valid,string card="guard",int seat=0,Vector3[] formation=null,float radius=.32f,DuelMessage state=null)
        {
            if(formation==null)formation=new[]{point};
            var definition=rules.Find(card);
            spellPoint=MirrorDepth(point,seat);
            DeploymentPreview.Show(card,seat,point,visible,valid,formation,radius,definition.IsSpell,definition.initialAir);
            previewSpell=visible&&valid&&definition.IsSpell?definition:null;
            var current=state??previewState;
            if(current!=null)UpdateSpellOutline(current);
        }
        public void Reset()
        {
            effects.Reset();previewSpell=null;previewState=null;
            foreach(var v in units.Values){RemoveAura(v);UnityEngine.Object.Destroy(v.Art.gameObject);}units.Clear();
            foreach(var v in corpses)UnityEngine.Object.Destroy(v.Art.gameObject);corpses.Clear();
            foreach(var a in arrows)UnityEngine.Object.Destroy(a.Root.gameObject);arrows.Clear();
            foreach(var b in bolts)UnityEngine.Object.Destroy(b.Line.gameObject);bolts.Clear();towerAttacks.Clear();towerHealth.Clear();
            foreach(var tower in towers.Values)tower.SetActive(true);
            Environment?.Reset();
            DeploymentPreview.Hide();
            RenderedKinds=WalkPoseSamples=AttackPoseSamples=DeathPoseSamples=DeathCount=ArrowsFired=WalkKinds=AttackKinds=0;
            SpriteKinds=0;
            SpriteShadowKinds=0;
        }
        public void Dispose(){Reset();auraArt.Dispose();DeploymentGrid.Dispose();DeploymentPreview.Dispose();effects.Dispose();Environment?.Dispose();UnityEngine.Object.Destroy(root.gameObject);}
    }
}
