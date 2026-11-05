using System;
using System.Diagnostics;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FlippyAuras;

/// The Flippy Auras panel (J): looks (built-in and saved presets, Randomize, Save), the 3 aura slots, the selected
/// slot's type, colours (1 to 3 gradient stops: swatches, hue, saturation, brightness), sliders, its options and
/// reactions, and Preview 360° (the camera circles Flippy). It slides in from the right so Flippy stays in view on
/// the left, every change shows on the auras at once, and the look is saved a second after each change.
/// Built once a few seconds after launch (kit trap 4) and only switched on and off; while open it frees the cursor and
/// pauses the game's controls (traps 6 and 7).
internal static class Panel
{
    const float W = 468f, H = 966f, Pad = 24f, Inner = W - 2 * Pad, Margin = 40f;
    const float RowLabel = 104f, RowValue = 70f, RowSlider = Inner - RowLabel - RowValue - 6f;
    static readonly string[] Swatches = { "#FF3D3D", "#FF9A1F", "#FFE14D", "#3DFF7A", "#3DE8FF", "#3D7BFF", "#B26BFF", "#FF5CD6", "#FFFFFF" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static GameObject root;
    static RectTransform panel, presetBox;
    static Look look;
    static int sel, stop;
    static float hue, sat, val;
    static string activePreset;
    static float slideStart = -1f, nextRefresh;
    static CursorLockMode oldLock;
    static bool oldVisible;
    static readonly Random rnd = new();

    static Ui.Switch master;
    static TextMeshProUGUI keyText;
    static readonly SlotTab[] tabs = new SlotTab[3];
    static readonly TypeTile[] tiles = new TypeTile[8];
    static Ui.Segmented stopsSeg, shapeSeg, ringsSeg, reactSeg;
    static RawImage gradBar;
    static Texture2D gradTex, hueTex, satTex, valTex;
    static readonly RectTransform[] marks = new RectTransform[3];
    static readonly Image[] markInner = new Image[3], markRing = new Image[3];
    static TextMeshProUGUI hexText, optionsHint, costText, previewText;
    static Row hueRow, satRow, valRow, glowRow, sizeRow, speedRow, amountRow, radiusRow;
    static GameObject orbitRow;
    static Image previewBack;

    public static bool IsOpen => root != null && root.activeSelf;

    static Slot S => look.Slots[sel];

    // ---------------------------------------------------------------- build (once)

    public static void Build(Look current)
    {
        if (root != null) return;
        look = current;
        Ui.Build();
        try
        {
            var watch = Stopwatch.StartNew();
            root = new GameObject("FlippyAuras Panel");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30025;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            panel = Ui.Node(root.transform, "Panel");
            Ui.Anchor(panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Margin, -38f), new Vector2(W, H));
            Ui.Fill(panel, Ui.rounded, Ui.Back, true); // catches clicks so they never fall through

            BuildHeader();
            BuildLooks();
            BuildSlots();
            BuildColours();
            BuildMotion();
            BuildBottom();
            Presets.LoadSaved();
            root.SetActive(false);
            Plugin.Logger.LogInfo($"Flippy Auras panel ready ({watch.ElapsedMilliseconds} ms)");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("couldn't build the panel: " + e);
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }

    static void BuildHeader()
    {
        Ui.Icon(panel, Glow.Ring, Pad - 2f, 18f, 36f, Ui.Accent);
        Ui.Label(panel, "Flippy Auras", Ui.semiBold, 28f, Color.white, Pad + 42f, 14f, 260f, 40f);
        master = new Ui.Switch(panel, W - Pad - 34f - 12f - 54f, 21f, () =>
        {
            Plugin.Enabled.Value = !Plugin.Enabled.Value;
            Refresh();
        });
        var close = Ui.KeyCap(panel, Ui.KeyName(Plugin.PanelKey.Value), W - Pad - 34f, 21f, out keyText);
        Ui.Click(close.gameObject, close.GetComponent<Image>(), Close);
    }

    static void BuildLooks()
    {
        Ui.Section(panel, "LOOKS", Pad, 68f, Inner);
        presetBox = Ui.At(panel, "Looks", Pad, 92f, Inner, 72f);
    }

    /// The preset chips, laid out in rows: Randomize, Save, the built-in looks, then saved ones (each with a ×).
    static void FillLooks()
    {
        for (int i = presetBox.childCount - 1; i >= 0; i--) Object.Destroy(presetBox.GetChild(i).gameObject);
        float x = 0f, y = 0f;
        bool full = false;
        void Place(float w, Action<float, float> make)
        {
            if (full) return;
            if (x > 0f && x + w > Inner) { x = 0f; y += 38f; }
            if (y > 38f) { full = true; return; }
            make(x, y);
            x += w + 8f;
        }
        Chip(Place, "Randomize", Glow.Dice, false, () =>
        {
            Presets.Randomize(look, rnd);
            activePreset = null;
            stop = 0;
            Edited(true);
        });
        Chip(Place, "Save", -1, false, () =>
        {
            try
            {
                activePreset = Presets.SaveAs(look);
                Toast($"Saved as {activePreset}");
            }
            catch (Exception e)
            {
                Toast("Couldn't save: " + e.Message);
            }
            FillLooks();
            Refresh();
        });
        foreach (var preset in Presets.BuiltIn) LookChip(Place, preset, false);
        foreach (var preset in Presets.Saved) LookChip(Place, preset, true);
    }

    static void LookChip(Action<float, Action<float, float>> place, Look preset, bool saved)
    {
        Chip(place, preset.Name, -1, preset.Name == activePreset, () =>
        {
            look.CopyFrom(preset);
            activePreset = preset.Name;
            stop = 0;
            Edited(true, keepPreset: true);
        }, saved ? () =>
        {
            Presets.Delete(preset.Name);
            if (activePreset == preset.Name) activePreset = null;
            Toast($"Deleted {preset.Name}");
            FillLooks();
        } : null);
    }

    static void Chip(Action<float, Action<float, float>> place, string text, int icon, bool on, Action click, Action delete = null)
    {
        float textW = Ui.semiBold != null ? MeasureChip(text) : text.Length * 9f;
        float w = textW + 28f + (icon >= 0 ? 24f : 0f) + (delete != null ? 22f : 0f);
        place(w, (cx, cy) =>
        {
            var chip = Ui.At(presetBox, text, cx, cy, w, 32f);
            var back = Ui.Fill(chip, Ui.small, on ? Ui.Accent : Ui.Glass, true);
            float tx = 14f;
            if (icon >= 0) { Ui.Icon(chip, icon, 10f, 7f, 18f, on ? Ui.AccentText : Ui.Accent); tx += 22f; }
            Ui.Label(chip, text, Ui.semiBold, 16f, on ? Ui.AccentText : Color.white, tx, 0f, textW + 4f, 32f);
            Ui.Click(chip.gameObject, back, click);
            if (delete != null)
            {
                var x = Ui.At(chip, "Delete", w - 28f, 4f, 24f, 24f);
                var xBack = Ui.Fill(x, Ui.circle, new Color(0f, 0f, 0f, 0.25f), true);
                Ui.Label(x, "×", Ui.semiBold, 17f, on ? Ui.AccentText : Ui.Ink, 0f, -1f, 24f, 24f, TextAlignmentOptions.Center);
                Ui.Click(x.gameObject, xBack, delete);
            }
        });
    }

    static TextMeshProUGUI measure;

    static float MeasureChip(string text)
    {
        if (measure == null)
        {
            measure = Ui.Label(panel, "", Ui.semiBold, 16f, Color.clear, 0f, 0f, 10f, 10f);
        }
        return measure.GetPreferredValues(text).x;
    }

    static void BuildSlots()
    {
        Ui.Section(panel, "AURAS · UP TO 3 AT ONCE", Pad, 178f, Inner);
        float tw = (Inner - 16f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            tabs[i] = new SlotTab(panel, Pad + i * (tw + 8f), 202f, tw, 56f, i,
                () => { sel = index; stop = 0; LoadHsv(); Refresh(); },
                () => { look.Slots[index].On = !look.Slots[index].On; Edited(false); Refresh(); });
        }
        float w = (Inner - 24f) / 4f;
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            string name = i < 7 ? Auras.Names[i] : "Surprise";
            int icon = i < 7 ? Auras.Icons[i] : Glow.Dice;
            tiles[i] = new TypeTile(panel, Pad + i % 4 * (w + 8f), 268f + i / 4 * 46f, w, 38f, name, icon, i == (int)AuraType.Image, () => PickType(index));
        }
    }

