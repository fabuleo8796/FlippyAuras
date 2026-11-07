using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace FlippyAuras;

/// Flippy Auras for Flipping is Hard: effects around Flippy (lightning, ghosts, orbiters, flames, sparkles, a halo),
/// up to 3 at once, each with its own colours, size, speed and amount, reacting to jumps, falls and landings.
/// J opens the panel, Shift+J turns the auras on and off. With FIH Sync installed, other modded players see them too.
[BepInPlugin(Guid, "Flippy Auras", "0.2.0")]
[BepInDependency("com.fabuleo8796.fihsync", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.fabuleo8796.flippyauras";
    internal static ManualLogSource Logger;
    internal static string Folder;
    internal static ConfigEntry<Key> PanelKey;
    internal static ConfigEntry<bool> Enabled;

    public override void Load()
    {
        Logger = Log;
        Folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        if (string.IsNullOrEmpty(Folder)) Folder = Path.Combine(Paths.PluginPath, "FlippyAuras");
        PanelKey = Config.Bind("Keys", "Panel", Key.J, "Opens and closes the Flippy Auras panel. Shift + this key turns the auras on and off.");
        Enabled = Config.Bind("Auras", "On", true, "Show the auras (Shift + the panel key switches this in game).");

        ClassInjector.RegisterTypeInIl2Cpp<AuraDriver>();
        var host = new GameObject("FlippyAuras");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<AuraDriver>();
        Log.LogInfo($"Flippy Auras loaded: {PanelKey.Value} opens the panel, Shift+{PanelKey.Value} turns the auras on/off.");
    }

    internal static string ConfigFolder => Path.Combine(Paths.ConfigPath, "FlippyAuras");
}
