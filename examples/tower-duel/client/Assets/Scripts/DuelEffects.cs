using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDuel
{
    // Original low-poly spell visuals. Damage, radii and lifetime come from server ticks.
    public sealed class DuelEffects : IDisposable
    {
        private sealed class Visual { public Transform Root,Symbol; public LineRenderer Ring,Trail; public bool Impacted; }
        private readonly Transform root;
        private readonly Dictionary<long,Visual> visuals=new Dictionary<long,Visual>();
        private readonly Action<string> sound;
        private readonly List<(Transform root,double until)> bursts=new List<(Transform,double)>();
        public int SeenSpells {get;private set;}
        public DuelEffects(Transform parent,Action<string> sound=null){root=new GameObject("Spell effects").transform;root.SetParent(parent,false);this.sound=sound;}
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
            var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(parent,false);line.sharedMaterial=Material(material);line.useWorldSpace=true;line.widthMultiplier=width;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;return line;
        }
        public void Update(DuelSession session)
        {
            var state=session.State;if(state==null)return;
            for(int i=bursts.Count-1;i>=0;i--){var burst=bursts[i];float remaining=(float)(burst.until-DuelSession.Now);if(remaining<=0){UnityEngine.Object.Destroy(burst.root.gameObject);bursts.RemoveAt(i);}else burst.root.localScale=Vector3.one*(1-remaining/.35f);}
            double renderTick=session.RenderTick;
            var seen=new HashSet<long>();
            foreach(var e in session.SampleEffects())
            {
                seen.Add(e.id);
                if(!visuals.TryGetValue(e.id,out var visual))
                {
                    var owner=new GameObject(e.kind+" "+e.id).transform;owner.SetParent(root,false);
                    visual=new Visual{Root=owner,Symbol=Symbol(e.kind,owner).transform,Ring=Line(owner,"Effect radius",e.kind=="fireball"?"Fire":"Ice",.07f),Trail=Line(owner,"Fire trail","Gold",.14f)};
                    visual.Ring.loop=true;visual.Ring.positionCount=32;visual.Trail.positionCount=8;visuals.Add(e.id,visual);SeenSpells|=e.kind=="fireball"?1:2;
                }
                var target=new Vector3(e.x,.3f,e.z);
                for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;visual.Ring.SetPosition(i,target+new Vector3(Mathf.Cos(a)*e.radius,0,Mathf.Sin(a)*e.radius));}
                bool impact=renderTick>=e.impact;
                if(impact&&!visual.Impacted){visual.Impacted=true;sound?.Invoke(e.kind);}
                visual.Trail.enabled=e.kind=="fireball"&&!impact;
                if(e.kind=="fireball")
                {
                    float t=Mathf.Clamp01((float)(renderTick-e.start)/Mathf.Max(1,e.impact-e.start));
                    Vector3 from=new Vector3(0,5,e.seat==0?-11:11);
                    Vector3 Flight(float progress)=>Vector3.Lerp(from,target,progress)+Vector3.up*Mathf.Sin(progress*Mathf.PI)*3;
                    visual.Symbol.position=Flight(t);visual.Symbol.localScale=Vector3.one*(impact?Mathf.Lerp(.5f,e.radius,(float)(renderTick-e.impact)/Mathf.Max(1,e.end-e.impact)):.6f);
                    for(int i=0;i<8;i++)visual.Trail.SetPosition(i,Flight(Mathf.Clamp01(t-i*.025f)));
                }
                else
                {
                    visual.Symbol.position=target;visual.Symbol.localScale=Vector3.one*(e.radius*.7f);visual.Symbol.Rotate(Vector3.up,16*Time.unscaledDeltaTime,Space.World);
                }
                float remaining=Mathf.Clamp01((float)(e.end-renderTick)/15f);visual.Ring.widthMultiplier=.07f*remaining;
                if(impact&&e.kind=="fireball")visual.Symbol.localScale*=remaining;
            }
            var removed=new List<long>();foreach(var pair in visuals)if(!seen.Contains(pair.Key))removed.Add(pair.Key);
            foreach(long id in removed){UnityEngine.Object.Destroy(visuals[id].Root.gameObject);visuals.Remove(id);}
        }
        public void Burst(Vector3 position,float radius)
        {
            if(bursts.Count>=24)return;
            var burst=Symbol("fireball",root).transform;burst.position=position;burst.localScale=Vector3.zero;
            foreach(Transform child in burst)child.localPosition*=radius;
            bursts.Add((burst,DuelSession.Now+.35));
        }
        public void Reset(){foreach(var visual in visuals.Values)UnityEngine.Object.Destroy(visual.Root.gameObject);visuals.Clear();foreach(var burst in bursts)UnityEngine.Object.Destroy(burst.root.gameObject);bursts.Clear();SeenSpells=0;}
        public void Dispose(){Reset();UnityEngine.Object.Destroy(root.gameObject);}
    }
}
