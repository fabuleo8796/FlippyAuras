using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FlippyAuras;

internal enum AuraType { Lightning, Ghosts, Orbiters, Flames, Sparkles, Halo, Image }

/// A colour in plain floats (0..1). The auras' maths stays in managed code: every UnityEngine.Color or Mathf call
/// goes through the IL2CPP bridge, which adds up fast with hundreds of particles a frame.
internal struct Col
{
    public float r, g, b;

    public Col(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }

    public static Col Lerp(Col a, Col b, float t) => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);

    public UnityEngine.Color32 ToColor32(float alpha) => new UnityEngine.Color32
    {
        r = Byte(r), g = Byte(g), b = Byte(b), a = Byte(alpha),
    };

    public UnityEngine.Color ToColor(float alpha = 1f) => new UnityEngine.Color { r = r, g = g, b = b, a = alpha };

    static byte Byte(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));

    public string Hex => $"#{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}";

    public static Col FromHex(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return new Col(1f, 1f, 1f);
        return new Col((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f);
    }

    public static Col FromHsv(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        int i = (int)h;
        float f = h - i, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
        return i switch
        {
            0 => new Col(v, t, p), 1 => new Col(q, v, p), 2 => new Col(p, v, t),
            3 => new Col(p, q, v), 4 => new Col(t, p, v), _ => new Col(v, p, q),
        };
    }

    public void ToHsv(out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), d = max - min;
        v = max;
        s = max > 0f ? d / max : 0f;
        if (d <= 0f) { h = 0f; return; }
        if (max == r) h = (g - b) / d / 6f;
        else if (max == g) h = ((b - r) / d + 2f) / 6f;
        else h = ((r - g) / d + 4f) / 6f;
        if (h < 0f) h += 1f;
    }
}

/// One of the 3 aura slots: what it is, its colours (1 to 3 gradient stops) and its sliders. The auras read it every
/// frame, so the panel's changes show at once.
internal sealed class Slot
{
    public bool On = true;
    public AuraType Type = AuraType.Orbiters;
    public int Stops = 2;
    public readonly Col[] Colors = { Col.FromHex("#FF5CD6"), Col.FromHex("#3DE8FF"), Col.FromHex("#FFFFFF") };
    public float Glow = 3f, Size = 1f, Speed = 1f, Amount = 0.5f, Radius = 1f;
    public int Shape, Rings = 1;
    public bool Jump = true, Fall = true, Land = true, Still = true;

    /// Along the gradient: 0 = first stop, 1 = last.
    public Col Sample(float t)
    {
        t = t <= 0f ? 0f : t >= 1f ? 1f : t;
        if (Stops <= 1) return Colors[0];
        if (Stops == 2) return Col.Lerp(Colors[0], Colors[1], t);
        return t < 0.5f ? Col.Lerp(Colors[0], Colors[1], t * 2f) : Col.Lerp(Colors[1], Colors[2], t * 2f - 1f);
    }

    public void CopyFrom(Slot o)
    {
        On = o.On; Type = o.Type; Stops = o.Stops;
        for (int i = 0; i < 3; i++) Colors[i] = o.Colors[i];
        Glow = o.Glow; Size = o.Size; Speed = o.Speed; Amount = o.Amount; Radius = o.Radius;
        Shape = o.Shape; Rings = o.Rings;
        Jump = o.Jump; Fall = o.Fall; Land = o.Land; Still = o.Still;
    }

    public Slot With(AuraType type, string c0, string c1 = null, string c2 = null, float glow = 3f, float amount = 0.5f)
    {
        On = true; Type = type; Glow = glow; Amount = amount;
        Colors[0] = Col.FromHex(c0);
        Colors[1] = Col.FromHex(c1 ?? c0);
        Colors[2] = Col.FromHex(c2 ?? c1 ?? c0);
        Stops = c2 != null ? 3 : c1 != null ? 2 : 1;
        return this;
    }
}

/// The 3 slots together: what the panel edits and what presets save.
internal sealed class Look
{
    public string Name = "";
    public readonly Slot[] Slots = { new(), new(), new() };

