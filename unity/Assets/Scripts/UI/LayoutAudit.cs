using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Looks for layout mistakes on the screen as it is drawn (used by the capture tour, next to every
    /// screenshot): text that does not fit its box, text or icons that stick out of the framed box they
    /// sit in, an icon or text drawn over another text, and anything off the screen. Each finding is one
    /// line, so a capture run can list every broken screen without anyone having to spot it by eye.
    /// </summary>
    public static class LayoutAudit
    {
        const float Slack = 2f; // pixels of rounding we let pass

        /// <summary>A label or picture as it shows: the part inside its scroll view, and its layer (HUD, window, dialog…).</summary>
        struct Seen
        {
            public VisualElement E;
            public Rect Shown;
            public int Layer;
        }

        public static List<string> Check(VisualElement root)
        {
            var found = new List<string>();
            if (root?.panel == null) return found;
            var screen = root.worldBound;
            var texts = new List<Seen>();
            var pictures = new List<Seen>();
            int layer = 0;
            // Each child of the root is a layer drawn over the ones before it (HUD, windows, dialogs, loading):
            // only things on the same layer can collide; a higher layer covers a lower one on purpose.
            foreach (var l in root.Children()) Walk(l, null, screen, screen, layer++, false, found, texts, pictures);

            for (int i = 0; i < texts.Count; i++)
            {
                var a = Ink(texts[i]);
                for (int j = i + 1; j < texts.Count; j++)
                {
                    if (texts[i].Layer != texts[j].Layer || Related(texts[i].E, texts[j].E)) continue;
                    if (Overlap(a, Ink(texts[j])) > 0.15f)
                        found.Add($"text over text: {Name(texts[i].E)} / {Name(texts[j].E)}");
                }
                foreach (var p in pictures)
                {
                    if (p.Layer != texts[i].Layer || Related(texts[i].E, p.E) || Covers(p.E, texts[i].E)) continue;
                    if (Overlap(a, p.Shown) > 0.35f)
                        found.Add($"picture over text: {Name(p.E)} on {Name(texts[i].E)}");
                }
            }
            return found;
        }

        static void Walk(VisualElement e, VisualElement frame, Rect clip, Rect screen, int layer, bool badge, List<string> found,
                         List<Seen> texts, List<Seen> pictures)
        {
            var rs = e.resolvedStyle;
            if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden || rs.opacity < 0.05f) return;
            var r = e.worldBound;
            if (r.width < 1 || r.height < 1 || float.IsNaN(r.x)) return;
            // Badges (a count or "!" on a corner) sit over their button on purpose.
            badge |= e.name == "badge";
            var shown = Intersect(r, clip);

            var bgi = e.style.backgroundImage.value;
            bool picture = bgi.texture != null || bgi.sprite != null || bgi.vectorImage != null;
            if ((r.xMin < screen.xMin - Slack || r.xMax > screen.xMax + Slack ||
                 r.yMin < screen.yMin - Slack || r.yMax > screen.yMax + Slack) &&
                (e is TextElement || picture) && r.width < screen.width * 0.9f && shown.width > 0)
                found.Add($"off screen: {Name(e)}");

            bool visible = shown.width > 1 && shown.height > 1;
            if (e is TextElement te && !string.IsNullOrEmpty(te.text) && e.childCount == 0)
            {
                var box = te.contentRect;
                var one = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                bool wraps = rs.whiteSpace == WhiteSpace.Normal;
                if (visible && !wraps && one.x > box.width + Slack && rs.textOverflow != TextOverflow.Ellipsis)
                    found.Add($"text too wide ({one.x:0} > {box.width:0}): {Name(e)}");
                if (visible && wraps)
                {
                    var multi = te.MeasureTextSize(te.text, box.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    if (multi.y > box.height + Slack + 1)
                        found.Add($"text too tall ({multi.y:0} > {box.height:0}): {Name(e)}");
                }
                if (visible && !badge) texts.Add(new Seen { E = e, Shown = shown, Layer = layer });
            }
            else if (picture && e.childCount == 0 && r.width < 160 && r.height < 160 && visible && !badge)
                pictures.Add(new Seen { E = e, Shown = shown, Layer = layer });

            // Out of its frame: any text by more than a hair; a picture by more than a third of itself
            // (icons that overhang their counter a little are drawn that way).
            if (frame != null && visible && !badge && (e is TextElement || picture))
            {
                var f = frame.worldBound;
                float ox = Mathf.Max(0, f.xMin - r.xMin) + Mathf.Max(0, r.xMax - f.xMax);
                float oy = Mathf.Max(0, f.yMin - r.yMin) + Mathf.Max(0, r.yMax - f.yMax);
                bool text = e is TextElement;
                if (text ? ox > Slack || oy > Slack : ox > r.width * 0.35f || oy > r.height * 0.35f)
                    found.Add($"sticks out of its frame: {Name(e)} out of {Name(frame)}");
            }

            // A framed box (one with a picture behind it) is the frame its contents must stay in;
            // a scroll view or a clipping box cuts what is inside it (that part is not shown, so not checked).
            var nextFrame = picture && e.childCount > 0 ? e : frame;
            var nextClip = clip;
            if (e is ScrollView sv) { nextClip = Intersect(clip, sv.contentViewport.worldBound); nextFrame = null; }
            else if (e.style.overflow.value == Overflow.Hidden) { nextClip = Intersect(clip, r); nextFrame = null; }
            foreach (var c in e.Children()) Walk(c, nextFrame, nextClip, screen, layer, badge, found, texts, pictures);
        }

        static Rect Intersect(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin);
            float x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }

        /// <summary>Where a label's letters actually are (its box can be wider than its text).</summary>
        static Rect Ink(Seen seen)
        {
            var t = (TextElement)seen.E;
            var r = t.worldBound;
            var s = t.MeasureTextSize(t.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            float w = Mathf.Min(r.width, s.x), h = Mathf.Min(r.height, s.y);
            float x = r.x, y = r.y + (r.height - h) * 0.5f;
            var align = t.resolvedStyle.unityTextAlign;
            if (align == TextAnchor.UpperCenter || align == TextAnchor.MiddleCenter || align == TextAnchor.LowerCenter) x += (r.width - w) * 0.5f;
            else if (align == TextAnchor.UpperRight || align == TextAnchor.MiddleRight || align == TextAnchor.LowerRight) x += r.width - w;
            return Intersect(new Rect(x, y, w, h), seen.Shown);
        }

        /// <summary>A picture that is the background of a whole label row (a badge behind its number) is fine.</summary>
        static bool Covers(VisualElement p, VisualElement t) => p.worldBound.Contains(t.worldBound.min) && p.worldBound.Contains(t.worldBound.max);

        static bool Related(VisualElement a, VisualElement b) => a == b || a.FindCommonAncestor(b) == a || a.FindCommonAncestor(b) == b;

        /// <summary>Overlapping area as a share of the smaller rectangle.</summary>
        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (w <= 0 || h <= 0) return 0;
            return w * h / Mathf.Max(1f, Mathf.Min(a.width * a.height, b.width * b.height));
        }

        static string Name(VisualElement e)
        {
            var sb = new StringBuilder();
            if (e is TextElement t && !string.IsNullOrEmpty(t.text))
                sb.Append('"').Append(t.text.Length > 30 ? t.text.Substring(0, 30) + "…" : t.text).Append('"');
            else
            {
                var bg = e.style.backgroundImage.value.texture;
                sb.Append(bg != null ? "[" + bg.name + "]" : string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name);
            }
            // Nearest named ancestors say where on the screen it is.
            int n = 0;
            for (var p = e.parent; p != null && n < 2; p = p.parent)
                if (!string.IsNullOrEmpty(p.name)) { sb.Append(" in ").Append(p.name); n++; }
            var r = e.worldBound;
            sb.Append($" @{r.x:0},{r.y:0} {r.width:0}x{r.height:0}");
            return sb.ToString();
        }
    }
}