    static void PickType(int index)
    {
        var s = S;
        if (index == 7)
        {
            AuraType t;
            do t = (AuraType)rnd.Next(6); while (t == s.Type);
            Presets.Randomize(s, t, rnd, (float)rnd.NextDouble());
        }
        else
        {
            var t = (AuraType)index;
            if (t != s.Type && (t == AuraType.Ghosts || s.Type == AuraType.Ghosts)) s.Glow = t == AuraType.Ghosts ? 1.3f : 3f; // ghosts are see-through, not glowing
            s.Type = t;
            if (t == AuraType.Image) Toast("Image Aura is coming in the next update");
        }
        s.On = true;
        stop = Math.Min(stop, s.Stops - 1);
        Edited(true);
    }

    static void BuildColours()
    {
        Ui.Section(panel, "COLOURS", Pad, 368f, 200f);
        hexText = Ui.Label(panel, "", Ui.medium, 15f, Ui.Dim, W - Pad - 120f, 368f, 120f, 20f, TextAlignmentOptions.MidlineRight);
        stopsSeg = new Ui.Segmented(panel, Pad, 392f, 108f, 32f, new[] { "1", "2", "3" }, i =>
        {
            S.Stops = i + 1;
            if (stop > i) stop = i;
            LoadHsv();
            Edited(false);
            RefreshColours();
        }, 15f);

        float gx = Pad + 120f, gw = Inner - 120f;
        var bar = Ui.At(panel, "Gradient", gx, 392f, gw, 32f);
        Ui.Fill(bar, Ui.small, new Color(0f, 0f, 0f, 0.3f));
        var img = Ui.At(bar, "Picture", 4f, 4f, gw - 8f, 24f);
        gradBar = img.gameObject.AddComponent<RawImage>();
        gradTex = Ui.Strip(128);
        gradBar.texture = gradTex;
        gradBar.raycastTarget = false;
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            marks[i] = Ui.At(bar, "Stop", 0f, 2f, 28f, 28f);
            markRing[i] = Ui.Fill(marks[i], Ui.circle, Color.white, true);
            var inner = Ui.At(marks[i], "Colour", 4f, 4f, 20f, 20f);
            markInner[i] = Ui.Fill(inner, Ui.circle, Color.white);
            Ui.Click(marks[i].gameObject, markRing[i], () => { stop = index; LoadHsv(); RefreshColours(); });
        }

