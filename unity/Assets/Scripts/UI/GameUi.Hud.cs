using System;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The in-game HUD in the style of mobile survival builders: a wooden settlement badge and a
    /// dark status bar (time, people, resources) along the top, columns of chunky square and round
    /// buttons down the sides, a gold compass and a gold build button in the bottom corners and a
    /// news strip between them. Text is bold with a dark outline so it reads over any scene.
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

        /// <summary>A round dark button (secondary actions down the left side).</summary>
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

        // MARK: - Top: badge + status bar + resources

        string TopKey() => $"{G.State.SettlementName}|{G.State.StageId}|{G.State.EpochId}|{G.State.Chapter}|{MilestoneReady()}";

        void BuildTop(VisualElement host)
        {
            var row = E.Div().Row(0, Align.FlexStart).Pad(4, 12, 0, 12).Ignore();

            // Settlement badge: the hall of the current era, with the chapter's progress under it.
            var badge = E.Div().Col(Align.Center).Ignore();
            var hall = WoodTile(G.State.EpochId == "epoch_medieval_kingdoms" ? "landmark" : "home", 60, () => store.ActivePanel = Panel.Objectives);
            if (MilestoneReady() || G.PendingStory != null) hall.Add(RedDot().Abs(null, -4, -4));
            badge.Add(hall);
            var chapterBar = LiveBar(() =>
            {
                var req = G.ChapterRequirements();
                return req.Count == 0 ? 1 : req.Count(r => r.Met) / (double)req.Count;
            }, 6, null, Theme.Mint);
            chapterBar.style.width = 54;
            chapterBar.style.marginTop = -8;
            chapterBar.Border(WoodEdge, 1.5f);
            badge.Add(chapterBar);
            row.Add(badge);

            var right = E.Div().Col().Grow().Ignore();
            right.style.marginLeft = 8;

            // Status bar: time of day, people, settlement name.
            var bar = E.Div().Row().Radius(14).Bg(BarDark).Pad(7, 12).Border(new Color(1, 1, 1, 0.1f));
            var dayIcon = E.Icon("summer", 18, Theme.Gold);
            Bind(() =>
            {
                bool night = Atmosphere.Nightness > 0.3f;
                dayIcon.style.backgroundImage = new StyleBackground(Theme.Icon(night ? "winter" : SeasonIcon(G.Season?.Id ?? "summer")));
                dayIcon.style.unityBackgroundImageTintColor = night ? Theme.Hex("#B9A8FF") : SeasonTint(G.Season?.Id ?? "summer");
            });
            bar.Add(dayIcon);
            var clock = Bold("", 15);
            clock.style.marginLeft = 6;
            Bind(() => clock.text = GameClock());
            bar.Add(clock);
            bar.Add(Divider());
            bar.Add(E.Icon("people", 17, Color.white));
            var pop = Bold("", 15);
            pop.style.marginLeft = 5;
            Bind(() => pop.text = $"{G.State.Population}/{G.HousingCapacity()}");
            bar.Add(pop);
            var idle = RedBadge("!");
            idle.style.marginLeft = 4;
            Bind(() => idle.style.display = G.IdleWorkers > 0 || G.State.FoodShortage ? DisplayStyle.Flex : DisplayStyle.None);
            bar.Add(idle);
            Tap(bar, () => store.ActivePanel = Panel.People);
            right.Add(bar);

            // Resources: compact chips (tap one to see who makes it).
            var res = E.Div().Row().Margin(6).Ignore();
            res.style.flexWrap = Wrap.Wrap;
            foreach (var t in new[] { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Ore, ResourceType.Knowledge })
                res.Add(ResourceChip(t));
            right.Add(res);
            row.Add(right);

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

        VisualElement ResourceChip(ResourceType type)
        {
            var c = E.Div().Row().Radius(11).Bg(BarDark).Pad(4, 8, 4, 5);
            c.style.marginRight = 5;
            c.style.marginBottom = 4;
            var tile = E.Div().Size(20, 20).Radius(10).Center().Bg(Theme.Alpha(TintFor(type), 0.28f)).Ignore();
            tile.Add(E.Icon(IconFor(type), 13, Color.Lerp(TintFor(type), Color.white, 0.35f)));
            c.Add(tile);
            var v = Bold("", 13);
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

        /// <summary>Day and hour of the game clock (one day is a few real minutes).</summary>
        string GameClock()
        {
            double t = G.State.SimTime / Atmosphere.DayLength;
            int day = (int)t + 1;
            // Daylight is the first 75% of the cycle: map it to 06:00–21:00, the night to 21:00–06:00.
            double p = t - Math.Floor(t);
            double hours = p < 0.75 ? 6 + p / 0.75 * 15 : 21 + (p - 0.75) / 0.25 * 9;
            hours %= 24;
            return $"Day {day} · {(int)hours:00}:{(int)(hours % 1 * 60):00}";
        }

        // MARK: - Side columns

        string SidesKey()
        {
            var r = G.State.ActiveResearch;
            var e = G.State.ActiveExpedition;
            var builds = string.Join(",", G.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).Select(b => b.Id));
            bool ev = G.PendingEvent != null && store.EventMinimized;
            return $"{G.CurrentObjective?.Id}|{ev}|{G.IdleWorkers > 0}|{MilestoneReady()}|{r?.TechId}|{e?.RegionId}|{builds}|{G.SeasonIndex}|{G.Freezing}";
        }

        void BuildLeftSide(VisualElement host)
        {
            // The current goal as a parchment card.
            var o = G.CurrentObjective;
            if (o != null)
            {
                var card = E.Div().Col().Radius(14).Pad(8, 10).BgImage(Theme.Gradient("parchment", true, Theme.Hex("#F7E7C4"), Theme.Hex("#E7CC98")))
                    .Border(WoodEdge, 2f);
                card.style.width = 190;
                card.style.marginBottom = 10;
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
            var col = E.Div().Col(Align.FlexStart).Ignore();
            if (G.PendingEvent != null && store.EventMinimized)
                col.Add(Spaced(DarkRound("bell", 48, () => store.EventMinimized = false, "1")));
            col.Add(Spaced(DarkRound("world", 48, () => store.ActivePanel = Panel.Map)));
            col.Add(Spaced(DarkRound("legacy", 48, () => store.ActivePanel = Panel.Profile)));
            col.Add(Spaced(DarkRound("eye_off", 48, () =>
            {
                store.CancelPlacement();
                store.SelectedBuildingId = null;
                store.SelectedNode = null;
                store.ActivePanel = Panel.None;
                store.HudHidden = true;
            })));
            host.Add(col);
        }

        static VisualElement Spaced(VisualElement e)
        {
            e.style.marginBottom = 10;
            return e;
        }

        void BuildRightSide(VisualElement host)
        {
            var col = E.Div().Col(Align.FlexEnd).Ignore();
            string goals = MilestoneReady() ? "!" : null;
            col.Add(Spaced(WoodTile("goals", 54, () => store.ActivePanel = Panel.Objectives, goals, "Goals")));
            string idle = G.IdleWorkers > 0 ? G.IdleWorkers.ToString(CultureInfo.InvariantCulture) : null;
            col.Add(Spaced(WoodTile("people", 54, () => store.ActivePanel = Panel.People, idle, "People")));
            col.Add(Spaced(WoodTile("knowledge", 54, () => store.ActivePanel = Panel.Research,
                G.State.ActiveResearch == null && G.Content.Technologies.Any(t => G.GetResearchStatus(t.Id).State == ResearchState.Available) ? "!" : null, "Research")));
            host.Add(col);

            var timers = E.Div().Col().Ignore();
            timers.style.width = 128;
            timers.Add(SeasonCard());
            var r = G.State.ActiveResearch;
            if (r != null)
            {
                var tech = G.Content.Technology(r.TechId);
                timers.Add(TimerCard("knowledge", "Research", Theme.Lavender, tech?.Name ?? r.TechId,
                    () => 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds), () => $"{Fmt.Clock(r.RemainingSeconds)} left",
                    Theme.LavenderFill, () => store.ActivePanel = Panel.Research));
            }
            foreach (var b in G.State.Buildings.Where(x => x.Status != BuildingStatus.Complete).Take(2))
            {
                var def = G.Content.Building(b.Status == BuildingStatus.Upgrading && b.UpgradeTargetId != null ? b.UpgradeTargetId : b.DefinitionId);
                int id = b.Id;
                timers.Add(TimerCard("build", b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Building", Theme.Gold, def?.Name ?? b.DefinitionId,
                    () => G.State.Building(id)?.Progress ?? 1, () => $"{Fmt.Clock(G.EstimatedSeconds(G.State.Building(id)?.RemainingWork ?? 0))} left",
                    Theme.GoldFill, () => store.FocusBuilding(id)));
            }
            var e = G.State.ActiveExpedition;
            if (e != null)
            {
                var region = G.Content.Regions.FirstOrDefault(x => x.Id == e.RegionId);
                timers.Add(TimerCard("compass", "Scouting", Theme.Mint, region?.Name ?? e.RegionId,
                    () => 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds), () => $"{Fmt.Clock(e.RemainingSeconds)} left",
                    Theme.MintFill, () => store.ActivePanel = Panel.Map));
            }
            host.Add(timers);
        }

        // MARK: - Bottom corners and the news strip

        string BottomKey() => $"{store.ActivePanel}|{store.Toasts.Count}|{store.PlacingBuildingId != null || store.DecoratingId != null}";

        void BuildBottom(VisualElement host)
        {
            var row = E.Div().Row(0, Align.FlexEnd).Pad(0, 12, 6, 12).Ignore();

            // The compass needle follows the camera; tap it to face north-west again.
            var compass = GoldRound("compass", 74, () => store.Rig?.ResetView(), "View");
            Bind(() =>
            {
                var face = compass.ElementAt(0);
                face.style.rotate = new Rotate(new Angle(-(CameraRig.Yaw + 45f), AngleUnit.Degree));
            });
            row.Add(compass);

            // News: the latest thing that happened in the settlement.
            var news = E.Div().Col().Grow().Radius(12).Bg(BarDark).Pad(7, 10).Ignore();
            news.style.marginLeft = 10;
            news.style.marginRight = 10;
            news.style.marginBottom = 6;
            var line1 = E.Text("", 11.5f, Theme.GoldPale, Theme.F.TextBold).NoWrap();
            var line2 = E.Text("", 11.5f, Color.white, Theme.F.TextMedium).NoWrap();
            foreach (var l in new[] { line1, line2 }) { l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis; }
            line2.style.marginTop = 2;
            Bind(() =>
            {
                var last = G.State.Legacy.LastOrDefault();
                line1.text = $"[{G.Season?.Name ?? "Year"} Y{G.Year}] {G.CurrentChapter?.Title ?? G.StageName}";
                line2.text = store.Toasts.Count > 0 ? store.Toasts[store.Toasts.Count - 1].Notice.Message : last != null ? last.Title : "";
            });
            news.Add(line1);
            news.Add(line2);
            row.Add(news);

            row.Add(GoldRound("build", 74, () =>
            {
                store.CancelPlacement();
                store.ActivePanel = store.ActivePanel == Panel.Build ? Panel.None : Panel.Build;
            }, "Build"));
            host.Add(row);
        }
    }
}