    public void CopyFrom(Look o)
    {
        Name = o.Name;
        for (int i = 0; i < 3; i++) Slots[i].CopyFrom(o.Slots[i]);
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("name", Name);
            w.WriteStartArray("auras");
            foreach (var s in Slots)
            {
                w.WriteStartObject();
                w.WriteBoolean("on", s.On);
                w.WriteString("type", s.Type.ToString());
                w.WriteNumber("stops", s.Stops);
                w.WriteStartArray("colors");
                foreach (var c in s.Colors) w.WriteStringValue(c.Hex);
                w.WriteEndArray();
                w.WriteNumber("glow", Round(s.Glow));
                w.WriteNumber("size", Round(s.Size));
                w.WriteNumber("speed", Round(s.Speed));
                w.WriteNumber("amount", Round(s.Amount));
                w.WriteNumber("radius", Round(s.Radius));
                w.WriteNumber("shape", s.Shape);
                w.WriteNumber("rings", s.Rings);
                w.WriteBoolean("onJump", s.Jump);
                w.WriteBoolean("onFall", s.Fall);
                w.WriteBoolean("onLanding", s.Land);
                w.WriteBoolean("calmWhenStill", s.Still);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static double Round(float v) => Math.Round(v, 3);

    /// Reads what ToJson wrote; anything missing or broken keeps its default.
    public static Look FromJson(string json)
    {
        var look = new Look();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) look.Name = name.GetString();
        if (!root.TryGetProperty("auras", out var auras) || auras.ValueKind != JsonValueKind.Array) return look;
        int i = 0;
        foreach (var a in auras.EnumerateArray())
        {
            if (i >= 3) break;
            var s = look.Slots[i++];
            s.On = Bool(a, "on", s.On);
            if (a.TryGetProperty("type", out var type) && Enum.TryParse(type.GetString(), out AuraType t)) s.Type = t;
            s.Stops = Math.Clamp((int)Num(a, "stops", s.Stops), 1, 3);
            if (a.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Array)
            {
                int k = 0;
                foreach (var c in colors.EnumerateArray())
                    if (k < 3 && c.ValueKind == JsonValueKind.String) s.Colors[k++] = Col.FromHex(c.GetString());
            }
            s.Glow = Math.Clamp(Num(a, "glow", s.Glow), 0f, 8f);
            s.Size = Math.Clamp(Num(a, "size", s.Size), 0.25f, 3f);
            s.Speed = Math.Clamp(Num(a, "speed", s.Speed), 0f, 3f);
            s.Amount = Math.Clamp(Num(a, "amount", s.Amount), 0f, 1f);
            s.Radius = Math.Clamp(Num(a, "radius", s.Radius), 0.4f, 2.5f);
            s.Shape = Math.Clamp((int)Num(a, "shape", s.Shape), 0, Auras.Shapes.Length - 1);
            s.Rings = Math.Clamp((int)Num(a, "rings", s.Rings), 1, 3);
            s.Jump = Bool(a, "onJump", s.Jump);
            s.Fall = Bool(a, "onFall", s.Fall);
            s.Land = Bool(a, "onLanding", s.Land);
            s.Still = Bool(a, "calmWhenStill", s.Still);
        }
        return look;
    }

    static float Num(JsonElement e, string key, float fallback) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    static bool Bool(JsonElement e, string key, bool fallback) =>
        e.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
}

/// Built-in looks, saved looks (BepInEx\config\FlippyAuras\presets\*.json) and the look in use (current.json, saved a
/// moment after each change).
internal static class Presets
{
    internal static readonly List<Look> BuiltIn = new();
    internal static readonly List<Look> Saved = new();
    static float saveAt = -1f;

    static string PresetFolder => Path.Combine(Plugin.ConfigFolder, "presets");
    static string CurrentFile => Path.Combine(Plugin.ConfigFolder, "current.json");

    static Presets()
    {
        BuiltIn.Add(Make("Storm", l =>
        {
            l.Slots[0].With(AuraType.Lightning, "#BFF4FF", "#3D7BFF", glow: 5f, amount: 0.6f);
            l.Slots[1].With(AuraType.Sparkles, "#FFFFFF", "#7FE8FF", glow: 3f, amount: 0.35f);
            l.Slots[2].With(AuraType.Halo, "#7FE8FF").On = false;
        }));
        BuiltIn.Add(Make("Haunted", l =>
        {
            l.Slots[0].With(AuraType.Ghosts, "#E2CCFF", "#9B6BFF", glow: 1.4f, amount: 0.45f);
            l.Slots[1].With(AuraType.Orbiters, "#B26BFF", "#5A2DFF", glow: 2.5f, amount: 0.35f).Shape = 2;
            l.Slots[2].With(AuraType.Sparkles, "#C9A6FF").On = false;
        }));
        BuiltIn.Add(Make("Royal", l =>
        {
            l.Slots[0].With(AuraType.Halo, "#FFE7A3", "#FFC23D", glow: 3.5f, amount: 0f);
            var orbit = l.Slots[1].With(AuraType.Orbiters, "#FFD36B", "#FFF6D6", glow: 3f, amount: 0.45f);
            orbit.Rings = 2;
            l.Slots[2].With(AuraType.Sparkles, "#FFE7A3", "#FFFFFF", glow: 2.5f, amount: 0.25f);
        }));
        BuiltIn.Add(Make("Inferno", l =>
        {
            l.Slots[0].With(AuraType.Flames, "#FFF27A", "#FF8A1F", "#FF2A12", glow: 3.5f, amount: 0.6f);
            l.Slots[1].With(AuraType.Lightning, "#FFB347", "#FF4A1F", glow: 4f, amount: 0.2f);
            l.Slots[2].With(AuraType.Sparkles, "#FFD36B").On = false;
        }));
        BuiltIn.Add(Make("Fairy", l =>
        {
            var orbit = l.Slots[0].With(AuraType.Orbiters, "#FF5CD6", "#3DE8FF", glow: 3f, amount: 0.5f);
            orbit.Shape = 1;
            l.Slots[1].With(AuraType.Sparkles, "#FFB8F0", "#FFFFFF", glow: 3f, amount: 0.5f);
            l.Slots[2].With(AuraType.Halo, "#FFB8F0", "#B8F4FF", glow: 2.5f, amount: 0f).On = false;
        }));
    }

