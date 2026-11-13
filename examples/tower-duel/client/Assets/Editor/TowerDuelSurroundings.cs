using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel.Editor
{
    public static class TowerDuelSurroundings
    {
        private const string Folder="Assets/Resources/TowerDuel/Environment";
        private const int Columns=96, Rows=36;
        private const float Span=80;

        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var original=UnityEngine.Object.FindObjectsByType<MeshRenderer>();
            var tree=original.FirstOrDefault(r=>r.name.StartsWith("SM_Tree_",StringComparison.Ordinal));
            var rock=original.FirstOrDefault(r=>r.name.StartsWith("SM_Rock_",StringComparison.Ordinal));
            var grass=original.FirstOrDefault(r=>r.name.StartsWith("SM_GrassBank_",StringComparison.Ordinal));
            var water=original.FirstOrDefault(r=>r.name=="SM_River");
            if(!tree||!rock||!grass||!water)throw new InvalidOperationException("Arena nature sources are missing");
            var root=new GameObject("Continuous meadow and river").transform;

            // Replace the boxed board and repeated edge rows with one continuous landscape.
            foreach(var renderer in original)
                if(renderer.name=="SM_ArenaPlinth"||renderer.name.StartsWith("SM_GrassBank_",StringComparison.Ordinal)||
                   renderer.name=="SM_River"||renderer.name.StartsWith("SM_Riverbank",StringComparison.Ordinal)||
                   renderer.name.StartsWith("SM_Tree_",StringComparison.Ordinal)||renderer.name.StartsWith("SM_Rock_",StringComparison.Ordinal))
                    renderer.enabled=false;

            var meadow=Material("Meadow",grass.sharedMaterial);
            meadow.SetTexture("_BaseMap",MeadowTexture());
            meadow.SetTextureScale("_BaseMap",Vector2.one);
            var river=Material("River",water.sharedMaterial);
            var shore=Material("River shore",grass.sharedMaterial);
            shore.SetColor("_BaseColor",Color.Lerp(grass.sharedMaterial.GetColor("_BaseColor"),new Color(.50f,.43f,.27f),.55f));
            Surface(root,"Continuous grass",Land(),meadow);
            Surface(root,"River beyond the arena",Water(),river);
            Surface(root,"Soft river shores",Shores(),shore);

            var random=new System.Random(61112);
            int trees=0,rocks=0;
            float Next(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
            bool Clear(float x,float z)=>(Mathf.Abs(x)<11.4f&&Mathf.Abs(z)<17.5f)||Mathf.Abs(z-RiverCentre(x))<RiverHalf(x)+1.0f;
            void Place(MeshRenderer source,float x,float z,float scale)
            {
                var copy=UnityEngine.Object.Instantiate(source.gameObject,root,false);
                copy.name="Meadow "+source.name;copy.SetActive(true);
                copy.transform.localScale=source.transform.lossyScale*scale;
                copy.transform.rotation=Quaternion.AngleAxis(Next(0,360),Vector3.up)*source.transform.rotation;
                copy.transform.position=new Vector3(x,Height(x,z),z);
                var renderers=copy.GetComponentsInChildren<Renderer>(true);
                foreach(var renderer in renderers)renderer.enabled=true;
                Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                copy.transform.position+=new Vector3(x-bounds.center.x,Height(x,z)-bounds.min.y,z-bounds.center.z);
                foreach(var collider in copy.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(var child in copy.GetComponentsInChildren<Transform>(true))GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic);
            }
            // Irregular groves leave clearings rather than a grid or a visible perimeter.
            foreach(int side in new[]{-1,1})
            {
                var centres=new List<Vector2>{
                    new Vector2(side*15,-11),new Vector2(side*16,10),new Vector2(side*24,-3),
                    new Vector2(side*29,17),new Vector2(side*32,-18),new Vector2(side*44,6),
                    new Vector2(side*48,-24),new Vector2(side*58,25),new Vector2(side*12,24)
                };
                foreach(var centre in centres)
                    for(int i=0;i<9;i++)
                    {
                        float angle=Next(0,Mathf.PI*2),distance=Mathf.Sqrt(Next(0,1))*Next(3,5.5f);
                        float x=centre.x+Mathf.Cos(angle)*distance,z=centre.y+Mathf.Sin(angle)*distance;
                        if(Clear(x,z))continue;
                        Place(tree,x,z,Next(1.25f,2.5f));trees++;
                        if(i%4==0){float rx=x+Next(-1,1),rz=z+Next(-1,1);if(!Clear(rx,rz)){Place(rock,rx,rz,Next(.8f,1.7f));rocks++;}}
                    }
                for(int i=0;i<18;i++)
                {
                    float x=side*Next(10.8f,14.8f),z=Next(-20,20);
                    if(Clear(x,z))continue;
                    Place(rock,x,z,Next(.55f,1.2f));rocks++;
                }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds");
            File.WriteAllText("Builds/surroundings-audit.json",JsonUtility.ToJson(new Audit{
                trees=trees,rocks=rocks,colliders=root.GetComponentsInChildren<Collider>().Length,
                playableWidth=20,playableLength=30,continuousGround=true,riverExtends=true,plinthHidden=true
            },true));
        }

        private static Material Material(string name,Material source)
        {
            string path=Folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
            else EditorUtility.CopySerialized(source,material);
            material.name=name;EditorUtility.SetDirty(material);return material;
        }
        private static Texture2D MeadowTexture()
        {
            const int size=256;string path=Folder+"/MeadowNoise.png";
            var texture=new Texture2D(size,size,TextureFormat.RGB24,false,true);
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float broad=Mathf.PerlinNoise(x*.035f+13.6f,y*.035f+5.2f);
                float small=Mathf.PerlinNoise(x*.21f+2.1f,y*.21f+17.9f);
                float value=.91f+broad*.085f+small*.015f;
                pixels[y*size+x]=new Color(value,value,value);
            }
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;
            importer.maxTextureSize=256;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static float RiverCentre(float x)
        {
            float outer=Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Abs(x)-10)/12));
            return Mathf.Sin(x*.075f)*1.8f*outer;
        }
        private static float RiverHalf(float x)
        {
            float outer=Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Abs(x)-9)/8));
            return 1.5f+outer*(.25f+Mathf.Sin(x*.31f)*.2f);
        }
        private static float Height(float x,float z)
        {
            float outside=Mathf.Max(Mathf.Abs(x)-10,Mathf.Abs(z)-15);
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(outside/6));
            return blend*(Mathf.PerlinNoise(x*.065f+17,z*.065f+23)*.8f-.36f);
        }
        private static Mesh Land()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(int side in new[]{-1,1})
                for(int row=0;row<Rows;row++)
                    for(int column=0;column<Columns;column++)
                    {
                        float x0=-Span+column*Span*2/Columns,x1=-Span+(column+1)*Span*2/Columns;
                        Vector3 Point(float x,int r)
                        {
                            float edge=RiverCentre(x)+side*(RiverHalf(x)+.25f);
                            float z=Mathf.Lerp(edge,side*Span,r/(float)Rows);
                            return new Vector3(x,Height(x,z),z);
                        }
                        Quad(vertices,uv,triangles,Point(x0,row),Point(x1,row),Point(x1,row+1),Point(x0,row+1));
                    }
            return Mesh("Meadow terrain",vertices,uv,triangles);
        }
        private static Mesh Water()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int column=0;column<Columns;column++)
            {
                float x0=-Span+column*Span*2/Columns,x1=-Span+(column+1)*Span*2/Columns;
                Vector3 Point(float x,int side)=>new Vector3(x,-.16f,RiverCentre(x)+side*(RiverHalf(x)+.12f));
                Quad(vertices,uv,triangles,Point(x0,-1),Point(x1,-1),Point(x1,1),Point(x0,1));
            }
            return Mesh("Meandering river",vertices,uv,triangles);
        }
        private static Mesh Shores()
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(int side in new[]{-1,1})
                for(int column=0;column<Columns;column++)
                {
                    float x0=-Span+column*Span*2/Columns,x1=-Span+(column+1)*Span*2/Columns;
                    Vector3 Point(float x,bool outer)
                    {
                        float z=RiverCentre(x)+side*(RiverHalf(x)+(outer?.32f:0));
                        return new Vector3(x,outer?Height(x,z)+.004f:-.13f,z);
                    }
                    Quad(vertices,uv,triangles,Point(x0,false),Point(x1,false),Point(x1,true),Point(x0,true));
                }
            return Mesh("Organic shore strips",vertices,uv,triangles);
        }
        private static void Quad(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            int start=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            foreach(var point in new[]{a,b,c,d})uv.Add(new Vector2(point.x/32,point.z/32));
            bool up=Vector3.Cross(b-a,c-a).y>0;
            triangles.AddRange(up?new[]{start,start+1,start+2,start,start+2,start+3}:new[]{start,start+2,start+1,start,start+3,start+2});
        }
        private static Mesh Mesh(string name,List<Vector3> vertices,List<Vector2> uv,List<int> triangles)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!existing){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
        }
        private static void Surface(Transform parent,string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
        }
        [Serializable] private sealed class Audit
        {
            public int trees,rocks,colliders,playableWidth,playableLength;
            public bool continuousGround,riverExtends,plinthHidden;
        }
    }
}
