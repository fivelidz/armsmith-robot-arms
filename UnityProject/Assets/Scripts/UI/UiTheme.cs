using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArmSmith.UI
{
    /// <summary>
    /// Shared UI Toolkit theme + widget factory for the ARMSMITH interface system. Mirrors the design
    /// language in design/ui_html/styles.css (dark robotics-console: near-black bg, teal primary, orange
    /// power/torque, green success, red danger, monospace HUD type). One place to build PanelSettings (so a
    /// UIDocument renders with NO manual asset authoring) and to construct the reusable widgets (panels,
    /// headers, buttons, sliders, toggles, stat rows) used across every window.
    ///
    /// All colours are the exact hex tokens from styles.css. Layout uses USS flex (no CSS grid).
    /// </summary>
    public static class UiTheme
    {
        // ── palette (from styles.css :root) ───────────────────────────────────────────────────────────
        public static readonly Color Bg       = Hex("0a0d0f");
        public static readonly Color Surface  = Hex("111519");
        public static readonly Color Card     = Hex("151c22");
        public static readonly Color Card2    = Hex("0e1318");
        public static readonly Color Border   = Hex("1e2a34");
        public static readonly Color BorderHi = Hex("2a3d50");
        public static readonly Color Teal     = Hex("00d4b4");
        public static readonly Color TealDim  = Hex("007a68");
        public static readonly Color Orange   = Hex("ff6b2b");
        public static readonly Color OrangeDim= Hex("7a3010");
        public static readonly Color Green    = Hex("39ff82");
        public static readonly Color Red      = Hex("ff3a5c");
        public static readonly Color Yellow   = Hex("ffcc00");
        public static readonly Color Muted    = Hex("4a6070");
        public static readonly Color Text     = Hex("c8d8e4");
        public static readonly Color TextHi   = Hex("e8f4ff");
        public static readonly Color TextDim  = Hex("3a5060");

        // joint colour-coding J0..J5 (red->purple), matches servo colours used elsewhere
        public static readonly Color[] JointColors = {
            Hex("f24d4d"), Hex("fa9e33"), Hex("f2d940"), Hex("59cc66"), Hex("4da6f2"), Hex("b373f2")
        };

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c); return c;
        }

        // ── PanelSettings (runtime, no asset authoring) ───────────────────────────────────────────────
        static PanelSettings _panelSettings;

        /// <summary>Create/return a PanelSettings configured for a 1920x1080 reference, scale-with-screen,
        /// with the runtime theme loaded from Resources/UI/ArmSmithTheme. A UIDocument MUST have this or it
        /// renders nothing. Safe to call headless (theme load may be null in -nographics; that's fine).</summary>
        public static PanelSettings GetPanelSettings()
        {
            if (_panelSettings != null) return _panelSettings;
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "ArmSmithPanelSettings";
            var theme = Resources.Load<ThemeStyleSheet>("UI/ArmSmithTheme");
            if (theme != null) _panelSettings.themeStyleSheet = theme;
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            _panelSettings.match = 0.5f;
            // (clearColor defaults to transparent in this Unity version, so the panel overlays the 3D scene)
            return _panelSettings;
        }

        /// <summary>The shared USS stylesheet (Resources/UI/ArmSmithUI). May be null headless; widgets fall
        /// back to inline styles so they still build + test without it.</summary>
        public static StyleSheet LoadStyle() => Resources.Load<StyleSheet>("UI/ArmSmithUI");

        // ── widget factory (inline-styled so it works with OR without the USS) ─────────────────────────

        public static VisualElement Row(int gap = 6)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            if (gap > 0) e.style.marginBottom = 0;
            return e;
        }

        public static VisualElement Col(int gap = 6)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        /// <summary>A console PANEL (card bg, border, rounded, teal top-edge accent line).</summary>
        public static VisualElement Panel(Color? accent = null)
        {
            var p = new VisualElement();
            p.style.backgroundColor = Card;
            SetBorder(p, Border, 1);
            SetRadius(p, 8);
            p.style.marginBottom = 8;
            p.style.paddingTop = 0;
            // top accent line
            var edge = new VisualElement();
            edge.style.height = 2;
            edge.style.backgroundColor = accent ?? Teal;
            SetRadiusTop(edge, 8);
            p.Add(edge);
            return p;
        }

        public static Label PanelTitle(string text, Color? accent = null)
        {
            var l = new Label(text.ToUpperInvariant());
            l.style.color = accent ?? Teal;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 12;
            l.style.letterSpacing = 1.5f;
            l.style.paddingLeft = 10; l.style.paddingTop = 8; l.style.paddingBottom = 8;
            return l;
        }

        public static VisualElement PanelHeader(string title, Color? accent = null, string badge = null, Color? badgeColor = null)
        {
            var h = Row();
            h.style.backgroundColor = Card2;
            h.style.justifyContent = Justify.SpaceBetween;
            h.style.borderBottomColor = Border; h.style.borderBottomWidth = 1;
            h.Add(PanelTitle(title, accent));
            if (badge != null) h.Add(Badge(badge, badgeColor ?? accent ?? Teal));
            return h;
        }

        public static Label Badge(string text, Color color)
        {
            var b = new Label(text.ToUpperInvariant());
            b.style.fontSize = 9;
            b.style.color = color;
            b.style.letterSpacing = 1f;
            b.style.paddingLeft = 6; b.style.paddingRight = 6; b.style.paddingTop = 2; b.style.paddingBottom = 2;
            b.style.marginRight = 8;
            SetBorder(b, color, 1); SetRadius(b, 3);
            return b;
        }

        public static Label Lbl(string text, Color? color = null, int size = 11)
        {
            var l = new Label(text);
            l.style.color = color ?? Text;
            l.style.fontSize = size;
            return l;
        }

        /// <summary>Small uppercase muted caption (the dominant ".label" style).</summary>
        public static Label Caption(string text)
        {
            var l = new Label(text.ToUpperInvariant());
            l.style.color = Muted; l.style.fontSize = 10; l.style.letterSpacing = 1.2f;
            return l;
        }

        public static Button Btn(string text, System.Action onClick, Color? color = null)
        {
            var b = new Button(onClick) { text = text.ToUpperInvariant() };
            var c = color ?? Teal;
            b.style.fontSize = 11; b.style.letterSpacing = 1f;
            b.style.color = c;
            b.style.backgroundColor = new Color(0, 0, 0, 0);
            SetBorder(b, c, 1); SetRadius(b, 4);
            b.style.paddingLeft = 8; b.style.paddingRight = 8; b.style.paddingTop = 4; b.style.paddingBottom = 4;
            b.style.marginRight = 4; b.style.marginTop = 2; b.style.marginBottom = 2;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            return b;
        }

        /// <summary>Mark a button as "active" (filled accent) or not.</summary>
        public static void SetActive(Button b, bool on, Color? color = null)
        {
            var c = color ?? Teal;
            if (on) { b.style.backgroundColor = new Color(c.r, c.g, c.b, 0.18f); b.style.color = TextHi; }
            else    { b.style.backgroundColor = new Color(0, 0, 0, 0); b.style.color = c; }
        }

        /// <summary>A labelled slider row: [label  ====  value]. Returns the row; out-params expose the
        /// Slider + value Label so the caller can bind/read.</summary>
        public static VisualElement SliderRow(string label, float min, float max, float val,
                                              out Slider slider, out Label valueLabel,
                                              string valueSuffix = "", Color? thumb = null)
        {
            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 3; row.style.marginBottom = 3;
            var cap = Caption(label); cap.style.width = 120; cap.style.flexShrink = 0;
            slider = new Slider(min, max) { value = val };
            slider.style.flexGrow = 1; slider.style.marginLeft = 6; slider.style.marginRight = 6;
            valueLabel = Lbl(val.ToString("0.##") + valueSuffix, thumb ?? Teal);
            valueLabel.style.width = 56; valueLabel.style.flexShrink = 0;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(cap); row.Add(slider); row.Add(valueLabel);
            return row;
        }

        /// <summary>A labelled toggle row: [label / description    (toggle)].</summary>
        public static VisualElement ToggleRow(string label, string desc, bool val, out Toggle toggle)
        {
            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 3; row.style.marginBottom = 3;
            var left = Col();
            left.Add(Lbl(label, Text, 11));
            if (!string.IsNullOrEmpty(desc)) left.Add(Caption(desc));
            toggle = new Toggle { value = val };
            row.Add(left); row.Add(toggle);
            return row;
        }

        /// <summary>A stat row: [LABEL ......... VALUE].</summary>
        public static VisualElement StatRow(string label, string value, out Label valueLabel, Color? valueColor = null)
        {
            var row = Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.borderTopColor = Border; row.style.borderTopWidth = 1;
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            row.Add(Caption(label));
            valueLabel = Lbl(value, valueColor ?? Teal, 12);
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(valueLabel);
            return row;
        }

        /// <summary>A glowing status dot (●) of a given colour, followed by a label.</summary>
        public static VisualElement StatusDot(string text, Color color, out Label lbl)
        {
            var row = Row();
            var dot = new Label("●"); dot.style.color = color; dot.style.fontSize = 10; dot.style.marginRight = 4;
            lbl = Lbl(text, Text, 10);
            row.Add(dot); row.Add(lbl);
            return row;
        }

        public static VisualElement ProgressBar(float pct01, Color fill, float height = 8)
        {
            var track = new VisualElement();
            track.style.height = height; track.style.backgroundColor = Card2;
            SetRadius(track, 3); track.style.marginTop = 3; track.style.marginBottom = 3;
            var bar = new VisualElement();
            bar.style.height = height; bar.style.backgroundColor = fill;
            SetRadius(bar, 3);
            bar.style.width = new Length(Mathf.Clamp01(pct01) * 100f, LengthUnit.Percent);
            bar.name = "fill";
            track.Add(bar);
            return track;
        }

        public static void SetProgress(VisualElement track, float pct01)
        {
            var fill = track?.Q<VisualElement>("fill");
            if (fill != null) fill.style.width = new Length(Mathf.Clamp01(pct01) * 100f, LengthUnit.Percent);
        }

        public static Label SectionHead(string text)
        {
            var l = new Label(text.ToUpperInvariant());
            l.style.color = Muted; l.style.fontSize = 10; l.style.letterSpacing = 1.2f;
            l.style.borderBottomColor = Border; l.style.borderBottomWidth = 1;
            l.style.marginTop = 8; l.style.marginBottom = 4; l.style.paddingBottom = 3;
            return l;
        }

        // ── style helpers ─────────────────────────────────────────────────────────────────────────────
        public static void SetBorder(VisualElement e, Color c, float w)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c; e.style.borderRightColor = c;
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
        }
        public static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }
        public static void SetRadiusTop(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
        }
        public static void Pad(VisualElement e, float p)
        {
            e.style.paddingTop = p; e.style.paddingBottom = p; e.style.paddingLeft = p; e.style.paddingRight = p;
        }

        // ── industry-pattern widgets (Foxglove/W&B/RViz style) ──────────────────────────────────────────

        /// <summary>Semantic colour for a bounded quantity: green within range, amber approaching the limit,
        /// red over. `pct01` is fraction of the limit used (0..1+).</summary>
        public static Color SemColor(float pct01)
        {
            if (pct01 >= 0.92f) return Red;
            if (pct01 >= 0.75f) return Orange;
            return Green;
        }

        /// <summary>A status chip: ● dot + label, with a consistent colour grammar (the Foxglove pattern).
        /// Pair colour with text so it survives colour-blindness.</summary>
        public static VisualElement StatusChip(string text, Color color)
        {
            var chip = Row();
            chip.style.paddingLeft = 6; chip.style.paddingRight = 8; chip.style.paddingTop = 2; chip.style.paddingBottom = 2;
            chip.style.marginRight = 6;
            chip.style.backgroundColor = new Color(color.r, color.g, color.b, 0.12f);
            SetBorder(chip, color, 1); SetRadius(chip, 10);
            var dot = new Label("●"); dot.style.color = color; dot.style.fontSize = 9; dot.style.marginRight = 4;
            var l = new Label(text); l.style.color = TextHi; l.style.fontSize = 10; l.style.letterSpacing = 0.8f;
            chip.Add(dot); chip.Add(l);
            return chip;
        }

        /// <summary>A labelled GAUGE bar: name + coloured fill (auto amber/red near the limit) + numeric value.
        /// `value01` is the fill fraction; `pctOfLimit` decides the colour (defaults to value01).</summary>
        public static VisualElement Gauge(string label, float value01, string valueText, out VisualElement fill, float? pctOfLimit = null)
        {
            var row = Row(); row.style.justifyContent = Justify.SpaceBetween; row.style.marginTop = 2; row.style.marginBottom = 2;
            var name = Caption(label); name.style.width = 86; name.style.flexShrink = 0;
            var track = new VisualElement();
            track.style.flexGrow = 1; track.style.height = 10; track.style.backgroundColor = Card2;
            SetRadius(track, 3); track.style.marginLeft = 6; track.style.marginRight = 6;
            fill = new VisualElement();
            fill.style.height = 10; SetRadius(fill, 3);
            fill.style.width = new Length(Mathf.Clamp01(value01) * 100f, LengthUnit.Percent);
            fill.style.backgroundColor = SemColor(pctOfLimit ?? value01);
            fill.name = "gfill";
            track.Add(fill);
            var v = Lbl(valueText, Text, 10); v.style.width = 64; v.style.flexShrink = 0; v.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(name); row.Add(track); row.Add(v);
            return row;
        }

        /// <summary>Update a gauge row built by Gauge(): re-set fill width + colour + value text.</summary>
        public static void SetGauge(VisualElement row, float value01, string valueText, float? pctOfLimit = null)
        {
            var fill = row?.Q<VisualElement>("gfill");
            if (fill != null)
            {
                fill.style.width = new Length(Mathf.Clamp01(value01) * 100f, LengthUnit.Percent);
                fill.style.backgroundColor = SemColor(pctOfLimit ?? value01);
            }
            // value label is the last child
            if (row != null && row.childCount > 0 && row[row.childCount - 1] is Label l) l.text = valueText;
        }

        /// <summary>A SPARKLINE element backed by a live data source (W&B/Grafana inline-trend pattern).
        /// Draws the supplied series via the Vector API; call MarkDirtyRepaint() to refresh.</summary>
        public sealed class Sparkline : VisualElement
        {
            public System.Func<IList<float>> source;
            public Color lineColor = Teal;
            public bool fillArea = false;
            public Sparkline(System.Func<IList<float>> src, Color color, float height = 40, bool fill = false)
            {
                source = src; lineColor = color; fillArea = fill;
                style.height = height; style.backgroundColor = Card2; SetRadius(this, 4);
                style.marginTop = 4; style.marginBottom = 4;
                generateVisualContent += Draw;
            }
            void Draw(MeshGenerationContext ctx)
            {
                var data = source != null ? source() : null;
                if (data == null || data.Count < 2) return;
                float w = contentRect.width, h = contentRect.height;
                if (w <= 1 || h <= 1) return;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var v in data) { if (v < min) min = v; if (v > max) max = v; }
                if (max - min < 1e-4f) { max = min + 1f; }
                var p = ctx.painter2D;
                float pad = 3f;
                Vector2 P(int i) => new Vector2(pad + (i / (float)(data.Count - 1)) * (w - 2 * pad),
                                                h - pad - Mathf.InverseLerp(min, max, data[i]) * (h - 2 * pad));
                if (fillArea)
                {
                    p.fillColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.15f);
                    p.BeginPath(); p.MoveTo(new Vector2(P(0).x, h - pad));
                    for (int i = 0; i < data.Count; i++) p.LineTo(P(i));
                    p.LineTo(new Vector2(P(data.Count - 1).x, h - pad)); p.ClosePath(); p.Fill();
                }
                p.strokeColor = lineColor; p.lineWidth = 1.6f; p.BeginPath();
                for (int i = 0; i < data.Count; i++) { if (i == 0) p.MoveTo(P(i)); else p.LineTo(P(i)); }
                p.Stroke();
            }
        }

        /// <summary>A titled metric tile (big number + sparkline) — the W&B/TensorBoard dashboard cell.</summary>
        public static VisualElement MetricTile(string title, Color accent, System.Func<float> value,
                                               System.Func<IList<float>> series, out Label valueLabel, string fmt = "F2")
        {
            var tile = Panel(accent); tile.style.flexGrow = 1; tile.style.marginRight = 6; tile.style.minWidth = 130;
            var b = new VisualElement(); Pad(b, 8); tile.Add(b);
            b.Add(Caption(title));
            valueLabel = new Label(value != null ? value().ToString(fmt) : "—");
            valueLabel.style.color = accent; valueLabel.style.fontSize = 22; valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.Add(valueLabel);
            if (series != null) b.Add(new Sparkline(series, accent, 34, true));
            return tile;
        }

        /// <summary>A dual-range (min/max) row built from two sliders — for domain-randomization ranges.</summary>
        public static VisualElement DualRange(string label, float absMin, float absMax, float lo, float hi,
                                              out Slider loS, out Slider hiS, out Label valLbl)
        {
            var col = Col(); col.style.marginTop = 3; col.style.marginBottom = 3;
            var head = Row(); head.style.justifyContent = Justify.SpaceBetween;
            head.Add(Caption(label));
            valLbl = Lbl($"{lo:0.##} – {hi:0.##}", Teal, 10);
            head.Add(valLbl); col.Add(head);
            var sliders = Row();
            loS = new Slider(absMin, absMax) { value = lo }; loS.style.flexGrow = 1; loS.style.marginRight = 4;
            hiS = new Slider(absMin, absMax) { value = hi }; hiS.style.flexGrow = 1;
            sliders.Add(loS); sliders.Add(hiS); col.Add(sliders);
            return col;
        }
    }
}
