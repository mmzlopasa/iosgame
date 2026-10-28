using System;
using System.Collections.Generic;
using System.Globalization;
using Founders.Core;
using Godot;
using Founders.Art;
using Founders.World;

namespace Founders
{
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            int t = Math.Max(0, (int)Math.Ceiling(seconds));
            return t >= 60 ? $"{t / 60}m {t % 60}s" : $"{t}s";
        }

        /// <summary>Countdown style: 0:42, 3:07, 1:02:30.</summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            int t = Math.Max(0, (int)Math.Ceiling(seconds));
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }

        public static string Amount(double value) => ((int)Math.Floor(value)).ToString("#,0", Inv);

        /// <summary>Compact numbers for tight slots: 956, 1.3K, 12K, 1.2M (never longer than 4 characters plus a sign).</summary>
        public static string Compact(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            double a = Math.Abs(v);
            string sign = v < 0 ? "−" : "";
            if (a < 1000) return sign + ((int)Math.Floor(a)).ToString(Inv);
            if (a < 10_000) return sign + (Math.Floor(a / 100) / 10).ToString("0.#", Inv) + "K";
            if (a < 1_000_000) return sign + Math.Floor(a / 1000).ToString(Inv) + "K";
            if (a < 10_000_000) return sign + (Math.Floor(a / 100_000) / 10).ToString("0.#", Inv) + "M";
            return sign + Math.Floor(a / 1_000_000).ToString(Inv) + "M";
        }

        public static string PerMinute(double perSecond) =>
            (perSecond * 60).ToString("+0.0;−0.0;+0.0", Inv) + "/m";

        public static string Signed(double value) =>
            ((int)Math.Round(value)).ToString("+#,0;−#,0;0", Inv);

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", _ => n.ToString(Inv) };
    }
}

namespace Founders.UI
{
    /// <summary>
    /// The game's UI, built with UI Toolkit from code (no editor-made assets), in the style of a casual
    /// mobile strategy kit (see <see cref="Theme"/>): a thin resource strip on top, menu tiles on the
    /// right, the Build button bottom-right; popups are parchment windows that never cover the whole
    /// screen. Landscape: a centred window, a docked card on the left or a strip along the bottom.
    /// Portrait (<see cref="Portrait"/>, the kit's phone screens): every card, strip, window and dialog is
    /// a bottom sheet across the screen with the back arrow under it. The phone may turn at any time:
    /// the panel is re-scaled and every region rebuilt for the new shape.
    ///
    /// Dynamic parts are <see cref="Region"/>s: a region is rebuilt only when its structural key changes
    /// (a building finished, a panel opened…); in between, bound labels and bars are refreshed in place,
    /// so taps and scroll positions are never interrupted.
    /// </summary>
    public partial class GameUi : UiCanvas
    {
        Session store;
        VisualElement root, hudLayer, sheetLayer, modalLayer, showHud, topShade;
        float confirmDemolishUntil;

        /// <summary>The whole UI tree.</summary>
        public VisualElement UiRoot => root;

        /// <summary>Hides the game UI (used for clean screenshots).</summary>
        public bool Hidden;

        sealed class Region
        {
            public VisualElement Host;
            public Func<string> Key;
            public Action<VisualElement> Build;
            public string Last;
            public readonly List<Action> Binds = new List<Action>();
        }

        readonly List<Region> regions = new List<Region>();
        Region building;
        bool dirty = true;
        float nextTick;
        Rect2I lastSafe;
        float lastWidth;

        // Per-tick snapshot shared by all bindings.
        EconomySnapshot econ;
        ResourceBundle caps;
        Game G => store.Game;

        // MARK: - Setup

        /// <summary>Design canvas of a regular phone in landscape (the viewport's base size, see project.godot).</summary>
        public static readonly Vector2I ReferenceResolution = new Vector2I(1082, 500);

        public GameUi()
        {
            Layer = 10;
            BuildSkeleton();
        }

