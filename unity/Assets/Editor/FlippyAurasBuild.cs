// Builds Flippy Auras' shader bundle and checks the shader on D3D11, including a render test of both blend modes.
// Unity.exe -batchmode -quit -projectPath unity -buildTarget Win64 -executeMethod FlippyAurasBuild.BuildAndTest -logFile <log>
// Output: bin\bundle\flippyauras_shaders (loaded by the mod from its plugin folder) and bin\shader_test.txt.
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FlippyAurasBuild
{
    static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    static readonly StringBuilder report = new StringBuilder();
    static int failures;

    public static void BuildAndTest()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear; // like the game
            Build();
            Test();
        }
        catch (Exception e)
        {
            failures++;
            Line("FAILED: " + e);
        }
        Line(failures == 0 ? "ALL OK" : $"{failures} check(s) failed");
        Directory.CreateDirectory(Path.Combine(Root, "bin"));
        File.WriteAllText(Path.Combine(Root, "bin", "shader_test.txt"), report.ToString());
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static string[] ShaderPaths() =>
        AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();

    static void Build()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
        string output = Path.Combine(Root, "bin", "bundle");
        Directory.CreateDirectory(output);
        var bundle = new AssetBundleBuild { assetBundleName = "flippyauras_shaders", assetNames = ShaderPaths() };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { bundle },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new Exception("bundle build failed");
        Line($"bundle: {new FileInfo(Path.Combine(output, "flippyauras_shaders")).Length} bytes, shaders: {string.Join(", ", bundle.assetNames)}");
    }

    static void Test()
    {
        Line($"colour space {QualitySettings.activeColorSpace}, device {SystemInfo.graphicsDeviceType}");
        foreach (var path in ShaderPaths())
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            bool ok = shader.isSupported && !ShaderUtil.ShaderHasError(shader);
            if (!ok) failures++;
            Line($"{shader.name}: supported {shader.isSupported}, passes {shader.passCount}, errors {ShaderUtil.ShaderHasError(shader)}" + (ok ? " OK" : " FAIL"));
            foreach (var m in ShaderUtil.GetShaderMessages(shader)) Line($"  {m.severity} {m.platform} line {m.line}: {m.message}");
        }
        TestBlend();
    }

    // Draws a white quad at half alpha over a grey background, once additive and once see-through, and checks the
    // pixels: additive adds light (brighter than the background), see-through mixes toward white without going past it.
    static void TestBlend()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/FlippyAurasGlow.shader");
        var material = new Material(shader);
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) },
            colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 128), 4).ToArray(),
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        foreach (var (additive, name) in new[] { (1f, "additive"), (0f, "see-through") })
        {
            material.SetFloat("_Additive", additive);
            material.SetFloat("_Intensity", 1f);
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var cmd = new CommandBuffer();
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(false, true, new Color(0.25f, 0.25f, 0.25f, 1f));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            var read = new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            RenderTexture.active = null;
            float v = read.GetPixel(8, 8).r;
            float expect = additive > 0.5f ? 0.25f + 0.502f : 0.502f + 0.25f * (1f - 0.502f);
            bool ok = Mathf.Abs(v - expect) < 0.02f;
            if (!ok) failures++;
            Line($"blend {name}: {v:0.000} (expected {expect:0.000})" + (ok ? " OK" : " FAIL"));
            rt.Release();
        }
    }

    static void Line(string s)
    {
        report.AppendLine(s);
        Debug.Log("[FlippyAurasBuild] " + s);
    }
}
