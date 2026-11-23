using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDuel
{
    // Reuses the acquired Rumblebound VFX. Spell boundaries and playback follow SDK render ticks.
    public sealed class DuelEffects : IDisposable
    {
        private sealed class Visual
        {
            public Transform Root;
            public DuelParticleEffect Particles;
            public LineRenderer Ring,Trail;
            public bool Impacted;
        }
        private readonly Transform root;
        private readonly Dictionary<long,Visual> visuals=new Dictionary<long,Visual>();
        private readonly Action<string> sound;
        private readonly DuelFeedbackPool feedback;
        private double clock;
        private double endedAt=-1,endedTick;
        private DuelMessage welcome;
        public int SeenSpells {get;private set;}
        public int ActiveFeedback=>feedback.ActiveCount;
        public int AllocatedFeedback=>feedback.AllocatedCount;
        public int FeedbackPlayed=>feedback.PlayedCount;
        public int ParticleCount=>feedback.ParticleCount;
        public DuelEffects(Transform parent,Action<string> sound=null)
        {
            root=new GameObject("Spell effects").transform;root.SetParent(parent,false);
            this.sound=sound;feedback=new DuelFeedbackPool(root);
        }
        private static Mesh mesh;
        public static Mesh CrystalMesh()
        {
            if(mesh)return mesh;
            Vector3[] p={Vector3.up,Vector3.right,Vector3.forward,Vector3.left,Vector3.back,Vector3.down};
            int[] triangles={0,2,1,0,3,2,0,4,3,0,1,4,5,1,2,5,2,3,5,3,4,5,4,1};
            var vertices=new Vector3[24];var indices=new int[24];for(int i=0;i<24;i++){vertices[i]=p[triangles[i]];indices[i]=i;}
            mesh=new Mesh{name="Duel spell crystal"};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();return mesh;
        }
        private static Material Material(string name)=>Resources.Load<Material>("TowerDuel/FX/"+name);
        public static GameObject Symbol(string kind,Transform parent)
        {
            var go=new GameObject(kind+" visual");go.transform.SetParent(parent,false);
            void Crystal(string name,Vector3 position,Vector3 scale,string material)
            {
                var child=new GameObject(name);child.transform.SetParent(go.transform,false);child.transform.localPosition=position;child.transform.localScale=scale;
                child.AddComponent<MeshFilter>().sharedMesh=CrystalMesh();var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=Material(material);renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if(kind=="fireball")
            {
                Crystal("Amber flame",Vector3.zero,Vector3.one*.65f,"Fire");Crystal("Hot core",new Vector3(.08f,.08f,-.4f),Vector3.one*.43f,"Gold");
                for(int i=0;i<5;i++)Crystal("Flame shard",new Vector3(Mathf.Cos(i*1.25f)*.4f,Mathf.Sin(i*1.25f)*.4f,.3f+i*.14f),new Vector3(.20f,.20f,.38f),"Fire");
            }
            else
            {
                for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Crystal("Ice shard",new Vector3(Mathf.Cos(a)*.65f,.3f,Mathf.Sin(a)*.65f),new Vector3(.15f,.55f,.15f),"Ice");}
                Crystal("Ice core",Vector3.up*.25f,Vector3.one*.36f,"Ice");
            }
            return go;
        }
        private LineRenderer Line(Transform parent,string name,string material,float width)
        {
            var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(parent,false);
            line.sharedMaterial=Resources.Load<Material>("TowerDuel/BattleFX/Trail");
            line.useWorldSpace=true;line.widthMultiplier=width;
            line.startColor=line.endColor=material=="Ice"?new Color(.36f,.85f,1,.7f):new Color(1,.63f,.12f,.6f);
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;return line;
        }
        public void Update(DuelSession session)
        {
            var state=session.State;if(state==null)return;
            double renderTick=session.RenderTick;
            clock=renderTick;
            if(state.type=="welcome"&&!ReferenceEquals(welcome,state)){welcome=state;ClearSpells();feedback.Reset();}
            if(state.phase=="ended")
            {
                if(endedAt<0){endedAt=DuelSession.Now;endedTick=renderTick;ClearSpells();}
                // Gameplay has ended; let the final castle debris settle behind the result panel.
                clock=endedTick+(DuelSession.Now-endedAt)*30;feedback.Update(clock);return;
            }
            endedAt=-1;
            feedback.Update(renderTick);
            var seen=new HashSet<long>();
            foreach(var e in session.SampleEffects())
            {
                seen.Add(e.id);
                if(!visuals.TryGetValue(e.id,out var visual))
                {
                    var owner=new GameObject(e.kind+" "+e.id).transform;owner.SetParent(root,false);
                    visual=new Visual{Root=owner,Particles=new DuelParticleEffect(DuelFeedbackPool.Load(e.kind=="fireball"?"Fireball":"Frost"),owner,unchecked((uint)e.id)),Ring=Line(owner,"Effect radius",e.kind=="fireball"?"Fire":"Ice",.065f),Trail=Line(owner,"Fire trail","Gold",.22f)};
                    visual.Ring.loop=true;visual.Ring.positionCount=32;visual.Trail.positionCount=8;visuals.Add(e.id,visual);SeenSpells|=e.kind=="fireball"?1:2;
                    visual.Trail.widthCurve=AnimationCurve.Linear(0,1,1,0);
                    visual.Trail.startColor=new Color(1,.88f,.40f,.85f);visual.Trail.endColor=new Color(1,.27f,.035f,0);
                }
                var target=DuelPresentation.MirrorDepth(new Vector3(e.x,.3f,e.z),state.seat);
                for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;visual.Ring.SetPosition(i,target+new Vector3(Mathf.Cos(a)*e.radius,0,Mathf.Sin(a)*e.radius));}
                bool impact=renderTick>=e.impact;
                if(impact&&!visual.Impacted)
                {
                    visual.Impacted=true;sound?.Invoke(e.kind);
                    if(e.kind=="fireball")feedback.Play("Explosion",target,Vector3.zero,e.radius,e.impact,renderTick,e.end);
                }
                visual.Trail.enabled=e.kind=="fireball"&&!impact;
                if(e.kind=="fireball")
                {
                    float t=Mathf.Clamp01((float)(renderTick-e.start)/Mathf.Max(1,e.impact-e.start));
                    Vector3 Flight(float progress)=>target+Vector3.up*(9f*(1-progress*progress));
                    if(impact)visual.Particles.Clear();
                    else
                    {
                        visual.Particles.Place(Flight(t),Quaternion.identity,1);
                        visual.Particles.Present((float)(renderTick-e.start)/30f+.12f);
                    }
                    for(int i=0;i<8;i++)visual.Trail.SetPosition(i,Flight(Mathf.Clamp01(t-i*.012f)));
                }
                else
                {
                    if(impact)
                    {
                        visual.Particles.Place(new Vector3(target.x,.24f,target.z),Quaternion.identity,e.radius);
                        visual.Particles.Present((float)(renderTick-e.impact)/30f);
                    }
                }
                float remaining=Mathf.Clamp01((float)(e.end-renderTick)/15f);visual.Ring.widthMultiplier=.07f*remaining;
                var color=e.kind=="fireball"?new Color(1,.58f,.12f,.55f*remaining):new Color(.35f,.85f,1,.7f*remaining);
                visual.Ring.startColor=visual.Ring.endColor=color;
            }
            var removed=new List<long>();foreach(var pair in visuals)if(!seen.Contains(pair.Key))removed.Add(pair.Key);
            foreach(long id in removed){visuals[id].Particles.Dispose();UnityEngine.Object.Destroy(visuals[id].Root.gameObject);visuals.Remove(id);}
        }
        public void Burst(Vector3 position,float radius,long tick=0)
        {
            feedback.Play("Explosion",new Vector3(position.x,.20f,position.z),Vector3.zero,radius*.72f,tick>0?tick:clock,clock);
        }
        public void Impact(Vector3 position,bool heavy,long tick)
        {
            feedback.Play(heavy?"HeavyHit":"Hit",position,Vector3.zero,1,tick,clock);
        }
        public void Slash(Vector3 position,Vector3 direction,bool heavy,long tick)
        {
            feedback.Play("Slash",position+Vector3.up*.65f,direction,heavy?1.7f:1.2f,tick,clock);
        }
        public void Death(Vector3 position)=>feedback.Play("Death",position+Vector3.up*.3f,Vector3.zero,1,clock,clock);
        public void Collapse(Vector3 position,bool keep)=>feedback.Play("Collapse",position+Vector3.up*.8f,Vector3.zero,keep?1.5f:1.1f,clock,clock);
        private void ClearSpells()
        {
            foreach(var visual in visuals.Values){visual.Particles.Dispose();UnityEngine.Object.Destroy(visual.Root.gameObject);}
            visuals.Clear();
        }
        public void Reset(){ClearSpells();feedback.Reset();SeenSpells=0;clock=0;endedAt=-1;welcome=null;}
        public void Dispose(){Reset();feedback.Dispose();UnityEngine.Object.Destroy(root.gameObject);}
    }
}
