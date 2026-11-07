using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FlippyAuras;

/// Other players' auras through FIH Sync (a separate, optional mod): your look JSON goes to the other modded players
/// in the lobby, and theirs comes back and gets its own AuraStack on their Flippy. Without FIH Sync installed this
/// does nothing; the calls into it sit in NoInlining methods, so a missing FIHSync.dll is one caught exception.
internal static class SyncLink
{
    static readonly Dictionary<int, AuraStack> remote = new();
    static bool linked;

    internal static void Start()
    {
        try { Link(); linked = true; }
        catch (Exception) { Plugin.Logger.LogInfo("FIH Sync isn't installed: other players won't see your auras"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Link() => FIHSync.Share.Register("auras", Version, Data, Received, Gone);

    // the look as JSON is tiny, so it's its own version string; off = nothing to show
    static string Version() => Plugin.Enabled.Value && AuraDriver.Instance != null ? AuraDriver.Instance.Look.ToJson() : null;

    static byte[] Data() => Encoding.UTF8.GetBytes(Version() ?? "");

    static void Received(int ownerId, byte[] data, string folder)
    {
        Gone(ownerId);
        var look = Look.FromJson(Encoding.UTF8.GetString(data));
        remote[ownerId] = new AuraStack(look);
        Plugin.Logger.LogInfo($"showing player {ownerId}'s auras ({look.Name})");
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
            kv.Value.Tick(p, dt);
        }
    }
}
