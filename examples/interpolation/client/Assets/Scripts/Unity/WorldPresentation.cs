using System.Collections.Generic;
using UnityEngine;

namespace Realtime.Examples.InterpolationLesson
{
    // Owns scene objects only; positions always come from the replica.
    public sealed class WorldPresentation
    {
        private readonly Transform root;
        private readonly Dictionary<long, GameObject> cubes = new Dictionary<long, GameObject>();
        private readonly Dictionary<long, GameObject> outlines = new Dictionary<long, GameObject>();
        private readonly Material ownMaterial;
        private readonly Material remoteMaterial;
        private readonly List<long> removed = new List<long>();

        public WorldPresentation(Transform root)
        {
            this.root = root;
            ownMaterial = MakeMaterial(new Color(0.12f, 0.48f, 1f));
            remoteMaterial = MakeMaterial(new Color(1f, 0.42f, 0.12f));
        }

        public static Material MakeMaterial(Color color)
        { var material = new Material(Shader.Find("Standard")); material.color = color; return material; }

        public void Render(ReplicaState state, InterpolatedWorld view, bool compareSelf, bool showLatest)
        {
            removed.Clear();
            foreach (var item in cubes) if (!state.Players.ContainsKey(item.Key)) removed.Add(item.Key);
            foreach (long id in removed) { Object.Destroy(cubes[id]); Object.Destroy(outlines[id]); cubes.Remove(id); outlines.Remove(id); }
            foreach (var item in state.Players)
            {
                if (!cubes.TryGetValue(item.Key, out var cube))
                {
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Player " + item.Key;
                    cube.transform.SetParent(root, false);
                    cube.GetComponent<Renderer>().sharedMaterial = item.Key == state.LocalPlayerID ? ownMaterial : remoteMaterial;
                    cubes.Add(item.Key, cube);
                    var outline=new GameObject("Latest " + item.Key,typeof(LineRenderer));
                    outline.transform.SetParent(root,false);
                    var line=outline.GetComponent<LineRenderer>();
                    line.useWorldSpace=false;line.loop=true;line.positionCount=4;line.widthMultiplier=.045f;
                    line.sharedMaterial=item.Key==state.LocalPlayerID?ownMaterial:remoteMaterial;
                    line.SetPositions(new[]{new Vector3(-.6f,.04f,-.6f),new Vector3(.6f,.04f,-.6f),new Vector3(.6f,.04f,.6f),new Vector3(-.6f,.04f,.6f)});
                    outlines.Add(item.Key,outline);
                }
                float x=item.Value.x,z=item.Value.z;
                bool latest=view.Latest(item.Key,out float latestX,out float latestZ);
                if(latest){x=latestX;z=latestZ;}
                if((compareSelf||item.Key!=state.LocalPlayerID) && view.Sample(item.Key,out float sampledX,out float sampledZ)) {x=sampledX;z=sampledZ;}
                cube.transform.localPosition = new Vector3(x, 0.5f, z);
                outlines[item.Key].SetActive(showLatest && latest);
                outlines[item.Key].transform.localPosition=new Vector3(latestX,0,latestZ);
            }
        }

        public void Dispose()
        {
            foreach (var cube in cubes.Values) Object.Destroy(cube);
            foreach (var outline in outlines.Values) Object.Destroy(outline);
            cubes.Clear(); outlines.Clear(); Object.Destroy(ownMaterial); Object.Destroy(remoteMaterial);
        }
    }
}
