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
    /// Windows opened from the HUD (People, Goals, Research, Customs, Weather, World, Profile, Settings),
    /// after the kit's popup frame: a parchment window centred under the top strip (never the whole
    /// screen: the world shows around it), a wooden plank with the title and the red close button,
    /// a tab strip under it (or along the bottom for Goals, like the kit's task screens) and the golden
    /// back arrow bottom-left, outside the window.
    /// </summary>
    public partial class GameUi
    {
        Panel shownPanel = Panel.None;
        bool sheetShown;
        string buildTab = "All";
        float confirmResetUntil;
        int peopleTab = 1, goalsTab, mapTab;
        VisualElement scrim, sheet, sheetHead, sheetFixed, sheetTabsBottom, backArrow;
        ScrollView sheetScroll;

        /// <summary>Panels drawn in the fixed (non-scrolling) body: they scroll inside themselves.</summary>
        bool FixedPanel => store.ActivePanel == Panel.Research || store.ActivePanel == Panel.Customs;

        bool WindowPanel => SheetOpen && store.ActivePanel != Panel.Build;

        float WindowW => Mathf.Clamp(SafeWidth - 116, 500, 620);

        void BuildSheet()
        {
            scrim = E.Div("scrim").Fill().Bg(Theme.Scrim);
            Tap(scrim, () => store.ActivePanel = Panel.None);
            sheetLayer.Add(scrim);

            sheet = E.Panel().Abs().Pad(0, 10, 10, 10);
            sheet.name = "window";
            sheet.Transition(220, "translate", "opacity", "scale");
            sheetHead = E.Div("window-head").Col().Ignore();
            sheetHead.style.flexShrink = 0;
            AddRegion(sheetHead, () => WindowPanel ? $"{store.ActivePanel}|{peopleTab}|{mapTab}|{HeaderKey()}" : "", BuildSheetHeader);
            sheet.Add(sheetHead);

            sheetScroll = E.Scroll(ScrollViewMode.Vertical);
            sheetScroll.style.flexGrow = 1;
            sheetScroll.style.flexShrink = 1;
            sheetScroll.style.minHeight = 0;
            sheetScroll.contentContainer.style.paddingTop = 6;
            sheetScroll.contentContainer.style.paddingBottom = 6;
            sheet.Add(sheetScroll);
            AddRegion(sheetScroll.contentContainer, () => WindowPanel && !FixedPanel ? $"{store.ActivePanel}|{peopleTab}|{goalsTab}|{mapTab}|{PanelKey()}" : "", BuildPanelContent);

            sheetFixed = E.Div("window-fixed").Col().Ignore();
            sheetFixed.style.flexGrow = 1;
            sheetFixed.style.flexShrink = 1;
            sheetFixed.style.minHeight = 0;
            sheetFixed.style.paddingTop = 6;
            sheet.Add(sheetFixed);
            AddRegion(sheetFixed, () => WindowPanel && FixedPanel ? $"{store.ActivePanel}|{DrawerKey()}" : "", BuildFixedContent);
            sheetLayer.Add(sheet);

            // Bottom tabs (the kit's task screens) sit under the window's lower edge.
            sheetTabsBottom = E.Div("window-tabs").Row(0, Align.FlexEnd).Ignore().Abs();
            AddRegion(sheetTabsBottom, () => WindowPanel && store.ActivePanel == Panel.Objectives ? $"g{goalsTab}" : "", BuildBottomTabs);
            sheetLayer.Add(sheetTabsBottom);

            backArrow = E.Icon("back", 46).Abs();
            backArrow.name = "back";
            Tap(backArrow, () => store.ActivePanel = Panel.None);
            sheetLayer.Add(backArrow);
            BuildDrawer();
        }

        /// <summary>Sizes and places the window (every tick; only changes are written).</summary>
        void LayoutSheet()
        {
            if (sheet == null) return;
            bool window = WindowPanel;
            sheet.Hidden(!window);
            sheetTabsBottom.Hidden(!window || store.ActivePanel != Panel.Objectives);
            float w = WindowW;
            float bottomTabs = store.ActivePanel == Panel.Objectives ? 24 : 0;
            float top = PopupTop, bottom = BottomPad + 8 + bottomTabs;
            float maxH = 430;
            float h = PanelH - top - bottom;
            if (h > maxH) { top += (h - maxH) / 2; bottom += (h - maxH) / 2; }
            float left = SafeLeft + (SafeWidth - w) / 2;
            Place(sheet, left, top, null, bottom);
            if (!Mathf.Approximately(sheet.style.width.value.value, w)) sheet.style.width = w;
            Place(sheetTabsBottom, left + 14, null, null, bottom - bottomTabs - 4);
            bool roomForBack = (SafeWidth - w) / 2 >= 52;
            backArrow.Hidden(!window || !roomForBack);
            Place(backArrow, SafeLeft + 6, null, null, BottomPad + 2);
            sheetScroll.Hidden(!window || FixedPanel);
            sheetFixed.Hidden(!window || !FixedPanel);
        }

        /// <summary>Visibility of layers and the open animation, run once per tick before regions.</summary>
        void OnTick()
        {
            bool open = SheetOpen;
            if (store.ActivePanel != shownPanel)
            {
                shownPanel = store.ActivePanel;
                sheetScroll.scrollOffset = Vector2.zero;
                buildTab = pendingTab ?? "All";
                pendingTab = null;
                if (open) Pop(store.ActivePanel == Panel.Build ? drawer : sheet);
            }
            if (open != sheetShown)
            {
                sheetShown = open;
                sheetLayer.Hidden(!open);
            }
            LayoutHud();
            LayoutSheet();
            UpdateDrawer();
            UpdateHeatOverlay();
            modalLayer.Hidden(ModalKey() == "");
        }

        /// <summary>The opening animation of a popup: a short rise and fade.</summary>
        static void Pop(VisualElement e)
        {
            e.style.translate = new Translate(0, 14);
            e.style.opacity = 0;
            e.schedule.Execute(() =>
            {
                e.style.translate = new Translate(0, 0);
                e.style.opacity = 1;
            }).StartingIn(16);
        }

        string HeaderKey() => store.ActivePanel switch
        {
            Panel.People => $"{G.State.People.Count}|{G.IdleWorkers}",
            Panel.Customs => $"{G.State.Laws.Count}",
            _ => "",
        };

        // MARK: - Header: plank, close, tabs

        void BuildSheetHeader(VisualElement host)
        {
            if (!WindowPanel) return;
            string title = store.ActivePanel switch
            {
                Panel.People => "People & Work",
                Panel.Research => "Research",
                Panel.Objectives => "Chapter Tasks",
                Panel.Weather => "Weather & Warmth",
                Panel.Customs => "Book of Customs",
                Panel.Map => "World & Legacy",
                Panel.Profile => "Founder",
                _ => "Settings",
            };
            var plank = E.Plank(title, null, 16, WindowW);
            plank.style.marginLeft = -14;
            plank.style.marginRight = -14;
            plank.style.marginTop = -4;
            host.Add(plank);
            var close = E.Close(Act(() => store.ActivePanel = Panel.None)).Abs(null, -9, -10);
            host.Add(close);

            var row = E.Div().Row(0, Align.FlexEnd).Ignore();
            row.style.marginTop = 4;
            void TabRow(params (string label, int index, string count)[] tabs)
            {
                int current = store.ActivePanel == Panel.People ? peopleTab : mapTab;
                foreach (var (label, index, count) in tabs)
                {
                    int i = index;
                    var t = E.Tab(label, i == current, Act(() => { if (store.ActivePanel == Panel.People) peopleTab = i; else mapTab = i; sheetScroll.scrollOffset = Vector2.zero; }), count);
                    t.style.flexGrow = 1;
                    t.style.flexBasis = 0;
                    if (row.childCount > 0) t.style.marginLeft = 4;
                    row.Add(t);
                }
            }
            switch (store.ActivePanel)
            {
                case Panel.People:
                    TabRow(("Work", 0, G.IdleWorkers > 0 ? $"{G.IdleWorkers} idle" : null), ("Villagers", 1, G.State.People.Count.ToString(Inv)), ("Wellbeing", 2, null));
                    break;
                case Panel.Map:
                    TabRow(("Explore", 0, null), ("Legacy", 1, G.State.Legacy.Count.ToString(Inv)));
                    break;
                case Panel.Research:
                {
                    var know = E.Capsule(24);
                    know.Add(E.Icon("knowledge", 18));
                    know.Add(Live(() => $"{Fmt.Compact(G.State.Resources.Knowledge)}  {Fmt.PerMinute(econ.Net.Knowledge)}", 12).Margin(0, 0, 0, 4));
                    row.Add(LiveFit(() => $"{G.State.Researched.Count}/{G.Content.Technologies.Count} discovered · {G.HeartName} Lv.{G.HeartLevel}", 12, WindowW - 200, Theme.Pale).Grow());
                    row.Add(know);
                    break;
                }
                case Panel.Customs:
                {
                    var chip = E.Capsule(24);
                    var icon = E.Icon("clock", 16);
                    chip.Add(icon);
                    chip.Add(Live(() => G.LawCooldownLeft > 0 ? $"Next law in {Fmt.Clock(G.LawCooldownLeft)}" : "A law can be signed", 11.5f).Margin(0, 0, 0, 4));
                    row.Add(LiveFit(() =>
                    {
                        string d = G.HasDiscontent ? $" · discontent {(int)Math.Round(G.State.Discontent)}" : "";
                        return $"{G.State.Laws.Count}/{G.Content.Laws.Count} customs signed{d}";
                    }, 12, WindowW - 220, Theme.Pale).Grow());
                    row.Add(chip);
                    break;
                }
                case Panel.Weather when G.HasClimate:
                {
                    var chip = E.Capsule(24);
                    chip.Add(E.Icon("thermometer", 17));
                    chip.Add(Live(() => Deg(G.Temperature), 12).Margin(0, 0, 0, 3));
                    row.Add(LiveFit(() => G.Season == null ? "" : $"{G.Season.Name} · Year {G.Year} · next season in {Fmt.Clock(G.SecondsUntilNextSeason)}", 12, WindowW - 120, Theme.Pale).Grow());
                    row.Add(chip);
                    break;
                }
            }
            if (row.childCount > 0) host.Add(row);
        }

        void BuildBottomTabs(VisualElement host)
        {
            if (!WindowPanel || store.ActivePanel != Panel.Objectives) return;
            foreach (var (label, i) in new[] { ("Chapter", 0), ("Milestones", 1), ("First Steps", 2) })
            {
                int index = i;
                var t = E.Tab(label, goalsTab == i, Act(() => { goalsTab = index; sheetScroll.scrollOffset = Vector2.zero; }),
                              i == 1 && MilestoneReady() ? "!" : null, 26);
                t.style.width = 112;
                t.style.marginRight = 4;
                host.Add(t);
            }
        }

        string PanelKey()
        {
            switch (store.ActivePanel)
            {
                case Panel.People:
                    if (peopleTab == 0)
                        return $"{G.IdleWorkers}|{G.State.Population}|{G.State.FoodShortage}|{G.HousingCapacity()}|" + string.Join(",", Enums.Jobs.Select(j => $"{G.State.Assignments[j]}/{G.JobCapacity(j)}"));
                    if (peopleTab == 1)
                        return expandedPerson + "|" + string.Join(",", G.State.People.Select(p => $"{p.Job}{SkillSum(p)}{(p.Sick ? "s" : "")}")) + "|" + SurvivalPeopleKey();
                    return $"{G.SeasonIndex}|{G.Freezing}|{string.Join(",", G.ApprovalFactors().Select(f => f.Label + f.Points))}|{G.State.FoodShortage}|{SurvivalPeopleKey()}";
                case Panel.Weather:
                    return WeatherKey();
                case Panel.Objectives:
                    return $"{G.State.Chapter}|" + string.Join("", G.ChapterRequirements().Select(r => r.Met ? "1" : "0")) + "|" +
                           string.Join(",", G.State.CompletedObjectives) + "|" + G.CurrentObjective?.Id + "|" +
                           string.Join(",", G.Content.Milestones.Select(m => $"{G.CanClaimMilestone(m.Id)}{G.State.ReachedMilestones.Contains(m.Id)}")) + "|" +
                           (G.CurrentObjective == null ? "" : string.Join("", G.Status(G.CurrentObjective.Conditions).Select(s => s.Met ? "1" : "0"))) + "|" +
                           string.Join("", G.Content.Milestones.SelectMany(m => G.MilestoneRequirements(m.Id)).Select(s => s.Met ? "1" : "0"));
                case Panel.Map:
                    return $"{G.State.ActiveExpedition?.RegionId}|{string.Join(",", G.State.ExploredRegions)}|{G.State.Legacy.Count}|" +
                           string.Join("", G.Content.Regions.Select(r => G.State.Resources.Covers(r.Cost) ? "1" : "0"));
                case Panel.Profile:
                    return $"{G.State.Legacy.Count}|{G.State.Researched.Count}|{G.State.Buildings.Count}|{G.State.StageId}";
                default:
                    return $"{Prefs.Quality}|{Prefs.BatterySaver}|{Prefs.Haptics}|{AudioDirector.MusicOn}|{AudioDirector.SoundOn}|{Time.unscaledTime < confirmResetUntil}|{store.SaveStatus}";
            }
        }

        void BuildPanelContent(VisualElement host)
        {
            if (!WindowPanel || FixedPanel) return;
            switch (store.ActivePanel)
            {
                case Panel.People:
                    if (peopleTab == 0) BuildWorkTab(host);
                    else if (peopleTab == 1) BuildVillagers(host);
                    else BuildWellbeing(host);
                    break;
                case Panel.Objectives: BuildGoalsPanel(host); break;
                case Panel.Weather: BuildWeatherPanel(host); break;
                case Panel.Map: if (mapTab == 0) BuildWorldPanel(host); else BuildLegacy(host); break;
                case Panel.Profile: BuildProfilePanel(host); break;
                default: BuildSettingsPanel(host); break;
            }
        }

        void BuildFixedContent(VisualElement host)
        {
            if (!WindowPanel || !FixedPanel) return;
            if (store.ActivePanel == Panel.Customs) BuildCustoms(host);
            else BuildResearch(host);
        }

        /// <summary>A section with a margin under it, for window bodies.</summary>
        static VisualElement Block(string title, out VisualElement body, VisualElement extra = null)
        {
            var s = E.Section(title, out body, extra);
            s.style.marginBottom = 6;
            return s;
        }

        /// <summary>A small stat box: value over a label.</summary>
        VisualElement StatBox(string icon, string label, Func<string> value, Func<Color> color = null)
        {
            var c = E.Inset().Row().Grow();
            c.Add(E.Icon(icon, 24));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 6;
            var v = Live(value, 15);
            if (color != null) Bind(() => E.Tint(v, color()));
            col.Add(v);
            col.Add(E.T(label, 10, Theme.Pale));
            c.Add(col);
            return c;
        }

        /// <summary>A reward/resource cell with the amount at the bottom (the kit's rarity cells).</summary>
        static VisualElement RewardCell(string icon, E.Cell kind, string amount, float size = 36)
        {
            var c = E.Slot(icon, size, kind, 0.6f);
            c.style.justifyContent = Justify.FlexStart;
            c.ElementAt(0).style.marginTop = 3;
            if (amount != null)
            {
                var l = E.T(amount, 9.5f).Fit(size - 2, 7).Align(TextAnchor.MiddleCenter).Abs(0, null, 0, 3);
                c.Add(l);
            }
            return c;
        }

        // MARK: - People: Work

        static string JobIcon(JobType job) => job switch
        {
            JobType.Foraging => "food",
            JobType.Farming => "wheat",
            JobType.Fishing => "fish",
            JobType.Hunting => "spear",
            JobType.Herding => "hides",
            JobType.Woodcutting => "axe",
            JobType.Stonecutting => "stone",
            JobType.Mining => "pickaxe",
            JobType.Crafting => "settings",
            JobType.Construction => "build",
            JobType.Healing => "sick",
            JobType.Research => "knowledge",
            _ => "people",
        };

        void BuildWorkTab(VisualElement host)
        {
            var stats = E.Div().Row().Ignore().Margin(0, 0, 6);
            stats.Add(StatBox("people", "People / beds", () => $"{G.State.Population}/{G.HousingCapacity()}"));
            stats.Add(StatBox("user", "Idle", () => $"{G.IdleWorkers}", () => G.IdleWorkers > 0 ? Theme.Gold : Color.white).Margin(0, 0, 0, 6));
            stats.Add(StatBox("food", "Food", () => Fmt.PerMinute(econ.Net.Food), () => econ.Net.Food < 0 ? Theme.Bad : Theme.Up).Margin(0, 0, 0, 6));
            host.Add(stats);

            if (G.State.FoodShortage) host.Add(Notice("alert", "Food has run out. All work is slower until food is restored.", true));
            else if (G.State.Population >= G.HousingCapacity()) host.Add(Notice("home", "Build or upgrade shelters so the community can grow."));
            else
            {
                var g = E.Inset().Row().Margin(0, 0, 6);
                g.Add(E.Icon("people", 18));
                g.Add(E.T("Next newcomer", 11.5f).Margin(0, 0, 0, 5).Grow());
                var bar = LiveLabeledBar(() => G.State.GrowthProgress, () => $"{G.State.GrowthProgress * 100:0}%", 13);
                bar.style.width = 150;
                g.Add(bar);
                host.Add(g);
            }

            var sec = Block("Work", out var rows, E.T("Places from workplaces", 10.5f, Theme.Pale));
            bool sep = false;
            foreach (var job in Enums.Jobs) { rows.Add(JobRow(job, sep)); sep = true; }
            host.Add(sec);
        }

        /// <summary>One job: icon, name and effect, the −/+ stepper with workers/places.</summary>
        VisualElement JobRow(JobType job, bool separator)
        {
            var config = G.Content.JobConfig(job);
            var row = E.Line(separator, 40);
            row.Add(E.Slot(JobIcon(job), 30, E.Cell.Slot, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 6;
            col.Add(E.T(config?.Name ?? job.Id(), 12));
            var effect = LiveFit(() => EffectText(job, G.State.Assignments[job]), 10, WindowW - 220, Theme.Pale);
            col.Add(effect);
            row.Add(col);
            int workers = G.State.Assignments[job];
            int capacity = G.JobCapacity(job);
            bool unlimited = config?.UnlimitedSlots ?? false;
            row.Add(E.Square("minus", workers > 0 ? Act(() => store.SetWorkers(job, G.State.Assignments[job] - 1)) : null, workers > 0 ? E.Kind.Red : E.Kind.Grey, 28));
            var count = E.Capsule(24).Pad(0, 6);
            count.style.width = 62;
            count.style.justifyContent = Justify.Center;
            count.style.marginLeft = -2; count.style.marginRight = -2;
            count.Add(E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", 12).Fit(52, 8));
            row.Add(count);
            bool canAdd = G.IdleWorkers > 0 && workers < capacity;
            row.Add(E.Square("plus", canAdd ? Act(() => store.SetWorkers(job, G.State.Assignments[job] + 1)) : null, canAdd ? E.Kind.Green : E.Kind.Grey, 28));
            return row;
        }

        // MARK: - People: Villagers

        int SkillSum(Person p) => Enums.Jobs.Sum(j => G.SkillLevel(p, j));
        int expandedPerson = -1;

        void BuildVillagers(VisualElement host)
        {
            var grid = E.Div().Row(0, Align.FlexStart).Ignore();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            float cellW = (WindowW - 20 - 6) / 2;
            int shown = 0;
            // The sick first (the gravest on top), then the most skilled.
            foreach (var p in G.State.People.OrderByDescending(x => x.Sick).ThenByDescending(x => x.Sick ? x.Severity : 0).ThenByDescending(SkillSum))
            {
                if (shown++ >= 60) break;
                var cell = E.Inset().Row().Pad(5, 8);
                cell.style.width = cellW;
                cell.style.marginBottom = 6;
                cell.style.minHeight = 50;
                var face = E.Slot(FaceFor(p), 38, E.Cell.Slot, 0.86f);
                if (p.Sick) face.Add(E.Icon("sick", 16).Abs(null, null, -4, -2));
                cell.Add(face);
                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 8;
                col.Add(E.T(p.Name, 12.5f).Fit(cellW - 140, 8));
                var job = p.Job == null ? null : Enums.Jobs.Cast<JobType?>().FirstOrDefault(j => j.Value.Id() == p.Job);
                string doing = job == null ? "Idle" : G.Content.JobConfig(job.Value)?.Name ?? p.Job;
                col.Add(E.T(doing, 10.5f, job == null ? Theme.Gold : Theme.Pale).Fit(cellW - 140, 7));
                if (p.Sick) SickLine(col, p, cellW - 140);
                cell.Add(col);
                var best = G.BestSkill(p);
                if (best != null)
                {
                    int lv = G.SkillLevel(p, best.Value);
                    var skill = E.Div().Col(Align.FlexEnd).Ignore().Size(84);
                    skill.Add(E.T($"{SkillTitle(best.Value)} <color=#D7F25A>Lv.{lv}</color>", 10.5f).Fit(84, 7));
                    var bar = E.Bar(out var fill, 8, E.FillKind.Gold);
                    E.SetFill(fill, G.SkillProgress(p, best.Value));
                    bar.style.width = 70;
                    bar.style.marginTop = 3;
                    skill.Add(bar);
                    cell.Add(skill);
                }
                int pid = p.Id;
                Tap(cell, () => expandedPerson = expandedPerson == pid ? -1 : pid);
                grid.Add(cell);
                if (expandedPerson == p.Id)
                {
                    var skills = PersonSkills(p);
                    skills.style.width = Length.Percent(100);
                    grid.Add(skills);
                }
            }
            if (G.State.People.Count == 0) grid.Add(E.Ink("No one lives here yet."));
            host.Add(grid);
            host.Add(E.Ink($"Tap someone to see their skills. Every job has its own skill up to level {G.Content.Balance.MaxSkillLevel}: each level adds " +
                           $"{G.Content.Balance.SkillBonusPerLevel * 100:0}% output and perks come at levels 2, 4, 6, 8 and 10. The most skilled idle people take new jobs first.",
                           10.5f).Margin(2, 4, 0, 4));
        }

        /// <summary>One person's skills: level, progress to the next and the perks earned in each job.</summary>
        VisualElement PersonSkills(Person p)
        {
            var box = E.Section($"{p.Name}'s skills", out var rows);
            box.style.marginBottom = 6;
            var jobs = Enums.Jobs.Where(j => Game.Experience(p, j) > 0).OrderByDescending(j => Game.Experience(p, j)).ToList();
            if (jobs.Count == 0) rows.Add(E.Ink("No experience yet.").Margin(4, 0, 4));
            bool sep = false;
            foreach (var job in jobs)
            {
                int lv = G.SkillLevel(p, job);
                var line = E.Line(sep, 26);
                sep = true;
                line.Add(E.Icon(JobIcon(job), 16).Margin(0, 5));
                line.Add(E.T(SkillTitle(job), 11.5f).Grow());
                var bar = E.Bar(out var fill, 8, E.FillKind.Gold);
                E.SetFill(fill, G.SkillProgress(p, job));
                bar.style.width = 80;
                bar.style.marginRight = 8;
                line.Add(bar);
                line.Add(E.T($"Lv.{lv}/{G.Content.Balance.MaxSkillLevel}", 11, Theme.Lv));
                rows.Add(line);
                var skill = G.Content.Skill(job);
                if (skill == null) continue;
                var perks = E.Div().Row().Ignore().Margin(0, 0, 4);
                perks.style.flexWrap = Wrap.Wrap;
                foreach (var perk in skill.Perks)
                {
                    bool has = perk.Level <= lv;
                    var chip = E.Capsule(20).Pad(0, 7).Margin(0, 4, 3, 0);
                    chip.Add(E.T($"{perk.Level} · {perk.Name}", 10, has ? Theme.Gold : Theme.Hex("#B9A898")));
                    perks.Add(chip);
                }
                rows.Add(perks);
            }
            return box;
        }

        string SkillTitle(JobType job) => G.Content.Skill(job)?.Title ?? SkillTitleFallback(job);

        static string SkillTitleFallback(JobType job) => job switch
        {
            JobType.Foraging => "Forager",
            JobType.Farming => "Farmer",
            JobType.Fishing => "Fisher",
            JobType.Hunting => "Hunter",
            JobType.Herding => "Herder",
            JobType.Woodcutting => "Woodcutter",
            JobType.Stonecutting => "Quarrier",
            JobType.Mining => "Miner",
            JobType.Crafting => "Toolmaker",
            JobType.Construction => "Builder",
            JobType.Healing => "Healer",
            JobType.Research => "Thinker",
            _ => job.Id(),
        };

        // MARK: - People: Wellbeing (approval, discontent, health, season, upkeep)

        void BuildWellbeing(VisualElement host)
        {
            var pct = E.T("", 12);
            Bind(() => { int a = ApprovalNow; string s = a + "%"; if (pct.text != s) pct.text = s; E.Tint(pct, ApprovalColor(a)); });
            var card = Block("Hope", out var rows, pct);
            var bar = E.Bar(out var fill, 11, E.FillKind.Green);
            Bind(() => { int a = ApprovalNow; E.SetFill(fill, a / 100.0); });
            bar.style.marginTop = 6;
            rows.Add(bar);
            var effect = Live(() =>
            {
                int a = ApprovalNow;
                int work = (int)Math.Round((G.ApprovalProductivity(a) - 1) * 100);
                string growth = a < 30 ? "no newcomers" : $"growth ×{G.ApprovalGrowth(a):0.0}";
                return $"Work {(work >= 0 ? "+" : "")}{work}% · {growth}";
            }, 11, Theme.Pale);
            effect.style.marginTop = 3;
            effect.style.marginBottom = 2;
            rows.Add(effect);
            rows.Add(E.StatRow("approval", "Base", Game.ApprovalBase.ToString(Inv), null, true, Theme.Pale));
            foreach (var f in G.ApprovalFactors())
                rows.Add(E.StatRow(null, f.Label, (f.Points > 0 ? "+" : f.Points < 0 ? "−" : "") + Math.Abs(f.Points), null, true,
                                   f.Points > 0 ? Theme.Up : f.Points < 0 ? Theme.Bad : Theme.Pale));
            host.Add(card);
            BuildDiscontent(host);
            BuildHealth(host);

            var season = G.Season;
            if (season != null)
            {
                var sc = Block($"{season.Name} · Year {G.Year}", out var srows, Live(() => Fmt.Clock(G.SecondsUntilNextSeason), 11, Theme.Pale));
                if (!string.IsNullOrEmpty(season.Description)) srows.Add(E.Ink(season.Description, 10.5f).Margin(4, 0, 2));
                srows.Add(E.StatRow("food", "Foraging", $"×{season.Foraging:0.##}", null, true));
                srows.Add(E.StatRow("wheat", "Farming", season.Farming <= 0 ? "None" : $"×{season.Farming:0.##}", null, true, season.Farming <= 0 ? Theme.Bad : (Color?)null));
                if (season.FuelPerPersonPerSecond > 0) srows.Add(E.StatRow("flame", "Firewood", $"{G.FuelPerSecond * 60:0.#}/m", null, true));
                if (G.Freezing) srows.Add(Notice("alert", "The woodpile is empty and people are freezing. Put more people on Wood Gathering.", true));
                host.Add(sc);
            }

            var up = Block("Upkeep", out var urows);
            var r1 = E.Line(false);
            r1.Add(E.Icon("food", 15).Margin(0, 5));
            r1.Add(E.T("Eating", 11.5f).Grow());
            r1.Add(Live(() => Fmt.PerMinute(-G.State.Population * G.Content.Balance.FoodPerPersonPerSecond), 12, Theme.Bad));
            urows.Add(r1);
            var r2 = E.Line(true);
            r2.Add(E.Icon("storage", 15).Margin(0, 5));
            r2.Add(E.T("Spoiling", 11.5f).Grow());
            r2.Add(Live(() => Fmt.PerMinute(-econ.Spoilage), 12, Theme.Bad));
            urows.Add(r2);
            host.Add(up);
        }

        // MARK: - Goals (the kit's Chapter Tasks)

        void BuildGoalsPanel(VisualElement host)
        {
            if (goalsTab == 0) BuildChapters(host);
            else if (goalsTab == 1) BuildMilestones(host);
            else BuildObjectives(host);
        }

        void BuildChapters(VisualElement host)
        {
            var current = G.CurrentChapter;
            if (current == null) { host.Add(E.Ink("The story has not begun yet.")); return; }
            // Banner: the chapter on dark wood, with the story button.
            var banner = E.Div().Row().Radius(9).Border(Theme.Outline, 2).Pad(8, 10).Margin(0, 0, 6);
            banner.BgImage(Theme.Gradient("banner", false, Theme.Hex("#4A2E1A"), Theme.Hex("#8A5A32")));
            banner.style.minHeight = 64;
            banner.Add(E.Icon("campfire", 44));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            col.Add(E.T($"Chapter {current.Number} · {current.Era}", 11, Theme.Gold));
            col.Add(E.T(current.Title, 15).Fit(WindowW - 230, 9));
            col.Add(E.T(current.Goal, 10.5f, Theme.Pale).Wrap());
            banner.Add(col);
            banner.Add(E.Button("Story", Act(() => G.State.PendingStory = current.Id), E.Kind.Orange, 32, "legacy", 12.5f, null, 92).Margin(0, 0, 0, 8));
            host.Add(banner);

            if (current.Locked) host.Add(Notice("lock", "The next era is still being built. Your settlement keeps growing until it arrives."));
            else
                foreach (var req in G.ChapterRequirements()) host.Add(TaskRow(req.Text, req.Met, () => store.ActivePanel = Panel.Build));

            // The road ahead: every chapter, with the ones still to come.
            var road = Block("The road ahead", out var rows);
            road.style.marginTop = 4;
            bool sep = false;
            foreach (var ch in G.Content.Chapters)
            {
                bool done = ch.Number < current.Number;
                bool now = ch.Number == current.Number;
                var row = E.Line(sep);
                sep = true;
                row.Add(E.Icon(done ? "check" : ch.Locked ? "lock" : now ? "flame" : "next", 16).Margin(0, 6));
                row.Add(E.T($"{ch.Number}. {ch.Title}", 11.5f, now ? Theme.Gold : done ? (Color?)null : Theme.Pale).Fit(WindowW - 200, 8).Grow());
                row.Add(E.T(ch.Era, 10.5f, Theme.Pale));
                rows.Add(row);
            }
            host.Add(road);
        }

        /// <summary>A task: title, then ✓ when done or a blue Go.</summary>
        VisualElement TaskRow(string text, bool met, Action go)
        {
            var r = E.Inset().Row().Margin(0, 0, 5);
            r.style.minHeight = 38;
            r.Add(E.T(text, 12, met ? Theme.Up : (Color?)null).Fit(WindowW - 130, 8).Grow());
            var pill = E.Capsule(20).Pad(0, 7);
            pill.Add(E.T(met ? "1/1" : "0/1", 11, met ? Theme.Up : Color.white));
            r.Add(pill);
            var right = E.Div().Row().Center().Ignore().Size(58);
            right.Add(met ? E.Icon("check", 24) : E.Button("Go", Act(go), E.Kind.Blue, 28, null, 13, null, 52));
            r.Add(right);
            return r;
        }

        void BuildMilestones(VisualElement host)
        {
            foreach (var ms in G.Content.Milestones)
            {
                bool reached = G.State.ReachedMilestones.Contains(ms.Id);
                var head = reached ? E.T("Reached", 11, Theme.Up) : null;
                var card = Block(ms.Name, out var rows, head);
                if (!string.IsNullOrEmpty(ms.Description)) rows.Add(E.Ink(ms.Description, 10.5f).Margin(4, 0, 2));
                if (!reached)
                {
                    bool sep = true;
                    foreach (var req in G.MilestoneRequirements(ms.Id))
                    {
                        rows.Add(RequirementRow("goals", req.Text, req.Met, null, sep, WindowW));
                        sep = true;
                    }
                    bool can = G.CanClaimMilestone(ms.Id);
                    string id = ms.Id;
                    var claim = E.Button(can ? "Claim" : "Not ready yet", can ? Act(() => { store.ClaimMilestone(id); store.ActivePanel = Panel.None; }) : null,
                                         can ? E.Kind.Orange : E.Kind.Grey, 34, can ? "trophy" : null, 13);
                    claim.style.marginTop = 4;
                    claim.style.marginBottom = 4;
                    claim.style.alignSelf = Align.Center;
                    claim.style.minWidth = 180;
                    rows.Add(claim);
                }
                else rows.Add(E.Div().Row().Center().Ignore().Margin(4, 0, 4).Kids(E.Icon("trophy", 30)));
                host.Add(card);
            }
        }

        void BuildObjectives(VisualElement host)
        {
            var current = G.CurrentObjective;
            foreach (var obj in G.Content.Objectives)
            {
                bool done = G.State.CompletedObjectives.Contains(obj.Id);
                bool isCurrent = current != null && current.Id == obj.Id;
                var card = E.Inset().Col().Margin(0, 0, 5);
                if (!done && !isCurrent) card.style.opacity = 0.7f;
                var head = E.Div().Row().Ignore();
                head.style.minHeight = 30;
                head.Add(E.Icon(done ? "check" : isCurrent ? "goals" : "lock", 20));
                head.Add(E.T(obj.Title, 12, isCurrent ? Theme.Gold : (Color?)null).Fit(WindowW - 200, 8).Grow().Margin(0, 0, 0, 6));
                if (obj.Reward.HasValue && !done)
                    foreach (var kv in obj.Reward.Value.Entries)
                        head.Add(RewardCell(IconFor(kv.Key), CellFor(kv.Key), Fmt.Compact(kv.Value), 32).Margin(0, 0, 0, 4));
                card.Add(head);
                if (isCurrent)
                {
                    if (!string.IsNullOrEmpty(obj.Hint)) card.Add(E.Ink(obj.Hint, 10.5f).Margin(3));
                    bool sep = true;
                    foreach (var req in G.Status(obj.Conditions)) card.Add(RequirementRow("next", req.Text, req.Met, null, sep, WindowW));
                }
                host.Add(card);
            }
        }

        // MARK: - World & Legacy

        void BuildWorldPanel(VisualElement host)
        {
            var info = E.Inset().Col().Margin(0, 0, 6);
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("world", 22));
            head.Add(E.T(G.State.SettlementName, 14).Margin(0, 0, 0, 6).Grow());
            head.Add(E.T(G.Content.Map.Name ?? "", 11, Theme.Pale));
            info.Add(head);
            var traits = E.Div().Row().Ignore().Margin(4);
            traits.style.flexWrap = Wrap.Wrap;
            foreach (var t in G.Content.Map.Traits)
            {
                var chip = E.Capsule(20).Pad(0, 7).Margin(0, 4, 3, 0);
                chip.Add(E.T(t, 10.5f));
                traits.Add(chip);
            }
            info.Add(traits);
            host.Add(info);

            var active = G.State.ActiveExpedition;
            foreach (var region in G.Content.Regions.Where(r => !r.RevealedAtStart))
            {
                bool explored = G.State.ExploredRegions.Contains(region.Id);
                var card = Block(region.Name, out var rows, explored ? E.T("Explored", 11, Theme.Up) : null);
                rows.Add(E.Ink(region.Description, 10.5f).Margin(4, 0, 2));
                var finds = RegionFinds(region, explored);
                if (finds != null) rows.Add(finds);
                if (active != null && active.RegionId == region.Id)
                {
                    var r = E.Line(true);
                    r.Add(E.Icon("compass", 15).Margin(0, 5));
                    r.Add(E.T("Scouts away", 11.5f).Grow());
                    r.Add(Live(() => Fmt.Clock(G.State.ActiveExpedition?.RemainingSeconds ?? 0), 12));
                    rows.Add(r);
                    rows.Add(LiveBar(() => { var e = G.State.ActiveExpedition; return e == null ? 1 : 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds); }, 10).Margin(0, 0, 5));
                }
                else if (!explored)
                {
                    var bottom = E.Line(true, 40);
                    bottom.Add(CostLine(region.Cost, 11).Grow());
                    bool can = active == null && G.State.Resources.Covers(region.Cost);
                    string id = region.Id;
                    bottom.Add(E.Button(active != null ? "Scouts busy" : "Send scouts", can ? Act(() => store.Explore(id)) : null,
                                        can ? E.Kind.Green : E.Kind.Grey, 34, null, 12.5f, SubLine("clock", Fmt.Clock(region.ExpeditionSeconds)), 130));
                    rows.Add(bottom);
                }
                host.Add(card);
            }
        }

        void BuildLegacy(VisualElement host)
        {
            var entries = Enumerable.Reverse(G.State.Legacy).ToList();
            if (entries.Count == 0) host.Add(E.Ink("Nothing written yet: your choices will fill these pages."));
            for (int i = 0; i < entries.Count; i++) host.Add(LegacyRow(entries[i], i == entries.Count - 1));
        }

        VisualElement LegacyRow(LegacyEntry entry, bool last)
        {
            var row = E.Div().Row(0, Align.Stretch).Ignore();
            var rail = E.Div().Col(Align.Center).Size(24).Ignore();
            rail.Add(E.Icon("sparkle", 18).Margin(2));
            if (!last)
            {
                var l = E.Div().Ignore().Bg(Theme.Wood4);
                l.style.width = 3;
                l.style.flexGrow = 1;
                l.style.marginTop = 2;
                rail.Add(l);
            }
            row.Add(rail);
            var body = E.Inset().Col().Grow().Margin(0, 0, 5, 6);
            var head = E.Div().Row().Ignore();
            head.Add(E.T(entry.Title, 12).Fit(WindowW - 150, 8).Grow());
            head.Add(E.T(Fmt.Clock(entry.SimTime), 10.5f, Theme.Pale));
            body.Add(head);
            if (!string.IsNullOrEmpty(entry.Detail)) body.Add(E.Ink(entry.Detail, 10.5f).Margin(2));
            row.Add(body);
            return row;
        }

        // MARK: - Founder (profile)

        void BuildProfilePanel(VisualElement host)
        {
            var hero = E.Inset().Row().Margin(0, 0, 6);
            hero.Add(E.Slot(G.State.EpochId == "epoch_medieval_kingdoms" ? "landmark" : "home", 56, E.Cell.Tile, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.T(G.State.SettlementName, 17).Fit(WindowW - 200, 10));
            col.Add(E.T($"{G.StageName} · Era {Fmt.Roman(G.Content.Epoch(G.State.EpochId)?.Index ?? 1)}", 11, Theme.Pale));
            col.Add(E.Ink(G.EpochName, 10.5f));
            hero.Add(col);
            var id = E.Capsule(24);
            id.Add(E.T("ID " + FounderId(), 11));
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
            float w = (WindowW - 20 - 12) / 3;
            foreach (var s in stats)
            {
                var c = StatBox(s.icon, s.label, s.value);
                c.style.flexGrow = 0;
                c.style.width = w;
                c.style.marginBottom = 6;
                grid.Add(c);
            }
            host.Add(grid);

            var ms = Block("Milestones", out var mrows);
            var cells = E.Div().Row().Ignore().Margin(5, 0, 4);
            cells.style.flexWrap = Wrap.Wrap;
            foreach (var m in G.Content.Milestones)
            {
                bool got = G.State.ReachedMilestones.Contains(m.Id);
                var c = E.Div().Col(Align.Center).Ignore().Size(96);
                c.Add(E.Slot(got ? "trophy" : "lock", 40, got ? E.Cell.Legendary : E.Cell.Common, 0.62f, !got));
                c.Add(E.T(m.Name, 10.5f, got ? (Color?)null : Theme.Pale).TwoLines(92, 7).Margin(2));
                cells.Add(c);
            }
            mrows.Add(cells);
            host.Add(ms);
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
            var gfx = Block("Graphics", out var grows);
            var seg = E.Div().Row(0, Align.FlexEnd).Ignore().Margin(6, 0, 6);
            string[] names = { "Smooth", "Balanced", "Cinematic" };
            for (int i = 0; i < names.Length; i++)
            {
                int q = i;
                var t = E.Tab(names[i], Prefs.Quality == i, Act(() => { Prefs.Quality = q; Prefs.Apply(); }));
                t.style.flexGrow = 1;
                t.style.flexBasis = 0;
                if (i > 0) t.style.marginLeft = 4;
                seg.Add(t);
            }
            grows.Add(seg);
            host.Add(gfx);

            var game = Block("Game", out var rows);
            rows.Add(ToggleRow("Battery saver", "30 fps, lighter on the battery", Prefs.BatterySaver, v => { Prefs.BatterySaver = v; Prefs.Apply(); }, false));
            rows.Add(ToggleRow("Haptics", "A tap of vibration on milestones", Prefs.Haptics, v => Prefs.Haptics = v, true));
            rows.Add(ToggleRow("Music", "Calm themes between the seasons", AudioDirector.MusicOn, v => AudioDirector.MusicOn = v, true));
            rows.Add(ToggleRow("Sound", "Fire, river, birds, wind and taps", AudioDirector.SoundOn, v => AudioDirector.SoundOn = v, true));
            host.Add(game);

            var acc = Block("Account", out var arows);
            arows.Add(E.StatRow("user", "Founder ID", FounderId(), null, false));
            arows.Add(E.StatRow("storage", "Save", store.SaveStatus, null, true, Theme.Pale));
            arows.Add(E.StatRow("world", "Language", "English", null, true));
            host.Add(acc);

            var dev = Block("Developer", out var drows);
            var r1 = E.Div().Row().Ignore().Margin(6);
            r1.Add(Share(E.Button("Save now", Act(store.Save), E.Kind.Blue, 32, null, 12), r1));
            r1.Add(Share(E.Button("+50 all", Act(store.DebugGrant), E.Kind.Blue, 32, null, 12), r1));
            r1.Add(Share(E.Button("+1 min", Act(() => store.DebugFastForward(1)), E.Kind.Blue, 32, null, 12), r1));
            r1.Add(Share(E.Button("+10 min", Act(() => store.DebugFastForward(10)), E.Kind.Blue, 32, null, 12), r1));
            drows.Add(r1);
            bool armed = Time.unscaledTime < confirmResetUntil;
            var reset = E.Button(armed ? "Tap again to erase this settlement" : "Reset settlement", Act(() =>
            {
                if (Time.unscaledTime < confirmResetUntil) { confirmResetUntil = 0; store.ResetPrototype(); store.ActivePanel = Panel.None; }
                else
                {
                    confirmResetUntil = Time.unscaledTime + 4f;
                    sheet.schedule.Execute(() => dirty = true).StartingIn(4100);
                }
            }), E.Kind.Red, 32, "trash", 12);
            reset.style.marginTop = 6;
            reset.style.marginBottom = 6;
            drows.Add(reset);
            host.Add(dev);

            host.Add(E.Ink($"{G.Content.Branding.Title} · {G.Content.Branding.Subtitle} · v{Application.version}\nContent {G.Content.ContentVersion} · Save format {SaveCodec.CurrentVersion} · Unity {Application.unityVersion}",
                           10, Theme.InkSoft).Align(TextAnchor.MiddleCenter).Margin(2));
        }

        /// <summary>A setting with the kit's ON/OFF switch.</summary>
        VisualElement ToggleRow(string label, string hint, bool value, Action<bool> set, bool separator)
        {
            var row = E.Line(separator, 38);
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.T(label, 12));
            col.Add(E.T(hint, 10, Theme.Pale).Fit(WindowW - 150, 7));
            row.Add(col);
            var sw = E.Div().Row().Skin(value ? "btn_green" : "btn_grey").Pad(0, 4, 3, 6).Size(58, 26).Ignore();
            var knob = E.Div().Skin("slot").Size(18, 18).Ignore();
            var text = E.T(value ? "ON" : "OFF", 10.5f).Grow().Align(TextAnchor.MiddleCenter);
            if (value) { sw.Add(text); sw.Add(knob); }
            else { sw.Add(knob); sw.Add(text); }
            row.Add(sw);
            Tap(row, () => set(!value));
            return row;
        }
    }
}
