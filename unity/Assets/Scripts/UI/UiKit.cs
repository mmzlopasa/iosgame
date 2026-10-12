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
    /// Sizes are in panel units: the game runs in landscape and the panel is scaled so a phone screen is
    /// 390 units tall (690–850 wide); see <see cref="GameUi.MatchFor"/>. The skins (9-slice textures) and
    /// the font advances come from tools/ui/build_ui_assets.py through <see cref="UiGenerated"/>.
    /// </summary>
    public static class Theme
    {
        // MARK: - Palette

        /// <summary>The dark brown of every outline (panels, buttons, text).</summary>
        public static readonly Color Outline = Hex("#3A2314");
        /// <summary>Plain text on parchment (paragraphs).</summary>
        public static readonly Color InkText = Hex("#5A3A1E");
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

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

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
        public static float OutlineFor(float size) => Mathf.Clamp(size * 0.12f, 1f, 2.2f);

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
        public static float FitSize(string s, float maxWidth, float size, float min = 7f)
        {
            if (maxWidth <= 0) return size;
            float w = TextWidth(s, size);
            if (w <= maxWidth) return size;
            return Mathf.Max(min, Mathf.Floor(size * maxWidth / w * 4f) / 4f);
        }

        // MARK: - Textures

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> skins = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> gradients = new Dictionary<string, Texture2D>();

        public static Texture2D Icon(string name)
        {
            if (name == null) return null;
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
            var tex = Theme.SkinTexture(name);
            if (tex == null) return e;
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
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

        /// <summary>The kit's text: white (or a colour) with a dark outline and a hard drop; one line.</summary>
        public static Label T(string text, float size = 12, Color? color = null)
        {
            var l = Bare(text, size, color ?? Color.white);
            Outline(l, size);
            return l;
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
        public static Label Ink(string text, float size = 11, Color? color = null)
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

        /// <summary>Shrinks the label's font until one line fits the width (no ellipsis anywhere).</summary>
        public static Label Fit(this Label l, float maxWidth, float min = 7f)
        {
            float size = l.style.fontSize.value.value;
            float fit = Theme.FitSize(l.text, maxWidth, size, min);
            if (!Mathf.Approximately(fit, size)) { l.style.fontSize = fit; if (l.style.unityTextOutlineWidth.value > 0) Outline(l, fit); }
            l.style.maxWidth = maxWidth;
            l.style.overflow = Overflow.Hidden;
            return l;
        }

        /// <summary>
        /// Two lines at most in a fixed box: the size shrinks until the longest word fits the width and the
        /// whole text fits two lines (estimated conservatively).
        /// </summary>
        public static Label TwoLines(this Label l, float width, float min = 7f)
        {
            float size = l.style.fontSize.value.value;
            string s = l.text ?? "";
            float longest = 0;
            foreach (var word in s.Split(' ')) longest = Mathf.Max(longest, Theme.TextWidth(word, 1, false));
            float total = Theme.TextWidth(s, 1, false);
            float fit = size;
            // Every word on a line of its own must fit; the whole must fit 2 lines with 15% slack for wrapping.
            if (longest * fit + 2 * Theme.OutlineFor(fit) > width) fit = (width - 4) / Mathf.Max(0.01f, longest);
            if (total * fit * 1.15f > width * 2) fit = Mathf.Min(fit, width * 2 / (total * 1.15f));
            fit = Mathf.Clamp(Mathf.Floor(fit * 4) / 4, min, size);
            l.style.fontSize = fit;
            if (l.style.unityTextOutlineWidth.value > 0) Outline(l, fit);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.width = width;
            l.style.height = fit * 2.2f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.overflow = Overflow.Hidden;
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
        public static VisualElement Panel() => Div("panel").Col().Skin("panel").Pad(4, 8, 8, 8);

        /// <summary>The wooden title strip: name and an optional "Lv.N" in yellow-green.</summary>
        public static VisualElement Plank(string title, int? level = null, float size = 15, float width = 0)
        {
            var p = Div("plank").Row().Center().Skin("plank").Pad(0, 30, 3, 30).Ignore();
            p.style.height = 30;
            p.style.flexShrink = 0;
            string text = level.HasValue ? $"{title} <color=#D7F25A>Lv.{level.Value}</color>" : title;
            var l = T(text, size);
            if (width > 0) l.Fit(width - 64, 9);
            p.Add(l);
            return p;
        }

        /// <summary>An inset section: tan header band with the title (and extras on the right) over a body.</summary>
        public static VisualElement Section(string title, out VisualElement body, VisualElement extra = null)
        {
            var s = Div("section").Col().Skin("section");
            s.style.flexShrink = 0;
            var head = Div().Row().Ignore().Bg(Theme.SectionHead).Radius(7, 0).Pad(0, 8);
            head.style.height = 20;
            head.style.borderBottomWidth = 1;
            head.style.borderBottomColor = Theme.SectionLine;
            head.style.marginLeft = 1; head.style.marginRight = 1; head.style.marginTop = 1;
            head.Add(T(title, 11.5f).Grow().Also(l => l.style.overflow = Overflow.Hidden));
            if (extra != null) head.Add(extra.NoShrink());
            s.Add(head);
            body = Div().Col().Pad(0, 8, 3, 8);
            s.Add(body);
            return s;
        }

        /// <summary>A plain inset box (no header).</summary>
        public static VisualElement Inset() { var s = Div().Col().Skin("section").Pad(5, 8); s.style.flexShrink = 0; return s; }

        /// <summary>A section row: icon, label on the left, content on the right; a hairline above unless first.</summary>
        public static VisualElement Line(bool separator, float height = 23)
        {
            var r = Div().Row(0).Ignore();
            r.style.minHeight = height;
            r.style.flexShrink = 0;
            if (separator) { r.style.borderTopWidth = 1; r.style.borderTopColor = Theme.RowLine; }
            return r;
        }

        /// <summary>Stat row: "Label:  now ➜ next" (the next value green), right-aligned values.</summary>
        public static VisualElement StatRow(string icon, string label, string now, string next = null, bool separator = true, Color? nowColor = null)
        {
            var r = Line(separator);
            if (icon != null) r.Add(Icon(icon, 15).Margin(0, 5));
            r.Add(T(label, 11.5f).Grow().Also(l => l.style.overflow = Overflow.Hidden));
            r.Add(T(now, 12, nowColor).NoShrink());
            if (next != null)
            {
                r.Add(Icon("arrow", 13).Margin(0, 4, 0, 4));
                r.Add(T(next, 12, Theme.Up).NoShrink());
            }
            return r;
        }

        // MARK: - Buttons

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
        public static VisualElement Button(string label, Action onTap, Kind kind = Kind.Green, float height = 34, string icon = null,
                                           float size = 13, VisualElement sub = null, float width = 0)
        {
            var b = Div("button").Col().Center().Skin(SkinOf(kind)).Pad(0, 8, 3, 8);
            b.style.height = height;
            b.style.flexShrink = 0;
            if (width > 0) b.style.width = width;
            var top = Div().Row().Center().Ignore();
            if (icon != null) top.Add(Icon(icon, size + 5).Margin(0, 4));
            var l = T(label, size);
            if (width > 0) l.Fit(width - 18 - (icon != null ? size + 9 : 0), 8);
            top.Add(l);
            b.Add(top);
            if (sub != null) { sub.style.marginTop = -1; b.Add(sub); }
            if (onTap != null && kind != Kind.Grey) b.OnTap(onTap);
            else b.pickingMode = PickingMode.Position; // still swallows taps
            return b;
        }

        /// <summary>A square button with an icon (close, −, +, rotate, trash…).</summary>
        public static VisualElement Square(string icon, Action onTap, Kind kind = Kind.Brown, float size = 30)
        {
            var b = Div("square").Center().Skin(SkinOf(kind)).Pad(0, 0, 3, 0).Size(size, size);
            b.Add(Icon(icon, Mathf.Round(size * 0.6f)));
            if (onTap != null && kind != Kind.Grey) b.OnTap(onTap);
            else b.pickingMode = PickingMode.Position;
            return b;
        }

        /// <summary>The red close button that sits on a popup's top-right corner.</summary>
        public static VisualElement Close(Action onTap) => Square("close", onTap, Kind.Red, 30);

        /// <summary>A tab: raised gold when selected, flat brown otherwise; text may carry a counter.</summary>
        public static VisualElement Tab(string label, bool on, Action onTap, string count = null, float height = 26, float size = 12, string icon = null)
        {
            var t = Div("tab").Row().Center().Skin(on ? "tab_on" : "tab_off").Pad(0, 8, on ? 3 : 2, 8);
            t.style.height = on ? height + 2 : height;
            t.style.flexShrink = 0;
            t.style.alignSelf = UnityEngine.UIElements.Align.FlexEnd;
            if (icon != null) t.Add(Icon(icon, 18));
            if (!string.IsNullOrEmpty(label))
            {
                string text = count != null ? $"{label} <color=#D7F25A>{count}</color>" : label;
                var l = T(text, size, on ? Color.white : Theme.Pale);
                if (icon != null) l.style.marginLeft = 3;
                t.Add(l);
            }
            if (onTap != null) t.OnTap(onTap);
            return t;
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
            if (icon != null) s.Add(dim ? IconDim(icon, Mathf.Round(size * iconShare)) : Icon(icon, Mathf.Round(size * iconShare)));
            return s;
        }

        /// <summary>A small badge: green level square or red notification dot, with a number or "!".</summary>
        public static VisualElement Badge(string text, bool red = true, float size = 16)
        {
            var b = Div("badge").Center().Skin(red ? "badge_red" : "badge_green").Pad(0, 3, 1, 3).Ignore();
            b.style.minWidth = size;
            b.style.height = size;
            b.style.flexShrink = 0;
            b.Add(T(text, Mathf.Round(size * 0.66f)));
            return b;
        }

        /// <summary>The dark translucent HUD capsule.</summary>
        public static VisualElement Capsule(float height = 28)
        {
            var c = Div("capsule").Row().Skin("capsule").Pad(0, 9);
            c.style.height = height;
            c.style.flexShrink = 0;
            return c;
        }

        // MARK: - Progress

        public enum FillKind { Green, Gold, Blue, Red }

        static string SkinOf(FillKind k) => k switch { FillKind.Gold => "fill_gold", FillKind.Blue => "fill_blue", FillKind.Red => "fill_red", _ => "fill_green" };

        /// <summary>A progress bar (dark track, coloured fill); returns the fill for <see cref="SetFill"/>.</summary>
        public static VisualElement Bar(out VisualElement fill, float height = 10, FillKind kind = FillKind.Green)
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
        public static VisualElement LabeledBar(out VisualElement fill, out Label label, float height = 12, FillKind kind = FillKind.Green)
        {
            var bar = Bar(out fill, height, kind);
            label = T("", Mathf.Max(8, height - 2)).Align(TextAnchor.MiddleCenter);
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
        /// column runs out of room: for cards and dialog panes that must never be taller than the short
        /// landscape screen. The height is set from the content's measured size.
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
