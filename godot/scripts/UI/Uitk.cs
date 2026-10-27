using System;
using System.Collections.Generic;
using Godot;

namespace Founders.UI
{
    // A small UI Toolkit work-alike on Godot Controls: the Unity interface was built from code as flexbox
    // elements with inline styles; the same code runs here on these types. VisualElement is a Control that
    // lays out its children itself (a compact flexbox: direction, wrap, grow/shrink/basis, align, justify,
    // margins, padding, min/max, percentages, absolute positioning) and draws its own background (colour,
    // rounded border, picture or 9-slice skin). Label draws outlined, coloured rich text.

    public enum StyleKeyword { Null = 0, Undefined, Auto, None, Initial }
    public enum LengthUnit { Pixel, Percent }
    public enum FlexDirection { Column, Row, ColumnReverse, RowReverse }
    public enum Align { Auto, FlexStart, Center, FlexEnd, Stretch }
    public enum Justify { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly }
    public enum Position { Relative, Absolute }
    public enum DisplayStyle { Flex, None }
    public enum WhiteSpace { Normal, NoWrap }
    public enum Wrap { NoWrap, Wrap, WrapReverse }
    public enum Overflow { Visible, Hidden }
    public enum PickingMode { Position, Ignore }
    public enum TextOverflow { Clip, Ellipsis }
    public enum SliceType { Sliced, Tiled }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum ScrollViewMode { Vertical, Horizontal, VerticalAndHorizontal }
    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

    public struct Length
    {
        public float value;
        public LengthUnit unit;
        public Length(float v, LengthUnit u = LengthUnit.Pixel) { value = v; unit = u; }
        public static Length Percent(float v) => new Length(v, LengthUnit.Percent);
        public static implicit operator Length(float v) => new Length(v);
    }

    public struct StyleLength : IEquatable<StyleLength>
    {
        public Length value;
        public StyleKeyword keyword;
        public StyleLength(float v) { value = new Length(v); keyword = StyleKeyword.Undefined; }
        public StyleLength(Length v) { value = v; keyword = StyleKeyword.Undefined; }
        public StyleLength(StyleKeyword k) { value = default; keyword = k; }
        public static implicit operator StyleLength(float v) => new StyleLength(v);
        public static implicit operator StyleLength(Length v) => new StyleLength(v);
        public static implicit operator StyleLength(StyleKeyword k) => new StyleLength(k);
        /// <summary>A length was given (not auto, not unset).</summary>
        public bool IsSet => keyword == StyleKeyword.Undefined;
        public bool Equals(StyleLength o) => keyword == o.keyword && value.value == o.value.value && value.unit == o.value.unit;
        public override bool Equals(object o) => o is StyleLength s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(keyword, value.value, value.unit);
        public static bool operator ==(StyleLength a, StyleLength b) => a.Equals(b);
        public static bool operator !=(StyleLength a, StyleLength b) => !a.Equals(b);
    }

    public struct StyleFloat : IEquatable<StyleFloat>
    {
        public float value;
        public StyleKeyword keyword;
        public StyleFloat(float v) { value = v; keyword = StyleKeyword.Undefined; }
        public StyleFloat(StyleKeyword k) { value = 0; keyword = k; }
        public static implicit operator StyleFloat(float v) => new StyleFloat(v);
        public static implicit operator StyleFloat(StyleKeyword k) => new StyleFloat(k);
        public bool IsSet => keyword == StyleKeyword.Undefined;
        public float Or(float fallback) => IsSet ? value : fallback;
        public bool Equals(StyleFloat o) => keyword == o.keyword && value == o.value;
        public override bool Equals(object o) => o is StyleFloat s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(keyword, value);
        public static bool operator ==(StyleFloat a, StyleFloat b) => a.Equals(b);
        public static bool operator !=(StyleFloat a, StyleFloat b) => !a.Equals(b);
    }

    public struct StyleInt
    {
        public int value;
        public StyleKeyword keyword;
        public StyleInt(int v) { value = v; keyword = StyleKeyword.Undefined; }
        public static implicit operator StyleInt(int v) => new StyleInt(v);
    }

    public struct StyleColor : IEquatable<StyleColor>
    {
        public Color value;
        public StyleKeyword keyword;
        public StyleColor(Color c) { value = c; keyword = StyleKeyword.Undefined; }
        public StyleColor(StyleKeyword k) { value = default; keyword = k; }
        public static implicit operator StyleColor(Color c) => new StyleColor(c);
        public static implicit operator StyleColor(StyleKeyword k) => new StyleColor(k);
        public bool IsSet => keyword == StyleKeyword.Undefined;
        public bool Equals(StyleColor o) => keyword == o.keyword && value == o.value;
        public override bool Equals(object o) => o is StyleColor s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(keyword, value);
        public static bool operator ==(StyleColor a, StyleColor b) => a.Equals(b);
        public static bool operator !=(StyleColor a, StyleColor b) => !a.Equals(b);
    }

    public struct StyleEnum<T> : IEquatable<StyleEnum<T>> where T : struct, Enum
    {
        public T value;
        public StyleKeyword keyword;
        public StyleEnum(T v) { value = v; keyword = StyleKeyword.Undefined; }
        public StyleEnum(StyleKeyword k) { value = default; keyword = k; }
        public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T>(v);
        public static implicit operator StyleEnum<T>(StyleKeyword k) => new StyleEnum<T>(k);
        public bool IsSet => keyword == StyleKeyword.Undefined;
        public T Or(T fallback) => IsSet ? value : fallback;
        public bool Equals(StyleEnum<T> o) => keyword == o.keyword && EqualityComparer<T>.Default.Equals(value, o.value);
        public override bool Equals(object o) => o is StyleEnum<T> s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(keyword, value);
        public static bool operator ==(StyleEnum<T> a, StyleEnum<T> b) => a.Equals(b);
        public static bool operator !=(StyleEnum<T> a, StyleEnum<T> b) => !a.Equals(b);
    }

    public struct StyleBackground
    {
        public Texture2D texture;
        public StyleBackground(Texture2D t) { texture = t; }
        public StyleBackground(StyleKeyword k) { texture = null; }
        public static implicit operator StyleBackground(Texture2D t) => new StyleBackground(t);
        public static implicit operator StyleBackground(StyleKeyword k) => default;
    }

    public enum BackgroundSizeType { Length, Cover, Contain }

    public struct BackgroundSize
    {
        public BackgroundSizeType sizeType;
        public Length x, y;
        public BackgroundSize(Length x, Length y) { this.x = x; this.y = y; sizeType = BackgroundSizeType.Length; }
        public BackgroundSize(BackgroundSizeType t) { x = default; y = default; sizeType = t; }
    }

    public enum BackgroundPositionKeyword { Center, Top, Bottom, Left, Right }

    public struct BackgroundPosition
    {
        public BackgroundPositionKeyword keyword;
        public Length offset;
        public BackgroundPosition(BackgroundPositionKeyword k) { keyword = k; offset = default; }
        public BackgroundPosition(BackgroundPositionKeyword k, Length o) { keyword = k; offset = o; }
    }

    public struct TextShadow
    {
        public Vector2 offset;
        public float blurRadius;
        public Color color;
    }

    public struct StyleTextShadow
    {
        public TextShadow value;
        public bool set;
        public static implicit operator StyleTextShadow(TextShadow t) => new StyleTextShadow { value = t, set = true };
        public static implicit operator StyleTextShadow(StyleKeyword k) => default;
    }

    public struct Translate
    {
        public Length x, y;
        public Translate(Length x, Length y) { this.x = x; this.y = y; }
    }

    public struct StyleTranslate
    {
        public Translate value;
        public bool set;
        public static implicit operator StyleTranslate(Translate t) => new StyleTranslate { value = t, set = true };
        public static implicit operator StyleTranslate(StyleKeyword k) => default;
    }

    public struct Scale
    {
        public Vector2 value;
        public Scale(Vector2 v) { value = v; }
        public Scale(Vector3 v) { value = new Vector2(v.X, v.Y); }
    }

    public struct StyleScale
    {
        public Scale value;
        public bool set;
        public static implicit operator StyleScale(Scale s) => new StyleScale { value = s, set = true };
        public static implicit operator StyleScale(StyleKeyword k) => default;
    }

