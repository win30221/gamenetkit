using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel
{
    // Local visual intent only. These objects never enter the authoritative unit collection.
    public sealed class DuelDeploymentPreview : IDisposable
    {
        private const int Segments=64;
        private readonly Transform root;
        private readonly IReadOnlyDictionary<string,GameObject> prefabs;
        private readonly Dictionary<Material,Material> ghostMaterials=new Dictionary<Material,Material>();
        private readonly List<GameObject> ghosts=new List<GameObject>();
        private readonly LineRenderer[] rings=new LineRenderer[3];
        private readonly Material template,ringMaterial,areaMaterial;
        private readonly Mesh areaMesh;
        private readonly Transform area;
        private string kind;
        public int ActiveGhosts {get;private set;}
        public float ActiveRadius {get;private set;}
        public string ActiveKind {get;private set;}

        public DuelDeploymentPreview(Transform parent,IReadOnlyDictionary<string,GameObject> prefabs)
        {
            this.prefabs=prefabs;
            root=new GameObject("Deployment preview").transform;root.SetParent(parent,false);
            template=Resources.Load<Material>("TowerDuel/Materials/Preview");
            if(!template)throw new InvalidOperationException("Prepare the transparent deployment-preview material before building");
            ringMaterial=new Material(template){name="Preview outline"};
            areaMaterial=new Material(template){name="Preview spell area"};
            for(int n=0;n<rings.Length;n++)
            {
                var line=new GameObject("Footprint "+n).AddComponent<LineRenderer>();line.transform.SetParent(root,false);
                line.sharedMaterial=ringMaterial;line.loop=true;line.useWorldSpace=true;line.positionCount=Segments;
                line.widthMultiplier=.055f;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;rings[n]=line;
            }
            area=new GameObject("Actual spell radius").transform;area.SetParent(root,false);
            areaMesh=new Mesh{name="Unit-radius spell preview"};
            var vertices=new Vector3[Segments+1];var indices=new int[Segments*3];
            for(int i=0;i<Segments;i++)
            {
                float angle=i*Mathf.PI*2/Segments;vertices[i+1]=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                indices[i*3]=0;indices[i*3+1]=(i+1)%Segments+1;indices[i*3+2]=i+1;
            }
            areaMesh.vertices=vertices;areaMesh.triangles=indices;areaMesh.RecalculateBounds();
            area.gameObject.AddComponent<MeshFilter>().sharedMesh=areaMesh;
            var renderer=area.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=areaMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            Hide();
        }
        private void CreateGhosts(string card,int count)
        {
            foreach(var ghost in ghosts){ghost.SetActive(false);UnityEngine.Object.Destroy(ghost);}ghosts.Clear();kind=card;
            for(int i=0;i<count;i++)
            {
                var ghost=UnityEngine.Object.Instantiate(prefabs[card],root,false);ghost.name=card+" translucent placement "+i;
                var art=ghost.GetComponent<DuelUnitArt>();art.TeamMarker.gameObject.SetActive(false);
                if(art.Sprites)art.Sprites.PresentPreview(0,true);
                else{art.Animator.Rebind();art.Animator.Update(0);art.Animator.enabled=false;}
                foreach(var collider in ghost.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                foreach(var renderer in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    if(renderer==art.TeamMarker)continue;
                    var materials=renderer.sharedMaterials;
                    for(int slot=0;slot<materials.Length;slot++)
                    {
                        var source=materials[slot];if(!source)continue;
                        if(!ghostMaterials.TryGetValue(source,out var material))
                        {
                            material=new Material(renderer is SpriteRenderer?source:template){name=source.name+" placement ghost"};
                            if(renderer is SpriteRenderer){material.SetFloat("_ZWrite",0);material.renderQueue=(int)RenderQueue.Transparent;}
                            if(source.HasProperty("_BaseMap")){material.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));material.SetTextureScale("_BaseMap",source.GetTextureScale("_BaseMap"));material.SetTextureOffset("_BaseMap",source.GetTextureOffset("_BaseMap"));}
                            ghostMaterials.Add(source,material);
                        }
                        materials[slot]=material;
                    }
                    renderer.sharedMaterials=materials;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                }
                ghosts.Add(ghost);
            }
        }
        public void Show(string card,int seat,Vector3 point,bool visible,bool valid,Vector3[] formation,float radius,bool spell)
        {
            if(!visible){Hide();return;}
            ActiveKind=card;ActiveRadius=spell?radius:0;
            if(!spell&&(kind!=card||ghosts.Count!=formation.Length))CreateGhosts(card,formation.Length);
            Color color=!valid?new Color(1,.27f,.22f,1):spell?(card=="fireball"?new Color(1,.55f,.12f,1):new Color(.3f,.86f,1,1)):new Color(.5f,1,.78f,1);
            ringMaterial.SetColor("_BaseColor",color);
            foreach(var material in ghostMaterials.Values)material.SetColor("_BaseColor",valid?new Color(.78f,1,1,.42f):new Color(1,.36f,.3f,.42f));
            ActiveGhosts=spell?0:formation.Length;
            for(int i=0;i<ghosts.Count;i++)
            {
                var ghost=ghosts[i];ghost.SetActive(!spell&&i<formation.Length);
                if(!ghost.activeSelf)continue;ghost.transform.position=formation[i];ghost.transform.rotation=Quaternion.Euler(0,seat==0?0:180,0);
                var art=ghost.GetComponent<DuelUnitArt>();if(art.Sprites)art.Sprites.PresentPreview(seat,valid);
            }
            area.gameObject.SetActive(spell);
            if(spell){area.position=new Vector3(point.x,.24f,point.z);area.localScale=new Vector3(radius,1,radius);color.a=.17f;areaMaterial.SetColor("_BaseColor",color);}
            for(int n=0;n<rings.Length;n++)
            {
                var line=rings[n];line.enabled=n<(spell?1:formation.Length);if(!line.enabled)continue;
                var centre=spell?point:formation[n];float r=spell?radius:.37f;
                for(int i=0;i<Segments;i++){float angle=i*Mathf.PI*2/Segments;line.SetPosition(i,new Vector3(centre.x+Mathf.Cos(angle)*r,.25f,centre.z+Mathf.Sin(angle)*r));}
            }
        }
        public void Hide(){ActiveGhosts=0;ActiveRadius=0;ActiveKind=null;foreach(var ghost in ghosts)ghost.SetActive(false);foreach(var ring in rings)ring.enabled=false;area.gameObject.SetActive(false);}
        public void Dispose(){UnityEngine.Object.Destroy(root.gameObject);UnityEngine.Object.Destroy(areaMesh);UnityEngine.Object.Destroy(ringMaterial);UnityEngine.Object.Destroy(areaMaterial);foreach(var material in ghostMaterials.Values)UnityEngine.Object.Destroy(material);}
    }
}
