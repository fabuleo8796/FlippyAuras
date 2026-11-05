using System;
using System.Diagnostics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace FlippyAuras;

/// Runs everything: keys (J panel, Shift+J on/off), the shader load and panel build a few seconds after launch, the
/// auras each frame after the game has moved Flippy, their cost, and Preview 360° (the game's camera circles Flippy,
/// with the Cinemachine brain paused the way Tripod Cam does it, and handed back after).
public class AuraDriver : MonoBehaviour
{
    public AuraDriver(IntPtr ptr) : base(ptr) { }

    internal static AuraDriver Instance;
    readonly Stopwatch watch = new();
    AuraStack stack;
    Look look;
    int errors;
    long ticks;
    int ticked;
    float costAt;
    bool panelBuilt;

    internal double CostMs;
    internal int Particles;
    internal bool Drawing;

    // Preview 360°
    internal bool Previewing;
    Camera previewCam;
    CinemachineBrain brain;
    bool brainWasOn;
    float previewAngle, previewFov;
    EHS.CameraManager manager;

    void Awake()
    {
        Instance = this;
        look = Presets.LoadCurrent();
        stack = new AuraStack(look);
    }

    void Update()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (now > 3f) Glow.Load();
            if (!panelBuilt && now > 6f && Glow.Ready)
            {
                panelBuilt = true;
                Panel.Build(look); // ahead of time: the first open stays instant (kit trap 4)
            }
            var kb = Keyboard.current;
            GameControls.Tick(kb, Mouse.current);
            Ui.Tick();
            Presets.Tick(look);
            if (Panel.IsOpen) Panel.Tick(kb);
            if (kb == null) return;
            if (kb[Plugin.PanelKey.Value].wasPressedThisFrame)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (shift)
                {
                    Plugin.Enabled.Value = !Plugin.Enabled.Value;
                    Ui.Toast(Plugin.Enabled.Value ? "Auras on" : "Auras off");
                    if (Panel.IsOpen) Panel.Refresh();
                }
                else if (Panel.IsOpen) Panel.Close();
                else if (Cursor.lockState == CursorLockMode.Locked) OpenPanel(); // playing, not in a menu or chat
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("update: " + e);
        }
    }

    [HideFromIl2Cpp]
    void OpenPanel()
    {
        if (!panelBuilt)
        {
            if (!Glow.Ready) { Ui.Toast("Flippy Auras is still loading, try again in a moment"); return; }
            panelBuilt = true;
            Panel.Build(look);
        }
        Panel.Open();
        if (!Panel.IsOpen) Ui.Toast("The Flippy Auras panel couldn't open (see LogOutput.log)");
    }

    void LateUpdate()
    {
        var player = EHS.PlayerRef.LocalPlayer;
        try
        {
            watch.Restart();
            Drawing = stack.Tick(player, Time.deltaTime);
            if (Drawing)
            {
                ticks += watch.ElapsedTicks;
                ticked++;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("auras: " + e);
            if (++errors >= 5)
            {
                Plugin.Enabled.Value = false;
                Plugin.Logger.LogError("auras switched off after 5 errors (Shift+J turns them back on)");
                Ui.Toast("Flippy Auras hit an error and switched off");
                errors = 0;
            }
        }
        if (Time.unscaledTime >= costAt)
        {
            costAt = Time.unscaledTime + 1f;
            CostMs = ticked > 0 ? ticks * 1000.0 / Stopwatch.Frequency / ticked : 0;
            Particles = stack.Particles;
            ticks = 0;
            ticked = 0;
        }
        try
        {
            if (Previewing) PreviewStep(player);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("preview: " + e);
            StopPreview();
        }
    }

    // ----- Preview 360° -----

    [HideFromIl2Cpp]
    internal void TogglePreview()
    {
        if (Previewing) { StopPreview(); return; }
        var player = EHS.PlayerRef.LocalPlayer;
        manager = FindManager();
        previewCam = manager != null ? manager.cam : null;
        if (player == null || previewCam == null) { Ui.Toast("Get into a level first"); return; }
        brain = previewCam.GetComponent<CinemachineBrain>();
        brainWasOn = brain != null && brain.enabled;
        if (brain != null) brain.enabled = false;
        previewFov = previewCam.fieldOfView;
        var offset = previewCam.transform.position - stack.Events.CenterV3;
        previewAngle = MathF.Atan2(offset.x, offset.z);
        Previewing = true;
    }

    [HideFromIl2Cpp]
    internal void StopPreview()
    {
        if (!Previewing) return;
        Previewing = false;
        if (previewCam != null) previewCam.fieldOfView = previewFov;
        if (brain != null) brain.enabled = brainWasOn; // the brain puts the camera back on Flippy next frame
        brain = null;
        previewCam = null;
        if (Panel.IsOpen) Panel.Refresh();
    }

    /// The camera circles Flippy slowly, a little above him, aimed to his right so he stays clear of the panel.
    [HideFromIl2Cpp]
    void PreviewStep(EHS.PlayerRef player)
    {
        if (player == null || previewCam == null || !Panel.IsOpen) { StopPreview(); return; }
        previewAngle += Time.unscaledDeltaTime * 0.42f; // a full turn in 15 s
        var c = stack.Events.CenterV3;
        float dist = 4.6f;
        var pos = c + new Vector3(MathF.Sin(previewAngle) * dist, 1.4f, MathF.Cos(previewAngle) * dist);
        var toward = c - pos;
        var right = new Vector3(toward.z, 0f, -toward.x).normalized;
        var look = Quaternion.LookRotation((c + right * 1.1f) - pos, Vector3.up);
        previewCam.transform.SetPositionAndRotation(pos, look);
        previewCam.fieldOfView = 60f;
    }

    [HideFromIl2Cpp]
    static EHS.CameraManager FindManager()
    {
        var found = Object.FindObjectsOfType(Il2CppType.Of<EHS.CameraManager>());
        return found != null && found.Length > 0 ? found[0].TryCast<EHS.CameraManager>() : null;
    }

    void OnApplicationQuit()
    {
        try { Presets.SaveCurrentNow(look); } catch { /* quitting anyway */ }
    }
}
