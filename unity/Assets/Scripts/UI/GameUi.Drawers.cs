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
    /// The Build strip along the bottom of the screen (category tabs in the header next to the plank, a
    /// sideways row of item cells with the name and cost under each, like the kit's slot rows) and the
    /// Research window (the technology tree in columns by the campfire level they need, the selected
    /// technology's card on the right).
    /// </summary>
    public partial class GameUi
    {
        VisualElement drawer;
        string pendingTab;
        string selectedTech;

        /// <summary>Opens the build strip on a category next time (used by the capture tour).</summary>
        public void SelectTab(string tab) => pendingTab = tab;

        /// <summary>Height of the build strip: plank, a row of item cells with their names and costs.</summary>
        const float BuildStripH = 204;

        void BuildDrawer()
        {
            drawer = E.Panel().Abs().Pad(0, 10, 10, 10);
            drawer.name = "build-strip";
            drawer.style.height = BuildStripH;
            drawer.Transition(220, "translate", "opacity");
            AddRegion(drawer, () => store.ActivePanel == Panel.Build ? $"build|{DrawerKey()}|{SafeWidth}|{Portrait}" : "", BuildDrawerBuild);
            sheetLayer.Add(drawer);
        }

        /// <summary>Shows the build strip for the Build panel and places it (every tick).</summary>
        void UpdateDrawer()
        {
            bool d = store.ActivePanel == Panel.Build;
            drawer.Hidden(!d);
            // The world stays bright around the strip; windows dim it a little more.
            var c = d ? new Color(0, 0, 0, 0.08f) : Theme.Scrim;
            if (scrim.style.backgroundColor != c) scrim.style.backgroundColor = c;
            if (!d) return;
            if (Portrait)
            {
                // A bottom sheet over about the lower half, the back arrow under it; its items wrap in rows and scroll.
                float w = SheetW;
                float left = SafeLeft + (SafeWidth - w) / 2;
                Place(drawer, left, Mathf.Max(PopupTop, Mathf.Round(PanelH * 0.48f)), SafeLeft + SafeWidth + SafeRight - left - w, SheetBottom);
                if (drawer.style.height.keyword != StyleKeyword.Auto) drawer.style.height = StyleKeyword.Auto;
            }
            else
            {
                Place(drawer, SafeLeft + 8, null, SafeRight + 8, BottomPad + 4);
                if (!Mathf.Approximately(drawer.style.height.value.value, BuildStripH)) drawer.style.height = BuildStripH;
            }
        }

        string DrawerKey()
        {
            if (store.ActivePanel == Panel.Build)
                return buildTab + "|" + G.ActiveConstructionCount() + "|" + string.Join("", G.Content.Buildings.Where(b => b.Buildable).Select(d =>
                           $"{(G.State.Resources.Covers(d.Cost) ? 1 : 0)}{(G.IsUnlocked(d) ? 1 : 0)}{G.Count(d.Id, true)}")) + "|" +
                       string.Join("", G.Content.Decorations.Select(d => $"{(G.State.Resources.Covers(d.Cost) ? 1 : 0)}{(DecorationUnlocked(d) ? 1 : 0)}"));
            if (store.ActivePanel == Panel.Customs) return CustomsKey();
            return $"{selectedTech}|{G.State.ActiveResearch?.TechId}|{G.HeartLevel}|{WindowW}|" + string.Join("", G.Content.Technologies.Select(t =>
                       $"{(int)G.GetResearchStatus(t.Id).State}{(G.State.Resources.Covers(t.Cost) ? 1 : 0)}"));
        }

        bool DecorationUnlocked(DecorationDefinition d) => d.RequiresTech == null || G.State.Researched.Contains(d.RequiresTech);

        // MARK: - Build strip

        static readonly (string tab, string icon)[] BuildTabs =
        {
            ("All", "build"), ("Shelter", "home"), ("Food", "food"), ("Work", "pickaxe"), ("Storage", "storage"),
            ("Roads", "road"), ("Fences", "fence"), ("Lights", "lantern"), ("Nature", "tree"), ("Monuments", "landmark"),
        };

        void BuildDrawerBuild(VisualElement host)
        {
            if (store.ActivePanel != Panel.Build) return;
            if (Portrait) { BuildDrawerPortrait(host); return; }
            var head = E.Div().Row(0, Align.FlexEnd).Ignore();
            head.style.marginTop = -6;
            head.style.marginLeft = -14;
            head.style.flexShrink = 0;
            string title = buildTab == "All" ? "Build" : buildTab;
            const float plankW = 176;
            var plank = E.Plank(title, null, Theme.H1, 0);
            plank.style.width = plankW;
            plank.style.paddingLeft = 16; plank.style.paddingRight = 16;
            ((Label)plank.ElementAt(0)).Fit(plankW - 34, Theme.Body).style.whiteSpace = WhiteSpace.NoWrap;
            head.Add(plank);

            // Category tabs: icons, the selected one raised with its name.
            var decorCats = new HashSet<string>(G.Content.Decorations.Select(d => d.Category));
            var tabs = BuildTabStrip(decorCats);
            head.Add(tabs);
            var queue = E.Capsule(32).Pad(0, 10);
            queue.style.marginLeft = 8;
            queue.style.marginRight = Theme.CloseSize;
            queue.style.marginBottom = 2;
            queue.Add(E.Icon("build", 22));
            queue.Add(Live(() => $"{G.ActiveConstructionCount()}/{G.Content.Balance.MaxConcurrentConstructions}", Theme.Num).Margin(0, 0, 0, 4));
            head.Add(queue);
            host.Add(head);
            host.Add(E.Close(Act(() => store.ActivePanel = Panel.None)).Abs(null, -12, -12));

            var strip = E.Scroll(ScrollViewMode.Horizontal);
            strip.style.flexGrow = 1;
            strip.contentContainer.style.alignItems = Align.FlexStart;
            strip.contentContainer.style.paddingTop = 10;
            foreach (var card in BuildItems(decorCats)) strip.Add(card);
            host.Add(strip);
        }

        /// <summary>
        /// Portrait: the kit's bottom sheet: the plank across the top with the close button, the category tabs and the
        /// queue under it, then the items in rows of three (wrapping, scrolling), like the kit's furniture grid.
        /// </summary>
        void BuildDrawerPortrait(VisualElement host)
        {
            string title = buildTab == "All" ? "Build" : buildTab;
            float w = SheetW;
            var plank = E.Plank(title, null, Theme.H1, w + 8);
            plank.style.marginTop = -6;
            plank.style.marginLeft = -14;
            plank.style.marginRight = -14;
            host.Add(plank);
            host.Add(E.Close(Act(() => store.ActivePanel = Panel.None)).Abs(null, -12, -12));

            var head = E.Div().Row(0, Align.FlexEnd).Ignore();
            head.style.marginTop = 6;
            head.style.flexShrink = 0;
            var decorCats = new HashSet<string>(G.Content.Decorations.Select(d => d.Category));
            var tabs = BuildTabStrip(decorCats);
            tabs.style.marginLeft = 0;
            head.Add(tabs);
            var queue = E.Capsule(32).Pad(0, 10);
            queue.style.marginLeft = 6;
            queue.style.marginBottom = 2;
            queue.Add(E.Icon("build", 22));
            queue.Add(Live(() => $"{G.ActiveConstructionCount()}/{G.Content.Balance.MaxConcurrentConstructions}", Theme.Num).Margin(0, 0, 0, 4));
            head.Add(queue);
            host.Add(head);

            var scroll = E.Scroll(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            var grid = E.Div().Row(0, Align.FlexStart).Ignore();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.paddingTop = 10;
            grid.style.paddingBottom = 4;
            // As many items per row as fit; the spare width is shared out as equal margins so the columns line up.
            float rowW = w - 20;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(rowW / ItemW));
            float side = Mathf.Floor((rowW - perRow * ItemW) / (2 * perRow));
            foreach (var card in BuildItems(decorCats))
            {
                card.style.marginLeft = side;
                card.style.marginRight = side;
                card.style.marginBottom = 10;
                grid.Add(card);
            }
            scroll.Add(grid);
            host.Add(scroll);
        }

        /// <summary>Category tabs: icons, the selected one raised with its name (scrolls sideways).</summary>
        ScrollView BuildTabStrip(HashSet<string> decorCats)
        {
            var tabs = E.Scroll(ScrollViewMode.Horizontal);
            tabs.style.flexGrow = 1;
            tabs.style.flexShrink = 1;
            tabs.style.minWidth = 0;
            tabs.style.marginLeft = 8;
            tabs.contentContainer.style.alignItems = Align.FlexEnd;
            foreach (var (tab, icon) in BuildTabs)
            {
                if (tab != "All" && !decorCats.Contains(tab) && !G.Content.Buildings.Any(b => b.Buildable && Category(b) == tab)) continue;
                bool on = tab == buildTab;
                string name = tab;
                var t = E.Tab(on ? tab : null, on, Act(() => buildTab = name), null, Theme.TabH, Theme.Num, icon);
                t.style.minWidth = 44;
                t.style.marginRight = 3;
                tabs.Add(t);
            }
            return tabs;
        }

        /// <summary>The items of the current category: buildings (available first), decorations, and the remove tool.</summary>
        List<VisualElement> BuildItems(HashSet<string> decorCats)
        {
            var list = new List<VisualElement>();
            bool decorTab = decorCats.Contains(buildTab);
            if (!decorTab)
            {
                var defs = G.Content.Buildings.Where(b => b.Buildable && (buildTab == "All" || Category(b) == buildTab))
                    .OrderBy(b => G.IsUnlocked(b) ? 0 : 1).ToList();
                foreach (var def in defs) list.Add(BuildCard(def));
            }
            if (decorTab || buildTab == "All")
                foreach (var d in G.Content.Decorations.Where(x => buildTab == "All" ? x.IsRoad : x.Category == buildTab))
                    list.Add(DecorCard(d));
            if (decorTab)
            {
                var remove = ItemCard("trash", E.Cell.Common, "Remove", false, null);
                remove.Add(E.T("Half back", Theme.Small, Theme.Pale).Margin(2));
                Tap(remove, () => store.BeginDecorating(GameStore.RemoveTool));
                list.Add(remove);
            }
            return list;
        }

        const float ItemW = 104, ItemCell = 78;

        /// <summary>An item of the strip: rarity cell with the icon (badge: how many stand), the name in two lines.</summary>
        static VisualElement ItemCard(string icon, E.Cell cell, string name, bool locked, string count)
        {
            var c = E.Div().Col(Align.Center).Size(ItemW);
            c.style.marginRight = 6;
            c.pickingMode = PickingMode.Position;
            var slot = E.Slot(icon, ItemCell, locked ? E.Cell.Common : cell, 0.66f, locked);
            if (count != null) slot.Add(E.Badge(count, false, 22).Abs(-5, null, null, -3));
            if (locked) slot.Add(E.Icon("lock", 26).Abs(null, null, -5, -4));
            c.Add(slot);
            c.Add(E.T(name, Theme.Body).TwoLines(ItemW - 2).Margin(3));
            return c;
        }

        VisualElement BuildCard(BuildingDefinition def)
        {
            bool unlocked = G.IsUnlocked(def);
            int count = G.Count(def.Id, true);
            bool limit = def.MaxCount.HasValue && count >= def.MaxCount.Value;
            var card = ItemCard(BuildingIcon(def), CellFor(def), def.Name, !unlocked, count > 0 ? (def.MaxCount.HasValue ? $"{count}/{def.MaxCount}" : $"×{count}") : null);
            if (!unlocked)
                card.Add(E.T(G.Content.Technology(def.RequiresTech ?? "")?.Name ?? "Research", Theme.Small, Theme.Lilac).Fit(ItemW - 2).Align(TextAnchor.MiddleCenter));
            else if (limit) card.Add(E.T("Limit reached", Theme.Small, Theme.Pale).Fit(ItemW - 2).Align(TextAnchor.MiddleCenter));
            else
            {
                card.Add(CostLine(def.Cost, 13, true).Also(r => r.style.maxWidth = ItemW));
            }
            string id = def.Id;
            Tap(card, () =>
            {
                if (unlocked && !limit) store.BeginPlacement(id);
                else if (!unlocked) store.ActivePanel = Panel.Research;
            });
            return card;
        }

        VisualElement DecorCard(DecorationDefinition d)
        {
            bool unlocked = DecorationUnlocked(d);
            var card = ItemCard(DecorIconFor(d), E.Cell.Epic, d.Name, !unlocked, null);
            if (!unlocked)
                card.Add(E.T(G.Content.Technology(d.RequiresTech)?.Name ?? "Research", Theme.Small, Theme.Lilac).Fit(ItemW - 2).Align(TextAnchor.MiddleCenter));
            else
            {
                card.Add(CostLine(d.Cost, 13, true).Also(r => r.style.maxWidth = ItemW));
                string id = d.Id;
                Tap(card, () => store.BeginDecorating(id));
            }
            return card;
        }

        // MARK: - Research window

        /// <summary>The campfire level band a technology belongs to (columns of the tree).</summary>
        static int Tier(TechnologyDefinition t) => Math.Max(0, ((t.HeartLevel ?? 1) - 1) / 5);

        const float TechColW = 162, TechDetailW = 240;

        void BuildResearch(VisualElement host)
        {
            var active = G.State.ActiveResearch;
            var techs = G.Content.Technologies;
            if (selectedTech == null || G.Content.Technology(selectedTech) == null)
                selectedTech = active?.TechId ?? techs.FirstOrDefault(t => G.GetResearchStatus(t.Id).State == ResearchState.Available)?.Id ?? techs.First().Id;
            if (Portrait) { BuildResearchPortrait(host); return; }

            var body = E.Div().Row(0, Align.Stretch).Ignore();
            body.style.flexGrow = 1;
            body.style.minHeight = 0;

            // The tree: one column per band of campfire levels; scrolls both ways.
            var tree = E.Scroll(ScrollViewMode.VerticalAndHorizontal);
            tree.style.flexGrow = 1;
            tree.style.flexShrink = 1;
            tree.style.minWidth = 0;
            tree.contentContainer.style.flexDirection = FlexDirection.Row;
            tree.contentContainer.style.alignItems = Align.FlexStart;
            tree.contentContainer.style.paddingBottom = 6;
            // Whole columns only: the tree's width is shared out so the last one in view ends at its edge (the rest
            // scroll in from the right) instead of a column cut in half under the detail card.
            float treeW = InnerW - TechDetailW - 8;
            int cols = Mathf.Max(1, Mathf.FloorToInt((treeW + 8) / (TechColW + 8)));
            float colW = Mathf.Floor((treeW + 8) / cols) - 8;
            foreach (var group in techs.GroupBy(Tier).OrderBy(g => g.Key))
            {
                int from = group.Key * 5 + 1, to = from + 4;
                bool reached = G.HeartLevel >= from;
                var col = E.Div().Col().Ignore().Size(colW);
                col.style.marginRight = 8;
                var head = E.Div().Row().Center().Ignore();
                head.style.height = 28;
                head.Add(E.Icon(reached ? "flame" : "lock", 22));
                head.Add(E.T(group.Key >= 6 ? $"Level {from}+" : $"Level {from}–{to}", Theme.Num, reached ? Color.white : Theme.Pale).Margin(0, 0, 0, 5));
                col.Add(head);
                foreach (var t in group) col.Add(TechNode(t, colW));
                tree.Add(col);
            }
            body.Add(tree);

            var side = E.Scroll(ScrollViewMode.Vertical);
            side.style.width = TechDetailW;
            side.style.flexShrink = 0;
            side.style.marginLeft = 8;
            side.contentContainer.style.paddingBottom = 6;
            if (active != null) side.Add(ActiveResearchBar(active, TechDetailW));
            else if (econ.Production.Knowledge <= 0)
                side.Add(Notice("people", "Nobody studies yet: put people on Research (People › Work).", true).Margin(0, 0, 6));
            side.Add(TechDetail(G.Content.Technology(selectedTech), TechDetailW));
            body.Add(side);
            host.Add(body);
        }

        /// <summary>The running research: its name, a bar and the time left (tap: select it).</summary>
        VisualElement ActiveResearchBar(ActiveResearch active, float width)
        {
            var tech = G.Content.Technology(active.TechId);
            var bar = E.Capsule(46).Pad(4, 12, 4, 8).Margin(0, 0, 8);
            bar.style.height = StyleKeyword.Auto;
            bar.style.minHeight = 46;
            bar.Add(E.Icon("clock", 28));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            col.Add(E.T(tech?.Name ?? active.TechId, Theme.Body).Fit(width - 20 - 36 - 8));
            var r = E.Div().Row().Ignore().Margin(2);
            r.Add(LiveBar(() => G.GetResearchStatus(active.TechId).Progress, 10, E.FillKind.Blue).Grow());
            r.Add(Live(() => Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0), Theme.Small).Margin(0, 0, 0, 6));
            col.Add(r);
            bar.Add(col);
            string id = active.TechId;
            Tap(bar, () => selectedTech = id);
            return bar;
        }

        /// <summary>
        /// Portrait: one scrolling column: the running research and the chosen technology's card on top (a tap on a
        /// technology below brings it up there), then the tree band by band, two technologies per row.
        /// </summary>
        void BuildResearchPortrait(VisualElement host)
        {
            var active = G.State.ActiveResearch;
            var scroll = E.Scroll(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            scroll.contentContainer.style.paddingBottom = 6;
            if (active != null) scroll.Add(ActiveResearchBar(active, InnerW));
            else if (econ.Production.Knowledge <= 0)
                scroll.Add(Notice("people", "Nobody studies yet: put people on Research (People › Work).", true).Margin(0, 0, 6));
            scroll.Add(TechDetail(G.Content.Technology(selectedTech), InnerW).Margin(0, 0, 8));
            int perRow = InnerW >= 2 * TechColW + 8 ? Mathf.FloorToInt((InnerW + 8) / (TechColW + 8)) : 2;
            float nodeW = Mathf.Floor((InnerW - 8 * (perRow - 1)) / perRow);
            foreach (var group in G.Content.Technologies.GroupBy(Tier).OrderBy(g => g.Key))
            {
                int from = group.Key * 5 + 1, to = from + 4;
                bool reached = G.HeartLevel >= from;
                var head = E.Div().Row().Center().Ignore();
                head.style.height = 30;
                head.Add(E.Icon(reached ? "flame" : "lock", 22));
                head.Add(E.T(group.Key >= 6 ? $"Level {from}+" : $"Level {from}–{to}", Theme.Num, reached ? Color.white : Theme.Pale).Margin(0, 0, 0, 5));
                scroll.Add(head);
                var grid = E.Div().Row(0, Align.FlexStart).Ignore();
                grid.style.flexWrap = Wrap.Wrap;
                int i = 0;
                foreach (var t in group)
                {
                    var node = TechNode(t, nodeW);
                    node.style.width = nodeW;
                    if (i++ % perRow != 0) node.style.marginLeft = 8;
                    grid.Add(node);
                }
                scroll.Add(grid);
            }
            host.Add(scroll);
        }

        static (E.Cell cell, string icon) TechLook(ResearchState s) => s switch
        {
            ResearchState.Researched => (E.Cell.Uncommon, "check"),
            ResearchState.InProgress => (E.Cell.Rare, "clock"),
            ResearchState.Available => (E.Cell.Legendary, "knowledge"),
            _ => (E.Cell.Common, "lock"),
        };

        VisualElement TechNode(TechnologyDefinition t, float width)
        {
            var st = G.GetResearchStatus(t.Id);
            bool sel = t.Id == selectedTech;
            var (cell, icon) = TechLook(st.State);
            var n = E.Div().Row().Skin(sel ? "slot" : "section").Pad(0, 6, sel ? 3 : 0, 6).Margin(0, 0, 8);
            n.style.minHeight = 56;
            n.Add(E.Slot(icon, 40, cell, 0.62f));
            var name = E.T(t.Name, Theme.Body, st.State == ResearchState.Blocked ? Theme.Pale : (Color?)null).TwoLines(width - 12 - 40 - 6);
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.marginLeft = 6;
            n.Add(name);
            if (st.State == ResearchState.InProgress)
                n.Add(LiveBar(() => G.GetResearchStatus(t.Id).Progress, 7, E.FillKind.Blue).Abs(8, null, 8, 5));
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

        VisualElement TechDetail(TechnologyDefinition t, float detailW)
        {
            var card = E.Inset().Col().Pad(10, 10);
            if (t == null) return card;
            var st = G.GetResearchStatus(t.Id);
            var (cell, icon) = TechLook(st.State);
            var head = E.Div().Row().Ignore();
            head.Add(E.Slot(st.State == ResearchState.Blocked ? "lock" : "knowledge", 52, cell, 0.62f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            col.Add(E.T(t.Name, Theme.H2).TwoLines(detailW - 20 - 52 - 8, Theme.Body).Also(l => l.style.unityTextAlign = TextAnchor.MiddleLeft));
            var time = E.Div().Row().Ignore();
            time.Add(E.Icon("clock", 20));
            time.Add(E.T(Fmt.Clock(t.ResearchSeconds), Theme.Num).Margin(0, 0, 0, 4));
            col.Add(time);
            head.Add(col);
            card.Add(head);
            card.Add(E.Ink(t.Description, Theme.Body).Margin(6));
            string fx = EffectsText(t);
            if (fx.Length > 0) card.Add(E.T(fx, Theme.Body, Theme.Up).Wrap().Margin(6));

            var bottom = E.Div().Col().Ignore().Margin(8);
            switch (st.State)
            {
                case ResearchState.Researched:
                    bottom.Add(E.Div().Row().Ignore().Kids(E.Icon("check", 28), E.T("Discovered", Theme.H2, Theme.Up).Margin(0, 0, 0, 6)));
                    break;
                case ResearchState.InProgress:
                    bottom.Add(LiveLabeledBar(() => G.GetResearchStatus(t.Id).Progress, () => Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0), 20, E.FillKind.Blue));
                    break;
                case ResearchState.Blocked:
                    foreach (var m in st.Missing)
                    {
                        var r = E.Div().Row(0, Align.FlexStart).Ignore().Margin(4);
                        r.Add(E.Icon("lock", 20));
                        r.Add(E.T(m, Theme.Body, Theme.Pale).Flex().Margin(0, 0, 0, 6));
                        bottom.Add(r);
                    }
                    break;
                default:
                {
                    bool sep = false;
                    CostRows(bottom, t.Cost, ref sep);
                    bool busy = G.State.ActiveResearch != null;
                    bool can = !busy && G.State.Resources.Covers(t.Cost);
                    string id = t.Id;
                    var b = E.Button(busy ? "One at a time" : "Research", can ? Act(() => store.StartResearch(id)) : null,
                                     can ? E.Kind.Green : E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("clock", Fmt.Clock(t.ResearchSeconds)), detailW - 20);
                    b.style.marginTop = 8;
                    bottom.Add(b);
                    break;
                }
            }
            card.Add(bottom);
            return card;
        }
    }
}
