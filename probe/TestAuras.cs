using System;
using System.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FlippyAurasProbe;

/// F9's test auras: one of each way Flippy Auras could draw, so one test run shows which work in the game, in Tripod
/// Cam's recordings and with FIH Ultra's bloom.
/// - Halo: a LineRenderer loop above Flippy (gold, glow 3), bigger for a moment on each jump.
/// - Orbiting stars: our own billboard mesh (12 quads, one draw), on a tilted ring, pink to cyan.
/// - Lightning: 3 LineRenderer bolts, rebuilt every 3 frames between random spots around him.
/// - Ghosts: the billboard mesh again, see-through instead of glowing, floating in lazy loops.
/// - Sparkle trail: a Unity ParticleSystem fed by Emit() as he moves (its settings modules are stripped from the game's
///   build, so only Emit and the main module are used).
/// - Ribbon: a TrailRenderer on a point that circles him.
/// Everything is in world space and only follows his position, so nothing spins when he tumbles. It hides while the
/// phone is destroyed. All on layer 0 (Default), which the player's camera and Tripod Cam's tripod both draw.
class TestAuras
{
    const int Stars = 12, Ghosts = 4, Bolts = 3, BoltPoints = 12, HaloPoints = 64;

    internal bool On;
    bool built;
    string status = "not switched on yet";
    GameObject root;
    LineRenderer halo;
    readonly LineRenderer[] bolts = new LineRenderer[Bolts];
    Billboards stars, ghosts;
    ParticleSystem sparkles;
    TrailRenderer ribbon;
    Transform ribbonPoint;
    Vector3 lastCenter;
    float sparkleDistance;
    int frame;
    readonly System.Random random = new(7);
    readonly Stopwatch watch = new();
    long ticks;
    int ticked;
    float costAt;

    internal string Status => status;

    public void Toggle()
    {
        if (On) { Hide(); Plugin.Logger.LogInfo("test auras off"); return; }
        if (!built) Build();
        if (root == null) return;
        On = true;
        root.SetActive(true);
        ribbon?.Clear();
        costAt = Time.unscaledTime + 10f;
        Plugin.Logger.LogInfo("test auras on (glow shader: " + Glow.State + ")");
    }

    public void Hide()
    {
        if (!On) return;
        On = false;
        if (root != null) root.SetActive(false);
    }

