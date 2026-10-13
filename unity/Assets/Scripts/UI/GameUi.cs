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
    /// screen (a centred window, a docked card on the left or a strip along the bottom).
    ///
    /// Dynamic parts are <see cref="Region"/>s: a region is rebuilt only when its structural key changes
    /// (a building finished, a panel opened…); in between, bound labels and bars are refreshed in place,
    /// so taps and scroll positions are never interrupted.
    /// </summary>
    public partial class GameUi : MonoBehaviour
    {
        GameStore store;
        UIDocument doc;
        VisualElement root, hudLayer, sheetLayer, modalLayer, loadingLayer, showHud;
        float confirmDemolishUntil;
        public LoadingScreen Loading { get; private set; }

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

        /// <summary>Design canvas: a landscape phone, 844 × 390 points.</summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(844, 390);

        /// <summary>
        /// Phones in landscape (16:9 and wider) are matched on height, so the panel is always 390 units tall and
        /// 690–850 wide. Squarer screens (iPad) are matched on width: 844 units across, taller than 390.
        /// </summary>
        public static float MatchFor(int width, int height) => width / (float)Mathf.Max(1, height) >= 1.7f ? 1f : 0f;

        public static GameUi Create(GameObject host)
        {
            var settings = UnityEngine.Resources.Load<PanelSettings>("UI/FoundersPanel");
            if (settings == null)
            {
                Debug.LogWarning("[FOUNDERS] PanelSettings asset missing; using a runtime instance.");
                settings = ScriptableObject.CreateInstance<PanelSettings>();
            }
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
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
            root.style.unityFontDefinition = Theme.Font;

            hudLayer = E.Div("hud").Fill().Ignore();
            // A soft shade behind the top strip only (the world stays bright everywhere else).
            hudLayer.Add(E.Div().Abs(0, 0, 0).Size(null, 84).BgImage(Theme.TopShade).Ignore());

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
            Loading.StartVideo(gameObject);
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
        bool ContextOpen => store != null && (store.DecoratingId != null || store.PlacingBuildingId != null ||
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

        /// <summary>Keeps the panel matched on height for phones and on width for iPads (e.g. after a resize).</summary>
        void UpdateScale()
        {
            var settings = doc.panelSettings;
            if (settings == null) return;
            float m = MatchFor(Screen.width, Screen.height);
            if (!Mathf.Approximately(settings.match, m)) settings.match = m;
        }

        // MARK: - Safe area

        /// <summary>Panel size and the safe-area insets (notch / Dynamic Island on the left or right, home indicator below), in panel units.</summary>
        float PanelW = 844, PanelH = 390, SafeTop, SafeBottom, SafeLeft, SafeRight;
        float lastHeight;

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
            SafeTop = Mathf.Max(0, (Screen.height - s.yMax) * k);
            SafeBottom = Mathf.Max(0, s.yMin * k);
            SafeLeft = Mathf.Max(0, s.xMin * k);
            SafeRight = Mathf.Max(0, (Screen.width - s.xMax) * k);
            showHud.style.top = SafeTop + 8;
            showHud.style.right = SafeRight + 10;
            Loading?.SetInsets(SafeBottom);
            dirty = true;
        }
    }
}
