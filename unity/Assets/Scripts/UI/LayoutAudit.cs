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

        public static List<string> Check(VisualElement root)
        {
            var found = new List<string>();
            if (root?.panel == null) return found;
            var screen = root.worldBound;
            var texts = new List<TextElement>();
            var pictures = new List<VisualElement>();
            Walk(root, null, false, screen, found, texts, pictures);

            // Text over text, icon over text (neither inside the other).
            for (int i = 0; i < texts.Count; i++)
            {
                var a = Ink(texts[i]);
                for (int j = i + 1; j < texts.Count; j++)
                {
                    if (Related(texts[i], texts[j])) continue;
                    if (Overlap(a, Ink(texts[j])) > 0.15f)
                        found.Add($"text over text: {Name(texts[i])} / {Name(texts[j])}");
                }
                foreach (var p in pictures)
                {
                    if (Related(texts[i], p) || Covers(p, texts[i])) continue;
                    if (Overlap(a, p.worldBound) > 0.2f)
                        found.Add($"picture over text: {Name(p)} on {Name(texts[i])}");
                }
            }
            return found;
        }

        static void Walk(VisualElement e, VisualElement frame, bool clipped, Rect screen, List<string> found,
                         List<TextElement> texts, List<VisualElement> pictures)
        {
            var rs = e.resolvedStyle;
            if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden || rs.opacity < 0.05f) return;
            var r = e.worldBound;
            if (r.width < 1 || r.height < 1 || float.IsNaN(r.x)) return;

            var bgi = e.style.backgroundImage.value;
            bool picture = bgi.texture != null || bgi.sprite != null || bgi.vectorImage != null;
            if (!clipped && (r.xMin < screen.xMin - Slack || r.xMax > screen.xMax + Slack ||
                             r.yMin < screen.yMin - Slack || r.yMax > screen.yMax + Slack) &&
                (e is TextElement || picture) && r.width < screen.width * 0.9f)
                found.Add($"off screen: {Name(e)}");

            if (e is TextElement te && !string.IsNullOrEmpty(te.text) && e.childCount == 0)
            {
                var box = te.contentRect;
                var one = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                bool wraps = rs.whiteSpace == WhiteSpace.Normal;
                if (!wraps && one.x > box.width + Slack && rs.textOverflow != TextOverflow.Ellipsis)
                    found.Add($"text too wide ({one.x:0} > {box.width:0}): {Name(e)}");
                if (wraps)
                {
                    var multi = te.MeasureTextSize(te.text, box.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    if (multi.y > box.height + Slack + 1)
                        found.Add($"text too tall ({multi.y:0} > {box.height:0}): {Name(e)}");
                }
                texts.Add(te);
            }
            else if (picture && e.childCount == 0 && r.width < 160 && r.height < 160)
                pictures.Add(e);

            if (frame != null && !clipped && (e is TextElement || picture))
            {
                var f = frame.worldBound;
                if (r.xMin < f.xMin - Slack || r.xMax > f.xMax + Slack || r.yMin < f.yMin - Slack || r.yMax > f.yMax + Slack)
                    found.Add($"sticks out of its frame: {Name(e)} out of {Name(frame)}");
            }

            // A framed box (one with a picture behind it) is the frame its contents must stay in.
            var nextFrame = picture && e.childCount > 0 ? e : frame;
            bool nextClipped = clipped || e is ScrollView || e.style.overflow.value == Overflow.Hidden;
            if (nextClipped) nextFrame = null;
            foreach (var c in e.Children()) Walk(c, nextFrame, nextClipped, screen, found, texts, pictures);
        }

        /// <summary>Where a label's letters actually are (its box can be wider than its text).</summary>
        static Rect Ink(TextElement t)
        {
            var r = t.worldBound;
            var s = t.MeasureTextSize(t.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            float w = Mathf.Min(r.width, s.x), h = Mathf.Min(r.height, s.y);
            float x = r.x, y = r.y + (r.height - h) * 0.5f;
            var align = t.resolvedStyle.unityTextAlign;
            if (align == TextAnchor.UpperCenter || align == TextAnchor.MiddleCenter || align == TextAnchor.LowerCenter) x += (r.width - w) * 0.5f;
            else if (align == TextAnchor.UpperRight || align == TextAnchor.MiddleRight || align == TextAnchor.LowerRight) x += r.width - w;
            return new Rect(x, y, w, h);
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
