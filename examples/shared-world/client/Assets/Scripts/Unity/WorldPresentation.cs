using System.Collections.Generic;
using UnityEngine;

namespace Realtime.Examples.SharedWorld
{
    // Owns scene objects only; positions always come from the replica.
    public sealed class WorldPresentation
    {
        private readonly Transform root;
        private readonly Dictionary<long, GameObject> cubes = new Dictionary<long, GameObject>();
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

        public void Render(ReplicaState state)
        {
            removed.Clear();
            foreach (var item in cubes) if (!state.Players.ContainsKey(item.Key)) removed.Add(item.Key);
            foreach (long id in removed) { Object.Destroy(cubes[id]); cubes.Remove(id); }
            foreach (var item in state.Players)
            {
                if (!cubes.TryGetValue(item.Key, out var cube))
                {
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Player " + item.Key;
                    cube.transform.SetParent(root, false);
                    cube.GetComponent<Renderer>().sharedMaterial = item.Key == state.LocalPlayerID ? ownMaterial : remoteMaterial;
                    cubes.Add(item.Key, cube);
                }
                cube.transform.localPosition = new Vector3(item.Value.x, 0.5f, item.Value.z);
            }
        }

        public void Dispose()
        {
            foreach (var cube in cubes.Values) Object.Destroy(cube);
            cubes.Clear(); Object.Destroy(ownMaterial); Object.Destroy(remoteMaterial);
        }
    }
}
