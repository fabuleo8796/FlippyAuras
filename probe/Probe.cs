// FlippyAurasProbe: Flippy Auras' M0 recon plugin. It writes down what can't be read from the game's files, into
// BepInEx\FlippyAurasProbe_*.txt, 5 s after you're in a level (and again in each new level):
// - Flippy: his transforms, the visuals root, the bounding box renderer and its size, the camera target, everything
//   under him (layers, renderers, shaders);
// - the layers and every camera's culling mask (so auras render in the player's view and Tripod Cam's tripod, never
//   in UI cameras);
// - the game's own particle effects, line and trail renderers (which shaders they use);
// - the jump / landing / fall speed / destroyed / repaired events it saw.
// Keys:
// - F9: test auras on/off (halo, orbiting stars, lightning, ghosts, sparkle trail, a trail ribbon). They follow
//   Flippy upright, pulse on jumps and hide while the phone is destroyed. Their cost is logged every 10 s.
// - Shift+F9: a fresh report now.
using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FlippyAurasProbe;

[BepInPlugin(Guid, "Flippy Auras Probe", "0.0.1")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.fabuleo8796.flippyaurasprobe";
    internal static ManualLogSource Logger;
    internal static string Folder;

    public override void Load()
    {
        Logger = Log;
        Folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        if (string.IsNullOrEmpty(Folder)) Folder = Path.Combine(Paths.PluginPath, "FlippyAuras");
        ClassInjector.RegisterTypeInIl2Cpp<ProbeDriver>();
        var host = new GameObject("FlippyAurasProbe");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<ProbeDriver>();
        Log.LogInfo("Flippy Auras Probe loaded: a report 5 s into each level (BepInEx\\FlippyAurasProbe_*.txt). F9 test auras on/off, Shift+F9 a new report.");
    }

    internal static string Out(string file) => Path.Combine(Paths.BepInExRootPath, file);
}

public class ProbeDriver : MonoBehaviour
{
    public ProbeDriver(IntPtr ptr) : base(ptr) { }

    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    float readySince = -1f;
    string reportedScene;
    int snapshots, errors;
    readonly Events events = new();
    readonly TestAuras auras = new();

    void Update()
    {
        try
        {
            if (Time.realtimeSinceStartup > 3f) Glow.Load();
            var player = EHS.PlayerRef.LocalPlayer;
            if (player == null)
            {
                readySince = -1f;
                auras.Hide();
                return;
            }
            if (readySince < 0f) readySince = Time.unscaledTime;
            events.Tick(player);
            string scene = SceneManager.GetActiveScene().name;
            if (reportedScene != scene && Time.unscaledTime - readySince > 5f)
            {
                reportedScene = scene;
                WriteReport("level_" + scene.Replace(' ', '_'));
            }
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
            {
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) WriteReport("snapshot" + (++snapshots));
                else auras.Toggle();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("probe update: " + e);
        }
    }

    void LateUpdate()
    {
        try
        {
            var player = EHS.PlayerRef.LocalPlayer;
            if (auras.On && player != null) auras.Tick(player, events);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("test auras: " + e);
            if (++errors >= 5)
            {
                auras.Hide();
                Plugin.Logger.LogError("test auras switched off after 5 errors");
            }
        }
    }

    [HideFromIl2Cpp]
    void WriteReport(string when)
    {
        var r = new System.Text.StringBuilder();
        r.AppendLine($"FlippyAurasProbe report ({when}), {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene '{SceneManager.GetActiveScene().name}'");
        Report.Section(r, "Flippy", () => Report.Player(r, EHS.PlayerRef.LocalPlayer));
        Report.Section(r, "Layers", () => Report.Layers(r));
        Report.Section(r, "Cameras", () => Report.Cameras(r));
        Report.Section(r, "The game's particle effects, lines and trails", () => Report.Effects(r));
        Report.Section(r, "Shaders", () => Report.Shaders(r));
        Report.Section(r, "Events seen so far", () => r.Append(events.Log));
        Report.Section(r, "Test auras", () => r.AppendLine(auras.Status));
        string path = Plugin.Out($"FlippyAurasProbe_{when}.txt");
        File.WriteAllText(path, r.ToString());
        Plugin.Logger.LogInfo("Flippy Auras Probe report written: " + path);
    }
}

/// The game events auras react to, read each frame from the local player (no patches):
/// jump = movementJump.LastJumpTime changes; leaving / touching the ground = playerFallData.isGrounded, with
/// lastFallDistance on landing; fall speed = the rigidbody's downward velocity; destroyed / repaired =
/// destroyedPhoneEffectHandler.IsDestroyed().
class Events
{
    float lastJump = -1f, lowestVy;
    bool grounded, groundedKnown, destroyedKnown;
    int logged, jumps, landings, destroys, repairs;
    internal readonly System.Text.StringBuilder Log = new();
    internal float JumpPulse, FallSpeed;
    internal bool Destroyed;

    public void Tick(EHS.PlayerRef p)
    {
        var jump = p.movementJump;
        if (jump != null)
        {
            float t = jump.LastJumpTime;
            if (lastJump >= 0f && t != lastJump) { jumps++; JumpPulse = 1f; Note($"jump (#{jumps})"); }
            lastJump = t;
        }
        var rb = p.rb;
        float vy = rb != null ? rb.linearVelocity.y : 0f;
        FallSpeed = Mathf.Max(0f, -vy);
        if (vy < lowestVy) lowestVy = vy;
        var fall = p.playerFallData;
        if (fall != null)
        {
            bool g = fall.isGrounded;
            if (groundedKnown && g != grounded)
            {
                if (g) { landings++; Note($"landed (#{landings}): fall {fall.lastFallDistance.ToString("0.0", ProbeDriver.Inv)} m, fastest fall {(-lowestVy).ToString("0.0", ProbeDriver.Inv)} m/s"); }
                else { lowestVy = 0f; Note("left the ground"); }
            }
            grounded = g;
            groundedKnown = true;
        }
        var phone = p.destroyedPhoneEffectHandler;
        if (phone != null)
        {
            bool d = phone.IsDestroyed();
            if (destroyedKnown && d != Destroyed)
            {
                if (d) destroys++; else repairs++;
                Note(d ? $"phone destroyed (#{destroys})" : $"phone repaired (#{repairs})");
            }
            Destroyed = d;
            destroyedKnown = true;
        }
        JumpPulse = Mathf.Max(0f, JumpPulse - Time.unscaledDeltaTime * 2.5f);
    }

    void Note(string what)
    {
        string line = $"{Time.unscaledTime.ToString("0.00", ProbeDriver.Inv)} s: {what}";
        if (logged++ < 80) Log.AppendLine(line);
        if (logged <= 40) Plugin.Logger.LogInfo("event: " + line);
    }
}