    public enum AngleUnit { Degree, Radian }

    public struct Angle
    {
        public float value;
        public Angle(float degrees) { value = degrees; }
        public Angle(float v, AngleUnit unit) { value = unit == AngleUnit.Radian ? Mathf.RadToDeg(v) : v; }
    }

    public struct Rotate
    {
        public Angle angle;
        public Rotate(Angle a) { angle = a; }
    }

    public struct StyleRotate
    {
        public Rotate value;
        public bool set;
        public static implicit operator StyleRotate(Rotate r) => new StyleRotate { value = r, set = true };
        public static implicit operator StyleRotate(StyleKeyword k) => default;
    }

    /// <summary>Unity's Rect (x, y, width, height) for layout results.</summary>
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 size => new Vector2(width, height);
        public Vector2 position => new Vector2(x, y);
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public bool Contains(Vector2 p) => p.X >= x && p.X <= x + width && p.Y >= y && p.Y <= y + height;
        public static bool operator ==(Rect a, Rect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override bool Equals(object o) => o is Rect r && r == this;
        public override int GetHashCode() => HashCode.Combine(x, y, width, height);
    }

    /// <summary>The inline style of an element (UI Toolkit's names).</summary>
    public sealed class IStyle
    {
        public StyleLength width, height, minWidth, minHeight, maxWidth, maxHeight;
        public StyleLength left, top, right, bottom;
        public StyleLength marginLeft, marginTop, marginRight, marginBottom;
        public StyleLength paddingLeft, paddingTop, paddingRight, paddingBottom;
        public StyleLength flexBasis, fontSize, letterSpacing;
        public StyleLength borderTopLeftRadius, borderTopRightRadius, borderBottomLeftRadius, borderBottomRightRadius;
        public StyleFloat borderLeftWidth, borderTopWidth, borderRightWidth, borderBottomWidth;
        public StyleFloat flexGrow, flexShrink, opacity;
        public StyleFloat unitySliceScale, unityTextOutlineWidth;
        public StyleInt unitySliceLeft, unitySliceTop, unitySliceRight, unitySliceBottom;
        public StyleEnum<FlexDirection> flexDirection;
        public StyleEnum<Align> alignItems, alignSelf, alignContent;
        public StyleEnum<Justify> justifyContent;
        public StyleEnum<Wrap> flexWrap;
        public StyleEnum<Position> position;
        public StyleEnum<DisplayStyle> display;
        public StyleEnum<WhiteSpace> whiteSpace;
        public StyleEnum<Overflow> overflow;
        public StyleEnum<TextAnchor> unityTextAlign;
        public StyleEnum<FontStyle> unityFontStyleAndWeight;
        public StyleEnum<TextOverflow> textOverflow;
        public StyleEnum<SliceType> unitySliceType;
        public StyleColor color, backgroundColor, unityBackgroundImageTintColor, unityTextOutlineColor;
        public StyleColor borderLeftColor, borderTopColor, borderRightColor, borderBottomColor;
        public StyleBackground backgroundImage;
        public BackgroundSize backgroundSize;
        public BackgroundPosition backgroundPositionX, backgroundPositionY;
        public StyleTextShadow textShadow;
        public StyleTranslate translate;
        public StyleScale scale;
        public StyleRotate rotate;
        public Font unityFontDefinition;
    }

    /// <summary>The resolved geometry of an element (what UI Toolkit calls resolvedStyle).</summary>
    public readonly struct Resolved
    {
        readonly VisualElement e;
        public Resolved(VisualElement e) { this.e = e; }
        public float width => e.Size.X;
        public float height => e.Size.Y;
        public float paddingBottom => e.style.paddingBottom.IsSet ? e.style.paddingBottom.value.value : 0;
        public float paddingTop => e.style.paddingTop.IsSet ? e.style.paddingTop.value.value : 0;
        public float opacity => e.style.opacity.Or(1);
    }

    public abstract class EventBase { }
    public sealed class AttachToPanelEvent : EventBase { }
    public sealed class DetachFromPanelEvent : EventBase { }
    public sealed class GeometryChangedEvent : EventBase
    {
        public Rect oldRect, newRect;
    }

    /// <summary>Delayed work (UI Toolkit's element.schedule).</summary>
    public sealed class Scheduler
    {
        internal static readonly List<(double at, VisualElement owner, Action act)> Pending = new List<(double, VisualElement, Action)>();
        readonly VisualElement owner;
        public Scheduler(VisualElement owner) { this.owner = owner; }

        public sealed class Item
        {
            readonly VisualElement owner;
            readonly Action act;
            public Item(VisualElement owner, Action act) { this.owner = owner; this.act = act; Pending.Add((UiClock.Now, owner, act)); }
            /// <summary>Runs it after a delay instead of on the next frame.</summary>
            public Item StartingIn(long ms)
            {
                for (int i = Pending.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(Pending[i].act, act)) { Pending[i] = (UiClock.Now + ms / 1000.0, owner, act); break; }
                return this;
            }
        }

        public Item Execute(Action a) => new Item(owner, a);

        internal static void Run()
        {
            if (Pending.Count == 0) return;
            double now = UiClock.Now;
            for (int i = 0; i < Pending.Count; i++)
            {
                var (at, el, act) = Pending[i];
                if (at > now) continue;
                Pending.RemoveAt(i--);
                if (el != null && !GodotObject.IsInstanceValid(el)) continue;
                try { act(); } catch (Exception e) { GD.PrintErr("[FOUNDERS] UI: " + e); }
            }
        }
    }

    public static class UiClock
    {
        public static double Now => Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>A flexbox element.</summary>
    public partial class VisualElement : Control
    {
        public readonly IStyle style = new IStyle();
        public string name;
        public object userData;
        HashSet<string> classes;
        PickingMode picking = PickingMode.Position;
        Scheduler sched;
        List<Action<AttachToPanelEvent>> onAttach;
        List<Action<GeometryChangedEvent>> onGeometry;
        internal Action tap;
        Rect lastRect;

        public VisualElement()
        {
            MouseFilter = MouseFilterEnum.Pass;
            FocusMode = FocusModeEnum.None;
        }

        public PickingMode pickingMode
        {
            get => picking;
            set { picking = value; MouseFilter = value == PickingMode.Ignore ? MouseFilterEnum.Ignore : MouseFilterEnum.Pass; }
        }

        public Scheduler schedule => sched ??= new Scheduler(this);
        public IStyle resolvedStyleSource => style;
        public Resolved resolvedStyle => new Resolved(this);
        public Rect layout => new Rect(Position.X, Position.Y, Size.X, Size.Y);
        public Rect worldBound { get { var r = GetGlobalRect(); return new Rect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y); } }
        public Rect contentRect => new Rect(0, 0, Size.X, Size.Y);
        public bool visible { get => Visible; set => Visible = value; }
        public bool focusable { get; set; }
        /// <summary>The element's own children (a scroll view's decorations, not its content).</summary>
        public Hierarchy hierarchy => new Hierarchy(this);

        public readonly struct Hierarchy
        {
            readonly VisualElement owner;
            public Hierarchy(VisualElement owner) { this.owner = owner; }
            public void Add(VisualElement child)
            {
                if (child.GetParent() != null) child.GetParent().RemoveChild(child);
                owner.AddChild(child);
                LayoutDirty = true;
            }
        }

        /// <summary>Where children go (a scroll view's content).</summary>
        public virtual VisualElement contentContainer => this;

        public VisualElement parent
        {
            get
            {
                for (var p = GetParent(); p != null; p = p.GetParent())
                {
                    if (p is VisualElement v)
                    {
                        // A scroll view's content counts as the scroll view's child.
                        return v;
                    }
                    if (!(p is ScrollContainer)) return null;
                }
                return null;
            }
        }

        // MARK: - Classes

        public void AddToClassList(string c) => (classes ??= new HashSet<string>()).Add(c);
        public void RemoveFromClassList(string c) => classes?.Remove(c);
        public bool ClassListContains(string c) => classes != null && classes.Contains(c);
        public void EnableInClassList(string c, bool on) { if (on) AddToClassList(c); else RemoveFromClassList(c); }

