using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using NQ = System.Numerics.Quaternion;
using NV3 = System.Numerics.Vector3;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FlippyAuras;

/// This frame, for every aura: where Flippy is, and what just happened.
internal struct Frame
{
    public NV3 Center;
    public float Dt, Time, Jump, Fall, Still, LandPower;
    public bool Jumped, Landed, Teleported;
}

/// The names, icons and words the panel shows for each aura, and how a slot's Amount turns into a count.
internal static class Auras
{
    internal static readonly string[] Names = { "Lightning", "Ghosts", "Orbiters", "Flames", "Sparkles", "Halo", "Image" };
    internal static readonly int[] Icons = { Glow.Bolt, Glow.Ghost, Glow.Star, Glow.Flame, Glow.Spark, Glow.Ring, Glow.Picture };
    internal static readonly int[] Shapes = { Glow.Star, Glow.Heart, Glow.Dot, Glow.Flame, Glow.Gem };
    internal static readonly string[] ShapeNames = { "Stars", "Hearts", "Dots", "Flames", "Gems" };

    internal static int Count(float amount, int min, int max) => min + (int)MathF.Round(Math.Clamp(amount, 0f, 1f) * (max - min));

    internal static string AmountText(Slot s) => s.Type switch
    {
        AuraType.Lightning => Plural(Lightning.Bolts(s), "bolt"),
        AuraType.Ghosts => Plural(Ghosts.Count(s), "ghost"),
        AuraType.Orbiters => $"{Orbiters.PerRing(s)} a ring",
        AuraType.Flames => $"{Flames.Rate(s):0} a sec",
        AuraType.Sparkles => $"{Sparkles.PerMetre(s):0} a metre",
        AuraType.Halo => Plural(Halo.Rings(s), "ring"),
        _ => "–",
    };

    static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    internal static string Hint(AuraType t) => t switch
    {
        AuraType.Lightning => "Electric bolts jump around Flippy. Falling fast makes them wilder.",
        AuraType.Ghosts => "Little ghosts drift around Flippy in lazy loops, fading in and out.",
        AuraType.Flames => "Flames lick up around Flippy with embers rising. Moving leaves a fire trail.",
        AuraType.Sparkles => "Glitter that trails behind Flippy on the move, and twinkles when still.",
        AuraType.Halo => "A glowing ring floating above Flippy. Amount stacks up to 3 rings.",
        AuraType.Image => "Your own picture floating around Flippy is coming in the next update.",
        _ => "",
    };

    internal static Aura Create(Slot s, Transform root) => s.Type switch
    {
        AuraType.Lightning => new Lightning(s, root),
        AuraType.Ghosts => new Ghosts(s, root),
        AuraType.Orbiters => new Orbiters(s, root),
        AuraType.Flames => new Flames(s, root),
        AuraType.Sparkles => new Sparkles(s, root),
        AuraType.Halo => new Halo(s, root),
        _ => null,
    };
}

/// One aura in a slot. It owns its objects and materials, reads its slot every frame (so the panel's edits show at
/// once), and handles the landing shockwave, which every aura can do.
internal abstract class Aura
{
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    const int ShockDots = 48;

    internal readonly AuraType Type;
    protected readonly Slot slot;
    protected readonly GameObject go;
    protected readonly Random rnd = new();
    readonly List<Material> materials = new();
    protected float phase;
    float glowSet = -1f;
    Quads shock;
    float shockT = 1f, shockPower;
    NV3 shockAt;
    bool shockShown;

    protected Aura(AuraType type, Slot slot, Transform root)
    {
        Type = type;
        this.slot = slot;
        go = new GameObject(type.ToString());
        go.transform.SetParent(root, false);
    }

    internal virtual int Particles => 0;

    protected Material Mat(Texture texture, bool additive, bool billboard)
    {
        var m = Glow.Material(texture, additive, billboard);
        materials.Add(m);
        return m;
    }

    public void Tick(in Frame f)
    {
        float speed = slot.Speed * (slot.Still ? 1f - 0.65f * f.Still : 1f);
        phase += f.Dt * speed;
        if (glowSet != slot.Glow)
        {
            glowSet = slot.Glow;
            foreach (var m in materials) m.SetFloat(IntensityId, Math.Max(0.05f, slot.Glow));
        }
        if (f.Teleported) Teleported();
        Run(f, speed);
        Shock(f);
    }