        void BuildSkeleton()
        {
            root = Root;
            root.pickingMode = PickingMode.Ignore;

            hudLayer = E.Div("hud").Fill().Ignore();
            // A soft shade behind the top strip only (the world stays bright everywhere else).
            topShade = E.Div().Abs(0, 0, 0).Size(null, 84).BgImage(Theme.TopShade).Ignore();
            hudLayer.Add(topShade);

            sheetLayer = E.Div("sheets").Fill().Ignore().HiddenIf(true);
            modalLayer = E.Div("modals").Fill().Ignore().HiddenIf(true);
            root.Add(hudLayer);
            // While the HUD is hidden only this small eye button remains, to bring it back (top right).
            showHud = E.HudSquare("eye", Act(() => store.HudHidden = false), 44).Abs(null, 8, 10);
            showHud.style.opacity = 0.85f;
            root.Add(showHud);
            root.Add(sheetLayer);
            root.Add(modalLayer);
        }

        public void Init(Session owner)
        {
            store = owner;
            store.OnRefresh += () => dirty = true;
            BuildHud();
            BuildSheet();
            AddRegion(modalLayer, ModalKey, BuildModal);
            dirty = true;
        }

        // MARK: - Pointer blocking (for the camera)

        bool SheetOpen => store != null && store.ActivePanel != Panel.None;
        bool ModalOpen => store != null && ModalKey() != "";

        /// <summary>True while a card for a selection, a resource or a placement is docked on the left.</summary>
        bool ContextOpen => store != null && (store.Founding || store.DecoratingId != null || store.PlacingBuildingId != null ||
                                              store.SelectedBuildingId != null || store.SelectedNode != null);

        /// <summary>True when a point of the viewport (the canvas, units) is over the interface or a sheet/dialog is open.</summary>
        public bool BlocksPointer(Vector2 at)
        {
            if (LoadingScreen.Showing) return true;
            if (Hidden || store == null) return false;
            if (SheetOpen || ModalOpen) return true;
            return root.Picks(at);
        }

        // MARK: - Regions and bindings

        Region AddRegion(VisualElement host, Func<string> key, Action<VisualElement> build)
        {
            var r = new Region { Host = host, Key = key, Build = build };
            regions.Add(r);
            return r;
        }

        void Bind(Action a)
        {
            building?.Binds.Add(a);
            a();
        }

        /// <summary>A live outlined label.</summary>
        Label Live(Func<string> text, float size = Theme.Num, Color? color = null)
        {
            var l = E.T("", size, color);
            Bind(() => { var t = text(); if (l.text != t) l.text = t; });
            return l;
        }

        /// <summary>
        /// A live outlined label that shrinks to fit a width when its text grows (not below the minimum size);
        /// text still too long wraps onto a second line instead of being cut.
        /// </summary>
        Label LiveFit(Func<string> text, float size, float maxWidth, Color? color = null, float min = Theme.Small)
        {
            min = Mathf.Max(min, Theme.Small);
            var l = E.T("", size, color);
            l.style.maxWidth = maxWidth;
            Bind(() =>
            {
                var t = text();
                if (l.text == t) return;
                l.text = t;
                float fit = Theme.FitSize(t, maxWidth, size, min);
                if (!Mathf.IsEqualApprox(l.style.fontSize.value.value, fit))
                {
                    l.style.fontSize = fit;
                    if (!l.ClassListContains("ink-mode")) E.Outline(l, fit);
                }
                var ws = Theme.TextWidth(t, fit) > maxWidth ? WhiteSpace.Normal : WhiteSpace.NoWrap;
                if (l.style.whiteSpace != ws) l.style.whiteSpace = ws;
            });
            return l;
        }

        /// <summary>A live plain (ink) label.</summary>
        Label LiveInk(Func<string> text, float size = Theme.Body, Color? color = null)
        {
            var l = E.Ink("", size, color);
            Bind(() => { var t = text(); if (l.text != t) l.text = t; });
            return l;
        }

        VisualElement LiveBar(Func<double> value, float height = 12, E.FillKind kind = E.FillKind.Green)
        {
            var bar = E.Bar(out var fill, height, kind);
            Bind(() => E.SetFill(fill, value()));
            return bar;
        }