        // MARK: - Children

        public virtual void Add(VisualElement child)
        {
            if (child == null) return;
            var target = contentContainer;
            if (child.GetParent() != null) child.GetParent().RemoveChild(child);
            if (target == this) AddChild(child);
            else target.Add(child);
            LayoutDirty = true;
        }

        public void Insert(int index, VisualElement child)
        {
            var target = contentContainer;
            if (target != this) { target.Insert(index, child); return; }
            if (child.GetParent() != null) child.GetParent().RemoveChild(child);
            AddChild(child);
            int n = 0;
            for (int i = 0; i < GetChildCount(); i++)
            {
                if (!(GetChild(i) is VisualElement)) continue;
                if (n++ == index) { MoveChild(child, i); break; }
            }
        }

        public void Remove(VisualElement child)
        {
            if (child?.GetParent() != null) child.GetParent().RemoveChild(child);
            LayoutDirty = true;
        }

        public void RemoveFromHierarchy() => GetParent()?.RemoveChild(this);

        /// <summary>Removes and frees every child.</summary>
        public virtual void Clear()
        {
            var target = contentContainer;
            if (target != this) { target.Clear(); return; }
            for (int i = GetChildCount() - 1; i >= 0; i--)
            {
                if (GetChild(i) is VisualElement v)
                {
                    RemoveChild(v);
                    v.QueueFree();
                }
            }
            LayoutDirty = true;
        }

        public int childCount
        {
            get
            {
                var target = contentContainer;
                int n = 0;
                for (int i = 0; i < target.GetChildCount(); i++) if (target.GetChild(i) is VisualElement) n++;
                return n;
            }
        }

        public IEnumerable<VisualElement> Children()
        {
            var target = contentContainer;
            for (int i = 0; i < target.GetChildCount(); i++)
                if (target.GetChild(i) is VisualElement v) yield return v;
        }

        public VisualElement ElementAt(int index)
        {
            int n = 0;
            foreach (var c in Children()) if (n++ == index) return c;
            return null;
        }

        public VisualElement this[int index] => ElementAt(index);

        public int IndexOf(VisualElement child)
        {
            int n = 0;
            foreach (var c in Children()) { if (c == child) return n; n++; }
            return -1;
        }

        public void BringToFront() => GetParent()?.MoveChild(this, -1);
        public void SendToBack() => GetParent()?.MoveChild(this, 0);

        /// <summary>The first descendant with a name (or any, when null) of a type.</summary>
        public T Q<T>(string name = null, string className = null) where T : VisualElement
        {
            foreach (var c in Children())
            {
                if (c is T t && (name == null || c.name == name) && (className == null || c.ClassListContains(className))) return t;
                var deep = c.Q<T>(name, className);
                if (deep != null) return deep;
            }
            return null;
        }

        public VisualElement Q(string name = null, string className = null) => Q<VisualElement>(name, className);

        // MARK: - Events

        public void RegisterCallback<T>(Action<T> callback) where T : EventBase
        {
            if (callback is Action<AttachToPanelEvent> a) { (onAttach ??= new List<Action<AttachToPanelEvent>>()).Add(a); if (IsInsideTree()) a(new AttachToPanelEvent()); }
            else if (callback is Action<GeometryChangedEvent> g) (onGeometry ??= new List<Action<GeometryChangedEvent>>()).Add(g);
        }

        public void UnregisterCallback<T>(Action<T> callback) where T : EventBase
        {
            if (callback is Action<AttachToPanelEvent> a) onAttach?.Remove(a);
            else if (callback is Action<GeometryChangedEvent> g) onGeometry?.Remove(g);
        }

        public override void _EnterTree()
        {
            if (onAttach == null) return;
            foreach (var a in onAttach.ToArray()) a(new AttachToPanelEvent());
        }

        public void MarkDirtyRepaint() => QueueRedraw();

        // MARK: - Taps

        static VisualElement pressed;
        static Vector2 pressAt;
        static InputEvent claimedPress, firedRelease;

        /// <summary>Movement (in units) after which a press is a drag (a scroll) rather than a tap.</summary>
        const float TapSlop = 14f;

        public override void _GuiInput(InputEvent e)
        {
            if (tap == null) return;
            if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                var at = GetGlobalTransformWithCanvas() * mb.Position;
                if (mb.Pressed)
                {
                    if (ReferenceEquals(claimedPress, e)) return;
                    claimedPress = e;
                    pressed = this;
                    pressAt = at;
                    PressLook(true);
                }
                else if (pressed == this)
                {
                    pressed = null;
                    PressLook(false);
                    if (ReferenceEquals(firedRelease, e)) return;
                    if (at.DistanceTo(pressAt) > TapSlop) return;
                    firedRelease = e;
                    var act = tap;
                    try { act(); } catch (Exception ex) { GD.PrintErr("[FOUNDERS] UI tap: " + ex); }
                }
            }
            else if (e is InputEventMouseMotion mm && pressed == this)
            {
                var at = GetGlobalTransformWithCanvas() * mm.Position;
                if (at.DistanceTo(pressAt) > TapSlop) { PressLook(false); pressed = null; }
            }
        }

        /// <summary>A pressed button darkens a little.</summary>
        void PressLook(bool down) => SelfModulate = down ? new Color(0.82f, 0.84f, 0.9f) : Colors.White;

        // MARK: - Layout

        internal static int Frame;
        /// <summary>Something was added, removed or retexted: lay out on this frame.</summary>
        public static bool LayoutDirty = true;
        int measuredFrame = -1;
        float measuredAvail, measuredPW, measuredPH;
        Vector2 measured;

        internal static float Res(StyleLength l, float basis)
        {
            if (!l.IsSet) return float.NaN;
            return l.value.unit == LengthUnit.Percent ? (float.IsNaN(basis) ? float.NaN : basis * l.value.value / 100f) : l.value.value;
        }

        internal static float Res0(StyleLength l, float basis)
        {
            float v = Res(l, basis);
            return float.IsNaN(v) ? 0 : v;
        }

        internal bool InFlow => style.display.Or(DisplayStyle.Flex) != DisplayStyle.None && style.position.Or(Founders.UI.Position.Relative) != Founders.UI.Position.Absolute;
        internal bool Shown => style.display.Or(DisplayStyle.Flex) != DisplayStyle.None;

        float PadL(float pw) => Res0(style.paddingLeft, pw) + style.borderLeftWidth.Or(0);
        float PadR(float pw) => Res0(style.paddingRight, pw) + style.borderRightWidth.Or(0);
        float PadT(float pw) => Res0(style.paddingTop, pw) + style.borderTopWidth.Or(0);
        float PadB(float pw) => Res0(style.paddingBottom, pw) + style.borderBottomWidth.Or(0);

        static float Clamp(float v, float min, float max)
        {
            if (!float.IsNaN(max) && v > max) v = max;
            if (!float.IsNaN(min) && v < min) v = min;
            return v;
        }

        /// <summary>Content size of a leaf (text, a picture); containers measure their children.</summary>
        protected virtual Vector2 MeasureContent(float width, bool widthKnown) => Vector2.Zero;

        protected virtual bool IsLeaf => false;

        /// <summary>
        /// The element's border-box size with auto sides resolved from its content: <paramref name="avail"/> bounds
        /// an auto width (NaN: unbounded), <paramref name="pw"/>/<paramref name="ph"/> are the parent's inner size for percentages.
        /// </summary>
        internal Vector2 Measure(float avail, float pw, float ph)
        {
            if (measuredFrame == Frame && SameF(measuredAvail, avail) && SameF(measuredPW, pw) && SameF(measuredPH, ph)) return measured;
            float w = Res(style.width, pw), h = Res(style.height, ph);
            float minW = Res(style.minWidth, pw), maxW = Res(style.maxWidth, pw);
            float minH = Res(style.minHeight, ph), maxH = Res(style.maxHeight, ph);
            float padH = PadL(pw) + PadR(pw), padV = PadT(pw) + PadB(pw);
            if (float.IsNaN(w))
            {
                float bound = avail;
                if (!float.IsNaN(maxW) && (float.IsNaN(bound) || maxW < bound)) bound = maxW;
                float inner = float.IsNaN(bound) ? float.NaN : Mathf.Max(0, bound - padH);
                float cw = IsLeaf ? MeasureContent(inner, false).X : Flow(inner, float.NaN, false, false, false).X;
                if (!float.IsNaN(inner) && IsLeaf) cw = Mathf.Min(cw, inner);
                w = cw + padH;
            }
            w = Mathf.Max(0, Clamp(w, minW, maxW));
            if (float.IsNaN(h))
            {
                float inner = Mathf.Max(0, w - padH);
                float ch = IsLeaf ? MeasureContent(inner, true).Y : Flow(inner, float.NaN, true, false, false).Y;
                h = ch + padV;
            }
            h = Mathf.Max(0, Clamp(h, minH, maxH));
            measuredFrame = Frame;
            measuredAvail = avail; measuredPW = pw; measuredPH = ph;
            measured = new Vector2(w, h);
            return measured;
        }

