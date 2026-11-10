# Flippy Auras (0.2.1)

Effects around Flippy for Flipping is Hard (BepInEx 6 IL2CPP), by Fabuleo8796: lightning, ghosts, orbiting shapes,
flames, sparkles and a halo. You can run up to 3 at once. Each one has its own colours, glow, size, speed, amount and
radius, and they react to what Flippy does.

Everything is client-side, so it's safe in multiplayer. Other players don't see your auras, unless you both run
**[FIH Sync](https://github.com/Fabuleo8796/FIHSync)**: then modded players see each other's auras automatically.
Other players' sliders are kept inside the panel's ranges, and FIH Sync's lag shield hides auras that cost too much.

## The auras

| Aura | What it looks like |
|---|---|
| ⚡ **Lightning** | Jagged electric bolts jumping around Flippy, with side branches and sparks where they land. They get wilder when you fall fast |
| 👻 **Ghosts** | Little see-through ghosts drifting around in lazy loops, bobbing and fading in and out |
| 🌀 **Orbiters** | Stars, hearts, dots, flames or gems circling Flippy on 1–3 tilted rings |
| 🔥 **Flames** | Flames licking up around Flippy, with embers rising. Moving leaves a trail of fire |
| ✨ **Sparkles** | Glitter that trails behind Flippy on the move, and twinkles when standing still |
| 😇 **Halo** | A glowing ring floating above Flippy, up to 3 stacked |

They follow Flippy but always stay upright, so they don't spin when the phone tumbles. They hide while the phone is
destroyed and come back when it's repaired.

**Reactions** (each one can be turned on or off per aura):

- **Jump burst**: a quick puff or burst on every jump.
- **Fall boost**: falling fast makes lightning denser and trails longer.
- **Land ring**: a hard landing sends a ring of light out along the ground.
- **Calm still**: standing still makes the aura slower and calmer.

## Keys

| Key | What |
|---|---|
| **J** | Opens and closes the panel. **Esc** closes it too |
| **Shift + J** | Turns the auras on or off, anywhere |

The key can be changed in `BepInEx\config\com.fabuleo8796.flippyauras.cfg`.

## The panel

It slides in from the right, so Flippy stays in view on the left. Every change shows on the auras right away.

- **Looks**:
  - built-in presets: Storm, Haunted, Royal, Inferno and Fairy;
  - **Randomize** for a surprise combo;
  - **Save** to keep your own look ("My look 1", "My look 2"…). The **×** on a saved look deletes it.
- **Auras**: 3 slots, each with its own on/off dot. Pick a type for the selected slot, or **Surprise** for a random
  one.
- **Colours**: 1 to 3 colours as a gradient. Click a colour dot on the bar to edit it, then pick a swatch or use
  hue, saturation and brightness.
- **Look & motion**: Glow, Size, Speed, Amount and Radius. For Orbiters, pick the shape and the number of rings.
- **Reactions**: the four switches above.
- **Preview 360°**: the camera slowly circles Flippy so you can see the aura from every side.
- At the bottom: how long the auras take each frame (ms) and how many particles are up.

Your look saves by itself, so it's there next time you play. Looks are saved in `BepInEx\config\FlippyAuras\`
(`current.json`, and `presets\*.json` for the saved ones).

**Coming next**: the Image Aura (copies of your own picture floating around Flippy). Its tile is in the panel with a
SOON tag.

## Install

You need Flipping is Hard with BepInEx 6 (IL2CPP).

Download `FlippyAuras-v0.2.1.zip` from the releases and extract it into the game folder (the one with
`Flipping is Hard.exe`). You should end up with `BepInEx\plugins\FlippyAuras\FlippyAuras.dll` and
`flippyauras_shaders` next to it. Both files are needed: the second one is the glow shader.

## Good to know

- **Performance**: about 0.15–0.3 ms a frame with 3 auras. Off costs nothing.
- **Tripod Cam**: the auras show in its recordings. Every particle turns to face whichever camera is drawing it.
- **FIH Ultra**: the glow goes brighter than white, so FIH Ultra's bloom makes it shine.
- **Other mods**: it doesn't patch any game code, so it can't clash with other mods' patches.

If something looks wrong, open an issue with your `BepInEx\LogOutput.log` and a screenshot.

## How it works

- **Its own particles.** The game's build stripped most of Unity's particle system settings (colour and size over
  lifetime, noise, shapes can't be set from a mod). So each aura is one mesh of quads, moved and coloured in code
  every frame and drawn in a single draw call.
- **Its own shader** (`FlippyAuras/Glow`, in `flippyauras_shaders`):
  - premultiplied blending, glowing (additive) or see-through;
  - an HDR intensity for the glow;
  - the quads turn toward the camera in the shader, so every camera sees them face-on.
- **Lightning** uses LineRenderers, which the game kept. The halo and the landing ring are rings of soft dots, so
  they glow evenly from every side and take gradients.
- **Reactions** are read from the local player every frame:
  - jumps from the jump time;
  - landings from the grounded flag;
  - fall speed from the rigidbody;
  - destroyed and repaired from the phone's destroyed state.
- **Speed.** The maths runs in plain .NET (`System.Numerics`) instead of Unity's types, because each Unity call goes
  through the IL2CPP bridge. Buffers are kept and refilled, so nothing is allocated per frame.
- **Textures.** Every shape (dot, star, ghost, heart, flame, spark, gem, ring and the panel icons) is drawn in code
  when the game starts. There are no image files.

## Layout

| Path | What |
|---|---|
| `src/Plugin.cs` | Load, config |
| `src/AuraDriver.cs` | Keys, cost readout, Preview 360° |
| `src/Events.cs` | Jump, landing, fall speed, destroyed: read from the player |
| `src/Auras.cs` | The six auras, the landing ring, and the stack of 3 slots |
| `src/Draw.cs` | `Quads` (camera-facing quad meshes) and `Particles` (the particle pool) |
| `src/Glow.cs` | Loads the shader, makes materials, draws the shape atlas |
| `src/Look.cs` | Slots, colours, presets, Randomize, saving to JSON |
| `src/SyncLink.cs` | Other players' auras through FIH Sync (optional) |
| `lib/FIHSync.dll` | FIH Sync, built against (a soft dependency: Flippy Auras runs without it) |
| `src/Panel.cs` | The J panel |
| `src/Ui.cs` | UI pieces (Geist font, rounded shapes, sliders, switches, toast) |
| `src/GameControls.cs` | Pauses the game's input while the panel is open |
| `src/Resources/` | Geist fonts (OFL), embedded |
| `unity/` | A minimal Unity 6000.0.66f1 project holding the shader, and `FlippyAurasBuild`, which builds the bundle and tests the shader |
| `probe/` | FlippyAurasProbe, the recon plugin that checked particles, events and cameras in the game |
| `tools/make_rsp.py` | Writes the compiler's response file |

## Building (no .NET SDK)

1. **The shader** (only when `unity/Assets/Shaders` changes):

   ```
   Unity.exe -batchmode -quit -projectPath unity -buildTarget Win64 -executeMethod FlippyAurasBuild.BuildAndTest -logFile _private\unity_build.log
   ```

   It writes `bin\bundle\flippyauras_shaders` and `bin\shader_test.txt`, which must end with `ALL OK`.
2. **The plugin**, with Unity 6000.0.66f1's bundled compiler:
   - `python tools\make_rsp.py flippyauras bin\FlippyAuras.dll src` writes `_private\flippyauras.rsp`. It references
     the game's `dotnet\` and `BepInEx\core\` DLLs and every `BepInEx\interop\` DLL except the 3 KB `UnityEngine.dll`,
     and embeds `src\Resources\`.
   - Then compile:

     ```
     <Unity>\Editor\Data\NetCoreRuntime\dotnet.exe <Unity>\Editor\Data\DotNetSdkRoslyn\csc.dll -noconfig @_private\flippyauras.rsp
     ```

MIT license. Geist font © Vercel, SIL Open Font License (`src/Resources/OFL-Geist.txt`).
