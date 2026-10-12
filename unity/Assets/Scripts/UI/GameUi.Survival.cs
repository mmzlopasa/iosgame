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
    /// The survival layer (after Frostpunk): weather and warmth, stoking the heart, illness and
    /// healers, discontent beside approval (hope), the Book of Customs and the people's petitions.
    ///
    /// Expensive core queries (the warmth of every home, approval, discontent factors, the
    /// patients under care, the law statuses) are computed at most once per UI tick and shared by
    /// every binding through the cached properties below.
    /// </summary>
    public partial class GameUi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Color Icy = Theme.Hex("#9CCBFF");
        static readonly Color Ember = Theme.Hex("#FFA25C");

        // MARK: - Entry points (capture tour, deep links)

        /// <summary>Opens the Weather & Warmth sheet.</summary>
        public void ShowWeather() => OpenPanel(Panel.Weather);

        /// <summary>Opens the Book of Customs, optionally on one law (and on its confirm step).</summary>
        public void ShowCustoms(string lawId = null, bool confirm = false)
        {
            if (lawId != null && G.Content.Law(lawId) != null) selectedLaw = lawId;
            confirmLaw = confirm ? lawId : null;
            OpenPanel(Panel.Customs);
        }

        /// <summary>Opens People & Work (villagers, health, approval and discontent).</summary>
        public void ShowPeople() => OpenPanel(Panel.People);

        /// <summary>Selects the heart and shows its card (warmth and the stoke button).</summary>
        public void ShowHeart()
        {
            var heart = G.Heart;
            if (heart != null) store.FocusBuilding(heart.Id);
            dirty = true;
        }

        /// <summary>
        /// Shows a petition dialog. When none is pending, one is raised (the given one or the first):
        /// for screenshots and tests only, as the people normally petition on their own.
        /// </summary>
        public void ShowPetition(string petitionId = null)
        {
            if (G.PendingPetition == null)
            {
                var p = petitionId != null ? G.Content.Petition(petitionId) : G.Content.Petitions.FirstOrDefault();
                if (p != null) G.State.PendingPetition = p.Id;
            }
            store.ActivePanel = Panel.None;
            dirty = true;
        }

        void OpenPanel(Panel panel)
        {
            store.CancelPlacement();
            store.SelectedBuildingId = null;
            store.SelectedNode = null;
            store.ActivePanel = panel;
            dirty = true;
        }

        // MARK: - Heat overlay on the ground

        bool heatWanted;

        /// <summary>
        /// Shows the reach of every fire on the ground while warmth matters to what the player is doing:
        /// the Weather sheet is open, a home or a fire is being placed, or the heart is selected. Only
        /// changes are written, so others (the capture tour) may set the overlay themselves in between.
        /// </summary>
        void UpdateHeatOverlay()
        {
            var world = store.World;
            if (world == null) return;
            bool want = false;
            if (G.HasWarmth)
            {
                if (store.ActivePanel == Panel.Weather) want = true;
                else if (store.PlacingBuildingId != null)
                {
                    var def = G.Content.Building(store.PlacingBuildingId);
                    want = def != null && (def.HousingValue > 0 || def.Heat != null || def.Heart);
                }
                else if (store.DecoratingId != null) want = G.Content.Decoration(store.DecoratingId)?.Heat != null;
                else if (store.SelectedBuildingId != null && store.ActivePanel == Panel.None)
                {
                    var b = G.State.Building(store.SelectedBuildingId.Value);
                    var def = b == null ? null : G.Content.Building(b.DefinitionId);
                    want = def != null && (def.Heart || def.Heat != null);
                }
            }
            if (want == heatWanted) return;
            heatWanted = want;
            world.ShowHeat = want;
        }

        // MARK: - Per-tick caches

        int tickNo, warmAt = -1, approvalAt = -1, discAt = -1, treatedAt = -1, lawsAt = -1;
        WarmthReport warmNow;
        int approvalNow;
        List<DiscontentFactor> discNow;
        double discRateNow;
        HashSet<int> treatedNow;
        List<LawStatus> lawsNow;

        WarmthReport Warm
        {
            get
            {
                if (warmAt != tickNo) { warmAt = tickNo; warmNow = G.Warmth(); }
                return warmNow;
            }
        }

        int ApprovalNow
        {
            get
            {
                if (approvalAt != tickNo) { approvalAt = tickNo; approvalNow = G.Approval; }
                return approvalNow;
            }
        }

        List<DiscontentFactor> DiscFactors
        {
            get
            {
                if (discAt != tickNo)
                {
                    discAt = tickNo;
                    discNow = G.DiscontentFactors();
                    discRateNow = 0;
                    foreach (var f in discNow) discRateNow += f.PerHour;
                }
                return discNow;
            }
        }

        double DiscRate { get { _ = DiscFactors; return discRateNow; } }

        HashSet<int> Treated
        {
            get
            {
                if (treatedAt != tickNo) { treatedAt = tickNo; treatedNow = G.TreatedPatients(); }
                return treatedNow;
            }
        }

        List<LawStatus> LawsNow
        {
            get
            {
                if (lawsAt != tickNo) { lawsAt = tickNo; lawsNow = G.LawStatuses(); }
                return lawsNow;
            }
        }

        bool HasCustoms => G.Content.Laws.Count > 0;

        /// <summary>A custom can be signed right now (the Customs tile shows a "!").</summary>
        bool LawReady()
        {
            if (!HasCustoms || G.LawCooldownLeft > 0) return false;
            foreach (var s in LawsNow) if (s.State == LawState.Available) return true;
            return false;
        }

        // MARK: - Formatting and colours

        static string Deg(double t) => Math.Round(t, MidpointRounding.AwayFromZero).ToString("0;−0;0", Inv) + "°";
        static string SignedDeg(double t) => Math.Round(t, MidpointRounding.AwayFromZero).ToString("+0;−0;0", Inv) + "°";
        static string PerHour(double v) => v.ToString("+0.0;−0.0;0.0", Inv) + "/h";
        static string Num(double v) => v.ToString("0.#", Inv);

        static Color ExposureColor(Exposure e) => e switch
        {
            Exposure.Comfortable => Theme.Hex("#FFC27A"),
            Exposure.Chilly => Theme.Hex("#E3ECF5"),
            Exposure.Cold => Icy,
            _ => Theme.Error,
        };

        static Color MoodColor(string mood) => mood switch
        {
            "Revolt" => Theme.Error,
            "Unrest" => Theme.Hex("#F0A35E"),
            "Grumbling" => Theme.Gold,
            _ => Theme.Mint,
        };

        ColdSnapDefinition CurrentSnap()
        {
            var c = G.Content.Balance.Climate;
            if (!G.HasClimate || c.PeriodSeconds <= 0) return null;
            return G.SnapAt((long)Math.Floor(G.State.SimTime / c.PeriodSeconds));
        }

        /// <summary>"Now", "25m", "1h25": time until a forecast period begins.</summary>
        static string Until(double seconds)
        {
            if (seconds <= 0) return "Now";
            int m = (int)Math.Ceiling(seconds / 60);
            return m < 60 ? $"{m}m" : $"{m / 60}h{m % 60:00}";
        }

        // MARK: - HUD pieces

        /// <summary>Outside temperature, coloured by how it feels; a snowflake while a cold snap lasts.</summary>
        VisualElement ThermoChip()
        {
            // Compact: it shares the season card's narrow top line with the season's name.
            var chip = E.Div().Row().Radius(999).Pad(0, 5, 0, 3).Bg(new Color(0.04f, 0.05f, 0.07f, 0.6f)).Ignore().NoShrink();
            chip.style.height = 16;
            var icon = E.Icon("thermometer", 10, Color.white);
            chip.Add(icon);
            var t = Bold("", 10.5f);
            t.style.marginLeft = 1;
            chip.Add(t);
            int lastSnap = -1;
            Bind(() =>
            {
                double temp = G.Temperature;
                bool snap = CurrentSnap() != null;
                var c = snap ? Icy : ExposureColor(G.ExposureOf(temp));
                string s = Deg(temp);
                if (t.text != s) t.text = s;
                t.style.color = c;
                icon.style.unityBackgroundImageTintColor = c;
                if ((snap ? 1 : 0) != lastSnap)
                {
                    lastSnap = snap ? 1 : 0;
                    icon.style.backgroundImage = new StyleBackground(Theme.Icon(snap ? "winter" : "thermometer"));
                    chip.Border(Theme.Alpha(c, snap ? 0.8f : 0.45f));
                }
            });
            return chip;
        }

        /// <summary>A Frostpunk-style meter: icon, thin bar (with threshold ticks) and the value.</summary>
        VisualElement Meter(string icon, Func<double> value, Func<Color> color, float[] ticks)
        {
            var row = E.Div().Row().Ignore();
            row.style.height = 11;
            var ic = E.Icon(icon, 11, Theme.Body);
            row.Add(ic);
            var bar = E.Bar(out var fill, 4, null, Theme.Gold).Grow();
            bar.style.marginLeft = 4;
            bar.style.marginRight = 4;
            if (ticks != null)
                foreach (float k in ticks)
                {
                    var tick = E.Div().Ignore().Bg(new Color(0, 0, 0, 0.55f)).Abs(null, 0, null, 0);
                    tick.style.width = 1;
                    tick.style.left = Length.Percent(k * 100);
                    bar.Add(tick);
                }
            row.Add(bar);
            var v = E.Mono("", 9.5f).Align(TextAnchor.MiddleRight);
            v.style.width = 20;
            row.Add(v);
            Bind(() =>
            {
                double x = value();
                var c = color();
                E.SetFill(fill, x / 100.0);
                fill.style.backgroundColor = c;
                ic.style.unityBackgroundImageTintColor = c;
                string s = ((int)Math.Round(x)).ToString(Inv);
                if (v.text != s) v.text = s;
                v.style.color = c;
            });
            return row;
        }

        /// <summary>Hope (approval) and discontent, stacked (tap: People & wellbeing).</summary>
        VisualElement HudMeters()
        {
            var m = E.Div("meters").Col();
            m.Add(Meter("approval", () => ApprovalNow, () => ApprovalColor(ApprovalNow), null));
            if (G.HasDiscontent)
            {
                var d = G.Content.Balance.Discontent;
                var dm = Meter("discontent", () => G.State.Discontent, () => MoodColor(G.DiscontentMood),
                               new[] { (float)(d.Grumbling / 100), (float)(d.Unrest / 100) });
                dm.style.marginTop = 3;
                m.Add(dm);
            }
            Tap(m, () => store.ActivePanel = Panel.People);
            return m;
        }

        /// <summary>Red badge with the sick count, in the status bar (hidden while nobody is ill).</summary>
        VisualElement SickChip(float size)
        {
            var c = E.Div().Row().Ignore();
            c.style.marginLeft = 6;
            var dot = E.Div().Size(20, 20).Radius(10).Center().Bg(Notify).Border(Color.white, 1.5f).Ignore();
            dot.Add(E.Icon("sick", 12, Color.white));
            c.Add(dot);
            var n = Bold("", size, Theme.Hex("#FFB3B3"));
            n.style.marginLeft = 3;
            c.Add(n);
            Bind(() =>
            {
                int sick = G.SickCount;
                c.Hidden(sick == 0);
                string s = sick.ToString(Inv);
                if (n.text != s) n.text = s;
            });
            return c;
        }

        float SickChipWidth(float size) => G.HasHealth && G.SickCount > 0 ? 29 + TextWidth(G.SickCount.ToString(Inv), size) : 0;

        /// <summary>Size of the wooden tiles on the right so all of them fit between the top row and the Build button.</summary>
        float TileSize()
        {
            int n = HasCustoms ? 4 : 3;
            float gap = TileGap();
            float room = PanelH - SafeTop - HudTop - BottomPad - BottomRowH - 4;
            return Mathf.Clamp(Mathf.Floor((room - (n - 1) * gap) / n), 44, TileW);
        }

        float TileGap() => HasCustoms ? 8 : 10;

        /// <summary>The promise made to the people: what is still owed and the time left (left rail).</summary>
        VisualElement PromiseCard()
        {
            var p = G.ActivePetitionDefinition;
            if (p == null || G.State.ActivePetition == null) return null;
            var card = E.Div().Col().Radius(14).Pad(7, 10).BgImage(Theme.Gradient("parchment", true, Theme.Hex("#F7E7C4"), Theme.Hex("#E7CC98")))
                .Border(Ruby, 2f);
            card.style.width = LeftRailW;
            card.style.marginBottom = 8;
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("discontent", 14, Ruby));
            var t = E.Text($"Promise: {p.Title}", 12, Theme.Hex("#3A220E"), Theme.F.TextBold).NoWrap().Grow();
            t.style.marginLeft = 6;
            head.Add(t);
            var left = E.Mono("", 10.5f, InkSoft);
            left.style.marginLeft = 6;
            Bind(() =>
            {
                double s = (G.State.ActivePetition?.DeadlineSimTime ?? 0) - G.State.SimTime;
                string txt = Fmt.Clock(s);
                if (left.text != txt) left.text = txt;
                left.style.color = s < 600 ? Ruby : InkSoft;
            });
            head.Add(left);
            card.Add(head);
            var goal = E.Text("", 10.5f, InkSoft, Theme.F.TextMedium).NoWrap();
            goal.style.marginTop = 3;
            var bar = E.Bar(out var fill, 5, null, Theme.Hex("#6FB24A"));
            bar.style.marginTop = 5;
            Bind(() =>
            {
                var pd = G.ActivePetitionDefinition;
                if (pd == null) return;
                var st = G.PetitionGoalStatus(pd);
                int met = st.Count(x => x.Met);
                var open = st.FirstOrDefault(x => !x.Met);
                string txt = st.Count == 0 ? "" : open.Text != null ? open.Text : "Kept: waiting for the people to see it";
                if (goal.text != txt) goal.text = txt;
                E.SetFill(fill, st.Count == 0 ? 0 : met / (double)st.Count);
            });
            card.Add(goal);
            card.Add(bar);
            Tap(card, () => store.ActivePanel = Panel.People);
            return card;
        }

        // MARK: - Stoking the heart

        double StokeNeed
        {
            get
            {
                var w = G.Content.Balance.Warmth;
                return w == null ? 0 : w.StokeWoodPerSecond * Math.Min(w.StokeSeconds, 600);
            }
        }

        /// <summary>The state of the stoke button (part of the keys of the cards that show it).</summary>
        string StokeKey()
        {
            if (!G.HasWarmth) return "";
            bool lit = G.Heart?.IsOperational ?? false;
            return $"{G.IsStoked}{G.StokeReadyIn > 0}{G.State.Resources.Wood >= StokeNeed}{lit}";
        }

        /// <summary>Frostpunk's overdrive: what stoking gives and costs, the time left while it roars, the rest after.</summary>
        VisualElement StokeBlock()
        {
            var w = G.Content.Balance.Warmth;
            bool stoked = G.IsStoked;
            var box = E.Div().Col().Radius(18).Pad(12).Margin(10);
            if (stoked) box.Bg(Theme.Alpha(Ember, 0.12f)).Border(Theme.Alpha(Ember, 0.45f));
            else box.BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.3f));
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("flame", 16, stoked ? Ember : Theme.Gold));
            var t = E.Text(stoked ? "The fire roars" : "Stoke the fire", 14, Theme.Ink, Theme.F.Display).Grow();
            t.style.marginLeft = 7;
            head.Add(t);
            box.Add(head);
            string cheer = w.StokeApproval > 0.01 ? $" People cheer it (+{Num(w.StokeApproval)} approval)." : "";
            box.Add(E.Text($"Burns {Fmt.Amount(w.StokeWoodPerSecond * 3600)} wood per hour for {Game.Duration(w.StokeSeconds)}: " +
                           $"{SignedDeg(w.StokeHeat)} warmth and {Num(w.StokeRadius)} more cells of reach.{cheer}", 11.5f, Theme.Body).Margin(6));

            bool lit = G.Heart?.IsOperational ?? false;
            if (stoked)
            {
                var row = E.Div().Row().Between().Margin(8).Ignore();
                row.Add(E.Text("Roaring", 12, Ember, Theme.F.TextBold));
                row.Add(Live(() => $"{Fmt.Clock(G.State.StokedUntilSimTime - G.State.SimTime)} left", 12, Ember, Theme.F.Mono));
                box.Add(row);
                var bar = E.Bar(out var fill, 5, null, Ember);
                bar.style.marginTop = 6;
                Bind(() => E.SetFill(fill, (G.State.StokedUntilSimTime - G.State.SimTime) / Math.Max(1, w.StokeSeconds)));
                box.Add(bar);
            }
            else if (G.StokeReadyIn > 0)
            {
                var row = E.Div().Row().Between().Margin(8).Ignore();
                row.Add(E.Text("The fire rests", 12, Theme.Muted, Theme.F.TextBold));
                row.Add(Live(() => $"again in {Fmt.Clock(G.StokeReadyIn)}", 12, Theme.Body, Theme.F.Mono));
                box.Add(row);
            }
            else if (!lit)
                box.Add(E.Button($"The {G.HeartName} is not burning", null, E.Kind.Disabled, 40).Margin(10));
            else
            {
                bool can = G.State.Resources.Wood >= StokeNeed;
                box.Add(E.Button(can ? "Stoke the fire" : $"Need {Fmt.Amount(Math.Ceiling(StokeNeed))} wood", can ? Act(store.StokeHeart) : null,
                                 can ? E.Kind.Gold : E.Kind.Disabled, 42, can ? "flame" : null).Margin(10));
            }
            return box;
        }

        // MARK: - Building and decoration cards

        VisualElement LiveChip(string icon, Func<string> text, Color? tint = null)
        {
            var c = E.Div().Row().Radius(999).Pad(5, 10).Bg(new Color(1, 1, 1, 0.06f)).Ignore();
            c.style.marginRight = 6;
            c.style.marginBottom = 4;
            c.Add(E.Icon(icon, 13, tint ?? Theme.GoldLight));
            var t = Live(text, 11.5f, Theme.Ink, Theme.F.TextMedium);
            t.style.marginLeft = 5;
            c.Add(t);
            return c;
        }

        /// <summary>Warmth inside a home, the heart's warmth and reach (and its stoke button), a fire's warmth.</summary>
        void WarmthSection(VisualElement card, PlacedBuilding b, BuildingDefinition def)
        {
            if (!G.HasWarmth) return;
            int id = b.Id;
            if (def.HousingValue > 0)
            {
                var box = E.Div().Col().Radius(16).Pad(10, 12).Bg(new Color(1, 1, 1, 0.045f)).Border(Theme.EdgeSoft).Margin(8);
                var row = E.Div().Row().Ignore();
                var icon = E.Icon("thermometer", 15, Theme.Body);
                row.Add(icon);
                var label = E.Text("", 13, Theme.Ink, Theme.F.TextBold).Grow();
                label.style.marginLeft = 7;
                row.Add(label);
                box.Add(row);
                var sub = E.Text("", 11, Theme.Muted);
                sub.style.marginTop = 3;
                box.Add(sub);
                Bind(() =>
                {
                    HomeWarmth? home = null;
                    foreach (var h in Warm.Homes) if (h.Building.Id == id) { home = h; break; }
                    string text, detail;
                    Color c;
                    if (home is HomeWarmth hw)
                    {
                        c = ExposureColor(hw.Exposure);
                        text = $"Warmth inside: {Deg(hw.Warmth)} ({hw.Exposure})";
                        detail = hw.Occupants == 0 ? "Nobody lives here yet" : hw.Occupants == 1 ? "1 person lives here" : $"{hw.Occupants} people live here";
                        detail += $" · outside {Deg(Warm.Temperature)}";
                    }
                    else
                    {
                        c = Theme.Muted;
                        text = "Warmth inside: not lived in yet";
                        detail = $"Outside {Deg(Warm.Temperature)}";
                    }
                    if (label.text != text) label.text = text;
                    if (sub.text != detail) sub.text = detail;
                    label.style.color = c;
                    icon.style.unityBackgroundImageTintColor = c;
                });
                var chips = E.Div().Row().Margin(8).Ignore();
                chips.style.flexWrap = Wrap.Wrap;
                chips.Add(StatChip("home", $"Walls {SignedDeg(G.InsulationOf(b, def))}"));
                var w = G.Content.Balance.Warmth;
                if (w.InsulationPerLevel > 0 && b.Level < def.MaxLevel)
                    chips.Add(StatChip("level_up", $"+{w.InsulationPerLevel.ToString("0.#", Inv)}° per level"));
                box.Add(chips);
                card.Add(box);
            }
            if (def.Heart)
            {
                var chips = E.Div().Row().Margin(8).Ignore();
                chips.style.flexWrap = Wrap.Wrap;
                chips.Add(LiveChip("flame", () =>
                {
                    var (heat, radius) = G.HeartFire();
                    return heat <= 0 ? "Gives no warmth" : $"Warmth {SignedDeg(heat)} within {Num(radius)} cells";
                }, Ember));
                card.Add(chips);
                if (b.Status == BuildingStatus.Complete || b.IsOperational) card.Add(StokeBlock());
            }
            else if (def.Heat != null)
            {
                var chips = E.Div().Row().Margin(8).Ignore();
                chips.Add(StatChip("flame", $"Warmth {SignedDeg(def.Heat.Heat)} within {Num(def.Heat.Radius)} cells"));
                card.Add(chips);
            }
        }

        /// <summary>Braziers and fire pits: how much warmth they give and how far.</summary>
        VisualElement HeatChips(DecorationDefinition def)
        {
            if (def?.Heat == null) return null;
            var row = E.Div().Row().Margin(8).Ignore();
            row.style.flexWrap = Wrap.Wrap;
            row.Add(StatChip("flame", $"Warmth {SignedDeg(def.Heat.Heat)} within {Num(def.Heat.Radius)} cells"));
            if (def.Heat.WoodPerSecond > 0) row.Add(StatChip("wood", $"{Num(def.Heat.WoodPerSecond * 3600)} wood/h in the cold"));
            return row;
        }

        // MARK: - Expeditions

        /// <summary>"2 survivors, 40 food" (null when the region holds nothing).</summary>
        static string FindsText(RegionDefinition r)
        {
            var parts = new List<string>();
            if (r.Survivors > 0) parts.Add(r.Survivors == 1 ? "1 survivor" : $"{r.Survivors} survivors");
            foreach (var kv in r.Loot.Entries)
                if (kv.Value > 0) parts.Add($"{Fmt.Amount(kv.Value)} {kv.Key.DisplayName().ToLowerInvariant()}");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        VisualElement RegionFinds(RegionDefinition r, bool explored)
        {
            if (explored)
            {
                if (string.IsNullOrEmpty(r.Finding)) return null;
                var f = E.Text(r.Finding, 11.5f, Theme.Mint).Margin(6);
                f.style.unityFontStyleAndWeight = FontStyle.Italic;
                return f;
            }
            string finds = FindsText(r);
            if (finds == null) return null;
            var row = E.Div().Row(0, Align.FlexStart).Margin(6).Ignore();
            row.Add(E.Icon(r.Survivors > 0 ? "people" : "compass", 13, Theme.GoldLight));
            var t = E.Text($"Scouts may find: {finds}", 11.5f, Theme.GoldLight, Theme.F.TextMedium).Grow();
            t.style.marginLeft = 6;
            row.Add(t);
            return row;
        }

        // MARK: - People: health, discontent, the sick

        string SurvivalPeopleKey()
        {
            string disc = G.HasDiscontent ? string.Join(",", DiscFactors.Select(f => f.Label)) + G.DiscontentMood + G.State.ActivePetition?.PetitionId : "";
            if (!G.HasHealth) return disc;
            var treated = Treated;
            string sick = string.Join(",", G.State.People.Where(p => p.Sick).Select(p => $"{p.Id}{(treated.Contains(p.Id) ? "t" : "")}{(p.Severity > 0.5 ? "!" : "")}"));
            return $"{disc}|{sick}|{G.SickCount > G.CareCapacity}|{G.State.Assignments[JobType.Healing] > 0}";
        }

        /// <summary>Discontent: the meter, the mood and what it does, and every cause per hour.</summary>
        void BuildDiscontent(VisualElement host)
        {
            if (!G.HasDiscontent) return;
            var d = G.Content.Balance.Discontent;
            var card = Card();
            var head = E.Div().Row().Ignore();
            var icon = E.Icon("discontent", 18, Theme.Mint);
            head.Add(icon);
            var t = E.Text("Discontent", 15, Theme.Ink, Theme.F.Display).Grow();
            t.style.marginLeft = 8;
            head.Add(t);
            var value = E.Mono("", 15);
            head.Add(value);
            card.Add(head);
            var bar = E.Bar(out var fill, 6, null, Theme.Mint);
            foreach (double k in new[] { d.Grumbling, d.Unrest })
            {
                var tick = E.Div().Ignore().Bg(new Color(0, 0, 0, 0.6f)).Abs(null, 0, null, 0);
                tick.style.width = 2;
                tick.style.left = Length.Percent((float)k);
                bar.Add(tick);
            }
            bar.style.marginTop = 10;
            card.Add(bar);
            Bind(() =>
            {
                var c = MoodColor(G.DiscontentMood);
                string s = $"{(int)Math.Round(G.State.Discontent)} · {G.DiscontentMood}";
                if (value.text != s) value.text = s;
                value.style.color = c;
                icon.style.unityBackgroundImageTintColor = c;
                E.SetFill(fill, G.State.Discontent / 100);
                fill.style.backgroundColor = c;
            });
            var effect = Live(() =>
            {
                string trend = $"{PerHour(DiscRate)} now";
                switch (G.DiscontentMood)
                {
                    case "Revolt": return $"{trend} · families are leaving";
                    case "Unrest": return $"{trend} · work {Fmt.Signed((G.DiscontentWork - 1) * 100)}% · no newcomers";
                    case "Grumbling": return $"{trend} · work {Fmt.Signed((G.DiscontentWork - 1) * 100)}%";
                    default: return $"{trend} · calm";
                }
            }, 11.5f, Theme.Body);
            effect.style.marginTop = 6;
            card.Add(effect);
            card.Add(E.Div().Size(null, 1).Bg(new Color(1, 1, 1, 0.07f)).Margin(10).Ignore());
            var factors = DiscFactors;
            for (int i = 0; i < factors.Count; i++)
            {
                int index = i;
                bool up = factors[i].PerHour > 0;
                var r = E.Div().Row().Between().Margin(6).Ignore();
                r.Add(E.Text(factors[i].Label, 12.5f, Theme.Ink).Grow());
                r.Add(Live(() => { var f = DiscFactors; return index < f.Count ? PerHour(f[index].PerHour) : ""; }, 12.5f, up ? Theme.Error : Theme.Mint, Theme.F.Mono));
                card.Add(r);
            }

            var promise = G.ActivePetitionDefinition;
            if (promise != null && G.State.ActivePetition != null)
            {
                var box = E.Div().Col().Radius(14).Pad(10, 12).Bg(Theme.Alpha(Theme.Gold, 0.08f)).Border(Theme.Alpha(Theme.Gold, 0.3f)).Margin(12);
                var ph = E.Div().Row().Ignore();
                ph.Add(E.Icon("goals", 14, Theme.Gold));
                var pt = E.Text($"You promised: {promise.Promise}", 12.5f, Theme.Ink, Theme.F.TextBold).Grow();
                pt.style.marginLeft = 7;
                ph.Add(pt);
                ph.Add(Live(() => Fmt.Clock((G.State.ActivePetition?.DeadlineSimTime ?? 0) - G.State.SimTime), 11.5f, Theme.Gold, Theme.F.Mono));
                box.Add(ph);
                foreach (var req in G.PetitionGoalStatus(promise)) box.Add(Check(req.Text, req.Met).Margin(6));
                box.Add(E.Text($"Kept: −{Num(d.KeptRelief)} discontent and +{Num(d.KeptApproval)} approval. Broken: +{Num(d.BrokenAnger)} discontent.", 11, Theme.Muted).Margin(6));
                card.Add(box);
            }
            card.Add(E.Text($"Hunger, cold, illness and homelessness stir discontent; content people calm it. From {Num(d.Grumbling)} people grumble and work slower, " +
                            $"from {Num(d.Unrest)} there is unrest, and at 100 families leave. Customs and promises calm them.", 11, Theme.Muted).Margin(10));
            host.Add(card);
        }

        /// <summary>Who is ill, how many the healers can tend and how fast people fall ill.</summary>
        void BuildHealth(VisualElement host)
        {
            if (!G.HasHealth) return;
            var card = Card();
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("sick", 18, Theme.Error));
            var t = E.Text("Health", 15, Theme.Ink, Theme.F.Display).Grow();
            t.style.marginLeft = 8;
            head.Add(t);
            if (G.State.Deaths > 0) head.Add(E.Text($"{G.State.Deaths} lost", 11.5f, Theme.Muted));
            card.Add(head);
            var row = E.Div().Row().Margin(10).Ignore();
            row.Add(Stat("Ill", () => $"{G.SickCount}", G.SickCount > 0 ? Theme.Error : Theme.Ink));
            row.Add(Stat("Under care", () => $"{Math.Min(G.SickCount, G.CareCapacity)}/{G.SickCount}", Theme.Mint).Margin(0, 0, 0, 8));
            row.Add(Stat("Falling ill", () => (G.IllnessRate() * 3600).ToString("0.0", Inv) + "/h", Theme.Body).Margin(0, 0, 0, 8));
            card.Add(row);
            var hut = G.Content.Building("healers_hut");
            int per = G.Content.Balance.Health.PatientsPerHealer;
            if (G.SickCount > G.CareCapacity)
                card.Add(Notice("sick", G.State.Assignments[JobType.Healing] > 0
                    ? $"More are ill than the healers can tend ({per} each). Add healers or build another {hut?.Name ?? "healer's hut"}."
                    : $"Nobody tends the sick. Build a {hut?.Name ?? "healer's hut"} and assign healers: each tends {per}.", Theme.Error));
            card.Add(E.Text("People in cold homes, without a home or without food fall ill. Under a healer's care they get well quickly; " +
                            "left alone in the cold an illness grows grave and may kill.", 11, Theme.Muted).Margin(10));
            host.Add(card);
        }

        /// <summary>Under a villager's name: ill, under care or not, and how far along the recovery is.</summary>
        void SickLine(VisualElement col, Person p)
        {
            bool treated = Treated.Contains(p.Id);
            bool grave = p.Severity > 0.5;
            var status = E.Div().Row().Ignore();
            status.style.marginTop = 2;
            status.Add(E.Icon("sick", 11, treated ? Theme.Mint : Theme.Error));
            var st = E.Text(treated ? "Ill · under care" : grave ? "Gravely ill · untreated" : "Ill · untreated", 11, treated ? Theme.Mint : Theme.Error, Theme.F.TextBold);
            st.style.marginLeft = 4;
            status.Add(st);
            col.Add(status);
            int pid = p.Id;
            var bar = E.Bar(out var fill, 3, Theme.MintFill);
            bar.style.marginTop = 4;
            bar.style.marginRight = 8;
            Bind(() =>
            {
                Person who = null;
                foreach (var x in G.State.People) if (x.Id == pid) { who = x; break; }
                E.SetFill(fill, who == null ? 1 : who.Recovery);
            });
            col.Add(bar);
        }

        // MARK: - Weather & Warmth sheet

        string WeatherKey()
        {
            if (!G.HasClimate) return "off";
            var c = G.Content.Balance.Climate;
            long period = (long)Math.Floor(G.State.SimTime / c.PeriodSeconds);
            var w = Warm;
            string homes = string.Join(",", w.Homes.Select(h => $"{h.Building.Id}:{(int)h.Exposure}:{h.Occupants}"));
            return $"{period}|{G.SeasonIndex}|{G.Freezing}|{StokeKey()}|{w.Homeless > 0}|{homes}|{G.HasWarmth}";
        }

        void BuildWeatherPanel(VisualElement host)
        {
            if (!G.HasClimate)
            {
                host.Add(Notice("summer", "The weather is mild here: nobody needs to worry about the cold.", Theme.Mint).Margin(0));
                return;
            }
            var clim = G.Content.Balance.Climate;
            var season = G.Season;

            // Now
            var hero = Card();
            var row = E.Div().Row().Ignore();
            var bigIcon = E.Icon("thermometer", 38, Theme.Body);
            row.Add(bigIcon);
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.Eyebrow("Outside now", Theme.Muted, 9.5f));
            var temp = E.Text("", 32, Theme.Ink, Theme.F.Display);
            col.Add(temp);
            var feel = E.Text("", 12, Theme.Body);
            col.Add(feel);
            row.Add(col);
            if (season != null)
            {
                var sc = E.Div().Col(Align.FlexEnd).Ignore();
                sc.Add(E.Icon(SeasonIcon(season.Id), 22, SeasonTint(season.Id)));
                sc.Add(E.Text($"{season.Name} · Y{G.Year}", 11.5f, Theme.Ink, Theme.F.TextBold).Margin(4));
                sc.Add(Live(() => $"next season {Fmt.Clock(G.SecondsUntilNextSeason)}", 10.5f, Theme.Muted, Theme.F.Mono).Margin(2));
                row.Add(sc);
            }
            Bind(() =>
            {
                double t = G.Temperature;
                var e = G.ExposureOf(t);
                var c = ExposureColor(e);
                string s = Deg(t);
                if (temp.text != s) temp.text = s;
                temp.style.color = c;
                bigIcon.style.unityBackgroundImageTintColor = c;
                string f = $"Feels {e.ToString().ToLowerInvariant()} outside";
                if (feel.text != f) feel.text = f;
            });
            hero.Add(row);
            var snapNow = CurrentSnap();
            if (snapNow != null)
                hero.Add(Notice("winter", $"{snapNow.Name}: {Num(snapNow.Drop)}° colder than usual. Keep the woodpile full and the fires burning.", Icy));
            else if (G.Freezing)
                hero.Add(Notice("alert", "The woodpile is empty: the fires are out and people are freezing.", Theme.Error));
            host.Add(hero);

            // Forecast
            var forecast = G.Forecast();
            double hours = forecast.Count * clim.PeriodSeconds / 3600.0;
            host.Add(SectionLabel($"Forecast · next {Num(hours)} hours"));
            var strip = E.Div().Row(0, Align.Stretch).Margin(0, 0, 6).Ignore();
            foreach (var period in forecast)
            {
                var exposure = G.ExposureOf(period.Temperature);
                var c = ExposureColor(exposure);
                bool snap = period.Snap != null;
                var cell = E.Div().Col(Align.Center).Grow().Radius(12).Pad(6, 0, 0, 0).Ignore();
                cell.style.marginRight = 3;
                cell.style.overflow = Overflow.Hidden;
                if (snap) cell.Bg(Theme.Alpha(Icy, 0.16f)).Border(Theme.Alpha(Icy, 0.55f));
                else cell.Bg(new Color(1, 1, 1, 0.045f)).Border(Theme.EdgeSoft);
                double start = period.StartSimTime;
                cell.Add(Live(() => Until(start - G.State.SimTime), 9.5f, snap ? Icy : Theme.Muted, Theme.F.Mono));
                cell.Add(E.Icon(snap ? "winter" : "thermometer", 14, snap ? Icy : c).Margin(4));
                cell.Add(E.Mono(Deg(period.Temperature), 12.5f, c).Margin(3));
                var under = E.Div().Ignore().Bg(c);
                under.style.height = 3;
                under.style.alignSelf = Align.Stretch;
                under.style.marginTop = 6;
                cell.Add(under);
                strip.Add(cell);
            }
            host.Add(strip);
            // Cold snaps in sight, one pill per spell.
            bool anySnap = false;
            for (int i = 0; i < forecast.Count; i++)
            {
                var snap = forecast[i].Snap;
                if (snap == null || (i > 0 && forecast[i - 1].Snap == snap)) continue;
                int j = i;
                while (j + 1 < forecast.Count && forecast[j + 1].Snap == snap) j++;
                anySnap = true;
                double start = forecast[i].StartSimTime, end = forecast[j].EndSimTime;
                bool lastsBeyond = j == forecast.Count - 1;
                var pill = E.Div().Row().Radius(12).Pad(7, 10).Bg(Theme.Alpha(Icy, 0.1f)).Border(Theme.Alpha(Icy, 0.4f)).Margin(4).Ignore();
                pill.Add(E.Icon("winter", 14, Icy));
                var name = E.Text(snap.Name, 12.5f, Theme.Ink, Theme.F.TextBold);
                name.style.marginLeft = 7;
                pill.Add(name);
                var when = Live(() =>
                {
                    double now = G.State.SimTime;
                    return start <= now
                        ? (lastsBeyond ? "now" : $"now · ends in {Game.Duration(end - now)}")
                        : $"in {Game.Duration(start - now)}";
                }, 11.5f, Icy, Theme.F.TextMedium).Grow();
                when.style.marginLeft = 7;
                pill.Add(when);
                pill.Add(E.Mono($"−{Num(snap.Drop)}°", 11.5f, Icy));
                host.Add(pill);
            }
            if (!anySnap) host.Add(E.Text("No cold snaps in sight.", 11.5f, Theme.Muted).Margin(2, 4, 0, 4));

            if (!G.HasWarmth) return;
            var wc = G.Content.Balance.Warmth;

            // Homes
            host.Add(SectionLabel("Homes"));
            var homes = Card();
            var grid = E.Div().Row().Ignore();
            foreach (Exposure e in new[] { Exposure.Comfortable, Exposure.Chilly, Exposure.Cold, Exposure.Freezing })
            {
                var c = ExposureColor(e);
                var cell = E.Div().Col(Align.Center).Grow().Radius(12).Pad(8, 2).Bg(Theme.Alpha(c, 0.08f)).Border(Theme.Alpha(c, 0.25f)).Ignore();
                if (e != Exposure.Comfortable) cell.style.marginLeft = 6;
                int k = (int)e;
                cell.Add(Live(() => Warm.People[k].ToString(Inv), 17, c, Theme.F.Mono));
                cell.Add(E.Text(e.ToString(), 10.5f, Theme.Body).Margin(2));
                grid.Add(cell);
            }
            homes.Add(grid);
            if (Warm.Homeless > 0)
                homes.Add(Notice("people", "", Theme.Error).Also(n =>
                {
                    var l = n.Q<Label>();
                    Bind(() =>
                    {
                        var w = Warm;
                        string s = $"{w.Homeless} without a home sleep beside the fire ({Deg(w.HomelessWarmth)}). Build shelters.";
                        if (l.text != s) l.text = s;
                    });
                }));
            // Coldest homes first (tap one to see it).
            var list = Warm.Homes.Where(h => h.Occupants > 0).OrderBy(h => h.Warmth).Take(6).ToList();
            if (list.Count > 0)
            {
                homes.Add(E.Eyebrow("Coldest homes", Theme.Muted, 9.5f).Margin(12, 0, 2, 0));
                foreach (var h in list)
                {
                    int id = h.Building.Id;
                    var r = E.Div().Row().Radius(10).Pad(6, 4).Margin(2);
                    r.Add(E.Icon("home", 13, Theme.Body));
                    var n = E.Text($"{G.DisplayName(h.Building)} · {h.Occupants}", 12, Theme.Ink).NoWrap().Grow();
                    n.style.marginLeft = 7;
                    r.Add(n);
                    var v = E.Mono("", 12);
                    Bind(() =>
                    {
                        foreach (var x in Warm.Homes)
                            if (x.Building.Id == id)
                            {
                                string s = $"{Deg(x.Warmth)} {x.Exposure}";
                                if (v.text != s) v.text = s;
                                v.style.color = ExposureColor(x.Exposure);
                                return;
                            }
                    });
                    r.Add(v);
                    Tap(r, () => store.FocusBuilding(id));
                    homes.Add(r);
                }
            }
            homes.Add(E.Text($"Comfortable from {Deg(wc.Comfortable)}, cold below {Deg(wc.Cold)}, freezing below {Deg(wc.Freezing)}. " +
                             "Better walls (levels) and fires nearby keep homes warm; cold homes make people ill and discontent.", 11, Theme.Muted).Margin(10));
            host.Add(homes);

            // The heart
            host.Add(SectionLabel(G.HeartName));
            var heart = Card();
            var hr = E.Div().Row().Ignore();
            hr.Add(E.Icon("flame", 18, Ember));
            var ht = Live(() =>
            {
                var (heat, radius) = G.HeartFire();
                return heat <= 0 ? "The fire gives no warmth" : $"Warms {SignedDeg(heat)} within {Num(radius)} cells";
            }, 14, Theme.Ink, Theme.F.Display).Grow();
            ht.style.marginLeft = 8;
            hr.Add(ht);
            heart.Add(hr);
            var fuel = E.Div().Row().Between().Margin(10).Ignore();
            fuel.Add(E.Text("Firewood burnt", 12, Theme.Body));
            fuel.Add(Live(() => $"{(G.FuelPerSecond * 60).ToString("0.0", Inv)}/m", 12, Theme.Ink, Theme.F.Mono));
            heart.Add(fuel);
            var lasts = E.Div().Row().Between().Margin(6).Ignore();
            lasts.Add(E.Text("Woodpile lasts", 12, Theme.Body));
            lasts.Add(Live(() =>
            {
                double burn = G.FuelPerSecond, net = econ.Net.Wood;
                if (burn <= 0) return "no fires needed";
                if (net >= 0) return "growing";
                return Fmt.Clock(G.State.Resources.Wood / -net);
            }, 12, Theme.Ink, Theme.F.Mono));
            heart.Add(lasts);
            if (G.Heart != null) heart.Add(StokeBlock());
            var brazier = G.Content.Decorations.FirstOrDefault(d => d.Heat != null);
            if (brazier != null)
                heart.Add(E.Text($"{brazier.Name}s (Build › {brazier.Category}) warm the homes around them: {SignedDeg(brazier.Heat.Heat)} within {Num(brazier.Heat.Radius)} cells.",
                                 11, Theme.Muted).Margin(10));
            host.Add(heart);
        }

        // MARK: - Book of Customs

        string selectedLaw, confirmLaw;

        string CustomsKey()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(selectedLaw).Append('|').Append(confirmLaw).Append('|').Append(G.LawCooldownLeft > 0).Append('|');
            foreach (var s in LawsNow) sb.Append((int)s.State).Append(s.Reasons.Count);
            return sb.ToString();
        }

        static bool Excludes(LawDefinition a, LawDefinition b) => a.Excludes.Contains(b.Id) || b.Excludes.Contains(a.Id);

        void BuildDrawerCustoms(VisualElement host)
        {
            var statuses = LawsNow;
            if (statuses.Count == 0) return;
            if (selectedLaw == null || statuses.All(s => s.Law.Id != selectedLaw))
                selectedLaw = (statuses.FirstOrDefault(s => s.State == LawState.Available) ?? statuses[0]).Law.Id;

            var chip = E.Div().Row().Radius(999).Pad(4, 9).Bg(Theme.Alpha(InkBrown, 0.1f)).Ignore();
            var clockIcon = E.Icon("clock", 12, InkSoft);
            chip.Add(clockIcon);
            var ct = Ink("", 11, InkBrown, Theme.F.Mono);
            ct.style.marginLeft = 4;
            Bind(() =>
            {
                double left = G.LawCooldownLeft;
                string s = left > 0 ? $"Next law in {Fmt.Clock(left)}" : "A law can be signed";
                if (ct.text != s) ct.text = s;
                ct.style.color = left > 0 ? InkBrown : Leaf;
                clockIcon.style.unityBackgroundImageTintColor = left > 0 ? InkSoft : Leaf;
            });
            chip.Add(ct);
            host.Add(DrawerHeader("customs", "Book of Customs", () =>
            {
                int signed = G.State.Laws.Count;
                string d = G.HasDiscontent ? $" · discontent {(int)Math.Round(G.State.Discontent)} ({G.DiscontentMood})" : "";
                return $"{signed}/{G.Content.Laws.Count} customs signed{d}";
            }, chip));

            var body = E.Div().Row(0, Align.Stretch).Ignore();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.style.minHeight = 0;

            var board = new ScrollView(ScrollViewMode.Vertical);
            board.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            board.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            board.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            board.style.flexGrow = 1;
            board.style.flexShrink = 1;
            board.style.minWidth = 0;
            board.contentContainer.style.flexDirection = FlexDirection.Row;
            board.contentContainer.style.alignItems = Align.FlexStart;
            board.contentContainer.style.paddingLeft = 8;
            board.contentContainer.style.paddingRight = 4;
            board.contentContainer.style.paddingTop = 6;
            board.contentContainer.style.paddingBottom = 8;
            foreach (var branch in statuses.Select(s => s.Law.Branch).Distinct())
            {
                var laws = statuses.Where(s => s.Law.Branch == branch).ToList();
                var col = E.Div().Col().Ignore().Grow();
                col.style.flexBasis = 0;
                col.style.marginRight = 6;
                var head = E.Div().Row().Center().Radius(8).Pad(3, 6).Margin(0, 0, 5).Bg(Theme.Alpha(InkBrown, 0.08f)).Ignore();
                head.Add(E.Icon(branch == "Community" ? "people" : "flame", 11, InkSoft));
                var hl = Ink($"{branch} · {laws.Count(s => s.State == LawState.Signed)}/{laws.Count}", 10, InkSoft);
                hl.style.marginLeft = 4;
                head.Add(hl);
                col.Add(head);
                // Laws that rule each other out are drawn together, joined by "or".
                var done = new HashSet<string>();
                foreach (var s in laws)
                {
                    if (!done.Add(s.Law.Id)) continue;
                    var group = new List<LawStatus> { s };
                    foreach (var o in laws)
                        if (!done.Contains(o.Law.Id) && Excludes(s.Law, o.Law)) { group.Add(o); done.Add(o.Law.Id); }
                    if (group.Count == 1) { col.Add(LawNode(s)); continue; }
                    var box = E.Div().Col().Radius(14).Pad(4).Margin(0, 0, 5).Bg(Theme.Alpha(InkBrown, 0.05f)).Border(Theme.Alpha(InkBrown, 0.3f), 1.5f).Ignore();
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (i > 0) box.Add(OrConnector());
                        var node = LawNode(group[i]);
                        node.style.marginBottom = 0;
                        box.Add(node);
                    }
                    col.Add(box);
                }
                board.Add(col);
            }
            body.Add(board);

            var side = new ScrollView(ScrollViewMode.Vertical);
            side.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            side.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            side.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            side.style.width = Mathf.Clamp((PanelW - SafeLeft - SafeRight) * 0.36f, 230, 290);
            side.style.flexShrink = 0;
            side.contentContainer.style.paddingRight = 8;
            side.contentContainer.style.paddingTop = 6;
            side.contentContainer.style.paddingBottom = 8;
            side.Add(LawDetail(statuses.First(s => s.Law.Id == selectedLaw)));
            body.Add(side);
            host.Add(body);
        }

        static VisualElement OrConnector()
        {
            var row = E.Div().Row().Ignore();
            row.style.height = 16;
            var l1 = E.Div().Ignore().Grow().Bg(Theme.Alpha(InkBrown, 0.25f));
            l1.style.height = 1;
            row.Add(l1);
            var pill = E.Div().Ignore().Radius(999).Pad(0, 7).Bg(Theme.Alpha(InkBrown, 0.12f));
            pill.Add(Ink("or", 9.5f, InkSoft, Theme.F.TextBold));
            pill.style.marginLeft = 4;
            pill.style.marginRight = 4;
            row.Add(pill);
            var l2 = E.Div().Ignore().Grow().Bg(Theme.Alpha(InkBrown, 0.25f));
            l2.style.height = 1;
            row.Add(l2);
            return row;
        }

        /// <summary>The wax seal of a law: red and stamped once signed.</summary>
        static VisualElement Seal(LawState state, float size)
        {
            var s = E.Div().Size(size, size).Radius(size / 2).Center().Ignore();
            switch (state)
            {
                case LawState.Signed:
                    s.BgImage(Theme.Gradient("waxseal", true, Theme.Hex("#DE5A42"), Theme.Hex("#9A2A1C"))).Border(Theme.Hex("#6E1A10"), 2f);
                    s.Add(E.Icon("check", size * 0.5f, Theme.Hex("#FFE3D6")));
                    break;
                case LawState.Available:
                    s.BgImage(Theme.Gradient("goldround", true, GoldTop, GoldBottom)).Border(GoldEdge, 1.5f);
                    s.Add(E.Icon("customs", size * 0.52f, Theme.Hex("#5A3208")));
                    break;
                default:
                    s.Bg(Theme.Alpha(InkBrown, 0.3f));
                    s.Add(E.Icon("lock", size * 0.5f, Color.white));
                    break;
            }
            return s;
        }

        VisualElement LawNode(LawStatus st)
        {
            var law = st.Law;
            bool sel = law.Id == selectedLaw;
            Color fill, edge;
            switch (st.State)
            {
                case LawState.Signed: fill = Theme.Hex("#F6DCCB"); edge = Theme.Alpha(Ruby, 0.7f); break;
                case LawState.Available: fill = Theme.Hex("#FFF3CF"); edge = GoldBottom; break;
                default: fill = Theme.Hex("#E9D9B6"); edge = Theme.Alpha(InkBrown, 0.3f); break;
            }
            var n = E.Div().Row().Radius(11).Pad(5, 6).Margin(0, 0, 5).Bg(fill).Border(sel ? InkBrown : edge, sel ? 2.5f : 1.5f);
            n.Add(Seal(st.State, 24));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 6;
            var name = Ink(law.Name, 11, st.State == LawState.Locked ? InkSoft : InkBrown);
            name.style.whiteSpace = WhiteSpace.Normal;
            col.Add(name);
            string meta; Color mc;
            switch (st.State)
            {
                case LawState.Signed: meta = "Signed"; mc = Ruby; break;
                case LawState.Available: meta = "Can be signed"; mc = Leaf; break;
                default: meta = st.Reasons.FirstOrDefault() ?? "Locked"; mc = InkSoft; break;
            }
            var m = Ink(meta, 9.5f, mc, Theme.F.TextMedium).NoWrap();
            col.Add(m);
            n.Add(col);
            string id = law.Id;
            Tap(n, () => { selectedLaw = id; confirmLaw = null; });
            return n;
        }

        /// <summary>Plain-language effects of a law, each marked good or bad for the settlement.</summary>
        List<(string text, bool good)> LawEffects(LawDefinition law)
        {
            var list = new List<(string, bool)>();
            foreach (var e in law.Effects)
            {
                double v = e.Value ?? 1;
                string pct = ((int)Math.Round((v - 1) * 100)).ToString("+0;−0;0", Inv) + "%";
                string pts = v.ToString("+0.#;−0.#;0", Inv);
                switch (e.Kind)
                {
                    case EffectKind.ConsumptionMultiplier: list.Add(($"{pct} food eaten", v < 1)); break;
                    case EffectKind.SicknessMultiplier: list.Add(($"{pct} illness", v < 1)); break;
                    case EffectKind.RecoveryMultiplier: list.Add(($"{pct} recovery speed", v > 1)); break;
                    case EffectKind.DeathMultiplier: list.Add(($"{pct} deadly illness", v < 1)); break;
                    case EffectKind.WorkRateMultiplier: list.Add(($"{pct} work", v > 1)); break;
                    case EffectKind.JobRateMultiplier: list.Add(($"{pct} {G.Content.JobConfig(e.Job ?? JobType.Research)?.Name ?? e.Job?.Id()}", v > 1)); break;
                    case EffectKind.ExperienceMultiplier: list.Add(($"{pct} skill gain", v > 1)); break;
                    case EffectKind.GriefMultiplier: list.Add(($"{pct} grief from deaths", v < 1)); break;
                    case EffectKind.FuelMultiplier: list.Add(($"{pct} firewood burnt", v < 1)); break;
                    case EffectKind.GrowthMultiplier: list.Add(($"{pct} growth", v > 1)); break;
                    case EffectKind.SpoilageMultiplier: list.Add(($"{pct} spoilage", v < 1)); break;
                    case EffectKind.StorageMultiplier: list.Add(($"{pct} {e.Resource?.DisplayName().ToLowerInvariant()} storage", v > 1)); break;
                    case EffectKind.ConstructionSpeedMultiplier: list.Add(($"{pct} building speed", v > 1)); break;
                    case EffectKind.ApprovalBonus: list.Add(($"{pts} approval", v > 0)); break;
                    case EffectKind.DiscontentPerHour: list.Add(($"{pts} discontent per hour", v < 0)); break;
                    case EffectKind.HeatBonus: list.Add(($"{SignedDeg(v)} warmth from the {G.HeartName}", v > 0)); break;
                }
            }
            if (Math.Abs(law.OnSign) > 0.01) list.Add(($"{law.OnSign.ToString("+0.#;−0.#;0", Inv)} discontent when signed", law.OnSign < 0));
            return list;
        }

        VisualElement LawDetail(LawStatus st)
        {
            var law = st.Law;
            var card = E.Div().Col().Radius(16).Pad(10, 12).Bg(Theme.Hex("#FFF6DE")).Border(Theme.Alpha(InkBrown, 0.35f), 1.5f);
            card.style.flexShrink = 0;
            var head = E.Div().Row().Ignore();
            var names = E.Div().Col().Grow().Ignore();
            names.Add(Ink(law.Branch.ToUpperInvariant(), 9, InkSoft, Theme.F.TextBold));
            var title = Ink(law.Name, 15, InkBrown, Theme.F.DisplayBold);
            title.style.whiteSpace = WhiteSpace.Normal;
            names.Add(title);
            head.Add(names);
            head.Add(Seal(st.State, 34));
            card.Add(head);
            var desc = Ink(law.Description, 10.5f, InkSoft, Theme.F.TextMedium).Margin(5);
            desc.style.whiteSpace = WhiteSpace.Normal;
            card.Add(desc);

            foreach (var (text, good) in LawEffects(law))
            {
                var r = E.Div().Row().Margin(4).Ignore();
                r.Add(E.Icon(good ? "check" : "alert", 11, good ? Leaf : Ruby));
                var t = Ink(text, 10.5f, good ? Leaf : Ruby).Grow();
                t.style.marginLeft = 5;
                t.style.whiteSpace = WhiteSpace.Normal;
                r.Add(t);
                card.Add(r);
            }
            var rivals = G.Content.Laws.Where(o => o.Id != law.Id && Excludes(law, o)).Select(o => o.Name).ToList();
            if (rivals.Count > 0)
            {
                var x = Ink($"Either this or {string.Join(", ", rivals)}: signing one rules out the other for good.", 10, Plum, Theme.F.TextMedium).Margin(6);
                x.style.whiteSpace = WhiteSpace.Normal;
                card.Add(x);
            }
            if (law.RequiresLaw != null)
                card.Add(Ink($"Follows {G.Content.Law(law.RequiresLaw)?.Name ?? law.RequiresLaw}.", 10, InkSoft, Theme.F.TextMedium).Margin(4));

            var bottom = E.Div().Col().Margin(10).Ignore();
            string id = law.Id;
            switch (st.State)
            {
                case LawState.Signed:
                {
                    var r = E.Div().Row().Ignore();
                    r.Add(E.Icon("customs", 14, Ruby));
                    var t = Ink("Signed into custom", 12, Ruby);
                    t.style.marginLeft = 6;
                    r.Add(t);
                    bottom.Add(r);
                    if (!string.IsNullOrEmpty(law.LegacyText))
                    {
                        var lt = Ink(law.LegacyText, 10.5f, InkSoft, Theme.F.Text).Margin(5);
                        lt.style.whiteSpace = WhiteSpace.Normal;
                        lt.style.unityFontStyleAndWeight = FontStyle.Italic;
                        bottom.Add(lt);
                    }
                    break;
                }
                case LawState.Locked:
                    foreach (var reason in st.Reasons)
                    {
                        var r = E.Div().Row().Margin(3).Ignore();
                        r.Add(E.Icon("lock", 11, Ruby));
                        var t = Ink(reason, 10.5f, Ruby).Grow();
                        t.style.marginLeft = 5;
                        t.style.whiteSpace = WhiteSpace.Normal;
                        r.Add(t);
                        bottom.Add(r);
                    }
                    break;
                default:
                    if (confirmLaw == id)
                    {
                        var warn = E.Div().Row(0, Align.FlexStart).Radius(12).Pad(8, 10).Bg(Theme.Alpha(Ruby, 0.1f)).Border(Theme.Alpha(Ruby, 0.45f), 1.5f).Ignore();
                        warn.Add(E.Icon("alert", 14, Ruby));
                        var wt = Ink($"A custom is for good: once signed, {law.Name} can never be revoked.", 10.5f, Ruby).Grow();
                        wt.style.marginLeft = 6;
                        wt.style.whiteSpace = WhiteSpace.Normal;
                        warn.Add(wt);
                        bottom.Add(warn);
                        var buttons = E.Div().Row().Margin(8).Ignore();
                        buttons.Add(ParchButton("Cancel", () => confirmLaw = null));
                        var sign = E.Button("Sign for good", Act(() =>
                        {
                            confirmLaw = null;
                            store.SignLaw(id);
                        }), E.Kind.Gold, 36, "customs", 12.5f).Grow();
                        sign.style.marginLeft = 8;
                        buttons.Add(sign);
                        bottom.Add(buttons);
                    }
                    else if (G.LawCooldownLeft > 0)
                    {
                        var b = E.Button("", null, E.Kind.Disabled, 36, "clock", 12.5f);
                        var l = b.Q<Label>();
                        Bind(() => { string s = $"Next custom in {Fmt.Clock(G.LawCooldownLeft)}"; if (l.text != s) l.text = s; });
                        bottom.Add(b);
                        var hint = Ink("The people need time to get used to each new custom.", 10, InkSoft, Theme.F.TextMedium).Margin(5);
                        hint.style.whiteSpace = WhiteSpace.Normal;
                        bottom.Add(hint);
                    }
                    else
                        bottom.Add(E.Button("Sign the custom", Act(() => confirmLaw = id), E.Kind.Gold, 36, "customs", 12.5f));
                    break;
            }
            card.Add(bottom);
            return card;
        }

        /// <summary>A quiet pill button for parchment (the dark ghost button would vanish on it).</summary>
        VisualElement ParchButton(string label, Action onTap)
        {
            var b = E.Div().Row().Center().Radius(18).Pad(0, 16).Bg(Theme.Alpha(InkBrown, 0.1f)).Border(Theme.Alpha(InkBrown, 0.35f), 1.5f);
            b.style.height = 36;
            b.style.flexShrink = 0;
            b.Add(Ink(label, 12.5f, InkBrown).NoWrap());
            Tap(b, onTap);
            return b;
        }

        // MARK: - Petition dialog

        bool petitionShowing;

        VisualElement PetitionCard()
        {
            var p = G.PendingPetition;
            var d = G.Content.Balance.Discontent;
            var c = DialogShell(out var left, out var right, 40);

            left.BgImage(Theme.Gradient("petitionart", true, Theme.Hex("#3b2a2a"), Theme.Hex("#18141a")));
            left.Add(E.Div().Abs(0, 0, 0, 0).BgImage(Theme.GoldGlow).Opacity(0.5f).Ignore());
            var story = PaneBody(16, 18, 16);
            var tag = E.Div().Row().Radius(999).Pad(6, 10).Bg(new Color(0.05f, 0.06f, 0.08f, 0.6f)).Border(Theme.Alpha(Theme.Error, 0.45f)).Ignore();
            tag.style.alignSelf = Align.FlexStart;
            tag.Add(E.Eyebrow("Petition", Theme.Error, 9.5f));
            story.Add(tag);
            var mark = E.Icon("discontent", 40, MoodColor(G.DiscontentMood)).Margin(12);
            mark.style.alignSelf = Align.Center;
            story.Add(mark);
            story.Add(E.Text(p.Title, 21, Theme.Ink, Theme.F.Display).Margin(10));
            var text = E.Text(p.Text, 13, Theme.Body).Margin(6);
            text.style.unityFontStyleAndWeight = FontStyle.Italic;
            story.Add(text);
            var mood = E.Div().Row().Margin(12).Ignore();
            mood.Add(E.Icon("discontent", 13, Theme.Muted));
            var mt = Live(() => $"Discontent {(int)Math.Round(G.State.Discontent)} · {G.DiscontentMood}", 11.5f, Theme.Muted);
            mt.style.marginLeft = 6;
            mood.Add(mt);
            story.Add(mood);
            left.Add(story);

            var body = PaneBody(14, 16, 2);
            body.Add(E.Eyebrow("The people ask", Theme.Muted).Margin(2, 0, 0, 4));
            var ask = E.Div().Col().Radius(18).Pad(12, 14).BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.5f)).Margin(10);
            var ah = E.Div().Row(0, Align.FlexStart).Ignore();
            ah.Add(E.Icon("goals", 16, Theme.Gold));
            var at = E.Text(p.Promise, 14, Theme.Ink, Theme.F.TextBold).Grow();
            at.style.marginLeft = 8;
            ah.Add(at);
            ask.Add(ah);
            var due = E.Div().Row().Margin(8).Ignore();
            due.Add(E.Icon("clock", 12, Theme.GoldLight));
            var dt = E.Text($"To be kept within {Game.Duration(p.DeadlineSeconds)}", 12, Theme.GoldLight, Theme.F.TextMedium);
            dt.style.marginLeft = 6;
            due.Add(dt);
            ask.Add(due);
            foreach (var req in G.PetitionGoalStatus(p)) ask.Add(Check(req.Text, req.Met).Margin(6));
            body.Add(ask);
            if (d != null)
            {
                body.Add(Consequence("check", $"Promise: discontent −{Num(d.PromiseRelief)} now; kept, −{Num(d.KeptRelief)} more and +{Num(d.KeptApproval)} approval.", Theme.Mint).Margin(10));
                body.Add(Consequence("alert", $"A broken promise: +{Num(d.BrokenAnger)} discontent. Refusing: +{Num(d.RefusedAnger)} now.", Theme.Error).Margin(6));
            }
            right.Add(body);

            var foot = PaneFooter(16);
            var buttons = E.Div().Row(0, Align.Stretch).Ignore();
            var refuse = E.Button("Refuse", Act(() => store.AnswerPetition(false)), E.Kind.Ghost, 48);
            refuse.style.height = StyleKeyword.Auto;
            refuse.style.minHeight = 48;
            buttons.Add(refuse);
            var promise = E.Div().Row().Center().Radius(18).Pad(7, 14).BgImage(Theme.GoldFill).Grow();
            promise.style.minHeight = 48;
            promise.style.marginLeft = 10;
            promise.Add(E.Text($"Promise: {p.Promise} ({Game.Duration(p.DeadlineSeconds)})", 13, Theme.OnGold, Theme.F.TextBold).Align(TextAnchor.MiddleCenter).Grow());
            Tap(promise, () => store.AnswerPetition(true));
            buttons.Add(promise);
            foot.Add(buttons);
            right.Add(foot);
            return c;
        }

        static VisualElement Consequence(string icon, string text, Color color)
        {
            var r = E.Div().Row(0, Align.FlexStart).Ignore();
            r.Add(E.Icon(icon, 13, color));
            var t = E.Text(text, 11.5f, Theme.Body).Grow();
            t.style.marginLeft = 7;
            r.Add(t);
            return r;
        }
    }

    static class SurvivalUx
    {
        /// <summary>Runs a setup step on an element inline and returns it.</summary>
        public static T Also<T>(this T e, Action<T> a) where T : VisualElement { a(e); return e; }
    }
}
