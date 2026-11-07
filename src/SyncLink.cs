using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace FlippyAuras;

/// Other players' auras through FIH Sync (a separate, optional mod): your look JSON goes to the other modded players
/// in the lobby, and theirs comes back and gets its own AuraStack on their Flippy. Without FIH Sync installed this
/// does nothing; the calls into it sit in NoInlining methods, so a missing FIHSync.dll is one caught exception.
/// Other players' sliders are clamped to the panel's ranges, and the frame time of their auras goes to FIH Sync's lag
/// shield.
internal static class SyncLink
{
    static readonly Dictionary<int, AuraStack> remote = new();
    static readonly Stopwatch watch = new();
    static bool linked;

    internal static void Start()
    {
        try { Link(); linked = true; }
        catch (Exception) { Plugin.Logger.LogInfo("FIH Sync isn't installed: other players won't see your auras"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Link() => FIHSync.Share.Register("auras", Version, Data, Received, Gone);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ReportCost(int owner, double ms) => FIHSync.Share.ReportCost(owner, ms);

    // the look as JSON is tiny, so it's its own version string; off = nothing to show
    static string Version() => Plugin.Enabled.Value && AuraDriver.Instance != null ? AuraDriver.Instance.Look.ToJson() : null;

    static byte[] Data() => Encoding.UTF8.GetBytes(Version() ?? "");

    static void Received(int ownerId, byte[] data, string folder)
    {
        Gone(ownerId);
        var look = Look.FromJson(Encoding.UTF8.GetString(data));
        foreach (var s in look.Slots) Clamp(s);
        remote[ownerId] = new AuraStack(look);
        Plugin.Logger.LogInfo($"showing player {ownerId}'s auras ({look.Name})");
    }

    /// Another player's sliders, kept inside the panel's own ranges (nobody gets to send Amount 9999 or NaN).
    static void Clamp(Slot s)
    {
        static float C(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        if (!Enum.IsDefined(typeof(AuraType), s.Type)) s.On = false;
        s.Glow = C(s.Glow, 0f, 8f, 3f);
        s.Size = C(s.Size, 0.25f, 3f, 1f);
        s.Speed = C(s.Speed, 0f, 3f, 1f);
        s.Amount = C(s.Amount, 0f, 1f, 0.5f);
        s.Radius = C(s.Radius, 0.4f, 2.5f, 1f);
        s.Stops = Math.Clamp(s.Stops, 1, 3);
        s.Shape = Math.Clamp(s.Shape, 0, Auras.ShapeNames.Length - 1);
        s.Rings = Math.Clamp(s.Rings, 1, 3);
        for (int i = 0; i < s.Colors.Length; i++)
        {
            var c = s.Colors[i];
            s.Colors[i] = new Col(C(c.r, 0f, 1f, 1f), C(c.g, 0f, 1f, 1f), C(c.b, 0f, 1f, 1f));
        }
    }

    static void Gone(int ownerId)
    {
        if (!remote.TryGetValue(ownerId, out var stack)) return;
        stack.Destroy();
        remote.Remove(ownerId);
    }

    /// Every frame after the local auras. A player who isn't spawned (loading, between levels) just hides theirs.
    internal static void Tick(float dt)
    {
        if (!linked || remote.Count == 0) return;
        foreach (var kv in remote)
        {
            EHS.PlayerRef p = null;
            try { if (!EHS.PlayerRef.TryGetConnectedPlayerByClientId(kv.Key, out p)) p = null; }
            catch { p = null; }
            if (p != null && EHS.PlayerRef.IsLocalPlayer(p)) p = null;
            watch.Restart();
            kv.Value.Tick(p, dt);
            try { ReportCost(kv.Key, watch.Elapsed.TotalMilliseconds); } catch { /* FIH Sync gone */ }
        }
    }
}
