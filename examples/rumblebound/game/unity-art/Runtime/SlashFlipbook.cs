using Rumblebound.Gameplay;
using UnityEngine;

namespace Rumblebound.Presentation
{
    // Presentation is advanced by CombatEffects' clock, including native previews.
    // Each instance owns a property block; the imported material stays shared.
    public sealed class SlashFlipbook : MonoBehaviour
    {
        [SerializeField] private MeshRenderer surface;
        [SerializeField] private int columns = 4, rows = 4, frameCount = 10;
        private MaterialPropertyBlock properties;
        private static readonly int TextureST = Shader.PropertyToID("_MainTex_ST");
        private static readonly int ArcCos = Shader.PropertyToID("_ArcCos");
        public int CurrentFrame { get; private set; }
        public int FrameCount => frameCount;
        public MeshRenderer Surface => surface;

        private void Awake() { EnsureProperties(); }

        // Used by the deterministic editor asset pipeline, never by gameplay.
        public void Configure(MeshRenderer renderer, int atlasColumns, int atlasRows, int frames)
        {
            surface = renderer;
            columns = Mathf.Max(1, atlasColumns);
            rows = Mathf.Max(1, atlasRows);
            frameCount = Mathf.Clamp(frames, 1, columns * rows);
            EnsureProperties();
        }

        public void Present(float normalizedAge, float arcDegrees)
        {
            if (surface == null || float.IsNaN(normalizedAge) || float.IsInfinity(normalizedAge) ||
                float.IsNaN(arcDegrees) || float.IsInfinity(arcDegrees) || arcDegrees <= 0 || arcDegrees > 180) return;
            EnsureProperties();
            CurrentFrame = Mathf.Min(frameCount - 1, Mathf.FloorToInt(Mathf.Clamp01(normalizedAge) * frameCount));
            int x = CurrentFrame % columns, y = CurrentFrame / columns;
            properties.SetVector(TextureST, new Vector4(1f / columns, 1f / rows, (float)x / columns, 1f - (float)(y + 1) / rows));
            properties.SetFloat(ArcCos, Mathf.Cos(arcDegrees * .5f * Mathf.Deg2Rad));
            surface.SetPropertyBlock(properties);
        }

        private void EnsureProperties()
        {
            if (properties == null) properties = new MaterialPropertyBlock();
        }
    }
}