        static bool SameF(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

        /// <summary>Height for a given width (border box).</summary>
        int hFrame = -1;
        float hW, hPW, hPH, hResult;

        internal float HeightFor(float w, float pw, float ph)
        {
            if (hFrame == Frame && hW == w && SameF(hPW, pw) && SameF(hPH, ph)) return hResult;
            hFrame = Frame; hW = w; hPW = pw; hPH = ph;
            return hResult = HeightForUncached(w, pw, ph);
        }

        float HeightForUncached(float w, float pw, float ph)
        {
            float h = Res(style.height, ph);
            if (float.IsNaN(h))
            {
                float padH = PadL(pw) + PadR(pw), padV = PadT(pw) + PadB(pw);
                float inner = Mathf.Max(0, w - padH);
                h = (IsLeaf ? MeasureContent(inner, true).Y : Flow(inner, float.NaN, true, false, false).Y) + padV;
            }
            return Mathf.Max(0, Clamp(h, Res(style.minHeight, ph), Res(style.maxHeight, ph)));
        }

        internal float WidthBound(float w, float pw) => Mathf.Max(0, Clamp(w, Res(style.minWidth, pw), Res(style.maxWidth, pw)));

        protected virtual IEnumerable<VisualElement> LayoutChildren()
        {
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is VisualElement v) yield return v;
        }

        sealed class FlexItem
        {
            public VisualElement E;
            public float Basis, Main, Cross, MS, ME, CS, CE, Min, Max;
            public float Grow, Shrink;
            public bool Frozen;
        }

        static readonly Stack<List<FlexItem>> pool = new Stack<List<FlexItem>>();

        /// <summary>
        /// Flexbox: lays the in-flow children into the inner box W × H (NaN = auto). <paramref name="defW"/>/<paramref name="defH"/>
        /// tell whether the size is definite (else W is only a bound). Returns the size the children use; with
        /// <paramref name="apply"/> also places them (relative to the content box).
        /// </summary>
        internal Vector2 Flow(float W, float H, bool defW, bool defH, bool apply)
        {
            var dir = style.flexDirection.Or(FlexDirection.Column);
            bool row = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
            bool reverse = dir == FlexDirection.RowReverse || dir == FlexDirection.ColumnReverse;
            var alignItems = style.alignItems.Or(Align.Stretch);
            var justify = style.justifyContent.Or(Justify.FlexStart);
            bool wrap = style.flexWrap.Or(Wrap.NoWrap) != Wrap.NoWrap;
            float mainAvail = row ? W : H, crossAvail = row ? H : W;
            bool defMain = row ? defW : defH, defCross = row ? defH : defW;

            var items = pool.Count > 0 ? pool.Pop() : new List<FlexItem>();
            items.Clear();
            foreach (var c in LayoutChildren())
            {
                if (!c.InFlow) continue;
                var s = c.style;
                var it = new FlexItem { E = c };
                float ml = Res0(s.marginLeft, W), mr = Res0(s.marginRight, W), mt = Res0(s.marginTop, W), mb = Res0(s.marginBottom, W);
                if (row) { it.MS = ml; it.ME = mr; it.CS = mt; it.CE = mb; }
                else { it.MS = mt; it.ME = mb; it.CS = ml; it.CE = mr; }
                it.Grow = s.flexGrow.Or(0);
                it.Shrink = s.flexShrink.Or(1);
                var al = c.style.alignSelf.Or(Align.Auto);
                if (al == Align.Auto) al = alignItems;
                float crossRoom = float.IsNaN(crossAvail) ? float.NaN : crossAvail - it.CS - it.CE;
                if (row)
                {
                    float b = Res(s.flexBasis, W);
                    if (float.IsNaN(b)) b = Res(s.width, W);
                    if (float.IsNaN(b)) b = c.Measure(float.IsNaN(W) ? float.NaN : Mathf.Max(0, W - it.MS - it.ME), W, H).X;
                    it.Min = Res(s.minWidth, W); it.Max = Res(s.maxWidth, W);
                    it.Basis = Clamp(b, it.Min, it.Max);
                }
                else
                {
                    // A column's child: its width first (stretched, fixed or its own), then its height for that width.
                    float cw = Res(s.width, W);
                    if (float.IsNaN(cw))
                    {
                        if (al == Align.Stretch && defW && !float.IsNaN(crossRoom)) cw = c.WidthBound(crossRoom, W);
                        else cw = c.Measure(crossRoom, W, H).X;
                    }
                    cw = c.WidthBound(cw, W);
                    it.Cross = cw;
                    float b = Res(s.flexBasis, H);
                    if (float.IsNaN(b)) b = Res(s.height, H);
                    if (float.IsNaN(b)) b = c.HeightFor(cw, W, H);
                    it.Min = Res(s.minHeight, H); it.Max = Res(s.maxHeight, H);
                    it.Basis = Clamp(b, it.Min, it.Max);
                }
                it.Main = it.Basis;
                items.Add(it);
            }

            // Lines.
            float usedMain = 0, usedCross = 0;
            int start = 0;
            float crossCursor = 0;
            var lines = new List<(int from, int to, float cross)>();
            while (start < items.Count)
            {
                int end = start;
                float sum = 0;
                while (end < items.Count)
                {
                    float sz = items[end].Basis + items[end].MS + items[end].ME;
                    if (wrap && end > start && !float.IsNaN(mainAvail) && sum + sz > mainAvail + 0.01f) break;
                    sum += sz;
                    end++;
                }
                // Grow / shrink.
                float free = float.IsNaN(mainAvail) ? 0 : mainAvail - sum;
                if (free > 0 && !defMain) free = 0;
                Resolve(items, start, end, free);
                // Cross sizes.
                float lineCross = 0, lineMain = 0;
                for (int i = start; i < end; i++)
                {
                    var it = items[i];
                    var c = it.E;
                    if (row)
                    {
                        float ch = Res(c.style.height, H);
                        var al = c.style.alignSelf.Or(Align.Auto);
                        if (al == Align.Auto) al = alignItems;
                        if (float.IsNaN(ch))
                        {
                            if (al == Align.Stretch && defCross && !wrap && !float.IsNaN(crossAvail))
                                ch = Clamp(crossAvail - it.CS - it.CE, Res(c.style.minHeight, H), Res(c.style.maxHeight, H));
                            else ch = c.HeightFor(it.Main, W, H);
                        }
                        it.Cross = Mathf.Max(0, Clamp(ch, Res(c.style.minHeight, H), Res(c.style.maxHeight, H)));
                    }
                    lineCross = Mathf.Max(lineCross, it.Cross + it.CS + it.CE);
                    lineMain += it.Main + it.MS + it.ME;
                }
                lines.Add((start, end, lineCross));
                usedMain = Mathf.Max(usedMain, lineMain);
                usedCross += lineCross;
                start = end;
            }

            if (apply)
            {
                bool single = lines.Count == 1 && !wrap;
                foreach (var (from, to, lc) in lines)
                {
                    float lineCross = single && !float.IsNaN(crossAvail) ? crossAvail : lc;
                    float lineMain = 0;
                    int n = to - from;
                    for (int i = from; i < to; i++) lineMain += items[i].Main + items[i].MS + items[i].ME;
                    float rem = float.IsNaN(mainAvail) ? 0 : mainAvail - lineMain;
                    float pos = 0, between = 0;
                    switch (justify)
                    {
                        case Justify.Center: pos = rem / 2; break;
                        case Justify.FlexEnd: pos = rem; break;
                        case Justify.SpaceBetween: if (n > 1 && rem > 0) between = rem / (n - 1); break;
                        case Justify.SpaceAround: if (rem > 0) { between = rem / n; pos = between / 2; } break;
                        case Justify.SpaceEvenly: if (rem > 0) { between = rem / (n + 1); pos = between; } break;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        var it = items[reverse ? to - 1 - k : from + k];
                        var c = it.E;
                        var al = c.style.alignSelf.Or(Align.Auto);
                        if (al == Align.Auto) al = alignItems;
                        float cross = it.Cross;
                        bool fixedCross = row ? c.style.height.IsSet : c.style.width.IsSet;
                        if (al == Align.Stretch && !fixedCross)
                        {
                            cross = lineCross - it.CS - it.CE;
                            cross = row ? Clamp(cross, Res(c.style.minHeight, H), Res(c.style.maxHeight, H)) : c.WidthBound(cross, W);
                            cross = Mathf.Max(0, cross);
                        }
                        float cpos = it.CS;
                        if (al == Align.Center) cpos = (lineCross - cross - it.CS - it.CE) / 2 + it.CS;
                        else if (al == Align.FlexEnd) cpos = lineCross - cross - it.CE;
                        float mpos = pos + it.MS;
                        var rect = row ? new Rect2(mpos, crossCursor + cpos, it.Main, cross) : new Rect2(crossCursor + cpos, mpos, cross, it.Main);
                        c.Place(rect);
                        pos += it.MS + it.Main + it.ME + between;
                    }
                    crossCursor += lineCross;
                }
            }

            items.Clear();
            pool.Push(items);
            return row ? new Vector2(usedMain, usedCross) : new Vector2(usedCross, usedMain);
        }

