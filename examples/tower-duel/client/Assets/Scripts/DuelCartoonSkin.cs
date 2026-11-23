using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    /// <summary>
    /// Runtime-generated, nine-slice-safe UI artwork. UI graphics remain separate from
    /// B3 battlefield sprites and do not affect deployment, targeting or card rules.
    /// </summary>
    internal static class DuelCartoonSkin
    {
        private enum Surface
        {
            Wallpaper, Parchment, Scroll, Button, GreenButton, BlueButton,
            DarkPanel, CardFrame, CardBackdrop, CoinBadge
        }

        private static readonly Dictionary<Surface, Texture2D> Textures =
            new Dictionary<Surface, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache()
        {
            // With Enter Play Mode options, the Editor can preserve managed statics
            // while destroying the native Texture2D. Rebuild lazily on the next view.
            Textures.Clear();
        }

        public static void Apply(VisualElement root)
        {
            Paint(root, "menu-overlay", Surface.Wallpaper);
            Paint(root, "menu-shell", Surface.Parchment);
            Paint(root, "menu-title", Surface.Scroll);
            Paint(root, "menu-panel", Surface.Parchment);
            Paint(root, "menu-button", Surface.Button);
            Paint(root, "menu-primary", Surface.GreenButton);
            Paint(root, "menu-join", Surface.BlueButton);
            Paint(root, "menu-deck-count", Surface.BlueButton);

            Paint(root, "top-hud", Surface.DarkPanel);
            Paint(root, "dock", Surface.DarkPanel);
            Paint(root, "unit-card", Surface.CardFrame);
            Paint(root, "drag-card", Surface.CardFrame);
            Paint(root, "b3-card-frame", Surface.CardFrame);
            Paint(root, "card-cost", Surface.CoinBadge);
            Paint(root, "menu-card-cost", Surface.CoinBadge);
            Paint(root, "menu-card", Surface.CardFrame);
            Paint(root, "lobby-panel", Surface.Parchment);
            Paint(root, "small-button", Surface.Button);
            Paint(root, "primary-button", Surface.GreenButton);
            Paint(root, "secondary-button", Surface.Button);
        }

        // Called once per card ID by DuelSpriteLibrary's existing texture cache.
        // The resulting texture is fully opaque, so it works in every UI location.
        public static Texture2D CardArtWithBackdrop(Texture2D source)
        {
            if (!source) return null;
            try
            {
                return ComposeCardArt(source);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Card background compose failed for " +
                    source.name + ": " + error.Message);
                return source;
            }
        }

        private static Texture2D ComposeCardArt(Texture2D source)
        {
            Color32[] foreground = ReadCardPixels(source);
            bool transparent = false;
            for (int i = 0; i < foreground.Length; i++)
                if (foreground[i].a < 255) { transparent = true; break; }
            if (!transparent) return source; // Keep pre-illustrated cards unchanged.

            int width = source.width, height = source.height;
            Color32[] result = new Color32[foreground.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 hero = foreground[index];
                    if (hero.a == 255) { result[index] = hero; continue; }
                    Color32 bg = Pixel(Surface.CardBackdrop, x, y, width, height);
                    if (hero.a == 0) { result[index] = bg; continue; }
                    int a = hero.a, b = 255 - a;
                    result[index] = new Color32(
                        (byte)((hero.r * a + bg.r * b + 127) / 255),
                        (byte)((hero.g * a + bg.g * b + 127) / 255),
                        (byte)((hero.b * a + bg.b * b + 127) / 255),
                        255);
                }
            }

            var composed = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = source.name + "_WithBackdrop",
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            composed.SetPixels32(result);
            composed.Apply(false, true);
            return composed;
        }

        private static Color32[] ReadCardPixels(Texture2D source)
        {
            if (source.isReadable) return source.GetPixels32();

            // B3 cards use Read/Write-disabled imports; copy once through the GPU
            // instead of enabling permanent extra CPU-side texture memory.
            var target = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture oldTarget = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readback = new Texture2D(source.width, source.height,
                    TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height),
                    0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = oldTarget;
                RenderTexture.ReleaseTemporary(target);
                if (readback)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(readback);
                    else UnityEngine.Object.DestroyImmediate(readback);
                }
            }
        }

        public static void SetMenuTab(Button tab, bool selected)
        {
            if (tab != null)
                tab.style.backgroundImage = Get(selected ? Surface.GreenButton : Surface.Button);
        }

        private static void Paint(VisualElement root, string className, Surface surface)
        {
            Texture2D texture = Get(surface);
            root.Query<VisualElement>(className: className)
                .ForEach(element => element.style.backgroundImage = texture);
        }

        private static Texture2D Get(Surface surface)
        {
            if (Textures.TryGetValue(surface, out var cached) && cached) return cached;
            int width = surface == Surface.CoinBadge ? 96 : 192;
            int height = surface == Surface.Wallpaper || surface == Surface.CardBackdrop ? 256 :
                surface == Surface.CoinBadge ? 96 : 128;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = Pixel(surface, x, y, width, height);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TowerDuel/CartoonUI/" + surface,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Textures[surface] = texture;
            return texture;
        }

        private static Color32 Pixel(Surface surface, int x, int y, int width, int height)
        {
            if (surface == Surface.Wallpaper)
            {
                var blue = Color.Lerp(new Color32(20, 31, 57, 255),
                    new Color32(52, 69, 102, 255), y / (float)(height - 1));
                // Subtle diagonal motifs: visible on wide screens outside the lobby shell.
                if (((x + y / 2) % 74) < 2)
                    blue = Color.Lerp(blue, new Color32(97, 114, 146, 255), .13f);
                if (x % 73 == 13 && y % 61 == 27)
                    blue = Color.Lerp(blue, Color.white, .16f);
                return blue;
            }

            if (surface == Surface.CardBackdrop)
            {
                // A small fantasy landscape: glowing sky, distant hills and stone ground.
                // It sits *behind* the existing character PNG, never replacing the art.
                float u = x / (float)(width - 1), v = y / (float)(height - 1);
                var sky = Color.Lerp(new Color32(49, 105, 145, 255),
                    new Color32(158, 213, 225, 255), v);
                float dx = (u - .5f) * 1.2f, dy = v - .68f;
                float halo = Mathf.Clamp01(1f - (dx * dx + dy * dy) * 4f);
                sky = Color.Lerp(sky, new Color32(255, 228, 153, 255), halo * .35f);
                float ridge = .30f + .035f * Mathf.Sin(u * 10.5f)
                    + .018f * Mathf.Sin(u * 24f);
                if (v < ridge)
                {
                    sky = Color.Lerp(new Color32(42, 78, 89, 255),
                        new Color32(95, 151, 136, 255), Mathf.Clamp01(v / ridge));
                    // A quieter foreground gives the hero's silhouette clear contrast.
                    if (v < .13f)
                        sky = Color.Lerp(new Color32(94, 83, 77, 255),
                            new Color32(117, 115, 95, 255), v / .13f);
                }
                // Restrained, symmetrical fantasy crest glints at the top corners.
                float ornament = Mathf.Abs(u - .5f);
                if (v > .88f && ornament > .34f && ornament < .38f)
                    sky = Color.Lerp(sky, new Color32(255, 239, 176, 255), .40f);
                return sky;
            }

            if (surface == Surface.CoinBadge)
            {
                float dx = x - (width - 1) * .5f, dy = y - (height - 1) * .5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                if (radius > 47) return Color.clear;
                if (radius > 42) return new Color32(16, 16, 25, 255);
                if (radius > 37) return new Color32(245, 177, 61, 255);
                if (radius > 34) return new Color32(255, 236, 142, 255);
                var shade = Color.Lerp(new Color32(113, 27, 164, 255),
                    new Color32(238, 115, 253, 255), y / (float)height);
                if (dx < -12 && dy > 12) shade = Color.Lerp(shade, Color.white, .37f);
                return shade;
            }

            int r = surface == Surface.Scroll ? 23 : 20;
            if (!InsideRounded(x, y, width, height, 2, r)) return Color.clear;
            if (!InsideRounded(x, y, width, height, 8, r - 3))
                return new Color32(16, 18, 27, 255);
            if (!InsideRounded(x, y, width, height, 13, r - 6))
                return y > height / 2
                    ? new Color32(181, 101, 42, 255)
                    : new Color32(100, 48, 31, 255);
            if (!InsideRounded(x, y, width, height, 17, r - 8))
                return y > height / 2
                    ? new Color32(255, 200, 99, 255)
                    : new Color32(205, 123, 52, 255);

            Color32 bottom, top;
            switch (surface)
            {
                case Surface.GreenButton:
                    bottom = new Color32(53, 145, 29, 255);
                    top = new Color32(173, 240, 55, 255);
                    break;
                case Surface.BlueButton:
                    bottom = new Color32(28, 98, 160, 255);
                    top = new Color32(103, 193, 245, 255);
                    break;
                case Surface.DarkPanel:
                    bottom = new Color32(28, 31, 55, 255);
                    top = new Color32(75, 89, 123, 255);
                    break;
                case Surface.CardFrame:
                    bottom = new Color32(27, 40, 67, 255);
                    top = new Color32(75, 91, 139, 255);
                    break;
                case Surface.Scroll:
                    bottom = new Color32(235, 183, 107, 255);
                    top = new Color32(255, 242, 194, 255);
                    break;
                case Surface.Button:
                    bottom = new Color32(216, 165, 103, 255);
                    top = new Color32(255, 232, 176, 255);
                    break;
                default:
                    bottom = new Color32(245, 211, 157, 255);
                    top = new Color32(255, 245, 211, 255);
                    break;
            }

            float t = Mathf.Clamp01((y - 17f) / (height - 34f));
            var fill = Color.Lerp(bottom, top, t);
            if (y > height - 33)
                fill = Color.Lerp(fill, Color.white, .11f);

            // Thick black outlines and tiny golden rivets are preserved by nine-slicing.
            if (surface != Surface.DarkPanel && surface != Surface.CardFrame)
            {
                int edgeX = x < width / 2 ? 21 : width - 22;
                int edgeY = y < height / 2 ? 21 : height - 22;
                int sx = x - edgeX, sy = y - edgeY;
                float dot = sx * sx + sy * sy;
                if (dot < 43) return new Color32(22, 21, 27, 255);
                if (dot < 26) return new Color32(244, 167, 49, 255);
                if (dot < 9) return new Color32(255, 238, 143, 255);
            }
            return fill;
        }

        private static bool InsideRounded(int x, int y, int width, int height, int inset, int radius)
        {
            float left = inset, right = width - inset - 1;
            float bottom = inset, top = height - inset - 1;
            if (x < left || x > right || y < bottom || y > top) return false;
            radius = Mathf.Max(1, radius);
            float px = Mathf.Clamp(x, left + radius, right - radius);
            float py = Mathf.Clamp(y, bottom + radius, top - radius);
            float dx = x - px, dy = y - py;
            return dx * dx + dy * dy <= radius * radius;
        }
    }
}
