using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using NV3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace FlippyAuras;

/// Our own particles' drawing: one mesh of camera-facing quads, drawn in one go. Each quad's 4 vertices sit at its
/// centre and carry their corner in uv2; the glow shader turns the corners toward whichever camera draws them (the
/// game's ParticleSystem modules are stripped from its build, so colour and size over lifetime are done here).
/// Buffers are kept and refilled, the mesh is marked dynamic (kit trap 17); a hidden quad has zero-size corners.
internal sealed class Quads
{
    readonly Mesh mesh;
    readonly Il2CppStructArray<Vector3> positions;
    readonly Il2CppStructArray<Vector2> corners, uv;
    readonly Il2CppStructArray<Color32> colors;
    readonly int[] cells;
    bool uvDirty;
    internal readonly int Capacity;
    internal readonly GameObject Go;

    public Quads(string name, int capacity, Material material, Transform parent, int cell)
    {
        Capacity = capacity;
        positions = new Il2CppStructArray<Vector3>(capacity * 4);
        corners = new Il2CppStructArray<Vector2>(capacity * 4);
        colors = new Il2CppStructArray<Color32>(capacity * 4);
        uv = new Il2CppStructArray<Vector2>(capacity * 4);
        cells = new int[capacity];
        var triangles = new Il2CppStructArray<int>(capacity * 6);
        for (int i = 0; i < capacity; i++)
        {
            cells[i] = -1;
            SetCell(i, cell);
            int v = i * 4, k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
            triangles[k + 3] = v; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
        }
        mesh = new Mesh { name = "FlippyAuras " + name, hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mesh.vertices = positions;
        mesh.uv = uv;
        mesh.uv2 = corners;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        uvDirty = false;

        Go = new GameObject(name); // at the world origin, unrotated: the vertices are world positions
        Go.transform.SetParent(parent, false);
        Go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = Go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    public void SetCell(int i, int cell)
    {
        if (cells[i] == cell) return;
        cells[i] = cell;
        float x0 = cell % 4 * 0.25f, y0 = cell / 4 * 0.25f, x1 = x0 + 0.25f, y1 = y0 + 0.25f;
        uv[i * 4] = new Vector2 { x = x0, y = y0 };
        uv[i * 4 + 1] = new Vector2 { x = x1, y = y0 };
        uv[i * 4 + 2] = new Vector2 { x = x1, y = y1 };
        uv[i * 4 + 3] = new Vector2 { x = x0, y = y1 };
        uvDirty = true;
    }

    /// angle spins the quad on screen (radians).
    public void Set(int i, NV3 p, float size, float angle, Col color, float alpha)
    {
        var v = new Vector3 { x = p.X, y = p.Y, z = p.Z };
        var c = color.ToColor32(alpha);
        int k = i * 4;
        positions[k] = v; positions[k + 1] = v; positions[k + 2] = v; positions[k + 3] = v;
        colors[k] = c; colors[k + 1] = c; colors[k + 2] = c; colors[k + 3] = c;
        float cs = MathF.Cos(angle) * size, sn = MathF.Sin(angle) * size;
        // corners (-1,-1) (1,-1) (1,1) (-1,1), turned by angle
        corners[k] = new Vector2 { x = -cs + sn, y = -sn - cs };
        corners[k + 1] = new Vector2 { x = cs + sn, y = sn - cs };
        corners[k + 2] = new Vector2 { x = cs - sn, y = sn + cs };
        corners[k + 3] = new Vector2 { x = -cs - sn, y = -sn + cs };
    }

    public void Hide(int i)
    {
        int k = i * 4;
        var zero = new Vector2();
        corners[k] = zero; corners[k + 1] = zero; corners[k + 2] = zero; corners[k + 3] = zero;
    }

    public void HideFrom(int first)
    {
        for (int i = first; i < Capacity; i++) Hide(i);
    }

    public void Apply(NV3 center, float extent)
    {
        mesh.vertices = positions;
        mesh.uv2 = corners;
        mesh.colors32 = colors;
        if (uvDirty) { mesh.uv = uv; uvDirty = false; }
        mesh.bounds = new Bounds(new Vector3 { x = center.X, y = center.Y, z = center.Z }, new Vector3 { x = extent, y = extent, z = extent });
    }

    public void Destroy()
    {
        if (mesh != null) Object.Destroy(mesh);
    }
}

/// How a particle looks over its life.
internal struct Style
{
    public bool ColorByAge;   // walk the gradient from its start colour to the end over the particle's life
    public bool GrowShrink;   // swell in, then shrink away (flames); else just shrink
    public float Twinkle;     // 0..1 flicker
}

/// A pool of simulated particles drawn with Quads. Spawning reuses the oldest slot when full (a hard cap).
internal sealed class Particles
{
    internal readonly Quads Q;
    readonly NV3[] p, v;
    readonly float[] age, life, size, t, spin;
    readonly bool[] alive;
    int next;
    internal int Alive;

    public Particles(string name, int capacity, Material material, Transform parent, int cell)
    {
        Q = new Quads(name, capacity, material, parent, cell);
        p = new NV3[capacity];
        v = new NV3[capacity];
        age = new float[capacity];
        life = new float[capacity];
        size = new float[capacity];
        t = new float[capacity];
        spin = new float[capacity];
        alive = new bool[capacity];
        Q.HideFrom(0);
    }

    /// t = where on the gradient it starts (or sits, without ColorByAge).
    public void Spawn(NV3 pos, NV3 vel, float lifetime, float sz, float gradient, int cell, float spinSpeed)
    {
        int i = next;
        next = (next + 1) % Q.Capacity;
        if (!alive[i]) Alive++;
        alive[i] = true;
        p[i] = pos; v[i] = vel; age[i] = 0f; life[i] = Math.Max(0.05f, lifetime); size[i] = sz; t[i] = gradient; spin[i] = spinSpeed;
        Q.SetCell(i, cell);
    }

    public void Step(float dt, NV3 accel, float drag, Slot slot, Style look, float time, NV3 center)
    {
        float keep = Math.Max(0f, 1f - drag * dt);
        for (int i = 0; i < Q.Capacity; i++)
        {
            if (!alive[i]) continue;
            age[i] += dt;
            if (age[i] >= life[i]) { alive[i] = false; Alive--; Q.Hide(i); continue; }
            v[i] = (v[i] + accel * dt) * keep;
            p[i] += v[i] * dt;
            float k = age[i] / life[i];
            float sz = look.GrowShrink ? size[i] * MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1f, k * 1.15f)), 0.6f) : size[i] * (1f - k * k);
            float alpha = MathF.Min(1f, k * 12f) * (1f - k * k * k);
            if (look.Twinkle > 0f) alpha *= 1f - look.Twinkle * (0.5f + 0.5f * MathF.Sin(time * 23f + i * 1.7f));
            var c = slot.Sample(look.ColorByAge ? t[i] + k * (1f - t[i]) : t[i]);
            Q.Set(i, p[i], sz, spin[i] * age[i], c, alpha);
        }
        Q.Apply(center, 14f);
    }

    public void Clear()
    {
        for (int i = 0; i < Q.Capacity; i++) alive[i] = false;
        Alive = 0;
        Q.HideFrom(0);
    }
}
