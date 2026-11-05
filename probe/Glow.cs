using System;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace FlippyAurasProbe;

/// The glow shader (from flippyauras_shaders next to the DLL), its materials and the textures drawn in code.
/// The bundle loads like Tripod Cam's and FIH Ultra's: LoadFromFileAsync + LoadAllAssetsAsync, a step per frame
/// (the plain LoadFromFile / LoadFromMemory are rebuilt by Il2CppInterop and can crash the game: kit trap 19).
static class Glow
{
    internal static Shader Shader;
    internal static string State = "not loaded yet";
    static AssetBundleCreateRequest bundleLoad;
    static AssetBundleRequest shaderLoad;
    static int step; // 0 not started, 1 loading the bundle, 2 loading the shader, 3 done

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
                    State = $"{shader.name}, supported {shader.isSupported}, {shader.passCount} pass(es)";
                    Plugin.Logger.LogInfo("Glow shader loaded: " + State);
                    break;
            }
        }
        catch (Exception e)
        {
            step = 3;
            State = "failed: " + e.Message;
            Plugin.Logger.LogError("glow shader: " + e.Message);
        }
    }

    /// A material for one aura part. Raw numbers go in with SetFloat (SetColor would turn colours to linear).
    /// Without the bundle it falls back to the game's Sprites/Default, so the test still shows something.
    internal static Material Material(Texture texture, float intensity, bool additive, bool billboard)
    {
        var shader = Shader != null ? Shader : UnityEngine.Shader.Find("Sprites/Default");
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.mainTexture = texture;
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_Additive", additive ? 1f : 0f);
        m.SetFloat("_Billboard", billboard ? 1f : 0f);
        m.renderQueue = 3000;
        return m;
    }

    // ----- textures drawn in code (no files) -----

    internal const int Dot = 0, Star = 1, Ghost = 2, Heart = 3;

    /// 256×256 atlas, four 128 cells: soft dot, 4-point star, ghost, heart. Cell i covers u (i % 2) / 2, v (i / 2) / 2.
    internal static Texture2D Atlas()
    {
        const int size = 256, cell = 128;
        var px = new Color32[size * size];
        for (int c = 0; c < 4; c++)
        for (int y = 0; y < cell; y++)
        for (int x = 0; x < cell; x++)
        {
            float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f; // -1..1
            float a = c switch { Dot => DotShape(u, v), Star => StarShape(u, v), Ghost => GhostShape(u, v, out _), _ => HeartShape(u, v) };
            byte shade = 255;
            if (c == Ghost) { GhostShape(u, v, out bool eye); if (eye) shade = 40; }
            px[(y + c / 2 * cell) * size + x + c % 2 * cell] = new Color32(shade, shade, shade, (byte)(Mathf.Clamp01(a) * 255));
        }
        return Make(size, size, px, TextureWrapMode.Clamp);
    }

    internal static Rect Cell(int c) => new Rect(c % 2 * 0.5f, c / 2 * 0.5f, 0.5f, 0.5f);

    /// A soft dot for the particle system's billboards.
    internal static Texture2D SoftDot()
    {
        const int size = 64;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(DotShape(u, v)) * 255));
        }
        return Make(size, size, px, TextureWrapMode.Clamp);
    }

    /// Across a line's width (v): bright in the middle, soft at the edges.
    internal static Texture2D LineGlow()
    {
        const int w = 4, h = 32;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            byte a = (byte)(Mathf.Clamp01(1f - d * d) * 255);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, a);
        }
        return Make(w, h, px, TextureWrapMode.Clamp);
    }

    static float DotShape(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        return Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
    }

    static float StarShape(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float rays = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(u) * 9f) * (1f - Mathf.Abs(v)), Mathf.Clamp01(1f - Mathf.Abs(v) * 9f) * (1f - Mathf.Abs(u)));
        return Mathf.Max(rays, Mathf.Pow(Mathf.Clamp01(1f - r * 2.2f), 1.5f)) * Mathf.Clamp01((1f - r) * 4f);
    }

    /// A round-topped body with a wavy hem; eye = inside one of its two eyes.
    static float GhostShape(float u, float v, out bool eye)
    {
        float top = 0.1f;
        bool head = v > top && u * u + (v - top) * (v - top) < 0.62f * 0.62f;
        float hem = -0.75f + 0.12f * Mathf.Sin(u * 9f);
        bool body = v <= top && v > hem && Mathf.Abs(u) < 0.62f;
        float ex = Mathf.Abs(u) - 0.22f, ey = v - 0.18f;
        eye = (head || body) && ex * ex / 0.012f + ey * ey / 0.025f < 1f;
        return head || body ? 0.9f : 0f;
    }

    static float HeartShape(float u, float v)
    {
        float x = u * 1.3f, y = v * 1.3f + 0.15f; // (x² + y² - 1)³ - x²y³ < 0 is inside the heart
        float f = Mathf.Pow(x * x + y * y - 1f, 3f) - x * x * y * y * y;
        return f < 0f ? 1f : 0f;
    }

    static Texture2D Make(int w, int h, Color32[] px, TextureWrapMode wrap)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
