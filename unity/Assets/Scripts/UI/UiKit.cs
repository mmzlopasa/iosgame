using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Design tokens of the Founders UI, after a casual mobile strategy kit: warm parchment popups under a
    /// wooden title plank, inset sections with a tan header band, chunky outlined buttons (green primary,
    /// blue secondary, orange premium, grey disabled, red cancel), rarity cells behind icons, painted icons
    /// and one typeface (Changa One), white with a dark outline and a hard drop.
    ///
    /// Sizes are in panel units: a phone screen is <see cref="GameUi.PhoneShortSide"/> (446) units across its short
    /// side in either orientation (fewer on a small phone, so nothing drops under <see cref="GameUi.MinPointsPerUnit"/>
    /// points per unit), so text and buttons keep their physical size when the phone turns; see
    /// <see cref="GameUi.ShortSideUnits"/>. One unit is ~0.88 iOS points: Small text (12) is never under 10.5 pt and
    /// taps are at least <see cref="MinTap"/> units (40 pt) thanks to <see cref="Ux.HitSlop"/>. The skins (9-slice
    /// textures) and the font advances come from tools/ui/build_ui_assets.py through <see cref="UiGenerated"/>.
    /// </summary>
    public static class Theme
    {
        // MARK: - Palette

        /// <summary>The dark brown of every outline (panels, buttons, text).</summary>
        public static readonly Color Outline = Hex("#3A2314");
        /// <summary>Plain text on parchment (paragraphs).</summary>
        public static readonly Color InkText = Hex("#5A3A1E");
        public static readonly Color InkDark = Hex("#3F2612");
        public static readonly Color InkSoft = Hex("#8A6038");
        /// <summary>Secondary outlined text (sub-lines, inactive tabs).</summary>
        public static readonly Color Pale = Hex("#F3E6D2");
        /// <summary>"Lv.3" in titles.</summary>
        public static readonly Color Lv = Hex("#D7F25A");
        /// <summary>Next values, gains, met requirements.</summary>
        public static readonly Color Up = Hex("#B6F25A");
        /// <summary>Missing resources, losses, danger.</summary>
        public static readonly Color Bad = Hex("#FF6A5A");
        public static readonly Color Gold = Hex("#FFE07A");
        public static readonly Color Icy = Hex("#A9DCFF");
        public static readonly Color Ember = Hex("#FFB066");
        public static readonly Color Lilac = Hex("#E6C3FF");

        // The kit's UI ramp (dark → light) and button colours.
        public static readonly Color Wood5 = Hex("#A17F61");
        public static readonly Color Wood4 = Hex("#CB8A55");
        public static readonly Color Wood3 = Hex("#E0A265");
        public static readonly Color Wood2 = Hex("#E4BF91");
        public static readonly Color Wood1 = Hex("#E8CEAD");
        public static readonly Color GreenBtn = Hex("#9ACC41");
        public static readonly Color BlueBtn = Hex("#94B9EF");
        public static readonly Color OrangeBtn = Hex("#FDB939");
        public static readonly Color GreyBtn = Hex("#B3B3B3");
        public static readonly Color RedBtn = Hex("#DD5444");

        public static readonly Color SectionHead = Hex("#D9A266");
        public static readonly Color SectionLine = Hex("#BF884E");
        public static readonly Color RowLine = Hex("#CFA878");
        public static readonly Color Scrim = new Color(0.10f, 0.06f, 0.03f, 0.45f);

        // MARK: - Type scale and metrics (panel units; a phone is 446 across its short side, ~0.88 pt per unit)

        /// <summary>Window and card titles on the wooden plank.</summary>
        public const float H1 = 20;
        /// <summary>Section headers (the tan band), names in banners and hero lines.</summary>
        public const float H2 = 17;
        /// <summary>Numbers that matter: resources, stat values, "now ➜ next", counters.</summary>
        public const float Num = 15;
        /// <summary>Row labels, button sub-titles' big brother, sentences.</summary>
        public const float Body = 14;
        /// <summary>The smallest text anywhere: hints, sub-lines, captions. Nothing is drawn smaller.</summary>
        public const float Small = 12;

        /// <summary>A list row (stat, requirement, resource).</summary>
        public const float RowH = 40;
        /// <summary>A denser list row (long lists of factors), still a comfortable tap.</summary>
        public const float RowTightH = 36;
        /// <summary>The chunky button (one or two lines).</summary>
        public const float ButtonH = 46;
        /// <summary>The small blue "Go" in rows.</summary>
        public const float GoH = 34;
        public const float TabH = 36;
        public const float PlankH = 38;
        public const float SectionHeadH = 30;
        public const float CloseSize = 38;
        /// <summary>The smallest touch area of anything tappable (46 units = 40 pt at the smallest scale).</summary>
        public const float MinTap = 46;

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// A text colour for light parchment: white becomes dark brown ink, bright colours keep their hue but
        /// become deep enough to read (the kit's light green "next" value becomes a strong green, and so on).
        /// </summary>
        public static Color OnParchment(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f) return v > 0.85f ? InkDark : InkText; // white and pale greys: ink
            float hue = h * 360f;
            float targetV = hue > 35f && hue < 75f ? 0.52f : 0.6f; // yellows need to be darker to read
            return Color.HSVToRGB(h, Mathf.Clamp(s * 1.15f, 0.7f, 1f), Mathf.Min(v, targetV));
        }

        // MARK: - Font

        static FontDefinition font;
        static bool fontLoaded;

        /// <summary>
        /// Changa One as an SDF font asset with a wide padding (15 at 90 pt), so the outline can reach
        /// ~16% of the font size (UI Toolkit clamps outlines to the atlas padding). Falls back to the
        /// default dynamic asset if the runtime font asset cannot be made.
        /// </summary>
        public static FontDefinition Font
        {
            get
            {
                if (fontLoaded) return font;
                fontLoaded = true;
                var f = UnityEngine.Resources.Load<UnityEngine.Font>("UI/Fonts/ChangaOne-Regular");
                if (f == null) { Debug.LogWarning("[FOUNDERS] UI font missing"); return font = default; }
                FontAsset asset = null;
                try { asset = FontAsset.CreateFontAsset(f, 90, 15, GlyphRenderMode.SDFAA, 1024, 1024); }
                catch (Exception e) { Debug.LogWarning("[FOUNDERS] UI font asset: " + e.Message); }
                font = asset != null ? FontDefinition.FromSDFFont(asset) : FontDefinition.FromFont(f);
                return font;
            }
        }

        /// <summary>Outline width for outlined text of a size (the kit's outline is about an eighth of the size).</summary>
        // The SDF outline grows into the glyph as well as out of it: kept thin so the white fill stays bright
        // and thin strokes (the "/" in 46/105) survive; the hard drop below carries the weight instead.
        public static float OutlineFor(float size) => Mathf.Clamp(size * 0.085f, 0.8f, 1.7f);

        /// <summary>Width of a string in Changa One (plus its outline), from the measured advances.</summary>
        public static float TextWidth(string s, float size, bool outlined = true)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float em = 0;
            bool tag = false;
            foreach (char c in s)
            {
                // Rich-text tags (<color=…>) take no room.
                if (c == '<') { tag = true; continue; }
                if (tag) { if (c == '>') tag = false; continue; }
                em += UiGenerated.Advance(c);
            }
            return em * size + (outlined ? 2 * Mathf.Ceil(OutlineFor(size)) : 0) + 1;
        }

        /// <summary>The largest size (at most <paramref name="size"/>, at least <paramref name="min"/>) at which a line fits a width.</summary>
        public static float FitSize(string s, float maxWidth, float size, float min = Small)
        {
            if (maxWidth <= 0) return size;
            float w = TextWidth(s, size);
            if (w <= maxWidth) return size;
            return Mathf.Max(min, Mathf.Floor(size * maxWidth / w * 4f) / 4f);
        }

        /// <summary>How many lines a text takes in a width when it wraps word by word (measured advances, 5% slack).</summary>
        public static int WrappedLines(string s, float size, float width)
        {
            if (string.IsNullOrEmpty(s)) return 1;
            float room = width * 0.95f - 2 * Mathf.Ceil(OutlineFor(size)) - 1;
            float space = UiGenerated.Advance(' ') * size;
            int lines = 1;
            float x = 0;
            foreach (var word in s.Split(' '))
            {
                float w = TextWidth(word, size, false) - 1;
                if (x > 0 && x + space + w > room) { lines++; x = w; }
                else x += (x > 0 ? space : 0) + w;
                if (w > room) lines++; // a word wider than the line breaks inside itself
            }
            return lines;
        }

        /// <summary>The largest size (at most <paramref name="size"/>, at least <paramref name="min"/>) at which a text wraps into two lines of a width.</summary>
        public static float TwoLineSize(string s, float width, float size, float min = Small)
        {
            for (float f = size; f > min; f -= 0.25f)
                if (WrappedLines(s, f, width) <= 2) return f;
            return min;
        }

        // MARK: - Textures

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> skins = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> gradients = new Dictionary<string, Texture2D>();

        public static Texture2D Icon(string name)
        {
            if (name == null) return null;
            // "thumb:medieval_house" / "thumb:deco:well": a picture of the real model (see Thumbnails).
            if (name.StartsWith("thumb:")) return Thumbnails.Get(name.Substring(6)) ?? Icon("build");
            if (icons.TryGetValue(name, out var t)) return t;
            t = UnityEngine.Resources.Load<Texture2D>("UI/Icons/" + name);
            if (t == null) Debug.LogWarning($"[FOUNDERS] UI icon missing: {name}");
            icons[name] = t;
            return t;
        }

        public static Texture2D SkinTexture(string name)
        {
            if (skins.TryGetValue(name, out var t)) return t;
            t = UnityEngine.Resources.Load<Texture2D>("UI/Skin/" + name);
            if (t == null) Debug.LogWarning($"[FOUNDERS] UI skin missing: {name}");
            skins[name] = t;
            return t;
        }

        public static Texture2D Image(string name) => UnityEngine.Resources.Load<Texture2D>("UI/Images/" + name);

        /// <summary>A gradient through evenly spaced stops (vertical: top → bottom).</summary>
        public static Texture2D Gradient(string key, bool vertical, params Color[] stops)
        {
            if (gradients.TryGetValue(key, out var t)) return t;
            const int n = 64;
            t = vertical ? new Texture2D(1, n, TextureFormat.RGBA32, false) : new Texture2D(n, 1, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1) * (stops.Length - 1);
                int a = Mathf.Min(stops.Length - 2, (int)u);
                var c = Color.Lerp(stops[a], stops[a + 1], u - a);
                if (vertical) t.SetPixel(0, n - 1 - i, c); else t.SetPixel(i, 0, c);
            }
            t.Apply(false, true);
            gradients[key] = t;
            return t;
        }

        public static Texture2D TopShade => Gradient("topshade", true, new Color(0.08f, 0.05f, 0.02f, 0.55f), new Color(0.08f, 0.05f, 0.02f, 0f));
        public static Texture2D BottomShade => Gradient("botshade", true, new Color(0.08f, 0.05f, 0.02f, 0f), new Color(0.08f, 0.05f, 0.02f, 0.6f));
    }

    /// <summary>Fluent style helpers so screens read like the design markup.</summary>
    public static class Ux
    {
        public static T Abs<T>(this T e, float? left = null, float? top = null, float? right = null, float? bottom = null) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
            return e;
        }

        public static T Fill<T>(this T e) where T : VisualElement => e.Abs(0, 0, 0, 0);

        public static T Row<T>(this T e, float gap = 0, Align align = Align.Center) where T : VisualElement
        {
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = align;
            if (gap > 0) e.userData = gap;
            return e;
        }

        public static T Col<T>(this T e, Align align = Align.Stretch) where T : VisualElement
        {
            e.style.flexDirection = FlexDirection.Column;
            e.style.alignItems = align;
            return e;
        }

        public static T Center<T>(this T e) where T : VisualElement
        {
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            return e;
        }

        public static T Between<T>(this T e) where T : VisualElement { e.style.justifyContent = Justify.SpaceBetween; return e; }

        public static T Size<T>(this T e, float? w, float? h = null) where T : VisualElement
        {
            if (w.HasValue) { e.style.width = w.Value; e.style.flexShrink = 0; }
            if (h.HasValue) { e.style.height = h.Value; e.style.flexShrink = 0; }
            return e;
        }

        public static T Grow<T>(this T e, float g = 1) where T : VisualElement { e.style.flexGrow = g; e.style.flexShrink = 1; e.style.flexBasis = 0; e.style.minWidth = 0; return e; }
        public static T NoShrink<T>(this T e) where T : VisualElement { e.style.flexShrink = 0; return e; }

        public static T Pad<T>(this T e, float all) where T : VisualElement => e.Pad(all, all, all, all);
        public static T Pad<T>(this T e, float v, float h) where T : VisualElement => e.Pad(v, h, v, h);
        public static T Pad<T>(this T e, float top, float right, float bottom, float left) where T : VisualElement
        {
            e.style.paddingTop = top; e.style.paddingRight = right; e.style.paddingBottom = bottom; e.style.paddingLeft = left;
            return e;
        }

        public static T Margin<T>(this T e, float top, float right = 0, float bottom = 0, float left = 0) where T : VisualElement
        {
            e.style.marginTop = top; e.style.marginRight = right; e.style.marginBottom = bottom; e.style.marginLeft = left;
            return e;
        }

        public static T Radius<T>(this T e, float r) where T : VisualElement => e.Radius(r, r);

        public static T Radius<T>(this T e, float top, float bottom) where T : VisualElement
        {
            e.style.borderTopLeftRadius = top; e.style.borderTopRightRadius = top;
            e.style.borderBottomLeftRadius = bottom; e.style.borderBottomRightRadius = bottom;
            return e;
        }

        public static T Bg<T>(this T e, Color c) where T : VisualElement { e.style.backgroundColor = c; return e; }

        public static T BgImage<T>(this T e, Texture2D tex) where T : VisualElement
        {
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            return e;
        }

        public static T Border<T>(this T e, Color c, float w = 1) where T : VisualElement
        {
            e.style.borderTopWidth = w; e.style.borderRightWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w;
            e.style.borderTopColor = c; e.style.borderRightColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c;
            return e;
        }

        /// <summary>
        /// Gives an element a 9-slice skin (see tools/ui/build_ui_assets.py): the texture is drawn over the
        /// whole element with its slices (in points, drawn at 3 pixels per point) kept at their size.
        /// </summary>
        public static T Skin<T>(this T e, string name) where T : VisualElement
        {
            // Text on light parchment is dark and plain; on wood, buttons and the world it is white and outlined.
            e.EnableInClassList("surf-light", name == "panel" || name == "section" || name == "slot");
            e.EnableInClassList("surf-dark", !(name == "panel" || name == "section" || name == "slot"));
            var tex = Theme.SkinTexture(name);
            if (tex == null) return e;
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            // Real wood and parchment repeat (tiled) rather than stretch, so their grain keeps its size.
            var slice = UiGenerated.Tiled.Contains(name) ? SliceType.Tiled : SliceType.Sliced;
            if (e.style.unitySliceType != slice) e.style.unitySliceType = slice;
            if (UiGenerated.Skins.TryGetValue(name, out var s))
            {
                int k = UiGenerated.SkinScale;
                e.style.unitySliceLeft = s.Left * k;
                e.style.unitySliceTop = s.Top * k;
                e.style.unitySliceRight = s.Right * k;
                e.style.unitySliceBottom = s.Bottom * k;
                e.style.unitySliceScale = 1f / k;
            }
            return e;
        }

        public static T Opacity<T>(this T e, float o) where T : VisualElement { e.style.opacity = o; return e; }
        public static T Ignore<T>(this T e) where T : VisualElement { e.pickingMode = PickingMode.Ignore; return e; }

        public static T Hidden<T>(this T e, bool hidden) where T : VisualElement
        {
            var d = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (e.style.display != d) e.style.display = d;
            return e;
        }

        public static T Gap<T>(this T e, float gap) where T : VisualElement
        {
            // Unity's flexbox has no gap: children get a leading margin instead (see Kids).
            e.userData = gap;
            return e;
        }

        /// <summary>Adds children, applying the parent's gap as leading margins.</summary>
        public static T Kids<T>(this T parent, params VisualElement[] children) where T : VisualElement
        {
            float gap = parent.userData is float g ? g : 0;
            bool row = parent.style.flexDirection == FlexDirection.Row;
            foreach (var c in children)
            {
                if (c == null) continue;
                if (gap > 0 && parent.childCount > 0)
                {
                    if (row) c.style.marginLeft = gap; else c.style.marginTop = gap;
                }
                parent.Add(c);
            }
            return parent;
        }

        public static T Transition<T>(this T e, float ms, params string[] props) where T : VisualElement
        {
            var names = new List<StylePropertyName>();
            var times = new List<TimeValue>();
            var ease = new List<EasingFunction>();
            foreach (var p in props)
            {
                names.Add(new StylePropertyName(p));
                times.Add(new TimeValue(ms, TimeUnit.Millisecond));
                ease.Add(new EasingFunction(EasingMode.EaseOutCubic));
            }
            e.style.transitionProperty = names;
            e.style.transitionDuration = times;
            e.style.transitionTimingFunction = ease;
            return e;
        }

        public static T OnTap<T>(this T e, Action action) where T : VisualElement
        {
            e.pickingMode = PickingMode.Position;
            e.AddManipulator(new Clickable(() => action()));
            return e;
        }

        /// <summary>
        /// Grows the touch area of a small button to at least <paramref name="w"/> × <paramref name="h"/> units without
        /// changing its look or layout: a transparent child reaching past its edges takes the taps (a tap on a child
        /// counts as a tap on the button).
        /// </summary>
        public static T HitSlop<T>(this T e, float w, float h, float min = Theme.MinTap) where T : VisualElement
        {
            float sx = Mathf.Max(0, (min - w) / 2), sy = Mathf.Max(0, (min - h) / 2);
            if (sx <= 0 && sy <= 0) return e;
            var slop = new VisualElement { name = "hit-slop" };
            slop.style.position = Position.Absolute;
            slop.style.left = -sx; slop.style.right = -sx; slop.style.top = -sy; slop.style.bottom = -sy;
            slop.pickingMode = PickingMode.Position;
            e.Add(slop);
            return e;
        }

        /// <summary>Runs a setup step on an element inline and returns it.</summary>
        public static T Also<T>(this T e, Action<T> a) where T : VisualElement { a(e); return e; }
    }

    /// <summary>Element factory: the kit's components.</summary>
    public static class E
    {
        public static VisualElement Div(string name = null) => new VisualElement { name = name };

        static Label Bare(string text, float size, Color color)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontDefinition = Theme.Font;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textOverflow = TextOverflow.Clip;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>
        /// The kit's text: white (or a colour) with a dark outline and a hard drop; one line. Placed on light
        /// parchment it turns into plain dark ink of the same hue instead, which reads far better at small sizes.
        /// </summary>
        public static Label T(string text, float size = Theme.Body, Color? color = null)
        {
            var l = Bare(text, size, color ?? Color.white);
            Outline(l, size);
            l.AddToClassList("kit-text");
            l.RegisterCallback<AttachToPanelEvent>(_ => ApplySurface(l));
            return l;
        }

        /// <summary>True when the nearest skinned ancestor is light parchment.</summary>
        static bool OnParchment(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains("surf-light")) return true;
                if (p.ClassListContains("surf-dark")) return false;
            }
            return false;
        }

        static void ApplySurface(Label l)
        {
            bool ink = OnParchment(l);
            if (ink == l.ClassListContains("ink-mode")) return;
            l.EnableInClassList("ink-mode", ink);
            if (ink)
            {
                l.style.unityTextOutlineWidth = 0;
                l.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 0, color = new Color(1f, 0.97f, 0.9f, 0.55f) };
                l.style.color = Theme.OnParchment(l.style.color.value);
                // Coloured runs ("Lv.4" in yellow-green) darken the same way.
                if (l.text != null && l.text.Contains("<color=#"))
                    l.text = System.Text.RegularExpressions.Regex.Replace(l.text, "<color=#([0-9A-Fa-f]{6})>",
                        m => "<color=#" + ColorUtility.ToHtmlStringRGB(Theme.OnParchment(Theme.Hex("#" + m.Groups[1].Value))) + ">");
            }
            else
            {
                Outline(l, l.style.fontSize.value.value);
            }
        }

        /// <summary>Colours a label, keeping it readable on whatever surface it stands (use instead of style.color).</summary>
        public static void Tint(Label l, Color c)
        {
            var want = l.ClassListContains("ink-mode") ? Theme.OnParchment(c) : c;
            if (l.style.color != want) l.style.color = want;
        }

        /// <summary>Sets the outline and drop for a size (call again when the size changes).</summary>
        public static void Outline(Label l, float size)
        {
            float w = Theme.OutlineFor(size);
            l.style.unityTextOutlineWidth = w;
            l.style.unityTextOutlineColor = Theme.Outline;
            l.style.textShadow = new TextShadow { offset = new Vector2(0, w + 1f), blurRadius = 0, color = Theme.Outline };
            // Room for the outline at both ends, so a clipped (overflow: hidden) label never cuts it.
            float pad = Mathf.Ceil(w);
            l.style.paddingLeft = pad;
            l.style.paddingRight = pad;
        }

        /// <summary>Plain dark text on parchment for sentences; wraps.</summary>
        public static Label Ink(string text, float size = Theme.Body, Color? color = null)
        {
            var l = Bare(text, size, color ?? Theme.InkText);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// <summary>Lets an outlined label wrap (sentences in outlined text).</summary>
        public static Label Wrap(this Label l)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// <summary>
        /// The label of a row: takes the room left between fixed neighbours and wraps onto a second line
        /// when it must (never clipped, never under the minimum size).
        /// </summary>
        public static Label Flex(this Label l)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexGrow = 1;
            l.style.flexShrink = 1;
            l.style.flexBasis = 0;
            l.style.minWidth = 0;
            return l;
        }

        /// <summary>
        /// Shrinks the label's font (down to <paramref name="min"/>, never below <see cref="Theme.Small"/>) until
        /// one line fits the width; text that still does not fit wraps instead of being clipped.
        /// </summary>
        public static Label Fit(this Label l, float maxWidth, float min = Theme.Small)
        {
            min = Mathf.Max(min, Theme.Small);
            float size = l.style.fontSize.value.value;
            float fit = Theme.FitSize(l.text, maxWidth, size, min);
            if (!Mathf.Approximately(fit, size)) { l.style.fontSize = fit; if (l.style.unityTextOutlineWidth.value > 0) Outline(l, fit); }
            l.style.maxWidth = maxWidth;
            if (Theme.TextWidth(l.text, fit) > maxWidth) l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// <summary>
        /// Two lines in a box of a fixed width (names under cells): the size shrinks (not below the minimum)
        /// until the longest word fits the width and the whole text fits two lines (estimated conservatively).
        /// The box is as tall as two lines, so neighbours line up.
        /// </summary>
        public static Label TwoLines(this Label l, float width, float min = Theme.Small)
        {
            min = Mathf.Max(min, Theme.Small);
            float size = l.style.fontSize.value.value;
            float fit = Theme.TwoLineSize(l.text ?? "", width, size, min);
            l.style.fontSize = fit;
            if (l.style.unityTextOutlineWidth.value > 0) Outline(l, fit);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.width = width;
            l.style.minHeight = fit * 2.3f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return l;
        }

        public static Label Align(this Label l, TextAnchor a) { l.style.unityTextAlign = a; return l; }

        // MARK: - Icons

        public static VisualElement Icon(string name, float size)
        {
            var e = Div().Size(size, size).Ignore();
            var tex = Theme.Icon(name);
            if (tex != null) e.BgImage(tex);
            return e;
        }

        /// <summary>Swaps the picture of an icon made by <see cref="Icon"/>.</summary>
        public static void SetIcon(VisualElement e, string name)
        {
            var tex = Theme.Icon(name);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
        }

        /// <summary>An icon greyed out (locked states).</summary>
        public static VisualElement IconDim(string name, float size)
        {
            var e = Icon(name, size);
            e.style.unityBackgroundImageTintColor = new Color(0.62f, 0.58f, 0.54f, 0.85f);
            return e;
        }

        public static VisualElement Image(Texture2D tex, float w, float h)
        {
            var e = Div().Size(w, h).Ignore();
            if (tex != null) e.BgImage(tex);
            return e;
        }

        // MARK: - Containers

        /// <summary>The parchment popup body (padding covers the outline and lip).</summary>
        public static VisualElement Panel() => Div("panel").Col().Skin("panel").Pad(4, 10, 10, 10);

        /// <summary>The wooden title strip: name and an optional "Lv.N" in yellow-green.</summary>
        public static VisualElement Plank(string title, int? level = null, float size = Theme.H1, float width = 0)
        {
            var p = Div("plank").Row().Center().Skin("plank").Pad(0, 40, 4, 40).Ignore();
            p.style.height = Theme.PlankH;
            p.style.flexShrink = 0;
            string text = level.HasValue ? $"{title} <color=#D7F25A>Lv.{level.Value}</color>" : title;
            var l = T(text, size);
            // Room for the close button on the right and the same on the left: the title stays centred.
            if (width > 0) l.Fit(width - 2 * (Theme.CloseSize + 10), Theme.Body);
            l.style.whiteSpace = WhiteSpace.NoWrap;
            p.Add(l);
            return p;
        }

        /// <summary>An inset section: tan header band with the title (and extras on the right) over a body.</summary>
        public static VisualElement Section(string title, out VisualElement body, VisualElement extra = null)
        {
            var s = Div("section").Col().Skin("section");
            s.style.flexShrink = 0;
            // The tan band is wood, not parchment: its title is the kit's white outlined text.
            var head = Div().Row().Ignore().Bg(Theme.SectionHead).Radius(8, 0).Pad(0, 10);
            head.AddToClassList("surf-dark");
            head.style.minHeight = Theme.SectionHeadH;
            head.style.borderBottomWidth = 1.5f;
            head.style.borderBottomColor = Theme.SectionLine;
            head.style.marginLeft = 1.5f; head.style.marginRight = 1.5f; head.style.marginTop = 1.5f;
            head.Add(T(title, Theme.H2).Flex());
            if (extra != null) head.Add(extra.NoShrink().Margin(0, 0, 0, 8));
            s.Add(head);
            body = Div().Col().Pad(0, 10, 4, 10);
            s.Add(body);
            return s;
        }

        /// <summary>A plain inset box (no header).</summary>
        public static VisualElement Inset() { var s = Div().Col().Skin("section").Pad(7, 10); s.style.flexShrink = 0; return s; }

        /// <summary>A section row: icon, label on the left, content on the right; a hairline above unless first.</summary>
        public static VisualElement Line(bool separator, float height = Theme.RowH)
        {
            var r = Div().Row(0).Ignore();
            r.style.minHeight = height;
            r.style.flexShrink = 0;
            r.style.paddingTop = 2; r.style.paddingBottom = 2;
            if (separator) { r.style.borderTopWidth = 1.5f; r.style.borderTopColor = Theme.RowLine; }
            return r;
        }

        /// <summary>The icon at the start of a row.</summary>
        public static VisualElement RowIcon(string icon) => Icon(icon, 24).Margin(0, 8);

        /// <summary>Stat row: "Label:  now ➜ next" (the next value green), right-aligned values.</summary>
        public static VisualElement StatRow(string icon, string label, string now, string next = null, bool separator = true, Color? nowColor = null)
        {
            var r = Line(separator);
            if (icon != null) r.Add(RowIcon(icon));
            r.Add(T(label, Theme.Body).Flex());
            r.Add(T(now, Theme.Num, nowColor).NoShrink().Margin(0, 0, 0, 8));
            if (next != null)
            {
                r.Add(Icon("arrow", 20).Margin(0, 6, 0, 6));
                r.Add(T(next, Theme.Num, Theme.Up).NoShrink());
            }
            return r;
        }

        // MARK: - Buttons

        /// <summary>
        /// Button colours of the blue kit: Orange is the striped YELLOW primary (level up, build), Green confirms,
        /// Blue is a secondary go/close, Brown is the dark NAVY secondary (remove), Red cancels, Grey is locked.
        /// </summary>
        public enum Kind { Green, Blue, Orange, Grey, Red, Brown }

        static string SkinOf(Kind k) => k switch
        {
            Kind.Green => "btn_green",
            Kind.Blue => "btn_blue",
            Kind.Orange => "btn_orange",
            Kind.Red => "btn_red",
            Kind.Brown => "btn_brown",
            _ => "btn_grey",
        };

        /// <summary>
        /// The kit's chunky button: label, optional icon and an optional second line (a cost or a timer).
        /// Grey buttons swallow taps but do nothing. The label shrinks to the width it is given (<paramref name="width"/>).
        /// </summary>
        public static VisualElement Button(string label, Action onTap, Kind kind = Kind.Green, float height = Theme.ButtonH, string icon = null,
                                           float size = 16, VisualElement sub = null, float width = 0)
        {
            var b = Div("button").Col().Center().Skin(SkinOf(kind)).Pad(0, 10, 4, 10);
            b.style.height = height;
            b.style.flexShrink = 0;
            if (width > 0) b.style.width = width;
            var top = Div().Row().Center().Ignore();
            if (icon != null) top.Add(Icon(icon, size + 6).Margin(0, 5));
            // The kit's buttons speak in capitals.
            var l = T(label?.ToUpperInvariant(), size);
            // Shrinks to the width given; a label still too long at the minimum size wraps onto two lines.
            if (width > 0) l.Fit(width - 22 - (icon != null ? size + 11 : 0), Theme.Small).Align(TextAnchor.MiddleCenter);
            top.Add(l);
            b.Add(top);
            if (sub != null) { sub.style.marginTop = -2; b.Add(sub); }
            if (onTap != null && kind != Kind.Grey) b.OnTap(onTap);
            else b.pickingMode = PickingMode.Position; // still swallows taps
            if (onTap != null && height < Theme.MinTap) b.HitSlop(Theme.MinTap, height);
            return b;
        }

        /// <summary>A button that takes an equal share of a row of buttons (the label shrinks to its share).</summary>
        public static VisualElement Shared(this VisualElement b)
        {
            b.style.flexGrow = 1;
            b.style.flexBasis = 0;
            b.style.flexShrink = 1;
            b.style.minWidth = 0;
            return b;
        }

        /// <summary>A square button with an icon (close, −, +, rotate, trash…).</summary>
        public static VisualElement Square(string icon, Action onTap, Kind kind = Kind.Brown, float size = 40)
        {
            var b = Div("square").Center().Skin(SkinOf(kind)).Pad(0, 0, 3, 0).Size(size, size);
            b.Add(Icon(icon, Mathf.Round(size * 0.6f)));
            if (onTap != null && kind != Kind.Grey) b.OnTap(onTap);
            else b.pickingMode = PickingMode.Position;
            if (onTap != null) b.HitSlop(size, size);
            return b;
        }

        /// <summary>The red close button that sits on a popup's top-right corner.</summary>
        public static VisualElement Close(Action onTap) => Square("close", onTap, Kind.Red, Theme.CloseSize);

        /// <summary>A tab: raised gold when selected, flat brown otherwise; text may carry a counter.</summary>
        public static VisualElement Tab(string label, bool on, Action onTap, string count = null, float height = Theme.TabH, float size = Theme.Num, string icon = null)
        {
            var t = Div("tab").Row().Center().Skin(on ? "tab_on" : "tab_off").Pad(0, 10, on ? 4 : 3, 10);
            t.style.height = on ? height + 3 : height;
            t.style.flexShrink = 0;
            t.style.alignSelf = UnityEngine.UIElements.Align.FlexEnd;
            if (icon != null) t.Add(Icon(icon, 24));
            if (!string.IsNullOrEmpty(label))
            {
                string text = count != null ? $"{label} <color=#D7F25A>{count}</color>" : label;
                var l = T(text, size, on ? Color.white : Theme.Pale);
                if (icon != null) l.style.marginLeft = 4;
                t.Add(l);
            }
            if (onTap != null) t.OnTap(onTap).HitSlop(Theme.MinTap, height);
            return t;
        }

        /// <summary>Shrinks a tab's label to the width the tab is given (tabs sharing a row).</summary>
        public static VisualElement FitTab(this VisualElement tab, float width)
        {
            int kids = 0;
            foreach (var c in tab.Children()) if (c.name != "hit-slop") kids++;
            foreach (var c in tab.Children())
                if (c is Label l) l.Fit(width - 22 - (kids > 1 ? 28 : 0), Theme.Small).style.whiteSpace = WhiteSpace.NoWrap;
            return tab;
        }

        // MARK: - Slots, cells, badges

        public enum Cell { Slot, Tile, Common, Uncommon, Rare, Epic, Legendary }

        static string SkinOf(Cell c) => c switch
        {
            Cell.Tile => "tile",
            Cell.Common => "cell_common",
            Cell.Uncommon => "cell_uncommon",
            Cell.Rare => "cell_rare",
            Cell.Epic => "cell_epic",
            Cell.Legendary => "cell_legendary",
            _ => "slot",
        };

        /// <summary>A square slot (parchment, wooden tile or a rarity cell) with an icon filling about two thirds.</summary>
        public static VisualElement Slot(string icon, float size, Cell kind = Cell.Slot, float iconShare = 0.66f, bool dim = false)
        {
            var s = Div("slot").Center().Skin(SkinOf(kind)).Pad(0, 0, 3, 0).Size(size, size);
            if (icon != null && icon.StartsWith("thumb:")) iconShare = Mathf.Max(iconShare, 0.92f); // pictures fill the cell
            if (icon != null) s.Add(dim ? IconDim(icon, Mathf.Round(size * iconShare)) : Icon(icon, Mathf.Round(size * iconShare)));
            return s;
        }

        /// <summary>A small badge: green level square or red notification dot, with a number or "!".</summary>
        public static VisualElement Badge(string text, bool red = true, float size = 20)
        {
            var b = Div("badge").Center().Skin(red ? "badge_red" : "badge_green").Pad(0, 4, 1, 4).Ignore();
            b.style.minWidth = size;
            b.style.height = size;
            b.style.flexShrink = 0;
            b.Add(T(text, Mathf.Max(Theme.Small, Mathf.Round(size * 0.66f))));
            return b;
        }

        /// <summary>The dark translucent HUD capsule.</summary>
        public static VisualElement Capsule(float height = 32)
        {
            var c = Div("capsule").Row().Skin("capsule").Pad(0, 11);
            c.style.height = height;
            c.style.flexShrink = 0;
            return c;
        }

        // MARK: - The blue HUD (after the casual blue kit: navy counters, hexagon buttons, glossy blue squares)

        /// <summary>The kit's yellow used for ranks and levels in the HUD.</summary>
        public static readonly Color RankYellow = Theme.Hex("#FFD23A");

        /// <summary>A navy HUD capsule (counters, chips, news, timers); text on it is white and outlined.</summary>
        public static VisualElement HudCapsule(float height = 30)
        {
            var c = Div("hud-capsule").Row().Skin("hud_capsule").Pad(0, 12);
            c.style.height = height;
            c.style.flexShrink = 0;
            return c;
        }

        /// <summary>Width of the room an overlapping counter icon takes at the capsule's left end.</summary>
        public const float CounterIconRoom = 26;

        /// <summary>
        /// A resource counter: the navy capsule with the 3D icon overlapping its left end, the number, and the
        /// green + on the right when <paramref name="plus"/> is given (it gets its own tap).
        /// </summary>
        public static VisualElement HudCounter(string icon, out Label value, Action plus, float height = 30, float size = Theme.Num)
        {
            var c = HudCapsule(height);
            c.style.paddingLeft = CounterIconRoom + 4;
            c.style.paddingRight = plus != null ? 0 : 12;
            c.style.marginLeft = 6; // room for the icon's overhang
            float ic = height + 6;
            c.Add(Icon(icon, ic).Abs(-8, -3));
            value = T("", size);
            c.Add(value.NoShrink());
            if (plus != null)
            {
                var p = Div("hud-plus").Skin("hud_plus").Size(24, 25.5f);
                p.style.marginLeft = 6;
                p.style.marginRight = -3;
                p.style.marginBottom = 1;
                p.OnTap(plus).HitSlop(24, 25.5f, 36);
                c.Add(p);
            }
            c.pickingMode = PickingMode.Position;
            return c;
        }

        /// <summary>A small navy chip: icon and an outlined text (temperature, discontent, the sick…).</summary>
        public static VisualElement HudChip(string icon, out Label value, float height = 28)
        {
            var c = HudCapsule(height).Pad(0, 10, 0, 7);
            c.Add(Icon(icon, height - 6));
            value = T("", Theme.Body);
            value.style.marginLeft = 4;
            c.Add(value);
            c.pickingMode = PickingMode.Position;
            return c;
        }

        /// <summary>The kit's glossy blue square button (navigation, settings) with an icon.</summary>
        public static VisualElement HudSquare(string icon, Action onTap, float size = 44)
        {
            var b = Div("hud-square").Center().Skin("hud_square_blue").Pad(0, 0, 4, 0).Size(size, Mathf.Round(size * 1.06f));
            b.Add(Icon(icon, Mathf.Round(size * 0.6f)));
            if (onTap != null) b.OnTap(onTap).HitSlop(size, size * 1.06f);
            else b.pickingMode = PickingMode.Position;
            return b;
        }

        /// <summary>The kit's small round button (blue: close, yellow: info).</summary>
        public static VisualElement HudRound(string icon, Action onTap, bool yellow = false, float size = 30)
        {
            var b = Div("hud-round").Center().Skin(yellow ? "hud_round_yellow" : "hud_round_blue").Pad(0, 0, 2, 0).Size(size, Mathf.Round(size * 1.06f));
            b.Add(Icon(icon, Mathf.Round(size * 0.6f)));
            if (onTap != null) b.OnTap(onTap).HitSlop(size, size * 1.06f);
            return b;
        }

        /// <summary>The kit's blue hexagon button: the icon on the hexagon, an outlined label on its lower edge.</summary>
        public static VisualElement HudHex(string icon, string label, float size = 48)
        {
            var c = Div("hud-hex").Col(UnityEngine.UIElements.Align.Center).Size(size + 16, size + 8);
            var hex = Div().Center().Skin("hud_hex").Pad(0, 0, 4, 0).Size(size, size * 1.01f).Ignore();
            hex.Add(Icon(icon, Mathf.Round(size * 0.66f)));
            c.Add(hex);
            if (!string.IsNullOrEmpty(label))
            {
                // The label sits on the hexagon's lower point and may reach past its sides (like the kit's labels).
                var l = T(label, Theme.Small).Align(TextAnchor.MiddleCenter).Abs(-4, null, -4, 0);
                l.style.whiteSpace = WhiteSpace.NoWrap;
                c.Add(l);
            }
            c.pickingMode = PickingMode.Position;
            return c;
        }

        /// <summary>The kit's big hexagon frame with a portrait in its centre (the player block).</summary>
        public static VisualElement HexFrame(string portrait, float width = 64)
        {
            float h = Mathf.Round(width * 72f / 64f);
            var f = Div("hex-frame").Center().Skin("hud_hex_frame").Pad(0, 0, 3, 0).Size(width, h);
            f.Add(Icon(portrait, Mathf.Round(width * 0.74f)));
            return f;
        }

        /// <summary>The kit's rank pill: navy with a yellow rim and yellow text ("Lv.9").</summary>
        public static VisualElement RankPill(out Label value, float height = 22)
        {
            var p = Div("rank").Row().Center().Skin("hud_rank").Pad(0, 12, 1, 12);
            p.style.height = height;
            p.style.flexShrink = 0;
            value = T("", Theme.Body, RankYellow);
            p.Add(value);
            return p;
        }

        /// <summary>
        /// The kit's energy bar: a navy track whose left end is a hexagon cap holding an icon, a cyan fill and a
        /// centred outlined label. Returns the fill (for <see cref="SetFill"/>) and the label.
        /// </summary>
        public static VisualElement HudBar(string icon, out VisualElement fill, out Label label, float width, float height = 24)
        {
            var track = Div("hud-bar").Skin("hud_bar_track");
            track.style.width = width;
            track.style.height = height;
            track.style.flexShrink = 0;
            var area = Div().Ignore().Abs(25, 5, 4, 5);
            fill = Div().Skin("hud_bar_fill_blue").Ignore().Abs(0, 0, null, 0);
            fill.style.minWidth = height - 10;
            fill.style.width = Length.Percent(0);
            fill.style.display = DisplayStyle.None;
            area.Add(fill);
            track.Add(area);
            label = T("", Theme.Small).Align(TextAnchor.MiddleCenter);
            label.Abs(25, 0, 4, 0);
            track.Add(label);
            track.Add(Icon(icon, height + 2).Abs(-1, -1));
            return track;
        }

        // MARK: - Progress

        public enum FillKind { Green, Gold, Blue, Red, Hud }

        static string SkinOf(FillKind k) => k switch
        {
            FillKind.Gold => "fill_gold", FillKind.Blue => "fill_blue", FillKind.Red => "fill_red", FillKind.Hud => "hud_bar_fill_blue", _ => "fill_green",
        };

        /// <summary>A progress bar (dark track, coloured fill); returns the fill for <see cref="SetFill"/>.</summary>
        public static VisualElement Bar(out VisualElement fill, float height = 12, FillKind kind = FillKind.Green)
        {
            var track = Div("bar").Skin("bar_track").Ignore();
            track.style.height = height;
            track.style.flexShrink = 0;
            fill = Div().Skin(SkinOf(kind)).Ignore().Abs(0, 0, null, 0);
            fill.style.minWidth = height;
            fill.style.width = Length.Percent(0);
            fill.style.display = DisplayStyle.None;
            track.Add(fill);
            return track;
        }

        /// <summary>A bar with a centred outlined label ("73%"); returns the fill and the label.</summary>
        public static VisualElement LabeledBar(out VisualElement fill, out Label label, float height = 18, FillKind kind = FillKind.Green)
        {
            var bar = Bar(out fill, height, kind);
            label = T("", Mathf.Max(Theme.Small, height - 4)).Align(TextAnchor.MiddleCenter);
            label.Fill();
            bar.Add(label);
            return bar;
        }

        public static void SetFill(VisualElement fill, double value)
        {
            float v = Mathf.Clamp01((float)value);
            fill.Hidden(v <= 0.001f);
            fill.style.width = Length.Percent(v * 100f);
        }

        public static void SetFillKind(VisualElement fill, FillKind kind) => fill.Skin(SkinOf(kind));

        // MARK: - Scrolling

        /// <summary>
        /// Vertical scroll view exactly as tall as its content, which flex-shrinks (and then scrolls) when its
        /// column runs out of room: for cards and dialog panes that must never be taller than the room the
        /// screen leaves them. The height is set from the content's measured size.
        /// </summary>
        public static ScrollView FitScroll()
        {
            var s = Scroll(ScrollViewMode.Vertical);
            s.style.flexGrow = 0;
            s.style.flexShrink = 1;
            s.style.minHeight = 0;
            s.contentContainer.style.flexShrink = 0;
            float last = -1;
            s.contentContainer.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float h = e.newRect.height;
                if (float.IsNaN(h) || Mathf.Abs(h - last) < 0.5f) return;
                last = h;
                s.style.height = h;
            });
            return s;
        }

        public static ScrollView Scroll(ScrollViewMode mode)
        {
            var s = new ScrollView(mode);
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            s.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            if (mode == ScrollViewMode.Horizontal) s.contentContainer.style.flexDirection = FlexDirection.Row;
            return s;
        }
    }
}
