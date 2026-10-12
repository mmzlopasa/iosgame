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
    /// The in-game HUD for a landscape phone, in the style of mobile survival builders: a wooden
    /// settlement badge, a dark status bar (time, people) and the resources along the top; the
    /// goal and round buttons down the left; running timers and wooden tiles down the right; a
    /// gold compass and a gold build button in the bottom corners with a news strip between them.
    /// Text is bold with a dark outline so it reads over any scene.
    ///
    /// Layout (panel units, the screen is 390 tall and 690–850 wide inside the safe area):
    /// the top row ends at <see cref="HudTop"/>; the rails sit between it and the bottom row
    /// (<see cref="BottomRowH"/>); a selection card replaces the left rail (<see cref="ContextW"/> wide).
    /// </summary>
    public partial class GameUi
    {
        // MARK: - Style

        static readonly Color WoodLight = Theme.Hex("#E2B27A");
        static readonly Color WoodDark = Theme.Hex("#A86A3C");
        static readonly Color WoodEdge = Theme.Hex("#5E3A1E");
        static readonly Color GoldTop = Theme.Hex("#FFE07A");
        static readonly Color GoldBottom = Theme.Hex("#E79A26");
        static readonly Color GoldEdge = Theme.Hex("#7A4A12");
        static readonly Color BarDark = new Color(0.08f, 0.09f, 0.11f, 0.78f);
        static readonly Color Notify = Theme.Hex("#E5483B");

        // MARK: - Metrics (panel units)

        /// <summary>Distance from the top of the safe area to the rails (top row + gap).</summary>
        const float HudTop = 64;
        const float LeftRailW = 234;
        const float TimersW = 128;
        const float TileW = 54;
        /// <summary>Width of the selection / placement card docked on the left.</summary>
        const float ContextW = 320;
        /// <summary>Corner buttons (64) plus their labels hanging below (14).</summary>
        const float BottomRowH = 78;
        const float CornerButton = 64;
        const float SeasonCardH = 76;
        const float TimerCardH = 66;

        /// <summary>Bold white text with a dark outline (reads over bright and dark scenes alike).</summary>
        static Label Bold(string text, float size, Color? color = null)
        {
            var l = E.Text(text, size, color ?? Color.white, Theme.F.DisplayBold).NoWrap();
            l.style.unityTextOutlineWidth = 0.18f;
            l.style.unityTextOutlineColor = new Color(0.1f, 0.06f, 0.03f, 0.95f);
            return l;
        }

        /// <summary>A square wooden tile button with an icon (and an optional red count).</summary>
        VisualElement WoodTile(string icon, float size, Action onTap, string badge = null, string label = null)
        {
            var t = E.Div().Size(size, size).Radius(size * 0.22f).Center()
                .BgImage(Theme.Gradient("wood", true, WoodLight, WoodDark)).Border(WoodEdge, 2.5f);
            t.style.borderTopColor = Theme.Hex("#F6D3A0");
            t.Add(E.Icon(icon, size * 0.5f, Theme.Hex("#4A2C14")));
            if (label != null)
            {
                var l = Bold(label, 9.5f).Abs(0, null, 0, 3).Align(TextAnchor.MiddleCenter);
                t.Add(l);
            }
            if (badge != null) t.Add(RedBadge(badge).Abs(null, -6, -6));
            Tap(t, onTap);
            return t;
        }

        /// <summary>A round dark button (secondary actions on the left).</summary>
        VisualElement DarkRound(string icon, float size, Action onTap, string badge = null)
        {
            var b = E.Div().Size(size, size).Radius(size / 2).Center().Bg(new Color(0.1f, 0.11f, 0.13f, 0.82f))
                .Border(new Color(1, 1, 1, 0.22f), 2f);
            b.Add(E.Icon(icon, size * 0.46f, Color.white));
            if (badge != null) b.Add(RedBadge(badge).Abs(null, -4, -4));
            Tap(b, onTap);
            return b;
        }

        /// <summary>A big round gold button with a thick brown rim (the main corner actions).</summary>
        VisualElement GoldRound(string icon, float size, Action onTap, string label = null)
        {
            var rim = E.Div().Size(size, size).Radius(size / 2).Center().Bg(GoldEdge);
            var face = E.Div().Size(size - 8, size - 8).Radius((size - 8) / 2).Center().Ignore()
                .BgImage(Theme.Gradient("goldround", true, GoldTop, GoldBottom)).Border(Theme.Hex("#FFF2C2"), 2f);
            face.style.borderBottomColor = Theme.Hex("#B8701A");
            face.Add(E.Icon(icon, size * 0.46f, Theme.Hex("#5A3208")));
            rim.Add(face);
            if (label != null)
            {
                var l = Bold(label, 11f).Abs(0, null, 0, -14).Align(TextAnchor.MiddleCenter);
                rim.Add(l);
            }
            Tap(rim, onTap);
            return rim;
        }

        static VisualElement RedBadge(string text)
        {
            var b = E.Div().Ignore().Center().Radius(10).Bg(Notify).Border(Color.white, 1.5f).Pad(0, 5);
            b.style.minWidth = 20;
            b.style.height = 20;
            b.Add(Bold(text, 10.5f));
            return b;
        }

        static VisualElement RedDot() => E.Div().Ignore().Size(12, 12).Radius(6).Bg(Notify).Border(Color.white, 1.5f);

        // MARK: - Top row: badge + status bar + resources + settings

        static readonly ResourceType[] TopResources = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Ore, ResourceType.Knowledge };

        string TopKey() => $"{G.State.SettlementName}|{G.State.StageId}|{G.State.EpochId}|{G.State.Chapter}|{MilestoneReady()}|{TopDensity()}";

        /// <summary>Rough width of bold display text (digits and letters ~0.64 em, separators ~0.3 em); errs wide.</summary>
        static float TextWidth(string s, float size)
        {
            float em = 0;
            foreach (char c in s) em += c == ' ' || c == '·' || c == ':' || c == ',' || c == '.' || c == '/' ? 0.3f : 0.64f;
            return em * size;
        }

        /// <summary>
        /// How much the top row must be condensed to fit the safe width: 0 everything, 1 the clock without the
        /// day, 2 also smaller type, 3 also no clock. Re-evaluated every tick (cheap), so the row only rebuilds
        /// when the numbers grow past what fits.
        /// </summary>
        int TopDensity()
        {
            float usable = PanelW - SafeLeft - SafeRight - 8;
            for (int level = 0; level < 3; level++)
                if (TopRowWidth(level) <= usable) return level;
            return 3;
        }

        float TopRowWidth(int level)
        {
            float big = level >= 2 ? 13 : 15, small = level >= 2 ? 12 : 13;
            // Side padding 24, badge 50, gaps 8 + 6 + 8, gear 40.
            float w = 136;
            // Status bar: padding 24, people icon 17 + 5, the idle badge 24.
            w += 24 + 22 + TextWidth($"{G.State.Population}/{G.HousingCapacity()}", big) + 24 + SickChipWidth(big);
            // Day icon 18 + 6, the clock, the divider 20.
            if (level < 3) w += 24 + TextWidth(level == 0 ? GameClock() : ShortClock(), big) + 20;
            // Each chip: padding 13, tile 20, gap 5, margin 5.
            foreach (var t in TopResources) w += 43 + TextWidth(Fmt.Amount(G.State.Resources[t]), small);
            return w;
        }

        void BuildTop(VisualElement host)
        {
            int level = TopDensity();
            float big = level >= 2 ? 13 : 15, small = level >= 2 ? 12 : 13;
            var row = E.Div().Row().Pad(0, 12, 0, 12).Ignore();

            // Settlement badge: the hall of the current era, with the chapter's progress under it.
            var badge = E.Div().Col(Align.Center).Ignore().NoShrink();
            var hall = WoodTile(G.State.EpochId == "epoch_medieval_kingdoms" ? "landmark" : "home", 50, () => store.ActivePanel = Panel.Objectives);
            if (MilestoneReady() || G.PendingStory != null) hall.Add(RedDot().Abs(null, -4, -4));
            badge.Add(hall);
            var chapterBar = LiveBar(() =>
            {
                var req = G.ChapterRequirements();
                return req.Count == 0 ? 1 : req.Count(r => r.Met) / (double)req.Count;
            }, 6, null, Theme.Mint);
            chapterBar.style.width = 44;
            chapterBar.style.marginTop = -8;
            chapterBar.Border(WoodEdge, 1.5f);
            badge.Add(chapterBar);
            row.Add(badge);

            // Status bar: time of day and people.
            var bar = E.Div().Row().Radius(14).Bg(BarDark).Pad(7, 12).Border(new Color(1, 1, 1, 0.1f)).NoShrink();
            bar.style.marginLeft = 8;
            if (level < 3)
            {
                var dayIcon = E.Icon("summer", 18, Theme.Gold);
                Bind(() =>
                {
                    bool night = Atmosphere.Nightness > 0.3f;
                    dayIcon.style.backgroundImage = new StyleBackground(Theme.Icon(night ? "winter" : SeasonIcon(G.Season?.Id ?? "summer")));
                    dayIcon.style.unityBackgroundImageTintColor = night ? Theme.Hex("#B9A8FF") : SeasonTint(G.Season?.Id ?? "summer");
                });
                bar.Add(dayIcon);
                var clock = Bold("", big);
                clock.style.marginLeft = 6;
                Bind(() => clock.text = level == 0 ? GameClock() : ShortClock());
                bar.Add(clock);
                bar.Add(Divider());
            }
            bar.Add(E.Icon("people", 17, Color.white));
            var pop = Bold("", big);
            pop.style.marginLeft = 5;
            Bind(() => pop.text = $"{G.State.Population}/{G.HousingCapacity()}");
            bar.Add(pop);
            var idle = RedBadge("!");
            idle.style.marginLeft = 4;
            Bind(() => idle.style.display = G.IdleWorkers > 0 || G.State.FoodShortage ? DisplayStyle.Flex : DisplayStyle.None);
            bar.Add(idle);
            if (G.HasHealth) bar.Add(SickChip(big));
            Tap(bar, () => store.ActivePanel = Panel.People);
            row.Add(bar);

            // Resources in one line (tap one to see who makes it). Clipped rather than pushing the gear off
            // screen should the estimate above ever fall short.
            var res = E.Div().Row().Ignore();
            res.style.marginLeft = 6;
            res.style.flexShrink = 1;
            res.style.minWidth = 0;
            res.style.overflow = Overflow.Hidden;
            foreach (var t in TopResources) res.Add(ResourceChip(t, small));
            row.Add(res);

            row.Add(E.Div().Grow().Ignore());
            var gear = DarkRound("settings", 40, () => store.ActivePanel = Panel.Settings);
            gear.style.marginLeft = 8;
            row.Add(gear);
            host.Add(row);
        }

        static VisualElement Divider()
        {
            var d = E.Div().Size(2, 18).Bg(new Color(1, 1, 1, 0.22f)).Ignore();
            d.style.marginLeft = 9;
            d.style.marginRight = 9;
            return d;
        }

        VisualElement ResourceChip(ResourceType type, float size)
        {
            var c = E.Div().Row().Radius(11).Bg(BarDark).Pad(4, 8, 4, 5).NoShrink();
            c.style.marginRight = 5;
            var tile = E.Div().Size(20, 20).Radius(10).Center().Bg(Theme.Alpha(TintFor(type), 0.28f)).Ignore();
            tile.Add(E.Icon(IconFor(type), 13, Color.Lerp(TintFor(type), Color.white, 0.35f)));
            c.Add(tile);
            var v = Bold("", size);
            v.style.marginLeft = 5;
            Bind(() =>
            {
                double x = G.State.Resources[type];
                v.text = Fmt.Amount(x);
                v.style.color = x >= caps[type] - 0.5 ? Theme.Gold : econ.Net[type] < -0.0005 && x < 20 ? Theme.Hex("#FF8A7A") : Color.white;
            });
            c.Add(v);
            Tap(c, () => store.ActivePanel = type == ResourceType.Knowledge ? Panel.Research : Panel.People);
            return c;
        }

        /// <summary>Day and hour of the game clock (one day lasts six real hours).</summary>
        string GameClock() => $"Day {(int)(G.State.SimTime / Atmosphere.DayLength) + 1} · {ShortClock()}";

        /// <summary>The hour alone (used when the top row is short of room).</summary>
        string ShortClock()
        {
            double t = G.State.SimTime / Atmosphere.DayLength;
            // Daylight is the first 75% of the cycle: map it to 06:00–21:00, the night to 21:00–06:00.
            double p = t - Math.Floor(t);
            double hours = p < 0.75 ? 6 + p / 0.75 * 15 : 21 + (p - 0.75) / 0.25 * 9;
            hours %= 24;
            return $"{(int)hours:00}:{(int)(hours % 1 * 60):00}";
        }

        // MARK: - Side rails

        /// <summary>How many timer cards fit under the season card between the top row and the corner buttons.</summary>
        int MaxTimers()
        {
            float room = PanelH - SafeTop - 6 - HudTop - BottomPad - BottomRowH - 8 - SeasonCardH;
            return Mathf.Clamp(Mathf.FloorToInt(room / TimerCardH), 0, 6);
        }

        string SidesKey()
        {
            var r = G.State.ActiveResearch;
            var e = G.State.ActiveExpedition;
            var builds = string.Join(",", G.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).Select(b => b.Id));
            bool ev = G.PendingEvent != null && store.EventMinimized;
            return $"{G.CurrentObjective?.Id}|{ev}|{G.IdleWorkers > 0}|{MilestoneReady()}|{r?.TechId}|{e?.RegionId}|{builds}|{G.SeasonIndex}|{G.Freezing}|" +
                   $"{ContextOpen}|{MaxTimers()}|{G.State.ActiveResearch == null && G.Content.Technologies.Any(t => G.GetResearchStatus(t.Id).State == ResearchState.Available)}|" +
                   $"{G.State.ActivePetition?.PetitionId}|{LawReady()}|{TileSize()}|{G.HasClimate}|{G.HasDiscontent}";
        }

        void BuildLeftSide(VisualElement host)
        {
            // A selected building, resource or placement shows its card on this side instead.
            if (ContextOpen) return;

            // The current goal as a parchment card.
            var o = G.CurrentObjective;
            if (o != null)
            {
                var card = E.Div().Col().Radius(14).Pad(8, 10).BgImage(Theme.Gradient("parchment", true, Theme.Hex("#F7E7C4"), Theme.Hex("#E7CC98")))
                    .Border(WoodEdge, 2f);
                card.style.width = LeftRailW;
                card.style.marginBottom = 8;
                var head = E.Div().Row().Ignore();
                head.Add(E.Icon("goals", 14, Theme.Hex("#8A4E1E")));
                var t = E.Text(o.Title, 12.5f, Theme.Hex("#3A220E"), Theme.F.TextBold).Grow();
                t.style.marginLeft = 6;
                t.style.whiteSpace = WhiteSpace.Normal;
                head.Add(t);
                card.Add(head);
                var bar = LiveBar(() =>
                {
                    var st = G.Status(o.Conditions);
                    return st.Count == 0 ? 0 : st.Count(s => s.Met) / (double)st.Count;
                }, 5, null, Theme.Hex("#6FB24A"));
                bar.style.marginTop = 6;
                card.Add(bar);
                Tap(card, () => store.ActivePanel = Panel.Objectives);
                host.Add(card);
            }

            // A promise made to the people, with the time left to keep it.
            var promise = PromiseCard();
            if (promise != null) host.Add(promise);

            // Round buttons in a row under the goal (a column would run into the compass).
            var row = E.Div().Row().Ignore();
            void Round(VisualElement b) { b.style.marginRight = 6; row.Add(b); }
            if (G.PendingEvent != null && store.EventMinimized)
                Round(DarkRound("bell", 42, () => store.EventMinimized = false, "1"));
            Round(DarkRound("world", 42, () => store.ActivePanel = Panel.Map));
            Round(DarkRound("legacy", 42, () => store.ActivePanel = Panel.Profile));
            Round(DarkRound("eye_off", 42, () =>
            {
                store.CancelPlacement();
                store.SelectedBuildingId = null;
                store.SelectedNode = null;
                store.ActivePanel = Panel.None;
                store.HudHidden = true;
            }));
            host.Add(row);
        }

        void BuildRightSide(VisualElement host)
        {
            // Timers next to the tiles; folded away while a card is open so the scene stays in view.
            if (!ContextOpen)
            {
                var timers = E.Div().Col().Ignore().Size(TimersW);
                timers.style.marginRight = 8;
                timers.Add(SeasonCard());
                var cards = new List<VisualElement>();
                var r = G.State.ActiveResearch;
                if (r != null)
                {
                    var tech = G.Content.Technology(r.TechId);
                    cards.Add(TimerCard("knowledge", "Research", Theme.Lavender, tech?.Name ?? r.TechId,
                        () => 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds), () => Fmt.Clock(r.RemainingSeconds),
                        Theme.LavenderFill, () => store.ActivePanel = Panel.Research));
                }
                var e = G.State.ActiveExpedition;
                if (e != null)
                {
                    var region = G.Content.Regions.FirstOrDefault(x => x.Id == e.RegionId);
                    cards.Add(TimerCard("compass", "Scouting", Theme.Mint, region?.Name ?? e.RegionId,
                        () => 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds), () => Fmt.Clock(e.RemainingSeconds),
                        Theme.MintFill, () => store.ActivePanel = Panel.Map));
                }
                foreach (var b in G.State.Buildings.Where(x => x.Status != BuildingStatus.Complete).Take(3))
                {
                    var def = G.Content.Building(b.Status == BuildingStatus.Upgrading && b.UpgradeTargetId != null ? b.UpgradeTargetId : b.DefinitionId);
                    int id = b.Id;
                    cards.Add(TimerCard("build", b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Building", Theme.Gold, def?.Name ?? b.DefinitionId,
                        () => G.State.Building(id)?.Progress ?? 1, () => Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0)),
                        Theme.GoldFill, () => store.FocusBuilding(id)));
                }
                foreach (var c in cards.Take(MaxTimers())) timers.Add(c);
                host.Add(timers);
            }

            // Wooden tiles, sized so all of them fit above the Build button (four with the Book of Customs).
            var col = E.Div().Col(Align.FlexEnd).Ignore();
            col.style.width = TileW;
            float size = TileSize(), gap = TileGap();
            VisualElement Gap(VisualElement e) { e.style.marginBottom = gap; return e; }
            string goals = MilestoneReady() ? "!" : null;
            col.Add(Gap(WoodTile("goals", size, () => store.ActivePanel = Panel.Objectives, goals, "Goals")));
            string idle = G.IdleWorkers > 0 ? G.IdleWorkers.ToString(CultureInfo.InvariantCulture) : null;
            col.Add(Gap(WoodTile("people", size, () => store.ActivePanel = Panel.People, idle, "People")));
            col.Add(Gap(WoodTile("knowledge", size, () => store.ActivePanel = Panel.Research,
                G.State.ActiveResearch == null && G.Content.Technologies.Any(t => G.GetResearchStatus(t.Id).State == ResearchState.Available) ? "!" : null, "Research")));
            if (HasCustoms)
                col.Add(Gap(WoodTile("customs", size, () => store.ActivePanel = Panel.Customs, LawReady() ? "!" : null, "Customs")));
            host.Add(col);
        }

        // MARK: - Bottom corners and the news strip

        string BottomKey() => $"{store.ActivePanel}|{ContextOpen}";

        void BuildBottom(VisualElement host)
        {
            bool card = ContextOpen;
            // Bottom padding leaves room for the labels under the round buttons; with a card open the strip
            // starts right of it (the compass makes way).
            var row = E.Div().Row(0, Align.FlexEnd).Pad(0, 12, 14, card ? ContextW + 20 : 12).Ignore();

            if (!card)
            {
                // The compass needle follows the camera; tap it to face north-west again.
                var compass = GoldRound("compass", CornerButton, () => store.Rig?.ResetView(), "View");
                Bind(() =>
                {
                    var face = compass.ElementAt(0);
                    face.style.rotate = new Rotate(new Angle(-(CameraRig.Yaw + 45f), AngleUnit.Degree));
                });
                row.Add(compass);
            }

            // News: the latest thing that happened in the settlement, centred between the corners.
            var middle = E.Div().Row(0, Align.FlexEnd).Ignore();
            middle.style.flexGrow = 1;
            middle.style.flexShrink = 1;
            middle.style.flexBasis = 0;
            middle.style.minWidth = 0;
            middle.style.justifyContent = Justify.Center;
            middle.style.marginLeft = 10;
            middle.style.marginRight = 10;
            var news = E.Div().Col().Grow().Radius(12).Bg(BarDark).Pad(7, 10).Ignore();
            news.style.maxWidth = 440;
            news.style.marginBottom = 4;
            var line1 = E.Text("", 11.5f, Theme.GoldPale, Theme.F.TextBold).NoWrap();
            var line2 = E.Text("", 11.5f, Color.white, Theme.F.TextMedium).NoWrap();
            line2.style.marginTop = 2;
            Bind(() =>
            {
                var last = G.State.Legacy.LastOrDefault();
                line1.text = $"[{G.Season?.Name ?? "Year"} Y{G.Year}] {G.CurrentChapter?.Title ?? G.StageName}";
                line2.text = store.Toasts.Count > 0 ? store.Toasts[store.Toasts.Count - 1].Notice.Message : last != null ? last.Title : "";
            });
            news.Add(line1);
            news.Add(line2);
            middle.Add(news);
            row.Add(middle);

            row.Add(GoldRound("build", CornerButton, () =>
            {
                store.CancelPlacement();
                store.ActivePanel = store.ActivePanel == Panel.Build ? Panel.None : Panel.Build;
            }, "Build"));
            host.Add(row);
        }
    }
}
