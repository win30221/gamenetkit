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
            public bool Hidden;
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
                if(!prefab)prefab=Resources.Load<GameObject>("TowerDuel/Units/"+card.id);
                if(!prefab||!prefab.GetComponent<DuelUnitArt>())throw new InvalidOperationException("Missing equipped unit "+card.id);
                prefabs.Add(card.id,prefab);
            }
            arrowPrefab=Resources.Load<GameObject>("TowerDuel/Units/arrow");
            if(!arrowPrefab)throw new InvalidOperationException("Missing arrow prefab");
            bombPrefab=Resources.Load<GameObject>("TowerDuel/Units/bomb");
            effects=new DuelEffects(root,cue=>parent.GetComponent<TowerDuelClient>()?.Audio.Play(cue));
            trailMaterial=Resources.Load<Material>("TowerDuel/BattleFX/Trail");
            if(map.HasWall)Environment=new DuelEnvironment(root,map);
            var all=UnityEngine.Object.FindObjectsByType<Transform>();
            foreach(var e in map.towers)
            {
                if(Environment!=null){towers[e.id]=Environment.Building(e.id);continue;}
                string color=e.seat==0?"blue":"red";
                string name="SM_"+color+(e.kind=="keep"?"_MainCastle":"_Tower_"+(e.x<0?"-5":"5"));
                foreach(var t in all)if(t.name==name){towers[e.id]=t.gameObject;break;}
                if(!towers.ContainsKey(e.id))throw new InvalidOperationException("Missing arena tower "+name);
            }
            DeploymentPreview=new DuelDeploymentPreview(root,prefabs);
            DeploymentGrid=new DuelDeploymentGrid(root,map);
        }
        private static Material Load(string name)
        {
            var material=Resources.Load<Material>("TowerDuel/Materials/"+name);
            if(!material)throw new InvalidOperationException("Missing battle material "+name);
            return material;
        }
        private UnitVisual Create(EntityState e)
        {
            var go=UnityEngine.Object.Instantiate(prefabs[e.kind],root,false);go.name=e.kind+" "+e.id;go.transform.position=e.Position;
            var art=go.GetComponent<DuelUnitArt>();
            var contact=DuelContactShadow.Attach(art);
            if(art.Sprites)
            {
                art.Sprites.SetTeam(e.seat);SpriteKinds|=CardBit(e.kind);
            }
            RenderedKinds|=CardBit(e.kind);
            var pivot=art.transform.Find("Facing");
            return new UnitVisual{Art=art,Contact=contact,Previous=e.Position,Hit=e.hitTick,Health=e.hp,Renderers=art.GetComponentsInChildren<Renderer>(),Pivot=pivot,PivotPosition=pivot?pivot.localPosition:Vector3.zero,Leg=art.WalkProbe?art.WalkProbe.localRotation:Quaternion.identity,Arm=art.AttackProbe?art.AttackProbe.localRotation:Quaternion.identity,Hip=art.DeathProbe?art.DeathProbe.localRotation:Quaternion.identity};
        }
        public void Update(DuelSession session)
        {
            var state=session.State;if(state==null)return;
            effects.Update(session);
            var live=new HashSet<long>();
            foreach(var e in state.units)
            {
                live.Add(e.id);
                if(e.concealed)
                {
                    if(units.TryGetValue(e.id,out var hidden)){hidden.Hidden=true;hidden.Art.gameObject.SetActive(false);}
                    continue;
                }
                if(!units.TryGetValue(e.id,out var v)){v=Create(e);units.Add(e.id,v);}
                bool justRevealed=false;
                if(v.Hidden)
                {
                    v.Hidden=false;v.Art.gameObject.SetActive(true);v.Previous=e.Position;
                    v.Health=e.hp;v.Hit=e.hitTick;v.Attack=e.attackTick;
                    justRevealed=true;
                }
                var definition=rules.Find(e.kind);
                bool deploying=e.IsDeploying(session.RenderTick);
                Vector3 position=justRevealed?e.Position:session.Sample(e);
                float travel=Vector3.Distance(position,v.Previous);
                v.Art.transform.position=position;v.Previous=position;
                var direction=new Vector3(e.dx,0,e.dz);
                if(!v.Art.Sprites&&direction.sqrMagnitude>.001f)v.Art.transform.rotation=Quaternion.Slerp(v.Art.transform.rotation,Quaternion.LookRotation(direction),15*Time.unscaledDeltaTime);
                float speed=travel/Mathf.Max(.001f,Time.unscaledDeltaTime)/definition.speed;
                float slow=session.SampleSlowed(e)?rules.Find("frost").slow:1;
                if(!v.Art.Sprites)
                {
                    v.Art.Animator.speed=slow;
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
                    if((e.kind=="archer"||e.kind=="bomber")&&e.hitTick>=e.attackTick&&e.hitTick>0&&e.hitTick!=v.ReleaseHit&&TryTarget(state,e.hitTarget,out var hitPoint))
                    {
                        // Match the confirmed projectile's presentation start,
                        // and retain it if its target disappears in a later snapshot.
                        v.ReleaseHit=e.hitTick;
                        v.ReleaseTick=e.hitTick-RangedFlightDuration(position+Vector3.up*v.Art.Height*.72f,hitPoint)*30;
                    }
                    v.Art.Sprites.Present(e,definition,session.RenderTick,speed,deploying,slow,reaction,
                        v.ReleaseHit==e.hitTick&&!double.IsNaN(v.ReleaseTick)?v.ReleaseTick:(double?)null);
                }
                if(v.Art.Sprites&&v.Art.Sprites.ShadowMatchesFrame)SpriteShadowKinds|=CardBit(e.kind);
                if(!deploying&&e.attackTick>v.Attack)
                {
                    v.Attack=e.attackTick;if(!v.Art.Sprites)v.Art.Animator.SetTrigger(StrikeHash);
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
                else
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
                if(v.Hidden){UnityEngine.Object.Destroy(v.Art.gameObject);continue;}
                if(v.Art.Sprites)v.Art.Sprites.BeginDeath(DuelSession.Now);
                else{v.Art.Animator.ResetTrigger(StrikeHash);v.Art.Animator.SetBool(DeadHash,true);v.Art.Animator.SetFloat(SpeedHash,0);v.Art.Animator.speed=1;}
                effects.Death(v.Art.transform.position);
                v.Art.TeamMarker.enabled=false;v.Until=DuelSession.Now+1.15;corpses.Add(v);DeathCount++;
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
                else
                {
                    if(v.Art.Animator.isInitialized&&v.Art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==DeathState&&Quaternion.Angle(v.Hip,v.Art.DeathProbe.localRotation)>.15f)DeathPoseSamples++;
                    v.Hip=v.Art.DeathProbe.localRotation;
                    v.Contact.SetOpacity(Mathf.Clamp01((float)(v.Until-DuelSession.Now)/.4f));
                }
                if(DuelSession.Now>v.Until){UnityEngine.Object.Destroy(v.Art.gameObject);corpses.RemoveAt(i);}
            }
            foreach(var e in state.towers)
            {
                if(towerHealth.TryGetValue(e.id,out int priorHP)&&priorHP>e.hp)
                {
                    if(e.hp==0)effects.Collapse(e.Position,e.kind=="keep");
                    else effects.Impact(e.Position+Vector3.up*1.1f,false,(long)session.RenderTick);
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
                        line.SetPosition(0,Environment!=null?Environment.Muzzle(e.id):new Vector3(e.x,e.kind=="keep"?3.5f:2.9f,e.z));line.SetPosition(1,target+Vector3.up*.7f);
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
        private static float RangedFlightDuration(Vector3 from,Vector3 target)=>Mathf.Clamp(Vector3.Distance(from,target)/24,.08f,.24f);
        private bool TryTarget(DuelMessage state,long id,out Vector3 point)
        {
            foreach(var e in state.units)if(e.id==id&&!e.concealed){point=e.Position;return true;}
            foreach(var e in state.towers)if(e.id==id){point=e.Position;return true;}
            if(units.TryGetValue(id,out var v)&&!v.Hidden){point=v.Art.transform.position;return true;}
            point=Vector3.zero;return false;
        }
        private LineRenderer NewLine(string name,Material material,float width)
        {
            var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(root,false);
            line.sharedMaterial=material;line.useWorldSpace=true;line.widthMultiplier=width;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;line.numCapVertices=2;return line;
        }
        private int CardBit(string kind){for(int i=0;i<rules.cards.Length;i++)if(rules.cards[i].id==kind)return 1<<i;return 0;}
        public void Preview(Vector3 point,bool visible,bool valid,string card="guard",int seat=0,Vector3[] formation=null,float radius=.42f)
        {
            if(formation==null)formation=new[]{point};
            DeploymentPreview.Show(card,seat,point,visible,valid,formation,radius,rules.Find(card).IsSpell);
        }
        public void Reset()
        {
            effects.Reset();
            foreach(var v in units.Values)UnityEngine.Object.Destroy(v.Art.gameObject);units.Clear();
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
        public void Dispose(){Reset();DeploymentGrid.Dispose();DeploymentPreview.Dispose();effects.Dispose();Environment?.Dispose();UnityEngine.Object.Destroy(root.gameObject);}
    }
}