    protected abstract void Run(in Frame f, float speed);

    protected virtual void Teleported() { }

    /// Hard landings send a ring of light out along the ground.
    void Shock(in Frame f)
    {
        if (slot.Land && f.Landed)
        {
            if (shock == null)
            {
                shock = new Quads("Shockwave", ShockDots, Mat(Glow.Atlas(), true, true), go.transform, Glow.Dot);
                glowSet = -1f; // so the new material gets the glow too
            }
            shockT = 0f;
            shockPower = f.LandPower;
            shockAt = f.Center;
        }
        if (shock == null) return;
        if (shockT >= 1f)
        {
            if (shockShown) { shock.HideFrom(0); shock.Apply(shockAt, 1f); shockShown = false; }
            return;
        }
        shockT = Math.Min(1f, shockT + f.Dt / 0.55f);
        float ease = 1f - MathF.Pow(1f - shockT, 3f);
        float r = (0.3f + (1.6f + 1.8f * shockPower) * ease) * slot.Radius;
        float alpha = MathF.Pow(1f - shockT, 1.5f);
        float size = 0.12f * slot.Size * (1f - 0.5f * shockT);
        for (int i = 0; i < ShockDots; i++)
        {
            float a = i * MathF.Tau / ShockDots;
            var p = shockAt + new NV3(MathF.Cos(a) * r, -0.12f, MathF.Sin(a) * r);
            shock.Set(i, p, size, 0f, slot.Sample(0.5f - 0.5f * MathF.Cos(a)), alpha);
        }
        shock.Apply(shockAt, r + 1f);
        shockShown = true;
    }

    public void Show(bool on)
    {
        if (go.activeSelf != on) go.SetActive(on);
    }

    public virtual void Destroy()
    {
        Object.Destroy(go);
        foreach (var m in materials) Object.Destroy(m);
        shock?.Destroy();
    }

    protected NV3 OnSphere()
    {
        float z = (float)rnd.NextDouble() * 2f - 1f, a = (float)rnd.NextDouble() * MathF.Tau, s = MathF.Sqrt(1f - z * z);
        return new NV3(MathF.Cos(a) * s, z, MathF.Sin(a) * s);
    }

    protected float R01() => (float)rnd.NextDouble();

    protected static Vector3 V(NV3 v) => new() { x = v.X, y = v.Y, z = v.Z };
}

/// ⚡ Jagged bolts between random spots around Flippy, re-struck every few frames and fading fast in between, each
/// with a little side branch and sparks where it ends. LineRenderers in a holder that follows Flippy, so the bolts
/// stay on him between strikes.
internal sealed class Lightning : Aura
{
    const int Max = 6, Points = 12, BranchPoints = 5;
    readonly Transform holder;
    readonly LineRenderer[] bolts = new LineRenderer[Max], branches = new LineRenderer[Max];
    readonly float[] alpha = new float[Max];
    readonly Col[] from = new Col[Max], to = new Col[Max];
    readonly Particles sparks;
    float timer, widthSet = -1f;

    internal static int Bolts(Slot s) => Auras.Count(s.Amount, 1, Max);

    public Lightning(Slot slot, Transform root) : base(AuraType.Lightning, slot, root)
    {
        var h = new GameObject("Bolts");
        h.transform.SetParent(go.transform, false);
        holder = h.transform;
        var lineMat = Mat(Glow.LineGlow(), true, false);
        for (int i = 0; i < Max; i++)
        {
            bolts[i] = Line("Bolt", lineMat, Points);
            branches[i] = Line("Branch", lineMat, BranchPoints);
        }
        sparks = new Particles("Sparks", 48, Mat(Glow.Atlas(), true, true), go.transform, Glow.Spark);
    }

