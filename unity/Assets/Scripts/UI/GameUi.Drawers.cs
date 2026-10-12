using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Build and Research as wooden drawers rising from the bottom of the screen (mobile survival
    /// builder style) instead of side sheets. In landscape, Build is a low drawer (icon tabs in the
    /// header over a sideways strip of building cards) so the settlement stays in view; Research is a
    /// full-height board with the technology tree in columns by the campfire level they need on the
    /// left and the selected technology's card on the right.
    /// </summary>
    public partial class GameUi
    {
        static readonly Color Parch = Theme.Hex("#F4E5C2");
        static readonly Color ParchDeep = Theme.Hex("#E4C893");
        static readonly Color InkBrown = Theme.Hex("#3B230F");
        static readonly Color InkSoft = Theme.Hex("#7A5530");
        static readonly Color Leaf = Theme.Hex("#4E8C33");
        static readonly Color Ruby = Theme.Hex("#C2412F");
        static readonly Color Plum = Theme.Hex("#6D4FA8");

        VisualElement drawer, drawerInner, scrim;
        string pendingTab;

        /// <summary>Opens the build drawer on a category next time (used by the capture tour).</summary>
        public void SelectTab(string tab) => pendingTab = tab;
        string selectedTech;

        bool DrawerPanel => store != null && (store.ActivePanel == Panel.Build || store.ActivePanel == Panel.Research || store.ActivePanel == Panel.Customs);

        void BuildDrawer()
        {
            drawer = E.Div("drawer").Abs(0, null, 0, 0).Col().Radius(24, 0);
            drawer.BgImage(Theme.Gradient("drawerwood", true, WoodLight, WoodDark));
            drawer.style.borderTopWidth = 3;
            drawer.style.borderTopColor = Theme.Hex("#F6D3A0");
            drawer.style.paddingTop = 6;
            drawer.style.paddingLeft = 6;
            drawer.style.paddingRight = 6;
            drawer.Transition(260, "translate", "opacity");
            drawerInner = E.Div("drawer-inner").Col().Grow().Radius(18).Ignore();
            drawerInner.BgImage(Theme.Gradient("parch", true, Parch, ParchDeep));
            drawerInner.style.borderTopWidth = drawerInner.style.borderLeftWidth = drawerInner.style.borderRightWidth = 2;
            drawerInner.style.borderTopColor = drawerInner.style.borderLeftColor = drawerInner.style.borderRightColor = WoodEdge;
            drawerInner.style.overflow = Overflow.Hidden;
            drawer.Add(drawerInner);
            AddRegion(drawerInner, () => DrawerPanel ? $"{store.ActivePanel}|{DrawerKey()}" : "", BuildDrawerContent);
            sheetLayer.Add(drawer);
        }

        /// <summary>Shows the drawer or the sheet for the open panel (called every tick).</summary>
        void UpdateDrawer()
        {
            bool d = DrawerPanel;
            sheet.Hidden(d);
            drawer.Hidden(!d);
            scrim.style.backgroundColor = d ? new Color(0, 0, 0, 0.12f) : new Color(0.03f, 0.035f, 0.05f, 0.5f);
            if (!d) return;
            // Build: header with the tabs (50) + the card strip (158) + padding. Research: the whole height.
            drawer.style.height = store.ActivePanel == Panel.Build ? BuildDrawerHeight + SafeBottom : PanelH - SafeTop - 8;
            drawer.style.paddingBottom = SafeBottom;
            drawer.style.paddingLeft = 6 + SafeLeft;
            drawer.style.paddingRight = 6 + SafeRight;
        }

        const float BuildDrawerHeight = 222;

        string DrawerKey()
        {
            if (store.ActivePanel == Panel.Build)
                return buildTab + "|" + G.ActiveConstructionCount() + "|" + string.Join("", G.Content.Buildings.Where(b => b.Buildable).Select(d =>
                           $"{(G.State.Resources.Covers(d.Cost) ? 1 : 0)}{(G.IsUnlocked(d) ? 1 : 0)}{G.Count(d.Id, true)}")) + "|" +
                       string.Join("", G.Content.Decorations.Select(d => $"{(G.State.Resources.Covers(d.Cost) ? 1 : 0)}{(DecorationUnlocked(d) ? 1 : 0)}"));
            if (store.ActivePanel == Panel.Customs) return CustomsKey();
            return $"{selectedTech}|{G.State.ActiveResearch?.TechId}|{G.HeartLevel}|" + string.Join("", G.Content.Technologies.Select(t =>
                       $"{(int)G.GetResearchStatus(t.Id).State}{(G.State.Resources.Covers(t.Cost) ? 1 : 0)}"));
        }

        void BuildDrawerContent(VisualElement host)
        {
            if (!DrawerPanel) return;
            if (store.ActivePanel == Panel.Build) BuildDrawerBuild(host);
            else if (store.ActivePanel == Panel.Customs) BuildDrawerCustoms(host);
            else BuildDrawerResearch(host);
        }

        // MARK: - Shared bits

        static Label Ink(string text, float size, Color? color = null, Theme.F font = Theme.F.TextBold) =>
            E.Text(text, size, color ?? InkBrown, font);

        /// <summary>Badge, title (and a live subtitle), an optional middle part (the build tabs) and the close button.</summary>
        VisualElement DrawerHeader(string icon, string title, Func<string> sub, VisualElement extra = null, VisualElement middle = null)
        {
            var row = E.Div().Row().Pad(middle != null ? 2 : 8, 8, 2, 12).Ignore();
            row.style.flexShrink = 0;
            var badge = E.Div().Size(30, 30).Radius(9).Center().BgImage(Theme.Gradient("goldround", true, GoldTop, GoldBottom)).Border(GoldEdge, 1.5f).Ignore();
            badge.Add(E.Icon(icon, 17, Theme.Hex("#5A3208")));
            row.Add(badge);
            var col = E.Div().Col().Ignore();
            if (middle == null) col.Grow(); else col.NoShrink();
            col.style.marginLeft = 9;
            col.Add(Ink(title, 16, InkBrown, Theme.F.DisplayBold).NoWrap());
            if (sub != null)
            {
                var s = Ink("", 10.5f, InkSoft, Theme.F.TextMedium).NoWrap();
                Bind(() => s.text = sub());
                col.Add(s);
            }
            row.Add(col);
            if (middle != null)
            {
                middle.style.marginLeft = 10;
                middle.style.marginRight = 6;
                row.Add(middle);
            }
            if (extra != null) row.Add(extra.NoShrink());
            var close = E.Div().Size(32, 32).Radius(16).Center().Bg(Theme.Alpha(InkBrown, 0.12f));
            close.style.marginLeft = 8;
            close.style.flexShrink = 0;
            close.Add(E.Icon("close", 15, InkBrown));
            Tap(close, () => store.ActivePanel = Panel.None);
            row.Add(close);
            return row;
        }

        /// <summary>A cost as a few small icon + number pairs, red where the stores fall short.</summary>
        VisualElement CostLine(ResourceBundle cost, float size = 11f)
        {
            var row = E.Div().Row().Ignore();
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.Center;
            if (!cost.Entries.Any())
            {
                row.Add(Ink("Free", size, Leaf));
                return row;
            }
            foreach (var kv in cost.Entries)
            {
                bool shortOf = G.State.Resources[kv.Key] + 1e-6 < kv.Value;
                var item = E.Div().Row().Ignore();
                item.style.marginLeft = 3;
                item.style.marginRight = 3;
                item.Add(E.Icon(IconFor(kv.Key), size + 1, shortOf ? Ruby : Color.Lerp(TintFor(kv.Key), InkBrown, 0.45f)));
                var t = Ink(Fmt.Amount(kv.Value), size, shortOf ? Ruby : InkBrown, Theme.F.Mono);
                t.style.marginLeft = 2;
                item.Add(t);
                row.Add(item);
            }
            return row;
        }

        static ScrollView Sideways()
        {
            var s = new ScrollView(ScrollViewMode.Horizontal);
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            s.mode = ScrollViewMode.Horizontal;
            s.contentContainer.style.flexDirection = FlexDirection.Row;
            s.contentContainer.style.paddingLeft = 8;
            s.contentContainer.style.paddingRight = 8;
            return s;
        }

        // MARK: - Build drawer

        static readonly (string tab, string icon)[] BuildTabs =
        {
            ("All", "build"), ("Shelter", "home"), ("Food", "food"), ("Work", "pickaxe"), ("Storage", "storage"),
            ("Roads", "road"), ("Fences", "fence"), ("Lights", "lantern"), ("Nature", "tree"), ("Monuments", "landmark"),
        };

        void BuildDrawerBuild(VisualElement host)
        {
            var queue = E.Div().Row().Radius(999).Pad(4, 9).Bg(Theme.Alpha(InkBrown, 0.1f)).Ignore();
            queue.Add(E.Icon("build", 12, InkSoft));
            var qt = Ink("", 11, InkBrown, Theme.F.Mono);
            qt.style.marginLeft = 4;
            Bind(() => qt.text = $"{G.ActiveConstructionCount()}/{G.Content.Balance.MaxConcurrentConstructions}");
            queue.Add(qt);
            // Category tabs: round wooden icons, in the header row (the landscape screen is short).
            var tabs = Sideways();
            tabs.style.height = 46;
            tabs.style.flexGrow = 1;
            tabs.style.flexShrink = 1;
            tabs.style.minWidth = 0;
            tabs.contentContainer.style.paddingLeft = 0;
            var decorCats = new HashSet<string>(G.Content.Decorations.Select(d => d.Category));
            foreach (var (tab, icon) in BuildTabs)
            {
                if (tab != "All" && !decorCats.Contains(tab) && !G.Content.Buildings.Any(b => b.Buildable && Category(b) == tab)) continue;
                bool on = tab == buildTab;
                var t = E.Div().Size(38, 38).Radius(19).Center();
                t.style.marginRight = 7;
                t.style.marginTop = 4;
                if (on) t.BgImage(Theme.Gradient("goldround", true, GoldTop, GoldBottom)).Border(GoldEdge, 2f);
                else t.Bg(Theme.Alpha(InkBrown, 0.1f)).Border(Theme.Alpha(InkBrown, 0.25f), 1.5f);
                t.Add(E.Icon(icon, 18, on ? Theme.Hex("#5A3208") : InkSoft));
                string name = tab;
                Tap(t, () => buildTab = name);
                tabs.Add(t);
            }
            host.Add(DrawerHeader("build", buildTab == "All" ? "Build" : buildTab, null, queue, tabs));

            var strip = Sideways();
            strip.style.flexGrow = 1;
            strip.contentContainer.style.alignItems = Align.Center;
            bool decorTab = decorCats.Contains(buildTab);
            if (!decorTab)
            {
                // Available first, then the ones still locked behind research.
                var defs = G.Content.Buildings.Where(b => b.Buildable && (buildTab == "All" || Category(b) == buildTab))
                    .OrderBy(b => G.IsUnlocked(b) ? 0 : 1).ToList();
                foreach (var def in defs) strip.Add(BuildCard(def));
            }
            if (decorTab || buildTab == "All")
                foreach (var d in G.Content.Decorations.Where(x => buildTab == "All" ? x.IsRoad : x.Category == buildTab))
                    strip.Add(DecorCard(d));
            if (decorTab)
            {
                var remove = CardShell(false);
                remove.Add(E.Div().Size(52, 52).Radius(16).Center().Bg(Theme.Alpha(Ruby, 0.15f)).Ignore().Kids(E.Icon("trash", 24, Ruby)));
                remove.Add(Ink("Remove", 12).Align(TextAnchor.MiddleCenter).Margin(6));
                remove.Add(Ink("Tap a road or ornament to clear it", 9.5f, InkSoft, Theme.F.TextMedium).Align(TextAnchor.MiddleCenter).Margin(3));
                Tap(remove, () => store.BeginDecorating(GameStore.RemoveTool));
                strip.Add(remove);
            }
            host.Add(strip);
        }

        static VisualElement CardShell(bool dim)
        {
            var c = E.Div().Col(Align.Center).Radius(16).Pad(8, 6);
            c.style.width = 106;
            c.style.height = 150;
            c.style.marginRight = 8;
            c.style.flexShrink = 0;
            c.BgImage(Theme.Gradient("cardparch", true, Theme.Hex("#FFF4DA"), Theme.Hex("#EED8A8")));
            c.Border(Theme.Alpha(Theme.Hex("#5E3A1E"), 0.55f), 2f);
            c.style.borderBottomWidth = 4;
            if (dim) c.style.opacity = 0.62f;
            return c;
        }

        VisualElement BuildCard(BuildingDefinition def)
        {
            bool unlocked = G.IsUnlocked(def);
            bool affordable = G.State.Resources.Covers(def.Cost);
            int count = G.Count(def.Id, true);
            bool limit = def.MaxCount.HasValue && count >= def.MaxCount.Value;
            var card = CardShell(!unlocked || limit);
            var tile = BuildingTile(def, 54);
            if (count > 0)
            {
                var tag = E.Div().Ignore().Radius(999).Pad(1, 5).Bg(InkBrown).Abs(null, -4, -6);
                tag.Add(E.Text(def.MaxCount.HasValue ? $"{count}/{def.MaxCount}" : $"×{count}", 9.5f, Parch, Theme.F.Mono));
                tile.Add(tag);
            }
            if (!unlocked) tile.Add(E.Div().Ignore().Size(22, 22).Radius(11).Center().Bg(InkBrown).Abs(null, null, -5, -5).Kids(E.Icon("lock", 12, Parch)));
            card.Add(tile);
            // Two lines at most: long names get a smaller type, and anything more is clipped, never spilled.
            float nameSize = def.Name.Length > 22 ? 9.5f : def.Name.Length > 15 ? 10.5f : 11.5f;
            var name = Ink(def.Name, nameSize).Align(TextAnchor.MiddleCenter);
            name.style.marginTop = 6;
            name.style.height = 30;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.overflow = Overflow.Hidden;
            card.Add(name);
            if (!unlocked)
            {
                var need = Ink(G.Content.Technology(def.RequiresTech ?? "")?.Name ?? "Research", 9.5f, Plum, Theme.F.TextMedium).Align(TextAnchor.MiddleCenter);
                need.style.whiteSpace = WhiteSpace.Normal;
                card.Add(need);
            }
            else if (limit) card.Add(Ink("Limit reached", 10, InkSoft, Theme.F.TextMedium));
            else
            {
                card.Add(CostLine(def.Cost, 10.5f));
                var time = E.Div().Row().Margin(4).Ignore();
                time.Add(E.Icon("clock", 10, InkSoft));
                var tt = Ink(Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds)), 9.5f, InkSoft, Theme.F.Mono);
                tt.style.marginLeft = 3;
                time.Add(tt);
                card.Add(time);
            }
            if (!affordable && unlocked && !limit) card.style.borderBottomColor = Ruby;
            string id = def.Id;
            Tap(card, () =>
            {
                if (unlocked && !limit) store.BeginPlacement(id);
            });
            return card;
        }

        VisualElement DecorCard(DecorationDefinition d)
        {
            bool unlocked = DecorationUnlocked(d);
            var card = CardShell(!unlocked);
            var tile = E.Div().Size(54, 54).Radius(16).Center().Ignore()
                .BgImage(Theme.Gradient("decortile", true, Theme.Hex("#8A6A3E"), Theme.Hex("#4C3418")));
            tile.Add(E.Icon(DecorIcon.TryGetValue(d.Category ?? "", out var ic) ? (d.IsBridge ? "road" : ic) : "sparkle", 26, Theme.GoldPale));
            if (!unlocked) tile.Add(E.Div().Ignore().Size(22, 22).Radius(11).Center().Bg(InkBrown).Abs(null, null, -5, -5).Kids(E.Icon("lock", 12, Parch)));
            card.Add(tile);
            var name = Ink(d.Name, 11.5f).Align(TextAnchor.MiddleCenter);
            name.style.marginTop = 6;
            name.style.height = 30;
            name.style.whiteSpace = WhiteSpace.Normal;
            card.Add(name);
            if (!unlocked)
                card.Add(Ink(G.Content.Technology(d.RequiresTech)?.Name ?? "Research", 9.5f, Plum, Theme.F.TextMedium).Align(TextAnchor.MiddleCenter));
            else
            {
                card.Add(CostLine(d.Cost, 10.5f));
                string what = d.Heat != null ? $"Warmth {SignedDeg(d.Heat.Heat)}" : d.IsBridge ? "Spans the river" : d.Beauty > 0 ? $"Beauty +{d.Beauty}" : d.IsRoad ? "Faster walking" : d.Category;
                card.Add(Ink(what, 9.5f, InkSoft, Theme.F.TextMedium).Margin(4));
                string id = d.Id;
                Tap(card, () => store.BeginDecorating(id));
            }
            return card;
        }

        // MARK: - Research drawer

        /// <summary>The campfire level band a technology belongs to (columns of the tree).</summary>
        static int Tier(TechnologyDefinition t) => Math.Max(0, ((t.HeartLevel ?? 1) - 1) / 5);

        void BuildDrawerResearch(VisualElement host)
        {
            var know = E.Div().Row().Radius(999).Pad(4, 9).Bg(Theme.Alpha(Plum, 0.14f)).Ignore();
            know.Add(E.Icon("knowledge", 13, Plum));
            var kv = Ink("", 11.5f, Plum, Theme.F.Mono);
            kv.style.marginLeft = 4;
            Bind(() => kv.text = $"{Fmt.Amount(G.State.Resources.Knowledge)}  {Fmt.PerMinute(econ.Net.Knowledge)}");
            know.Add(kv);
            host.Add(DrawerHeader("knowledge", "Research", () =>
            {
                int done = G.State.Researched.Count;
                return $"{done}/{G.Content.Technologies.Count} discovered · {G.HeartName} level {G.HeartLevel}";
            }, know));

            // Two panes: the tree (scrolls both ways) and, on the right, what is being studied and the selected card.
            var active = G.State.ActiveResearch;
            var techs = G.Content.Technologies;
            if (selectedTech == null || G.Content.Technology(selectedTech) == null)
                selectedTech = active?.TechId ?? techs.FirstOrDefault(t => G.GetResearchStatus(t.Id).State == ResearchState.Available)?.Id ?? techs.First().Id;

            var body = E.Div().Row(0, Align.Stretch).Ignore();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.style.minHeight = 0;

            // The tree: one column per band of campfire levels.
            var tree = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            tree.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            tree.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            tree.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            tree.style.flexGrow = 1;
            tree.style.flexShrink = 1;
            tree.style.minWidth = 0;
            tree.contentContainer.style.flexDirection = FlexDirection.Row;
            tree.contentContainer.style.alignItems = Align.FlexStart;
            tree.contentContainer.style.paddingLeft = 8;
            tree.contentContainer.style.paddingRight = 8;
            tree.contentContainer.style.paddingTop = 6;
            tree.contentContainer.style.paddingBottom = 8;
            foreach (var group in techs.GroupBy(Tier).OrderBy(g => g.Key))
            {
                int from = group.Key * 5 + 1, to = from + 4;
                bool reached = G.HeartLevel >= from;
                var col = E.Div().Col().Ignore();
                col.style.width = 138;
                col.style.flexShrink = 0;
                col.style.marginRight = 6;
                var head = E.Div().Row().Center().Radius(8).Pad(3, 6).Margin(0, 0, 5).Bg(reached ? Theme.Alpha(Leaf, 0.16f) : Theme.Alpha(InkBrown, 0.08f)).Ignore();
                head.Add(E.Icon(reached ? "flame" : "lock", 11, reached ? Leaf : InkSoft));
                var hl = Ink(group.Key >= 6 ? $"Level {from}+" : $"Level {from}–{to}", 10, reached ? Leaf : InkSoft, Theme.F.TextBold);
                hl.style.marginLeft = 4;
                head.Add(hl);
                col.Add(head);
                foreach (var t in group) col.Add(TechNode(t));
                tree.Add(col);
            }
            body.Add(tree);

            var side = new ScrollView(ScrollViewMode.Vertical);
            side.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            side.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            side.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            side.style.width = 290;
            side.style.flexShrink = 0;
            side.contentContainer.style.paddingRight = 8;
            side.contentContainer.style.paddingTop = 6;
            side.contentContainer.style.paddingBottom = 8;
            if (active != null)
            {
                var tech = G.Content.Technology(active.TechId);
                var bar = E.Div().Row().Radius(12).Pad(6, 10).Margin(0, 0, 6).Bg(Theme.Alpha(Plum, 0.12f)).Border(Theme.Alpha(Plum, 0.3f), 1.5f);
                bar.Add(E.Icon("clock", 13, Plum));
                var n = Ink(tech?.Name ?? active.TechId, 11.5f, InkBrown).NoWrap();
                n.style.marginLeft = 6;
                n.style.width = 96;
                n.style.flexShrink = 0;
                bar.Add(n);
                var fillBar = LiveBar(() => G.GetResearchStatus(active.TechId).Progress, 7, Theme.LavenderFill).Grow();
                fillBar.style.marginLeft = 8;
                fillBar.style.marginRight = 8;
                bar.Add(fillBar);
                bar.Add(Live(() => Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0), 10.5f, Plum, Theme.F.Mono));
                string id = active.TechId;
                Tap(bar, () => selectedTech = id);
                side.Add(bar);
            }
            else if (econ.Production.Knowledge <= 0)
            {
                var hint = Ink("Nobody studies yet: put people on Research (People).", 10.5f, Ruby, Theme.F.TextMedium).Margin(0, 4, 6, 4);
                hint.style.whiteSpace = WhiteSpace.Normal;
                side.Add(hint);
            }
            side.Add(TechDetail(G.Content.Technology(selectedTech)));
            body.Add(side);
            host.Add(body);
        }

        VisualElement TechNode(TechnologyDefinition t)
        {
            var st = G.GetResearchStatus(t.Id);
            bool sel = t.Id == selectedTech;
            Color fill, edge, icoBg; string icon;
            switch (st.State)
            {
                case ResearchState.Researched: fill = Theme.Hex("#DDEBC4"); edge = Leaf; icoBg = Leaf; icon = "check"; break;
                case ResearchState.InProgress: fill = Theme.Hex("#E6DCF7"); edge = Plum; icoBg = Plum; icon = "clock"; break;
                case ResearchState.Available: fill = Theme.Hex("#FFF3CF"); edge = GoldBottom; icoBg = GoldBottom; icon = "knowledge"; break;
                default: fill = Theme.Hex("#E9D9B6"); edge = Theme.Alpha(InkBrown, 0.3f); icoBg = Theme.Alpha(InkBrown, 0.35f); icon = "lock"; break;
            }
            var n = E.Div().Row().Radius(11).Pad(5, 6).Margin(0, 0, 5).Bg(fill).Border(sel ? InkBrown : edge, sel ? 2.5f : 1.5f);
            var dot = E.Div().Size(22, 22).Radius(11).Center().Bg(icoBg).Ignore();
            dot.Add(E.Icon(icon, 12, Color.white));
            n.Add(dot);
            var name = Ink(t.Name, 10.5f, st.State == ResearchState.Blocked ? InkSoft : InkBrown).Grow();
            name.style.marginLeft = 6;
            name.style.whiteSpace = WhiteSpace.Normal;
            n.Add(name);
            if (st.State == ResearchState.InProgress)
            {
                var bar = LiveBar(() => G.GetResearchStatus(t.Id).Progress, 3, Theme.LavenderFill).Abs(6, null, 6, 2);
                n.Add(bar);
            }
            string id = t.Id;
            Tap(n, () => selectedTech = id);
            return n;
        }

        /// <summary>Plain-language summary of what a technology does.</summary>
        string EffectsText(TechnologyDefinition t)
        {
            var parts = new List<string>();
            foreach (var e in t.Effects)
            {
                double v = e.Value ?? 1;
                string pct = ((v - 1) * 100).ToString("+0;-0", CultureInfo.InvariantCulture) + "%";
                switch (e.Kind)
                {
                    case EffectKind.JobRateMultiplier: parts.Add($"{pct} {G.Content.JobConfig(e.Job ?? JobType.Foraging)?.Name ?? e.Job?.Id()}"); break;
                    case EffectKind.SpoilageMultiplier: parts.Add($"{pct} food spoilage"); break;
                    case EffectKind.StorageMultiplier: parts.Add($"{pct} {e.Resource?.DisplayName().ToLowerInvariant()} storage"); break;
                    case EffectKind.ConstructionSpeedMultiplier: parts.Add($"{pct} building speed"); break;
                    case EffectKind.GrowthMultiplier: parts.Add($"{pct} population growth"); break;
                    case EffectKind.ConsumptionMultiplier: parts.Add($"{pct} food eaten"); break;
                    case EffectKind.UnlockBuilding: parts.Add($"unlocks {G.Content.Building(e.BuildingId)?.Name}"); break;
                }
            }
            // Buildings and decorations that need this tech (those named by an unlock effect are listed already).
            var named = new HashSet<string>(t.Effects.Where(e => e.Kind == EffectKind.UnlockBuilding).Select(e => e.BuildingId));
            var unlocks = G.Content.Buildings.Where(b => b.RequiresTech == t.Id && !named.Contains(b.Id)).Select(b => b.Name)
                .Concat(G.Content.Decorations.Where(d => d.RequiresTech == t.Id).Select(d => d.Name)).ToList();
            if (unlocks.Count > 0) parts.Add("unlocks " + string.Join(", ", unlocks));
            return parts.Count == 0 ? "" : char.ToUpperInvariant(parts[0][0]) + string.Join(" · ", parts).Substring(1);
        }

        VisualElement TechDetail(TechnologyDefinition t)
        {
            var card = E.Div().Col().Radius(16).Pad(10, 12).Bg(Theme.Hex("#FFF6DE")).Border(Theme.Alpha(InkBrown, 0.35f), 1.5f);
            card.style.flexShrink = 0;
            if (t == null) return card;
            var st = G.GetResearchStatus(t.Id);
            var head = E.Div().Row().Ignore();
            var title = Ink(t.Name, 14.5f, InkBrown, Theme.F.DisplayBold).Grow();
            title.style.whiteSpace = WhiteSpace.Normal;
            head.Add(title);
            var time = E.Div().Row().Ignore().NoShrink();
            time.style.marginLeft = 6;
            time.Add(E.Icon("clock", 11, InkSoft));
            var tt = Ink(Fmt.Clock(t.ResearchSeconds), 10.5f, InkSoft, Theme.F.Mono);
            tt.style.marginLeft = 3;
            time.Add(tt);
            head.Add(time);
            card.Add(head);
            var desc = Ink(t.Description, 10.5f, InkSoft, Theme.F.TextMedium).Margin(3);
            desc.style.whiteSpace = WhiteSpace.Normal;
            card.Add(desc);
            string fx = EffectsText(t);
            if (fx.Length > 0)
            {
                var f = Ink(fx, 10.5f, Leaf, Theme.F.TextBold).Margin(4);
                f.style.whiteSpace = WhiteSpace.Normal;
                card.Add(f);
            }
            // Stacked (cost over a full-width button): the card is narrow in the landscape board.
            var bottom = E.Div().Col().Margin(8).Ignore();
            switch (st.State)
            {
                case ResearchState.Researched:
                    bottom.Add(Ink("Discovered", 12, Leaf));
                    break;
                case ResearchState.InProgress:
                    bottom.Add(Live(() => $"Studying · {Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0)} left", 11.5f, Plum, Theme.F.TextBold));
                    break;
                case ResearchState.Blocked:
                {
                    var need = Ink("Needs " + string.Join(", ", st.Missing), 10.5f, Ruby, Theme.F.TextBold).Grow();
                    need.style.whiteSpace = WhiteSpace.Normal;
                    bottom.Add(need);
                    break;
                }
                default:
                {
                    bottom.Add(CostLine(t.Cost, 12));
                    bool busy = G.State.ActiveResearch != null;
                    bool can = !busy && G.State.Resources.Covers(t.Cost);
                    string id = t.Id;
                    bottom.Add(E.Button(busy ? "One at a time" : can ? "Research" : "Need knowledge", can ? Act(() => store.StartResearch(id)) : null,
                                        can ? E.Kind.Gold : E.Kind.Disabled, 36, can ? "knowledge" : null, 12.5f).Pad(0, 14).Margin(8));
                    break;
                }
            }
            card.Add(bottom);
            return card;
        }
    }
}