    void Build()
    {
        built = true;
        try
        {
            root = new GameObject("FlippyAurasProbe test auras");
            Object.DontDestroyOnLoad(root);
            var atlas = Glow.Atlas();
            var line = Glow.LineGlow();

            halo = Line("Halo", Glow.Material(line, 3f, true, false), HaloPoints, 0.06f, new Color(1f, 0.82f, 0.35f, 1f));
            halo.loop = true;
            var boltMat = Glow.Material(line, 5f, true, false);
            for (int i = 0; i < Bolts; i++) bolts[i] = Line("Bolt " + i, boltMat, BoltPoints, 0.05f, new Color(0.45f, 0.9f, 1f, 1f));

            stars = new Billboards("Stars", Stars, Glow.Material(atlas, 2.5f, true, true), Glow.Cell(Glow.Star), root.transform);
            ghosts = new Billboards("Ghosts", Ghosts, Glow.Material(atlas, 1f, false, true), Glow.Cell(Glow.Ghost), root.transform);

            var spark = new GameObject("Sparkles");
            spark.transform.SetParent(root.transform, false);
            sparkles = spark.AddComponent<ParticleSystem>();
            var main = sparkles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.playOnAwake = false;
            var emission = sparkles.emission;
            emission.enabled = false;
            var psr = spark.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = Glow.Material(Glow.SoftDot(), 3f, true, false);
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            sparkles.Play();

            var point = new GameObject("Ribbon");
            point.transform.SetParent(root.transform, false);
            ribbonPoint = point.transform;
            ribbon = point.AddComponent<TrailRenderer>();
            ribbon.sharedMaterial = Glow.Material(line, 3f, true, false);
            ribbon.time = 0.45f;
            ribbon.startWidth = 0.08f;
            ribbon.endWidth = 0f;
            ribbon.minVertexDistance = 0.04f;
            ribbon.startColor = new Color(1f, 0.35f, 0.9f, 1f);
            ribbon.endColor = new Color(1f, 0.35f, 0.9f, 0f);
            ribbon.shadowCastingMode = ShadowCastingMode.Off;
            ribbon.receiveShadows = false;

            root.SetActive(false);
            status = $"built (glow shader: {Glow.State})";
            Plugin.Logger.LogInfo("test auras " + status);
        }
        catch (Exception e)
        {
            status = "couldn't build: " + e.Message;
            Plugin.Logger.LogError("couldn't build the test auras: " + e);
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }

    LineRenderer Line(string name, Material material, int points, float width, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = material;
        lr.useWorldSpace = true;
        lr.positionCount = points;
        lr.startWidth = width;
        lr.endWidth = width * 0.5f;
        lr.startColor = color;
        lr.endColor = color;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// Every frame (LateUpdate, after the game moved Flippy).
    public void Tick(EHS.PlayerRef player, Events events)
    {
        if (root == null) return;
        bool show = !events.Destroyed;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;
        watch.Restart();
        var center = Report.Center(player, out _);
        float t = Time.time, pulse = events.JumpPulse;
        frame++;

        // halo: an upright ring above him
        float r = 0.55f * (1f + 0.4f * pulse);
        var top = center + Vector3.up * 1.25f;
        for (int i = 0; i < HaloPoints; i++)
        {
            float a = i * Mathf.PI * 2f / HaloPoints;
            halo.SetPosition(i, top + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }

        // lightning: new jagged bolts every 3 frames, faster and more of them while falling fast
        if (frame % 3 == 0)
            for (int b = 0; b < Bolts; b++)
            {
                bool lit = random.NextDouble() < 0.7 + Mathf.Clamp01(events.FallSpeed / 20f) * 0.3;
                bolts[b].enabled = lit;
                if (!lit) continue;
                var from = center + RandomOnSphere() * 0.4f;
                var to = center + RandomOnSphere() * 1.5f;
                for (int i = 0; i < BoltPoints; i++)
                {
                    float f = i / (float)(BoltPoints - 1);
                    var jitter = i == 0 || i == BoltPoints - 1 ? Vector3.zero : RandomOnSphere() * (0.18f * (1f - Mathf.Abs(2f * f - 1f)) + 0.03f);
                    bolts[b].SetPosition(i, Vector3.Lerp(from, to, f) + jitter);
                }
            }

        // orbiting stars on a ring tilted 20°, pink → cyan around it, twinkling
        var tilt = Quaternion.Euler(20f, 0f, 0f);
        for (int i = 0; i < Stars; i++)
        {
            float a = t * 1.1f + i * Mathf.PI * 2f / Stars;
            var p = center + tilt * new Vector3(Mathf.Cos(a) * 1.4f, 0f, Mathf.Sin(a) * 1.4f);
            var c = Color.Lerp(new Color(1f, 0.4f, 0.8f), new Color(0.3f, 0.9f, 1f), 0.5f + 0.5f * Mathf.Sin(a));
            c.a = 0.75f + 0.25f * Mathf.Sin(t * 7f + i);
            stars.Set(i, p, 0.16f * (1f + 0.5f * pulse), c);
        }
        stars.Apply(center);

        // ghosts: lazy loops with a bob, fading in and out
        for (int i = 0; i < Ghosts; i++)
        {
            float a = t * 0.6f + i * Mathf.PI * 0.5f;
            var p = center + new Vector3(Mathf.Cos(a) * 1.9f, 0.35f * Mathf.Sin(t * 1.3f + i * 2f), Mathf.Sin(a) * 1.9f);
            ghosts.Set(i, p, 0.32f, new Color(1f, 1f, 1f, 0.45f + 0.4f * Mathf.Sin(t * 0.8f + i)));
        }
        ghosts.Apply(center);

        // sparkle trail: a few particles for every 15 cm travelled
        sparkleDistance += (center - lastCenter).magnitude;
        if ((center - lastCenter).sqrMagnitude > 25f) sparkleDistance = 0f; // a respawn, not a move
        lastCenter = center;
        while (sparkleDistance > 0.15f)
        {
            sparkleDistance -= 0.15f;
            for (int k = 0; k < 2; k++)
            {
                var c = Color.Lerp(Color.white, new Color(1f, 0.6f, 0.95f), (float)random.NextDouble());
                sparkles.Emit(center + RandomOnSphere() * 0.35f, RandomOnSphere() * 0.3f + Vector3.up * 0.25f,
                    0.06f + 0.08f * (float)random.NextDouble(), 0.7f, (Color32)c);
            }
        }

        // ribbon: a point circling him fast
        float ra = t * 9f;
        ribbonPoint.position = center + Quaternion.Euler(-30f, 0f, 0f) * new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra));

        ticks += watch.ElapsedTicks;
        ticked++;
        if (Time.unscaledTime >= costAt)
        {
            double ms = ticks * 1000.0 / Stopwatch.Frequency / Math.Max(1, ticked);
            status = $"on, {ms.ToString("0.000", ProbeDriver.Inv)} ms a frame (main thread, average of {ticked} frames)";
            Plugin.Logger.LogInfo("test auras: " + status);
            ticks = 0;
            ticked = 0;
            costAt = Time.unscaledTime + 10f;
        }
    }

    Vector3 RandomOnSphere()
    {
        float z = (float)random.NextDouble() * 2f - 1f, a = (float)random.NextDouble() * Mathf.PI * 2f, s = Mathf.Sqrt(1f - z * z);
        return new Vector3(Mathf.Cos(a) * s, z, Mathf.Sin(a) * s);
    }
}

/// Our own particles: one mesh of camera-facing quads, drawn in one go. Each quad's 4 vertices sit at its centre and
/// carry their corner in uv2; the glow shader turns the corners toward whichever camera draws them. Buffers are kept
/// and refilled, and the mesh is marked dynamic (kit trap 17).
class Billboards
{
    readonly Mesh mesh;
    readonly Il2CppStructArray<Vector3> positions;
    readonly Il2CppStructArray<Vector2> corners;
    readonly Il2CppStructArray<Color32> colors;