    LineRenderer Line(string name, Material material, int points)
    {
        var o = new GameObject(name);
        o.transform.SetParent(holder, false);
        var lr = o.AddComponent<LineRenderer>();
        lr.sharedMaterial = material;
        lr.useWorldSpace = false;
        lr.positionCount = points;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    internal override int Particles => sparks.Alive;

    protected override void Teleported() => sparks.Clear();

    protected override void Run(in Frame f, float speed)
    {
        holder.position = V(f.Center);
        if (widthSet != slot.Size)
        {
            widthSet = slot.Size;
            for (int i = 0; i < Max; i++)
            {
                bolts[i].startWidth = 0.055f * slot.Size; bolts[i].endWidth = 0.025f * slot.Size;
                branches[i].startWidth = 0.035f * slot.Size; branches[i].endWidth = 0.01f * slot.Size;
            }
        }
        float wild = slot.Fall ? f.Fall : 0f;
        bool burst = slot.Jump && f.Jumped;
        timer -= f.Dt * (0.35f + 0.65f * speed) * (1f + wild * 1.5f);
        if (timer <= 0f || burst)
        {
            timer = 0.065f;
            Strike(f, burst, wild);
        }
        for (int i = 0; i < Max; i++)
        {
            float a = alpha[i] = Math.Max(0f, alpha[i] - f.Dt * 8f);
            bool on = a > 0.02f;
            if (bolts[i].enabled != on) { bolts[i].enabled = on; branches[i].enabled = on; }
            if (!on) continue;
            bolts[i].startColor = from[i].ToColor(a);
            bolts[i].endColor = to[i].ToColor(a * 0.7f);
            branches[i].startColor = from[i].ToColor(a * 0.8f);
            branches[i].endColor = to[i].ToColor(0f);
        }
        sparks.Step(f.Dt, new NV3(0f, -3f, 0f), 2f, slot, new Style { Twinkle = 0.5f }, f.Time, f.Center);
    }

    void Strike(in Frame f, bool burst, float wild)
    {
        int n = Bolts(slot);
        float r = slot.Radius, reach = (1.25f + 0.5f * wild + (burst ? 0.5f : 0f)) * r;
        for (int i = 0; i < n; i++)
        {
            if (!burst && R01() > 0.7f + 0.3f * wild) continue;
            var a = OnSphere() * 0.35f * r;
            var b = OnSphere() * reach;
            NV3 mid = a;
            for (int k = 0; k < Points; k++)
            {
                float t = k / (float)(Points - 1);
                float jitter = k == 0 || k == Points - 1 ? 0f : 0.17f * r * (1f - MathF.Abs(2f * t - 1f)) + 0.02f;
                var p = NV3.Lerp(a, b, t) + OnSphere() * jitter;
                if (k == 5) mid = p;
                bolts[i].SetPosition(k, V(p));
            }
            var dir = NV3.Normalize(OnSphere() + NV3.Normalize(b - a + new NV3(0.0001f)) * 0.5f);
            for (int k = 0; k < BranchPoints; k++)
            {
                float t = k / (float)(BranchPoints - 1);
                branches[i].SetPosition(k, V(mid + dir * (0.4f * r * t) + (k == 0 ? NV3.Zero : OnSphere() * 0.05f * r)));
            }
            alpha[i] = 1f;
            from[i] = slot.Sample(R01());
            to[i] = slot.Sample(R01());
            for (int s = 0; s < 2; s++)
                sparks.Spawn(f.Center + b, OnSphere() * 1.5f, 0.25f + 0.2f * R01(), (0.05f + 0.04f * R01()) * slot.Size, R01(), Glow.Spark, 0f);
        }
    }
}

/// 🌀 Shapes circling Flippy on 1 to 3 tilted rings, each ring its own tilt, speed and direction.
internal sealed class Orbiters : Aura
{
    const int Max = 16;
    static readonly NQ[] Tilts =
    {
        NQ.CreateFromYawPitchRoll(0f, 0.35f, 0f),
        NQ.CreateFromYawPitchRoll(1.05f, -0.7f, 0f),
        NQ.CreateFromYawPitchRoll(-0.5f, 0f, 1.13f),
    };
    readonly Quads q;

    internal static int PerRing(Slot s) => Auras.Count(s.Amount, 3, Max);

    public Orbiters(Slot slot, Transform root) : base(AuraType.Orbiters, slot, root)
    {
        q = new Quads("Orbiters", Max * 3, Mat(Glow.Atlas(), true, true), go.transform, Glow.Star);
    }

