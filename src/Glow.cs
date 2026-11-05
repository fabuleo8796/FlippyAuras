using System;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace FlippyAuras;

/// The glow shader (FlippyAuras/Glow, from flippyauras_shaders next to the DLL), the auras' materials and the
/// textures drawn in code. The bundle loads with LoadFromFileAsync + LoadAllAssetsAsync, a step per frame (the plain
/// LoadFromFile / LoadFromMemory are rebuilt by Il2CppInterop and can crash the game: kit trap 19).
/// The shader: premultiplied blending, _Intensity for HDR glow (FIH Ultra's bloom picks it up), _Additive 1 = glows,
/// 0 = see-through, _Billboard 1 = quads turn to face whichever camera draws them (so Tripod Cam sees them right).
internal static class Glow
{
    internal static Shader Shader;
    internal static string State = "not loaded yet";
    static AssetBundleCreateRequest bundleLoad;
    static AssetBundleRequest shaderLoad;
    static int step; // 0 not started, 1 loading the bundle, 2 loading the shader, 3 done (or failed)

    internal static bool Ready => step == 3;

    internal static void Load()
    {
        if (step == 3) return;
        try
        {
            switch (step)
            {
                case 0:
                    step = 3;
                    var path = Path.Combine(Plugin.Folder, "flippyauras_shaders");
                    if (!File.Exists(path)) throw new Exception("missing " + path);
                    bundleLoad = AssetBundle.LoadFromFileAsync(path);
                    step = 1;
                    break;
                case 1:
                    if (!bundleLoad.isDone) return;
                    step = 3;
                    var bundle = bundleLoad.assetBundle;
                    bundleLoad = null;
                    if (bundle == null) throw new Exception("the shader bundle didn't load");
                    shaderLoad = bundle.LoadAllAssetsAsync(Il2CppType.Of<Shader>());
                    step = 2;
                    break;
                case 2:
                    if (!shaderLoad.isDone) return;
                    step = 3;
                    var assets = shaderLoad.allAssets;
                    shaderLoad = null;
                    var shader = assets != null && assets.Length > 0 ? assets[0].TryCast<Shader>() : null;
                    if (shader == null) throw new Exception("no shader in the bundle");
                    shader.hideFlags = HideFlags.HideAndDontSave;
                    Shader = shader;
                    State = $"{shader.name}, supported {shader.isSupported}";
                    Plugin.Logger.LogInfo("Glow shader loaded: " + State);
                    break;
            }
        }
        catch (Exception e)
        {
            step = 3;
            State = "failed: " + e.Message;
            Plugin.Logger.LogError("glow shader: " + e.Message + " (auras fall back to the game's Sprites/Default)");
        }
    }

    /// A material for one aura part. Without the bundle it falls back to the game's Sprites/Default.
    internal static Material Material(Texture texture, bool additive, bool billboard)
    {
        var shader = Shader != null ? Shader : UnityEngine.Shader.Find("Sprites/Default");
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.mainTexture = texture;
        m.SetFloat("_Intensity", 1f);
        m.SetFloat("_Additive", additive ? 1f : 0f);
        m.SetFloat("_Billboard", billboard ? 1f : 0f);
        m.renderQueue = 3000;
        return m;
    }

    // ----- textures drawn in code (no files) -----

    internal const int Dot = 0, Star = 1, Ghost = 2, Heart = 3, Flame = 4, Spark = 5, Gem = 6, Ring = 7, Bolt = 8, Picture = 9, Dice = 10;
    const int AtlasSize = 512, CellSize = 128, PerRow = 4;
    static Texture2D atlas, line;
    static readonly Sprite[] icons = new Sprite[16];

    /// 512×512, 4×4 cells of 128 (11 used), with mipmaps. Cell c covers u (c % 4) / 4, v (c / 4) / 4.
    internal static Texture2D Atlas()
    {
        if (atlas != null) return atlas;
        var px = new Color32[AtlasSize * AtlasSize];
        for (int c = 0; c <= Dice; c++)
        {
            int ox = c % PerRow * CellSize, oy = c / PerRow * CellSize;
            for (int y = 0; y < CellSize; y++)
            for (int x = 0; x < CellSize; x++)
            {
                // 4 samples a pixel: smooth edges on the hard shapes
                float a = 0f, shade = 0f;
                for (int s = 0; s < 4; s++)
                {
                    float u = (x + 0.25f + 0.5f * (s & 1)) / CellSize * 2f - 1f, v = (y + 0.25f + 0.5f * (s >> 1)) / CellSize * 2f - 1f;
                    float sa = Shape(c, u * 1.06f, v * 1.06f, out float sh); // a little margin so mipmaps don't bleed
                    a += sa;
                    shade += sh * sa;
                }
                shade = a > 0f ? shade / a : 1f;
                a *= 0.25f;
                byte w = (byte)(Math.Clamp(shade, 0f, 1f) * 255f);
                px[(oy + y) * AtlasSize + ox + x] = new Color32 { r = w, g = w, b = w, a = (byte)(Math.Clamp(a, 0f, 1f) * 255f) };
            }
        }
        atlas = Make(AtlasSize, AtlasSize, px, true);
        return atlas;
    }

    internal static Rect Cell(int c) => new() { x = c % PerRow * 0.25f, y = c / PerRow * 0.25f, width = 0.25f, height = 0.25f };

    /// A cell of the atlas as a UI sprite (the panel's icons).
    internal static Sprite Icon(int c)
    {
        if (icons[c] != null) return icons[c];
        var tex = Atlas();
        var rect = new Rect { x = c % PerRow * CellSize, y = c / PerRow * CellSize, width = CellSize, height = CellSize };
        var sprite = Sprite.Create(tex, rect, new Vector2 { x = 0.5f, y = 0.5f }, 100f, 0, SpriteMeshType.FullRect, Vector4.zero);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return icons[c] = sprite;
    }

