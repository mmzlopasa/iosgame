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

        public static GameUi Create(GameObject host)
        {
            var settings = UnityEngine.Resources.Load<PanelSettings>("UI/FoundersPanel");
            if (settings == null)
            {
                Debug.LogWarning("[FOUNDERS] PanelSettings asset missing; using a runtime instance.");
                settings = ScriptableObject.CreateInstance<PanelSettings>();
            }
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(390, 844);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            // Phones are matched on width; wide screens (iPad) on height so the layout keeps its proportions.
            settings.match = Screen.width / (float)Mathf.Max(1, Screen.height) > 0.6f ? 1f : 0f;
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
            hudLayer.Add(E.Div().Abs(0, 0, 0).Size(null, 230).BgImage(Theme.TopShade).Ignore());
            hudLayer.Add(E.Div().Abs(0, null, 0, 0).Size(null, 190).BgImage(Theme.BottomShade).Ignore());
            hudLayer.Add(safe);

            sheetLayer = E.Div("sheets").Fill().Ignore().Hidden(true);
            modalLayer = E.Div("modals").Fill().Ignore().Hidden(true);
            loadingLayer = E.Div("loading").Fill().Ignore().Hidden(true);
            root.Add(hudLayer);
            // While the HUD is hidden only this small eye button remains, to bring it back.
            showHud = E.IconButton("eye", Act(() => store.HudHidden = false), 44, null, true).Abs(null, 64, 18);
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
            if (color.HasValue) fill.style.backgroundImage = StyleKeyword.None;
            Bind(() => E.SetFill(fill, value()));
            return bar;
        }

        /// <summary>Wraps a UI action so the screen refreshes on the next frame.</summary>
        Action Act(Action a) => () => { AudioDirector.Ui("ui_tap"); a(); dirty = true; };

        VisualElement Tap(VisualElement e, Action a) => e.OnTap(Act(a));

        void Update()
        {
            if (store?.Game == null || root == null) return;
            UpdateSafeArea();
            hudLayer.Hidden(Hidden || store.HudHidden);
            showHud?.Hidden(Hidden || !store.HudHidden);
            if (Hidden) { sheetLayer.Hidden(true); modalLayer.Hidden(true); return; }
            if (store.HudHidden) { sheetLayer.Hidden(true); return; }

            if (!dirty && Time.unscaledTime < nextTick) return;
            dirty = false;
            nextTick = Time.unscaledTime + 0.1f;

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

        void UpdateSafeArea()
        {
            float w = root.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            var s = Screen.safeArea;
            if (s == lastSafe && Mathf.Approximately(w, lastWidth)) return;
            lastSafe = s;
            lastWidth = w;
            float k = w / Screen.width;
            safe.style.paddingTop = (Screen.height - s.yMax) * k + 4;
            safe.style.paddingBottom = Mathf.Max(10, s.yMin * k);
            safe.style.paddingLeft = s.xMin * k;
            safe.style.paddingRight = (Screen.width - s.xMax) * k;
            if (sheet != null) sheet.style.top = (Screen.height - s.yMax) * k + 34;
            SafeBottom = s.yMin * k;
            dirty = true;
        }

        float SafeBottom;

        // MARK: - HUD

        void BuildHud()
        {
            var top = E.Div("top").Ignore().Col();
            AddRegion(top, () => $"{G.State.SettlementName}|{G.State.StageId}|{G.State.EpochId}", BuildTopBar);
            safe.Add(top);

            var rails = E.Div("rails").Ignore().Row(0, Align.FlexStart).Margin(10, 14, 0, 14);
            var left = E.Div("left-rail").Ignore().Col().Size(222);
            var right = E.Div("right-rail").Ignore().Col().Size(118);
            rails.Add(left);
            rails.Add(E.Div().Grow().Ignore());
            rails.Add(right);
            AddRegion(left, LeftRailKey, BuildLeftRail);
            AddRegion(right, RightRailKey, BuildRightRail);
            safe.Add(rails);

            var toasts = E.Div("toasts").Ignore().Col(Align.Center).Margin(12, 16, 0, 16);
            AddRegion(toasts, () => string.Join(",", store.Toasts.Select(t => t.CreatedAt.ToString(CultureInfo.InvariantCulture))), BuildToasts);
            safe.Add(toasts);

            safe.Add(E.Div().Grow().Ignore());

            var context = E.Div("context").Ignore().Col().Margin(0, 12, 10, 12);
            AddRegion(context, ContextKey, BuildContext);
            safe.Add(context);

            var dock = E.Div("dock").Ignore().Margin(0, 12, 0, 12);
            AddRegion(dock, DockKey, BuildDock);
            safe.Add(dock);
        }

        void BuildTopBar(VisualElement host)
        {
            int era = G.Content.Epoch(G.State.EpochId)?.Index ?? 1;
            var row = E.Div().Row().Pad(4, 14, 0, 14).Ignore();

            var avatar = Avatar(48, 17);
            Tap(avatar, () => store.ActivePanel = Panel.Profile);
            row.Add(avatar);

            var names = E.Div().Col().Grow().Ignore();
            names.style.marginLeft = 10;
            names.Add(E.Text(G.State.SettlementName, 17, Theme.Ink, Theme.F.Display).NoWrap());
            var sub = E.Eyebrow($"{G.StageName} · Era {Fmt.Roman(era)}", Theme.Gold, 9.5f).NoWrap();
            sub.style.marginTop = 2;
            names.Add(sub);
            row.Add(names);

            var gear = E.IconButton("settings", Act(() => store.ActivePanel = Panel.Settings), 38, Theme.Ink, true);
            row.Add(gear);
            host.Add(row);

            var grid = E.Div().Row(0, Align.Stretch).Margin(10, 14, 0, 14).Ignore();
            int i = 0;
            foreach (var type in PrimaryResources) grid.Add(ResourcePill(type, i++ > 0));
            grid.Add(PeoplePill());
            host.Add(grid);
        }

        /// <summary>Resources shown in the top bar (ore and hides appear in costs and the stores panel).</summary>
        static readonly ResourceType[] PrimaryResources = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge };

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

        VisualElement PillShell(bool gap)
        {
            var cell = E.Div().Col(Align.Center).Grow().Glass(14).Pad(7, 2, 6, 2);
            if (gap) cell.style.marginLeft = 6;
            return cell;
        }

        VisualElement ResourcePill(ResourceType type, bool gap)
        {
            var cell = PillShell(gap);
            var line = E.Div().Row().Ignore();
            line.Add(E.Icon(IconFor(type), 14, TintFor(type)));
            var value = E.Mono("", 14);
            value.style.marginLeft = 4;
            Bind(() =>
            {
                double v = G.State.Resources[type];
                value.text = Fmt.Amount(v);
                value.style.color = v >= caps[type] - 0.5 ? Theme.Gold : Theme.Ink;
            });
            line.Add(value);
            cell.Add(line);
            var rate = E.Mono("", 9.5f, Theme.Muted).NoWrap();
            rate.style.marginTop = 2;
            Bind(() =>
            {
                double r = econ.Net[type];
                rate.text = Math.Abs(r) < 0.0005 ? (type.DisplayName()) : Fmt.PerMinute(r);
                rate.style.color = r < -0.0005 ? Theme.Error : r > 0.0005 ? Theme.Mint : Theme.Muted;
            });
            cell.Add(rate);
            Tap(cell, () => store.ActivePanel = type == ResourceType.Knowledge ? Panel.Research : Panel.People);
            return cell;
        }

        VisualElement PeoplePill()
        {
            var cell = PillShell(true);
            var line = E.Div().Row().Ignore();
            line.Add(E.Icon("people", 14, Theme.Ink));
            var value = E.Mono("", 14);
            value.style.marginLeft = 4;
            Bind(() => value.text = $"{G.State.Population}/{G.HousingCapacity()}");
            line.Add(value);
            cell.Add(line);
            var sub = E.Mono("", 9.5f).NoWrap();
            sub.style.marginTop = 2;
            Bind(() =>
            {
                sub.text = G.State.FoodShortage ? "hungry" : G.IdleWorkers > 0 ? $"{G.IdleWorkers} idle" : "all busy";
                sub.style.color = G.State.FoodShortage ? Theme.Error : G.IdleWorkers > 0 ? Theme.Gold : Theme.Muted;
            });
            cell.Add(sub);
            Tap(cell, () => store.ActivePanel = Panel.People);
            return cell;
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

        // MARK: - Left rail: goal + quick actions

        string LeftRailKey()
        {
            var o = G.CurrentObjective;
            bool ev = G.PendingEvent != null && store.EventMinimized;
            return $"{o?.Id}|{ev}|{G.IdleWorkers > 0}|{G.IdleWorkers}|{MilestoneReady()}";
        }

        bool MilestoneReady() => G.Content.Milestones.Any(m => G.CanClaimMilestone(m.Id));

        void BuildLeftRail(VisualElement host)
        {
            var o = G.CurrentObjective;
            if (o != null)
            {
                int index = G.Content.Objectives.FindIndex(x => x.Id == o.Id) + 1;
                var card = E.Div("goal").Row(0, Align.FlexStart).Glass(16, Theme.Alpha(Theme.Gold, 0.34f)).Pad(11, 12);
                var tile = E.Div().Size(28, 28).Radius(9).Center().Bg(Theme.Alpha(Theme.Gold, 0.16f)).Ignore();
                tile.Add(E.Icon("goals", 16, Theme.Gold));
                card.Add(tile);
                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 10;
                col.Add(E.Eyebrow($"Goal {index} of {G.Content.Objectives.Count}", Theme.Gold, 9.5f));
                var title = E.Text(o.Title, 13.5f, Theme.Ink, Theme.F.TextBold);
                title.style.marginTop = 3;
                col.Add(title);
                var hint = Live(() =>
                {
                    var st = G.Status(o.Conditions);
                    var next = st.FirstOrDefault(s => !s.Met);
                    return next.Text ?? (st.Count > 0 ? st[0].Text : o.Hint);
                }, 11.5f, Theme.Body);
                hint.style.marginTop = 3;
                col.Add(hint);
                var bar = LiveBar(() =>
                {
                    var st = G.Status(o.Conditions);
                    return st.Count == 0 ? 0 : st.Count(s => s.Met) / (double)st.Count;
                }, 4);
                bar.style.marginTop = 7;
                col.Add(bar);
                card.Add(col);
                Tap(card, () => store.ActivePanel = Panel.Objectives);
                host.Add(card);
            }
            else if (MilestoneReady())
            {
                var card = E.Div().Row().Glass(16, Theme.Alpha(Theme.Gold, 0.5f)).Pad(11, 12);
                card.Add(E.Icon("sparkle", 18, Theme.Gold));
                var t = E.Text("A milestone is ready to claim", 13, Theme.Ink, Theme.F.TextBold).Grow();
                t.style.marginLeft = 8;
                card.Add(t);
                Tap(card, () => store.ActivePanel = Panel.Objectives);
                host.Add(card);
            }

            var quick = E.Div().Row().Ignore().Margin(8);
            if (G.PendingEvent != null && store.EventMinimized)
                quick.Add(QuickButton("bell", "1", Theme.Gold, Theme.OnGold, () => store.EventMinimized = false));
            quick.Add(QuickButton("legacy", null, default, default, () => store.ActivePanel = Panel.Map));
            quick.Add(QuickButton("people", G.IdleWorkers > 0 ? G.IdleWorkers.ToString(CultureInfo.InvariantCulture) : null, Theme.Mint, Theme.Hex("#0D2A1F"),
                                  () => store.ActivePanel = Panel.People));
            host.Add(quick);
        }

        VisualElement QuickButton(string icon, string badge, Color badgeBg, Color badgeFg, Action onTap)
        {
            var b = E.Div().Size(46, 46).Glass(14).Center();
            b.style.marginRight = 8;
            b.Add(E.Icon(icon, 20, Theme.Ink));
            if (badge != null) b.Add(E.Badge(badge, badgeBg, badgeFg).Abs(null, -5, -5));
            Tap(b, onTap);
            return b;
        }

        // MARK: - Right rail: running timers

        string RightRailKey()
        {
            var r = G.State.ActiveResearch;
            var e = G.State.ActiveExpedition;
            var builds = string.Join(",", G.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).Select(b => b.Id));
            return $"{r?.TechId}|{e?.RegionId}|{builds}|{G.SeasonIndex}|{G.Freezing}";
        }

        static string SeasonIcon(string id) => id switch { "summer" => "summer", "autumn" => "autumn", "winter" => "winter", _ => "spring" };

        static Color SeasonTint(string id) => id switch
        {
            "summer" => Theme.Gold,
            "autumn" => Theme.Hex("#F0A35E"),
            "winter" => Theme.Hex("#A9D8FF"),
            _ => Theme.Mint,
        };

        static Color ApprovalColor(int a) => a >= 65 ? Theme.Mint : a >= 40 ? Theme.Gold : Theme.Error;

        /// <summary>Season, year and approval at a glance (tap: People & wellbeing).</summary>
        VisualElement SeasonCard()
        {
            var season = G.Season;
            var c = E.Div("season").Col().Glass(16, G.Freezing ? Theme.Alpha(Theme.Error, 0.5f) : (Color?)null).Pad(10).Margin(0, 0, 8);
            if (season != null)
            {
                var head = E.Div().Row().Ignore();
                head.Add(E.Icon(SeasonIcon(season.Id), 14, SeasonTint(season.Id)));
                var name = E.Text(season.Name, 12.5f, Theme.Ink, Theme.F.TextBold);
                name.style.marginLeft = 5;
                head.Add(name);
                head.Add(E.Div().Grow().Ignore());
                head.Add(Live(() => $"Y{G.Year}", 10, Theme.Muted, Theme.F.Mono));
                c.Add(head);
                var bar = LiveBar(() => G.SeasonProgress, 3, Theme.Gradient("season" + season.Id, false, SeasonTint(season.Id), Theme.Alpha(SeasonTint(season.Id), 0.7f)));
                bar.style.marginTop = 6;
                c.Add(bar);
            }
            var row = E.Div().Row().Margin(7).Ignore();
            row.Add(E.Icon("approval", 13, Theme.Body));
            var label = E.Text("Approval", 10.5f, Theme.Body);
            label.style.marginLeft = 5;
            row.Add(label);
            row.Add(E.Div().Grow().Ignore());
            var value = E.Mono("", 12);
            Bind(() => { int a = G.Approval; value.text = a + "%"; value.style.color = ApprovalColor(a); });
            row.Add(value);
            c.Add(row);
            if (G.Freezing) c.Add(E.Text("Freezing! Gather wood.", 10.5f, Theme.Error, Theme.F.TextBold).Margin(5));
            Tap(c, () => store.ActivePanel = Panel.People);
            return c;
        }

        void BuildRightRail(VisualElement host)
        {
            var tools = E.Div().Row().Ignore();
            tools.style.justifyContent = Justify.FlexEnd;
            tools.style.marginBottom = 8;
            tools.Add(E.IconButton("eye_off", Act(() =>
            {
                store.CancelPlacement();
                store.SelectedBuildingId = null;
                store.ActivePanel = Panel.None;
                store.HudHidden = true;
            }), 38));
            host.Add(tools);
            host.Add(SeasonCard());
            var r = G.State.ActiveResearch;
            if (r != null)
            {
                var tech = G.Content.Technology(r.TechId);
                host.Add(TimerCard("knowledge", "Research", Theme.Lavender, tech?.Name ?? r.TechId,
                    () => 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds), () => $"{Fmt.Clock(r.RemainingSeconds)} left",
                    Theme.LavenderFill, () => store.ActivePanel = Panel.Research));
            }
            foreach (var b in G.State.Buildings.Where(x => x.Status != BuildingStatus.Complete).Take(2))
            {
                var def = G.Content.Building(b.Status == BuildingStatus.Upgrading && b.UpgradeTargetId != null ? b.UpgradeTargetId : b.DefinitionId);
                int id = b.Id;
                host.Add(TimerCard("build", b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Building", Theme.Gold, def?.Name ?? b.DefinitionId,
                    () => G.State.Building(id)?.Progress ?? 1, () => $"{Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0))} left",
                    Theme.GoldFill, () => store.FocusBuilding(id)));
            }
            var e = G.State.ActiveExpedition;
            if (e != null)
            {
                var region = G.Content.Regions.FirstOrDefault(x => x.Id == e.RegionId);
                host.Add(TimerCard("compass", "Scouting", Theme.Mint, region?.Name ?? e.RegionId,
                    () => 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds), () => $"{Fmt.Clock(e.RemainingSeconds)} left",
                    Theme.MintFill, () => store.ActivePanel = Panel.Map));
            }
        }

        VisualElement TimerCard(string icon, string label, Color accent, string name, Func<double> progress, Func<string> left,
                                Texture2D fill, Action onTap)
        {
            var c = E.Div().Col().Glass(16).Pad(10).Margin(0, 0, 8);
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon(icon, 12, accent));
            var l = E.Eyebrow(label, Theme.Muted, 9);
            l.style.marginLeft = 5;
            head.Add(l);
            c.Add(head);
            var n = E.Text(name, 12, Theme.Ink, Theme.F.TextBold).NoWrap();
            n.style.marginTop = 5;
            c.Add(n);
            var bar = LiveBar(progress, 4, fill);
            bar.style.marginTop = 6;
            c.Add(bar);
            var t = Live(left, 10.5f, Theme.Body, Theme.F.Mono);
            t.style.marginTop = 5;
            c.Add(t);
            Tap(c, onTap);
            return c;
        }

        // MARK: - Toasts

        void BuildToasts(VisualElement host)
        {
            foreach (var toast in store.Toasts)
            {
                Color edge; string icon;
                switch (toast.Notice.Kind)
                {
                    case NoticeKind.Success: edge = Theme.Mint; icon = "check"; break;
                    case NoticeKind.Warning: edge = Theme.Error; icon = "alert"; break;
                    case NoticeKind.Milestone: edge = Theme.Gold; icon = "sparkle"; break;
                    default: edge = Theme.Lavender; icon = "bell"; break;
                }
                var t = E.Div().Row().Radius(18).Pad(9, 14).Bg(new Color(0.07f, 0.07f, 0.13f, 0.86f)).Border(Theme.Alpha(edge, 0.4f)).Margin(0, 0, 6);
                t.style.maxWidth = 350;
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

        // MARK: - Dock

        static readonly (string label, string icon, Panel panel)[] DockItems =
        {
            ("Build", "build", Panel.Build),
            ("People", "people", Panel.People),
            ("Research", "knowledge", Panel.Research),
            ("Goals", "goals", Panel.Objectives),
            ("World", "world", Panel.Map),
        };

        bool DockBadge(Panel p)
        {
            switch (p)
            {
                case Panel.People: return G.IdleWorkers > 0 || G.Freezing;
                case Panel.Research:
                    return G.State.ActiveResearch == null && G.Content.Technologies.Any(t =>
                        G.GetResearchStatus(t.Id).State == ResearchState.Available && G.State.Resources.Covers(t.Cost));
                case Panel.Objectives: return MilestoneReady();
                default: return false;
            }
        }

        string DockKey() => store.ActivePanel + "|" + string.Join("", DockItems.Select(d => DockBadge(d.panel) ? "1" : "0"));

        void BuildDock(VisualElement host)
        {
            var dock = E.Div().Row(0, Align.Stretch).Radius(26).Pad(8).BgImage(Theme.DockFill).Border(Theme.Edge);
            dock.style.borderTopColor = new Color(1, 1, 1, 0.22f);
            foreach (var item in DockItems)
            {
                bool on = store.ActivePanel == item.panel;
                var b = E.Div().Col(Align.Center).Grow().Radius(18).Center();
                b.style.height = 56;
                if (on) b.BgImage(Theme.GoldFill);
                var fg = on ? Theme.OnGold : Theme.Body;
                b.Add(E.Icon(item.icon, 22, fg));
                var l = E.Text(item.label, 11, fg, Theme.F.TextBold).NoWrap();
                l.style.marginTop = 4;
                b.Add(l);
                if (!on && DockBadge(item.panel))
                {
                    var dot = E.Div().Ignore().Size(8, 8).Radius(4).Bg(Theme.Gold).Abs(null, 8, 16);
                    b.Add(dot);
                }
                var panel = item.panel;
                Tap(b, () =>
                {
                    store.CancelPlacement();
                    store.ActivePanel = store.ActivePanel == panel ? Panel.None : panel;
                });
                dock.Add(b);
            }
            host.Add(dock);
        }

        // MARK: - Context card (placement / selected building / waiting decision)

        string ContextKey()
        {
            var ev = G.PendingEvent != null && store.EventMinimized ? G.PendingEvent.Id : "";
            if (store.DecoratingId != null)
            {
                var d = G.Content.Decoration(store.DecoratingId);
                return $"deco|{store.DecoratingId}|{store.PaintMode}|{store.DecorationRotation}|{(d == null || G.State.Resources.Covers(d.Cost))}|{G.State.Decorations.Count}";
            }
            if (store.PlacingBuildingId != null)
            {
                var issues = store.PlacementIssues();
                return $"place|{store.PlacingBuildingId}|{(issues.Count > 0 ? issues[0].Message : "ok")}|{store.PlacementRotation}|{ev}";
            }
            if (store.SelectedBuildingId != null)
            {
                var b = G.State.Building(store.SelectedBuildingId.Value);
                if (b == null) return "none|" + ev;
                var def = G.Content.Building(b.DefinitionId);
                var target = def?.UpgradesTo == null ? null : G.Content.Building(def.UpgradesTo);
                string up = target == null ? "" : $"{G.IsUnlocked(target)}{G.State.Resources.Covers(target.Cost)}";
                string jobs = string.Join(",", (def?.JobSlots ?? new List<JobSlot>()).Select(s => $"{G.State.Assignments[s.Job]}/{G.JobCapacity(s.Job)}"));
                var lv = G.NextLevel(b);
                string level = $"{b.Level}|{(lv == null ? "" : G.State.Resources.Covers(lv.Cost).ToString())}|{G.HasRoadAccess(b)}|{Time.unscaledTime < confirmDemolishUntil}";
                return $"sel|{b.Id}|{b.DefinitionId}|{b.Status}|{up}|{jobs}|{G.IdleWorkers > 0}|{level}|{ev}";
            }
            return "none|" + ev;
        }

        void BuildContext(VisualElement host)
        {
            if (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null)
            {
                var pill = E.Div().Row().Center().Radius(999).BgImage(Theme.GoldFill).Pad(11, 16).Margin(0, 0, 8);
                pill.Add(E.Icon("bell", 16, Theme.OnGold));
                var t = E.Text($"Decision waiting · {G.PendingEvent.Title}", 13, Theme.OnGold, Theme.F.TextBold).NoWrap();
                t.style.marginLeft = 8;
                pill.Add(t);
                Tap(pill, () => store.EventMinimized = false);
                host.Add(pill);
            }
            if (store.DecoratingId != null) DecorationCard(host);
            else if (store.PlacingBuildingId != null) PlacementCard(host);
            else if (store.SelectedBuildingId != null) BuildingCard(host);
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
            var msg = E.Text(ok ? "Good spot. Tap the ground to move it." : issues[0].Message, 12.5f, ok ? Theme.Mint : Theme.Error).Grow();
            msg.style.marginLeft = 7;
            status.Add(msg);
            card.Add(status);
            var buttons = E.Div().Row().Margin(12).Ignore();
            var rotate = E.IconButton("rotate", Act(store.RotatePlacement), 44);
            rotate.style.marginRight = 10;
            buttons.Add(rotate);
            buttons.Add(E.Button("Cancel", Act(store.CancelPlacement), E.Kind.Ghost).Grow());
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
            var title = E.Text(def.Name, 18, Theme.Ink, Theme.F.Display);
            title.style.marginTop = 2;
            names.Add(title);
            head.Add(names);
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
            if (def.HousingValue > 0) stats.Add(StatChip("home", $"Houses {def.HousingValue}"));
            if (def.StorageValue > 0) stats.Add(StatChip("storage", $"+{(int)def.StorageValue} storage"));
            if (def.ProtectedFoodValue > 0) stats.Add(StatChip("food", $"Keeps {(int)def.ProtectedFoodValue} food"));
            if (stats.childCount > 0) card.Add(stats);

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
            if (def.JobSlots != null && def.JobSlots.Count > 0)
                info.Add(StatChip("road", G.HasRoadAccess(b) ? "On a road +10%" : "No road"));
            card.Add(info);
            var lv = G.NextLevel(b);
            if (lv != null && b.Status == BuildingStatus.Complete)
            {
                var up = E.Div().Col().Radius(18).Pad(12).BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.3f)).Margin(10);
                var r1 = E.Div().Row().Between().Ignore();
                r1.Add(E.Text($"Level {b.Level + 1}: +50% capacity", 14, Theme.Ink, Theme.F.Display));
                r1.Add(E.Mono(Fmt.Clock(G.EstimatedSeconds(lv.BuildSeconds)), 11.5f, Theme.Body));
                up.Add(r1);
                up.Add(CostChips(lv.Cost).Margin(8));
                bool can = G.State.Resources.Covers(lv.Cost);
                up.Add(E.Button(can ? "Level up" : "Not enough resources", can ? Act(() => store.UpgradeLevel(id)) : null, can ? E.Kind.Gold : E.Kind.Disabled, 42, can ? "level_up" : null).Margin(8));
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
            string icon = def.Id.Contains("church") ? "landmark" : def.Id.Contains("mill") ? "wheat" : def.Id.Contains("market") ? "storage"
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
