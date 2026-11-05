using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlippyAurasProbe;

/// The report's sections. Every one is wrapped by Section(), so one that throws only costs its own lines.
static class Report
{
    static string F(float v) => v.ToString("0.###", ProbeDriver.Inv);
    static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    internal static void Section(StringBuilder r, string title, Action body)
    {
        r.AppendLine();
        r.AppendLine("## " + title);
        try { body(); }
        catch (Exception e) { r.AppendLine("(failed: " + e.Message + ")"); }
    }

    /// Where an aura centres on Flippy: the middle of his bounding box renderer when it has a size, else his visuals
    /// root, else the player object. size = the box's diagonal.
    internal static Vector3 Center(EHS.PlayerRef p, out float size)
    {
        var visuals = p.playerVisualsHandler;
        var box = visuals != null ? visuals.boundingBoxMeshRenderer : null;
        if (box != null)
        {
            var b = box.bounds;
            if (b.size.sqrMagnitude > 0.01f) { size = b.size.magnitude; return b.center; }
        }
        size = 2f;
        var root = visuals != null ? visuals.playerVisualT : null;
        return root != null ? root.position : p.transform.position;
    }

    internal static void Player(StringBuilder r, EHS.PlayerRef p)
    {
        if (p == null) { r.AppendLine("no local player"); return; }
        var t = p.transform;
        r.AppendLine($"player '{p.name}' at {V(t.position)}, rotation {V(t.eulerAngles)}, scale {V(t.lossyScale)}, layer {LayerMask.LayerToName(p.gameObject.layer)}");
        var rb = p.rb;
        if (rb != null) r.AppendLine($"rigidbody at {V(rb.position)}, velocity {V(rb.linearVelocity)}, mass {F(rb.mass)}");
        var visuals = p.playerVisualsHandler;
        var root = visuals != null ? visuals.playerVisualT : null;
        if (root != null) r.AppendLine($"visuals root '{root.name}' at {V(root.position)} (player + {V(root.position - t.position)}), rotation {V(root.eulerAngles)}, scale {V(root.lossyScale)}");
        var box = visuals != null ? visuals.boundingBoxMeshRenderer : null;
        if (box != null)
        {
            var b = box.bounds;
            r.AppendLine($"bounding box renderer '{box.name}': enabled {box.enabled}, active {box.gameObject.activeInHierarchy}, world bounds centre {V(b.center)} (player + {V(b.center - t.position)}), size {V(b.size)}, layer {LayerMask.LayerToName(box.gameObject.layer)}");
            var mf = box.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh != null) r.AppendLine($"  its mesh '{mesh.name}': local bounds centre {V(mesh.bounds.center)}, size {V(mesh.bounds.size)}, box scale {V(box.transform.lossyScale)}");
        }
        var target = p.cameraTargetT;
        if (target != null) r.AppendLine($"camera target '{target.name}' at {V(target.position)} (player + {V(target.position - t.position)})");
        var center = Center(p, out float size);
        r.AppendLine($"aura centre used: {V(center)} (player + {V(center - t.position)}), size {F(size)}");
        var fall = p.playerFallData;
        if (fall != null) r.AppendLine($"fall data: grounded {fall.isGrounded}, active fall {F(fall.ActiveFallDistance)} m over {F(fall.ActiveFallTime)} s, last fall {F(fall.lastFallDistance)} m");
        var jump = p.movementJump;
        if (jump != null) r.AppendLine($"jump: can jump {jump.CanJump}, last jump at {F(jump.LastJumpTime)} (game time now {F(Time.time)})");
        var phone = p.destroyedPhoneEffectHandler;
        if (phone != null) r.AppendLine($"destroyed: {phone.IsDestroyed()}");

