using Rumblebound.Gameplay;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rumblebound.Presentation
{
    // Shared visual environment. Gameplay and offline previews each own one
    // stage and dispose their own cameras, decorations and material instances.
    public sealed class ArenaStage : IDisposable
    {
        public Transform Root { get; private set; }
        public Camera Camera { get; private set; }
        public Camera Backdrop { get; private set; }
        private Material arenaBaseMaterial, arenaFloorMaterial, arenaAlternateFloorMaterial;

        public ArenaStage(Transform owner, ArenaMapDefinition map = null)
        {
            Root = new GameObject("Arena stage").transform;
            Root.SetParent(owner, false);
            try
            {
                CreateArenaArt((float)(map?.arenaHalfSize ?? 10));
                var light = new GameObject("Key light").AddComponent<Light>();
                light.transform.SetParent(Root, false);
                light.type = LightType.Directional; light.intensity = .85f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(55, -35, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.42f, .44f, .5f);
                Camera = new GameObject("Arena camera").AddComponent<Camera>();
                Camera.transform.SetParent(Root, false);
                Camera.gameObject.AddComponent<AudioListener>();
                Camera.transform.position = new Vector3(0, 23, -18);
                Camera.transform.LookAt(Vector3.zero);
                Camera.orthographic = true; Camera.orthographicSize = 14.5f;
                Camera.backgroundColor = new Color(.035f, .055f, .08f);
                Camera.clearFlags = CameraClearFlags.SolidColor;
                URPCameras.Configure(Camera, true);
                // Always clear letterboxes around the square gameplay viewport.
                Backdrop = new GameObject("Arena backdrop").AddComponent<Camera>();
                Backdrop.transform.SetParent(Root, false);
                Backdrop.depth = -10; Backdrop.cullingMask = 0;
                Backdrop.clearFlags = CameraClearFlags.SolidColor;
                Backdrop.backgroundColor = Camera.backgroundColor;
                URPCameras.Configure(Backdrop, false);
            }
            catch { Dispose(); throw; }
        }

        public static Material CreateMaterial(Color color)
        {
            var source = Resources.Load<Material>("G0Surface");
            var material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            return material;
        }

        public static GameObject PlaceArt(Transform parent, string path, Vector3 position, float yaw = 0, float width = 0)
        {
            var prefab = Resources.Load<GameObject>("Kenney/" + path);
            if (prefab == null) throw new InvalidOperationException("Missing arena art: " + path);
            var item = Object.Instantiate(prefab, position, Quaternion.Euler(0, yaw, 0), parent);
            item.name = path;
            if (width > 0)
            {
                var bounds = new Bounds();
                bool first = true;
                foreach (var renderer in item.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                // Measure before applying yaw: a wall's visible width may lie
                // along Z after rotation, so use its longest horizontal axis.
                item.transform.localScale *= width / Mathf.Max(.001f, bounds.size.x, bounds.size.z);
            }
            return item;
        }

        private void CreateArenaArt(float half)
        {
            var scenery = new GameObject("Kenney Mini arena art").transform;
            scenery.SetParent(Root, false);
            var floor = Object.Instantiate(Resources.Load<GameObject>("G0Floor"), scenery);
            floor.name = "Arena foundation";
            floor.transform.localScale = new Vector3(half * 2 + .6f, .5f, half * 2 + .6f);
            floor.transform.position = new Vector3(0, -.29f, 0);
            arenaBaseMaterial = CreateMaterial(new Color(.37f, .28f, .27f));
            floor.GetComponent<Renderer>().sharedMaterial = arenaBaseMaterial;

            var palette = Resources.Load<Material>("Kenney/ArenaPalette");
            arenaFloorMaterial = new Material(palette) { color = new Color(.85f, .88f, .92f) };
            arenaAlternateFloorMaterial = new Material(palette) { color = new Color(.81f, .84f, .88f) };

            float tile = half > 10 ? 5f : 2.5f;
            int tiles = Mathf.CeilToInt(half * 2 / tile), edges = Mathf.CeilToInt(half / tile);
            for (int x = 0; x < tiles; x++)
                for (int z = 0; z < tiles; z++)
                {
                    var slab = PlaceArt(scenery, "Arena/floor", new Vector3(-half + tile / 2 + x * tile, 0, -half + tile / 2 + z * tile), 0, tile);
                    slab.GetComponentInChildren<Renderer>().sharedMaterial = (x + z) % 2 == 0 ? arenaFloorMaterial : arenaAlternateFloorMaterial;
                }

            for (int i = -edges; i <= edges; i++)
            {
                PlaceArt(scenery, i == 0 ? "Arena/wall-gate" : "Arena/wall", new Vector3(i * tile, 0, (half + 1.5f)), 0, tile);
                // Low front edging preserves sight of players near the camera.
                PlaceArt(scenery, "Arena/border-straight", new Vector3(i * tile, 0, -(half + 1.5f)), 180, tile);
                if (Math.Abs(i) < edges)
                {
                    PlaceArt(scenery, "Arena/wall", new Vector3(-(half + 1.5f), 0, i * tile), 90, tile);
                    PlaceArt(scenery, "Arena/wall", new Vector3((half + 1.5f), 0, i * tile), -90, tile);
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                PlaceArt(scenery, "Arena/column", new Vector3(side * (half + 1.5f), 0, (half + .5f)));
                PlaceArt(scenery, "Arena/column-damaged", new Vector3(side * (half + 1.5f), 0, -(half + .5f)));
                PlaceArt(scenery, "Arena/tree", new Vector3(side * (half + 3.4f), -.05f, 8.8f));
                PlaceArt(scenery, "Arena/statue", new Vector3(side * 7.5f, 0, (half + 3.4f)), 180);
                PlaceArt(scenery, "Arena/banner", new Vector3(side * 4f, 0, (half + 3.1f)), 180);
                PlaceArt(scenery, "Dungeon/barrel", new Vector3(side * (half + 2.9f), 0, -6.2f));
                PlaceArt(scenery, "Dungeon/chest", new Vector3(side * (half + 2.8f), 0, -4.7f), side * 70);
                PlaceArt(scenery, "Arena/bricks", new Vector3(side * (half + 2.8f), 0, -8.4f), side * 25);
            }
            // All placed objects are static, unanimated decorations with shared
            // palette materials. Combine them once rather than per frame.
            StaticBatchingUtility.Combine(scenery.gameObject);
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            if (arenaBaseMaterial != null) Object.Destroy(arenaBaseMaterial);
            if (arenaFloorMaterial != null) Object.Destroy(arenaFloorMaterial);
            if (arenaAlternateFloorMaterial != null) Object.Destroy(arenaAlternateFloorMaterial);
            Root = null; Camera = null; Backdrop = null;
            arenaBaseMaterial = arenaFloorMaterial = arenaAlternateFloorMaterial = null;
        }
    }
}