        /// <summary>A bar with a live centred label ("73%").</summary>
        VisualElement LiveLabeledBar(Func<double> value, Func<string> label, float height = 18, E.FillKind kind = E.FillKind.Green)
        {
            var bar = E.LabeledBar(out var fill, out var l, height, kind);
            Bind(() =>
            {
                E.SetFill(fill, value());
                string s = label();
                if (l.text != s) l.text = s;
            });
            return bar;
        }

        /// <summary>Wraps a UI action so the screen refreshes on the next frame.</summary>
        Action Act(Action a) => () => { AudioDirector.Ui("ui_tap"); a(); dirty = true; };

        VisualElement Tap(VisualElement e, Action a) => e.OnTap(Act(a));

        protected override void BeforeLayout()
        {
            if (root == null) return;
            UpdateSafeArea();
            if (store?.Game == null) return;
            hudLayer.HiddenIf(Hidden || store.HudHidden);
            showHud?.HiddenIf(Hidden || !store.HudHidden);
            // No card on screen to keep the camera's focus clear of.
            if ((Hidden || store.HudHidden) && store.Rig != null) store.Rig.ViewShift = Vector2.Zero;
            if (Hidden) { sheetLayer.HiddenIf(true); modalLayer.HiddenIf(true); return; }
            if (store.HudHidden) { sheetLayer.HiddenIf(true); return; }

            float now = (float)UiClock.Now;
            if (!dirty && now < nextTick) return;
            dirty = false;
            nextTick = now + 0.1f;

            tickNo++;
            econ = G.Economy();
            caps = G.StorageCapacity();
            OnTick();
            foreach (var r in regions)
            {
                string key = r.Key();
                if (key != r.Last)
                {
                    r.Last = key;
                    r.Binds.Clear();
                    r.Host.Clear();
                    building = r;
                    try { r.Build(r.Host); }
                    catch (Exception e) { GD.PrintErr("[FOUNDERS] UI region: " + e); }
                    building = null;
                }
                else
                {
                    foreach (var b in r.Binds) b();
                }
            }
        }

        // MARK: - Safe area and orientation

        /// <summary>Panel size and the safe-area insets (notch / Dynamic Island on top in portrait or on a side in landscape, home indicator below), in panel units.</summary>
        float PanelW = 1082, PanelH = 500, SafeTop, SafeBottom, SafeLeft, SafeRight;
        float lastHeight;

        /// <summary>
        /// True while the panel is taller than wide (read from its resolved size each frame). Portrait follows the
        /// kit's phone layouts: the resource bar on top, the menu hexagons down the right edge, and every card, strip,
        /// window and dialog as a bottom sheet the full width of the screen with the back arrow under it.
        /// </summary>
        public bool Portrait { get; private set; }

        /// <summary>Bottom margin of the HUD: the home indicator's inset, or a little air on phones without one.</summary>
        float BottomPad => Mathf.Max(10, SafeBottom);

        /// <summary>Usable width between the insets.</summary>
        float SafeWidth => PanelW - SafeLeft - SafeRight;

        void UpdateSafeArea()
        {
            var view = ViewSize;
            float w = view.X, h = view.Y;
            if (w <= 0 || h <= 0) return;
            var win = DisplayServer.WindowGetSize();
            var s = DisplayServer.GetDisplaySafeArea();
            if (s.Size.X <= 0 || s.Size.Y <= 0) s = new Rect2I(Vector2I.Zero, win);
            if (s == lastSafe && Mathf.IsEqualApprox(w, lastWidth) && Mathf.IsEqualApprox(h, lastHeight)) return;
            lastSafe = s;
            lastWidth = w;
            lastHeight = h;
            float k = w / Mathf.Max(1, win.X);
            PanelW = w;
            PanelH = h;
            Portrait = h > w;
            SafeTop = Mathf.Max(0, s.Position.Y * k);
            SafeBottom = Mathf.Max(0, (win.Y - s.End.Y) * k);
            SafeLeft = Mathf.Max(0, s.Position.X * k);
            SafeRight = Mathf.Max(0, (win.X - s.End.X) * k);
            showHud.style.top = SafeTop + 8;
            showHud.style.right = SafeRight + 10;
            // The window changed: every region is built again for the new shape.
            foreach (var r in regions) r.Last = null;
            dirty = true;
        }
    }
}
