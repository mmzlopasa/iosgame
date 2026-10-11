using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>Bottom sheets opened from the dock, the avatar and the settings button.</summary>
    public partial class GameUi
    {
        Panel shownPanel = Panel.None;
        bool sheetShown;
        string buildTab = "All";
        float confirmResetUntil;

        void BuildSheet()
        {
            var scrim = E.Div("scrim").Fill().Bg(new Color(0.03f, 0.035f, 0.05f, 0.5f));
            Tap(scrim, () => store.ActivePanel = Panel.None);
            sheetLayer.Add(scrim);

            sheet = E.Div("sheet").Abs(0, 90, 0, 0).Col().Radius(28, 0).BgImage(Theme.SheetFill).Bg(Theme.Hex("#0F1117"));
            sheet.style.borderTopWidth = 1;
            sheet.style.borderTopColor = new Color(1, 1, 1, 0.2f);
            sheet.style.overflow = Overflow.Hidden;
            sheet.Transition(320, "translate", "opacity");

            var grabber = E.Div().Size(40, 4).Radius(2).Bg(new Color(1, 1, 1, 0.2f)).Ignore();
            grabber.style.alignSelf = Align.Center;
            grabber.style.marginTop = 10;
            sheet.Add(grabber);

            var header = E.Div("sheet-header").Ignore();
            AddRegion(header, () => SheetOpen ? store.ActivePanel + "|" + HeaderKey() : "", BuildSheetHeader);
            sheet.Add(header);

            sheetScroll = new ScrollView(ScrollViewMode.Vertical);
            sheetScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sheetScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sheetScroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            sheetScroll.style.flexGrow = 1;
            sheetScroll.contentContainer.style.paddingLeft = 16;
            sheetScroll.contentContainer.style.paddingRight = 16;
            sheetScroll.contentContainer.style.paddingBottom = 40;
            sheet.Add(sheetScroll);
            AddRegion(sheetScroll.contentContainer, () => SheetOpen ? store.ActivePanel + "|" + PanelKey() : "", BuildPanelContent);

            sheetLayer.Add(sheet);
        }

        /// <summary>Visibility of layers and the open animation, run once per tick before regions.</summary>
        void OnTick()
        {
            bool open = SheetOpen;
            if (store.ActivePanel != shownPanel)
            {
                shownPanel = store.ActivePanel;
                sheetScroll.scrollOffset = Vector2.zero;
                buildTab = "All";
            }
            if (open != sheetShown)
            {
                sheetShown = open;
                sheetLayer.Hidden(!open);
                if (open)
                {
                    sheet.style.translate = new Translate(0, 70);
                    sheet.style.opacity = 0;
                    sheet.schedule.Execute(() =>
                    {
                        sheet.style.translate = new Translate(0, 0);
                        sheet.style.opacity = 1;
                    }).StartingIn(16);
                }
            }
            sheetScroll.contentContainer.style.paddingBottom = 40 + SafeBottom;
            modalLayer.Hidden(ModalKey() == "");
        }

        string HeaderKey()
        {
            switch (store.ActivePanel)
            {
                case Panel.Build: return $"{G.ActiveConstructionCount()}";
                case Panel.Research: return "";
                default: return "";
            }
        }

        void BuildSheetHeader(VisualElement host)
        {
            if (!SheetOpen) return;
            string eyebrow, title;
            Color accent = Theme.Gold;
            switch (store.ActivePanel)
            {
                case Panel.Build: eyebrow = "Construction"; title = "Build"; break;
                case Panel.People: eyebrow = "Community"; title = "People & Work"; break;
                case Panel.Research: eyebrow = "Knowledge"; title = "Research"; accent = Theme.Lavender; break;
                case Panel.Objectives: eyebrow = "The long rise"; title = "Goals"; break;
                case Panel.Map: eyebrow = G.Content.Map.Name ?? "Riverside"; title = "World & Legacy"; break;
                case Panel.Profile: eyebrow = "Founder"; title = "Profile"; break;
                default: eyebrow = "Founders"; title = "Settings"; break;
            }
            var row = E.Div().Row().Pad(12, 16, 12, 20).Ignore();
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.Eyebrow(eyebrow, accent));
            var t = E.Title(title, 22);
            t.style.marginTop = 2;
            col.Add(t);
            row.Add(col);

            if (store.ActivePanel == Panel.Build)
            {
                var chip = E.Div().Row().Radius(999).Pad(6, 10).Bg(Theme.Fill).Border(new Color(1, 1, 1, 0.1f)).Ignore();
                chip.style.marginRight = 8;
                chip.Add(E.Text("Building", 12, Theme.Body).Margin(0, 6));
                chip.Add(Live(() => $"{G.ActiveConstructionCount()}", 12, Theme.Ink, Theme.F.Mono));
                row.Add(chip);
            }
            else if (store.ActivePanel == Panel.Research)
            {
                var chip = E.Div().Row().Radius(999).Pad(6, 11).Bg(Theme.Alpha(Theme.Lavender, 0.12f)).Border(Theme.Alpha(Theme.Lavender, 0.28f)).Ignore();
                chip.style.marginRight = 8;
                chip.Add(E.Icon("knowledge", 13, Theme.LavenderLight));
                var v = Live(() => Fmt.Amount(G.State.Resources.Knowledge), 13, Theme.LavenderLight, Theme.F.Mono);
                v.style.marginLeft = 5;
                chip.Add(v);
                var r = Live(() => " " + Fmt.PerMinute(econ.Net.Knowledge), 10.5f, Theme.Mint, Theme.F.Mono);
                chip.Add(r);
                row.Add(chip);
            }
            row.Add(E.IconButton("close", Act(() => store.ActivePanel = Panel.None), 36));
            host.Add(row);
        }

        string PanelKey()
        {
            switch (store.ActivePanel)
            {
                case Panel.Build:
                    return buildTab + "|" + string.Join(",", G.Content.Buildings.Where(b => b.Buildable).Select(d =>
                        $"{G.State.Resources.Covers(d.Cost)}{G.IsUnlocked(d)}{G.Count(d.Id, true)}"));
                case Panel.People:
                    return $"{G.SeasonIndex}|{G.Freezing}|{string.Join(",", G.ApprovalFactors().Select(f => f.Label + f.Points))}|{G.IdleWorkers}|{G.State.Population}|{G.HousingCapacity()}|{G.State.FoodShortage}|" +
                           string.Join(",", Enums.Jobs.Select(j => $"{G.State.Assignments[j]}/{G.JobCapacity(j)}"));
                case Panel.Research:
                    return $"{G.State.ActiveResearch?.TechId}|" + string.Join(",", G.Content.Technologies.Select(t =>
                        $"{G.GetResearchStatus(t.Id).State}{G.State.Resources.Covers(t.Cost)}"));
                case Panel.Objectives:
                    return string.Join(",", G.State.CompletedObjectives) + "|" + G.CurrentObjective?.Id + "|" +
                           string.Join(",", G.Content.Milestones.Select(m => $"{G.CanClaimMilestone(m.Id)}{G.State.ReachedMilestones.Contains(m.Id)}")) + "|" +
                           (G.CurrentObjective == null ? "" : string.Join("", G.Status(G.CurrentObjective.Conditions).Select(s => s.Met ? "1" : "0"))) + "|" +
                           string.Join("", G.Content.Milestones.SelectMany(m => G.MilestoneRequirements(m.Id)).Select(s => s.Met ? "1" : "0"));
                case Panel.Map:
                    return $"{G.State.ActiveExpedition?.RegionId}|{string.Join(",", G.State.ExploredRegions)}|{G.State.Legacy.Count}|" +
                           string.Join("", G.Content.Regions.Select(r => G.State.Resources.Covers(r.Cost) ? "1" : "0"));
                case Panel.Profile:
                    return $"{G.State.Legacy.Count}|{G.State.Researched.Count}|{G.State.Buildings.Count}|{G.State.StageId}";
                default:
                    return $"{Prefs.Quality}|{Prefs.BatterySaver}|{Prefs.Haptics}|{Time.unscaledTime < confirmResetUntil}|{store.SaveStatus}";
            }
        }

        void BuildPanelContent(VisualElement host)
        {
            if (!SheetOpen) return;
            switch (store.ActivePanel)
            {
                case Panel.Build: BuildBuildPanel(host); break;
                case Panel.People: BuildPeoplePanel(host); break;
                case Panel.Research: BuildResearchPanel(host); break;
                case Panel.Objectives: BuildGoalsPanel(host); break;
                case Panel.Map: BuildWorldPanel(host); break;
                case Panel.Profile: BuildProfilePanel(host); break;
                default: BuildSettingsPanel(host); break;
            }
        }

        static VisualElement Card(float radius = 20) => E.Div().Col().Glass(radius).Pad(14).Margin(0, 0, 10);

        static VisualElement SectionLabel(string text)
        {
            var l = E.Eyebrow(text, Theme.Muted);
            l.style.marginTop = 10;
            l.style.marginBottom = 8;
            l.style.marginLeft = 4;
            return l;
        }

        // MARK: - Build

        void BuildBuildPanel(VisualElement host)
        {
            var defs = G.Content.Buildings.Where(b => b.Buildable).ToList();
            var tabs = new[] { "All" }.Concat(defs.Select(Category).Distinct()).ToList();
            var tabRow = E.Div().Row().Margin(0, 0, 12).Ignore();
            tabRow.style.flexWrap = Wrap.Wrap;
            foreach (var tab in tabs)
            {
                bool on = tab == buildTab;
                var t = E.Div().Row().Radius(999).Pad(8, 13);
                t.style.marginRight = 6;
                t.style.marginBottom = 4;
                if (on) t.BgImage(Theme.GoldFill); else t.Bg(Theme.Fill).Border(new Color(1, 1, 1, 0.1f));
                t.Add(E.Text(tab, 12.5f, on ? Theme.OnGold : Theme.Body, Theme.F.TextBold));
                string name = tab;
                Tap(t, () => buildTab = name);
                tabRow.Add(t);
            }
            host.Add(tabRow);

            foreach (var def in defs.Where(d => buildTab == "All" || Category(d) == buildTab))
            {
                bool affordable = G.State.Resources.Covers(def.Cost);
                bool unlocked = G.IsUnlocked(def);
                int count = G.Count(def.Id, true);
                bool limit = def.MaxCount.HasValue && count >= def.MaxCount.Value;

                var card = E.Div().Row(0, Align.FlexStart).Glass(20, affordable && unlocked && !limit ? (Color?)null : Theme.EdgeSoft).Pad(12).Margin(0, 0, 10);
                if (!unlocked) card.style.opacity = 0.6f;
                var tile = BuildingTile(def, 70);
                var countTag = E.Mono(def.MaxCount.HasValue ? $"{count}/{def.MaxCount}" : $"{count}", 10, Theme.GoldLight);
                var tag = E.Div().Ignore().Radius(999).Pad(2, 6).Bg(new Color(0.05f, 0.06f, 0.08f, 0.75f)).Abs(null, null, 4, 4);
                tag.Add(countTag);
                tile.Add(tag);
                card.Add(tile);

                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 12;
                var top = E.Div().Row().Between().Ignore();
                top.Add(E.Text(def.Name, 15, Theme.Ink, Theme.F.Display).NoWrap().Grow());
                top.Add(E.Mono(Fmt.Clock(G.EstimatedSeconds(def.BuildSeconds)), 11, Theme.Muted));
                col.Add(top);
                var desc = E.Text(def.Description, 12, Theme.Body);
                desc.style.marginTop = 4;
                col.Add(desc);
                if (def.Footprint.Width > 1 || def.Footprint.Height > 1)
                    col.Add(E.Text($"Size {def.Footprint.Width}×{def.Footprint.Height}", 11, Theme.Muted).Margin(3));

                var bottom = E.Div().Row().Between().Margin(8).Ignore();
                bottom.Add(CostChips(def.Cost).Grow());
                string label; E.Kind kind; Action act = null;
                if (!unlocked) { label = "Locked"; kind = E.Kind.Disabled; }
                else if (limit) { label = "Limit"; kind = E.Kind.Disabled; }
                else if (!affordable) { label = "Need more"; kind = E.Kind.Disabled; }
                else { label = "Place"; kind = E.Kind.Gold; string id = def.Id; act = Act(() => store.BeginPlacement(id)); }
                bottom.Add(E.Button(label, act, kind, 32, null, 12).Pad(0, 14));
                col.Add(bottom);
                if (!unlocked)
                    col.Add(E.Text($"Requires research: {G.Content.Technology(def.RequiresTech ?? "")?.Name ?? "?"}", 11.5f, Theme.Lavender).Margin(4));
                card.Add(col);
                host.Add(card);
            }

            var hint = E.Div().Row().Radius(14).Pad(10, 14).Bg(Theme.Alpha(Theme.Lavender, 0.08f)).Border(Theme.Alpha(Theme.Lavender, 0.18f)).Margin(4);
            hint.Add(E.Icon("knowledge", 15, Theme.Lavender));
            var ht = E.Text("Pick a building, then tap the ground to move it. Resources are spent only when you confirm.", 12, Theme.Lavender).Grow();
            ht.style.marginLeft = 8;
            hint.Add(ht);
            host.Add(hint);
        }

        // MARK: - People

        void BuildPeoplePanel(VisualElement host)
        {
            BuildWellbeing(host);
            var summary = Card();
            var row = E.Div().Row().Ignore();
            row.Add(Stat("Population", () => $"{G.State.Population}/{G.HousingCapacity()}", Theme.Ink));
            row.Add(Stat("Idle", () => $"{G.IdleWorkers}", G.IdleWorkers > 0 ? Theme.Gold : Theme.Ink).Margin(0, 0, 0, 8));
            row.Add(Stat("Food", () => Fmt.PerMinute(econ.Net.Food), econ.Net.Food < 0 ? Theme.Error : Theme.Mint).Margin(0, 0, 0, 8));
            summary.Add(row);

            bool blocked = G.State.Population >= G.HousingCapacity();
            if (G.State.FoodShortage)
                summary.Add(Notice("alert", "Food has run out. All work is slower until food is restored.", Theme.Error));
            else if (blocked)
                summary.Add(Notice("home", "Build or upgrade shelters so the community can grow.", Theme.Gold));
            else
            {
                var g = E.Div().Row().Between().Margin(12).Ignore();
                g.Add(E.Text("Next newcomer", 12, Theme.Body));
                g.Add(Live(() => $"{G.State.GrowthProgress * 100:0}%", 12, Theme.Mint, Theme.F.Mono));
                summary.Add(g);
                summary.Add(LiveBar(() => G.State.GrowthProgress, 5, Theme.MintFill).Margin(6));
            }
            host.Add(summary);

            host.Add(SectionLabel("Work"));
            foreach (var job in Enums.Jobs) host.Add(JobRow(job, false).Margin(0, 0, 8));
            host.Add(E.Text("Workplaces set how many people each job can take. Changes apply at once.", 11.5f, Theme.Muted).Margin(4, 4, 0, 4));

            host.Add(SectionLabel("Upkeep"));
            var up = Card();
            var r1 = E.Div().Row().Between().Ignore();
            r1.Add(E.Text("Eating", 13, Theme.Body));
            r1.Add(Live(() => Fmt.PerMinute(-G.State.Population * G.Content.Balance.FoodPerPersonPerSecond), 13, Theme.Error, Theme.F.Mono));
            up.Add(r1);
            var r2 = E.Div().Row().Between().Margin(8).Ignore();
            r2.Add(E.Text("Spoiling", 13, Theme.Body));
            r2.Add(Live(() => Fmt.PerMinute(-econ.Spoilage), 13, Theme.Error, Theme.F.Mono));
            up.Add(r2);
            host.Add(up);
        }

        /// <summary>Approval with its causes, and what the season asks of the settlement.</summary>
        void BuildWellbeing(VisualElement host)
        {
            var card = Card();
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("approval", 18, Theme.Gold));
            var t = E.Text("Approval", 15, Theme.Ink, Theme.F.Display).Grow();
            t.style.marginLeft = 8;
            head.Add(t);
            var pct = E.Mono("", 18);
            Bind(() => { int a = G.Approval; pct.text = a + "%"; pct.style.color = ApprovalColor(a); });
            head.Add(pct);
            card.Add(head);
            var bar = E.Bar(out var fill, 6, null, Theme.Gold);
            fill.style.backgroundImage = StyleKeyword.None;
            Bind(() => { int a = G.Approval; E.SetFill(fill, a / 100.0); fill.style.backgroundColor = ApprovalColor(a); });
            bar.style.marginTop = 10;
            card.Add(bar);
            var effect = Live(() =>
            {
                int a = G.Approval;
                int work = (int)Math.Round((G.ApprovalProductivity(a) - 1) * 100);
                string growth = a < 30 ? "no newcomers" : $"growth ×{G.ApprovalGrowth(a):0.0}";
                return $"Work {(work >= 0 ? "+" : "")}{work}% · {growth}";
            }, 11.5f, Theme.Body);
            effect.style.marginTop = 6;
            card.Add(effect);
            card.Add(E.Div().Size(null, 1).Bg(new Color(1, 1, 1, 0.07f)).Margin(10).Ignore());
            var baseRow = E.Div().Row().Between().Margin(8).Ignore();
            baseRow.Add(E.Text("Base", 12.5f, Theme.Muted));
            baseRow.Add(E.Mono(Game.ApprovalBase.ToString(CultureInfo.InvariantCulture), 12.5f, Theme.Muted));
            card.Add(baseRow);
            foreach (var f in G.ApprovalFactors())
            {
                var r = E.Div().Row().Between().Margin(6).Ignore();
                r.Add(E.Text(f.Label, 12.5f, Theme.Ink).Grow());
                r.Add(E.Mono((f.Points > 0 ? "+" : f.Points < 0 ? "−" : "") + Math.Abs(f.Points), 12.5f, f.Points > 0 ? Theme.Mint : f.Points < 0 ? Theme.Error : Theme.Muted));
                card.Add(r);
            }
            host.Add(card);

            var season = G.Season;
            if (season != null)
            {
                var sc = Card();
                var sh = E.Div().Row().Ignore();
                sh.Add(E.Icon(SeasonIcon(season.Id), 18, SeasonTint(season.Id)));
                var st = E.Text($"{season.Name} · Year {G.Year}", 15, Theme.Ink, Theme.F.Display).Grow();
                st.style.marginLeft = 8;
                sh.Add(st);
                sh.Add(Live(() => Fmt.Clock(G.SecondsUntilNextSeason), 12, Theme.Muted, Theme.F.Mono));
                sc.Add(sh);
                if (!string.IsNullOrEmpty(season.Description)) sc.Add(E.Text(season.Description, 12, Theme.Body).Margin(6));
                var chips = E.Div().Row().Margin(10).Ignore();
                chips.style.flexWrap = Wrap.Wrap;
                chips.Add(StatChip("food", $"Foraging ×{season.Foraging:0.##}"));
                chips.Add(StatChip("spring", season.Farming <= 0 ? "No farming" : $"Farming ×{season.Farming:0.##}"));
                if (season.FuelPerPersonPerSecond > 0)
                    chips.Add(StatChip("flame", $"Firewood {G.FuelPerSecond * 60:0.#}/m"));
                sc.Add(chips);
                if (G.Freezing) sc.Add(Notice("alert", "The woodpile is empty and people are freezing. Put more people on Wood Gathering.", Theme.Error));
                host.Add(sc);
            }
        }

        VisualElement Stat(string label, Func<string> value, Color color)
        {
            var c = E.Div().Col().Grow().Radius(14).Pad(10).Bg(new Color(1, 1, 1, 0.04f)).Ignore();
            c.Add(E.Eyebrow(label, Theme.Muted, 9.5f));
            var v = Live(value, 16, color, Theme.F.Mono);
            v.style.marginTop = 4;
            c.Add(v);
            return c;
        }

        static VisualElement Notice(string icon, string text, Color color)
        {
            var n = E.Div().Row(0, Align.FlexStart).Radius(14).Pad(10, 12).Bg(Theme.Alpha(color, 0.08f)).Border(Theme.Alpha(color, 0.25f)).Margin(12).Ignore();
            n.Add(E.Icon(icon, 15, color));
            var t = E.Text(text, 12.5f, color).Grow();
            t.style.marginLeft = 8;
            n.Add(t);
            return n;
        }

        // MARK: - Research

        void BuildResearchPanel(VisualElement host)
        {
            var active = G.State.ActiveResearch;
            if (active != null)
            {
                var tech = G.Content.Technology(active.TechId);
                var card = E.Div().Col().Radius(20).Pad(14).BgImage(Theme.LavenderGlow).Border(Theme.Alpha(Theme.Lavender, 0.34f)).Margin(0, 0, 12);
                var head = E.Div().Row().Between().Ignore();
                head.Add(E.Eyebrow("In progress", Theme.Lavender));
                head.Add(Live(() => $"{Fmt.Clock(G.State.ActiveResearch?.RemainingSeconds ?? 0)} left", 11.5f, Theme.LavenderLight, Theme.F.Mono));
                card.Add(head);
                card.Add(E.Text(tech?.Name ?? active.TechId, 17, Theme.Ink, Theme.F.Display).Margin(8));
                card.Add(E.Text(tech?.Description ?? "", 12, Theme.Body).Margin(4));
                card.Add(LiveBar(() => G.GetResearchStatus(active.TechId).Progress, 6, Theme.LavenderFill).Margin(10));
                host.Add(card);
            }
            if (econ.Production.Knowledge <= 0)
                host.Add(Notice("people", "Assign people to Research (People tab) to produce knowledge.", Theme.Gold).Margin(0, 0, 10));

            host.Add(SectionLabel($"Era {Fmt.Roman(G.Content.Epoch(G.State.EpochId)?.Index ?? 1)} · {G.EpochName}"));
            foreach (var tech in G.Content.Technologies)
            {
                var status = G.GetResearchStatus(tech.Id);
                if (status.State == ResearchState.InProgress) continue;
                bool affordable = G.State.Resources.Covers(tech.Cost);
                bool busy = G.State.ActiveResearch != null;

                Color edge, iconBg, iconFg; string icon;
                switch (status.State)
                {
                    case ResearchState.Researched: edge = Theme.Alpha(Theme.Mint, 0.28f); iconBg = Theme.Alpha(Theme.Mint, 0.16f); iconFg = Theme.Mint; icon = "check"; break;
                    case ResearchState.Available: edge = Theme.Alpha(Theme.Gold, 0.4f); iconBg = Theme.Alpha(Theme.Gold, 0.16f); iconFg = Theme.Gold; icon = "plus"; break;
                    default: edge = Theme.EdgeSoft; iconBg = new Color(1, 1, 1, 0.06f); iconFg = Theme.Muted; icon = "lock"; break;
                }
                var card = E.Div().Col().Glass(18, edge).Pad(12).Margin(0, 0, 8);
                if (status.State == ResearchState.Blocked) card.style.opacity = 0.7f;
                var head = E.Div().Row().Ignore();
                var tile = E.Div().Size(32, 32).Radius(10).Center().Bg(iconBg).Ignore();
                tile.Add(E.Icon(icon, 16, iconFg));
                head.Add(tile);
                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 10;
                col.Add(E.Text(tech.Name, 14.5f, status.State == ResearchState.Blocked ? Theme.Body : Theme.Ink, Theme.F.Display));
                string meta = status.State == ResearchState.Researched ? "Researched"
                    : status.State == ResearchState.Blocked ? "Needs " + string.Join(", ", status.Missing)
                    : $"{Fmt.Clock(tech.ResearchSeconds)}";
                col.Add(E.Text(meta, 11, status.State == ResearchState.Researched ? Theme.Mint : status.State == ResearchState.Blocked ? Theme.Faint : Theme.GoldLight,
                               status.State == ResearchState.Available ? Theme.F.Mono : Theme.F.TextMedium).Margin(2));
                head.Add(col);
                card.Add(head);
                card.Add(E.Text(tech.Description, 12, Theme.Body).Margin(8));
                if (status.State == ResearchState.Available)
                {
                    var bottom = E.Div().Row().Between().Margin(10).Ignore();
                    bottom.Add(CostChips(tech.Cost).Grow());
                    bool can = affordable && !busy;
                    string id = tech.Id;
                    bottom.Add(E.Button(busy ? "One at a time" : affordable ? "Research" : "Need more",
                                        can ? Act(() => store.StartResearch(id)) : null, can ? E.Kind.Gold : E.Kind.Disabled, 32, null, 12).Pad(0, 14));
                    card.Add(bottom);
                }
                host.Add(card);
            }
        }

        // MARK: - Goals

        void BuildGoalsPanel(VisualElement host)
        {
            foreach (var ms in G.Content.Milestones)
            {
                bool reached = G.State.ReachedMilestones.Contains(ms.Id);
                var card = E.Div().Col().Radius(22).Pad(16).BgImage(Theme.GoldGlow).Border(Theme.Alpha(Theme.Gold, 0.34f)).Margin(0, 0, 12);
                var head = E.Div().Row().Ignore();
                head.Add(E.Icon(reached ? "trophy" : "sparkle", 18, Theme.Gold));
                var eb = E.Eyebrow(reached ? "Milestone reached" : "Milestone");
                eb.style.marginLeft = 8;
                head.Add(eb);
                card.Add(head);
                card.Add(E.Text(ms.Name, 20, Theme.Ink, Theme.F.Display).Margin(8));
                if (!string.IsNullOrEmpty(ms.Description)) card.Add(E.Text(ms.Description, 12.5f, Theme.Body).Margin(4));
                if (!reached)
                {
                    foreach (var req in G.MilestoneRequirements(ms.Id)) card.Add(Check(req.Text, req.Met).Margin(8));
                    bool can = G.CanClaimMilestone(ms.Id);
                    string id = ms.Id;
                    card.Add(E.Button(can ? "Found the village" : "Not ready yet", can ? Act(() => { store.ClaimMilestone(id); store.ActivePanel = Panel.None; }) : null,
                                      can ? E.Kind.Gold : E.Kind.Disabled, 46, can ? "sparkle" : null).Margin(14));
                }
                host.Add(card);
            }

            host.Add(SectionLabel("First steps"));
            var current = G.CurrentObjective;
            foreach (var obj in G.Content.Objectives)
            {
                bool done = G.State.CompletedObjectives.Contains(obj.Id);
                bool isCurrent = current != null && current.Id == obj.Id;
                var card = E.Div().Col().Glass(18, isCurrent ? Theme.Alpha(Theme.Gold, 0.4f) : done ? Theme.Alpha(Theme.Mint, 0.22f) : (Color?)Theme.EdgeSoft)
                    .Pad(12).Margin(0, 0, 8);
                if (!done && !isCurrent) card.style.opacity = 0.65f;
                var head = E.Div().Row().Ignore();
                var dot = E.Div().Size(26, 26).Radius(13).Center().Ignore()
                    .Bg(done ? Theme.Alpha(Theme.Mint, 0.16f) : isCurrent ? Theme.Alpha(Theme.Gold, 0.18f) : new Color(1, 1, 1, 0.06f));
                dot.Add(E.Icon(done ? "check" : isCurrent ? "goals" : "lock", 14, done ? Theme.Mint : isCurrent ? Theme.Gold : Theme.Muted));
                head.Add(dot);
                var t = E.Text(obj.Title, 14, Theme.Ink, isCurrent ? Theme.F.Display : Theme.F.TextMedium).Grow();
                t.style.marginLeft = 10;
                head.Add(t);
                card.Add(head);
                if (isCurrent)
                {
                    if (!string.IsNullOrEmpty(obj.Hint)) card.Add(E.Text(obj.Hint, 12, Theme.Body).Margin(8));
                    foreach (var req in G.Status(obj.Conditions)) card.Add(Check(req.Text, req.Met).Margin(6));
                }
                if (obj.Reward.HasValue && !done)
                {
                    var rw = E.Div().Row().Margin(8).Ignore();
                    rw.Add(E.Text("Reward", 11, Theme.Muted));
                    var chips = CostChips(obj.Reward.Value);
                    chips.style.marginLeft = 8;
                    rw.Add(chips);
                    card.Add(rw);
                }
                host.Add(card);
            }
        }

        static VisualElement Check(string text, bool met)
        {
            var r = E.Div().Row(0, Align.FlexStart).Ignore();
            r.Add(E.Icon(met ? "check" : "next", 14, met ? Theme.Mint : Theme.Muted));
            var t = E.Text(text, 12.5f, met ? Theme.Mint : Theme.Ink).Grow();
            t.style.marginLeft = 7;
            r.Add(t);
            return r;
        }

        // MARK: - World & Legacy

        void BuildWorldPanel(VisualElement host)
        {
            var info = Card();
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("world", 18, Theme.Gold));
            var n = E.Text(G.State.SettlementName, 16, Theme.Ink, Theme.F.Display);
            n.style.marginLeft = 8;
            head.Add(n);
            info.Add(head);
            var traits = E.Div().Row().Margin(10).Ignore();
            traits.style.flexWrap = Wrap.Wrap;
            foreach (var t in G.Content.Map.Traits) traits.Add(StatChip("compass", t));
            info.Add(traits);
            host.Add(info);

            host.Add(SectionLabel("Explore"));
            var active = G.State.ActiveExpedition;
            foreach (var region in G.Content.Regions.Where(r => !r.RevealedAtStart))
            {
                bool explored = G.State.ExploredRegions.Contains(region.Id);
                var card = E.Div().Col().Glass(18, explored ? Theme.Alpha(Theme.Mint, 0.24f) : (Color?)null).Pad(12).Margin(0, 0, 8);
                var top = E.Div().Row().Between().Ignore();
                top.Add(E.Text(region.Name, 14.5f, Theme.Ink, Theme.F.Display).Grow());
                if (explored) top.Add(E.Eyebrow("Explored", Theme.Mint, 9.5f));
                card.Add(top);
                card.Add(E.Text(region.Description, 12, Theme.Body).Margin(4));
                if (active != null && active.RegionId == region.Id)
                {
                    var r = E.Div().Row().Between().Margin(10).Ignore();
                    r.Add(E.Text("Scouts away", 12, Theme.Mint));
                    r.Add(Live(() => Fmt.Clock(G.State.ActiveExpedition?.RemainingSeconds ?? 0), 12, Theme.Mint, Theme.F.Mono));
                    card.Add(r);
                    card.Add(LiveBar(() => { var e = G.State.ActiveExpedition; return e == null ? 1 : 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds); }, 5, Theme.MintFill).Margin(6));
                }
                else if (!explored)
                {
                    var bottom = E.Div().Row().Between().Margin(10).Ignore();
                    bottom.Add(CostChips(region.Cost).Grow());
                    bool can = active == null && G.State.Resources.Covers(region.Cost);
                    string id = region.Id;
                    bottom.Add(E.Button(active != null ? "Scouts busy" : "Send scouts", can ? Act(() => store.Explore(id)) : null,
                                        can ? E.Kind.Gold : E.Kind.Disabled, 32, can ? "compass" : null, 12).Pad(0, 14));
                    card.Add(bottom);
                    card.Add(E.Text(Fmt.Clock(region.ExpeditionSeconds) + " expedition", 11, Theme.Muted, Theme.F.Mono).Margin(4));
                }
                host.Add(card);
            }

            host.Add(SectionLabel("Legacy Timeline"));
            var line = E.Div().Col().Ignore();
            var entries = Enumerable.Reverse(G.State.Legacy).ToList();
            for (int i = 0; i < entries.Count; i++) line.Add(LegacyRow(entries[i], i == entries.Count - 1));
            host.Add(line);
        }

        VisualElement LegacyRow(LegacyEntry entry, bool last)
        {
            var row = E.Div().Row(0, Align.Stretch).Ignore();
            var rail = E.Div().Col(Align.Center).Size(22).Ignore();
            rail.Add(E.Div().Size(12, 12).Radius(6).BgImage(Theme.GoldFill).Margin(4).Ignore());
            if (!last)
            {
                var l = E.Div().Size(2).Grow().Bg(Theme.Alpha(Theme.Gold, 0.25f)).Margin(4).Ignore();
                rail.Add(l);
            }
            row.Add(rail);
            var body = E.Div().Col().Grow().Pad(0, 0, 16, 10).Ignore();
            var head = E.Div().Row().Between().Ignore();
            head.Add(E.Text(entry.Title, 13.5f, Theme.Ink, Theme.F.TextBold).Grow());
            head.Add(E.Mono(Fmt.Clock(entry.SimTime), 10.5f, Theme.Muted));
            body.Add(head);
            if (!string.IsNullOrEmpty(entry.Detail)) body.Add(E.Text(entry.Detail, 12, Theme.Body).Margin(3));
            row.Add(body);
            return row;
        }

        // MARK: - Profile

        void BuildProfilePanel(VisualElement host)
        {
            var hero = E.Div().Col(Align.Center).Margin(4, 0, 14).Ignore();
            hero.Add(Avatar(96, 34));
            hero.Add(E.Text(G.State.SettlementName, 22, Theme.Ink, Theme.F.Display).Margin(12));
            var id = E.Div().Row().Radius(999).Pad(5, 10).Bg(Theme.Fill).Border(new Color(1, 1, 1, 0.1f)).Margin(8).Ignore();
            id.Add(E.Text("Founder ID", 11, Theme.Muted).Margin(0, 6));
            id.Add(E.Mono(FounderId(), 11, Theme.Body));
            hero.Add(id);
            host.Add(hero);

            var grid = E.Div().Row().Ignore();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            int techs = G.Content.Technologies.Count;
            var stats = new (string label, Func<string> value, string icon)[]
            {
                ("People", () => $"{G.State.Population}", "people"),
                ("Buildings", () => $"{G.State.Buildings.Count(b => b.Status == BuildingStatus.Complete)}", "home"),
                ("Research", () => $"{G.State.Researched.Count}/{techs}", "knowledge"),
                ("Regions", () => $"{G.State.ExploredRegions.Count}", "compass"),
                ("Legacy", () => $"{G.State.Legacy.Count}", "legacy"),
                ("Time", () => Fmt.Clock(G.State.SimTime), "clock"),
            };
            foreach (var s in stats)
            {
                var c = E.Div().Col().Glass(16).Pad(12).Margin(0, 0, 8).Ignore();
                c.style.width = Length.Percent(31.5f);
                c.Add(E.Icon(s.icon, 16, Theme.Gold));
                var v = Live(s.value, 17, Theme.Ink, Theme.F.Mono);
                v.style.marginTop = 8;
                c.Add(v);
                c.Add(E.Text(s.label, 11, Theme.Muted).Margin(2));
                grid.Add(c);
            }
            host.Add(grid);

            host.Add(SectionLabel("Settlement"));
            var card = Card();
            var r = E.Div().Row().Between().Ignore();
            r.Add(E.Text(G.StageName, 15, Theme.Ink, Theme.F.Display));
            r.Add(E.Eyebrow($"Era {Fmt.Roman(G.Content.Epoch(G.State.EpochId)?.Index ?? 1)}", Theme.Gold, 9.5f));
            card.Add(r);
            card.Add(E.Text(G.EpochName, 12, Theme.Body).Margin(4));
            var traits = E.Div().Row().Margin(10).Ignore();
            traits.style.flexWrap = Wrap.Wrap;
            foreach (var t in G.Content.Map.Traits) traits.Add(StatChip("world", t));
            card.Add(traits);
            host.Add(card);

            host.Add(SectionLabel("Milestones"));
            var ms = E.Div().Row().Ignore();
            foreach (var m in G.Content.Milestones)
            {
                bool got = G.State.ReachedMilestones.Contains(m.Id);
                var b = E.Div().Col(Align.Center).Grow().Glass(16, got ? Theme.Alpha(Theme.Gold, 0.4f) : (Color?)Theme.EdgeSoft).Pad(12).Margin(0, 8).Ignore();
                b.Add(E.Icon(got ? "trophy" : "lock", 22, got ? Theme.Gold : Theme.Muted));
                b.Add(E.Text(m.Name, 12, got ? Theme.Ink : Theme.Muted, Theme.F.TextBold).Align(TextAnchor.MiddleCenter).Margin(6));
                ms.Add(b);
            }
            host.Add(ms);

            host.Add(SectionLabel("Coming later"));
            var later = E.Div().Row().Ignore();
            foreach (var (label, icon) in new[] { ("Skins", "user"), ("Rankings", "trophy"), ("Alliance", "people") })
            {
                var b = E.Div().Col(Align.Center).Grow().Radius(16).Pad(12).Bg(new Color(1, 1, 1, 0.03f)).Border(Theme.EdgeSoft).Margin(0, 8).Ignore();
                b.style.opacity = 0.6f;
                b.Add(E.Icon(icon, 18, Theme.Muted));
                b.Add(E.Text(label, 11.5f, Theme.Muted).Margin(5));
                b.Add(E.Icon("lock", 11, Theme.Faint).Margin(4));
                later.Add(b);
            }
            host.Add(later);
        }

        string FounderId()
        {
            string raw = (G.State.SettlementId ?? "").Replace("-", "").ToUpperInvariant();
            if (raw.Length < 8) raw = raw.PadRight(8, '0');
            return raw.Substring(0, 4) + "-" + raw.Substring(4, 4);
        }

        // MARK: - Settings

        void BuildSettingsPanel(VisualElement host)
        {
            host.Add(SectionLabel("Graphics"));
            var seg = E.Div().Row().Radius(999).Pad(5).Bg(Theme.Fill).Border(new Color(1, 1, 1, 0.1f)).Margin(0, 0, 10);
            string[] names = { "Smooth", "Balanced", "Cinematic" };
            for (int i = 0; i < names.Length; i++)
            {
                bool on = Prefs.Quality == i;
                var b = E.Div().Grow().Center().Radius(999);
                b.style.height = 36;
                if (on) b.BgImage(Theme.GoldFill);
                b.Add(E.Text(names[i], 12.5f, on ? Theme.OnGold : Theme.Body, Theme.F.TextBold));
                int q = i;
                Tap(b, () => { Prefs.Quality = q; Prefs.Apply(); });
                seg.Add(b);
            }
            host.Add(seg);

            host.Add(SectionLabel("Game"));
            var toggles = E.Div().Col().Glass(20).Margin(0, 0, 10);
            toggles.style.overflow = Overflow.Hidden;
            toggles.Add(Toggle("Battery saver", "30 fps, lighter on the battery", Prefs.BatterySaver, v => { Prefs.BatterySaver = v; Prefs.Apply(); }, false));
            toggles.Add(Toggle("Haptics", "A tap of vibration on milestones", Prefs.Haptics, v => Prefs.Haptics = v, true));
            host.Add(toggles);

            host.Add(SectionLabel("Account"));
            var info = E.Div().Col().Glass(20).Margin(0, 0, 10);
            info.style.overflow = Overflow.Hidden;
            info.Add(InfoRow("Founder ID", FounderId(), false));
            info.Add(InfoRow("Save", store.SaveStatus, true));
            info.Add(InfoRow("Language", "English", true));
            host.Add(info);

            host.Add(SectionLabel("Developer"));
            var dev = E.Div().Col().Glass(20).Pad(12).Margin(0, 0, 10);
            var r1 = E.Div().Row().Ignore();
            r1.Add(E.Button("Save now", Act(store.Save), E.Kind.Ghost, 38, null, 12.5f).Grow());
            r1.Add(E.Button("+50 all", Act(store.DebugGrant), E.Kind.Ghost, 38, null, 12.5f).Grow().Margin(0, 0, 0, 8));
            dev.Add(r1);
            var r2 = E.Div().Row().Margin(8).Ignore();
            r2.Add(E.Button("+1 min", Act(() => store.DebugFastForward(1)), E.Kind.Ghost, 38, null, 12.5f).Grow());
            r2.Add(E.Button("+10 min", Act(() => store.DebugFastForward(10)), E.Kind.Ghost, 38, null, 12.5f).Grow().Margin(0, 0, 0, 8));
            dev.Add(r2);
            bool armed = Time.unscaledTime < confirmResetUntil;
            dev.Add(E.Button(armed ? "Tap again to erase this settlement" : "Reset settlement", Act(() =>
            {
                if (Time.unscaledTime < confirmResetUntil) { confirmResetUntil = 0; store.ResetPrototype(); store.ActivePanel = Panel.None; }
                else
                {
                    confirmResetUntil = Time.unscaledTime + 4f;
                    sheet.schedule.Execute(() => dirty = true).StartingIn(4100);
                }
            }), E.Kind.Danger, 40, null, 12.5f).Margin(8));
            host.Add(dev);

            var foot = E.Text($"{G.Content.Branding.Title} · {G.Content.Branding.Subtitle} · v{Application.version}\nContent {G.Content.ContentVersion} · Save format {SaveCodec.CurrentVersion} · Unity {Application.unityVersion}",
                              10.5f, Theme.Faint, Theme.F.Mono).Align(TextAnchor.MiddleCenter).Margin(8);
            host.Add(foot);
        }

        VisualElement Toggle(string label, string hint, bool value, Action<bool> set, bool separator)
        {
            var row = E.Div().Row().Pad(13, 14);
            if (separator) { row.style.borderTopWidth = 1; row.style.borderTopColor = new Color(1, 1, 1, 0.07f); }
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.Text(label, 14, Theme.Ink, Theme.F.TextMedium));
            col.Add(E.Text(hint, 11.5f, Theme.Muted).Margin(2));
            row.Add(col);
            var track = E.Div().Size(46, 28).Radius(14).Ignore();
            if (value) track.BgImage(Theme.GoldFill); else track.Bg(new Color(1, 1, 1, 0.1f)).Border(new Color(1, 1, 1, 0.1f));
            var knob = E.Div().Size(22, 22).Radius(11).Bg(Theme.Ink).Abs(value ? 21 : 3, 3).Ignore();
            track.Add(knob);
            row.Add(track);
            Tap(row, () => set(!value));
            return row;
        }

        static VisualElement InfoRow(string label, string value, bool separator)
        {
            var row = E.Div().Row().Between().Pad(13, 14).Ignore();
            if (separator) { row.style.borderTopWidth = 1; row.style.borderTopColor = new Color(1, 1, 1, 0.07f); }
            row.Add(E.Text(label, 14, Theme.Ink));
            row.Add(E.Text(value, 12.5f, Theme.Muted, Theme.F.Mono).NoWrap());
            return row;
        }
    }
}