        r.AppendLine();
        r.AppendLine("Everything under the player (depth ≤ 5): name [layer] active, renderer: enabled / world size / shader");
        int lines = 0;
        Walk(r, t, 0, ref lines);
    }

    static void Walk(StringBuilder r, Transform t, int depth, ref int lines)
    {
        if (lines >= 160) return;
        lines++;
        var go = t.gameObject;
        var sb = new StringBuilder();
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append($" [{LayerMask.LayerToName(go.layer)}]");
        if (!go.activeSelf) sb.Append(" (off)");
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            var m = renderer.sharedMaterial;
            sb.Append($"  {renderer.GetIl2CppType().Name} {(renderer.enabled ? "on" : "off")} size {V(renderer.bounds.size)} shader '{(m != null && m.shader != null ? m.shader.name : "-")}'");
        }
        r.AppendLine(sb.ToString());
        if (depth >= 5) return;
        for (int i = 0; i < t.childCount; i++) Walk(r, t.GetChild(i), depth + 1, ref lines);
    }

    internal static void Layers(StringBuilder r)
    {
        for (int i = 0; i < 32; i++)
        {
            string n = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(n)) r.AppendLine($"{i}: {n}");
        }
    }

    static string Mask(int mask)
    {
        var names = new List<string>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) names.Add(string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? i.ToString() : LayerMask.LayerToName(i));
        return mask == -1 ? "everything" : string.Join(", ", names);
    }

    internal static void Cameras(StringBuilder r)
    {
        foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<Camera>()))
        {
            var c = o.TryCast<Camera>();
            if (c == null) continue;
            r.AppendLine($"'{c.name}' (parent '{(c.transform.parent != null ? c.transform.parent.name : "-")}'): enabled {c.enabled}, depth {F(c.depth)}, " +
                         $"target {(c.targetTexture != null ? c.targetTexture.name : "screen")}, fov {F(c.fieldOfView)}, draws: {Mask(c.cullingMask)}");
        }
    }

    internal static void Effects(StringBuilder r)
    {
        var systems = Object.FindObjectsOfType(Il2CppType.Of<ParticleSystem>());
        r.AppendLine($"{systems.Length} particle systems playing in the scene");
        var byShader = new Dictionary<string, int>();
        int listed = 0;
        foreach (var o in systems)
        {
            var ps = o.TryCast<ParticleSystem>();
            if (ps == null) continue;
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            var m = psr != null ? psr.sharedMaterial : null;
            string shader = m != null && m.shader != null ? m.shader.name : "-";
            byShader[shader] = byShader.TryGetValue(shader, out int n) ? n + 1 : 1;
            if (listed++ < 30)
            {
                var main = ps.main;
                r.AppendLine($"  '{ps.name}' (parent '{(ps.transform.parent != null ? ps.transform.parent.name : "-")}') [{LayerMask.LayerToName(ps.gameObject.layer)}]: " +
                             $"{(psr != null ? psr.renderMode.ToString() : "?")}, {main.simulationSpace}, max {main.maxParticles}, material '{(m != null ? m.name : "-")}', shader '{shader}'");
            }
        }
        r.AppendLine("by shader: " + string.Join("; ", byShader.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} ×{kv.Value}")));
        foreach (var (type, label) in new[] { (Il2CppType.Of<LineRenderer>(), "line renderers"), (Il2CppType.Of<TrailRenderer>(), "trail renderers") })
        {
            var found = Object.FindObjectsOfType(type);
            var shaders = new HashSet<string>();
            foreach (var o in found)
            {
                var rend = o.TryCast<Renderer>();
                var m = rend != null ? rend.sharedMaterial : null;
                if (m != null && m.shader != null) shaders.Add(m.shader.name);
            }
            r.AppendLine($"{found.Length} {label}; shaders: {string.Join(", ", shaders)}");
        }
    }

    internal static void Shaders(StringBuilder r)
    {
        r.AppendLine("our glow shader: " + Glow.State);
        foreach (var name in new[] { "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Unlit", "Sprites/Default",
                                     "Legacy Shaders/Particles/Alpha Blended Premultiply", "Particles/Standard Unlit", "Shader Graphs/SG_VFX_LightningParticles" })
        {
            var s = Shader.Find(name);
            r.AppendLine($"{name}: {(s == null ? "not found" : $"found, supported {s.isSupported}")}");
        }
    }
}
