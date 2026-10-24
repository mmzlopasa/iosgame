using System;
using System.Collections.Generic;
using System.Globalization;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

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
    public partial class GameUi : MonoBehaviour
    {
        GameStore store;
        UIDocument doc;
        VisualElement root, hudLayer, sheetLayer, modalLayer, loadingLayer, showHud, topShade;
        float confirmDemolishUntil;
        public LoadingScreen Loading { get; private set; }

        /// <summary>The whole UI tree (the capture tour checks its layout, see LayoutAudit).</summary>
        public VisualElement Root => root;

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
        Rect lastSafe;
        float lastWidth;

        // Per-tick snapshot shared by all bindings.
        EconomySnapshot econ;
        ResourceBundle caps;
        Game G => store.Game;

        // MARK: - Setup

        /// <summary>
        /// Units across a phone's short side on a regular phone (390 pt wide): 500, i.e. ~0.78 pt per unit, everything
        /// about a fifth smaller than the first 390-unit design so more of the world shows. Smaller phones get fewer units
        /// (see <see cref="ShortSideUnits"/>) so text never drops below 9.4 pt.
        /// </summary>
        public const float PhoneShortSide = 500;
        /// <summary>Never fewer iOS points per panel unit than this (Small text, 12 units, stays ≥ 9.4 pt; a 46-unit tap is 36 pt).</summary>
        public const float MinPointsPerUnit = 0.78f;
        /// <summary>The short side a phone may not drop below (the first design's canvas).</summary>
        const float MinShortSide = 390;
        /// <summary>iPads: units along the long side (the first design matched 844 there; about a quarter smaller now).</summary>
        public const float TabletLongSide = 1080;

        /// <summary>Design canvas of a regular phone in landscape (portrait: the same phone turned).</summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(1082, 500);

        static bool IsPhone(int width, int height) =>
            Mathf.Max(width, height) / (float)Mathf.Max(1, Mathf.Min(width, height)) >= 1.7f;

        /// <summary>
        /// The screen's short side in iOS points: pixels over the device's scale (2x or 3x, from its pixel density;
        /// an unknown density counts as a regular 390-point phone).
        /// </summary>
        public static float ShortSidePoints(int width, int height, float dpi)
        {
            float px = Mathf.Min(width, height);
            if (dpi <= 0) return 390;
            float scale = Mathf.Max(1, Mathf.Round(dpi / 163f));
            return px / scale;
        }

        /// <summary>
        /// Units across a phone's short side: <see cref="PhoneShortSide"/>, or fewer on a small phone (iPhone SE /
        /// mini, 375 pt: 481 units) so a unit is never under <see cref="MinPointsPerUnit"/> points.
        /// </summary>
        public static float ShortSideUnits(int width, int height, float dpi)
        {
            float pts = ShortSidePoints(width, height, dpi);
            return Mathf.Clamp(Mathf.Round(pts / MinPointsPerUnit), MinShortSide, PhoneShortSide);
        }

        /// <summary>
        /// The design canvas for a screen. Phones (16:9 and longer) are matched on their short side
        /// (<see cref="ShortSideUnits"/> across it in either orientation, so text and buttons keep their physical size
        /// when the phone turns); squarer screens (iPad) on their long side (<see cref="TabletLongSide"/>).
        /// </summary>
        public static Vector2Int ReferenceFor(int width, int height, float dpi)
        {
            bool portrait = height > width;
            float ratio = Mathf.Max(width, height) / (float)Mathf.Max(1, Mathf.Min(width, height));
            int shortSide, longSide;
            if (IsPhone(width, height))
            {
                shortSide = Mathf.RoundToInt(ShortSideUnits(width, height, dpi));
                longSide = Mathf.RoundToInt(shortSide * ratio);
            }
            else
            {
                longSide = Mathf.RoundToInt(TabletLongSide);
                shortSide = Mathf.RoundToInt(TabletLongSide / ratio);
            }
            return portrait ? new Vector2Int(shortSide, longSide) : new Vector2Int(longSide, shortSide);
        }

        /// <summary>Phones are matched on their short side, iPads on their long side (match 0 = width, 1 = height).</summary>
        public static float MatchFor(int width, int height)
        {
            bool portrait = height > width;
            bool phone = IsPhone(width, height);
            return portrait ? (phone ? 0f : 1f) : (phone ? 1f : 0f);
        }

        public static GameUi Create(GameObject host)
        {
            var settings = UnityEngine.Resources.Load<PanelSettings>("UI/FoundersPanel");
            if (settings == null)
            {
                Debug.LogWarning("[FOUNDERS] PanelSettings asset missing; using a runtime instance.");
                settings = ScriptableObject.CreateInstance<PanelSettings>();
            }
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceFor(Screen.width, Screen.height, Screen.dpi);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = MatchFor(Screen.width, Screen.height);
            settings.sortingOrder = 10;

            var go = new GameObject("UI");
            go.transform.SetParent(host.transform, false);
            var ui = go.AddComponent<GameUi>();
            ui.doc = go.AddComponent<UIDocument>();
            ui.doc.panelSettings = settings;
            ui.BuildSkeleton();
            return ui;
        }

        void BuildSkeleton()
        {
            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;
            root.style.unityFontDefinition = Theme.Display;

            hudLayer = E.Div("hud").Fill().Ignore();
            // A soft shade behind the top strip only (the world stays bright everywhere else).
            topShade = E.Div().Abs(0, 0, 0).Size(null, 84).BgImage(Theme.TopShade).Ignore();
            hudLayer.Add(topShade);

            sheetLayer = E.Div("sheets").Fill().Ignore().Hidden(true);
            modalLayer = E.Div("modals").Fill().Ignore().Hidden(true);
            loadingLayer = E.Div("loading").Fill().Ignore().Hidden(true);
            root.Add(hudLayer);
            // While the HUD is hidden only this small eye button remains, to bring it back (top right).
            showHud = E.HudSquare("eye", Act(() => store.HudHidden = false), 44).Abs(null, 8, 10);
            showHud.style.opacity = 0.85f;
            root.Add(showHud);
            root.Add(sheetLayer);
            root.Add(modalLayer);
            root.Add(loadingLayer);

            Loading = new LoadingScreen(loadingLayer);
        }

        public void Init(GameStore owner)
        {
            store = owner;
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

        /// <summary>True when a screen point (pixels, bottom-left origin) is over UI or a sheet/dialog is open.</summary>
        public bool BlocksPointer(Vector2 screen)
        {
            if (root?.panel == null) return false;
            if (Loading.Visible) return true;
            if (Hidden) return false;
            if (SheetOpen || ModalOpen) return true;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = root.panel.Pick(p);
            return picked != null && picked != root && picked != root.panel.visualTree;
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
                if (!Mathf.Approximately(l.style.fontSize.value.value, fit))
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

        void Update()
        {
            if (root == null) return;
            UpdateScale();
            UpdateSafeArea();
            if (store?.Game == null) return;
            hudLayer.Hidden(Hidden || store.HudHidden);
            showHud?.Hidden(Hidden || !store.HudHidden);
            // No card on screen to keep the camera's focus clear of.
            if ((Hidden || store.HudHidden) && store.Rig != null) store.Rig.ViewShift = Vector2.zero;
            if (Hidden) { sheetLayer.Hidden(true); modalLayer.Hidden(true); return; }
            if (store.HudHidden) { sheetLayer.Hidden(true); return; }

            if (!dirty && Time.unscaledTime < nextTick) return;
            dirty = false;
            nextTick = Time.unscaledTime + 0.1f;

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
                    catch (Exception e) { Debug.LogException(e); }
                    building = null;
                }
                else
                {
                    foreach (var b in r.Binds) b();
                }
            }
        }

        /// <summary>
        /// Keeps the panel's design canvas and match in step with the screen (the phone turned, a resize): phones are
        /// matched on their short side in both orientations, iPads on their long side (see <see cref="ReferenceFor"/>).
        /// </summary>
        void UpdateScale()
        {
            var settings = doc.panelSettings;
            if (settings == null) return;
            var reference = ReferenceFor(Screen.width, Screen.height, Screen.dpi);
            if (settings.referenceResolution != reference) settings.referenceResolution = reference;
            float m = MatchFor(Screen.width, Screen.height);
            if (!Mathf.Approximately(settings.match, m)) settings.match = m;
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
            float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0 || float.IsNaN(h) || h <= 0) return;
            var s = Screen.safeArea;
            if (s == lastSafe && Mathf.Approximately(w, lastWidth) && Mathf.Approximately(h, lastHeight)) return;
            lastSafe = s;
            lastWidth = w;
            lastHeight = h;
            float k = w / Mathf.Max(1, Screen.width);
            PanelW = w;
            PanelH = h;
            Portrait = h > w;
            SafeTop = Mathf.Max(0, (Screen.height - s.yMax) * k);
            SafeBottom = Mathf.Max(0, s.yMin * k);
            SafeLeft = Mathf.Max(0, s.xMin * k);
            SafeRight = Mathf.Max(0, (Screen.width - s.xMax) * k);
            showHud.style.top = SafeTop + 8;
            showHud.style.right = SafeRight + 10;
            Loading?.SetInsets(SafeTop, SafeBottom, Portrait);
            // The phone turned (or the window changed): every region is built again for the new shape.
            foreach (var r in regions) r.Last = null;
            dirty = true;
        }
    }
}