    /// Across a line's width (v): bright in the middle, soft at the edges.
    internal static Texture2D LineGlow()
    {
        if (line != null) return line;
        const int w = 4, h = 32;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float d = MathF.Abs((y + 0.5f) / h * 2f - 1f);
            byte a = (byte)(Math.Clamp(1f - d * d, 0f, 1f) * 255);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32 { r = 255, g = 255, b = 255, a = a };
        }
        line = Make(w, h, px, false);
        return line;
    }

    /// Alpha of shape c at (u, v) in -1..1 (v up); shade = brightness (the ghost's eyes are dark).
    static float Shape(int c, float u, float v, out float shade)
    {
        shade = 1f;
        float r = MathF.Sqrt(u * u + v * v);
        switch (c)
        {
            case Dot:
                return Sq(Clamp01(1f - r));
            case Star:
            {
                float rays = MathF.Max(Clamp01(1f - MathF.Abs(u) * 9f) * (1f - MathF.Abs(v)), Clamp01(1f - MathF.Abs(v) * 9f) * (1f - MathF.Abs(u)));
                return MathF.Max(rays, MathF.Pow(Clamp01(1f - r * 2.2f), 1.5f)) * Clamp01((1f - r) * 4f);
            }
            case Ghost:
            {
                const float top = 0.1f;
                bool head = v > top && u * u + (v - top) * (v - top) < 0.62f * 0.62f;
                float hem = -0.75f + 0.12f * MathF.Sin(u * 9f);
                bool body = v <= top && v > hem && MathF.Abs(u) < 0.62f;
                if (!head && !body) return 0f;
                float ex = MathF.Abs(u) - 0.22f, ey = v - 0.18f;
                if (ex * ex / 0.012f + ey * ey / 0.025f < 1f) shade = 0.15f;
                return 0.92f;
            }
            case Heart:
            {
                float x = u * 1.3f, y = v * 1.3f + 0.15f; // (x² + y² - 1)³ - x²y³ < 0 is inside the heart
                float f = MathF.Pow(x * x + y * y - 1f, 3f) - x * x * y * y * y;
                return f < 0f ? 1f : 0f;
            }
            case Flame:
            {
                float t = (v + 0.8f) / 1.75f; // 0 at the bottom, 1 at the tip
                if (t <= 0f || t >= 1f) return 0f;
                float x = u - 0.18f * t * t; // the tip leans a little
                float half = t < 0.32f ? 0.55f * MathF.Sqrt(MathF.Max(0f, 1f - Sq((t - 0.32f) / 0.32f))) : 0.55f * MathF.Pow(1f - (t - 0.32f) / 0.68f, 1.5f);
                float across = MathF.Abs(x) / MathF.Max(half, 0.001f);
                if (across >= 1f) return 0f;
                shade = 0.7f + 0.3f * (1f - across); // a brighter core
                return Clamp01((1f - across) * 5f);
            }
            case Spark:
            {
                float rays = MathF.Max(Clamp01(1f - MathF.Abs(u) * 16f) * Sq(1f - MathF.Abs(v)), Clamp01(1f - MathF.Abs(v) * 16f) * Sq(1f - MathF.Abs(u)));
                return MathF.Max(rays, Sq(Clamp01(1f - r * 3.2f))) * Clamp01((1f - r) * 5f);
            }
            case Gem:
            {
                float d = MathF.Abs(u) * 0.95f + MathF.Abs(v) * 0.7f;
                if (d >= 0.72f) return 0f;
                shade = u + v > 0f ? 1f : 0.72f; // two facets
                return Clamp01((0.72f - d) * 12f);
            }
            case Ring:
                return Clamp01(1f - MathF.Abs(r - 0.66f) / 0.16f);
            case Bolt:
                return Inside(u, v, BoltShape) ? 1f : 0f;
            case Picture:
            {
                float ax = MathF.Abs(u), ay = MathF.Abs(v);
                if (ax > 0.82f || ay > 0.68f) return 0f;
                if (ax > 0.68f || ay > 0.54f) return 1f; // the frame
                bool hill = v < -0.54f + 0.75f * (1f - MathF.Abs(u + 0.15f) * 1.5f);
                bool sun = (u - 0.35f) * (u - 0.35f) + (v - 0.22f) * (v - 0.22f) < 0.15f * 0.15f;
                return hill || sun ? 1f : 0f;
            }
            case Dice:
            {
                float dx = MathF.Max(MathF.Abs(u) - 0.5f, 0f), dy = MathF.Max(MathF.Abs(v) - 0.5f, 0f);
                if (dx * dx + dy * dy > 0.25f * 0.25f) return 0f;
                foreach (var (px, py) in Pips)
                    if ((u - px) * (u - px) + (v - py) * (v - py) < 0.13f * 0.13f) return 0f;
                return 1f;
            }
        }
        return 0f;
    }

    static readonly (float, float)[] Pips = { (0f, 0f), (-0.38f, -0.38f), (0.38f, 0.38f), (-0.38f, 0.38f), (0.38f, -0.38f) };
    static readonly (float, float)[] BoltShape = { (0.30f, 0.95f), (-0.42f, 0.02f), (-0.02f, 0.02f), (-0.30f, -0.95f), (0.44f, 0.14f), (0.04f, 0.14f) };

    static bool Inside(float x, float y, (float, float)[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    static float Sq(float v) => v * v;

    static Texture2D Make(int w, int h, Color32[] px, bool mips)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mips) { wrapMode = TextureWrapMode.Clamp, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear };
        tex.SetPixels32(px);
        tex.Apply(mips, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
