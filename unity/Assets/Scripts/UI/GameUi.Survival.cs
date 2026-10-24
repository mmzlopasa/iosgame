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
    /// healers, discontent beside hope (approval), the Book of Customs and the people's petitions,
    /// drawn with the same kit components as everything else.
    ///
    /// Expensive core queries (the warmth of every home, approval, discontent factors, the
    /// patients under care, the law statuses) are computed at most once per UI tick and shared by
    /// every binding through the cached properties below.
    /// </summary>
    public partial class GameUi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // MARK: - Entry points (capture tour, deep links)

        /// <summary>Opens the Weather & Warmth window.</summary>
        public void ShowWeather() => OpenPanel(Panel.Weather);

        /// <summary>Opens the Book of Customs, optionally on one law (and on its confirm step).</summary>
        public void ShowCustoms(string lawId = null, bool confirm = false)
        {
            if (lawId != null && G.Content.Law(lawId) != null) selectedLaw = lawId;
            confirmLaw = confirm ? lawId : null;
            OpenPanel(Panel.Customs);
        }

        /// <summary>Opens People & Work on its wellbeing tab (health, hope and discontent).</summary>
        public void ShowPeople()
        {
            peopleTab = 2;
            OpenPanel(Panel.People);
        }

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
        /// the Weather window is open, a home or a fire is being placed, or the heart is selected. Only
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

        /// <summary>Text colours for how warm it feels (outlined text on parchment or the dark capsule).</summary>
        static Color ExposureColor(Exposure e) => e switch
        {
            Exposure.Comfortable => Theme.Hex("#FFD27A"),
            Exposure.Chilly => Theme.Hex("#E8F2FA"),
            Exposure.Cold => Theme.Icy,
            _ => Theme.Bad,
        };

        static E.Cell ExposureCell(Exposure e) => e switch
        {
            Exposure.Comfortable => E.Cell.Legendary,
            Exposure.Chilly => E.Cell.Common,
            Exposure.Cold => E.Cell.Rare,
            _ => E.Cell.Epic,
        };

        static Color MoodColor(string mood) => mood switch
        {
            "Revolt" => Theme.Bad,
            "Unrest" => Theme.Hex("#FFB066"),
            "Grumbling" => Theme.Gold,
            _ => Theme.Up,
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

        // MARK: - The promise card (left rail)

        /// <summary>The promise made to the people: what is still owed and the time left.</summary>
        VisualElement PromiseCard()
        {
            var p = G.ActivePetitionDefinition;
            if (p == null || G.State.ActivePetition == null) return null;
            var card = E.Panel().Pad(7, 11, 11, 11);
            card.style.width = LeftRailW;
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("discontent", 24));
            float timeW = Theme.TextWidth("0:00:00", Theme.Small);
            head.Add(E.T($"Promise: {p.Title}", Theme.Body).Fit(LeftRailW - 22 - 24 - 6 - timeW - 6).Flex().Margin(0, 0, 0, 6));
            var left = E.T("", Theme.Small);
            left.style.marginLeft = 6;
            Bind(() =>
            {
                double s = (G.State.ActivePetition?.DeadlineSimTime ?? 0) - G.State.SimTime;
                string txt = Fmt.Clock(s);
                if (left.text != txt) left.text = txt;
                E.Tint(left, s < 600 ? Theme.Bad : Color.white);
            });
            head.Add(left);
            card.Add(head);
            var goal = E.T("", Theme.Small, Theme.Pale).Wrap();
            goal.style.marginTop = 3;
            goal.style.maxWidth = LeftRailW - 22;
            var bar = E.Bar(out var fill, 10);
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
            Tap(card, () => { peopleTab = 2; store.ActivePanel = Panel.People; });
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

        /// <summary>Frostpunk's overdrive in the heart's card: what stoking gives and costs, and its state.</summary>
        VisualElement FireSection()
        {
            var w = G.Content.Balance.Warmth;
            bool stoked = G.IsStoked;
            var sec = E.Section(stoked ? "The fire roars" : "Stoke the fire", out var rows, LiveFire());
            sec.style.marginTop = 8;
            string cheer = w.StokeApproval > 0.01 ? $" People cheer it (+{Num(w.StokeApproval)} hope)." : "";
            rows.Add(E.Ink($"Burns {Fmt.Amount(w.StokeWoodPerSecond * 3600)} wood per hour for {Game.Duration(w.StokeSeconds)}: " +
                           $"{SignedDeg(w.StokeHeat)} warmth and {Num(w.StokeRadius)} more cells of reach.{cheer}", Theme.Body).Margin(6, 0, 6));
            if (stoked)
            {
                var bar = LiveLabeledBar(() => (G.State.StokedUntilSimTime - G.State.SimTime) / Math.Max(1, w.StokeSeconds),
                                         () => $"{Fmt.Clock(G.State.StokedUntilSimTime - G.State.SimTime)} left", 20, E.FillKind.Red);
                bar.style.marginBottom = 8;
                rows.Add(bar);
            }
            else if (G.StokeReadyIn > 0)
            {
                var r = E.Line(true);
                r.Add(E.RowIcon("clock"));
                r.Add(E.T("The fire rests", Theme.Body).Flex());
                r.Add(Live(() => $"again in {Fmt.Clock(G.StokeReadyIn)}", Theme.Num));
                rows.Add(r);
            }
            return sec;
        }

        /// <summary>The heart's live warmth and reach for the section header.</summary>
        Label LiveFire() => Live(() =>
        {
            var (heat, radius) = G.HeartFire();
            return heat <= 0 ? "No warmth" : $"{SignedDeg(heat)} · {Num(radius)} cells";
        }, Theme.Num, Theme.Gold);

        /// <summary>The orange Stoke button (the kit's "Finish Now" slot): wood cost, or the time until it can roar again.</summary>
        VisualElement StokeButton()
        {
            bool lit = G.Heart?.IsOperational ?? false;
            if (G.IsStoked)
            {
                var b = E.Button("Roaring", null, E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("flame", ""));
                var l = b.ElementAt(1).ElementAt(1) as Label;
                Bind(() => { string s = Fmt.Clock(G.State.StokedUntilSimTime - G.State.SimTime); if (l != null && l.text != s) l.text = s; });
                return b;
            }
            if (G.StokeReadyIn > 0)
            {
                var b = E.Button("Stoke", null, E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("clock", ""));
                var l = b.ElementAt(1).ElementAt(1) as Label;
                Bind(() => { string s = Fmt.Clock(G.StokeReadyIn); if (l != null && l.text != s) l.text = s; });
                return b;
            }
            if (!lit) return E.Button("Stoke", null, E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("alert", "Not lit", Theme.Pale));
            bool can = G.State.Resources.Wood >= StokeNeed;
            // Blue: the yellow is the card's primary (Level up) beside it.
            return E.Button("Stoke", can ? Act(store.StokeHeart) : null, can ? E.Kind.Blue : E.Kind.Grey, Theme.ButtonH, null, 16,
                            SubLine("wood", Fmt.Compact(Math.Ceiling(StokeNeed)), can ? (Color?)null : Theme.Bad));
        }

        // MARK: - Expeditions

        VisualElement RegionFinds(RegionDefinition r, bool explored)
        {
            if (explored)
            {
                if (string.IsNullOrEmpty(r.Finding)) return null;
                return E.T(r.Finding, Theme.Body, Theme.Up).Wrap().Margin(4, 0, 6);
            }
            var cells = new List<VisualElement>();
            if (r.Survivors > 0) cells.Add(RewardCell("people", E.Cell.Rare, r.Survivors.ToString(Inv), 46));
            foreach (var kv in r.Loot.Entries)
                if (kv.Value > 0) cells.Add(RewardCell(IconFor(kv.Key), CellFor(kv.Key), Fmt.Compact(kv.Value), 46));
            if (cells.Count == 0) return null;
            var row = E.Div().Row().Ignore().Margin(4, 0, 6);
            row.style.flexWrap = Wrap.Wrap;
            row.Add(E.T("Scouts may find", Theme.Body, Theme.Pale).Margin(0, 8));
            foreach (var c in cells) { c.style.marginRight = 8; row.Add(c); }
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
            var value = E.T("", Theme.Num);
            var card = Block("Discontent", out var rows, value);
            var bar = E.Bar(out var fill, 14, E.FillKind.Green);
            foreach (double k in new[] { d.Grumbling, d.Unrest })
            {
                var tick = E.Div().Ignore().Bg(Theme.Outline).Abs(null, 2, null, 2);
                tick.style.width = 2;
                tick.style.left = Length.Percent((float)k);
                bar.Add(tick);
            }
            bar.style.marginTop = 10;
            rows.Add(bar);
            string lastMood = null;
            Bind(() =>
            {
                string mood = G.DiscontentMood;
                string s = $"{(int)Math.Round(G.State.Discontent)} · {mood}";
                if (value.text != s) value.text = s;
                E.Tint(value, MoodColor(mood));
                E.SetFill(fill, G.State.Discontent / 100);
                if (mood != lastMood)
                {
                    lastMood = mood;
                    E.SetFillKind(fill, mood == "Revolt" || mood == "Unrest" ? E.FillKind.Red : mood == "Grumbling" ? E.FillKind.Gold : E.FillKind.Green);
                }
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
            }, Theme.Body, Theme.Pale);
            effect.style.marginTop = 6;
            effect.style.marginBottom = 6;
            rows.Add(effect);
            var factors = DiscFactors;
            for (int i = 0; i < factors.Count; i++)
            {
                int index = i;
                bool up = factors[i].PerHour > 0;
                var r = E.Line(true, Theme.RowTightH);
                r.Add(E.T(factors[i].Label, Theme.Body).Flex());
                r.Add(Live(() => { var f = DiscFactors; return index < f.Count ? PerHour(f[index].PerHour) : ""; }, Theme.Num, up ? Theme.Bad : Theme.Up).NoShrink().Margin(0, 0, 0, 8));
                rows.Add(r);
            }

            var promise = G.ActivePetitionDefinition;
            if (promise != null && G.State.ActivePetition != null)
            {
                var r = E.Line(true);
                r.Add(E.RowIcon("goals"));
                r.Add(E.T($"You promised: {promise.Promise}", Theme.Body, Theme.Gold).Flex());
                r.Add(Live(() => Fmt.Clock((G.State.ActivePetition?.DeadlineSimTime ?? 0) - G.State.SimTime), Theme.Num).NoShrink().Margin(0, 0, 0, 8));
                rows.Add(r);
                foreach (var req in G.PetitionGoalStatus(promise)) rows.Add(RequirementRow("next", req.Text, req.Met, null, true, WindowW));
                rows.Add(E.Ink($"Kept: −{Num(d.KeptRelief)} discontent and +{Num(d.KeptApproval)} hope. Broken: +{Num(d.BrokenAnger)} discontent.", Theme.Small).Margin(4, 0, 4));
            }
            rows.Add(E.Ink($"Hunger, cold, illness and homelessness stir discontent; content people calm it. From {Num(d.Grumbling)} people grumble and work slower, " +
                           $"from {Num(d.Unrest)} there is unrest, and at 100 families leave. Customs and promises calm them.", Theme.Small, Theme.InkSoft).Margin(4, 0, 6));
            host.Add(card);
        }

        /// <summary>Who is ill, how many the healers can tend and how fast people fall ill.</summary>
        void BuildHealth(VisualElement host)
        {
            if (!G.HasHealth) return;
            var card = Block("Health", out var rows, G.State.Deaths > 0 ? E.T($"{G.State.Deaths} lost", Theme.Num, Theme.Pale) : null);
            var row = E.Div().Row().Ignore().Margin(8, 0, 6);
            row.Add(StatBox("sick", "Ill", () => $"{G.SickCount}", () => G.SickCount > 0 ? Theme.Bad : Color.white));
            row.Add(StatBox("check", "Under care", () => $"{Math.Min(G.SickCount, G.CareCapacity)}/{G.SickCount}").Margin(0, 0, 0, 8));
            row.Add(StatBox("alert", "Falling ill", () => (G.IllnessRate() * 3600).ToString("0.0", Inv) + "/h").Margin(0, 0, 0, 8));
            rows.Add(row);
            var hut = G.Content.Building("healers_hut");
            int per = G.Content.Balance.Health.PatientsPerHealer;
            if (G.SickCount > G.CareCapacity)
                rows.Add(Notice("sick", G.State.Assignments[JobType.Healing] > 0
                    ? $"More are ill than the healers can tend ({per} each). Add healers or build another {hut?.Name ?? "healer's hut"}."
                    : $"Nobody tends the sick. Build a {hut?.Name ?? "healer's hut"} and assign healers: each tends {per}.", true).Margin(0, 0, 6));
            rows.Add(E.Ink("People in cold homes, without a home or without food fall ill. Under a healer's care they get well quickly; " +
                           "left alone in the cold an illness grows grave and may kill.", Theme.Small, Theme.InkSoft).Margin(4, 0, 6));
            host.Add(card);
        }

        /// <summary>Under a villager's name: ill, under care or not, and how far along the recovery is.</summary>
        void SickLine(VisualElement col, Person p, float width)
        {
            bool treated = Treated.Contains(p.Id);
            bool grave = p.Severity > 0.5;
            col.Add(E.T(treated ? "Ill · under care" : grave ? "Gravely ill · untreated" : "Ill · untreated", Theme.Small, treated ? Theme.Up : Theme.Bad).Fit(width));
            int pid = p.Id;
            var bar = E.Bar(out var fill, 8, E.FillKind.Green);
            bar.style.marginTop = 3;
            bar.style.width = Mathf.Min(width, 90);
            Bind(() =>
            {
                Person who = null;
                foreach (var x in G.State.People) if (x.Id == pid) { who = x; break; }
                E.SetFill(fill, who == null ? 1 : who.Recovery);
            });
            col.Add(bar);
        }

        // MARK: - Weather & Warmth window

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
                host.Add(Notice("summer", "The weather is mild here: nobody needs to worry about the cold."));
                return;
            }
            var clim = G.Content.Balance.Climate;

            // Now and the forecast, in one block.
            var forecast = G.Forecast();
            double hours = forecast.Count * clim.PeriodSeconds / 3600.0;
            var now = Block($"Forecast · next {Num(hours)} hours", out var rows);
            var hero = E.Div().Row().Ignore().Margin(8, 0, 6);
            var big = E.Slot("thermometer", 64, E.Cell.Rare, 0.7f);
            hero.Add(big);
            var col = E.Div().Col().Ignore().Margin(0, 12, 0, 10);
            col.Add(E.T("Outside now", Theme.Small, Theme.Pale));
            var temp = E.T("", 30);
            col.Add(temp);
            var feel = E.T("", Theme.Small, Theme.Pale);
            col.Add(feel);
            hero.Add(col);
            Bind(() =>
            {
                double t = G.Temperature;
                var e = G.ExposureOf(t);
                string s = Deg(t);
                if (temp.text != s) temp.text = s;
                E.Tint(temp, ExposureColor(e));
                string f = $"Feels {e.ToString().ToLowerInvariant()}";
                if (feel.text != f) feel.text = f;
            });
            // The forecast strip: one small cell per period (portrait: on its own line under "Outside now").
            var strip = E.Div().Row(0, Align.FlexStart).Grow().Ignore();
            foreach (var period in forecast)
            {
                var exposure = G.ExposureOf(period.Temperature);
                bool snap = period.Snap != null;
                var cell = E.Div().Col(Align.Center).Grow().Ignore();
                double start = period.StartSimTime;
                cell.Add(Live(() => Until(start - G.State.SimTime), Theme.Small, snap ? Theme.Icy : Theme.Pale));
                var slot = E.Slot(snap ? "winter" : "thermometer", 38, snap ? E.Cell.Rare : ExposureCell(exposure), 0.62f);
                slot.style.marginTop = 3;
                cell.Add(slot);
                cell.Add(E.T(Deg(period.Temperature), Theme.Body, ExposureColor(exposure)).Margin(2));
                strip.Add(cell);
            }
            if (Portrait)
            {
                rows.Add(hero);
                strip.style.marginBottom = 6;
                // In the column it must keep its own height, not grow or get squeezed under the rows.
                strip.style.flexGrow = 0;
                strip.style.flexShrink = 0;
                strip.style.flexBasis = StyleKeyword.Auto;
                rows.Add(strip);
            }
            else
            {
                hero.Add(strip);
                rows.Add(hero);
            }
            var snapNow = CurrentSnap();
            if (snapNow != null)
                rows.Add(Notice("winter", $"{snapNow.Name}: {Num(snapNow.Drop)}° colder than usual. Keep the woodpile full and the fires burning.").Margin(0, 0, 6));
            else if (G.Freezing)
                rows.Add(Notice("alert", "The woodpile is empty: the fires are out and people are freezing.", true).Margin(0, 0, 6));
            // Cold snaps in sight, one row per spell.
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
                var r = E.Line(true);
                r.Add(E.RowIcon("winter"));
                r.Add(E.T(snap.Name, Theme.Body, Theme.Icy).NoShrink());
                r.Add(Live(() =>
                {
                    double t = G.State.SimTime;
                    return start <= t ? (lastsBeyond ? "now" : $"now · ends in {Game.Duration(end - t)}") : $"in {Game.Duration(start - t)}";
                }, Theme.Body, Theme.Pale).Margin(0, 0, 0, 10).Flex());
                r.Add(E.T($"−{Num(snap.Drop)}°", Theme.Num, Theme.Icy).NoShrink().Margin(0, 0, 0, 8));
                rows.Add(r);
            }
            if (!anySnap) rows.Add(E.StatRow("winter", "Cold snaps", "None in sight", null, true, Theme.Pale));
            host.Add(now);

            if (!G.HasWarmth) return;
            var wc = G.Content.Balance.Warmth;

            // Homes: people by how warm they sleep, then the coldest homes.
            var homes = Block("Homes", out var hrows);
            var grid = E.Div().Row().Ignore().Margin(8, 0, 6);
            foreach (Exposure e in new[] { Exposure.Comfortable, Exposure.Chilly, Exposure.Cold, Exposure.Freezing })
            {
                int k = (int)e;
                var c = E.Div().Col(Align.Center).Grow().Ignore();
                var cell = E.Slot("people", 52, ExposureCell(e), 0.62f);
                c.Add(cell);
                // The count under the cell, clear of its frame, then the name.
                c.Add(Live(() => Warm.People[k].ToString(Inv), Theme.H2).Align(TextAnchor.MiddleCenter).Margin(2));
                c.Add(E.T(e.ToString(), Theme.Body, ExposureColor(e)).Margin(3));
                grid.Add(c);
            }
            hrows.Add(grid);
            if (Warm.Homeless > 0)
            {
                var hl = E.Line(true);
                hl.Add(E.RowIcon("people"));
                hl.Add(Live(() => $"{Warm.Homeless} without a home sleep beside the fire ({Deg(Warm.HomelessWarmth)}). Build shelters.", Theme.Body, Theme.Bad).Flex());
                hrows.Add(hl);
            }
            // Coldest homes first (tap one to see it).
            foreach (var h in Warm.Homes.Where(x => x.Occupants > 0).OrderBy(x => x.Warmth).Take(6))
            {
                int id = h.Building.Id;
                var r = E.Line(true);
                r.pickingMode = PickingMode.Position;
                r.Add(E.RowIcon("home"));
                r.Add(E.T($"{G.DisplayName(h.Building)} · {h.Occupants}", Theme.Body).Flex());
                var v = E.T("", Theme.Num).NoShrink().Margin(0, 0, 0, 8);
                Bind(() =>
                {
                    foreach (var x in Warm.Homes)
                        if (x.Building.Id == id)
                        {
                            string s = $"{Deg(x.Warmth)} {x.Exposure}";
                            if (v.text != s) v.text = s;
                            E.Tint(v, ExposureColor(x.Exposure));
                            return;
                        }
                });
                r.Add(v);
                Tap(r, () => store.FocusBuilding(id));
                hrows.Add(r);
            }
            hrows.Add(E.Ink($"Comfortable from {Deg(wc.Comfortable)}, cold below {Deg(wc.Cold)}, freezing below {Deg(wc.Freezing)}. " +
                            "Better walls (levels) and fires nearby keep homes warm; cold homes make people ill and discontent.", Theme.Small, Theme.InkSoft).Margin(4, 0, 6));
            host.Add(homes);

            // The heart.
            var heart = Block(G.HeartName, out var frows, LiveFire());
            var fuel = E.Line(false);
            fuel.Add(E.RowIcon("wood"));
            fuel.Add(E.T("Firewood burnt", Theme.Body).Flex());
            fuel.Add(Live(() => $"{(G.FuelPerSecond * 60).ToString("0.0", Inv)}/m", Theme.Num));
            frows.Add(fuel);
            var lasts = E.Line(true);
            lasts.Add(E.RowIcon("clock"));
            lasts.Add(E.T("Woodpile lasts", Theme.Body).Flex());
            lasts.Add(Live(() =>
            {
                double burn = G.FuelPerSecond, net = econ.Net.Wood;
                if (burn <= 0) return "No fires needed";
                if (net >= 0) return "Growing";
                return Fmt.Clock(G.State.Resources.Wood / -net);
            }, Theme.Num));
            frows.Add(lasts);
            if (G.Heart != null)
            {
                var stoke = E.Line(true, 58);
                stoke.Add(E.T(G.IsStoked ? "The fire roars" : "Stoke for more warmth", Theme.Body).Flex());
                var b = StokeButton();
                b.style.width = 160;
                b.style.marginLeft = 8;
                stoke.Add(b);
                frows.Add(stoke);
            }
            var brazier = G.Content.Decorations.FirstOrDefault(d => d.Heat != null);
            if (brazier != null)
                frows.Add(E.Ink($"{brazier.Name}s (Build › {brazier.Category}) warm the homes around them: {SignedDeg(brazier.Heat.Heat)} within {Num(brazier.Heat.Radius)} cells.", Theme.Small, Theme.InkSoft).Margin(4, 0, 6));
            host.Add(heart);
        }

        // MARK: - Book of Customs

        string selectedLaw, confirmLaw;

        string CustomsKey()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(selectedLaw).Append('|').Append(confirmLaw).Append('|').Append(G.LawCooldownLeft > 0).Append('|').Append(WindowW).Append('|');
            foreach (var s in LawsNow) sb.Append((int)s.State).Append(s.Reasons.Count);
            return sb.ToString();
        }

        static bool Excludes(LawDefinition a, LawDefinition b) => a.Excludes.Contains(b.Id) || b.Excludes.Contains(a.Id);

        const float LawDetailW = 248;

        void BuildCustoms(VisualElement host)
        {
            var statuses = LawsNow;
            if (statuses.Count == 0) return;
            if (selectedLaw == null || statuses.All(s => s.Law.Id != selectedLaw))
                selectedLaw = (statuses.FirstOrDefault(s => s.State == LawState.Available) ?? statuses[0]).Law.Id;
            if (Portrait) { BuildCustomsPortrait(host, statuses); return; }

            var body = E.Div().Row(0, Align.Stretch).Ignore();
            body.style.flexGrow = 1;
            body.style.minHeight = 0;

            var board = E.Scroll(ScrollViewMode.Vertical);
            board.style.flexGrow = 1;
            board.style.flexShrink = 1;
            board.style.minWidth = 0;
            board.contentContainer.style.flexDirection = FlexDirection.Row;
            board.contentContainer.style.alignItems = Align.FlexStart;
            board.contentContainer.style.paddingBottom = 6;
            var branches = statuses.Select(s => s.Law.Branch).Distinct().ToList();
            float colW = Mathf.Floor((InnerW - LawDetailW - 8 - 8 * (branches.Count - 1)) / Math.Max(1, branches.Count));
            for (int bi = 0; bi < branches.Count; bi++)
            {
                string branch = branches[bi];
                var laws = statuses.Where(s => s.Law.Branch == branch).ToList();
                var col = E.Div().Col().Ignore().Size(colW);
                if (bi > 0) col.style.marginLeft = 8;
                var head = E.Div().Row().Center().Ignore();
                head.style.height = 28;
                head.Add(E.Icon(branch == "Community" ? "people" : "flame", 22));
                head.Add(E.T($"{branch} {laws.Count(s => s.State == LawState.Signed)}/{laws.Count}", Theme.Num).Fit(colW - 30).Margin(0, 0, 0, 5));
                col.Add(head);
                // Laws that rule each other out are drawn together, joined by "or".
                var done = new HashSet<string>();
                foreach (var s in laws)
                {
                    if (!done.Add(s.Law.Id)) continue;
                    var group = new List<LawStatus> { s };
                    foreach (var o in laws)
                        if (!done.Contains(o.Law.Id) && Excludes(s.Law, o.Law)) { group.Add(o); done.Add(o.Law.Id); }
                    if (group.Count == 1) { col.Add(LawNode(s, colW)); continue; }
                    var box = E.Div().Col().Ignore().Radius(12).Border(Theme.KitLook ? Theme.Rim : Theme.Wood4, 2f).Pad(4).Margin(0, 0, 8);
                    box.style.backgroundColor = Theme.KitLook ? new Color(0.03f, 0.05f, 0.09f, 0.35f) : Theme.Alpha(Theme.Wood4, 0.18f);
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (i > 0) box.Add(OrConnector());
                        var node = LawNode(group[i], colW - 12);
                        node.style.marginBottom = 0;
                        box.Add(node);
                    }
                    col.Add(box);
                }
                board.Add(col);
            }
            body.Add(board);

            var side = SidePane(LawDetailW, out var sideScroll, out var sideFoot);
            sideScroll.Add(LawDetail(statuses.First(s => s.Law.Id == selectedLaw), LawDetailW, sideFoot));
            body.Add(side);
            host.Add(body);
        }

        /// <summary>
        /// Portrait: one scrolling column, the chosen custom's card on top (a tap on a custom below brings it up there),
        /// then each branch under its own header, laws that rule each other out boxed together with "or".
        /// </summary>
        void BuildCustomsPortrait(VisualElement host, List<LawStatus> statuses)
        {
            var scroll = E.Scroll(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            scroll.contentContainer.style.paddingBottom = 6;
            scroll.Add(LawDetail(statuses.First(s => s.Law.Id == selectedLaw), InnerW).Margin(0, 0, 8));
            float colW = InnerW;
            foreach (var branch in statuses.Select(s => s.Law.Branch).Distinct())
            {
                var laws = statuses.Where(s => s.Law.Branch == branch).ToList();
                var head = E.Div().Row().Center().Ignore();
                head.style.height = 30;
                head.Add(E.Icon(branch == "Community" ? "people" : "flame", 22));
                head.Add(E.T($"{branch} {laws.Count(s => s.State == LawState.Signed)}/{laws.Count}", Theme.Num).Margin(0, 0, 0, 5));
                scroll.Add(head);
                var done = new HashSet<string>();
                foreach (var st in laws)
                {
                    if (!done.Add(st.Law.Id)) continue;
                    var group = new List<LawStatus> { st };
                    foreach (var o in laws)
                        if (!done.Contains(o.Law.Id) && Excludes(st.Law, o.Law)) { group.Add(o); done.Add(o.Law.Id); }
                    if (group.Count == 1) { scroll.Add(LawNode(st, colW)); continue; }
                    var box = E.Div().Col().Ignore().Radius(12).Border(Theme.KitLook ? Theme.Rim : Theme.Wood4, 2f).Pad(4).Margin(0, 0, 8);
                    box.style.backgroundColor = Theme.KitLook ? new Color(0.03f, 0.05f, 0.09f, 0.35f) : Theme.Alpha(Theme.Wood4, 0.18f);
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (i > 0) box.Add(OrConnector());
                        var node = LawNode(group[i], colW - 12);
                        node.style.marginBottom = 0;
                        box.Add(node);
                    }
                    scroll.Add(box);
                }
            }
            host.Add(scroll);
        }

        static VisualElement OrConnector()
        {
            var row = E.Div().Row().Center().Ignore();
            row.style.height = 28;
            var pill = E.Capsule(24).Pad(0, 10);
            pill.Add(E.T("or", Theme.Small, Theme.Gold));
            row.Add(pill);
            return row;
        }

        /// <summary>The cell a custom sits on in a state, and the mark on its corner (signed: a tick, locked: a padlock).</summary>
        static (E.Cell cell, string icon) LawLook(LawState s) => s switch
        {
            LawState.Signed => (E.Cell.Uncommon, "check"),
            LawState.Available => (E.Cell.Legendary, "customs"),
            _ => (E.Cell.Common, "lock"),
        };

        /// <summary>The custom's own picture (Icons/law_&lt;id&gt;), "customs" if it has none yet.</summary>
        static string LawIcon(LawDefinition law)
        {
            string name = "law_" + law.Id;
            return Theme.HasIcon(name) ? name : "customs";
        }

        /// <summary>
        /// A custom's picture on its state's cell (gold: can be signed, green: signed, grey and dimmed: locked), with a
        /// small tick or padlock on the corner.
        /// </summary>
        static VisualElement LawSlot(LawStatus st, float size)
        {
            var (cell, mark) = LawLook(st.State);
            var slot = E.Slot(LawIcon(st.Law), size, cell, 0.72f, st.State == LawState.Locked);
            if (st.State != LawState.Available)
                slot.Add(E.Icon(mark, Mathf.Round(size * 0.44f)).Abs(null, null, -5, -4));
            return slot;
        }

        VisualElement LawNode(LawStatus st, float width)
        {
            var law = st.Law;
            bool sel = law.Id == selectedLaw;
            var n = E.Div().Row().Skin(sel ? "slot" : "section").Pad(6, 6, sel ? 9 : 6, 6).Margin(0, 0, 8);
            n.style.minHeight = 60;
            n.Add(LawSlot(st, 42));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            col.Add(E.T(law.Name, Theme.Num, st.State == LawState.Locked ? Theme.Pale : (Color?)null).Fit(width - 12 - 42 - 8));
            string meta; Color mc;
            switch (st.State)
            {
                case LawState.Signed: meta = "Signed"; mc = Theme.Up; break;
                case LawState.Available: meta = "Can be signed"; mc = Theme.Gold; break;
                default: meta = st.Reasons.FirstOrDefault() ?? "Locked"; mc = Theme.Pale; break;
            }
            col.Add(E.T(meta, Theme.Small, mc).Fit(width - 12 - 42 - 8));
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
                    case EffectKind.ApprovalBonus: list.Add(($"{pts} hope", v > 0)); break;
                    case EffectKind.DiscontentPerHour: list.Add(($"{pts} discontent per hour", v < 0)); break;
                    case EffectKind.HeatBonus: list.Add(($"{SignedDeg(v)} warmth from the {G.HeartName}", v > 0)); break;
                }
            }
            if (Math.Abs(law.OnSign) > 0.01) list.Add(($"{law.OnSign.ToString("+0.#;−0.#;0", Inv)} discontent when signed", law.OnSign < 0));
            return list;
        }

        VisualElement LawDetail(LawStatus st, float detailW, VisualElement foot = null)
        {
            var law = st.Law;
            var card = E.Inset().Col().Pad(10, 10);
            var head = E.Div().Row().Ignore();
            head.Add(LawSlot(st, 52));
            var names = E.Div().Col().Grow().Ignore();
            names.style.marginLeft = 8;
            names.Add(E.T(law.Branch, Theme.Small, Theme.Pale));
            names.Add(E.T(law.Name, Theme.H2).TwoLines(detailW - 20 - 52 - 8, Theme.Body).Also(l => l.style.unityTextAlign = TextAnchor.MiddleLeft));
            head.Add(names);
            card.Add(head);
            card.Add(E.Ink(law.Description, Theme.Body).Margin(6));

            foreach (var (text, good) in LawEffects(law))
            {
                var r = E.Div().Row(0, Align.FlexStart).Ignore().Margin(5);
                r.Add(E.Icon(good ? "check" : "alert", 22));
                r.Add(E.T(text, Theme.Body, good ? Theme.Up : Theme.Bad).Flex().Margin(0, 0, 0, 6));
                card.Add(r);
            }
            var rivals = G.Content.Laws.Where(o => o.Id != law.Id && Excludes(law, o)).Select(o => o.Name).ToList();
            if (rivals.Count > 0)
                card.Add(E.Ink($"Either this or {string.Join(", ", rivals)}: signing one rules out the other for good.", Theme.Small, Theme.InkSoft).Margin(6));
            if (law.RequiresLaw != null)
                card.Add(E.Ink($"Follows {G.Content.Law(law.RequiresLaw)?.Name ?? law.RequiresLaw}.", Theme.Small, Theme.InkSoft).Margin(4));

            var bottom = E.Div().Col().Ignore().Margin(8);
            string id = law.Id;
            switch (st.State)
            {
                case LawState.Signed:
                    bottom.Add(E.Div().Row().Ignore().Kids(E.Icon("customs", 26), E.T("Signed into custom", Theme.Num, Theme.Up).Margin(0, 0, 0, 6)));
                    if (!string.IsNullOrEmpty(law.LegacyText))
                    {
                        var lt = E.Ink(law.LegacyText, Theme.Body).Margin(6);
                        lt.style.unityFontStyleAndWeight = FontStyle.Italic;
                        bottom.Add(lt);
                    }
                    break;
                case LawState.Locked:
                    foreach (var reason in st.Reasons)
                    {
                        var r = E.Div().Row(0, Align.FlexStart).Ignore().Margin(4);
                        r.Add(E.Icon("lock", 22));
                        r.Add(E.T(reason, Theme.Body, Theme.Pale).Flex().Margin(0, 0, 0, 6));
                        bottom.Add(r);
                    }
                    break;
                default:
                    if (confirmLaw == id)
                    {
                        // The warning goes with the description (it scrolls); only the two buttons stay pinned.
                        var warn = Notice("alert", $"A custom is for good: once signed, {law.Name} can never be revoked.", true).Margin(foot != null ? 8 : 0);
                        if (foot != null) card.Add(warn); else bottom.Add(warn);
                        var buttons = E.Div().Row().Ignore().Margin(foot != null ? 0 : 8);
                        float half = (detailW - 20 - 8) / 2;
                        buttons.Add(Share(E.Button("Cancel", Act(() => confirmLaw = null), E.Kind.Red, Theme.ButtonH, null, 16, null, half), buttons));
                        buttons.Add(Share(E.Button("Sign", Act(() =>
                        {
                            confirmLaw = null;
                            store.SignLaw(id);
                        }), E.Kind.Green, Theme.ButtonH, null, 16, null, half), buttons));
                        bottom.Add(buttons);
                    }
                    else if (G.LawCooldownLeft > 0)
                    {
                        var b = E.Button("Sign", null, E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("clock", ""));
                        var l = b.ElementAt(1).ElementAt(1) as Label;
                        Bind(() => { string s = Fmt.Clock(G.LawCooldownLeft); if (l != null && l.text != s) l.text = s; });
                        bottom.Add(b);
                        bottom.Add(E.Ink("The people need time to get used to each new custom.", Theme.Small, Theme.InkSoft).Margin(6));
                    }
                    else
                        bottom.Add(E.Button("Sign the custom", Act(() => confirmLaw = id), E.Kind.Green, Theme.ButtonH, "customs", 16, null, detailW - 20));
                    break;
            }
            if (foot != null) { bottom.style.marginTop = 0; foot.Add(bottom); foot.Hidden(bottom.childCount == 0); }
            else card.Add(bottom);
            return card;
        }

        // MARK: - Petition dialog

        bool petitionShowing;

        VisualElement PetitionCard()
        {
            var p = G.PendingPetition;
            var d = G.Content.Balance.Discontent;
            var c = DialogShell(p.Title, DialogW, out var body, out var foot);
            // Left: the people's words and the mood (portrait: on top).
            var left = E.Div().Col().Ignore().Size(DialogArtW);
            var art = E.Div().Row().Center().Ignore();
            art.style.height = Portrait ? 76 : 90;
            // The kit look: the scene in the dark inset; the game's own: a wooden frame.
            if (Theme.KitLook) art.Skin("section");
            else art.Radius(10).Border(Theme.Outline, 2.5f).BgImage(Theme.Gradient("petitionart", true, Theme.Hex("#8A5A32"), Theme.Hex("#4A2E1A")));
            art.Add(E.Icon("people", 60));
            art.Add(E.Icon("discontent", 38).Margin(0, 0, 0, -8));
            left.Add(art);
            var text = E.Ink(p.Text, Theme.Body).Margin(8);
            text.style.unityFontStyleAndWeight = FontStyle.Italic;
            left.Add(text);
            var mood = E.Div().Row().Ignore().Margin(8);
            mood.Add(E.Icon("discontent", 22));
            mood.Add(Live(() => $"Discontent {(int)Math.Round(G.State.Discontent)} · {G.DiscontentMood}", Theme.Body, MoodColor(G.DiscontentMood)).Flex().Margin(0, 0, 0, 6));
            left.Add(mood);
            // Right: the ask, what it needs and what follows.
            var right = E.Div().Col().Grow().Ignore();
            right.style.marginLeft = 12;
            var ask = E.Section("The people ask", out var rows, E.Div().Row().Ignore().Kids(E.Icon("clock", 20), E.T(Game.Duration(p.DeadlineSeconds), Theme.Num).Margin(0, 0, 0, 4)));
            rows.Add(E.T(p.Promise, Theme.Num, Theme.Gold).Wrap().Margin(6, 0, 4));
            foreach (var req in G.PetitionGoalStatus(p)) rows.Add(RequirementRow("next", req.Text, req.Met, null, true));
            right.Add(ask);
            if (d != null)
            {
                right.Add(Consequence("check", $"Promise: discontent −{Num(d.PromiseRelief)} now; kept, −{Num(d.KeptRelief)} more and +{Num(d.KeptApproval)} hope.", Theme.Up).Margin(8));
                right.Add(Consequence("alert", $"A broken promise: +{Num(d.BrokenAnger)} discontent. Refusing: +{Num(d.RefusedAnger)} now.", Theme.Bad).Margin(6));
            }
            body.Add(TwoColumns(left, right));

            foot.Add(Share(E.Button("Refuse", Act(() => store.AnswerPetition(false)), E.Kind.Red), foot));
            foot.Add(Share(E.Button("Promise", Act(() => store.AnswerPetition(true)), E.Kind.Green, Theme.ButtonH, "goals", 16,
                                    SubLine("clock", Game.Duration(p.DeadlineSeconds))), foot));
            return c;
        }

        static VisualElement Consequence(string icon, string text, Color color)
        {
            var r = E.Div().Row(0, Align.FlexStart).Ignore();
            r.Add(E.Icon(icon, 22));
            r.Add(E.T(text, Theme.Body, color).Flex().Margin(0, 0, 0, 6));
            return r;
        }
    }
}
