using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders
{
    public static class Fmt
    {
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

        public static string Amount(double value) => ((int)Math.Floor(value)).ToString("#,0", CultureInfo.InvariantCulture);

        public static string PerMinute(double perSecond) =>
            (perSecond * 60).ToString("+0.0;−0.0;+0.0", CultureInfo.InvariantCulture) + "/m";

        public static string Signed(double value) =>
            ((int)Math.Round(value)).ToString("+#,0;−#,0;0", CultureInfo.InvariantCulture);

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", _ => n.ToString(CultureInfo.InvariantCulture) };
    }
}

namespace Founders.UI
{
    /// <summary>
    /// The game's UI, built with UI Toolkit from code (no editor-made assets). Screens follow
    /// the approved design canvas: night-navy glass, gold accents, Sora / Geist type.
    ///
    /// Dynamic parts are <see cref="Region"/>s: a region is rebuilt only when its structural
    /// key changes (a building finished, a panel opened…); in between, bound labels and bars
    /// are refreshed in place, so taps and scroll positions are never interrupted.
    /// </summary>
    public partial class GameUi : MonoBehaviour
    {
        GameStore store;
        UIDocument doc;
        VisualElement root, hudLayer, safe, sheetLayer, modalLayer, loadingLayer, showHud;
        float confirmDemolishUntil;
        ScrollView sheetScroll;
        VisualElement sheet;
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
            root.style.unityFontDefinition = Theme.Font(Theme.F.Text);

            hudLayer = E.Div("hud").Fill().Ignore();
            safe = E.Div("safe").Fill().Ignore().Col();
            // Soft shades behind the top row and the bottom corners (the screen is short: keep them low).
            hudLayer.Add(E.Div().Abs(0, 0, 0).Size(null, 120).BgImage(Theme.TopShade).Ignore());
            hudLayer.Add(E.Div().Abs(0, null, 0, 0).Size(null, 110).BgImage(Theme.BottomShade).Ignore());
            hudLayer.Add(safe);

            sheetLayer = E.Div("sheets").Fill().Ignore().Hidden(true);
            modalLayer = E.Div("modals").Fill().Ignore().Hidden(true);
            loadingLayer = E.Div("loading").Fill().Ignore().Hidden(true);
            root.Add(hudLayer);
            // While the HUD is hidden only this small eye button remains, to bring it back (top right).
            showHud = E.IconButton("eye", Act(() => store.HudHidden = false), 44, null, true).Abs(null, 10, 14);
            showHud.style.opacity = 0.75f;
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

        Label Live(Func<string> text, float size = 13, Color? color = null, Theme.F font = Theme.F.Text)
        {
            var l = E.Text("", size, color, font);
            Bind(() => { var t = text(); if (l.text != t) l.text = t; });
            return l;
        }

        VisualElement LiveBar(Func<double> value, float height = 5, Texture2D tex = null, Color? color = null)
        {
            var bar = E.Bar(out var fill, height, tex ?? Theme.GoldFill, color);
            if (color.HasValue)
            {
                fill.style.backgroundImage = StyleKeyword.None;
                fill.style.backgroundColor = color.Value;
            }
            Bind(() => E.SetFill(fill, value()));
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

        /// <summary>Usable height for a dialog (the screen minus the insets and a margin).</summary>
        float ModalMaxHeight => Mathf.Max(200, PanelH - SafeTop - SafeBottom - 24);

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

            safe.style.paddingTop = SafeTop + 6;
            safe.style.paddingBottom = BottomPad;
            safe.style.paddingLeft = SafeLeft;
            safe.style.paddingRight = SafeRight;
            showHud.style.top = SafeTop + 10;
            showHud.style.right = SafeRight + 14;
            if (contextPanel != null)
            {
                contextPanel.style.left = SafeLeft + 10;
                contextPanel.style.top = SafeTop + HudTop;
                contextPanel.style.bottom = BottomPad + 6;
            }
            LayoutSheet();
            Loading?.SetInsets(SafeBottom);
            dirty = true;
        }

        // MARK: - HUD

        VisualElement toastLayer, contextPanel;
        ScrollView contextScroll;
        string contextIdentity;

        void BuildHud()
        {
            var top = E.Div("top").Ignore().Col();
            AddRegion(top, TopKey, BuildTop);
            safe.Add(top);

            // Middle band: goal and buttons on the left, timers and tiles on the right.
            var rails = E.Div("rails").Ignore().Row(0, Align.FlexStart).Margin(8, 10, 0, 10);
            rails.style.flexGrow = 1;
            rails.style.flexShrink = 1;
            rails.style.minHeight = 0;
            var left = E.Div("left-rail").Ignore().Col(Align.FlexStart).Size(LeftRailW);
            var right = E.Div("right-rail").Ignore().Row(0, Align.FlexStart).NoShrink();
            rails.Add(left);
            rails.Add(E.Div().Grow().Ignore());
            rails.Add(right);
            AddRegion(left, SidesKey, BuildLeftSide);
            AddRegion(right, SidesKey, BuildRightSide);
            safe.Add(rails);

            var bottom = E.Div("bottom").Ignore();
            AddRegion(bottom, BottomKey, BuildBottom);
            safe.Add(bottom);

            // Notices: a column between the rails, under the top row (placed by LayoutOverlays).
            toastLayer = E.Div("toasts").Ignore().Col(Align.Center).Abs(0, HudTop, 0);
            toastLayer.style.overflow = Overflow.Hidden;
            AddRegion(toastLayer, ToastKey, BuildToasts);
            hudLayer.Add(toastLayer);

            // The selection / placement card, docked bottom-left; it scrolls when taller than the screen allows.
            contextPanel = E.Div("context").Ignore().Col().Abs(10, HudTop, null, 16);
            contextPanel.style.width = ContextW;
            contextPanel.style.justifyContent = Justify.FlexEnd;
            contextScroll = E.FitScroll();
            contextPanel.Add(contextScroll);
            AddRegion(contextScroll.contentContainer, ContextKey, BuildContext);
            hudLayer.Add(contextPanel);
            lastSafe = default;
        }