    protected override void Run(in Frame f, float speed)
    {
        int rings = Math.Clamp(slot.Rings, 1, 3), n = PerRing(slot), cell = Auras.Shapes[Math.Clamp(slot.Shape, 0, Auras.Shapes.Length - 1)];
        float jump = slot.Jump ? f.Jump : 0f;
        bool spins = cell == Glow.Star || cell == Glow.Gem;
        float size = 0.16f * slot.Size * (1f + 0.5f * jump);
        int idx = 0;
        for (int k = 0; k < rings; k++)
        {
            float r = 1.4f * slot.Radius * (1f + 0.2f * k) * (1f + 0.4f * jump);
            float turn = phase * 1.1f * (1f - 0.15f * k) * (k % 2 == 0 ? 1f : -1f) + k * 0.7f;
            for (int i = 0; i < n; i++)
            {
                float a = turn + i * MathF.Tau / n;
                var p = f.Center + NV3.Transform(new NV3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r), Tilts[k]);
                float alpha = 0.8f + 0.2f * MathF.Sin(f.Time * 6f + i * 1.3f + k);
                q.SetCell(idx, cell);
                q.Set(idx++, p, size, spins ? phase * 1.5f + i : 0f, slot.Sample(0.5f - 0.5f * MathF.Cos(i * MathF.Tau / n)), alpha);
            }
        }
        q.HideFrom(idx);
        q.Apply(f.Center, 6f * slot.Radius);
    }
}

/// 😇 A glowing ring floating above Flippy: soft dots close together, so it glows evenly from every side, with a
/// gentle shimmer. Up to 3 stacked rings.
internal sealed class Halo : Aura
{
    const int PerRing = 56;
    readonly Quads dots;

    internal static int Rings(Slot s) => Auras.Count(s.Amount, 1, 3);

    public Halo(Slot slot, Transform root) : base(AuraType.Halo, slot, root)
    {
        dots = new Quads("Halo", PerRing * 3, Mat(Glow.Atlas(), true, true), go.transform, Glow.Dot);
    }

    protected override void Run(in Frame f, float speed)
    {
        int rings = Rings(slot);
        float jump = slot.Jump ? f.Jump : 0f;
        float y = 1.25f + 0.05f * MathF.Sin(phase * 2f);
        int idx = 0;
        for (int k = 0; k < rings; k++)
        {
            float r = 0.5f * slot.Radius * (1f - 0.16f * k) * (1f + 0.35f * jump);
            float turn = phase * 0.3f * (k % 2 == 0 ? 1f : -1f);
            for (int i = 0; i < PerRing; i++)
            {
                float a = turn + i * MathF.Tau / PerRing;
                var p = f.Center + new NV3(MathF.Cos(a) * r, y + 0.13f * k, MathF.Sin(a) * r);
                float size = 0.075f * slot.Size * (1f + 0.12f * MathF.Sin(phase * 3f + i * 0.9f)) * (1f + 0.3f * jump);
                dots.Set(idx++, p, size, 0f, slot.Sample(0.5f - 0.5f * MathF.Cos(i * MathF.Tau / PerRing)), 0.85f);
            }
        }
        dots.HideFrom(idx);
        dots.Apply(f.Center, 4f * slot.Radius);
    }
}

/// 👻 Little see-through ghosts floating in lazy loops around Flippy, bobbing, wobbling and fading in and out.
internal sealed class Ghosts : Aura
{
    const int Max = 10;
    readonly Quads q;
    readonly float[] seed = new float[Max];

    internal static int Count(Slot s) => Auras.Count(s.Amount, 1, Max);

    public Ghosts(Slot slot, Transform root) : base(AuraType.Ghosts, slot, root)
    {
        q = new Quads("Ghosts", Max, Mat(Glow.Atlas(), false, true), go.transform, Glow.Ghost);
        for (int i = 0; i < Max; i++) seed[i] = R01();
    }

    protected override void Run(in Frame f, float speed)
    {
        int n = Count(slot);
        float jump = slot.Jump ? f.Jump : 0f;
        for (int i = 0; i < n; i++)
        {
            float s = seed[i];
            float a = phase * (0.45f + 0.25f * s) * (i % 2 == 0 ? 1f : -1f) + i * MathF.Tau / n;
            float r = 1.9f * slot.Radius * (0.8f + 0.35f * s) * (1f + 0.6f * jump);
            float y = 0.4f * MathF.Sin(phase * 1.2f + i * 2.1f) + 0.25f * (s - 0.5f);
            var p = f.Center + new NV3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
            float size = 0.3f * slot.Size * (1f + 0.07f * MathF.Sin(phase * 3.1f + i));
            float alpha = 0.55f + 0.35f * MathF.Sin(phase * 0.9f + i * 1.7f);
            q.Set(i, p, size, 0.18f * MathF.Sin(phase * 2f + i), slot.Sample(n == 1 ? 0f : i / (float)(n - 1)), alpha);
        }
        q.HideFrom(n);
        q.Apply(f.Center, 6f * slot.Radius);
    }
}

