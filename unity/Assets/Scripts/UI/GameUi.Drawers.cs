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

        const float BuildStripH = 158;

        void BuildDrawer()
        {
            drawer = E.Panel().Abs().Pad(0, 8, 9, 8);
            drawer.name = "build-strip";
            drawer.style.height = BuildStripH;
            drawer.Transition(220, "translate", "opacity");
            AddRegion(drawer, () => store.ActivePanel == Panel.Build ? $"build|{DrawerKey()}|{SafeWidth}" : "", BuildDrawerBuild);
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
            Place(drawer, SafeLeft + 8, null, SafeRight + 8, BottomPad + 4);
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
            var head = E.Div().Row(0, Align.FlexEnd).Ignore();
            head.style.marginTop = -4;
            head.style.marginLeft = -12;
            head.style.flexShrink = 0;
            string title = buildTab == "All" ? "Build" : buildTab;
            var plank = E.Plank(title, null, 15, 150);
            plank.style.width = 150;
            head.Add(plank);

            // Category tabs: icons, the selected one raised with its name.
            var tabs = E.Scroll(ScrollViewMode.Horizontal);
            tabs.style.flexGrow = 1;
            tabs.style.flexShrink = 1;
            tabs.style.minWidth = 0;
            tabs.style.marginLeft = 6;
            tabs.contentContainer.style.alignItems = Align.FlexEnd;
            var decorCats = new HashSet<string>(G.Content.Decorations.Select(d => d.Category));
            foreach (var (tab, icon) in BuildTabs)
            {
                if (tab != "All" && !decorCats.Contains(tab) && !G.Content.Buildings.Any(b => b.Buildable && Category(b) == tab)) continue;
                bool on = tab == buildTab;
                string name = tab;
                var t = E.Tab(on ? tab : null, on, Act(() => buildTab = name), null, 26, 11.5f, icon);
                t.style.marginRight = 3;
                tabs.Add(t);
            }
            head.Add(tabs);
            var queue = E.Capsule(22).Pad(0, 7);
            queue.style.marginLeft = 6;
            queue.style.marginRight = 26;
            queue.style.marginBottom = 2;
            queue.Add(E.Icon("build", 16));
            queue.Add(Live(() => $"{G.ActiveConstructionCount()}/{G.Content.Balance.MaxConcurrentConstructions}", 11.5f).Margin(0, 0, 0, 3));
            head.Add(queue);
            host.Add(head);
            host.Add(E.Close(Act(() => store.ActivePanel = Panel.None)).Abs(null, -9, -10));

            var strip = E.Scroll(ScrollViewMode.Horizontal);
            strip.style.flexGrow = 1;
            strip.contentContainer.style.alignItems = Align.FlexStart;
            strip.contentContainer.style.paddingTop = 8;
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
                var remove = ItemCard("trash", E.Cell.Common, "Remove", false, null);
                remove.Add(E.T("Half back", 10, Theme.Pale).Margin(2));
                Tap(remove, () => store.BeginDecorating(GameStore.RemoveTool));
                strip.Add(remove);
            }
            host.Add(strip);
        }

        const float ItemW = 82;

        /// <summary>An item of the strip: rarity cell with the icon (badge: how many stand), the name in two lines.</summary>
        static VisualElement ItemCard(string icon, E.Cell cell, string name, bool locked, string count)
        {
            var c = E.Div().Col(Align.Center).Size(ItemW);
            c.style.marginRight = 4;
            c.pickingMode = PickingMode.Position;
            var slot = E.Slot(icon, 54, locked ? E.Cell.Common : cell, 0.66f, locked);
            if (count != null) slot.Add(E.Badge(count, false, 16).Abs(-4, null, null, -2));
            if (locked) slot.Add(E.Icon("lock", 18).Abs(null, null, -4, -3));
            c.Add(slot);
            c.Add(E.T(name, 11).TwoLines(ItemW - 2, 7.5f).Margin(2));
            return c;
        }

        VisualElement BuildCard(BuildingDefinition def)
        {
            bool unlocked = G.IsUnlocked(def);
            int count = G.Count(def.Id, true);
            bool limit = def.MaxCount.HasValue && count >= def.MaxCount.Value;
            var card = ItemCard(BuildingIcon(def), CellFor(def), def.Name, !unlocked, count > 0 ? (def.MaxCount.HasValue ? $"{count}/{def.MaxCount}" : $"×{count}") : null);
            if (!unlocked)
                card.Add(E.T(G.Content.Technology(def.RequiresTech ?? "")?.Name ?? "Research", 10, Theme.Lilac).Fit(ItemW - 2, 7));
            else if (limit) card.Add(E.T("Limit reached", 10, Theme.Pale).Fit(ItemW - 2, 7));
            else
            {
                card.Add(CostLine(def.Cost, 10, true).Also(r => r.style.maxWidth = ItemW));
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
                card.Add(E.T(G.Content.Technology(d.RequiresTech)?.Name ?? "Research", 10, Theme.Lilac).Fit(ItemW - 2, 7));
            else
            {
                card.Add(CostLine(d.Cost, 10, true).Also(r => r.style.maxWidth = ItemW));
                string id = d.Id;
                Tap(card, () => store.BeginDecorating(id));
            }
            return card;
        }

        // MARK: - Research window

        /// <summary>The campfire level band a technology belongs to (columns of the tree).</summary>
        static int Tier(TechnologyDefinition t) => Math.Max(0, ((t.HeartLevel ?? 1) - 1) / 5);

        const float TechColW = 124, TechDetailW = 200;

        void BuildResearch(VisualElement host)
        {
            var active = G.State.ActiveResearch;
            var techs = G.Content.Technologies;
            if (selectedTech == null || G.Content.Technology(selectedTech) == null)
                selectedTech = active?.TechId ?? techs.FirstOrDefault(t => G.GetResearchStatus(t.Id).State == ResearchState.Available)?.Id ?? techs.First().Id;

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
            foreach (var group in techs.GroupBy(Tier).OrderBy(g => g.Key))
            {
                int from = group.Key * 5 + 1, to = from + 4;
                bool reached = G.HeartLevel >= from;
                var col = E.Div().Col().Ignore().Size(TechColW);
                col.style.marginRight = 5;
                var head = E.Div().Row().Center().Ignore();
                head.style.height = 20;
                head.Add(E.Icon(reached ? "flame" : "lock", 15));
                head.Add(E.T(group.Key >= 6 ? $"Level {from}+" : $"Level {from}–{to}", 11, reached ? Color.white : Theme.Pale).Margin(0, 0, 0, 3));
                col.Add(head);
                foreach (var t in group) col.Add(TechNode(t));
                tree.Add(col);
            }
            body.Add(tree);

            var side = E.Scroll(ScrollViewMode.Vertical);
            side.style.width = TechDetailW;
            side.style.flexShrink = 0;
            side.style.marginLeft = 6;
            side.contentContainer.style.paddingBottom = 6;
            if (active != null)
            {
                var tech = G.Content.Technology(active.TechId);
                var bar = E.Capsule(34).Pad(0, 8, 0, 6).Margin(0, 0, 6);
                bar.Add(E.Icon("clock", 20));
                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 5;
                var r = E.Div().Row().Ignore();
                r.Add(E.T(tech?.Name ?? active.TechId, 10.5f).Fit(TechDetailW - 90, 7).Grow());
                r.Add(Live(() => Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0), 10.5f));
                col.Add(r);
                col.Add(LiveBar(() => G.GetResearchStatus(active.TechId).Progress, 7, E.FillKind.Blue).Margin(2));
                bar.Add(col);
                string id = active.TechId;
                Tap(bar, () => selectedTech = id);
                side.Add(bar);
            }
            else if (econ.Production.Knowledge <= 0)
                side.Add(Notice("people", "Nobody studies yet: put people on Research (People › Work).", true).Margin(0, 0, 6));
            side.Add(TechDetail(G.Content.Technology(selectedTech)));
            body.Add(side);
            host.Add(body);
        }

        static (E.Cell cell, string icon) TechLook(ResearchState s) => s switch
        {
            ResearchState.Researched => (E.Cell.Uncommon, "check"),
            ResearchState.InProgress => (E.Cell.Rare, "clock"),
            ResearchState.Available => (E.Cell.Legendary, "knowledge"),
            _ => (E.Cell.Common, "lock"),
        };

        VisualElement TechNode(TechnologyDefinition t)
        {
            var st = G.GetResearchStatus(t.Id);
            bool sel = t.Id == selectedTech;
            var (cell, icon) = TechLook(st.State);
            var n = E.Div().Row().Skin(sel ? "slot" : "section").Pad(0, 4, sel ? 3 : 0, 4).Margin(0, 0, 5);
            n.style.height = 42;
            n.Add(E.Slot(icon, 30, cell, 0.62f));
            var name = E.T(t.Name, 11, st.State == ResearchState.Blocked ? Theme.Pale : (Color?)null).TwoLines(TechColW - 46, 7);
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.marginLeft = 4;
            n.Add(name);
            if (st.State == ResearchState.InProgress)
                n.Add(LiveBar(() => G.GetResearchStatus(t.Id).Progress, 5, E.FillKind.Blue).Abs(6, null, 6, 4));
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
            var card = E.Inset().Col().Pad(7, 8);
            if (t == null) return card;
            var st = G.GetResearchStatus(t.Id);
            var (cell, icon) = TechLook(st.State);
            var head = E.Div().Row().Ignore();
            head.Add(E.Slot(st.State == ResearchState.Blocked ? "lock" : "knowledge", 40, cell, 0.62f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 6;
            col.Add(E.T(t.Name, 13).TwoLines(TechDetailW - 70, 8).Also(l => l.style.unityTextAlign = TextAnchor.MiddleLeft));
            var time = E.Div().Row().Ignore();
            time.Add(E.Icon("clock", 14));
            time.Add(E.T(Fmt.Clock(t.ResearchSeconds), 10.5f).Margin(0, 0, 0, 3));
            col.Add(time);
            head.Add(col);
            card.Add(head);
            card.Add(E.Ink(t.Description, 10.5f).Margin(4));
            string fx = EffectsText(t);
            if (fx.Length > 0) card.Add(E.T(fx, 10.5f, Theme.Up).Wrap().Margin(4));

            var bottom = E.Div().Col().Ignore().Margin(6);
            switch (st.State)
            {
                case ResearchState.Researched:
                    bottom.Add(E.Div().Row().Ignore().Kids(E.Icon("check", 20), E.T("Discovered", 12, Theme.Up).Margin(0, 0, 0, 4)));
                    break;
                case ResearchState.InProgress:
                    bottom.Add(LiveLabeledBar(() => G.GetResearchStatus(t.Id).Progress, () => Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0), 14, E.FillKind.Blue));
                    break;
                case ResearchState.Blocked:
                    foreach (var m in st.Missing)
                    {
                        var r = E.Div().Row(0, Align.FlexStart).Ignore().Margin(2);
                        r.Add(E.Icon("lock", 15));
                        r.Add(E.T(m, 10.5f, Theme.Pale).Wrap().Grow().Margin(0, 0, 0, 4));
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
                                     can ? E.Kind.Green : E.Kind.Grey, 34, null, 13, SubLine("clock", Fmt.Clock(t.ResearchSeconds)));
                    b.style.marginTop = 6;
                    bottom.Add(b);
                    break;
                }
            }
            card.Add(bottom);
            return card;
        }
    }
}
