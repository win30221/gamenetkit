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
            public Vector3 Previous;
            public Quaternion Leg,Arm,Hip;
            public long Attack,Hit;
            public double Until;
        }
        private sealed class Bolt { public LineRenderer Line; public double Until; }
        private sealed class Arrow { public Transform Root; public Vector3 From,To; public double Started; public float Duration; public bool Bomb; }
        private static readonly int SpeedHash=Animator.StringToHash("Speed"),StrikeHash=Animator.StringToHash("Strike"),DeadHash=Animator.StringToHash("Dead");
        private static readonly int MoveState=Animator.StringToHash("Move"),AttackState=Animator.StringToHash("Attack"),DeathState=Animator.StringToHash("Death");
        private readonly Transform root;
        private readonly DuelRules rules;
        private readonly Dictionary<string,GameObject> prefabs=new Dictionary<string,GameObject>();
        private readonly Dictionary<long,UnitVisual> units=new Dictionary<long,UnitVisual>();
        private readonly Dictionary<long,GameObject> towers=new Dictionary<long,GameObject>();
        private readonly Dictionary<long,long> towerAttacks=new Dictionary<long,long>();
        private readonly List<UnitVisual> corpses=new List<UnitVisual>();
        private readonly List<Bolt> bolts=new List<Bolt>();
        private readonly List<Arrow> arrows=new List<Arrow>();
        private readonly Material blue,red,lineMaterial;
        private readonly GameObject arrowPrefab;
        private readonly GameObject bombPrefab;
        private readonly DuelEffects effects;
        public int SpellsSeen=>effects.SeenSpells;
        public DuelDeploymentPreview DeploymentPreview {get;}
        public DuelDeploymentGrid DeploymentGrid {get;}
        public int RenderedKinds {get;private set;}
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
            blue=Load("Blue");red=Load("Red");lineMaterial=Load("Bolt");
            foreach(var card in rules.cards)
            {
                if(card.IsSpell)continue;
                var prefab=Resources.Load<GameObject>("TowerDuel/Units/"+card.id);
                if(!prefab||!prefab.GetComponent<DuelUnitArt>())throw new InvalidOperationException("Missing equipped unit "+card.id);
                prefabs.Add(card.id,prefab);
            }
            arrowPrefab=Resources.Load<GameObject>("TowerDuel/Units/arrow");
            if(!arrowPrefab)throw new InvalidOperationException("Missing arrow prefab");
            bombPrefab=Resources.Load<GameObject>("TowerDuel/Units/bomb");
            effects=new DuelEffects(root,cue=>parent.GetComponent<TowerDuelClient>()?.Audio.Play(cue));
            var all=UnityEngine.Object.FindObjectsByType<Transform>();
            foreach(var e in map.towers)
            {
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
            art.TeamMarker.sharedMaterial=e.seat==0?blue:red;
            RenderedKinds|=CardBit(e.kind);
            return new UnitVisual{Art=art,Previous=e.Position,Hit=e.hitTick,Leg=art.WalkProbe.localRotation,Arm=art.AttackProbe.localRotation,Hip=art.DeathProbe.localRotation};
        }
        public void Update(DuelSession session)
        {
            var state=session.State;if(state==null)return;
            effects.Update(session);
            var live=new HashSet<long>();
            foreach(var e in state.units)
            {
                live.Add(e.id);
                if(!units.TryGetValue(e.id,out var v)){v=Create(e);units.Add(e.id,v);}
                var definition=rules.Find(e.kind);
                Vector3 position=session.Sample(e);
                float travel=Vector3.Distance(position,v.Previous);
                v.Art.transform.position=position;v.Previous=position;
                var direction=new Vector3(e.dx,0,e.dz);
                if(direction.sqrMagnitude>.001f)v.Art.transform.rotation=Quaternion.Slerp(v.Art.transform.rotation,Quaternion.LookRotation(direction),15*Time.unscaledDeltaTime);
                float speed=travel/Mathf.Max(.001f,Time.unscaledDeltaTime)/definition.speed;
                v.Art.Animator.speed=e.slowed?rules.Find("frost").slow:1;
                v.Art.Animator.SetFloat(SpeedHash,Mathf.Clamp01(speed),.08f,Time.unscaledDeltaTime);
                if(e.attackTick>v.Attack)
                {
                    v.Attack=e.attackTick;v.Art.Animator.SetTrigger(StrikeHash);
                }
                if(e.hitTick>v.Hit)
                {
                    v.Hit=e.hitTick;
                    if((e.kind=="archer"||e.kind=="bomber")&&TryTarget(state,e.hitTarget,out var target))
                    {
                        Vector3 from=position+Vector3.up*v.Art.Height*.72f;
                        var arrow=UnityEngine.Object.Instantiate(e.kind=="bomber"?bombPrefab:arrowPrefab,root,false).transform;
                        if(e.kind=="archer")arrow.localScale=Vector3.one*.65f;
                        arrows.Add(new Arrow{Root=arrow,From=from,To=target+Vector3.up*.8f,Started=DuelSession.Now,Duration=Mathf.Max(.12f,Vector3.Distance(from,target)/24),Bomb=e.kind=="bomber"});
                        ArrowsFired++;
                    }
                }
                int kindBit=CardBit(e.kind);
                int animation=v.Art.Animator.isInitialized?v.Art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash:0;
                if(animation==MoveState&&travel>.001f&&Quaternion.Angle(v.Leg,v.Art.WalkProbe.localRotation)>.15f){WalkPoseSamples++;WalkKinds|=kindBit;}
                if(animation==AttackState&&e.attackTick>0&&state.tick-e.attackTick<definition.cooldown&&Quaternion.Angle(v.Arm,v.Art.AttackProbe.localRotation)>.15f){AttackPoseSamples++;AttackKinds|=kindBit;}
                v.Leg=v.Art.WalkProbe.localRotation;v.Arm=v.Art.AttackProbe.localRotation;v.Hip=v.Art.DeathProbe.localRotation;
            }
            var removed=new List<long>();foreach(var pair in units)if(!live.Contains(pair.Key))removed.Add(pair.Key);
            foreach(long id in removed)
            {
                var v=units[id];units.Remove(id);v.Art.Animator.ResetTrigger(StrikeHash);v.Art.Animator.SetBool(DeadHash,true);v.Art.Animator.SetFloat(SpeedHash,0);v.Art.Animator.speed=1;
                v.Art.TeamMarker.enabled=false;v.Until=DuelSession.Now+1.15;corpses.Add(v);DeathCount++;
            }
            for(int i=corpses.Count-1;i>=0;i--)
            {
                var v=corpses[i];
                if(v.Art.Animator.isInitialized&&v.Art.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==DeathState&&Quaternion.Angle(v.Hip,v.Art.DeathProbe.localRotation)>.15f)DeathPoseSamples++;
                v.Hip=v.Art.DeathProbe.localRotation;
                if(DuelSession.Now>v.Until){UnityEngine.Object.Destroy(v.Art.gameObject);corpses.RemoveAt(i);}
            }
            foreach(var e in state.towers)
            {
                towers[e.id].SetActive(e.hp>0);
                if(e.attackTick>0&&(!towerAttacks.TryGetValue(e.id,out long prior)||prior<e.attackTick))
                {
                    towerAttacks[e.id]=e.attackTick;
                    if(TryTarget(state,e.target,out var target))
                    {
                        var line=NewLine("Tower shot",lineMaterial,.055f);line.positionCount=2;
                        line.SetPosition(0,new Vector3(e.x,e.kind=="keep"?3.5f:2.9f,e.z));line.SetPosition(1,target+Vector3.up*.7f);
                        bolts.Add(new Bolt{Line=line,Until=DuelSession.Now+.12});
                    }
                }
            }
            for(int i=bolts.Count-1;i>=0;i--)if(DuelSession.Now>bolts[i].Until){UnityEngine.Object.Destroy(bolts[i].Line.gameObject);bolts.RemoveAt(i);}
            for(int i=arrows.Count-1;i>=0;i--)
            {
                var a=arrows[i];float t=(float)(DuelSession.Now-a.Started)/a.Duration;
                if(t>=1){if(a.Bomb)effects.Burst(a.To,rules.Find("bomber").splash);UnityEngine.Object.Destroy(a.Root.gameObject);arrows.RemoveAt(i);continue;}
                a.Root.position=Vector3.Lerp(a.From,a.To,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.18f;
                a.Root.rotation=Quaternion.LookRotation(a.To-a.From);
            }
        }
        private bool TryTarget(DuelMessage state,long id,out Vector3 point)
        {
            foreach(var e in state.units)if(e.id==id){point=e.Position;return true;}
            foreach(var e in state.towers)if(e.id==id){point=e.Position;return true;}
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
            foreach(var b in bolts)UnityEngine.Object.Destroy(b.Line.gameObject);bolts.Clear();towerAttacks.Clear();
            foreach(var tower in towers.Values)tower.SetActive(true);
            DeploymentPreview.Hide();
            RenderedKinds=WalkPoseSamples=AttackPoseSamples=DeathPoseSamples=DeathCount=ArrowsFired=WalkKinds=AttackKinds=0;
        }
        public void Dispose(){Reset();DeploymentGrid.Dispose();DeploymentPreview.Dispose();effects.Dispose();UnityEngine.Object.Destroy(root.gameObject);}
    }
}
