using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel
{
    public sealed class DuelDeploymentGrid : IDisposable
    {
        private readonly Transform root;
        private readonly DuelMap map;
        private readonly Mesh allowed,blocked,hover;
        private readonly Material allowedMaterial,blockedMaterial,hoverMaterial;
        private string geometryKey;
        public int AllowedCells {get;private set;}
        public bool Visible=>root.gameObject.activeSelf;

        public DuelDeploymentGrid(Transform parent,DuelMap map)
        {
            this.map=map;root=new GameObject("Deployment cells").transform;root.SetParent(parent,false);
            var template=Resources.Load<Material>("TowerDuel/Materials/Preview");
            allowedMaterial=Material(template,"Available cells",new Color(.48f,1,.77f,.32f));
            blockedMaterial=Material(template,"Blocked cells",new Color(1,.32f,.25f,.14f));
            hoverMaterial=Material(template,"Selected cell",new Color(1,.88f,.42f,.5f));
            allowed=Layer("Available",allowedMaterial);blocked=Layer("Blocked",blockedMaterial);hover=Layer("Selected",hoverMaterial);
            Hide();
        }
        private static Material Material(Material template,string name,Color color)
        {
            var material=new Material(template){name=name};material.SetColor("_BaseColor",color);return material;
        }
        private Mesh Layer(string name,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);
            var mesh=new Mesh{name=name+" deployment cells"};go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return mesh;
        }
        public void Show(DuelMessage state,CardDefinition card)
        {
            root.gameObject.SetActive(true);
            string key=state.match+":"+state.seat+":"+card.id;
            foreach(var tower in state.towers)if(tower.hp<=0)key+=":"+tower.id;
            if(key==geometryKey)return;geometryKey=key;
            var good=new List<Vector3>();var bad=new List<Vector3>();
            var goodIndices=new List<int>();var badIndices=new List<int>();AllowedCells=0;
            float cell=map.deploymentCellSize;
            for(float x=-map.width*.5f+cell*.5f;x<map.width*.5f;x+=cell)
                for(float z=-map.length*.5f+cell*.5f;z<map.length*.5f;z+=cell)
                {
                    var p=new Vector3(x,map.HasWall?.012f:.22f,z);bool legal=map.CanDeploy(state.seat,p,state,card);
                    if(legal){Border(good,goodIndices,p,cell);AllowedCells++;}
                    else Border(bad,badIndices,p,cell);
                }
            SetMesh(allowed,good,goodIndices);SetMesh(blocked,bad,badIndices);
        }
        public void Highlight(Vector3 point,bool visible)
        {
            hover.Clear();if(!visible)return;
            // Spells retain free aim; only troop centres are snapped by the map.
            point=map.SnapDeployment(point,null);point.y=map.HasWall?.016f:.235f;
            var vertices=new List<Vector3>();var indices=new List<int>();
            Quad(vertices,indices,point,map.deploymentCellSize*.48f,map.deploymentCellSize*.48f);
            SetMesh(hover,vertices,indices);
        }
        private static void Border(List<Vector3> vertices,List<int> indices,Vector3 p,float cell)
        {
            float half=cell*.48f,edge=cell*.015f;
            Quad(vertices,indices,p+Vector3.forward*(half-edge),half,edge);
            Quad(vertices,indices,p-Vector3.forward*(half-edge),half,edge);
            Quad(vertices,indices,p+Vector3.right*(half-edge),edge,half-edge*2);
            Quad(vertices,indices,p-Vector3.right*(half-edge),edge,half-edge*2);
        }
        private static void Quad(List<Vector3> vertices,List<int> indices,Vector3 p,float x,float z)
        {
            int n=vertices.Count;
            vertices.Add(p+new Vector3(-x,0,-z));vertices.Add(p+new Vector3(-x,0,z));
            vertices.Add(p+new Vector3(x,0,z));vertices.Add(p+new Vector3(x,0,-z));
            indices.Add(n);indices.Add(n+1);indices.Add(n+2);indices.Add(n);indices.Add(n+2);indices.Add(n+3);
        }
        private static void SetMesh(Mesh mesh,List<Vector3> vertices,List<int> indices)
        {mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();}
        public void Hide(){root.gameObject.SetActive(false);hover.Clear();}
        public void Dispose()
        {
            UnityEngine.Object.Destroy(root.gameObject);
            foreach(var mesh in new[]{allowed,blocked,hover})UnityEngine.Object.Destroy(mesh);
            foreach(var material in new[]{allowedMaterial,blockedMaterial,hoverMaterial})UnityEngine.Object.Destroy(material);
        }
    }
}