        static void Resolve(List<FlexItem> items, int from, int to, float free)
        {
            if (Mathf.Abs(free) < 0.01f) return;
            for (int pass = 0; pass < 3 && Mathf.Abs(free) > 0.01f; pass++)
            {
                float weight = 0;
                for (int i = from; i < to; i++)
                {
                    var it = items[i];
                    if (it.Frozen) continue;
                    weight += free > 0 ? it.Grow : it.Shrink * it.Basis;
                }
                if (weight <= 0) return;
                float left = free;
                for (int i = from; i < to; i++)
                {
                    var it = items[i];
                    if (it.Frozen) continue;
                    float share = free * (free > 0 ? it.Grow : it.Shrink * it.Basis) / weight;
                    float want = it.Main + share;
                    float got = Clamp(want, float.IsNaN(it.Min) ? 0 : it.Min, it.Max);
                    if (got < 0) got = 0;
                    if (Mathf.Abs(got - want) > 0.01f) it.Frozen = true;
                    left -= got - it.Main;
                    it.Main = got;
                }
                free = left;
            }
        }

        /// <summary>Places this element (relative to its parent's content box) and lays out its children.</summary>
        internal void Place(Rect2 rect)
        {
            var p = parent;
            float ox = 0, oy = 0;
            if (p != null && GetParent() == p)
            {
                float pw = p.Size.X;
                ox = p.PadL(pw);
                oy = p.PadT(pw);
            }
            SetFrame(new Rect2(rect.Position + new Vector2(ox, oy), rect.Size));
        }

        Vector2 basePos;

        internal void SetFrame(Rect2 r)
        {
            basePos = r.Position;
            var size = new Vector2(Mathf.Max(0, r.Size.X), Mathf.Max(0, r.Size.Y));
            if (Size != size) Size = size;
            ApplyTransform();
            LayoutInside();
            var now = new Rect(r.Position.X, r.Position.Y, size.X, size.Y);
            if (onGeometry != null && now != lastRect)
            {
                var ev = new GeometryChangedEvent { oldRect = lastRect, newRect = now };
                lastRect = now;
                foreach (var g in onGeometry.ToArray()) g(ev);
            }
            lastRect = now;
        }

        /// <summary>Lays out the children inside the current size.</summary>
        internal virtual void LayoutInside()
        {
            float W = Size.X, H = Size.Y;
            var p = parent;
            float pw = p != null ? p.Size.X : W;
            float padL = PadL(pw), padT = PadT(pw);
            float iw = Mathf.Max(0, W - padL - PadR(pw)), ih = Mathf.Max(0, H - padT - PadB(pw));
            if (!IsLeaf) Flow(iw, ih, true, true, true);
            ClipContents = style.overflow.Or(Overflow.Visible) == Overflow.Hidden;
            // Absolutely placed children: against the border box.
            float bl = style.borderLeftWidth.Or(0), bt = style.borderTopWidth.Or(0);
            float bw = W - bl - style.borderRightWidth.Or(0), bh = H - bt - style.borderBottomWidth.Or(0);
            foreach (var c in LayoutChildren())
            {
                bool shown = c.Shown;
                if (c.Visible != shown) c.Visible = shown;
                if (!shown || c.InFlow) continue;
                var s = c.style;
                float ml = Res0(s.marginLeft, bw), mr = Res0(s.marginRight, bw), mt = Res0(s.marginTop, bw), mb = Res0(s.marginBottom, bw);
                float l = Res(s.left, bw), t = Res(s.top, bh), r = Res(s.right, bw), b = Res(s.bottom, bh);
                float w = Res(s.width, bw);
                if (float.IsNaN(w)) w = !float.IsNaN(l) && !float.IsNaN(r) ? bw - l - r - ml - mr : c.Measure(bw, bw, bh).X;
                w = c.WidthBound(w, bw);
                float h = Res(s.height, bh);
                if (float.IsNaN(h)) h = !float.IsNaN(t) && !float.IsNaN(b) ? bh - t - b - mt - mb : c.HeightFor(w, bw, bh);
                h = Mathf.Max(0, Clamp(h, Res(s.minHeight, bh), Res(s.maxHeight, bh)));
                float x = !float.IsNaN(l) ? l + ml : !float.IsNaN(r) ? bw - r - w - mr : padL - bl + ml;
                float y = !float.IsNaN(t) ? t + mt : !float.IsNaN(b) ? bh - b - h - mb : padT - bt + mt;
                c.SetFrame(new Rect2(bl + x, bt + y, w, h));
            }
        }

        // MARK: - Transform, opacity, transitions

        float transitionMs;
        HashSet<string> transitionProps;
        float shownOpacity = -1;
        Vector2 shownTranslate, shownScale = Vector2.One;
        float shownRotate;
        bool shownInit;

        /// <summary>Changes of these properties ease over <paramref name="ms"/> (UI Toolkit transitions).</summary>
        public void SetTransition(float ms, string[] props)
        {
            transitionMs = ms;
            transitionProps = new HashSet<string>(props);
        }

        bool Eases(string prop) => transitionMs > 0 && transitionProps != null && (transitionProps.Contains(prop) || transitionProps.Contains("all"));

        internal void ApplyTransform()
        {
            float dt = UiCanvas.Dt;
            float k = transitionMs > 0 ? 1f - Mathf.Exp(-dt * 4.6f / (transitionMs / 1000f)) : 1f;
            Vector2 tr = Vector2.Zero;
            if (style.translate.set)
                tr = new Vector2(Res0(new StyleLength(style.translate.value.x), Size.X), Res0(new StyleLength(style.translate.value.y), Size.Y));
            var sc = style.scale.set ? style.scale.value.value : Vector2.One;
            float rot = style.rotate.set ? style.rotate.value.angle.value : 0;
            float op = style.opacity.Or(1);
            if (!shownInit) { shownInit = true; shownTranslate = tr; shownScale = sc; shownRotate = rot; shownOpacity = op; }
            shownTranslate = Eases("translate") ? shownTranslate.Lerp(tr, k) : tr;
            shownScale = Eases("scale") ? shownScale.Lerp(sc, k) : sc;
            shownRotate = Eases("rotate") ? Mathf.Lerp(shownRotate, rot, k) : rot;
            shownOpacity = Eases("opacity") ? Mathf.Lerp(shownOpacity, op, k) : op;
            var pos = basePos + shownTranslate;
            if (Position != pos) Position = pos;
            PivotOffset = Size / 2;
            if (Scale != shownScale) Scale = shownScale;
            float rad = Mathf.DegToRad(shownRotate);
            if (Rotation != rad) Rotation = rad;
            var mod = new Color(1, 1, 1, Mathf.Clamp(shownOpacity, 0, 1));
            if (Modulate != mod) Modulate = mod;
        }

