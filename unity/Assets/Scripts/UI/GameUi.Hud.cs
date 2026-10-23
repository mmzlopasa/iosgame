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
    /// The in-game HUD, after the kit's "resource bar on top, gameplay visible" layout:
    /// a thin strip on top (the settlement tile with the chapter bar, a dark capsule with the hour,
    /// people and resources, settings on the right) and a second, smaller capsule under it (season,
    /// temperature, hope, discontent, the sick); the goal card on the left; running timers and the
    /// menu tiles on the right; the compass and small buttons bottom-left, news in the middle and the
    /// Build button bottom-right. Every piece is placed absolutely inside the safe area (<see cref="LayoutHud"/>).
    /// </summary>
    public partial class GameUi
    {
        // MARK: - Metrics (panel units; the mock in tools/ui/mock uses the same numbers)

        /// <summary>Top of the left rail (goal card, promise): under the player block.</summary>
        const float RailY = 86;
        /// <summary>Top of the right column (menu hexagons, timers): under the settings button.</summary>
        const float TilesY = 52;
        const float LeftRailW = 224;
        const float TimersW = 182;
        const float TimerH = 40;
        /// <summary>The menu hexagons and the room each takes (its label hangs under the lower point).</summary>
        const float TileSize = 48;
        const float TileH = 58;
        const float TileGap = 6;
        /// <summary>Width of the right column (a hexagon and its label's overhang).</summary>
        const float TileColW = TileSize + 16;
        const float BuildButton = 72;
        const float SmallButton = 44;
        const float CompassSize = 50;
        /// <summary>
        /// Width of the card (selection, placement): landscape docks it on the left at the kit's popup width (at most
        /// half the screen); portrait makes it a bottom sheet across the screen (at most 560 wide on an iPad).
        /// </summary>
        float CardW => Portrait ? SheetW : Mathf.Min(360, SafeWidth * 0.52f);
        /// <summary>
        /// Width of a portrait bottom sheet (cards, the build strip, windows, dialogs): the screen's width less a margin
        /// that keeps the close button on the plank's corner inside the screen; at most 560 (iPad).
        /// </summary>
        float SheetW => Mathf.Min(560, SafeWidth - 28);
        /// <summary>Top of popups (below the compact top bar: one row of counters, 40 units).</summary>
        float PopupTop => Portrait ? BarBottom + 8 : SafeTop + 50;

        // MARK: Portrait metrics (the kit's phone screens)

        /// <summary>Rows of counters in the top bar this tick (portrait stacks them; see <see cref="TopRows"/>).</summary>
        int topRowCount = 2;
        /// <summary>Lower edge of the resource rows in portrait.</summary>
        float BarBottom => SafeTop + 4 + topRowCount * (CounterH + 6);
        /// <summary>The player block's height (hexagon frame) under the bar in portrait.</summary>
        const float PlayerBlockH = 72;
        /// <summary>Room under the bottom sheets for the back arrow (portrait).</summary>
        const float BackZone = 60;
        /// <summary>Bottom edge of every bottom sheet in portrait: the back arrow sits under it.</summary>
        float SheetBottom => BottomPad + BackZone;
        /// <summary>Highest top edge of a card in portrait: it covers at most about the lower half of the screen.</summary>
        float CardTopLimit => Mathf.Max(PopupTop, Mathf.Round(PanelH * 0.45f));

        VisualElement topHost, leftHost, timersHost, tilesHost, bottomHost, toastLayer, contextPanel, cardBack;

        void BuildHud()
        {
            topHost = E.Div("top").Ignore().Abs();
            AddRegion(topHost, TopKey, BuildTop);
            leftHost = E.Div("left").Ignore().Col(Align.FlexStart).Abs();
            AddRegion(leftHost, LeftKey, BuildLeft);
            timersHost = E.Div("timers").Ignore().Col(Align.FlexEnd).Abs();
            AddRegion(timersHost, TimersKey, BuildTimers);
            tilesHost = E.Div("tiles").Ignore().Col(Align.FlexEnd).Abs();
            AddRegion(tilesHost, TilesKey, BuildTiles);
            bottomHost = E.Div("bottom").Ignore().Row(0, Align.FlexEnd).Abs();
            AddRegion(bottomHost, BottomKey, BuildBottom);
            toastLayer = E.Div("toasts").Ignore().Col(Align.Center).Abs();
            toastLayer.style.overflow = Overflow.Hidden;
            AddRegion(toastLayer, ToastKey, BuildToasts);

            // The selection / placement card, docked on the left; it sizes to its content (bottom-aligned)
            // and its body scrolls when taller than the screen allows.
            contextPanel = E.Div("context").Ignore().Col().Abs();
            contextPanel.style.justifyContent = Justify.FlexEnd;
            AddRegion(contextPanel, ContextKey, BuildContext);

            // Portrait: the back arrow under the card's bottom sheet (it closes the card, like the kit's).
            cardBack = E.Back().Abs();
            cardBack.name = "card-back";
            Tap(cardBack, CloseContext);

            foreach (var e in new[] { leftHost, timersHost, tilesHost, bottomHost, toastLayer, contextPanel, cardBack, topHost })
                hudLayer.Add(e);
            lastSafe = default;
        }

        /// <summary>Closes whatever card is open (placement, decorating, a selection).</summary>
        void CloseContext()
        {
            if (store.DecoratingId != null) store.EndDecorating();
            else if (store.PlacingBuildingId != null) store.CancelPlacement();
            store.SelectedBuildingId = null;
            store.SelectedNode = null;
        }

        /// <summary>Places the HUD pieces inside the safe area and hides what a popup covers (every tick; cheap).</summary>
        void LayoutHud()
        {
            bool sheet = SheetOpen, card = ContextOpen, modal = ModalOpen;
            toastLayer.Hidden(modal);
            float l = SafeLeft + 8, r = SafeRight + 8, top = SafeTop;
            topRowCount = Portrait ? TopRows().Count : 2;
            Place(topHost, l, top + 4, r, null);
            float shade = Portrait ? BarBottom + (TopCompact ? 14 : PlayerBlockH + 18) : 84;
            if (!Mathf.Approximately(topShade.style.height.value.value, shade)) topShade.style.height = shade;
            if (Portrait) LayoutHudPortrait(sheet || modal, card, l, r);
            else
            {
                Place(leftHost, l, top + RailY, null, null);
                Place(timersHost, null, top + RailY, r + TileColW + 8, null);
                Place(tilesHost, null, top + TilesY, r, null);
                Place(bottomHost, l, null, SafeRight + 6, BottomPad + 4);
                tilesHost.Hidden(sheet || modal);
                bottomHost.Hidden(sheet || modal);
                // Notices between whatever occupies the two sides.
                float leftEdge = l + (card ? CardW : LeftRailW) + 10;
                float rightEdge = r + TileColW + 8 + (card ? 0 : TimersW + 10);
                Place(toastLayer, leftEdge, top + RailY, rightEdge, BottomPad + BuildButton + 10);
                SetJustify(toastLayer, Justify.FlexStart);
                SetAlign(timersHost, Align.FlexEnd);
                contextPanel.style.width = CardW;
                Place(contextPanel, l, PopupTop + 4, null, BottomPad + 8);
                float maxCard = Mathf.Min(PanelH - PopupTop - 4 - BottomPad - 8, 470);
                contextPanel.style.maxHeight = maxCard;
                cardBack.Hidden(true);
            }
            leftHost.Hidden(sheet || card || modal);
            timersHost.Hidden(sheet || card || modal);
            UpdateViewShift(card && !sheet);
        }

        /// <summary>
        /// Portrait, after the kit's phone screens: the resource bar on top (two rows when needed) with the player block
        /// under it on the left, the goal and the running timers below that, the menu hexagons down the right edge,
        /// the news over the small buttons bottom-left and Build bottom-right. A card is a bottom sheet across the
        /// screen with the back arrow under it; notices rise from the bottom (or from the card's top edge).
        /// </summary>
        void LayoutHudPortrait(bool sheet, bool card, float l, float r)
        {
            float rail = BarBottom + 4 + (TopCompact ? 0 : PlayerBlockH + 8);
            Place(leftHost, l, rail, null, null);
            float leftH = leftHost.layout.height;
            if (float.IsNaN(leftH) || leftHost.childCount == 0) leftH = 0;
            Place(timersHost, l, rail + leftH, null, null);
            SetAlign(timersHost, Align.FlexStart);
            Place(tilesHost, null, BarBottom + 4, r, null);
            Place(bottomHost, l, null, SafeRight + 6, BottomPad + 4);
            tilesHost.Hidden(sheet || card);
            bottomHost.Hidden(sheet || card);

            // Above the bottom row: the small buttons and the news over them (or above the top edge of a sheet).
            float toastBottom = card || sheet ? PanelH - CardTopLimit + 8 : BottomPad + 4 + SmallButton * 1.06f + 10 + 30 + 10;
            float toastTop = card || sheet ? PopupTop : Mathf.Round(PanelH * 0.42f);
            Place(toastLayer, l, toastTop, r, toastBottom);
            SetJustify(toastLayer, Justify.FlexEnd);

            float w = CardW;
            if (!Mathf.Approximately(contextPanel.style.width.value.value, w)) contextPanel.style.width = w;
            Place(contextPanel, SafeLeft + (SafeWidth - w) / 2, CardTopLimit, null, SheetBottom);
            contextPanel.style.maxHeight = PanelH - CardTopLimit - SheetBottom;
            cardBack.Hidden(!card || sheet);
            Place(cardBack, SafeLeft + 6, null, null, BottomPad + 2);
        }

        /// <summary>
        /// Keeps what a card is about (the selected building, the ghost being placed) in view above the portrait bottom
        /// sheet: the camera's picture is shifted up so its focus sits in the middle of the world left uncovered.
        /// </summary>
        void UpdateViewShift(bool card)
        {
            var rig = store.Rig;
            if (rig == null) return;
            float shift = 0;
            if (Portrait && card)
            {
                float cardTop = PanelH - SheetBottom;
                if (contextPanel.childCount > 0)
                {
                    float y = contextPanel.ElementAt(0).worldBound.yMin;
                    if (!float.IsNaN(y) && y > 0) cardTop = y;
                }
                float middle = (BarBottom + cardTop) / 2; // panel units from the top
                shift = Mathf.Clamp(1f - 2f * middle / PanelH, 0f, 0.7f);
            }
            rig.ViewShift = new Vector2(0, shift);
        }

        static void SetJustify(VisualElement e, Justify j) { if (e.style.justifyContent != j) e.style.justifyContent = j; }
        static void SetAlign(VisualElement e, Align a) { if (e.style.alignItems != a) e.style.alignItems = a; }

        /// <summary>Places an absolute element; a side given as null is cleared (so a layout for one orientation never leaks into the other).</summary>
        static void Place(VisualElement e, float? left, float? top, float? right, float? bottom)
        {
            var st = e.style;
            if (Changed(st.left, left)) st.left = Side(left);
            if (Changed(st.top, top)) st.top = Side(top);
            if (Changed(st.right, right)) st.right = Side(right);
            if (Changed(st.bottom, bottom)) st.bottom = Side(bottom);
        }

        static bool Changed(StyleLength now, float? want) => want.HasValue
            ? now.keyword != StyleKeyword.Undefined || !Mathf.Approximately(now.value.value, want.Value)
            : now.keyword != StyleKeyword.Null;

        static StyleLength Side(float? v) => v.HasValue ? new StyleLength(v.Value) : new StyleLength(StyleKeyword.Null);

        // MARK: - Shared look-ups

        internal static string IconFor(ResourceType t) => t switch
        {
            ResourceType.Food => "food",
            ResourceType.Wood => "wood",
            ResourceType.Stone => "stone",
            ResourceType.Ore => "ore",
            ResourceType.Hides => "hides",
            ResourceType.Bronze => "bronze",
            ResourceType.Planks => "planks",
            ResourceType.Tools => "tools",
            ResourceType.Goods => "goods",
            ResourceType.Coins => "coins",
            _ => "knowledge",
        };

        /// <summary>The rarity cell a resource sits on (rewards, gathered goods).</summary>
        static E.Cell CellFor(ResourceType t) => t switch
        {
            ResourceType.Food => E.Cell.Uncommon,
            ResourceType.Wood => E.Cell.Legendary,
            ResourceType.Stone => E.Cell.Common,
            ResourceType.Ore => E.Cell.Rare,
            ResourceType.Hides => E.Cell.Legendary,
            ResourceType.Bronze => E.Cell.Rare,
            ResourceType.Planks => E.Cell.Legendary,
            ResourceType.Tools => E.Cell.Rare,
            ResourceType.Goods => E.Cell.Uncommon,
            ResourceType.Coins => E.Cell.Legendary,
            _ => E.Cell.Epic,
        };

        static string SeasonIcon(string id) => id switch { "summer" => "summer", "autumn" => "autumn", "winter" => "winter", _ => "spring" };

        static Color ApprovalColor(int a) => a >= 65 ? Theme.Up : a >= 40 ? Theme.Gold : Theme.Bad;

        bool MilestoneReady() => G.Content.Milestones.Any(m => G.CanClaimMilestone(m.Id));

        /// <summary>Category label used for tabs and the build strip.</summary>
        static string Category(BuildingDefinition d)
        {
            if (d.HousingValue > 0) return "Shelter";
            if (d.JobSlots != null && d.JobSlots.Any(s => s.Job == JobType.Foraging || s.Job == JobType.Farming)) return "Food";
            if (d.StorageValue > 0 || d.ProtectedFoodValue > 0) return "Storage";
            return "Work";
        }

        static E.Cell CellFor(BuildingDefinition d) => Category(d) switch
        {
            "Shelter" => E.Cell.Rare,
            "Food" => E.Cell.Uncommon,
            "Storage" => E.Cell.Legendary,
            _ => d.Heart ? E.Cell.Legendary : E.Cell.Common,
        };

        /// <summary>The picture of the real building (rendered from its model) at a level.</summary>
        static string BuildingIcon(BuildingDefinition def, int level = 1) => "thumb:" + def.SpriteAt(level);

        /// <summary>A red notification badge in the top-right corner of a parent.</summary>
        static VisualElement CornerBadge(string text, float size = 20) => E.Badge(text, true, size).Abs(null, -6, -6);

        static readonly ResourceType[] StripOrder = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge, ResourceType.Ore, ResourceType.Hides, ResourceType.Bronze,
                                                              ResourceType.Planks, ResourceType.Tools, ResourceType.Goods, ResourceType.Coins };

        // MARK: - Top: the player block and the resource counters (the blue kit)

        /// <summary>One piece of the top bar: a counter or a chip, its width and how willingly it is dropped (higher goes first).</summary>
        readonly struct TopItem
        {
            public readonly string Id;
            public readonly float Width;
            public readonly int Drop;
            public TopItem(string id, float width, int drop) { Id = id; Width = width; Drop = drop; }
        }

        const float CounterH = 30, ChipH = 28;
        /// <summary>Space between counters: tighter in portrait, where two rows must hold them all across the phone.</summary>
        float CounterGap => Portrait ? 6 : 10;

        /// <summary>Width of a counter showing a text (one digit of slack so it does not jump on every change).</summary>
        static float CounterW(string text, bool plus) => 6 + E.CounterIconRoom + 4 + Theme.TextWidth(text + "0", Theme.Num) + (plus ? 6 + 24 - 3 : 12);

        static float ChipW(string text) => 7 + (ChipH - 6) + 4 + Theme.TextWidth(text + "0", Theme.Body) + 10;

        /// <summary>Compact (one row, no player block) while a window, a card or the build strip is open: the popup needs the height.</summary>
        // A dialog (event, milestone, welcome back…) also gets the one-row bar: nothing of the HUD may peek out around it.
        bool TopCompact => SheetOpen || ContextOpen || ModalOpen;

        /// <summary>Width of the player block: the hexagon frame, the name, the rank and the hope bar.</summary>
        const float PlayerNameW = 156;
        float PlayerW => HexFrameW + 8 + PlayerNameW;
        const float HexFrameW = 64;

        static bool PlusFor(ResourceType t) => t == ResourceType.Food || t == ResourceType.Wood;

        /// <summary>Everything the top bar may show, in order, with its width now.</summary>
        List<TopItem> TopCandidates()
        {
            var list = new List<TopItem>
            {
                new TopItem("people", CounterW($"{Fmt.Compact(G.State.Population)}/{Fmt.Compact(G.HousingCapacity())}", true), 0),
            };
            foreach (var t in StripOrder)
            {
                if (t == ResourceType.Hides && G.State.Resources.Hides < 1) continue;
                if (t == ResourceType.Ore && G.State.Resources.Ore < 1) continue;
                if (t == ResourceType.Bronze && G.State.Resources.Bronze < 1) continue;
                if (t == ResourceType.Planks && G.State.Resources.Planks < 1) continue;
                if (t == ResourceType.Tools && G.State.Resources.Tools < 1) continue;
                if (t == ResourceType.Goods && G.State.Resources.Goods < 1) continue;
                if (t == ResourceType.Coins && G.State.Resources.Coins < 1) continue;
                int drop = t == ResourceType.Goods ? 13 : t == ResourceType.Coins ? 4 : t == ResourceType.Tools ? 12 : t == ResourceType.Planks ? 11 : t == ResourceType.Bronze ? 10 : t == ResourceType.Hides ? 9 : t == ResourceType.Ore ? 8 : t == ResourceType.Knowledge ? 3 : t == ResourceType.Stone ? 2 : 0;
                list.Add(new TopItem(t.ToString(), CounterW(Fmt.Compact(G.State.Resources[t]), PlusFor(t)), drop));
            }
            list.Add(new TopItem("clock", ChipW("00:00"), 7));
            if (G.HasClimate) list.Add(new TopItem("temp", ChipW(G.Freezing ? "Freezing" : "−00°"), 4));
            if (G.HasDiscontent) list.Add(new TopItem("discontent", ChipW("00"), 5));
            if (G.HasHealth && G.SickCount > 0) list.Add(new TopItem("sick", ChipW(G.SickCount.ToString(Inv)), 1));
            if (TopCompact) list.Add(new TopItem("hope", ChipW("100"), 6));
            return list;
        }

        /// <summary>
        /// The rows of the top bar: items in order, packed greedily into one row (compact) or two (beside the
        /// player block); while they do not fit, the most expendable item is dropped (hides, ore, the clock…).
        /// </summary>
        List<List<TopItem>> TopRows()
        {
            // Portrait: the bar has the whole width (the player block goes under it and the settings button tops the
            // menu column on the right), in two rows; landscape: one row while a popup is open, else two beside the
            // player block, the settings button at the end of the first.
            float avail = Portrait ? SafeWidth - 16 : SafeWidth - 16 - (TopCompact ? 0 : PlayerW + 10) - HudSquareSize - 10;
            int rows = Portrait || !TopCompact ? 2 : 1;
            // The second row ends where the menu hexagons begin (they start right under the settings button).
            float Avail(int row) => row == 0 || Portrait ? avail : SafeWidth - 16 - PlayerW - 10 - TileColW - 10;
            var items = TopCandidates();
            while (true)
            {
                var packed = new List<List<TopItem>> { new List<TopItem>() };
                float x = 0;
                bool fits = true;
                foreach (var it in items)
                {
                    float w = (x > 0 ? CounterGap : 0) + it.Width;
                    if (x + w > Avail(packed.Count - 1))
                    {
                        if (packed.Count == rows) { fits = false; break; }
                        packed.Add(new List<TopItem>());
                        x = 0;
                        w = it.Width;
                    }
                    packed[packed.Count - 1].Add(it);
                    x += w;
                }
                if (fits || items.Count <= 1) return packed;
                int worst = 0;
                for (int i = 1; i < items.Count; i++) if (items[i].Drop > items[worst].Drop) worst = i;
                items.RemoveAt(worst);
            }
        }

        string TopKey() => $"{SafeWidth}|{Portrait}|{TopCompact}|{G.State.EpochId}|{MilestoneReady() || G.PendingStory != null}|{G.State.SettlementName}|{store.Profile.Avatar}|" +
                           string.Join("/", TopRows().Select(r => string.Join(",", r.Select(i => i.Id))));

        void BuildTop(VisualElement host)
        {
            var bar = E.Div().Row(0, Align.FlexStart).Ignore();
            bar.style.width = SafeWidth - 16;
            if (!TopCompact && !Portrait) bar.Add(PlayerBlock());

            // The counters and chips, in rows.
            var rowsCol = E.Div().Col().Ignore();
            rowsCol.style.marginLeft = TopCompact || Portrait ? 0 : 10;
            foreach (var row in TopRows())
            {
                var r = E.Div().Row().Ignore();
                r.style.height = CounterH + 6;
                foreach (var it in row)
                {
                    var e = TopPiece(it.Id);
                    if (e == null) continue;
                    if (r.childCount > 0) e.style.marginLeft = CounterGap + e.style.marginLeft.value.value;
                    r.Add(e);
                }
                rowsCol.Add(r);
            }
            bar.Add(rowsCol);
            if (!Portrait)
            {
                bar.Add(E.Div().Grow().Ignore());
                bar.Add(E.Settings(Act(() => store.ActivePanel = Panel.Settings), HudSquareSize));
                host.Add(bar);
                return;
            }
            // Portrait: the bar across the top, the player block under it on the left (like the kit's phone screens).
            var col = E.Div().Col(Align.FlexStart).Ignore();
            col.Add(bar);
            if (!TopCompact) col.Add(PlayerBlock().Margin(4));
            host.Add(col);
        }

        /// <summary>
        /// The player block: the founder's portrait in the hexagon frame (tap: the profile, or Goals while one waits), the settlement's name, the heart's level on
        /// the rank pill and hope as the kit's energy bar.
        /// </summary>
        VisualElement PlayerBlock()
        {
            var block = E.Div().Row(0, Align.FlexStart).Ignore();
            var frame = E.HexFrame(store.Profile.Avatar, HexFrameW);
            bool goalWaits = MilestoneReady() || G.PendingStory != null;
            if (goalWaits) frame.Add(CornerBadge("!", 20).Abs(null, 2, 0));
            Tap(frame, () => { if (MilestoneReady() || G.PendingStory != null) store.ActivePanel = Panel.Objectives; else ShowProfile(); });
            block.Add(frame);
            var col = E.Div().Col().Ignore().Size(PlayerNameW);
            col.style.marginLeft = 8;
            col.style.paddingTop = 2;
            var name = E.T(G.State.SettlementName ?? "Founder", Theme.H2).Fit(PlayerNameW, Theme.Body);
            name.style.whiteSpace = WhiteSpace.NoWrap;
            col.Add(name);
            var rank = E.RankPill(out var lv);
            rank.style.alignSelf = Align.FlexStart;
            rank.style.marginTop = 2;
            Bind(() => { string s = $"Lv.{G.HeartLevel}"; if (lv.text != s) lv.text = s; });
            Tap(rank, () => { if (G.Heart != null) store.FocusBuilding(G.Heart.Id); });
            col.Add(rank);
            var hope = E.HudBar("approval", out var fill, out var label, PlayerNameW);
            hope.style.marginTop = 6;
            Bind(() =>
            {
                int a = ApprovalNow;
                E.SetFill(fill, a / 100.0);
                string s = $"{a}/100";
                if (label.text != s) label.text = s;
            });
            Tap(hope, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
            col.Add(hope);
            block.Add(col);
            return block;
        }

        const float HudSquareSize = 40;

        /// <summary>One counter or chip of the top bar, with its binding and its tap.</summary>
        VisualElement TopPiece(string id)
        {
            switch (id)
            {
                case "people":
                {
                    var c = E.HudCounter("people", out var v, Act(() => { peopleTab = 0; store.ActivePanel = Panel.People; }), CounterH);
                    var idle = E.Badge("!", true, 18).Abs(14, -6);
                    c.Add(idle);
                    Bind(() =>
                    {
                        string s = $"{Fmt.Compact(G.State.Population)}/{Fmt.Compact(G.HousingCapacity())}";
                        if (v.text != s) v.text = s;
                        idle.Hidden(!(G.IdleWorkers > 0 || G.State.FoodShortage));
                    });
                    Tap(c, () => { peopleTab = 0; store.ActivePanel = Panel.People; });
                    return c;
                }
                case "clock":
                {
                    var c = E.HudChip("summer", out var v, ChipH);
                    var clockIcon = c.ElementAt(0);
                    bool night = false;
                    Bind(() =>
                    {
                        bool n = Atmosphere.Nightness > 0.3f;
                        if (n != night || v.text == "") { night = n; E.SetIcon(clockIcon, n ? "moon" : SeasonIcon(G.Season?.Id ?? "summer")); }
                        string t = ShortClock();
                        if (v.text != t) v.text = t;
                    });
                    Tap(c, () => store.ActivePanel = G.HasClimate ? Panel.Weather : Panel.People);
                    return c;
                }
                case "temp":
                {
                    var c = E.HudChip("thermometer", out var v, ChipH);
                    var icon = c.ElementAt(0);
                    int last = -1;
                    Bind(() =>
                    {
                        double temp = G.Temperature;
                        bool snap = CurrentSnap() != null;
                        int state = G.Freezing ? 2 : snap ? 1 : 0;
                        string s = G.Freezing ? "Freezing" : Deg(temp);
                        if (v.text != s) v.text = s;
                        E.Tint(v, G.Freezing ? Theme.Bad : snap ? Theme.Icy : ExposureColor(G.ExposureOf(temp)));
                        if (state != last) { last = state; E.SetIcon(icon, state == 2 ? "alert" : state == 1 ? "winter" : "thermometer"); }
                    });
                    Tap(c, () => store.ActivePanel = Panel.Weather);
                    return c;
                }
                case "discontent":
                {
                    var c = E.HudChip("discontent", out var v, ChipH);
                    Bind(() =>
                    {
                        string s = ((int)Math.Round(G.State.Discontent)).ToString(Inv);
                        if (v.text != s) v.text = s;
                        E.Tint(v, MoodColor(G.DiscontentMood));
                    });
                    Tap(c, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
                    return c;
                }
                case "sick":
                {
                    var c = E.HudChip("sick", out var v, ChipH);
                    Bind(() => { string s = G.SickCount.ToString(Inv); if (v.text != s) v.text = s; E.Tint(v, Theme.Hex("#FFB3A8")); });
                    Tap(c, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
                    return c;
                }
                case "hope":
                {
                    var c = E.HudChip("approval", out var v, ChipH);
                    Bind(() => { int a = ApprovalNow; string s = a.ToString(Inv); if (v.text != s) v.text = s; E.Tint(v, ApprovalColor(a)); });
                    Tap(c, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
                    return c;
                }
            }
            if (!Enum.TryParse(id, out ResourceType type)) return null;
            // The green + only where it leads somewhere useful: food and wood open Build on the places that make them.
            Action plus = type == ResourceType.Food ? Act(() => OpenBuild("Food")) : type == ResourceType.Wood ? Act(() => OpenBuild("Work")) : null;
            var counter = E.HudCounter(IconFor(type), out var value, plus, CounterH);
            Bind(() =>
            {
                double x = G.State.Resources[type];
                string s = Fmt.Compact(x);
                if (value.text != s) value.text = s;
                E.Tint(value, x >= caps[type] - 0.5 ? Theme.Gold : econ.Net[type] < -0.0005 && x < 20 ? Theme.Bad : Color.white);
            });
            Tap(counter, () => { if (type == ResourceType.Knowledge) store.ActivePanel = Panel.Research; else { peopleTab = 0; store.ActivePanel = Panel.People; } });
            return counter;
        }

        /// <summary>Opens the build strip on a category.</summary>
        void OpenBuild(string tab)
        {
            store.CancelPlacement();
            store.SelectedBuildingId = null;
            store.SelectedNode = null;
            pendingTab = tab;
            store.ActivePanel = Panel.Build;
        }

        /// <summary>The hour of the game clock (one day lasts six real hours).</summary>
        string ShortClock()
        {
            double t = G.State.SimTime / Atmosphere.DayLength;
            // Daylight is the first 75% of the cycle: map it to 06:00–21:00, the night to 21:00–06:00.
            double p = t - Math.Floor(t);
            double hours = p < 0.75 ? 6 + p / 0.75 * 15 : 21 + (p - 0.75) / 0.25 * 9;
            hours %= 24;
            return $"{(int)hours:00}:{(int)(hours % 1 * 60):00}";
        }

        // MARK: - Left rail: the goal and a promise

        string LeftKey() => $"{G.CurrentObjective?.Id}|{G.State.ActivePetition?.PetitionId}";

        void BuildLeft(VisualElement host)
        {
            var o = G.CurrentObjective;
            if (o != null)
            {
                var card = E.Panel().Pad(7, 11, 11, 11);
                card.style.width = LeftRailW;
                card.style.marginBottom = 8;
                var head = E.Div().Row().Ignore();
                head.Add(E.Icon("goals", 24));
                // Two lines at most: the title shrinks (not below the minimum) to fit them.
                var t = E.T(o.Title, Theme.Body).TwoLines(LeftRailW - 22 - 24 - 6);
                t.style.unityTextAlign = TextAnchor.MiddleLeft;
                t.style.marginLeft = 6;
                head.Add(t);
                card.Add(head);
                var bar = LiveBar(() =>
                {
                    var st = G.Status(o.Conditions);
                    return st.Count == 0 ? 0 : st.Count(s => s.Met) / (double)st.Count;
                }, 10);
                bar.style.marginTop = 5;
                card.Add(bar);
                Tap(card, () => { goalsTab = 2; store.ActivePanel = Panel.Objectives; });
                host.Add(card);
            }
            var promise = PromiseCard();
            if (promise != null) host.Add(promise);
        }

        // MARK: - Timers (research, scouting, construction)

        int MaxTimers() => Portrait ? 3 : Mathf.Clamp(Mathf.FloorToInt((PanelH - SafeTop - RailY - BottomPad - 12) / (TimerH + 6)), 0, 5);

        string TimersKey()
        {
            var builds = string.Join(",", G.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).Select(b => b.Id));
            return $"{G.State.ActiveResearch?.TechId}|{G.State.ActiveExpedition?.RegionId}|{builds}|{MaxTimers()}|{G.IsStoked}|{G.State.Speedups.Count}";
        }

        void BuildTimers(VisualElement host)
        {
            var cards = new List<VisualElement>();
            var r = G.State.ActiveResearch;
            if (r != null)
            {
                var tech = G.Content.Technology(r.TechId);
                cards.Add(TimerCard("knowledge", tech?.Name ?? r.TechId, () => 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds),
                    () => Fmt.Clock(r.RemainingSeconds), E.FillKind.Blue, () => store.ActivePanel = Panel.Research, SpeedupTask.Research));
            }
            var e = G.State.ActiveExpedition;
            if (e != null)
            {
                var region = G.Content.Regions.FirstOrDefault(x => x.Id == e.RegionId);
                cards.Add(TimerCard("compass", region?.Name ?? e.RegionId, () => 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds),
                    () => Fmt.Clock(e.RemainingSeconds), E.FillKind.Green, () => { mapTab = 0; store.ActivePanel = Panel.Map; }, SpeedupTask.Expedition));
            }
            if (G.IsStoked && G.Heart != null)
            {
                var w = G.Content.Balance.Warmth;
                int heartId = G.Heart.Id;
                cards.Add(TimerCard("flame", "Roaring fire", () => (G.State.StokedUntilSimTime - G.State.SimTime) / Math.Max(1, w.StokeSeconds),
                    () => Fmt.Clock(G.State.StokedUntilSimTime - G.State.SimTime), E.FillKind.Red, () => store.FocusBuilding(heartId)));
            }
            foreach (var b in G.State.Buildings.Where(x => x.Status != BuildingStatus.Complete).Take(3))
            {
                var def = G.Content.Building(b.Status == BuildingStatus.Upgrading && b.UpgradeTargetId != null ? b.UpgradeTargetId : b.DefinitionId);
                int id = b.Id;
                cards.Add(TimerCard(b.Status == BuildingStatus.Upgrading ? "level_up" : "build", def?.Name ?? b.DefinitionId,
                    () => G.State.Building(id)?.Progress ?? 1, () => Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0)),
                    E.FillKind.Gold, () => store.FocusBuilding(id), SpeedupTask.Building(id)));
            }
            foreach (var c in cards.Take(MaxTimers())) host.Add(c);
        }

        /// <summary>A running job: icon, name and time left over a thin bar.</summary>
        VisualElement TimerCard(string icon, string name, Func<double> progress, Func<string> left, E.FillKind fill, Action onTap,
                                SpeedupTask? speedup = null)
        {
            // A navy HUD capsule with the job's icon overlapping its left end (like the counters).
            var c = E.HudCapsule(TimerH).Pad(4, 12, 5, E.CounterIconRoom + 6);
            c.style.height = StyleKeyword.Auto;
            c.style.minHeight = TimerH;
            c.style.width = TimersW;
            c.style.marginBottom = 6;
            if (Portrait) c.style.marginLeft = 6; // its icon overhangs the left end, which is the screen's edge here
            c.Add(E.Icon(icon, 34).Abs(-8, 2));
            var col = E.Div().Col().Grow().Ignore();
            // The name on its own line (it wraps rather than being cut), the bar and the time left under it.
            col.Add(E.T(name, Theme.Small).Fit(TimersW - 12 - E.CounterIconRoom - 6 - (speedup != null ? 30 : 0)));
            var line = E.Div().Row().Ignore();
            line.style.marginTop = 1;
            var bar = LiveBar(progress, 9, fill == E.FillKind.Blue ? E.FillKind.Hud : fill).Grow();
            line.Add(bar);
            line.Add(Live(left, Theme.Small).NoShrink().Margin(0, 0, 0, 5));
            col.Add(line);
            c.Add(col);
            // A speed-up button at the right end (a tap on it opens the speed-ups, elsewhere goes to the job).
            if (speedup != null) c.Add(SpeedupButton(speedup.Value, 26).NoShrink().Margin(0, 0, 0, 4));
            Tap(c, onTap);
            return c;
        }

        // MARK: - Right column: menu tiles

        string TilesKey() => $"{Portrait}|{MilestoneReady()}|{G.IdleWorkers}|{ResearchReady()}|{HasCustoms}|{LawReady()}";

        bool ResearchReady() => G.State.ActiveResearch == null && G.Content.Technologies.Any(t => G.GetResearchStatus(t.Id).State == ResearchState.Available);

        void BuildTiles(VisualElement host)
        {
            if (Portrait)
            {
                // Portrait: the settings button tops the column (the bar above has the whole width for the counters).
                var cell = E.Div().Row().Center().Ignore().Size(TileColW);
                cell.style.marginBottom = 8;
                cell.Add(E.Settings(Act(() => store.ActivePanel = Panel.Settings), HudSquareSize));
                host.Add(cell);
            }
            host.Add(MenuTile("goals", "Goals", MilestoneReady() ? "!" : null, () => { goalsTab = 0; store.ActivePanel = Panel.Objectives; }));
            host.Add(MenuTile("people", "People", G.IdleWorkers > 0 ? G.IdleWorkers.ToString(Inv) : null, () => { peopleTab = G.IdleWorkers > 0 ? 0 : 1; store.ActivePanel = Panel.People; }));
            host.Add(MenuTile("knowledge", "Research", ResearchReady() ? "!" : null, () => store.ActivePanel = Panel.Research));
            if (HasCustoms) host.Add(MenuTile("customs", "Customs", LawReady() ? "!" : null, () => store.ActivePanel = Panel.Customs));
        }

        /// <summary>A menu button: the kit's blue hexagon with the icon, an outlined label under it, a red badge on top.</summary>
        VisualElement MenuTile(string icon, string label, string badge, Action onTap)
        {
            var t = E.HudHex(icon, label, TileSize);
            t.style.height = TileH;
            t.style.marginBottom = TileGap;
            if (badge != null) t.Add(CornerBadge(badge, 20).Abs(null, -4, 2));
            Tap(t, onTap);
            return t;
        }

        // MARK: - Bottom row: compass and small buttons, news, Build

        string BottomKey() => $"{SafeWidth}|{Portrait}|{ContextOpen}|{G.PendingEvent != null && store.EventMinimized}";

        void BuildBottom(VisualElement host)
        {
            if (Portrait) { BuildBottomPortrait(host); return; }
            bool card = ContextOpen;
            if (!card)
            {
                var left = SmallButtons();
                host.Add(left);
            }

            // News: the latest thing that happened, one line, centred between the corners.
            var middle = E.Div().Row(0, Align.FlexEnd).Grow().Ignore();
            middle.style.justifyContent = Justify.Center;
            middle.style.marginLeft = 10;
            middle.style.marginRight = 10;
            if (!card)
            {
                int squares = G.PendingEvent != null && store.EventMinimized ? 4 : 3;
                float room = Mathf.Min(380, SafeWidth - 16 - CompassSize - squares * (SmallButton + 8) - 20 - BuildButton - 6);
                var news = NewsCapsule(room);
                middle.Add(news);
            }
            host.Add(middle);

            host.Add(BuildButtonElement());
        }

        /// <summary>Portrait: the news over the small buttons on the left, Build on the right (the bar is hidden under a card).</summary>
        void BuildBottomPortrait(VisualElement host)
        {
            var left = E.Div().Col(Align.FlexStart).Grow().Ignore();
            float room = SafeWidth - 16 - BuildButton - 10;
            var news = NewsCapsule(room);
            news.style.marginBottom = 10;
            left.Add(news);
            left.Add(SmallButtons());
            host.Add(left);
            host.Add(BuildButtonElement());
        }

        /// <summary>The compass (its needle follows the camera; tap: face north-west again) and the small blue squares.</summary>
        VisualElement SmallButtons()
        {
            var left = E.Div().Row(0, Align.FlexEnd).Ignore().NoShrink();
            var compass = E.HudSquare("compass", null, CompassSize);
            var needle = compass.ElementAt(0);
            Bind(() => needle.style.rotate = new Rotate(new Angle(-(CameraRig.Yaw + 45f), AngleUnit.Degree)));
            Tap(compass, () => store.Rig?.ResetView());
            left.Add(compass);
            void Small(VisualElement b) { b.style.marginLeft = 8; left.Add(b); }
            if (G.PendingEvent != null && store.EventMinimized)
            {
                var bell = E.Square("bell", Act(() => store.EventMinimized = false), E.Kind.Orange, SmallButton); // yellow: it wants an answer
                bell.Add(CornerBadge("1", 18));
                Small(bell);
            }
            Small(E.HudSquare("world", Act(() => { mapTab = 0; store.ActivePanel = Panel.Map; }), SmallButton));
            Small(E.HudSquare("legacy", Act(() => store.ActivePanel = Panel.Profile), SmallButton));
            Small(E.HudSquare("eye_off", Act(() =>
            {
                store.CancelPlacement();
                store.SelectedBuildingId = null;
                store.SelectedNode = null;
                store.ActivePanel = Panel.None;
                store.HudHidden = true;
            }), SmallButton));
            return left;
        }

        /// <summary>News: the latest thing that happened, one line (two at most) on a navy capsule with the bell overlapping its left end.</summary>
        VisualElement NewsCapsule(float maxWidth)
        {
            var news = E.HudCapsule(30).Pad(3, 14, 3, E.CounterIconRoom + 4).Ignore();
            news.style.height = StyleKeyword.Auto;
            news.style.minHeight = 30;
            news.style.marginBottom = 8;
            news.style.marginLeft = 6;
            news.style.maxWidth = maxWidth;
            news.Add(E.Icon("bell", 32).Abs(-8, -1));
            float room = maxWidth - 6 - E.CounterIconRoom - 4 - 14 - 6;
            var line = LiveFit(() =>
            {
                // The latest entry of the village's story (the news itself shows as a toast, never twice).
                var last = G.State.Legacy.LastOrDefault();
                return last != null ? last.Title : $"{G.CurrentChapter?.Title ?? G.StageName}";
            }, Theme.Small + 1, room, null, Theme.Small);
            news.Add(line);
            return news;
        }

        /// <summary>The big primary: the kit's striped yellow BUILD button.</summary>
        VisualElement BuildButtonElement()
        {
            var build = E.Div().Col().Center().Skin("btn_orange").Size(BuildButton, BuildButton).Pad(0, 0, 6, 0);
            build.Add(E.Icon("build", 42));
            build.Add(E.T("BUILD", Theme.Num).Margin(-4));
            Tap(build, () =>
            {
                store.CancelPlacement();
                store.ActivePanel = store.ActivePanel == Panel.Build ? Panel.None : Panel.Build;
            });
            return build;
        }

        // MARK: - Toasts

        string ToastKey() => (Portrait ? "p|" : "l|") + string.Join(",", store.Toasts.Select(t => t.CreatedAt.ToString(CultureInfo.InvariantCulture))) + "|" +
                             (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null ? G.PendingEvent.Id : "");

        void BuildToasts(VisualElement host)
        {
            float maxW = Portrait ? Mathf.Min(400, SafeWidth - 16 - 12)
                : Mathf.Max(220, Mathf.Min(400, PanelW - SafeLeft - SafeRight - 2 * (LeftRailW + 24)));
            // A decision put off for later stays on top until it is answered.
            if (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null)
            {
                var pill = E.Button($"Decision waiting: {G.PendingEvent.Title}", Act(() => store.EventMinimized = false), E.Kind.Orange, 40, "bell", Theme.Body, null, maxW);
                pill.style.marginBottom = 8;
                host.Add(pill);
            }
            // Landscape: newest first from the top; portrait: notices rise from the bottom, newest last. Either way,
            // if the column ever runs out of height, the oldest is the one cut.
            // At most the two newest at once, so the village stays in view.
            int n = store.Toasts.Count, first = Mathf.Max(0, n - 2);
            for (int k = 0; k < n - first; k++)
            {
                var toast = store.Toasts[Portrait ? first + k : n - 1 - k];
                string icon = toast.Notice.Kind switch
                {
                    NoticeKind.Success => "check",
                    NoticeKind.Warning => "alert",
                    NoticeKind.Milestone => "sparkle",
                    _ => "bell",
                };
                // A navy HUD capsule, its icon overlapping the left end; white outlined text (red for warnings).
                var t = E.HudCapsule(36).Pad(6, 16, 7, E.CounterIconRoom + 6).Ignore();
                t.style.height = StyleKeyword.Auto;
                t.style.minHeight = 36;
                t.style.maxWidth = maxW;
                t.style.marginLeft = 6;
                t.style.marginBottom = 6;
                t.Add(E.Icon(icon, 34).Abs(-8, 1));
                var text = E.T(toast.Notice.Message, Theme.Body, toast.Notice.Kind == NoticeKind.Warning ? Theme.Bad : (Color?)null).Wrap();
                text.style.flexShrink = 1;
                text.style.maxWidth = maxW - 6 - E.CounterIconRoom - 6 - 16;
                t.Add(text);
                t.style.opacity = 0;
                t.Transition(260, "opacity");
                t.schedule.Execute(() => t.style.opacity = 1).StartingIn(16);
                host.Add(t);
            }
        }
    }
}