/// 🔥 Flames licking up around Flippy (their colour walks the gradient as they rise), with small embers drifting up.
/// They live in the world, so moving leaves a trail of fire.
internal sealed class Flames : Aura
{
    readonly Particles flames, embers;
    float emit, emberEmit;

    internal static float Rate(Slot s) => 25f + 135f * Math.Clamp(s.Amount, 0f, 1f);

    public Flames(Slot slot, Transform root) : base(AuraType.Flames, slot, root)
    {
        var mat = Mat(Glow.Atlas(), true, true);
        flames = new Particles("Flames", 170, mat, go.transform, Glow.Flame);
        embers = new Particles("Embers", 60, mat, go.transform, Glow.Dot);
    }

    internal override int Particles => flames.Alive + embers.Alive;

    protected override void Teleported() { flames.Clear(); embers.Clear(); }

    protected override void Run(in Frame f, float speed)
    {
        float fall = slot.Fall ? f.Fall : 0f;
        float rate = Rate(slot) * (0.5f + 0.5f * Math.Min(speed, 2f));
        emit += f.Dt * rate;
        for (int guard = 0; emit >= 1f && guard < 20; guard++) { emit -= 1f; Flame(f, speed, fall, 0f); }
        if (emit > 1f) emit = 0f;
        emberEmit += f.Dt * rate * 0.15f;
        for (int guard = 0; emberEmit >= 1f && guard < 5; guard++) { emberEmit -= 1f; Ember(f); }
        if (emberEmit > 1f) emberEmit = 0f;
        if (slot.Jump && f.Jumped)
            for (int i = 0; i < 22; i++) Flame(f, speed, fall, 2.5f);
        flames.Step(f.Dt, new NV3(0f, 0.6f * Math.Max(0.3f, speed), 0f), 1.2f, slot, new Style { ColorByAge = true, GrowShrink = true, Twinkle = 0.15f }, f.Time, f.Center);
        embers.Step(f.Dt, new NV3(0f, 0.3f, 0f), 0.4f, slot, new Style { ColorByAge = true, Twinkle = 0.6f }, f.Time, f.Center);
    }

    void Flame(in Frame f, float speed, float fall, float boost)
    {
        float a = R01() * MathF.Tau, rr = 0.55f * slot.Radius * (0.5f + 0.5f * R01());
        var pos = f.Center + new NV3(MathF.Cos(a) * rr, -0.35f + 0.3f * R01(), MathF.Sin(a) * rr);
        float up = (0.9f + 0.6f * R01()) * (0.6f + 0.4f * Math.Min(speed, 2.5f)) * (1f + 0.5f * fall) + boost;
        var vel = new NV3(MathF.Cos(a) * (0.15f + boost * 0.2f) + (R01() - 0.5f) * 0.2f, up, MathF.Sin(a) * (0.15f + boost * 0.2f) + (R01() - 0.5f) * 0.2f);
        flames.Spawn(pos, vel, (0.5f + 0.35f * R01()) * (1f + 0.6f * fall), 0.24f * slot.Size * (0.7f + 0.6f * R01()), R01() * 0.15f, Glow.Flame, 0f);
    }

    void Ember(in Frame f)
    {
        float a = R01() * MathF.Tau, rr = 0.6f * slot.Radius * R01();
        var pos = f.Center + new NV3(MathF.Cos(a) * rr, -0.2f + 0.4f * R01(), MathF.Sin(a) * rr);
        var vel = new NV3((R01() - 0.5f) * 0.8f, 1.4f + R01(), (R01() - 0.5f) * 0.8f);
        embers.Spawn(pos, vel, 1.2f + 0.6f * R01(), 0.045f * slot.Size * (0.7f + 0.6f * R01()), 0.2f, Glow.Dot, 0f);
    }
}

/// ✨ Glitter that trails behind Flippy as he moves (more per metre with Amount), twinkling, and a calm sprinkle when
/// standing still.
internal sealed class Sparkles : Aura
{
    readonly Particles glitter;
    NV3 last;
    bool hasLast;
    float distance, idle;

    internal static float PerMetre(Slot s) => (0.6f + Math.Clamp(s.Amount, 0f, 1f) * 3f) / 0.15f;

