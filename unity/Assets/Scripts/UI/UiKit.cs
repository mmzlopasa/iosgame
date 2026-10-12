using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Design tokens of the Founders UI (night navy glass + gold, after the approved design
    /// canvas). Sizes are in panel units: the game runs in landscape and the panel is scaled so a
    /// phone screen is 390 units tall (and about 690–850 wide); see <see cref="GameUi.MatchFor"/>.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("#0D0F14");
        public static readonly Color Navy = Hex("#14161D");
        public static readonly Color Navy2 = Hex("#1A1D25");
        public static readonly Color Navy3 = Hex("#262A35");
        public static readonly Color Ink = Hex("#F5F2FF");
        public static readonly Color Body = Hex("#B8B4DD");
        public static readonly Color Muted = Hex("#8F8BBA");
        public static readonly Color Faint = Hex("#6F6B98");
        public static readonly Color Lavender = Hex("#C4BBFF");
        public static readonly Color LavenderLight = Hex("#E2DDFF");
        public static readonly Color Gold = Hex("#FFD76A");
        public static readonly Color GoldLight = Hex("#FFE7A0");
        public static readonly Color GoldPale = Hex("#FFF0B8");
        public static readonly Color GoldDeep = Hex("#E9B84A");
        public static readonly Color OnGold = Hex("#1B1530");
        public static readonly Color Error = Hex("#FF9AA8");
        public static readonly Color Mint = Hex("#8EF0C8");

        public static readonly Color Glass = new Color(0.16f, 0.17f, 0.22f, 0.78f);
        public static readonly Color GlassSolid = new Color(0.11f, 0.12f, 0.16f, 0.96f);
        public static readonly Color Edge = new Color(1, 1, 1, 0.14f);
        public static readonly Color EdgeSoft = new Color(1, 1, 1, 0.08f);
        public static readonly Color Fill = new Color(1, 1, 1, 0.05f);
        public static readonly Color Track = new Color(1, 1, 1, 0.09f);

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // MARK: - Fonts

        public enum F { Display, DisplayBold, Text, TextMedium, TextBold, Mono }
        static readonly Dictionary<F, FontDefinition> fonts = new Dictionary<F, FontDefinition>();

        public static FontDefinition Font(F f)
        {
            if (fonts.TryGetValue(f, out var def)) return def;
            string file = f switch
            {
                F.Display => "Sora-SemiBold",
                F.DisplayBold => "Sora-Bold",
                F.TextMedium => "Geist-Medium",
                F.TextBold => "Geist-SemiBold",
                F.Mono => "GeistMono-SemiBold",
                _ => "Geist-Regular",
            };
            var font = UnityEngine.Resources.Load<Font>("UI/Fonts/" + file);
            def = font != null ? FontDefinition.FromFont(font) : default;
            fonts[f] = def;
            return def;
        }

        // MARK: - Textures

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> gradients = new Dictionary<string, Texture2D>();

        public static Texture2D Icon(string name)
        {
            if (icons.TryGetValue(name, out var t)) return t;
            t = UnityEngine.Resources.Load<Texture2D>("UI/Icons/" + name);
            if (t == null) Debug.LogWarning($"[FOUNDERS] UI icon missing: {name}");
            icons[name] = t;
            return t;
        }

        public static Texture2D Image(string name) => UnityEngine.Resources.Load<Texture2D>("UI/Images/" + name);

        /// <summary>Horizontal gradient (left → right) through evenly spaced stops.</summary>
        public static Texture2D Gradient(string key, bool vertical, params Color[] stops)
        {
            if (gradients.TryGetValue(key, out var t)) return t;
            const int n = 128;
            t = vertical ? new Texture2D(1, n, TextureFormat.RGBA32, false) : new Texture2D(n, 1, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1) * (stops.Length - 1);
                int a = Mathf.Min(stops.Length - 2, (int)u);
                var c = Color.Lerp(stops[a], stops[a + 1], u - a);
                // Textures run bottom-up; vertical gradients are given top → bottom.
                if (vertical) t.SetPixel(0, n - 1 - i, c); else t.SetPixel(i, 0, c);
            }
            t.Apply(false, true);
            gradients[key] = t;
            return t;
        }

        public static Texture2D GoldFill => Gradient("gold", false, GoldPale, Gold, Gold, GoldDeep);
        public static Texture2D LavenderFill => Gradient("lav", false, LavenderLight, Lavender);
        public static Texture2D MintFill => Gradient("mint", false, Hex("#C8F7E4"), Mint);
        public static Texture2D GlassFill => Gradient("glass", true, new Color(0.22f, 0.23f, 0.29f, 0.86f), new Color(0.13f, 0.14f, 0.19f, 0.86f), new Color(0.16f, 0.17f, 0.22f, 0.88f));
        public static Texture2D SheetFill => Gradient("sheet", true, new Color(0.10f, 0.11f, 0.15f, 0.97f), new Color(0.05f, 0.06f, 0.08f, 0.99f));
        public static Texture2D DockFill => Gradient("dock", true, new Color(0.17f, 0.19f, 0.24f, 0.90f), new Color(0.09f, 0.10f, 0.13f, 0.94f));
        public static Texture2D GoldGlow => Gradient("goldglow", true, Alpha(Gold, 0.16f), Alpha(Gold, 0.03f), new Color(1, 1, 1, 0.03f));
        public static Texture2D LavenderGlow => Gradient("lavglow", true, Alpha(Lavender, 0.20f), Alpha(Lavender, 0.05f), new Color(1, 1, 1, 0.03f));
        public static Texture2D TopShade => Gradient("topshade", true, new Color(0.05f, 0.06f, 0.08f, 0.82f), new Color(0.05f, 0.06f, 0.08f, 0.45f), new Color(0.05f, 0.06f, 0.08f, 0f));
        public static Texture2D BottomShade => Gradient("botshade", true, new Color(0.05f, 0.06f, 0.08f, 0f), new Color(0.05f, 0.06f, 0.08f, 0.55f), new Color(0.05f, 0.06f, 0.08f, 0.85f));
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
            if (h.HasValue) e.style.height = h.Value;
            return e;
        }

        public static T WidthPct<T>(this T e, float pct) where T : VisualElement { e.style.width = Length.Percent(pct); return e; }
        public static T Grow<T>(this T e, float g = 1) where T : VisualElement { e.style.flexGrow = g; e.style.flexShrink = 1; e.style.minWidth = 0; return e; }
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

        public static T Radius<T>(this T e, float r) where T : VisualElement
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
            return e;
        }

        public static T Radius<T>(this T e, float top, float bottom) where T : VisualElement
        {
            e.style.borderTopLeftRadius = top; e.style.borderTopRightRadius = top;
            e.style.borderBottomLeftRadius = bottom; e.style.borderBottomRightRadius = bottom;
            return e;
        }

        public static T Bg<T>(this T e, Color c) where T : VisualElement { e.style.backgroundColor = c; return e; }

        public static T BgImage<T>(this T e, Texture2D tex, Color? tint = null) where T : VisualElement
        {
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            if (tint.HasValue) e.style.unityBackgroundImageTintColor = tint.Value;
            return e;
        }

        public static T Cover<T>(this T e, Texture2D tex) where T : VisualElement
        {
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            return e;
        }

        public static T Border<T>(this T e, Color c, float w = 1) where T : VisualElement
        {
            e.style.borderTopWidth = w; e.style.borderRightWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w;
            e.style.borderTopColor = c; e.style.borderRightColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c;
            return e;
        }

        /// <summary>Glass card: translucent navy, hairline edge, brighter top edge (the inset highlight).</summary>
        public static T Glass<T>(this T e, float radius = 20, Color? edge = null) where T : VisualElement
        {
            e.BgImage(Theme.GlassFill).Radius(radius).Border(edge ?? Theme.Edge);
            if (!edge.HasValue) e.style.borderTopColor = new Color(1, 1, 1, 0.24f);
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
            // Unity's flexbox has no gap: children get a leading margin instead (see Add).
            e.userData = gap;
            return e;
        }

        /// <summary>Adds children, applying the parent's gap as leading margins.</summary>
        public static T Kids<T>(this T parent, params VisualElement[] children) where T : VisualElement
        {
            float gap = parent.userData is float g ? g : 0;
            bool row = parent.resolvedStyle.flexDirection == FlexDirection.Row || parent.style.flexDirection == FlexDirection.Row;
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
    }

    /// <summary>Element factory.</summary>
    public static class E
    {
        public static VisualElement Div(string name = null) => new VisualElement { name = name };

        public static Label Text(string text, float size = 13, Color? color = null, Theme.F font = Theme.F.Text, float spacing = 0)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color ?? Theme.Ink;
            l.style.unityFontDefinition = Theme.Font(font);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            if (spacing != 0) l.style.letterSpacing = spacing;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static Label Eyebrow(string text, Color? color = null, float size = 10.5f) =>
            Text(text.ToUpperInvariant(), size, color ?? Theme.Gold, Theme.F.TextBold, 1.2f);

        public static Label Title(string text, float size = 22) => Text(text, size, Theme.Ink, Theme.F.Display);
        public static Label Mono(string text, float size = 12, Color? color = null) => Text(text, size, color ?? Theme.Ink, Theme.F.Mono);

        public static Label NoWrap(this Label l)
        {
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
            return l;
        }

        public static Label Align(this Label l, TextAnchor a) { l.style.unityTextAlign = a; return l; }

        public static VisualElement Icon(string name, float size, Color color)
        {
            var e = Div().Size(size, size).Ignore();
            var tex = Theme.Icon(name);
            if (tex != null) e.BgImage(tex, color);
            return e;
        }

        public static VisualElement Image(Texture2D tex, float w, float h)
        {
            var e = Div().Size(w, h).Ignore();
            if (tex != null) e.BgImage(tex);
            return e;
        }

        /// <summary>Thin progress track; returns the fill so it can be bound.</summary>
        public static VisualElement Bar(out VisualElement fill, float height = 5, Texture2D fillTex = null, Color? fillColor = null)
        {
            var track = Div().Ignore().Radius(height).Bg(Theme.Track);
            track.style.height = height;
            track.style.overflow = Overflow.Hidden;
            fill = Div().Ignore().Radius(height);
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(0);
            if (fillTex != null) fill.BgImage(fillTex); else fill.Bg(fillColor ?? Theme.Gold);
            track.Add(fill);
            return track;
        }

        public static void SetFill(VisualElement fill, double value) =>
            fill.style.width = Length.Percent(Mathf.Clamp01((float)value) * 100f);

        public enum Kind { Gold, Glass, Ghost, Danger, Lavender, Mint, Disabled }

        /// <summary>Pill button.</summary>
        public static VisualElement Button(string label, Action onTap, Kind kind = Kind.Gold, float height = 44, string icon = null, float fontSize = 14)
        {
            var b = Div("button").Row().Center().Radius(height / 2).Pad(0, 18);
            b.style.height = height;
            b.style.flexShrink = 0;
            Color text;
            switch (kind)
            {
                case Kind.Gold: b.BgImage(Theme.GoldFill); text = Theme.OnGold; break;
                case Kind.Lavender: b.BgImage(Theme.LavenderFill); text = Theme.OnGold; break;
                case Kind.Mint: b.BgImage(Theme.MintFill); text = Hex("#0D2A1F"); break;
                case Kind.Danger: b.Bg(Theme.Alpha(Theme.Error, 0.08f)).Border(Theme.Alpha(Theme.Error, 0.32f)); text = Theme.Error; break;
                case Kind.Ghost: b.Bg(Theme.Fill).Border(Theme.Edge); text = Theme.Body; break;
                case Kind.Disabled: b.Bg(new Color(1, 1, 1, 0.06f)).Border(Theme.EdgeSoft); text = Theme.Muted; break;
                default: b.Glass(height / 2); text = Theme.Ink; break;
            }
            if (icon != null)
            {
                var i = Icon(icon, fontSize + 2, text);
                i.style.marginRight = 7;
                b.Add(i);
            }
            b.Add(Text(label, fontSize, text, Theme.F.TextBold).NoWrap());
            if (onTap != null && kind != Kind.Disabled) b.OnTap(onTap);
            else b.pickingMode = PickingMode.Position; // still swallows taps
            return b;
        }

        static Color Hex(string h) => Theme.Hex(h);

        /// <summary>Round glass icon button (back, close, settings…).</summary>
        public static VisualElement IconButton(string icon, Action onTap, float size = 40, Color? tint = null, bool solid = false)
        {
            var b = Div("icon-button").Size(size, size).Center().Radius(size / 2);
            if (solid) b.Bg(new Color(0.05f, 0.06f, 0.08f, 0.6f)).Border(Theme.Edge);
            else b.Glass(size / 2);
            b.Add(Icon(icon, size * 0.45f, tint ?? Theme.Ink));
            b.OnTap(onTap);
            return b;
        }

        /// <summary>
        /// Vertical scroll view exactly as tall as its content, which flex-shrinks (and then scrolls) when its
        /// column runs out of room: for cards and dialog panes that must never be taller than the short
        /// landscape screen. The height is set from the content's measured size, so it works whether or not
        /// the scroll view would size itself to its content.
        /// </summary>
        public static ScrollView FitScroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            s.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
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

        /// <summary>Small numeric badge.</summary>
        public static VisualElement Badge(string text, Color bg, Color fg)
        {
            var b = Div().Ignore().Center().Radius(9).Bg(bg).Pad(0, 5);
            b.style.minWidth = 18;
            b.style.height = 18;
            b.Add(Text(text, 10, fg, Theme.F.TextBold).NoWrap());
            return b;
        }

        /// <summary>Resource cost chips: red when the stockpile can't cover it.</summary>
        public static VisualElement Chip(string text, Color fg, Color? bg = null)
        {
            var c = Div().Ignore().Row().Radius(999).Pad(4, 9).Bg(bg ?? new Color(1, 1, 1, 0.06f));
            c.Add(Mono(text, 11, fg));
            return c;
        }
    }
}