        // MARK: - Drawing

        int drawKey;
        StyleBoxFlat flat;

        /// <summary>Asks for a redraw when anything drawn changed.</summary>
        internal void CheckRedraw()
        {
            int key = DrawKey();
            if (key == drawKey) return;
            drawKey = key;
            QueueRedraw();
        }

        protected virtual int DrawKey()
        {
            var s = style;
            var h = new HashCode();
            h.Add(Size);
            h.Add(s.backgroundColor.value); h.Add(s.backgroundColor.keyword);
            h.Add(s.backgroundImage.texture);
            h.Add(s.unityBackgroundImageTintColor.value);
            h.Add(s.unitySliceLeft.value); h.Add(s.unitySliceTop.value); h.Add(s.unitySliceRight.value); h.Add(s.unitySliceBottom.value);
            h.Add(s.unitySliceScale.value); h.Add(s.unitySliceType.value);
            h.Add(s.borderLeftWidth.value); h.Add(s.borderTopWidth.value); h.Add(s.borderRightWidth.value); h.Add(s.borderBottomWidth.value);
            h.Add(s.borderLeftColor.value); h.Add(s.borderTopColor.value); h.Add(s.borderBottomColor.value);
            h.Add(s.borderTopLeftRadius.value.value); h.Add(s.borderBottomLeftRadius.value.value);
            h.Add(s.backgroundSize.sizeType); h.Add(s.backgroundPositionX.keyword); h.Add(s.backgroundPositionY.keyword);
            return h.ToHashCode();
        }

        public override void _Draw()
        {
            var s = style;
            var rect = new Rect2(Vector2.Zero, Size);
            float bl = s.borderLeftWidth.Or(0), bt = s.borderTopWidth.Or(0), br = s.borderRightWidth.Or(0), bb = s.borderBottomWidth.Or(0);
            bool hasBg = s.backgroundColor.IsSet && s.backgroundColor.value.A > 0.001f;
            bool hasBorder = bl > 0 || bt > 0 || br > 0 || bb > 0;
            if (hasBg || hasBorder)
            {
                flat ??= new StyleBoxFlat { AntiAliasing = true };
                flat.BgColor = hasBg ? s.backgroundColor.value : new Color(0, 0, 0, 0);
                flat.DrawCenter = hasBg;
                flat.BorderWidthLeft = Mathf.CeilToInt(bl); flat.BorderWidthTop = Mathf.CeilToInt(bt);
                flat.BorderWidthRight = Mathf.CeilToInt(br); flat.BorderWidthBottom = Mathf.CeilToInt(bb);
                var bc = s.borderTopColor.IsSet ? s.borderTopColor.value : s.borderLeftColor.IsSet ? s.borderLeftColor.value : s.borderBottomColor.value;
                flat.BorderColor = bc;
                flat.CornerRadiusTopLeft = Mathf.RoundToInt(Res0(s.borderTopLeftRadius, Size.X));
                flat.CornerRadiusTopRight = Mathf.RoundToInt(Res0(s.borderTopRightRadius, Size.X));
                flat.CornerRadiusBottomLeft = Mathf.RoundToInt(Res0(s.borderBottomLeftRadius, Size.X));
                flat.CornerRadiusBottomRight = Mathf.RoundToInt(Res0(s.borderBottomRightRadius, Size.X));
                flat.CornerDetail = 6;
                DrawStyleBox(flat, rect);
            }
            var tex = s.backgroundImage.texture;
            if (tex != null)
            {
                var tint = s.unityBackgroundImageTintColor.IsSet ? s.unityBackgroundImageTintColor.value : Colors.White;
                int sl = s.unitySliceLeft.value, st = s.unitySliceTop.value, sr = s.unitySliceRight.value, sb = s.unitySliceBottom.value;
                if (sl > 0 || st > 0 || sr > 0 || sb > 0)
                {
                    float k = s.unitySliceScale.IsSet && s.unitySliceScale.value > 0 ? s.unitySliceScale.value : 1f;
                    var box = Skins.Box(tex, sl, st, sr, sb, s.unitySliceType.Or(SliceType.Sliced) == SliceType.Tiled, tint);
                    DrawSetTransform(Vector2.Zero, 0, new Vector2(k, k));
                    DrawStyleBox(box, new Rect2(Vector2.Zero, Size / k));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                else if (s.backgroundSize.sizeType == BackgroundSizeType.Cover || s.backgroundSize.sizeType == BackgroundSizeType.Contain)
                {
                    var ts = tex.GetSize();
                    float scale = s.backgroundSize.sizeType == BackgroundSizeType.Cover
                        ? Mathf.Max(Size.X / ts.X, Size.Y / ts.Y) : Mathf.Min(Size.X / ts.X, Size.Y / ts.Y);
                    var sz = ts * scale;
                    float ax = AnchorOf(s.backgroundPositionX.keyword), ay = AnchorOf(s.backgroundPositionY.keyword);
                    var at = new Vector2((Size.X - sz.X) * ax, (Size.Y - sz.Y) * ay);
                    if (s.backgroundSize.sizeType == BackgroundSizeType.Cover)
                    {
                        // Draw only the part of the picture inside the element.
                        var src = new Rect2(-at / scale, Size / scale);
                        DrawTextureRectRegion(tex, rect, src, tint);
                    }
                    else DrawTextureRect(tex, new Rect2(at, sz), false, tint);
                }
                else DrawTextureRect(tex, rect, false, tint);
            }
        }

        static float AnchorOf(BackgroundPositionKeyword k) => k switch
        {
            BackgroundPositionKeyword.Left or BackgroundPositionKeyword.Top => 0f,
            BackgroundPositionKeyword.Right or BackgroundPositionKeyword.Bottom => 1f,
            _ => 0.5f,
        };

        /// <summary>Per frame: transforms (transitions) and redraw checks down the tree.</summary>
        internal void Tick()
        {
            if (!Visible) return;
            ApplyTransform();
            CheckRedraw();
            for (int i = 0; i < GetChildCount(); i++)
            {
                var n = GetChild(i);
                if (n is VisualElement v) v.Tick();
                else if (n is ScrollContainer sc)
                    for (int j = 0; j < sc.GetChildCount(); j++) if (sc.GetChild(j) is VisualElement w) w.Tick();
            }
        }

        // MARK: - Hit testing

        /// <summary>True when a picking element (not ignored, shown) of this subtree lies under a canvas point.</summary>
        public bool Picks(Vector2 at)
        {
            if (!Visible || !Shown || Modulate.A < 0.02f) return false;
            if (pickingMode == PickingMode.Position && GetGlobalRect().HasPoint(at)) return true;
            foreach (var c in Children()) if (c.Picks(at)) return true;
            if (this is ScrollView) return false;
            return false;
        }
    }

    /// <summary>9-slice style boxes for skin pictures (cached by picture, slices and tint).</summary>
    public static class Skins
    {
        static readonly Dictionary<(Texture2D, int, int, int, int, bool, Color), StyleBoxTexture> boxes = new Dictionary<(Texture2D, int, int, int, int, bool, Color), StyleBoxTexture>();

        public static StyleBoxTexture Box(Texture2D tex, int l, int t, int r, int b, bool tiled, Color tint)
        {
            var key = (tex, l, t, r, b, tiled, tint);
            if (boxes.TryGetValue(key, out var box)) return box;
            box = new StyleBoxTexture
            {
                Texture = tex,
                TextureMarginLeft = l, TextureMarginTop = t, TextureMarginRight = r, TextureMarginBottom = b,
                ModulateColor = tint,
                AxisStretchHorizontal = tiled ? StyleBoxTexture.AxisStretchMode.TileFit : StyleBoxTexture.AxisStretchMode.Stretch,
                AxisStretchVertical = tiled ? StyleBoxTexture.AxisStretchMode.TileFit : StyleBoxTexture.AxisStretchMode.Stretch,
            };
            return boxes[key] = box;
        }
    }