        float sw = 30f, gap = (Inner - Swatches.Length * sw) / (Swatches.Length - 1);
        for (int i = 0; i < Swatches.Length; i++)
        {
            var col = Col.FromHex(Swatches[i]);
            var dot = Ui.At(panel, "Swatch", Pad + i * (sw + gap), 434f, sw, sw);
            var image = Ui.Fill(dot, Ui.circle, col.ToColor(), true);
            Ui.Click(dot.gameObject, image, () =>
            {
                S.Colors[stop] = col;
                LoadHsv();
                Edited(false);
                RefreshColours();
            });
        }

        hueTex = Ui.Strip(64);
        var px = new Color32[64];
        for (int i = 0; i < 64; i++) px[i] = Col.FromHsv(i / 63f, 1f, 1f).ToColor32(1f);
        hueTex.SetPixels32(px);
        hueTex.Apply(false, false);
        satTex = Ui.Strip(32);
        valTex = Ui.Strip(32);
        hueRow = new Row(panel, 470f, "Hue", 0f, 1f, v => { hue = v; ColourFromHsv(); }, hueTex);
        satRow = new Row(panel, 502f, "Saturation", 0f, 1f, v => { sat = v; ColourFromHsv(); }, satTex);
        valRow = new Row(panel, 534f, "Brightness", 0.05f, 1f, v => { val = v; ColourFromHsv(); }, valTex);
    }

    static void ColourFromHsv()
    {
        S.Colors[stop] = Col.FromHsv(hue, sat, val);
        Edited(false);
        RefreshColours(sliders: false);
    }

    static void BuildMotion()
    {
        Ui.Section(panel, "LOOK & MOTION", Pad, 578f, Inner);
        glowRow = new Row(panel, 602f, "Glow", 0f, 8f, v => { S.Glow = Snap(v, 0.1f); Edited(false); RefreshRows(); });
        sizeRow = new Row(panel, 636f, "Size", 0.25f, 3f, v => { S.Size = Snap(v, 0.05f); Edited(false); RefreshRows(); });
        speedRow = new Row(panel, 670f, "Speed", 0f, 3f, v => { S.Speed = Snap(v, 0.05f); Edited(false); RefreshRows(); });
        amountRow = new Row(panel, 704f, "Amount", 0f, 1f, v => { S.Amount = Snap(v, 0.01f); Edited(false); RefreshRows(); });
        radiusRow = new Row(panel, 738f, "Radius", 0.4f, 2.5f, v => { S.Radius = Snap(v, 0.05f); Edited(false); RefreshRows(); });

        var orbit = Ui.At(panel, "Orbit options", 0f, 778f, W, 34f);
        orbitRow = orbit.gameObject;
        shapeSeg = new Ui.Segmented(orbit, Pad, 0f, Inner - 140f, 34f, Auras.ShapeNames, i => { S.Shape = i; Edited(false); Refresh(); }, 14f);
        ringsSeg = new Ui.Segmented(orbit, W - Pad - 132f, 0f, 132f, 34f, new[] { "1 ring", "2", "3" }, i => { S.Rings = i + 1; Edited(false); Refresh(); }, 14f);
        optionsHint = Ui.Label(panel, "", Ui.medium, 14f, Ui.Dim, Pad, 774f, Inner, 42f);
        optionsHint.enableWordWrapping = true;

        Ui.Section(panel, "REACTIONS", Pad, 828f, Inner);
        reactSeg = new Ui.Segmented(panel, Pad, 852f, Inner, 36f, new[] { "Jump burst", "Fall boost", "Land ring", "Calm still" }, i =>
        {
            var s = S;
            if (i == 0) s.Jump = !s.Jump;
            else if (i == 1) s.Fall = !s.Fall;
            else if (i == 2) s.Land = !s.Land;
            else s.Still = !s.Still;
            Edited(false);
            Refresh();
        }, 14.5f);
    }

    static void BuildBottom()
    {
        var button = Ui.At(panel, "Preview", Pad, 904f, 196f, 44f);
        previewBack = Ui.Fill(button, Ui.small, Ui.Glass, true);
        previewText = Ui.Label(button, "Preview 360°", Ui.semiBold, 18f, Color.white, 0f, 0f, 196f, 44f, TextAlignmentOptions.Center);
        Ui.Click(button.gameObject, previewBack, () => { AuraDriver.Instance?.TogglePreview(); Refresh(); });
        costText = Ui.Label(panel, "", Ui.medium, 13f, new Color(1f, 1f, 1f, 0.45f), Pad + 208f, 904f, Inner - 208f, 44f, TextAlignmentOptions.MidlineRight);
        costText.enableWordWrapping = true;
    }

    static float Snap(float v, float step) => MathF.Round(v / step) * step;

    // ---------------------------------------------------------------- open, close, per frame

    public static void Open()
    {
        if (root == null) return;
        Ui.EnsureEventSystem();
        sel = Math.Clamp(sel, 0, 2);
        LoadHsv();
        FillLooks();
        Refresh();
        root.SetActive(true);
        panel.anchoredPosition = new Vector2(W + Margin, -38f);
        slideStart = -1f;
        GameControls.Pause();
        oldLock = Cursor.lockState; oldVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public static void Close()
    {
        if (!IsOpen) return;
        AuraDriver.Instance?.StopPreview();
        root.SetActive(false);
        GameControls.Resume();
        Cursor.lockState = oldLock; Cursor.visible = oldVisible;
        Presets.SaveCurrentNow(look);
    }

    public static void Tick(Keyboard keyboard)
    {
        if (!IsOpen) return;
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None; // the game may grab it back
        if (!Cursor.visible) Cursor.visible = true;
        // slide in from the right; the timer starts on the first frame after opening, and always lands (trap 4)
        if (slideStart < 0f) slideStart = Time.unscaledTime;
        float t = Math.Min(1f, (Time.unscaledTime - slideStart) / 0.22f);
        float ease = 1f - (1f - t) * (1f - t) * (1f - t);
        panel.anchoredPosition = new Vector2(W + Margin - (W + 2f * Margin) * ease, -38f);
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (Time.unscaledTime >= nextRefresh) RefreshStats();
    }

    static void Toast(string text) => Ui.Toast(text);

    /// After any edit: the look saves a moment later, and a preset chip no longer shows as picked.
    static void Edited(bool full, bool keepPreset = false)
    {
        Presets.Changed();
        if (!keepPreset && activePreset != null)
        {
            activePreset = null;
            FillLooks();
        }
        else if (keepPreset) FillLooks();
        if (full) { LoadHsv(); Refresh(); }
    }

    static void LoadHsv() => S.Colors[Math.Clamp(stop, 0, 2)].ToHsv(out hue, out sat, out val);

    // ---------------------------------------------------------------- showing the settings

    public static void Refresh()
    {
        if (root == null) return;
        var s = S;
        master.Set(Plugin.Enabled.Value);
        keyText.text = Ui.KeyName(Plugin.PanelKey.Value);
        for (int i = 0; i < 3; i++) tabs[i].Show(look.Slots[i], i == sel);
        for (int i = 0; i < 8; i++) tiles[i].Show(i == (int)s.Type);
        RefreshColours();
        RefreshRows();
        bool orbit = s.Type == AuraType.Orbiters;
        if (orbitRow.activeSelf != orbit) orbitRow.SetActive(orbit);
        optionsHint.gameObject.SetActive(!orbit);
        optionsHint.text = Auras.Hint(s.Type);
        if (orbit)
        {
            shapeSeg.Select(s.Shape);
            ringsSeg.Select(s.Rings - 1);
        }
        reactSeg.Toggles(s.Jump, s.Fall, s.Land, s.Still);
        RefreshStats();
    }

    static void RefreshRows()
    {
        var s = S;
        glowRow.Set(s.Glow, s.Glow.ToString("0.0", Inv));
        sizeRow.Set(s.Size, "×" + s.Size.ToString("0.00", Inv));
        speedRow.Set(s.Speed, "×" + s.Speed.ToString("0.00", Inv));
        amountRow.Set(s.Amount, Auras.AmountText(s));
        radiusRow.Set(s.Radius, "×" + s.Radius.ToString("0.00", Inv));
        tabs[sel].Show(s, true);
    }

    static void RefreshColours(bool sliders = true)
    {
        var s = S;
        stop = Math.Clamp(stop, 0, s.Stops - 1);
        stopsSeg.Select(s.Stops - 1);
        var px = new Color32[128];
        for (int i = 0; i < 128; i++) px[i] = s.Sample(i / 127f).ToColor32(1f);
        gradTex.SetPixels32(px);
        gradTex.Apply(false, false);
        float gw = Inner - 120f;
        for (int i = 0; i < 3; i++)
        {
            bool shown = i < s.Stops;
            if (marks[i].gameObject.activeSelf != shown) marks[i].gameObject.SetActive(shown);
            if (!shown) continue;
            float t = s.Stops == 1 ? 0.5f : i / (float)(s.Stops - 1);
            marks[i].anchoredPosition = new Vector2(4f + t * (gw - 8f - 28f), -2f);
            markInner[i].color = s.Colors[i].ToColor();
            markRing[i].color = i == stop ? Ui.Accent : Color.white;
            marks[i].localScale = i == stop ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
        }
        hexText.text = s.Stops > 1 ? $"Stop {stop + 1}  {s.Colors[stop].Hex}" : s.Colors[stop].Hex;

        var sp = new Color32[32];
        for (int i = 0; i < 32; i++) sp[i] = Col.FromHsv(hue, i / 31f, val).ToColor32(1f);
        satTex.SetPixels32(sp);
        satTex.Apply(false, false);
        for (int i = 0; i < 32; i++) sp[i] = Col.FromHsv(hue, sat, i / 31f).ToColor32(1f);
        valTex.SetPixels32(sp);
        valTex.Apply(false, false);
        hueRow.Set(hue, $"{hue * 360f:0}°", sliders);
        satRow.Set(sat, $"{sat * 100f:0}%", sliders);
        valRow.Set(val, $"{val * 100f:0}%", sliders);
        tabs[sel].Show(s, true);
    }

    static void RefreshStats()
    {
        nextRefresh = Time.unscaledTime + 0.5f;
        var driver = AuraDriver.Instance;
        bool previewing = driver != null && driver.Previewing;
        previewBack.color = previewing ? Ui.Accent : Ui.Glass;
        previewText.color = previewing ? Ui.AccentText : Color.white;
        previewText.text = previewing ? "Stop preview" : "Preview 360°";
        string onOff = $"Shift+{Ui.KeyName(Plugin.PanelKey.Value)} turns auras on/off";
        if (!Plugin.Enabled.Value) costText.text = "Auras are off\n" + onOff;
        else if (driver == null || !driver.Drawing) costText.text = "Auras show once you're in a level\n" + onOff;
        else costText.text = $"{driver.CostMs.ToString("0.00", Inv)} ms a frame · {driver.Particles} particles\n{onOff}";
    }

    // ---------------------------------------------------------------- pieces

    /// A row: name, slider, value. With a picture, the slider's track shows it (hue, saturation, brightness).
    sealed class Row
    {
        readonly Slider slider;
        readonly TextMeshProUGUI value;

        public Row(Transform parent, float y, string name, float min, float max, Action<float> changed, Texture2D picture = null)
        {
            Ui.Label(parent, name, Ui.medium, 16f, Ui.Ink, Pad, y, RowLabel, 28f);
            slider = Ui.MakeSlider(parent, Pad + RowLabel, y, RowSlider, min, max, changed, out var track);
            value = Ui.Label(parent, "", Ui.medium, 16f, Ui.Ink, W - Pad - RowValue, y, RowValue, 28f, TextAlignmentOptions.MidlineRight);
            if (picture != null)
            {
                track.sprite = Sprite.Create(picture, new Rect(0, 0, picture.width, 1), new Vector2(0.5f, 0.5f), 100f);
                track.type = Image.Type.Simple;
                track.color = Color.white;
                slider.fillRect.gameObject.SetActive(false);
                slider.fillRect = null;
            }
        }

        public void Set(float v, string text, bool moveSlider = true)
        {
            if (moveSlider) slider.SetValueWithoutNotify(v);
            value.text = text;
        }
    }

    sealed class SlotTab
    {
        readonly Image back, icon, dot;
        readonly TextMeshProUGUI name, sub;
        readonly int index;

        public SlotTab(Transform parent, float x, float y, float w, float h, int index, Action select, Action toggle)
        {
            this.index = index;
            var tab = Ui.At(parent, "Slot " + (index + 1), x, y, w, h);
            back = Ui.Fill(tab, Ui.small, Ui.Soft, true);
            icon = Ui.Icon(tab, Glow.Star, 10f, 14f, 28f, Color.white);
            name = Ui.Label(tab, "", Ui.semiBold, 16f, Color.white, 44f, 8f, w - 76f, 22f);
            name.overflowMode = TextOverflowModes.Ellipsis;
            sub = Ui.Label(tab, "", Ui.medium, 13f, Ui.Dim, 44f, 29f, w - 50f, 20f);
            Ui.Click(tab.gameObject, back, select);
            var d = Ui.At(tab, "On", w - 30f, 8f, 22f, 22f);
            dot = Ui.Fill(d, Ui.circle, Ui.Accent, true);
            Ui.Click(d.gameObject, dot, toggle);
        }

        public void Show(Slot s, bool selected)
        {
            back.color = selected ? new Color(0.239f, 1f, 0.478f, 0.2f) : Ui.Soft;
            icon.sprite = Glow.Icon(Auras.Icons[(int)s.Type]);
            icon.color = s.On ? s.Sample(0.3f).ToColor() : Ui.Dim;
            name.text = Auras.Names[(int)s.Type];
            name.color = s.On ? Color.white : Ui.Dim;
            sub.text = $"Slot {index + 1} · " + (s.On ? "on" : "off");
            dot.color = s.On ? Ui.Accent : new Color(1f, 1f, 1f, 0.2f);
        }
    }

    sealed class TypeTile
    {
        readonly Image back, icon;
        readonly TextMeshProUGUI label;
        readonly bool soon;

        public TypeTile(Transform parent, float x, float y, float w, float h, string text, int cell, bool soon, Action click)
        {
            this.soon = soon;
            var tile = Ui.At(parent, text, x, y, w, h);
            back = Ui.Fill(tile, Ui.small, new Color(0f, 0f, 0f, 0.22f), true);
            icon = Ui.Icon(tile, cell, 9f, 8f, 22f, Color.white);
            label = Ui.Label(tile, text, Ui.medium, 15f, Ui.Ink, 36f, 0f, w - 38f, h);
            if (soon)
            {
                var badge = Ui.At(tile, "Soon", w - 40f, -7f, 38f, 16f);
                Ui.Fill(badge, Ui.small, new Color(1f, 0.85f, 0.3f, 0.9f));
                Ui.Label(badge, "SOON", Ui.semiBold, 10f, new Color(0.2f, 0.12f, 0f, 1f), 0f, 0f, 38f, 16f, TextAlignmentOptions.Center);
            }
            Ui.Click(tile.gameObject, back, click);
        }

        public void Show(bool selected)
        {
            back.color = selected ? Ui.Accent : new Color(0f, 0f, 0f, 0.22f);
            label.color = selected ? Ui.AccentText : soon ? Ui.Dim : Ui.Ink;
            icon.color = selected ? Ui.AccentText : soon ? Ui.Dim : Color.white;
        }
    }
}