        /// <summary>Places the notice column between whatever occupies the two sides (called every tick).</summary>
        void LayoutOverlays()
        {
            bool card = ContextOpen;
            toastLayer.style.top = SafeTop + HudTop;
            toastLayer.style.left = SafeLeft + 10 + (card ? ContextW : LeftRailW) + 10;
            toastLayer.style.right = SafeRight + 10 + (card ? TileW : TimersW + 8 + TileW) + 10;
            toastLayer.style.bottom = BottomPad + BottomRowH + 4;
        }

        /// <summary>Gold-ringed founder avatar with the settlement's initial.</summary>
        VisualElement Avatar(float size, float letter)
        {
            var a = E.Div("avatar").Size(size, size).Center();
            a.Add(E.Image(Theme.Image("avatar_ring"), size, size).Abs(0, 0));
            var inner = E.Div().Size(size - 10, size - 10).Radius((size - 10) / 2).Center().Ignore();
            inner.BgImage(Theme.Gradient("avatar", true, Theme.Navy3, Theme.Navy));
            string initial = string.IsNullOrEmpty(G.State.SettlementName) ? "F" : G.State.SettlementName.Substring(0, 1).ToUpperInvariant();
            inner.Add(E.Text(initial, letter, Theme.GoldLight, Theme.F.DisplayBold));
            a.Add(inner);
            int era = G.Content.Epoch(G.State.EpochId)?.Index ?? 1;
            var badge = E.Badge(Fmt.Roman(era), Theme.Gold, Theme.OnGold).Abs(null, null, -4, -3);
            a.Add(badge);
            return a;
        }

        static string IconFor(ResourceType t) => t switch
        {
            ResourceType.Food => "food",
            ResourceType.Wood => "wood",
            ResourceType.Stone => "stone",
            ResourceType.Ore => "ore",
            ResourceType.Hides => "hides",
            _ => "knowledge",
        };

        static Color TintFor(ResourceType t) => t switch
        {
            ResourceType.Food => Theme.Error,
            ResourceType.Wood => Theme.GoldDeep,
            ResourceType.Stone => Theme.Body,
            ResourceType.Ore => Theme.Hex("#E39A62"),
            ResourceType.Hides => Theme.Hex("#C9A27E"),
            _ => Theme.Lavender,
        };

        bool MilestoneReady() => G.Content.Milestones.Any(m => G.CanClaimMilestone(m.Id));

        // MARK: - Timers (right rail)

        static string SeasonIcon(string id) => id switch { "summer" => "summer", "autumn" => "autumn", "winter" => "winter", _ => "spring" };

        static Color SeasonTint(string id) => id switch
        {
            "summer" => Theme.Gold,
            "autumn" => Theme.Hex("#F0A35E"),
            "winter" => Theme.Hex("#A9D8FF"),
            _ => Theme.Mint,
        };

        static Color ApprovalColor(int a) => a >= 65 ? Theme.Mint : a >= 40 ? Theme.Gold : Theme.Error;

        /// <summary>
        /// Season, outside temperature, hope (approval) and discontent at a glance: <see cref="SeasonCardH"/> tall.
        /// The season and temperature open the Weather sheet; the meters open People & wellbeing.
        /// </summary>
        VisualElement SeasonCard()
        {
            var season = G.Season;
            var c = E.Div("season").Col().Glass(14, G.Freezing ? Theme.Alpha(Theme.Error, 0.5f) : (Color?)null).Pad(7, 9).Margin(0, 0, 6);
            c.style.height = SeasonCardH - 6;
            var top = E.Div("season-top").Col();
            var head = E.Div().Row().Ignore();
            head.style.height = 17;
            if (season != null)
            {
                // Freezing replaces the season's name (one line keeps the card's height fixed).
                head.Add(E.Icon(G.Freezing ? "alert" : SeasonIcon(season.Id), 14, G.Freezing ? Theme.Error : SeasonTint(season.Id)));
                var name = E.Text(G.Freezing ? "Freezing!" : season.Name, 12.5f, G.Freezing ? Theme.Error : Theme.Ink, Theme.F.TextBold).NoWrap();
                name.style.marginLeft = 5;
                name.style.flexShrink = 1;
                head.Add(name);
            }
            head.Add(E.Div().Grow().Ignore());
            if (G.HasClimate) head.Add(ThermoChip());
            else if (season != null) head.Add(Live(() => $"Y{G.Year}", 10, Theme.Muted, Theme.F.Mono));
            top.Add(head);
            if (season != null)
            {
                var bar = LiveBar(() => G.SeasonProgress, 3, Theme.Gradient("season" + season.Id, false, SeasonTint(season.Id), Theme.Alpha(SeasonTint(season.Id), 0.7f)));
                bar.style.marginTop = 5;
                top.Add(bar);
            }
            Tap(top, () => store.ActivePanel = G.HasClimate ? Panel.Weather : Panel.People);
            c.Add(top);
            var meters = HudMeters();
            meters.style.marginTop = 6;
            c.Add(meters);
            return c;
        }