    /// <summary>
    /// Text: outlined (the kit's white labels with a navy rim and a hard drop) or plain, one line or wrapped,
    /// with &lt;color=#RRGGBB&gt; runs.
    /// </summary>
    public partial class Label : VisualElement
    {
        string txt = "";
        public Label() { pickingMode = PickingMode.Ignore; }
        public Label(string text) : this() { txt = text ?? ""; }

        public string text
        {
            get => txt;
            set { value ??= ""; if (txt == value) return; txt = value; QueueRedraw(); LayoutDirty = true; }
        }

        protected override bool IsLeaf => true;

        Font FontOf => style.unityFontDefinition ?? Founders.UI.Theme.Display;
        float SizeOf => style.fontSize.IsSet ? style.fontSize.value.value : Founders.UI.Theme.Body;
        public float LineHeight => Mathf.Round(SizeOf * 1.18f);

        struct Run { public string Text; public Color? Color; }
        struct Piece { public string Text; public Color? Color; public float X, W; }

        // Parsed runs and laid-out lines, cached by text, size and width.
        string parsedFor;
        List<Run> runs;
        string linesFor;
        float linesSize, linesWidth = -1;
        bool linesWrap;
        List<List<Piece>> lines;
        List<float> lineWidths;

        List<Run> Runs()
        {
            if (parsedFor == txt && runs != null) return runs;
            parsedFor = txt;
            runs = Parse(txt);
            return runs;
        }

        static List<Run> Parse(string s)
        {
            var list = new List<Run>();
            var colors = new Stack<Color>();
            var sb = new System.Text.StringBuilder();
            void Flush()
            {
                if (sb.Length == 0) return;
                list.Add(new Run { Text = sb.ToString(), Color = colors.Count > 0 ? colors.Peek() : (Color?)null });
                sb.Clear();
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    int close = s.IndexOf('>', i);
                    if (close > i)
                    {
                        string tag = s.Substring(i + 1, close - i - 1);
                        if (tag.StartsWith("color=", StringComparison.Ordinal))
                        {
                            Flush();
                            string v = tag.Substring(6).Trim('"', '\'');
                            colors.Push(Founders.UI.Theme.Hex(v));
                            i = close;
                            continue;
                        }
                        if (tag == "/color") { Flush(); if (colors.Count > 0) colors.Pop(); i = close; continue; }
                        if (tag == "b" || tag == "/b" || tag == "i" || tag == "/i" || tag.StartsWith("size", StringComparison.Ordinal) ||
                            tag == "/size" || tag.StartsWith("/", StringComparison.Ordinal) || tag.StartsWith("alpha", StringComparison.Ordinal) ||
                            tag.StartsWith("cspace", StringComparison.Ordinal) || tag == "u" || tag == "/u")
                        { i = close; continue; }
                    }
                }
                sb.Append(c);
            }
            Flush();
            return list;
        }

        float Width(string s, int size) => s.Length == 0 ? 0 : FontOf.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

        float Outline => style.unityTextOutlineWidth.IsSet ? style.unityTextOutlineWidth.value : 0f;

        void LayoutLines(float width, bool wrap)
        {
            float size = SizeOf;
            if (lines != null && linesFor == txt && linesSize == size && linesWrap == wrap && (!wrap || Mathf.Abs(linesWidth - width) < 0.5f)) return;
            linesFor = txt; linesSize = size; linesWidth = width; linesWrap = wrap;
            int isz = Mathf.Max(1, Mathf.RoundToInt(size));
            lines = new List<List<Piece>> { new List<Piece>() };
            lineWidths = new List<float> { 0 };
            float x = 0;
            float space = Width(" ", isz);
            foreach (var run in Runs())
            {
                var paragraphs = run.Text.Split('\n');
                for (int p = 0; p < paragraphs.Length; p++)
                {
                    if (p > 0) { lineWidths[lines.Count - 1] = x; lines.Add(new List<Piece>()); lineWidths.Add(0); x = 0; }
                    string para = paragraphs[p];
                    if (!wrap || float.IsNaN(width))
                    {
                        float w = Width(para, isz);
                        if (para.Length > 0) lines[lines.Count - 1].Add(new Piece { Text = para, Color = run.Color, X = x, W = w });
                        x += w;
                        continue;
                    }
                    // Word by word.
                    var words = para.Split(' ');
                    for (int wi = 0; wi < words.Length; wi++)
                    {
                        string word = words[wi];
                        bool lead = wi > 0;
                        float w = Width(word, isz);
                        float need = (lead ? space : 0) + w;
                        if (x > 0 && x + need > width + 0.5f)
                        {
                            lineWidths[lines.Count - 1] = x;
                            lines.Add(new List<Piece>());
                            lineWidths.Add(0);
                            x = 0;
                            lead = false;
                            need = w;
                        }
                        string piece = (lead ? " " : "") + word;
                        if (piece.Length > 0) lines[lines.Count - 1].Add(new Piece { Text = piece, Color = run.Color, X = x, W = need });
                        x += need;
                    }
                }
            }
            lineWidths[lines.Count - 1] = x;
        }

        bool Wraps => style.whiteSpace.Or(WhiteSpace.Normal) == WhiteSpace.Normal;

        protected override Vector2 MeasureContent(float width, bool widthKnown)
        {
            bool wrap = Wraps && !float.IsNaN(width);
            LayoutLines(width, wrap);
            float max = 0;
            foreach (var w in lineWidths) max = Mathf.Max(max, w);
            return new Vector2(Mathf.Ceil(max + 1), LineHeight * lines.Count);
        }

        /// <summary>Width of the text on one line (no wrapping), in units.</summary>
        public float NaturalWidth()
        {
            LayoutLines(float.NaN, false);
            float max = 0;
            foreach (var w in lineWidths) max = Mathf.Max(max, w);
            return max;
        }

        protected override int DrawKey() => HashCode.Combine(base.DrawKey(), txt, style.fontSize.value.value, style.color.value,
            style.unityTextOutlineWidth.value, style.unityTextAlign.value, style.whiteSpace.value, style.textShadow.value.color);