    public Billboards(string name, int count, Material material, Rect cell, Transform parent)
    {
        positions = new Il2CppStructArray<Vector3>(count * 4);
        corners = new Il2CppStructArray<Vector2>(count * 4);
        colors = new Il2CppStructArray<Color32>(count * 4);
        var uv = new Il2CppStructArray<Vector2>(count * 4);
        var triangles = new Il2CppStructArray<int>(count * 6);
        for (int i = 0; i < count; i++)
        {
            uv[i * 4] = new Vector2(cell.xMin, cell.yMin);
            uv[i * 4 + 1] = new Vector2(cell.xMax, cell.yMin);
            uv[i * 4 + 2] = new Vector2(cell.xMax, cell.yMax);
            uv[i * 4 + 3] = new Vector2(cell.xMin, cell.yMax);
            int v = i * 4, k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
            triangles[k + 3] = v; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
        }
        mesh = new Mesh { name = "FlippyAurasProbe " + name, hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mesh.vertices = positions;
        mesh.uv = uv;
        mesh.uv2 = corners;
        mesh.colors32 = colors;
        mesh.triangles = triangles;

        var go = new GameObject(name); // at the world origin, unrotated: the vertices are world positions
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    public void Set(int i, Vector3 position, float size, Color color)
    {
        var c = (Color32)color;
        for (int k = 0; k < 4; k++)
        {
            positions[i * 4 + k] = position;
            colors[i * 4 + k] = c;
        }
        corners[i * 4] = new Vector2(-size, -size);
        corners[i * 4 + 1] = new Vector2(size, -size);
        corners[i * 4 + 2] = new Vector2(size, size);
        corners[i * 4 + 3] = new Vector2(-size, size);
    }

    public void Apply(Vector3 center)
    {
        mesh.vertices = positions;
        mesh.uv2 = corners;
        mesh.colors32 = colors;
        mesh.bounds = new Bounds(center, Vector3.one * 8f); // the vertices alone would make it a point
    }
}