    public Sparkles(Slot slot, Transform root) : base(AuraType.Sparkles, slot, root)
    {
        glitter = new Particles("Sparkles", 200, Mat(Glow.Atlas(), true, true), go.transform, Glow.Spark);
    }

    internal override int Particles => glitter.Alive;

    protected override void Teleported() { glitter.Clear(); hasLast = false; }

    protected override void Run(in Frame f, float speed)
    {
        float fall = slot.Fall ? f.Fall : 0f;
        if (hasLast) distance += NV3.Distance(f.Center, last);
        last = f.Center;
        hasLast = true;
        float step = 1f / PerMetre(slot);
        int spawned = 0;
        while (distance > step && spawned < 40) { distance -= step; Spawn(f, speed, fall, 0f); spawned++; }
        if (distance > step) distance = 0f;
        idle += f.Dt * (3f + 10f * slot.Amount);
        while (idle >= 1f) { idle -= 1f; Spawn(f, speed, fall, 0f); }
        if (slot.Jump && f.Jumped)
            for (int i = 0; i < 26; i++) Spawn(f, speed, fall, 2.2f);
        glitter.Step(f.Dt, new NV3(0f, -0.4f, 0f), 1.5f, slot, new Style { Twinkle = 0.7f }, f.Time, f.Center);
    }

    void Spawn(in Frame f, float speed, float fall, float burst)
    {
        var dir = OnSphere();
        var pos = f.Center + dir * (0.35f * slot.Radius * (burst > 0f ? 0.5f : R01()));
        var vel = (burst > 0f ? dir * burst : OnSphere() * (0.3f * Math.Max(0.2f, speed))) + new NV3(0f, 0.25f, 0f);
        float pick = R01();
        int cell = pick < 0.6f ? Glow.Spark : pick < 0.8f ? Glow.Star : Glow.Dot;
        glitter.Spawn(pos, vel, 0.7f * (1f + 1.2f * fall) * (0.7f + 0.6f * R01()), (0.05f + 0.08f * R01()) * slot.Size, R01(), cell, (R01() - 0.5f) * 4f);
    }
}

/// The 3 slots' auras: built when a slot gets a type, rebuilt when its type changes, hidden (no cost) when the auras
/// are off, the phone is destroyed or there's no level.
internal sealed class AuraStack
{
    internal readonly Look Look;
    internal readonly Events Events = new();
    readonly Aura[] auras = new Aura[3];
    GameObject root;
    float time;
    NV3 lastCenter;
    bool hasLast;

    internal int Particles
    {
        get
        {
            int n = 0;
            foreach (var a in auras) if (a != null) n += a.Particles;
            return n;
        }
    }

    public AuraStack(Look look) { Look = look; }

    /// Every frame, in LateUpdate (after the game moved Flippy). Returns whether anything was drawn.
    public bool Tick(EHS.PlayerRef player, float dt)
    {
        if (player == null) { Hide(); hasLast = false; return false; }
        Events.Tick(player, dt);
        if (!Plugin.Enabled.Value || Events.Destroyed || !Glow.Ready) { Hide(); return false; }
        if (root == null)
        {
            root = new GameObject("FlippyAuras auras"); // layer 0 (Default): the game's camera and Tripod Cam's both draw it
            Object.DontDestroyOnLoad(root);
        }
        if (!root.activeSelf) root.SetActive(true);
        time += dt;
        var f = new Frame
        {
            Center = Events.Center, Dt = dt, Time = time,
            Jump = Events.Jump, Jumped = Events.Jumped, Fall = Events.Fall, Still = Events.Still,
            Landed = Events.Landed, LandPower = Events.LandPower,
            Teleported = hasLast && NV3.DistanceSquared(Events.Center, lastCenter) > 36f, // a respawn, not a move
        };
        lastCenter = Events.Center;
        hasLast = true;
        for (int k = 0; k < 3; k++)
        {
            var slot = Look.Slots[k];
            bool want = slot.On && slot.Type != AuraType.Image;
            if (auras[k] != null && (!want || auras[k].Type != slot.Type)) { auras[k].Destroy(); auras[k] = null; }
            if (want && auras[k] == null) auras[k] = Auras.Create(slot, root.transform);
            auras[k]?.Tick(f);
        }
        return true;
    }

    void Hide()
    {
        if (root != null && root.activeSelf) root.SetActive(false);
    }
}
