using System;
using System.Collections.Generic;
using UnityEngine;

namespace Realtime.Examples.Presentation
{
    public sealed class SceneView:IDisposable
    {
        public static readonly Color Own=new Color(.12f,.48f,1),Remote=new Color(1,.42f,.12f),Marker=new Color(.8f,.4f,1),Target=new Color(.2f,1,.4f);
        private readonly Transform root;
        private sealed class Item {public GameObject Object;public Renderer Renderer;public LineRenderer Line;}
        private readonly Dictionary<string,Item> objects=new Dictionary<string,Item>();
        private readonly HashSet<string> seen=new HashSet<string>();
        private readonly List<string> removed=new List<string>();
        private readonly Material solid,line;
        private readonly MaterialPropertyBlock tint=new MaterialPropertyBlock();
        private readonly GameObject cameraObject,lightObject,floor;
        public SceneView(Transform root)
        {
            this.root=root;solid=new Material(Shader.Find("Standard"));line=new Material(Shader.Find("Sprites/Default"));
            cameraObject=new GameObject("Lesson camera",typeof(Camera));cameraObject.transform.SetParent(root,false);
            cameraObject.transform.position=new Vector3(0,20,-12);cameraObject.transform.LookAt(Vector3.zero);
            var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=10.8f;
            camera.rect=new Rect(.4f,0,.6f,1);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.13f);
            lightObject=new GameObject("Lesson light",typeof(Light));lightObject.transform.SetParent(root,false);
            lightObject.transform.rotation=Quaternion.Euler(50,-30,0);lightObject.GetComponent<Light>().type=LightType.Directional;
            RenderSettings.ambientLight=new Color(.5f,.5f,.6f);
            floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Floor";floor.transform.SetParent(root,false);
            floor.transform.localScale=new Vector3(1.8f,1,1.8f);floor.GetComponent<MeshRenderer>().sharedMaterial=solid;
            tint.SetColor("_Color",new Color(.2f,.24f,.3f));floor.GetComponent<MeshRenderer>().SetPropertyBlock(tint);
            // Concrete references retain CreatePrimitive's native components in
            // IL2CPP builds; a Collider-only reference strips these subclasses.
            UnityEngine.Object.Destroy(floor.GetComponent<MeshCollider>());
        }
        public void Begin()=>seen.Clear();
        public void Put(string key,float x,float z,Color color,float size=.8f)
        {
            seen.Add(key);
            if(!objects.TryGetValue(key,out var item)){
                var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=key;obj.transform.SetParent(root,false);
                item=new Item{Object=obj,Renderer=obj.GetComponent<MeshRenderer>()};item.Renderer.sharedMaterial=solid;
                UnityEngine.Object.Destroy(obj.GetComponent<BoxCollider>());objects.Add(key,item);
            }
            item.Object.transform.localPosition=new Vector3(x,size/2,z);item.Object.transform.localScale=Vector3.one*size;
            tint.SetColor("_Color",color);item.Renderer.SetPropertyBlock(tint);
        }
        public void Ring(string key,float x,float z,float radius,Color color)
        {
            seen.Add(key);
            if(!objects.TryGetValue(key,out var item)){
                var obj=new GameObject(key,typeof(LineRenderer));obj.transform.SetParent(root,false);
                var view=obj.GetComponent<LineRenderer>();item=new Item{Object=obj,Line=view};objects.Add(key,item);
                view.loop=true;view.useWorldSpace=false;view.positionCount=48;
                view.widthMultiplier=.04f;view.sharedMaterial=line;
            }
            item.Object.transform.localPosition=new Vector3(x,.055f,z);var renderer=item.Line;
            renderer.startColor=renderer.endColor=color;
            for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;renderer.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
        }
        public void Segment(string key,float ax,float az,float bx,float bz,Color color)
        {
            seen.Add(key);
            if(!objects.TryGetValue(key,out var item)){
                var obj=new GameObject(key,typeof(LineRenderer));obj.transform.SetParent(root,false);
                var view=obj.GetComponent<LineRenderer>();item=new Item{Object=obj,Line=view};objects.Add(key,item);
                view.useWorldSpace=false;view.positionCount=2;view.widthMultiplier=.05f;view.sharedMaterial=line;
            }
            var renderer=item.Line;renderer.startColor=renderer.endColor=color;
            renderer.SetPosition(0,new Vector3(ax,.1f,az));renderer.SetPosition(1,new Vector3(bx,.1f,bz));
        }
        public void End(){removed.Clear();foreach(var key in objects.Keys)if(!seen.Contains(key))removed.Add(key);foreach(var key in removed){UnityEngine.Object.Destroy(objects[key].Object);objects.Remove(key);}}
        public void Dispose(){foreach(var item in objects.Values)UnityEngine.Object.Destroy(item.Object);objects.Clear();UnityEngine.Object.Destroy(solid);UnityEngine.Object.Destroy(line);UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(lightObject);UnityEngine.Object.Destroy(floor);}
    }
}
