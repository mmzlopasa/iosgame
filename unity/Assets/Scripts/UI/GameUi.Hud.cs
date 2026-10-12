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

        /// <summary>Top of the rails (goal card, timers, menu tiles) below the safe top.</summary>
        const float RailY = 74;
        const float StripH = 40;
        const float AvatarSize = 40;
        const float LeftRailW = 206;
        const float TimersW = 142;
        const float TileSize = 46;
        const float TileGap = 6;
        const float BuildButton = 58;
        /// <summary>Width of the docked card (selection, placement), at most 45% of the screen.</summary>
        float CardW => Mathf.Min(330, PanelW * 0.45f - 8);
        /// <summary>Top of popups (below the top strip).</summary>
        float PopupTop => SafeTop + 50;

        VisualElement topHost, chipsHost, leftHost, timersHost, tilesHost, bottomHost, toastLayer, contextPanel;

        void BuildHud()
        {
            topHost = E.Div("top").Ignore().Abs();
            AddRegion(topHost, TopKey, BuildTop);
            chipsHost = E.Div("chips").Ignore().Abs();
            AddRegion(chipsHost, ChipsKey, BuildChips);
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

            foreach (var e in new[] { chipsHost, leftHost, timersHost, tilesHost, bottomHost, toastLayer, contextPanel, topHost })
                hudLayer.Add(e);
            lastSafe = default;
        }

        /// <summary>Places the HUD pieces inside the safe area and hides what a popup covers (every tick; cheap).</summary>
        void LayoutHud()
        {
            bool sheet = SheetOpen, card = ContextOpen;
            float l = SafeLeft + 8, r = SafeRight + 8, top = SafeTop;
            Place(topHost, l, top + 6, r, null);
            Place(chipsHost, l + AvatarSize + 6, top + 37, null, null);
            Place(leftHost, l, top + RailY, null, null);
            Place(timersHost, null, top + RailY, r + TileSize + 8, null);
            Place(tilesHost, null, top + RailY, r, null);
            Place(bottomHost, l, null, SafeRight + 6, BottomPad + 4);
            chipsHost.Hidden(sheet || card);
            leftHost.Hidden(sheet || card);
            timersHost.Hidden(sheet || card);
            tilesHost.Hidden(sheet);
            bottomHost.Hidden(sheet);
            // Notices between whatever occupies the two sides.
            float leftEdge = l + (card ? CardW : LeftRailW) + 10;
            float rightEdge = r + TileSize + 8 + (card ? 0 : TimersW + 8);
            Place(toastLayer, leftEdge, top + RailY, rightEdge, BottomPad + BuildButton + 10);
            contextPanel.style.width = CardW;
            Place(contextPanel, l, PopupTop, null, BottomPad + 8);
            float maxCard = Mathf.Min(PanelH - PopupTop - BottomPad - 8, 430);
            contextPanel.style.maxHeight = maxCard;
        }

        static void Place(VisualElement e, float? left, float? top, float? right, float? bottom)
        {
            if (left.HasValue && !Mathf.Approximately(e.style.left.value.value, left.Value)) e.style.left = left.Value;
            if (top.HasValue && !Mathf.Approximately(e.style.top.value.value, top.Value)) e.style.top = top.Value;
            if (right.HasValue && !Mathf.Approximately(e.style.right.value.value, right.Value)) e.style.right = right.Value;
            if (bottom.HasValue && !Mathf.Approximately(e.style.bottom.value.value, bottom.Value)) e.style.bottom = bottom.Value;
        }

        // MARK: - Shared look-ups

        internal static string IconFor(ResourceType t) => t switch
        {
            ResourceType.Food => "food",
            ResourceType.Wood => "wood",
            ResourceType.Stone => "stone",
            ResourceType.Ore => "ore",
            ResourceType.Hides => "hides",
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
        static VisualElement CornerBadge(string text, float size = 16) => E.Badge(text, true, size).Abs(null, -5, -5);

        // MARK: - Top strip

        static readonly ResourceType[] StripOrder = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge, ResourceType.Ore, ResourceType.Hides };

        /// <summary>Which resources the strip shows at a density: all; without hides; without ore and hides.</summary>
        IEnumerable<ResourceType> StripResources(int density)
        {
            foreach (var t in StripOrder)
            {
                if (t == ResourceType.Hides && (density >= 1 || G.State.Resources.Hides < 1)) continue;
                if (t == ResourceType.Ore && (density >= 2 || G.State.Resources.Ore < 1)) continue;
                yield return t;
            }
        }

        string TopKey() => $"{SafeWidth}|{G.State.EpochId}|{MilestoneReady() || G.PendingStory != null}|{TopDensity()}|{G.State.Resources.Ore >= 1}|{G.State.Resources.Hides >= 1}";

        /// <summary>
        /// How much the strip must give up to fit the safe width: 0 everything, 1 no hides, 2 no ore, 3 no clock,
        /// 4 smaller numbers. Re-evaluated every tick (cheap): the strip only rebuilds when this changes.
        /// </summary>
        int TopDensity()
        {
            // Avatar 40 + 6, capsule slices 18, settings 30 + 8, margins 16.
            float usable = SafeWidth - 118;
            for (int level = 0; level < 4; level++)
                if (StripWidth(level, 13) <= usable) return level;
            return 4;
        }

        float StripWidth(int density, float size)
        {
            const float item = 18 + 3, gap = 8;
            float w = item + Theme.TextWidth($"{Fmt.Compact(G.State.Population)}/{Fmt.Compact(G.HousingCapacity())}", size) + 17;
            if (density < 3) w += gap + item + Theme.TextWidth("00:00", size);
            foreach (var t in StripResources(density)) w += gap + item + Theme.TextWidth(Fmt.Compact(G.State.Resources[t]) + "0", size);
            return w;
        }

        void BuildTop(VisualElement host)
        {
            int density = TopDensity();
            float size = density >= 4 ? 12 : 13;
            var row = E.Div().Row(0, Align.FlexStart).Ignore();
            row.style.width = SafeWidth - 16;

            // Settlement tile with the chapter's progress under it (tap: Goals).
            // The founder's portrait, as the kit's player avatar.
            var avatar = E.Slot("face_m1", AvatarSize, E.Cell.Tile, 0.92f);
            var chapter = LiveBar(() =>
            {
                var req = G.ChapterRequirements();
                return req.Count == 0 ? 1 : req.Count(r => r.Met) / (double)req.Count;
            }, 7).Abs(3, null, 3, -4);
            avatar.Add(chapter);
            if (MilestoneReady() || G.PendingStory != null) avatar.Add(CornerBadge("!", 14));
            Tap(avatar, () => store.ActivePanel = Panel.Objectives);
            row.Add(avatar);

            // The capsule: the hour, people, resources (tap people: People; a resource: who makes it).
            var cap = E.Capsule(28);
            cap.style.marginLeft = 6;
            cap.style.marginTop = 3;
            if (density < 3)
            {
                var hour = StripItem(cap, "summer", size);
                var clockIcon = hour.ElementAt(0);
                var clock = (Label)hour.ElementAt(1);
                bool night = false;
                Bind(() =>
                {
                    bool n = Atmosphere.Nightness > 0.3f;
                    if (n != night || clock.text == "") { night = n; E.SetIcon(clockIcon, n ? "moon" : SeasonIcon(G.Season?.Id ?? "summer")); }
                    string t = ShortClock();
                    if (clock.text != t) clock.text = t;
                });
                Tap(hour, () => store.ActivePanel = G.HasClimate ? Panel.Weather : Panel.People);
            }
            var people = StripItem(cap, "people", size);
            var pop = (Label)people.ElementAt(1);
            var idle = E.Badge("!", true, 14);
            idle.style.marginLeft = 3;
            people.Add(idle);
            Bind(() =>
            {
                string s = $"{Fmt.Compact(G.State.Population)}/{Fmt.Compact(G.HousingCapacity())}";
                if (pop.text != s) pop.text = s;
                idle.Hidden(!(G.IdleWorkers > 0 || G.State.FoodShortage));
            });
            Tap(people, () => { peopleTab = 0; store.ActivePanel = Panel.People; });
            foreach (var t in StripResources(density))
            {
                var item = StripItem(cap, IconFor(t), size);
                var v = (Label)item.ElementAt(1);
                var type = t;
                Bind(() =>
                {
                    double x = G.State.Resources[type];
                    string s = Fmt.Compact(x);
                    if (v.text != s) v.text = s;
                    E.Tint(v, x >= caps[type] - 0.5 ? Theme.Gold : econ.Net[type] < -0.0005 && x < 20 ? Theme.Bad : Color.white);
                });
                Tap(item, () => { if (type == ResourceType.Knowledge) store.ActivePanel = Panel.Research; else { peopleTab = 0; store.ActivePanel = Panel.People; } });
            }
            row.Add(cap);
            row.Add(E.Div().Grow().Ignore());
            var gear = E.Square("settings", Act(() => store.ActivePanel = Panel.Settings), E.Kind.Brown, 30);
            gear.style.marginTop = 2;
            row.Add(gear);
            host.Add(row);
        }

        /// <summary>An icon and an outlined number inside the strip capsule.</summary>
        static VisualElement StripItem(VisualElement capsule, string icon, float size)
        {
            var item = E.Div().Row();
            if (capsule.childCount > 0) item.style.marginLeft = 8;
            item.Add(E.Icon(icon, 18));
            var l = E.T("", size);
            l.style.marginLeft = 3;
            item.Add(l);
            item.pickingMode = PickingMode.Position;
            capsule.Add(item);
            return item;
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

        int Day => (int)(G.State.SimTime / Atmosphere.DayLength) + 1;

        // MARK: - Second line: season, temperature, hope, discontent, the sick

        string ChipsKey() => $"{G.Season?.Id}|{G.HasClimate}|{G.HasDiscontent}|{G.HasHealth}|{G.Freezing}";

        void BuildChips(VisualElement host)
        {
            var cap = E.Capsule(21).Pad(0, 7);
            var season = G.Season;
            if (season != null)
            {
                var s = Chip(cap, G.Freezing ? "alert" : SeasonIcon(season.Id));
                Bind(() =>
                {
                    string t = G.Freezing ? "Freezing!" : $"{season.Name} · Day {Day}";
                    var l = (Label)s.ElementAt(1);
                    if (l.text != t) l.text = t;
                    E.Tint(l, G.Freezing ? Theme.Bad : Color.white);
                });
                Tap(s, () => store.ActivePanel = G.HasClimate ? Panel.Weather : Panel.People);
            }
            if (G.HasClimate)
            {
                var th = Chip(cap, "thermometer");
                var icon = th.ElementAt(0);
                int lastSnap = -1;
                Bind(() =>
                {
                    double temp = G.Temperature;
                    bool snap = CurrentSnap() != null;
                    var l = (Label)th.ElementAt(1);
                    string s = Deg(temp);
                    if (l.text != s) l.text = s;
                    E.Tint(l, snap ? Theme.Icy : ExposureColor(G.ExposureOf(temp)));
                    if ((snap ? 1 : 0) != lastSnap) { lastSnap = snap ? 1 : 0; E.SetIcon(icon, snap ? "winter" : "thermometer"); }
                });
                Tap(th, () => store.ActivePanel = Panel.Weather);
            }
            var hope = Chip(cap, "approval");
            Bind(() =>
            {
                int a = ApprovalNow;
                var l = (Label)hope.ElementAt(1);
                string s = a.ToString(Inv);
                if (l.text != s) l.text = s;
                E.Tint(l, ApprovalColor(a));
            });
            Tap(hope, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
            if (G.HasDiscontent)
            {
                var dis = Chip(cap, "discontent");
                Bind(() =>
                {
                    var l = (Label)dis.ElementAt(1);
                    string s = ((int)Math.Round(G.State.Discontent)).ToString(Inv);
                    if (l.text != s) l.text = s;
                    E.Tint(l, MoodColor(G.DiscontentMood));
                });
                Tap(dis, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
            }
            if (G.HasHealth)
            {
                var sick = Chip(cap, "sick");
                Bind(() =>
                {
                    int n = G.SickCount;
                    sick.Hidden(n == 0);
                    var l = (Label)sick.ElementAt(1);
                    string s = n.ToString(Inv);
                    if (l.text != s) l.text = s;
                    E.Tint(l, Theme.Hex("#FFB3A8"));
                });
                Tap(sick, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
            }
            host.Add(cap);
        }

        static VisualElement Chip(VisualElement capsule, string icon)
        {
            var c = E.Div().Row();
            if (capsule.childCount > 0) c.style.marginLeft = 8;
            c.Add(E.Icon(icon, 15));
            var l = E.T("", 11);
            l.style.marginLeft = 3;
            c.Add(l);
            c.pickingMode = PickingMode.Position;
            capsule.Add(c);
            return c;
        }

        // MARK: - Left rail: the goal and a promise

        string LeftKey() => $"{G.CurrentObjective?.Id}|{G.State.ActivePetition?.PetitionId}";

        void BuildLeft(VisualElement host)
        {
            var o = G.CurrentObjective;
            if (o != null)
            {
                var card = E.Panel().Pad(5, 9, 9, 9);
                card.style.width = LeftRailW;
                card.style.marginBottom = 6;
                var head = E.Div().Row().Ignore();
                head.Add(E.Icon("goals", 18));
                var t = E.T(o.Title, 11.5f).Wrap().Grow();
                t.style.marginLeft = 5;
                t.style.maxHeight = 30;
                head.Add(t);
                card.Add(head);
                var bar = LiveBar(() =>
                {
                    var st = G.Status(o.Conditions);
                    return st.Count == 0 ? 0 : st.Count(s => s.Met) / (double)st.Count;
                }, 8);
                bar.style.marginTop = 4;
                card.Add(bar);
                Tap(card, () => { goalsTab = 2; store.ActivePanel = Panel.Objectives; });
                host.Add(card);
            }
            var promise = PromiseCard();
            if (promise != null) host.Add(promise);
        }

        // MARK: - Timers (research, scouting, construction)

        int MaxTimers() => Mathf.Clamp(Mathf.FloorToInt((PanelH - SafeTop - RailY - BottomPad - 12) / 39f), 0, 5);

        string TimersKey()
        {
            var builds = string.Join(",", G.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).Select(b => b.Id));
            return $"{G.State.ActiveResearch?.TechId}|{G.State.ActiveExpedition?.RegionId}|{builds}|{MaxTimers()}|{G.IsStoked}";
        }

        void BuildTimers(VisualElement host)
        {
            var cards = new List<VisualElement>();
            var r = G.State.ActiveResearch;
            if (r != null)
            {
                var tech = G.Content.Technology(r.TechId);
                cards.Add(TimerCard("knowledge", tech?.Name ?? r.TechId, () => 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds),
                    () => Fmt.Clock(r.RemainingSeconds), E.FillKind.Blue, () => store.ActivePanel = Panel.Research));
            }
            var e = G.State.ActiveExpedition;
            if (e != null)
            {
                var region = G.Content.Regions.FirstOrDefault(x => x.Id == e.RegionId);
                cards.Add(TimerCard("compass", region?.Name ?? e.RegionId, () => 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds),
                    () => Fmt.Clock(e.RemainingSeconds), E.FillKind.Green, () => { mapTab = 0; store.ActivePanel = Panel.Map; }));
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
                    E.FillKind.Gold, () => store.FocusBuilding(id)));
            }
            foreach (var c in cards.Take(MaxTimers())) host.Add(c);
        }

        /// <summary>A running job: icon, name and time left over a thin bar, 34 tall.</summary>
        VisualElement TimerCard(string icon, string name, Func<double> progress, Func<string> left, E.FillKind fill, Action onTap)
        {
            var c = E.Capsule(34).Pad(0, 8, 0, 6);
            c.style.width = TimersW;
            c.style.marginBottom = 5;
            c.Add(E.Icon(icon, 22));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 5;
            var head = E.Div().Row().Ignore();
            var time = Live(left, 10.5f);
            head.Add(E.T(name, 10.5f).Fit(TimersW - 22 - 5 - 16 - 36).Grow());
            head.Add(time.NoShrink());
            col.Add(head);
            var bar = LiveBar(progress, 7, fill);
            bar.style.marginTop = 2;
            col.Add(bar);
            c.Add(col);
            Tap(c, onTap);
            return c;
        }

        // MARK: - Right column: menu tiles

        string TilesKey() => $"{MilestoneReady()}|{G.IdleWorkers}|{ResearchReady()}|{HasCustoms}|{LawReady()}";

        bool ResearchReady() => G.State.ActiveResearch == null && G.Content.Technologies.Any(t => G.GetResearchStatus(t.Id).State == ResearchState.Available);

        void BuildTiles(VisualElement host)
        {
            host.Add(MenuTile("goals", "Goals", MilestoneReady() ? "!" : null, () => { goalsTab = 0; store.ActivePanel = Panel.Objectives; }));
            host.Add(MenuTile("people", "People", G.IdleWorkers > 0 ? G.IdleWorkers.ToString(Inv) : null, () => { peopleTab = G.IdleWorkers > 0 ? 0 : 1; store.ActivePanel = Panel.People; }));
            host.Add(MenuTile("knowledge", "Research", ResearchReady() ? "!" : null, () => store.ActivePanel = Panel.Research));
            if (HasCustoms) host.Add(MenuTile("customs", "Customs", LawReady() ? "!" : null, () => store.ActivePanel = Panel.Customs));
        }

        /// <summary>A wooden menu tile: icon with an outlined label inside the bottom edge, a red badge on top.</summary>
        VisualElement MenuTile(string icon, string label, string badge, Action onTap)
        {
            var t = E.Div().Center().Skin("tile").Size(TileSize, TileSize).Pad(0, 0, 3, 0);
            t.style.marginBottom = TileGap;
            t.style.justifyContent = Justify.FlexStart;
            var ic = E.Icon(icon, 28);
            ic.style.marginTop = 3;
            t.Add(ic);
            var l = E.T(label, 9.5f).Fit(TileSize - 6, 7).Align(TextAnchor.MiddleCenter);
            l.style.marginTop = -2;
            t.Add(l);
            if (badge != null) t.Add(CornerBadge(badge));
            Tap(t, onTap);
            return t;
        }

        // MARK: - Bottom row: compass and small buttons, news, Build

        string BottomKey() => $"{SafeWidth}|{ContextOpen}|{G.PendingEvent != null && store.EventMinimized}";

        void BuildBottom(VisualElement host)
        {
            bool card = ContextOpen;
            if (!card)
            {
                var left = E.Div().Row(0, Align.FlexEnd).Ignore().NoShrink();
                // The compass needle follows the camera; tap it to face north-west again.
                var compass = E.Slot("compass", 50, E.Cell.Tile, 0.72f);
                var needle = compass.ElementAt(0);
                Bind(() => needle.style.rotate = new Rotate(new Angle(-(CameraRig.Yaw + 45f), AngleUnit.Degree)));
                Tap(compass, () => store.Rig?.ResetView());
                left.Add(compass);
                void Small(VisualElement b) { b.style.marginLeft = 6; left.Add(b); }
                if (G.PendingEvent != null && store.EventMinimized)
                {
                    var bell = E.Square("bell", Act(() => store.EventMinimized = false), E.Kind.Orange, 34);
                    bell.Add(CornerBadge("1", 14));
                    Small(bell);
                }
                Small(E.Square("world", Act(() => { mapTab = 0; store.ActivePanel = Panel.Map; }), E.Kind.Brown, 34));
                Small(E.Square("legacy", Act(() => store.ActivePanel = Panel.Profile), E.Kind.Brown, 34));
                Small(E.Square("eye_off", Act(() =>
                {
                    store.CancelPlacement();
                    store.SelectedBuildingId = null;
                    store.SelectedNode = null;
                    store.ActivePanel = Panel.None;
                    store.HudHidden = true;
                }), E.Kind.Brown, 34));
                host.Add(left);
            }

            // News: the latest thing that happened, one line, centred between the corners.
            var middle = E.Div().Row(0, Align.FlexEnd).Grow().Ignore();
            middle.style.justifyContent = Justify.Center;
            middle.style.marginLeft = 10;
            middle.style.marginRight = 10;
            if (!card)
            {
                var news = E.Capsule(24).Ignore();
                news.style.marginBottom = 4;
                news.style.maxWidth = 340;
                news.style.overflow = Overflow.Hidden;
                news.Add(E.Icon("bell", 16));
                float room = Mathf.Min(340, SafeWidth - 16 - 50 - 3 * 40 - 20 - BuildButton - 20) - 40;
                var line = LiveFit(() =>
                {
                    if (store.Toasts.Count > 0) return store.Toasts[store.Toasts.Count - 1].Notice.Message;
                    var last = G.State.Legacy.LastOrDefault();
                    return last != null ? last.Title : $"{G.CurrentChapter?.Title ?? G.StageName}";
                }, 11, room, null, 8);
                line.style.marginLeft = 4;
                news.Add(line);
                middle.Add(news);
            }
            host.Add(middle);

            var build = E.Div().Col().Center().Skin("btn_orange").Size(BuildButton, BuildButton).Pad(0, 0, 4, 0);
            build.Add(E.Icon("build", 30));
            build.Add(E.T("Build", 11.5f).Margin(-2));
            Tap(build, () =>
            {
                store.CancelPlacement();
                store.ActivePanel = store.ActivePanel == Panel.Build ? Panel.None : Panel.Build;
            });
            host.Add(build);
        }

        // MARK: - Toasts

        string ToastKey() => string.Join(",", store.Toasts.Select(t => t.CreatedAt.ToString(CultureInfo.InvariantCulture))) + "|" +
                             (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null ? G.PendingEvent.Id : "");

        void BuildToasts(VisualElement host)
        {
            float maxW = Mathf.Min(380, PanelW - SafeLeft - SafeRight - 2 * (LeftRailW + 30));
            maxW = Mathf.Max(220, maxW);
            // A decision put off for later stays on top until it is answered.
            if (G.PendingEvent != null && store.EventMinimized && store.PlacingBuildingId == null)
            {
                var pill = E.Button($"Decision waiting: {G.PendingEvent.Title}", Act(() => store.EventMinimized = false), E.Kind.Orange, 30, "bell", 12, null, maxW);
                pill.style.marginBottom = 6;
                host.Add(pill);
            }
            // Newest first: if the column ever runs out of height, the oldest is the one cut.
            for (int i = store.Toasts.Count - 1; i >= 0; i--)
            {
                var toast = store.Toasts[i];
                string icon = toast.Notice.Kind switch
                {
                    NoticeKind.Success => "check",
                    NoticeKind.Warning => "alert",
                    NoticeKind.Milestone => "sparkle",
                    _ => "bell",
                };
                var t = E.Panel().Row().Pad(4, 10, 7, 9).Ignore();
                t.style.maxWidth = maxW;
                t.style.flexShrink = 0;
                t.style.marginBottom = 5;
                t.Add(E.Icon(icon, 18).NoShrink());
                var text = E.T(toast.Notice.Message, 11.5f, toast.Notice.Kind == NoticeKind.Warning ? Theme.Hex("#FFD2C8") : (Color?)null).Wrap();
                text.style.marginLeft = 6;
                text.style.flexShrink = 1;
                text.style.maxWidth = maxW - 46;
                t.Add(text);
                t.style.opacity = 0;
                t.Transition(260, "opacity");
                t.schedule.Execute(() => t.style.opacity = 1).StartingIn(16);
                host.Add(t);
            }
        }
    }
}