        /// <summary>A running job (research, construction, scouting): about <see cref="TimerCardH"/> tall.</summary>
        VisualElement TimerCard(string icon, string label, Color accent, string name, Func<double> progress, Func<string> left,
                                Texture2D fill, Action onTap)
        {
            var c = E.Div().Col().Glass(14).Pad(8, 10).Margin(0, 0, 6);
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon(icon, 12, accent));
            var l = E.Eyebrow(label, Theme.Muted, 9).NoWrap();
            l.style.marginLeft = 5;
            l.style.flexShrink = 1;
            head.Add(l);
            head.Add(E.Div().Grow().Ignore());
            head.Add(Live(left, 10, Theme.Body, Theme.F.Mono));
            c.Add(head);
            var n = E.Text(name, 12, Theme.Ink, Theme.F.TextBold).NoWrap();
            n.style.marginTop = 4;
            c.Add(n);
            var bar = LiveBar(progress, 4, fill);
            bar.style.marginTop = 6;
            c.Add(bar);
            Tap(c, onTap);
            return c;
        }

        // MARK: - Toasts

        string ToastKey() => string.Join(",", store.Toasts.Select(t => t.CreatedAt.ToString(CultureInfo.InvariantCulture))) + "|" +
                             (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null ? G.PendingEvent.Id : "");

        void BuildToasts(VisualElement host)
        {
            // A decision put off for later stays on top until it is answered.
            if (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null)
            {
                var pill = E.Div().Row().Center().Radius(999).BgImage(Theme.GoldFill).Pad(9, 14).Margin(0, 0, 6);
                pill.style.maxWidth = Length.Percent(100);
                pill.Add(E.Icon("bell", 15, Theme.OnGold));
                var t = E.Text($"Decision waiting · {G.PendingEvent.Title}", 12.5f, Theme.OnGold, Theme.F.TextBold).NoWrap();
                t.style.marginLeft = 8;
                t.style.flexShrink = 1;
                pill.Add(t);
                Tap(pill, () => store.EventMinimized = false);
                host.Add(pill);
            }
            // Newest first: if the column ever runs out of height, the oldest is the one cut.
            for (int i = store.Toasts.Count - 1; i >= 0; i--)
            {
                var toast = store.Toasts[i];
                Color edge; string icon;
                switch (toast.Notice.Kind)
                {
                    case NoticeKind.Success: edge = Theme.Mint; icon = "check"; break;
                    case NoticeKind.Warning: edge = Theme.Error; icon = "alert"; break;
                    case NoticeKind.Milestone: edge = Theme.Gold; icon = "sparkle"; break;
                    default: edge = Theme.Lavender; icon = "bell"; break;
                }
                var t = E.Div().Row().Radius(16).Pad(8, 12).Bg(new Color(0.07f, 0.07f, 0.13f, 0.86f)).Border(Theme.Alpha(edge, 0.4f)).Margin(0, 0, 6);
                t.style.maxWidth = 360;
                t.style.flexShrink = 0;
                t.Add(E.Icon(icon, 15, edge));
                var text = E.Text(toast.Notice.Message, 12.5f, Theme.Ink).Grow();
                text.style.marginLeft = 8;
                t.Add(text);
                t.style.opacity = 0;
                t.Transition(260, "opacity");
                t.schedule.Execute(() => t.style.opacity = 1).StartingIn(16);
                host.Add(t);
            }
        }

        // MARK: - Context card (placement / selected building / tapped resource)