        public override void _Draw()
        {
            base._Draw();
            if (string.IsNullOrEmpty(txt)) return;
            float pw = parent != null ? parent.Size.X : Size.X;
            float padL = VisualElement.Res0(style.paddingLeft, pw), padR = VisualElement.Res0(style.paddingRight, pw);
            float padT = VisualElement.Res0(style.paddingTop, pw), padB = VisualElement.Res0(style.paddingBottom, pw);
            float inner = Mathf.Max(1, Size.X - padL - padR);
            LayoutLines(inner, Wraps);
            float size = SizeOf;
            int isz = Mathf.Max(1, Mathf.RoundToInt(size));
            var font = FontOf;
            var anchor = style.unityTextAlign.Or(TextAnchor.UpperLeft);
            int col = (int)anchor % 3, rowA = (int)anchor / 3;
            float lh = LineHeight;
            float total = lh * lines.Count;
            float innerH = Size.Y - padT - padB;
            float y0 = padT + (rowA == 0 ? 0 : rowA == 1 ? (innerH - total) / 2 : innerH - total);
            var baseColor = style.color.IsSet ? style.color.value : Colors.White;
            float outline = Outline;
            var rim = style.unityTextOutlineColor.IsSet ? style.unityTextOutlineColor.value : Founders.UI.Theme.Outline;
            var shadow = style.textShadow.value;
            bool hasShadow = style.textShadow.set && shadow.color.A > 0;
            int osz = outline > 0 ? Mathf.Max(1, Mathf.RoundToInt(outline * 2f)) : 0;
            // Capitals sit in the middle of the line.
            float cap = size * 0.72f;
            for (int li = 0; li < lines.Count; li++)
            {
                float lw = lineWidths[li];
                float x0 = padL + (col == 0 ? 0 : col == 1 ? (inner - lw) / 2 : inner - lw);
                float baseline = y0 + li * lh + (lh + cap) / 2;
                foreach (var p in lines[li])
                {
                    var at = new Vector2(x0 + p.X, baseline);
                    var c = p.Color ?? baseColor;
                    c.A *= baseColor.A;
                    if (hasShadow)
                    {
                        var sc = shadow.color; sc.A *= baseColor.A;
                        if (osz > 0) DrawStringOutline(font, at + shadow.offset, p.Text, HorizontalAlignment.Left, -1, isz, osz, sc);
                        DrawString(font, at + shadow.offset, p.Text, HorizontalAlignment.Left, -1, isz, sc);
                    }
                    if (osz > 0)
                    {
                        var oc = rim; oc.A *= baseColor.A;
                        DrawStringOutline(font, at, p.Text, HorizontalAlignment.Left, -1, isz, osz, oc);
                    }
                    DrawString(font, at, p.Text, HorizontalAlignment.Left, -1, isz, c);
                }
            }
        }
    }

    /// <summary>A text field (Godot LineEdit inside an element).</summary>
    public partial class TextField : VisualElement
    {
        public readonly LineEdit Edit;
        public const string inputUssClassName = "text-input";
        public event Action<string> Changed;

        public TextField()
        {
            Edit = new LineEdit { FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Stop };
            Edit.AddThemeFontOverride("font", Founders.UI.Theme.Display);
            Edit.AddThemeFontSizeOverride("font_size", 15);
            Edit.AddThemeColorOverride("font_color", Founders.UI.Theme.Prose);
            var empty = new StyleBoxEmpty { ContentMarginLeft = 10, ContentMarginRight = 10 };
            Edit.AddThemeStyleboxOverride("normal", empty);
            Edit.AddThemeStyleboxOverride("focus", empty);
            Edit.TextChanged += t => Changed?.Invoke(t);
            AddChild(Edit);
            focusable = true;
        }

        public string value { get => Edit.Text; set => Edit.Text = value ?? ""; }
        public bool isPasswordField { get => Edit.Secret; set => Edit.Secret = value; }
        public int maxLength { get => Edit.MaxLength; set => Edit.MaxLength = value; }
        public bool isReadOnly { get => !Edit.Editable; set => Edit.Editable = !value; }

        public void RegisterValueChangedCallback(Action<ChangeEvent<string>> cb) =>
            Changed += t => cb(new ChangeEvent<string> { newValue = t });

        public void Blur() => Edit.ReleaseFocus();

        internal override void LayoutInside()
        {
            base.LayoutInside();
            Edit.Position = Vector2.Zero;
            Edit.Size = Size;
        }

        protected override bool IsLeaf => true;
        protected override Vector2 MeasureContent(float width, bool widthKnown) => new Vector2(120, 32);
    }

    public sealed class ChangeEvent<T> : EventBase
    {
        public T newValue, previousValue;
    }

    /// <summary>A scrolling element (Godot ScrollContainer around a content element).</summary>
    public partial class ScrollView : VisualElement
    {
        public readonly ScrollContainer Scroller;
        readonly VisualElement content;
        public readonly ScrollViewMode mode;
        public ScrollerVisibility verticalScrollerVisibility, horizontalScrollerVisibility;
        public TouchScrollBehavior touchScrollBehavior;
        /// <summary>Sizes to its content (then shrinks and scrolls when its column runs out of room).</summary>
        public bool FitContent;
        public readonly Scroller verticalScroller = new Scroller();
        public ScrollView contentViewport => this;

        public enum TouchScrollBehavior { Unrestricted, Elastic, Clamped }

        public ScrollView() : this(ScrollViewMode.Vertical) { }

        public ScrollView(ScrollViewMode mode)
        {
            this.mode = mode;
            Scroller = new ScrollContainer
            {
                MouseFilter = MouseFilterEnum.Pass,
                HorizontalScrollMode = mode == ScrollViewMode.Vertical ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.ShowNever,
                VerticalScrollMode = mode == ScrollViewMode.Horizontal ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.ShowNever,
                ScrollDeadzone = 8,
                FollowFocus = false,
            };
            Scroller.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            Scroller.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            AddChild(Scroller);
            content = new VisualElement { name = "content" };
            if (mode == ScrollViewMode.Horizontal) content.style.flexDirection = FlexDirection.Row;
            Scroller.AddChild(content);
            Scroller.GetVScrollBar().ValueChanged += v => verticalScroller.Fire((float)v);
            ClipContents = true;
        }

        public override VisualElement contentContainer => content;

        public Vector2 scrollOffset
        {
            get => new Vector2(Scroller.ScrollHorizontal, Scroller.ScrollVertical);
            set { Scroller.ScrollHorizontal = Mathf.RoundToInt(value.X); Scroller.ScrollVertical = Mathf.RoundToInt(value.Y); }
        }

        public void ScrollTo(VisualElement e)
        {
            if (e == null) return;
            var top = e.GetGlobalRect().Position.Y - content.GetGlobalRect().Position.Y;
            Scroller.ScrollVertical = Mathf.RoundToInt(Mathf.Max(0, top - 8));
        }

        protected override IEnumerable<VisualElement> LayoutChildren()
        {
            // Only the decorations added straight to the scroll view (fades) are its own children.
            for (int i = 0; i < GetChildCount(); i++)
                if (GetChild(i) is VisualElement v) yield return v;
        }

        protected override bool IsLeaf => true;

        protected override Vector2 MeasureContent(float width, bool widthKnown)
        {
            if (mode == ScrollViewMode.Horizontal)
            {
                float h = content.Measure(float.NaN, float.NaN, float.NaN).Y;
                return new Vector2(0, h);
            }
            if (!FitContent) return Vector2.Zero;
            float w = float.IsNaN(width) ? content.Measure(float.NaN, float.NaN, float.NaN).X : width;
            return new Vector2(w, content.HeightFor(w, w, float.NaN));
        }

        internal override void LayoutInside()
        {
            base.LayoutInside();
            Scroller.Position = Vector2.Zero;
            Scroller.Size = Size;
            Vector2 cs;
            if (mode == ScrollViewMode.Horizontal)
            {
                float w = content.Measure(float.NaN, Size.X, Size.Y).X;
                cs = new Vector2(Mathf.Max(w, Size.X), Size.Y);
            }
            else
            {
                float h = content.HeightFor(Size.X, Size.X, Size.Y);
                cs = new Vector2(Size.X, Mathf.Max(h, Size.Y));
            }
            if (content.CustomMinimumSize != cs) content.CustomMinimumSize = cs;
            content.SetFrame(new Rect2(content.Position, cs));
        }
    }

    /// <summary>The scroll position's change notification (UI Toolkit's verticalScroller.valueChanged).</summary>
    public sealed class Scroller
    {
        public event Action<float> valueChanged;
        internal void Fire(float v) => valueChanged?.Invoke(v);
    }

    /// <summary>
    /// A canvas layer that hosts a tree of elements over the whole viewport and lays it out every frame.
    /// </summary>
    public partial class UiCanvas : CanvasLayer
    {
        public readonly VisualElement Root = new VisualElement { name = "root" };
        internal static float Dt = 1f / 60f;
        double nextLayout;
        Vector2 lastSize;

        public UiCanvas()
        {
            Root.pickingMode = PickingMode.Ignore;
            AddChild(Root);
        }

        public Vector2 ViewSize => GetViewport() != null ? GetViewport().GetVisibleRect().Size : new Vector2(1082, 500);

        public override void _Process(double delta)
        {
            Dt = Mathf.Min((float)delta, 0.1f);
            Scheduler.Run();
            BeforeLayout();
            var size = ViewSize;
            double now = UiClock.Now;
            if (VisualElement.LayoutDirty || now >= nextLayout || size != lastSize || Root.Size != size)
            {
                VisualElement.LayoutDirty = false;
                nextLayout = now + 0.1;
                lastSize = size;
                VisualElement.Frame++;
                Root.SetFrame(new Rect2(Vector2.Zero, size));
            }
            Root.Tick();
        }

        /// <summary>Per frame before layout (state, bindings).</summary>
        protected virtual void BeforeLayout() { }
    }
}