    static Look Make(string name, Action<Look> fill)
    {
        var look = new Look { Name = name };
        fill(look);
        return look;
    }

    /// The look from last time, or Fairy on a first run.
    internal static Look LoadCurrent()
    {
        try
        {
            if (File.Exists(CurrentFile)) return Look.FromJson(File.ReadAllText(CurrentFile));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("couldn't read current.json, starting fresh: " + e.Message);
        }
        var look = new Look();
        look.CopyFrom(BuiltIn[4]);
        look.Name = "";
        return look;
    }

    /// Saves the current look a second after the last change (sliders change it every frame while dragged).
    internal static void Changed() => saveAt = UnityEngine.Time.unscaledTime + 1f;

    internal static void Tick(Look current)
    {
        if (saveAt < 0f || UnityEngine.Time.unscaledTime < saveAt) return;
        SaveCurrentNow(current);
    }

    internal static void SaveCurrentNow(Look current)
    {
        if (saveAt < 0f) return;
        saveAt = -1f;
        try
        {
            Directory.CreateDirectory(Plugin.ConfigFolder);
            File.WriteAllText(CurrentFile, current.ToJson());
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("couldn't save current.json: " + e.Message);
        }
    }

    internal static void LoadSaved()
    {
        Saved.Clear();
        try
        {
            if (!Directory.Exists(PresetFolder)) return;
            var files = Directory.GetFiles(PresetFolder, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    var look = Look.FromJson(File.ReadAllText(file));
                    look.Name = Path.GetFileNameWithoutExtension(file);
                    Saved.Add(look);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"skipped preset {Path.GetFileName(file)}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("couldn't list presets: " + e.Message);
        }
    }

    /// Saves the look as "My look N" (the first free N) and returns that name.
    internal static string SaveAs(Look current)
    {
        Directory.CreateDirectory(PresetFolder);
        int n = 1;
        while (File.Exists(Path.Combine(PresetFolder, $"My look {n}.json"))) n++;
        var name = $"My look {n}";
        var copy = new Look();
        copy.CopyFrom(current);
        copy.Name = name;
        File.WriteAllText(Path.Combine(PresetFolder, name + ".json"), copy.ToJson());
        LoadSaved();
        return name;
    }

    internal static void Delete(string name)
    {
        try
        {
            var path = Path.Combine(PresetFolder, name + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"couldn't delete preset {name}: {e.Message}");
        }
        LoadSaved();
    }

    // ----- randomize -----

    static readonly AuraType[] Pickable = { AuraType.Lightning, AuraType.Ghosts, AuraType.Orbiters, AuraType.Flames, AuraType.Sparkles, AuraType.Halo };

    /// 1 to 3 different auras in colours that go together (nearby hues, sometimes a contrast).
    internal static void Randomize(Look look, Random rnd)
    {
        int count = 1 + rnd.Next(3);
        var used = new List<AuraType>();
        float baseHue = (float)rnd.NextDouble();
        for (int i = 0; i < 3; i++)
        {
            var s = look.Slots[i];
            AuraType type;
            do type = Pickable[rnd.Next(Pickable.Length)]; while (used.Contains(type));
            used.Add(type);
            s.On = i < count;
            Randomize(s, type, rnd, baseHue + i * 0.08f);
        }
        look.Name = "";
    }

    internal static void Randomize(Slot s, AuraType type, Random rnd, float hue)
    {
        s.Type = type;
        s.Stops = 1 + rnd.Next(3);
        float spread = rnd.NextDouble() < 0.3 ? 0.5f : 0.06f + (float)rnd.NextDouble() * 0.12f;
        for (int k = 0; k < 3; k++)
            s.Colors[k] = Col.FromHsv(hue + spread * k, 0.35f + (float)rnd.NextDouble() * 0.6f, 0.85f + (float)rnd.NextDouble() * 0.15f);
        s.Glow = type == AuraType.Ghosts ? 1f + (float)rnd.NextDouble() : 2f + (float)rnd.NextDouble() * 3f;
        s.Size = 0.7f + (float)rnd.NextDouble() * 0.7f;
        s.Speed = 0.6f + (float)rnd.NextDouble() * 0.9f;
        s.Amount = 0.2f + (float)rnd.NextDouble() * 0.6f;
        s.Radius = 0.8f + (float)rnd.NextDouble() * 0.5f;
        s.Shape = rnd.Next(Auras.Shapes.Length);
        s.Rings = 1 + rnd.Next(3);
    }
}