        string ContextKey()
        {
            if (store.DecoratingId != null)
            {
                var d = G.Content.Decoration(store.DecoratingId);
                return $"deco|{store.DecoratingId}|{store.PaintMode}|{store.DecorationRotation}|{(d == null || G.State.Resources.Covers(d.Cost))}|{G.State.Decorations.Count}";
            }
            if (store.PlacingBuildingId != null)
            {
                var issues = store.PlacementIssues();
                return $"place|{store.PlacingBuildingId}|{(issues.Count > 0 ? issues[0].Message : "ok")}|{store.PlacementRotation}|{store.GhostCell}";
            }
            if (store.SelectedNode is (int nx, int ny) && store.SelectedBuildingId == null)
            {
                var n = G.NodeAt(nx, ny);
                return n == null ? "none" : $"node|{nx},{ny}|{n.Ordered}|{(int)Math.Min(1e6, n.Remaining)}|{G.IsExhausted(n.Job)}|{G.JobCapacity(n.Job)}";
            }
            if (store.SelectedBuildingId != null)
            {
                var b = G.State.Building(store.SelectedBuildingId.Value);
                if (b == null) return "none";
                var def = G.Content.Building(b.DefinitionId);
                var target = def?.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
                string up = target == null ? "" : $"{G.IsUnlocked(target)}{G.State.Resources.Covers(target.Cost)}";
                string jobs = string.Join(",", (def?.JobSlots ?? new List<JobSlot>()).Select(s => $"{G.State.Assignments[s.Job]}/{G.JobCapacity(s.Job)}"));
                var lv = G.NextLevel(b);
                string level = $"{b.Level}|{(lv == null ? "" : G.State.Resources.Covers(lv.Cost).ToString())}|{G.HasRoadAccess(b)}|{Time.unscaledTime < confirmDemolishUntil}|" +
                               string.Join("", G.LevelRequirements(b).Select(r => r.Met ? "1" : "0"));
                return $"sel|{b.Id}|{b.DefinitionId}|{b.Status}|{up}|{jobs}|{G.IdleWorkers > 0}|{level}|{(def?.Heart == true ? StokeKey() : "")}";
            }
            return "none";
        }

        void BuildContext(VisualElement host)
        {
            // A different selection starts at the top of its card; updates of the same one keep the scroll.
            string identity = $"{store.DecoratingId}|{store.PlacingBuildingId}|{store.SelectedBuildingId}|{store.SelectedNode}";
            if (identity != contextIdentity)
            {
                contextIdentity = identity;
                contextScroll.scrollOffset = Vector2.zero;
            }
            if (store.DecoratingId != null) DecorationCard(host);
            else if (store.PlacingBuildingId != null) PlacementCard(host);
            else if (store.SelectedBuildingId != null) BuildingCard(host);
            else if (store.SelectedNode != null) NodeCard(host);
        }

