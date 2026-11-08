using System;
using UnityEngine;
using NV3 = System.Numerics.Vector3;

namespace FlippyAuras;

/// What the auras react to, read each frame from the local player (no patches, so nothing to clash with other mods):
/// jump = movementJump.LastJumpTime changes; landing = playerFallData.isGrounded turning on, with the fastest fall
/// speed since leaving the ground; fall speed = the rigidbody's downward velocity; destroyed / repaired =
/// destroyedPhoneEffectHandler.IsDestroyed(). All checked in the M0 probe's test run.
internal sealed class Events
{
    float lastJump = -1f, lowestVy, stillFor;
    NV3 last;
    bool hadLast;
    // another player's phone (FIH Sync): the network moves it, and the game may not sync its jump time or ground flag
    bool remote, jumpSeen, groundSeen;
    float smoothVx, smoothVy, smoothVz, prevVy, clock, remoteJumpAt = -9f;
    bool grounded, groundedKnown;
    IntPtr player;
    Transform visual;

    internal NV3 Center;
    internal bool HasCenter, Destroyed, Jumped, Landed;
    internal float Jump, Fall, Still, LandPower, Speed;

    internal Vector3 CenterV3 => new() { x = Center.X, y = Center.Y, z = Center.Z };

    public void Tick(EHS.PlayerRef p, float dt)
    {
        Jumped = Landed = false;
        if (p.Pointer != player)
        {
            player = p.Pointer;
            var visuals = p.playerVisualsHandler;
            visual = visuals != null && visuals.playerVisualT != null ? visuals.playerVisualT : p.transform;
            lastJump = -1f;
            groundedKnown = false;
            HasCenter = false;
            hadLast = false;
            remote = !EHS.PlayerRef.IsLocalPlayer(p);
            jumpSeen = groundSeen = false;
            smoothVx = smoothVy = smoothVz = prevVy = 0f;
        }
        if (visual == null) visual = p.transform;
        var c = visual.position; // the phone's middle (the probe found the visuals root right at it)
        Center = new NV3(c.x, c.y, c.z);
        HasCenter = true;

        var jump = p.movementJump;
        if (jump != null)
        {
            float t = jump.LastJumpTime;
            if (lastJump >= 0f && t != lastJump) { Jumped = true; Jump = 1f; jumpSeen = true; }
            lastJump = t;
        }
        var rb = p.rb;
        Vector3 vel;
        clock += dt;
        if (!remote && rb != null) vel = rb.linearVelocity;
        else
        {
            // another player's phone: its rigidbody's velocity reads 0 here, so measure how fast it moves (smoothed,
            // the network updates it in steps)
            if (hadLast && dt > 0f)
            {
                float k = Math.Min(1f, dt * 12f);
                smoothVx += ((Center.X - last.X) / dt - smoothVx) * k;
                smoothVy += ((Center.Y - last.Y) / dt - smoothVy) * k;
                smoothVz += ((Center.Z - last.Z) / dt - smoothVz) * k;
            }
            vel = new Vector3(smoothVx, smoothVy, smoothVz);
            // a jump: a sudden climb; a landing: a fast fall that stops (only if the game's own signals never came)
            if (!jumpSeen && vel.y > 3f && prevVy <= 3f && clock - remoteJumpAt > 0.35f) { Jumped = true; Jump = 1f; remoteJumpAt = clock; }
            if (!groundSeen && prevVy < -7f && vel.y > -1.5f)
            {
                LandPower = Math.Clamp((-Math.Min(lowestVy, prevVy) - 7f) / 12f, 0f, 1f);
                if (LandPower > 0f) Landed = true;
                lowestVy = 0f;
            }
            prevVy = vel.y;
        }
        last = Center;
        hadLast = true;
        Speed = MathF.Sqrt(vel.x * vel.x + vel.y * vel.y + vel.z * vel.z);
        if (vel.y < lowestVy) lowestVy = vel.y;
        // 5 m/s down is an ordinary hop; from 20 m/s it's all-out
        float fallTarget = Math.Clamp((-vel.y - 5f) / 15f, 0f, 1f);
        Fall += (fallTarget - Fall) * Math.Min(1f, dt * 8f);

        var fall = p.playerFallData;
        if (fall != null)
        {
            bool g = fall.isGrounded;
            if (groundedKnown && g != grounded)
            {
                groundSeen = true;
                if (g)
                {
                    // the test run's ordinary landings peaked at 3-7 m/s; a hard one starts around 8
                    LandPower = Math.Clamp((-lowestVy - 7f) / 12f, 0f, 1f);
                    if (LandPower > 0f) Landed = true;
                }
                lowestVy = 0f;
            }
            grounded = g;
            groundedKnown = true;
        }

        var phone = p.destroyedPhoneEffectHandler;
        Destroyed = phone != null && phone.IsDestroyed();

        stillFor = Speed < 0.4f ? stillFor + dt : 0f;
        float stillTarget = stillFor > 0.6f ? 1f : 0f;
        Still += (stillTarget - Still) * Math.Min(1f, dt * (stillTarget > Still ? 1.5f : 6f));
        Jump = Math.Max(0f, Jump - dt * 2.5f);
    }
}
