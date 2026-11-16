using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDuel
{
    // Only the small, feathered contact under the boots. The animated directional
    // silhouette is a separate ground projection, rather than an opaque disc.
    public sealed class DuelContactShadow : MonoBehaviour
    {
        private static Mesh footprint;
        private Renderer shadowRenderer;
        private MaterialPropertyBlock properties;
        public Renderer Renderer => shadowRenderer;
        public float Opacity { get; private set; } = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            if (footprint) Destroy(footprint);
            footprint = null;
        }
        public static DuelContactShadow Attach(DuelUnitArt art)
        {
            if (art.TeamMarker) art.TeamMarker.enabled = false;
            var existing = art.GetComponentInChildren<DuelContactShadow>();
            if (existing) return existing;
            var material = Resources.Load<Material>("TowerDuel/B3/ContactShadow");
            if (!material) throw new InvalidOperationException("Prepare the foot contact shadow material before building");
            if (!footprint)
            {
                footprint = new Mesh { name = "Soft foot contact", hideFlags = HideFlags.HideAndDontSave };
                footprint.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f) };
                footprint.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                footprint.triangles = new[] { 0,2,1,2,3,1 }; footprint.RecalculateBounds();
            }
            var go = new GameObject("Foot contact shadow"); go.transform.SetParent(art.transform, false);
            go.transform.localPosition = new Vector3(0, .003f, 0);
            float size=art.Sprites?art.Sprites.Set.WorldHeight/1.05f:1;
            go.transform.localScale = new Vector3(.58f*size, 1, .38f*size);
            go.AddComponent<MeshFilter>().sharedMesh = footprint;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            var contact = go.AddComponent<DuelContactShadow>(); contact.shadowRenderer = renderer;
            contact.properties = new MaterialPropertyBlock(); contact.SetOpacity(1);
            if(art.Sprites&&art.Sprites.Set.Baked)renderer.enabled=false;
            return contact;
        }
        public void SetOpacity(float value)
        {
            Opacity = Mathf.Clamp01(value);
            properties.SetFloat("_Opacity", Opacity); shadowRenderer.SetPropertyBlock(properties);
        }
    }
}