        void PlacementCard(VisualElement host)
        {
            var def = G.Content.Building(store.PlacingBuildingId);
            if (def == null) return;
            var issues = store.PlacementIssues();
            var card = E.Div().Col().Glass(22).Pad(14);
            card.Add(E.Eyebrow("Placing"));
            var head = E.Div().Row().Between().Margin(4).Ignore();
            head.Add(E.Text(def.Name, 18, Theme.Ink, Theme.F.Display));
            head.Add(E.Mono(Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds)), 12, Theme.Body));
            card.Add(head);
            card.Add(CostChips(def.Cost).Margin(8));
            var status = E.Div().Row().Margin(10).Ignore();
            bool ok = issues.Count == 0;
            status.Add(E.Icon(ok ? "check" : "alert", 15, ok ? Theme.Mint : Theme.Error));
            var msg = E.Text(ok ? "Good spot. Drag it or tap the ground to move it." : issues[0].Message, 12.5f, ok ? Theme.Mint : Theme.Error).Grow();
            msg.style.marginLeft = 7;
            status.Add(msg);
            card.Add(status);
            // Gatherers' camps: how much of their resource lies within reach of this spot.
            if (store.GhostCell is (int gx, int gy))
                foreach (var slot in def.JobSlots ?? new List<JobSlot>())
                {
                    int n = G.NodesInReach(slot.Job, gx, gy, Game.Rotated(def.Footprint, store.PlacementRotation));
                    if (n < 0) continue;
                    var kind = G.NodeKinds.FirstOrDefault(k => k.Job == slot.Job);
                    var reach = E.Div().Row().Margin(6).Ignore();
                    reach.Add(E.Icon(IconFor(kind.Resource), 14, n > 4 ? Theme.Mint : Theme.Error));
                    var rt = E.Text($"{kind.Name} in reach: {n}", 12, n > 4 ? Theme.Body : Theme.Error);
                    rt.style.marginLeft = 7;
                    reach.Add(rt);
                    card.Add(reach);
                }
            var buttons = E.Div().Row().Margin(12).Ignore();
            var rotate = E.IconButton("rotate", Act(store.RotatePlacement), 44);
            rotate.style.marginRight = 10;
            buttons.Add(rotate);
            // Cancel keeps its natural width so "Build here" never gets squeezed in the narrow side card.
            buttons.Add(E.Button("Cancel", Act(store.CancelPlacement), E.Kind.Ghost));
            var confirm = E.Button("Build here", Act(store.ConfirmPlacement), ok ? E.Kind.Gold : E.Kind.Disabled, 44, "build").Grow();
            confirm.style.marginLeft = 10;
            buttons.Add(confirm);
            card.Add(buttons);
            host.Add(card);
        }

        /// <summary>The town tool bar: what is being laid out, draw/tap mode, rotation, remove and done.</summary>
        void DecorationCard(VisualElement host)
        {
            bool removing = store.DecoratingId == GameStore.RemoveTool;
            var def = removing ? null : G.Content.Decoration(store.DecoratingId);
            var card = E.Div().Col().Glass(22).Pad(14);
            card.Add(E.Eyebrow(removing ? "Clearing" : def?.Category ?? "Town", removing ? Theme.Error : Theme.Gold));
            var head = E.Div().Row().Between().Margin(4).Ignore();
            head.Add(E.Text(removing ? "Remove decorations" : def?.Name ?? "", 18, Theme.Ink, Theme.F.Display));
            if (def != null && def.Beauty > 0) head.Add(StatChip("sparkle", $"Beauty +{def.Beauty}"));
            card.Add(head);
            var heat = HeatChips(def);
            if (heat != null) card.Add(heat);
            if (def != null && def.Cost.Entries.Any()) card.Add(CostChips(def.Cost).Margin(8));
            string hint = removing ? "Tap a road, fence or ornament to remove it. Half of its cost comes back."
                : store.PaintMode ? "Drag across the ground to draw. Tap the hand to move the camera instead."
                : "Tap the ground to place. Each tap places one.";
            card.Add(E.Text(hint, 12, Theme.Body).Margin(8));
            var buttons = E.Div().Row().Margin(12).Ignore();
            if (!removing)
            {
                var mode = E.IconButton(store.PaintMode ? "road" : "compass", Act(store.TogglePaintMode), 44, store.PaintMode ? Theme.Gold : (Color?)null, store.PaintMode);
                mode.style.marginRight = 8;
                buttons.Add(mode);
                var rot = E.IconButton("rotate", Act(store.RotatePlacement), 44);
                rot.style.marginRight = 8;
                buttons.Add(rot);
            }
            var trash = E.IconButton("trash", Act(() => store.BeginDecorating(removing ? "dirt_path" : GameStore.RemoveTool)), 44,
                                     removing ? Theme.Error : (Color?)null, removing);
            trash.style.marginRight = 10;
            buttons.Add(trash);
            buttons.Add(E.Button("Done", Act(store.EndDecorating), E.Kind.Gold, 44, "check").Grow());
            card.Add(buttons);
            host.Add(card);
        }

        /// <summary>A tapped tree, rock, ore vein, berry bush or fishing water: what is left, and send someone to it.</summary>
        void NodeCard(VisualElement host)
        {
            var (x, y) = store.SelectedNode.Value;
            var n = G.NodeAt(x, y);
            if (n == null) { store.SelectedNode = null; return; }
            string title = n.Kind?.Name ?? "Resource";
            string icon = n.Terrain == TerrainType.Forest ? "tree" : n.Terrain == TerrainType.Rock ? "stone" : n.Terrain == TerrainType.Ore ? "ore"
                : n.Terrain == TerrainType.Water ? "fish" : "food";
            var card = E.Div().Col().Glass(22).Pad(12, 14);
            var head = E.Div().Row().Ignore();
            var tile = E.Div().Size(44, 44).Radius(14).Center().Bg(Theme.Alpha(TintFor(n.Resource), 0.2f)).Ignore();
            tile.Add(E.Icon(icon, 23, TintFor(n.Resource)));
            head.Add(tile);
            var names = E.Div().Col().Grow().Ignore();
            names.style.marginLeft = 12;
            string jobName = G.Content.JobConfig(n.Job)?.Name ?? n.Job.Id();
            int crew = G.State.Assignments[n.Job];
            names.Add(E.Eyebrow(n.Ordered ? "Gathered first" : $"{jobName} · {crew} at work", n.Ordered ? Theme.Gold : Theme.Muted, 9.5f));
            names.Add(E.Text(title, 17, Theme.Ink, Theme.F.Display).Margin(2));
            head.Add(names);
            head.Add(E.IconButton("close", Act(() => store.SelectedNode = null), 34));
            card.Add(head);

            bool endless = double.IsInfinity(n.Remaining);
            var row = E.Div().Row().Between().Margin(10).Ignore();
            row.Add(E.Text($"{n.Resource.DisplayName()} left", 12, Theme.Body));
            double regrow = G.RegrowSecondsLeft(n);
            row.Add(E.Mono(endless ? "plenty" : n.Depleted ? (regrow > 0 ? $"back in {Fmt.Clock(regrow)}" : "used up") : $"{Fmt.Amount(n.Remaining)} / {Fmt.Amount(n.Capacity)}",
                           12, n.Depleted ? Theme.Error : Theme.Ink));
            card.Add(row);
            if (!endless)
            {
                var bar = E.Bar(out var fill, 6, Theme.GoldFill);
                E.SetFill(fill, n.Fraction);
                bar.style.marginTop = 6;
                card.Add(bar);
            }
            if (G.JobCapacity(n.Job) == 0)
                card.Add(Notice("build", $"Nobody can gather this yet: build a workplace for {jobName} nearby.", Theme.Gold).Margin(10));
            if (!n.Depleted)
            {
                string verb = n.Terrain == TerrainType.Forest ? "Send woodcutters" : n.Terrain == TerrainType.Rock ? "Send quarriers"
                    : n.Terrain == TerrainType.Ore ? "Send miners" : n.Terrain == TerrainType.Water ? "Send fishers" : "Send foragers";
                var buttons = E.Div().Row().Margin(10).Ignore();
                buttons.Add(E.Button(verb, Act(() => store.SendGatherer(x, y)), E.Kind.Gold, 42,
                                     n.Terrain == TerrainType.Forest ? "axe" : n.Terrain == TerrainType.Water ? "spear" : n.Terrain == TerrainType.Grass ? "food" : "pickaxe").Grow());
                if (n.Ordered)
                {
                    var cancel = E.IconButton("close", Act(() => store.ToggleHarvest(x, y)), 42);
                    cancel.style.marginLeft = 8;
                    buttons.Add(cancel);
                }
                card.Add(buttons);
            }
            host.Add(card);
        }

        VisualElement CostChips(ResourceBundle cost)
        {
            var row = E.Div().Row().Ignore();
            row.style.flexWrap = Wrap.Wrap;
            foreach (var kv in cost.Entries)
            {
                bool short_ = G.State.Resources[kv.Key] + 1e-6 < kv.Value;
                var chip = E.Div().Row().Radius(999).Pad(4, 8).Bg(short_ ? Theme.Alpha(Theme.Error, 0.12f) : new Color(1, 1, 1, 0.06f)).Ignore();
                chip.style.marginRight = 6;
                chip.style.marginBottom = 4;
                chip.Add(E.Icon(IconFor(kv.Key), 12, short_ ? Theme.Error : TintFor(kv.Key)));
                var t = E.Mono(Fmt.Amount(kv.Value), 11.5f, short_ ? Theme.Error : Theme.Ink);
                t.style.marginLeft = 4;
                chip.Add(t);
                row.Add(chip);
            }
            return row;
        }

        void BuildingCard(VisualElement host)
        {
            var b = G.State.Building(store.SelectedBuildingId.Value);
            var def = b == null ? null : G.Content.Building(b.DefinitionId);
            if (def == null) { store.SelectedBuildingId = null; return; }
            int id = b.Id;
            var card = E.Div().Col().Glass(22).Pad(14);

            var head = E.Div().Row().Ignore();
            head.Add(BuildingTile(def, 46));
            var names = E.Div().Col().Grow().Ignore();
            names.style.marginLeft = 12;
            names.Add(E.Eyebrow(b.Status == BuildingStatus.Complete ? Category(def) : b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Under construction",
                                b.Status == BuildingStatus.Complete ? Theme.Muted : Theme.Gold, 9.5f));
            var title = E.Text(G.DisplayName(b), 18, Theme.Ink, Theme.F.Display);
            title.style.marginTop = 2;
            names.Add(title);
            head.Add(names);
            var lvBadge = E.Div().Size(38, 38).Radius(19).Center().BgImage(Theme.Gradient("goldround", true, GoldTop, GoldBottom)).Border(GoldEdge, 2f).Ignore();
            lvBadge.style.marginRight = 6;
            lvBadge.Add(E.Text(b.Level.ToString(CultureInfo.InvariantCulture), 15, Theme.Hex("#5A3208"), Theme.F.DisplayBold));
            head.Add(lvBadge);
            head.Add(E.IconButton("close", Act(() => store.SelectedBuildingId = null), 34));
            card.Add(head);

            var desc = E.Text(def.Description, 12.5f, Theme.Body).Margin(10);
            card.Add(desc);

            if (b.Status != BuildingStatus.Complete)
            {
                var row = E.Div().Row().Between().Margin(10).Ignore();
                row.Add(E.Text("Progress", 11.5f, Theme.Muted));
                row.Add(Live(() => $"{Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0))} left", 11.5f, Theme.Body, Theme.F.Mono));
                card.Add(row);
                var bar = LiveBar(() => G.State.Building(id)?.Progress ?? 1, 6);
                bar.style.marginTop = 6;
                card.Add(bar);
            }

            var stats = E.Div().Row().Margin(10).Ignore();
            stats.style.flexWrap = Wrap.Wrap;
            if (def.HousingValue > 0) stats.Add(StatChip("home", $"Houses {G.HousingOf(b, def)}"));
            if (def.StorageValue > 0) stats.Add(StatChip("storage", $"+{(int)(def.StorageValue * Game.LevelFactor(b.Level))} storage"));
            if (def.ProtectedFoodValue > 0) stats.Add(StatChip("food", $"Keeps {(int)def.ProtectedFoodValue} food"));
            if (stats.childCount > 0) card.Add(stats);
            WarmthSection(card, b, def);

            foreach (var slot in def.JobSlots ?? new List<JobSlot>())
                card.Add(JobRow(slot.Job, true).Margin(10));

            var target = def.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
            if (target != null && b.Status == BuildingStatus.Complete)
            {
                bool locked = !G.IsUnlocked(target);
                var up = E.Div().Col().Radius(18).Pad(12).BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.3f)).Margin(12);
                var r1 = E.Div().Row().Between().Ignore();
                r1.Add(E.Text($"Upgrade to {target.Name}", 14, Theme.Ink, Theme.F.Display));
                r1.Add(E.Mono(Fmt.Clock(G.EstimatedSeconds(target.BuildSeconds)), 11.5f, Theme.Body));
                up.Add(r1);
                if (locked)
                    up.Add(E.Text($"Requires {G.Content.Technology(target.RequiresTech ?? "")?.Name ?? "research"}", 12, Theme.Error).Margin(6));
                else
                {
                    up.Add(CostChips(target.Cost).Margin(8));
                    bool can = G.State.Resources.Covers(target.Cost);
                    up.Add(E.Button(can ? "Upgrade" : "Not enough resources", can ? Act(() => store.Upgrade(id)) : null, can ? E.Kind.Gold : E.Kind.Disabled, 42).Margin(8));
                }
                card.Add(up);
            }
            var next = def.SuccessorId == null ? null : G.Content.Building(def.SuccessorId);
            if (next != null) card.Add(E.Text($"Becomes the {next.Name} when the settlement grows.", 11.5f, Theme.Muted).Margin(10));

            // Levels (Foundation-style upgrades of the same building) and road access.
            var info = E.Div().Row().Margin(10).Ignore();
            info.style.flexWrap = Wrap.Wrap;
            info.Add(StatChip("level_up", $"Level {b.Level}/{def.MaxLevel}"));
            if (!def.Heart) info.Add(StatChip("flame", $"{G.HeartName} {G.HeartLevel}"));
            if (def.JobSlots != null && def.JobSlots.Count > 0)
                info.Add(StatChip("road", G.HasRoadAccess(b) ? "On a road +10%" : "No road"));
            card.Add(info);
            var lv = G.NextLevel(b);
            if (lv != null && b.Status == BuildingStatus.Complete)
            {
                var up = E.Div().Col().Radius(18).Pad(12).BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.3f)).Margin(10);
                var r1 = E.Div().Row().Between().Ignore();
                string gain = def.Heart ? "the heart of the settlement grows" : "+25% capacity";
                r1.Add(E.Text($"Level {b.Level + 1}: {gain}", 14, Theme.Ink, Theme.F.Display).Grow());
                r1.Add(E.Mono(Fmt.Clock(G.EstimatedSeconds(lv.BuildSeconds)), 11.5f, Theme.Body));
                up.Add(r1);
                // What the next looks and eras are (a tent becomes a hut at 5; the camp a village at 10…).
                var look = def.Looks.FirstOrDefault(l => l.Level > b.Level);
                if (look != null && !string.IsNullOrEmpty(look.Name))
                    up.Add(E.Text($"Becomes a {look.Name} at level {look.Level}.", 11.5f, Theme.GoldLight).Margin(4));
                if (def.Heart)
                {
                    var era = G.Content.Milestones.Where(m => !G.State.ReachedMilestones.Contains(m.Id))
                        .Select(m => (m, lvl: m.Requirements.FirstOrDefault(r => r.Kind == ConditionKind.HeartLevelAtLeast)?.Amount ?? 0))
                        .Where(x => x.lvl > b.Level).OrderBy(x => x.lvl).FirstOrDefault();
                    if (era.m != null) up.Add(E.Text($"Level {era.lvl:0} opens: {era.m.Name}.", 11.5f, Theme.GoldLight).Margin(4));
                }
                var reqs = G.LevelRequirements(b);
                foreach (var r in reqs)
                {
                    var rr = E.Div().Row().Margin(4).Ignore();
                    rr.Add(E.Icon(r.Met ? "check" : "lock", 12, r.Met ? Theme.Mint : Theme.Error));
                    var rt = E.Text(r.Text, 11.5f, r.Met ? Theme.Body : Theme.Error).Grow();
                    rt.style.marginLeft = 6;
                    rr.Add(rt);
                    up.Add(rr);
                }
                up.Add(CostChips(lv.Cost).Margin(8));
                bool ready = reqs.All(r => r.Met);
                bool can = ready && G.State.Resources.Covers(lv.Cost);
                string label = !ready ? "Requirements not met" : can ? "Level up" : "Not enough resources";
                up.Add(E.Button(label, can ? Act(() => store.UpgradeLevel(id)) : null, can ? E.Kind.Gold : E.Kind.Disabled, 42, can ? "level_up" : null).Margin(8));
                card.Add(up);
            }
            if (def.Buildable)
            {
                bool confirm = Time.unscaledTime < confirmDemolishUntil;
                var demolish = E.Button(confirm ? "Tap again to demolish (50% back)" : "Demolish",
                    Act(() =>
                    {
                        if (Time.unscaledTime < confirmDemolishUntil) { confirmDemolishUntil = 0; store.Demolish(id); }
                        else confirmDemolishUntil = Time.unscaledTime + 3f;
                    }), confirm ? E.Kind.Danger : E.Kind.Ghost, 38, "trash").Margin(10);
                card.Add(demolish);
            }
            host.Add(card);
        }

        VisualElement StatChip(string icon, string text)
        {
            var c = E.Div().Row().Radius(999).Pad(5, 10).Bg(new Color(1, 1, 1, 0.06f)).Ignore();
            c.style.marginRight = 6;
            c.style.marginBottom = 4;
            c.Add(E.Icon(icon, 13, Theme.GoldLight));
            var t = E.Text(text, 11.5f, Theme.Ink, Theme.F.TextMedium);
            t.style.marginLeft = 5;
            c.Add(t);
            return c;
        }

        /// <summary>Category label used for tabs and eyebrows.</summary>
        static string Category(BuildingDefinition d)
        {
            if (d.HousingValue > 0) return "Shelter";
            if (d.JobSlots != null && d.JobSlots.Any(s => s.Job == JobType.Foraging || s.Job == JobType.Farming)) return "Food";
            if (d.StorageValue > 0 || d.ProtectedFoodValue > 0) return "Storage";
            return "Work";
        }

        static readonly Dictionary<string, (Color a, Color b, string icon)> TileLook = new Dictionary<string, (Color, Color, string)>
        {
            ["Shelter"] = (Theme.Hex("#7a4a2c"), Theme.Hex("#3b2416"), "home"),
            ["Food"] = (Theme.Hex("#4f6b3a"), Theme.Hex("#22301a"), "food"),
            ["Storage"] = (Theme.Hex("#6b5a3a"), Theme.Hex("#2e2616"), "storage"),
            ["Work"] = (Theme.Hex("#4a5466"), Theme.Hex("#1f2430"), "build"),
        };

        VisualElement BuildingTile(BuildingDefinition def, float size)
        {
            string cat = Category(def);
            var look = TileLook[cat];
            var t = E.Div().Size(size, size).Radius(size * 0.3f).Center().Border(new Color(1, 1, 1, 0.12f)).Ignore();
            t.BgImage(Theme.Gradient("tile" + cat, true, look.a, look.b));
            string icon = def.Id.Contains("heal") ? "sick" : def.Id.Contains("church") ? "landmark" : def.Id.Contains("mill") ? "wheat" : def.Id.Contains("market") ? "storage"
                : def.Id.Contains("smithy") ? "pickaxe" : def.Id.Contains("farm") ? "spring" : def.Id.Contains("fire") || def.Id.Contains("kiln") || def.Id.Contains("smelter") ? "flame"
                : def.Id.Contains("stone") ? "stone" : def.Id.Contains("wood") ? "wood" : look.icon;
            t.Add(E.Icon(icon, size * 0.46f, Theme.Alpha(Theme.GoldPale, 0.95f)));
            return t;
        }

        /// <summary>Worker count for a job with −/+ steppers and its effect.</summary>
        VisualElement JobRow(JobType job, bool compact)
        {
            var config = G.Content.JobConfig(job);
            var row = E.Div().Row().Radius(16).Pad(10, 10, 10, 12).Bg(new Color(1, 1, 1, 0.045f)).Border(Theme.EdgeSoft);
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.Text(config?.Name ?? job.Id(), compact ? 13 : 14, Theme.Ink, Theme.F.TextBold));
            var effect = Live(() => EffectText(job, G.State.Assignments[job]), 11, Theme.Body);
            effect.style.marginTop = 2;
            col.Add(effect);
            row.Add(col);

            int workers = G.State.Assignments[job];
            int capacity = G.JobCapacity(job);
            bool unlimited = config?.UnlimitedSlots ?? false;
            row.Add(Stepper("minus", workers > 0, () => store.SetWorkers(job, G.State.Assignments[job] - 1)));
            var count = E.Mono(unlimited ? $"{workers}" : $"{workers}/{capacity}", 14).Align(TextAnchor.MiddleCenter);
            count.style.width = 46;
            row.Add(count);
            row.Add(Stepper("plus", G.IdleWorkers > 0 && workers < capacity, () => store.SetWorkers(job, G.State.Assignments[job] + 1)));
            return row;
        }

        VisualElement Stepper(string icon, bool enabled, Action onTap)
        {
            var b = E.Div().Size(36, 36).Radius(12).Center();
            if (enabled) b.Bg(new Color(1, 1, 1, 0.08f)).Border(Theme.Edge);
            else b.Bg(new Color(1, 1, 1, 0.03f)).Border(Theme.EdgeSoft);
            b.Add(E.Icon(icon, 16, enabled ? Theme.Ink : Theme.Faint));
            if (enabled) Tap(b, onTap); else b.pickingMode = PickingMode.Position;
            return b;
        }

        string EffectText(JobType job, int workers)
        {
            var config = G.Content.JobConfig(job);
            if (config == null) return "";
            if (job == JobType.Healing)
            {
                int sick = G.SickCount, care = G.CareCapacity;
                if (workers == 0) return sick > 0 ? $"{sick} ill and nobody tends them" : "Tends the sick: they get well quickly";
                return sick == 0 ? $"Nobody is ill · room for {care}" : $"Tending {Math.Min(sick, care)} of {sick} ill · room for {care}";
            }
            if (config.Output.HasValue)
            {
                double each = G.RatePerWorker(job) * 60;
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}/m each · {2:0.0}/m total",
                                     config.Output.Value.DisplayName(), each, each * workers);
            }
            switch (job)
            {
                case JobType.Construction:
                    return string.Format(CultureInfo.InvariantCulture, "Build speed ×{0:0.0}", G.ConstructionSpeed());
                case JobType.Crafting:
                    if (workers == 0) return "Tools boost all gathering";
                    return econ.CraftingActive
                        ? string.Format(CultureInfo.InvariantCulture, "Gathering +{0:0}% · uses wood & stone", econ.CraftingBonus * 100)
                        : "Needs wood and stone to work";
                default:
                    return config.Description;
            }
        }
    }
}
