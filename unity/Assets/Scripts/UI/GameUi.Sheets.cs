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

        /// <summary>The centred window: as wide as the kit's popups allow next to the back arrow (620–680 on phones).</summary>
        float WindowW => Mathf.Clamp(SafeWidth - 120, 560, 680);

        /// <summary>Usable width inside a window's body (panel padding 10 + 10).</summary>
        float InnerW => WindowW - 20;

        /// <summary>Height of the goals' bottom tabs under the window.</summary>
        const float BottomTabsH = 40;

        void BuildSheet()
        {
            scrim = E.Div("scrim").Fill().Bg(Theme.Scrim);
            Tap(scrim, () => store.ActivePanel = Panel.None);
            sheetLayer.Add(scrim);

            sheet = E.Panel().Abs().Pad(0, 10, 12, 10);
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
            sheetScroll.contentContainer.style.paddingTop = 8;
            sheetScroll.contentContainer.style.paddingBottom = 8;
            sheet.Add(sheetScroll);
            AddRegion(sheetScroll.contentContainer, () => WindowPanel && !FixedPanel ? $"{store.ActivePanel}|{peopleTab}|{goalsTab}|{mapTab}|{PanelKey()}" : "", BuildPanelContent);

            sheetFixed = E.Div("window-fixed").Col().Ignore();
            sheetFixed.style.flexGrow = 1;
            sheetFixed.style.flexShrink = 1;
            sheetFixed.style.minHeight = 0;
            sheetFixed.style.paddingTop = 8;
            sheet.Add(sheetFixed);
            AddRegion(sheetFixed, () => WindowPanel && FixedPanel ? $"{store.ActivePanel}|{DrawerKey()}" : "", BuildFixedContent);
            sheetLayer.Add(sheet);

            // Bottom tabs (the kit's task screens) sit under the window's lower edge.
            sheetTabsBottom = E.Div("window-tabs").Row(0, Align.FlexEnd).Ignore().Abs();
            AddRegion(sheetTabsBottom, () => WindowPanel && store.ActivePanel == Panel.Objectives ? $"g{goalsTab}" : "", BuildBottomTabs);
            sheetLayer.Add(sheetTabsBottom);

            backArrow = E.Icon("back", 54).Abs();
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
            float bottomTabs = store.ActivePanel == Panel.Objectives ? BottomTabsH - 6 : 0;
            float top = PopupTop, bottom = BottomPad + 8 + bottomTabs;
            float maxH = 470;
            float h = PanelH - top - bottom;
            if (h > maxH) { top += (h - maxH) / 2; bottom += (h - maxH) / 2; }
            float left = SafeLeft + (SafeWidth - w) / 2;
            Place(sheet, left, top, null, bottom);
            if (!Mathf.Approximately(sheet.style.width.value.value, w)) sheet.style.width = w;
            Place(sheetTabsBottom, left + 16, null, null, bottom - bottomTabs - 4);
            bool roomForBack = (SafeWidth - w) / 2 >= 56;
            backArrow.Hidden(!window || !roomForBack);
            Place(backArrow, SafeLeft + 4, null, null, BottomPad);
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
            var plank = E.Plank(title, null, Theme.H1, WindowW);
            plank.style.marginLeft = -14;
            plank.style.marginRight = -14;
            plank.style.marginTop = -6;
            host.Add(plank);
            var close = E.Close(Act(() => store.ActivePanel = Panel.None)).Abs(null, -12, -12);
            host.Add(close);

            var row = E.Div().Row(0, Align.FlexEnd).Ignore();
            row.style.marginTop = 6;
            void TabRow(params (string label, int index, string count)[] tabs)
            {
                int current = store.ActivePanel == Panel.People ? peopleTab : mapTab;
                float tabW = (InnerW - 6 * (tabs.Length - 1)) / tabs.Length;
                foreach (var (label, index, count) in tabs)
                {
                    int i = index;
                    var t = E.Tab(label, i == current, Act(() => { if (store.ActivePanel == Panel.People) peopleTab = i; else mapTab = i; sheetScroll.scrollOffset = Vector2.zero; }), count).FitTab(tabW);
                    t.style.flexGrow = 1;
                    t.style.flexBasis = 0;
                    if (row.childCount > 0) t.style.marginLeft = 6;
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
                    var know = E.Capsule(34);
                    know.Add(E.Icon("knowledge", 24));
                    know.Add(Live(() => $"{Fmt.Compact(G.State.Resources.Knowledge)}  {Fmt.PerMinute(econ.Net.Knowledge)}", Theme.Num).Margin(0, 0, 0, 5));
                    row.Add(LiveFit(() => $"{G.State.Researched.Count}/{G.Content.Technologies.Count} discovered · {G.HeartName} Lv.{G.HeartLevel}", Theme.Body, InnerW - 190, Theme.Pale).Grow());
                    row.Add(know);
                    break;
                }
                case Panel.Customs:
                {
                    var chip = E.Capsule(34);
                    var icon = E.Icon("clock", 22);
                    chip.Add(icon);
                    chip.Add(Live(() => G.LawCooldownLeft > 0 ? $"Next law in {Fmt.Clock(G.LawCooldownLeft)}" : "A law can be signed", Theme.Body).Margin(0, 0, 0, 5));
                    row.Add(LiveFit(() =>
                    {
                        string d = G.HasDiscontent ? $" · discontent {(int)Math.Round(G.State.Discontent)}" : "";
                        return $"{G.State.Laws.Count}/{G.Content.Laws.Count} customs signed{d}";
                    }, Theme.Body, InnerW - 230, Theme.Pale).Grow());
                    row.Add(chip);
                    break;
                }
                case Panel.Weather when G.HasClimate:
                {
                    var chip = E.Capsule(34);
                    chip.Add(E.Icon("thermometer", 24));
                    chip.Add(Live(() => Deg(G.Temperature), Theme.Num).Margin(0, 0, 0, 4));
                    row.Add(LiveFit(() => G.Season == null ? "" : $"{G.Season.Name} · Year {G.Year} · next season in {Fmt.Clock(G.SecondsUntilNextSeason)}", Theme.Body, InnerW - 110, Theme.Pale).Grow());
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
                              i == 1 && MilestoneReady() ? "!" : null, BottomTabsH).FitTab(150);
                t.style.width = 150;
                t.style.marginRight = 6;
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
            s.style.marginBottom = 8;
            return s;
        }

        /// <summary>A small stat box: value over a label.</summary>
        VisualElement StatBox(string icon, string label, Func<string> value, Func<Color> color = null)
        {
            var c = E.Inset().Row().Grow();
            c.style.minHeight = 58;
            c.Add(E.Icon(icon, 32));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 8;
            var v = Live(value, Theme.H2);
            if (color != null) Bind(() => E.Tint(v, color()));
            col.Add(v);
            col.Add(E.T(label, Theme.Small, Theme.Pale));
            c.Add(col);
            return c;
        }

        /// <summary>A reward/resource cell with the amount at the bottom (the kit's rarity cells).</summary>
        static VisualElement RewardCell(string icon, E.Cell kind, string amount, float size = 48)
        {
            var c = E.Slot(icon, size, kind, 0.58f);
            c.style.justifyContent = Justify.FlexStart;
            c.ElementAt(0).style.marginTop = 3;
            if (amount != null)
            {
                // The amount sits on the cell's lower edge and may reach past its sides a little (like the kit's "80,000").
                var l = E.T(amount, Theme.Small).Align(TextAnchor.MiddleCenter).Abs(-6, null, -6, 1);
                l.style.whiteSpace = WhiteSpace.NoWrap;
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
            var stats = E.Div().Row().Ignore().Margin(0, 0, 8);
            // People / beds, with the next newcomer's progress under it (no row of its own: the jobs come first).
            var people = StatBox("people", "People / beds", () => $"{G.State.Population}/{G.HousingCapacity()}");
            var growth = LiveBar(() => G.State.GrowthProgress, 8);
            growth.style.marginTop = 3;
            people.ElementAt(1).Add(growth);
            stats.Add(people);
            stats.Add(StatBox("user", "Idle", () => $"{G.IdleWorkers}", () => G.IdleWorkers > 0 ? Theme.Gold : Color.white).Margin(0, 0, 0, 8));
            stats.Add(StatBox("food", "Food", () => Fmt.PerMinute(econ.Net.Food), () => econ.Net.Food < 0 ? Theme.Bad : Theme.Up).Margin(0, 0, 0, 8));
            host.Add(stats);

            if (G.State.FoodShortage) host.Add(Notice("alert", "Food has run out. All work is slower until food is restored.", true).Margin(0, 0, 8));
            else if (G.State.Population >= G.HousingCapacity()) host.Add(Notice("home", "Build or upgrade shelters so the community can grow.").Margin(0, 0, 8));

            var sec = Block("Work", out var rows, E.T("Places from workplaces", Theme.Small, Theme.Pale));
            bool sep = false;
            foreach (var job in Enums.Jobs) { rows.Add(JobRow(job, sep)); sep = true; }
            host.Add(sec);
        }

        /// <summary>One job: icon, name and effect, the −/+ stepper with workers/places.</summary>
        VisualElement JobRow(JobType job, bool separator)
        {
            var config = G.Content.JobConfig(job);
            var row = E.Line(separator, 58);
            row.Add(E.Slot(JobIcon(job), 44, E.Cell.Slot, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 10;
            col.Add(E.T(config?.Name ?? job.Id(), Theme.Num));
            // Stepper: 40 + 80 + 40 (overlapping by 3 on each side); the effect line gets the rest.
            float stepper = 40 + 80 + 40 - 6 + 8;
            var effect = LiveFit(() => EffectText(job, G.State.Assignments[job]), Theme.Small, InnerW - 20 - 44 - 10 - stepper, Theme.Pale);
            col.Add(effect);
            row.Add(col);
            int workers = G.State.Assignments[job];
            int capacity = G.JobCapacity(job);
            bool unlimited = config?.UnlimitedSlots ?? false;
            row.Add(E.Square("minus", workers > 0 ? Act(() => store.SetWorkers(job, G.State.Assignments[job] - 1)) : null, workers > 0 ? E.Kind.Red : E.Kind.Grey, 40).Margin(0, 0, 0, 8));
            var count = E.Capsule(32).Pad(0, 6);
            count.style.width = 80;
            count.style.justifyContent = Justify.Center;
            count.style.marginLeft = -3; count.style.marginRight = -3;
            count.Add(E.T(unlimited ? $"{workers}" : $"{workers}/{capacity}", Theme.Num).Fit(68));
            row.Add(count);
            bool canAdd = G.IdleWorkers > 0 && workers < capacity;
            row.Add(E.Square("plus", canAdd ? Act(() => store.SetWorkers(job, G.State.Assignments[job] + 1)) : null, canAdd ? E.Kind.Green : E.Kind.Grey, 40));
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
            float cellW = Mathf.Floor((InnerW - 8) / 2);
            const float skillW = 112;
            int shown = 0;
            // The sick first (the gravest on top), then the most skilled.
            foreach (var p in G.State.People.OrderByDescending(x => x.Sick).ThenByDescending(x => x.Sick ? x.Severity : 0).ThenByDescending(SkillSum))
            {
                if (shown++ >= 60) break;
                var cell = E.Inset().Row().Pad(8, 10);
                cell.style.width = cellW;
                cell.style.marginBottom = 8;
                cell.style.minHeight = 64;
                var face = E.Slot(FaceFor(p), 48, E.Cell.Slot, 0.86f);
                if (p.Sick) face.Add(E.Icon("sick", 20).Abs(null, null, -5, -3));
                cell.Add(face);
                var col = E.Div().Col().Grow().Ignore();
                col.style.marginLeft = 10;
                float textW = cellW - 20 - 48 - 10 - skillW - 6;
                col.Add(E.T(p.Name, Theme.Num).Fit(textW));
                var job = p.Job == null ? null : Enums.Jobs.Cast<JobType?>().FirstOrDefault(j => j.Value.Id() == p.Job);
                string doing = job == null ? "Idle" : G.Content.JobConfig(job.Value)?.Name ?? p.Job;
                col.Add(E.T(doing, Theme.Small, job == null ? Theme.Gold : Theme.Pale).Fit(textW));
                if (p.Sick) SickLine(col, p, textW);
                cell.Add(col);
                var best = G.BestSkill(p);
                if (best != null)
                {
                    int lv = G.SkillLevel(p, best.Value);
                    var skill = E.Div().Col(Align.FlexEnd).Ignore().Size(skillW);
                    skill.style.marginLeft = 6;
                    skill.Add(E.T(SkillTitle(best.Value), Theme.Small).Fit(skillW));
                    skill.Add(E.T($"Lv.{lv}", Theme.Num, Theme.Lv));
                    var bar = E.Bar(out var fill, 10, E.FillKind.Gold);
                    E.SetFill(fill, G.SkillProgress(p, best.Value));
                    bar.style.width = 90;
                    bar.style.marginTop = 2;
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
                           Theme.Small).Margin(4, 6, 0, 6));
        }

        /// <summary>One person's skills: level, progress to the next and the perks earned in each job.</summary>
        VisualElement PersonSkills(Person p)
        {
            var box = E.Section($"{p.Name}'s skills", out var rows);
            box.style.marginBottom = 8;
            var jobs = Enums.Jobs.Where(j => Game.Experience(p, j) > 0).OrderByDescending(j => Game.Experience(p, j)).ToList();
            if (jobs.Count == 0) rows.Add(E.Ink("No experience yet.").Margin(6, 0, 6));
            bool sep = false;
            foreach (var job in jobs)
            {
                int lv = G.SkillLevel(p, job);
                var line = E.Line(sep);
                sep = true;
                line.Add(E.RowIcon(JobIcon(job)));
                line.Add(E.T(SkillTitle(job), Theme.Body).Flex());
                var bar = E.Bar(out var fill, 12, E.FillKind.Gold);
                E.SetFill(fill, G.SkillProgress(p, job));
                bar.style.width = 110;
                bar.style.marginRight = 10;
                line.Add(bar);
                line.Add(E.T($"Lv.{lv}/{G.Content.Balance.MaxSkillLevel}", Theme.Num, Theme.Lv));
                rows.Add(line);
                var skill = G.Content.Skill(job);
                if (skill == null) continue;
                var perks = E.Div().Row().Ignore().Margin(0, 0, 6);
                perks.style.flexWrap = Wrap.Wrap;
                foreach (var perk in skill.Perks)
                {
                    bool has = perk.Level <= lv;
                    var chip = E.Capsule(28).Pad(0, 10).Margin(0, 6, 4, 0);
                    chip.Add(E.T($"{perk.Level} · {perk.Name}", Theme.Small, has ? Theme.Gold : Theme.Hex("#B9A898")));
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
            var pct = E.T("", Theme.Num);
            Bind(() => { int a = ApprovalNow; string s = a + "%"; if (pct.text != s) pct.text = s; E.Tint(pct, ApprovalColor(a)); });
            var card = Block("Hope", out var rows, pct);
            var bar = E.Bar(out var fill, 14, E.FillKind.Green);
            Bind(() => { int a = ApprovalNow; E.SetFill(fill, a / 100.0); });
            bar.style.marginTop = 10;
            rows.Add(bar);
            var effect = Live(() =>
            {
                int a = ApprovalNow;
                int work = (int)Math.Round((G.ApprovalProductivity(a) - 1) * 100);
                string growth = a < 30 ? "no newcomers" : $"growth ×{G.ApprovalGrowth(a):0.0}";
                return $"Work {(work >= 0 ? "+" : "")}{work}% · {growth}";
            }, Theme.Body, Theme.Pale);
            effect.style.marginTop = 6;
            effect.style.marginBottom = 6;
            rows.Add(effect);
            rows.Add(E.StatRow("approval", "Base", Game.ApprovalBase.ToString(Inv), null, true, Theme.Pale));
            foreach (var f in G.ApprovalFactors())
                rows.Add(E.StatRow(null, f.Label, (f.Points > 0 ? "+" : f.Points < 0 ? "−" : "") + Math.Abs(f.Points), null, true,
                                   f.Points > 0 ? Theme.Up : f.Points < 0 ? Theme.Bad : Theme.Pale).Also(r => r.style.minHeight = Theme.RowTightH));
            host.Add(card);
            BuildDiscontent(host);
            BuildHealth(host);

            var season = G.Season;
            if (season != null)
            {
                var sc = Block($"{season.Name} · Year {G.Year}", out var srows, Live(() => Fmt.Clock(G.SecondsUntilNextSeason), Theme.Num, Theme.Pale));
                if (!string.IsNullOrEmpty(season.Description)) srows.Add(E.Ink(season.Description, Theme.Body).Margin(6, 0, 4));
                srows.Add(E.StatRow("food", "Foraging", $"×{season.Foraging:0.##}", null, true));
                srows.Add(E.StatRow("wheat", "Farming", season.Farming <= 0 ? "None" : $"×{season.Farming:0.##}", null, true, season.Farming <= 0 ? Theme.Bad : (Color?)null));
                if (season.FuelPerPersonPerSecond > 0) srows.Add(E.StatRow("flame", "Firewood", $"{G.FuelPerSecond * 60:0.#}/m", null, true));
                if (G.Freezing) srows.Add(Notice("alert", "The woodpile is empty and people are freezing. Put more people on Wood Gathering.", true));
                host.Add(sc);
            }

            var up = Block("Upkeep", out var urows);
            var r1 = E.Line(false);
            r1.Add(E.RowIcon("food"));
            r1.Add(E.T("Eating", Theme.Body).Flex());
            r1.Add(Live(() => Fmt.PerMinute(-G.State.Population * G.Content.Balance.FoodPerPersonPerSecond), Theme.Num, Theme.Bad));
            urows.Add(r1);
            var r2 = E.Line(true);
            r2.Add(E.RowIcon("storage"));
            r2.Add(E.T("Spoiling", Theme.Body).Flex());
            r2.Add(Live(() => Fmt.PerMinute(-econ.Spoilage), Theme.Num, Theme.Bad));
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
            // The kit's chapter banner: dark wood, "Chapter N" big, the title under it, the story button on the right.
            var banner = E.Div().Row().Radius(10).Border(Theme.Outline, 2.5f).Pad(10, 12).Margin(0, 0, 8);
            banner.BgImage(Theme.Gradient("banner", false, Theme.Hex("#4A2E1A"), Theme.Hex("#8A5A32")));
            banner.AddToClassList("surf-dark");
            banner.style.minHeight = 92;
            banner.Add(E.Icon("campfire", 60));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 12;
            const float storyW = 120;
            float textW = InnerW - 24 - 4 - 60 - 12 - storyW - 10;
            col.Add(E.T($"Chapter {current.Number} · {current.Era}", Theme.Body, Theme.Gold).Fit(textW));
            col.Add(E.T(current.Title, Theme.H1).Fit(textW, Theme.H2));
            col.Add(E.T(current.Goal, Theme.Small, Theme.Pale).Wrap().Margin(2));
            banner.Add(col);
            banner.Add(E.Button("Story", Act(() => G.State.PendingStory = current.Id), E.Kind.Blue, Theme.ButtonH, "legacy", 16, null, storyW).Margin(0, 0, 0, 10));
            host.Add(banner);

            if (current.Locked) host.Add(Notice("lock", "The next era is still being built. Your settlement keeps growing until it arrives."));
            else
                foreach (var req in G.ChapterRequirements()) host.Add(TaskRow(req.Text, req.Met, () => store.ActivePanel = Panel.Build));

            // The road ahead: every chapter, with the ones still to come.
            var road = Block("The road ahead", out var rows);
            road.style.marginTop = 2;
            bool sep = false;
            foreach (var ch in G.Content.Chapters)
            {
                bool done = ch.Number < current.Number;
                bool now = ch.Number == current.Number;
                var row = E.Line(sep);
                sep = true;
                row.Add(E.RowIcon(done ? "check" : ch.Locked ? "lock" : now ? "flame" : "next"));
                row.Add(E.T($"{ch.Number}. {ch.Title}", Theme.Body, now ? Theme.Gold : done ? (Color?)null : Theme.Pale).Flex());
                row.Add(E.T(ch.Era, Theme.Small, Theme.Pale).NoShrink().Margin(0, 0, 0, 8));
                rows.Add(row);
            }
            host.Add(road);
        }

        /// <summary>A task: title, then ✓ when done or a blue Go.</summary>
        VisualElement TaskRow(string text, bool met, Action go)
        {
            // The kit's task row: the task's title, the "x/y" pill, then ✓ or a big blue Go.
            var r = E.Inset().Row().Margin(0, 0, 8);
            r.style.minHeight = 58;
            r.Add(E.T(text, Theme.Num, met ? Theme.Up : (Color?)null).Flex());
            var pill = E.Capsule(30).Pad(0, 12);
            pill.style.marginLeft = 8;
            pill.Add(E.T(met ? "1/1" : "0/1", Theme.Num, met ? Theme.Up : Color.white));
            r.Add(pill);
            var right = E.Div().Row().Center().Ignore().Size(96);
            right.Add(met ? E.Icon("check", 32) : E.Button("Go", Act(go), E.Kind.Blue, 42, null, 18, null, 86));
            r.Add(right);
            return r;
        }

        void BuildMilestones(VisualElement host)
        {
            foreach (var ms in G.Content.Milestones)
            {
                bool reached = G.State.ReachedMilestones.Contains(ms.Id);
                var head = reached ? E.T("Reached", Theme.Num, Theme.Up) : null;
                var card = Block(ms.Name, out var rows, head);
                if (!string.IsNullOrEmpty(ms.Description)) rows.Add(E.Ink(ms.Description, Theme.Body).Margin(6, 0, 4));
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
                                         can ? E.Kind.Orange : E.Kind.Grey, Theme.ButtonH, can ? "trophy" : null, 16);
                    claim.style.marginTop = 8;
                    claim.style.marginBottom = 8;
                    claim.style.alignSelf = Align.Center;
                    claim.style.minWidth = 220;
                    rows.Add(claim);
                }
                else rows.Add(E.Div().Row().Center().Ignore().Margin(6, 0, 6).Kids(E.Icon("trophy", 40)));
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
                var card = E.Inset().Col().Margin(0, 0, 8);
                if (!done && !isCurrent) card.style.opacity = 0.7f;
                var head = E.Div().Row().Ignore();
                head.style.minHeight = 50;
                head.Add(E.Icon(done ? "check" : isCurrent ? "goals" : "lock", 28));
                head.Add(E.T(obj.Title, Theme.Num, isCurrent ? Theme.Gold : (Color?)null).Flex().Margin(0, 0, 0, 8));
                if (obj.Reward.HasValue && !done)
                    foreach (var kv in obj.Reward.Value.Entries)
                        head.Add(RewardCell(IconFor(kv.Key), CellFor(kv.Key), Fmt.Compact(kv.Value), 46).Margin(0, 0, 0, 8));
                card.Add(head);
                if (isCurrent)
                {
                    if (!string.IsNullOrEmpty(obj.Hint)) card.Add(E.Ink(obj.Hint, Theme.Body).Margin(4));
                    bool sep = true;
                    foreach (var req in G.Status(obj.Conditions)) card.Add(RequirementRow("next", req.Text, req.Met, null, sep, WindowW));
                }
                host.Add(card);
            }
        }

        // MARK: - World & Legacy

        void BuildWorldPanel(VisualElement host)
        {
            var info = E.Inset().Col().Margin(0, 0, 8);
            var head = E.Div().Row().Ignore();
            head.Add(E.Icon("world", 30));
            head.Add(E.T(G.State.SettlementName, Theme.H2).Flex().Margin(0, 0, 0, 8));
            head.Add(E.T(G.Content.Map.Name ?? "", Theme.Body, Theme.Pale).NoShrink().Margin(0, 0, 0, 8));
            info.Add(head);
            var traits = E.Div().Row().Ignore().Margin(6);
            traits.style.flexWrap = Wrap.Wrap;
            foreach (var t in G.Content.Map.Traits)
            {
                var chip = E.Capsule(28).Pad(0, 10).Margin(0, 6, 4, 0);
                chip.Add(E.T(t, Theme.Small));
                traits.Add(chip);
            }
            info.Add(traits);
            host.Add(info);

            var active = G.State.ActiveExpedition;
            foreach (var region in G.Content.Regions.Where(r => !r.RevealedAtStart))
            {
                bool explored = G.State.ExploredRegions.Contains(region.Id);
                var card = Block(region.Name, out var rows, explored ? E.T("Explored", Theme.Num, Theme.Up) : null);
                rows.Add(E.Ink(region.Description, Theme.Body).Margin(6, 0, 4));
                var finds = RegionFinds(region, explored);
                if (finds != null) rows.Add(finds);
                if (active != null && active.RegionId == region.Id)
                {
                    var r = E.Line(true);
                    r.Add(E.RowIcon("compass"));
                    r.Add(E.T("Scouts away", Theme.Body).Flex());
                    r.Add(Live(() => Fmt.Clock(G.State.ActiveExpedition?.RemainingSeconds ?? 0), Theme.Num));
                    rows.Add(r);
                    rows.Add(LiveBar(() => { var e = G.State.ActiveExpedition; return e == null ? 1 : 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds); }, 14).Margin(0, 0, 8));
                }
                else if (!explored)
                {
                    var bottom = E.Line(true, 58);
                    bottom.Add(CostLine(region.Cost).Grow());
                    bool can = active == null && G.State.Resources.Covers(region.Cost);
                    string id = region.Id;
                    bottom.Add(E.Button(active != null ? "Scouts busy" : "Send scouts", can ? Act(() => store.Explore(id)) : null,
                                        can ? E.Kind.Green : E.Kind.Grey, Theme.ButtonH, null, 16, SubLine("clock", Fmt.Clock(region.ExpeditionSeconds)), 170));
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
            var rail = E.Div().Col(Align.Center).Size(30).Ignore();
            rail.Add(E.Icon("sparkle", 24).Margin(4));
            if (!last)
            {
                var l = E.Div().Ignore().Bg(Theme.Wood4);
                l.style.width = 3;
                l.style.flexGrow = 1;
                l.style.marginTop = 2;
                rail.Add(l);
            }
            row.Add(rail);
            var body = E.Inset().Col().Grow().Margin(0, 0, 8, 8);
            body.style.minHeight = 48;
            body.style.justifyContent = Justify.Center;
            var head = E.Div().Row().Ignore();
            head.Add(E.T(entry.Title, Theme.Num).Flex());
            head.Add(E.T(Fmt.Clock(entry.SimTime), Theme.Small, Theme.Pale).NoShrink().Margin(0, 0, 0, 8));
            body.Add(head);
            if (!string.IsNullOrEmpty(entry.Detail)) body.Add(E.Ink(entry.Detail, Theme.Body).Margin(3));
            row.Add(body);
            return row;
        }

        // MARK: - Founder (profile)

        void BuildProfilePanel(VisualElement host)
        {
            var hero = E.Inset().Row().Margin(0, 0, 8);
            hero.Add(E.Slot(G.State.EpochId == "epoch_medieval_kingdoms" ? "landmark" : "home", 68, E.Cell.Tile, 0.7f));
            var col = E.Div().Col().Grow().Ignore();
            col.style.marginLeft = 12;
            float idW = Theme.TextWidth("ID " + FounderId(), Theme.Body) + 22;
            float nameW = InnerW - 20 - 68 - 12 - idW - 10;
            col.Add(E.T(G.State.SettlementName, Theme.H1).Fit(nameW, Theme.H2));
            col.Add(E.T($"{G.StageName} · Era {Fmt.Roman(G.Content.Epoch(G.State.EpochId)?.Index ?? 1)}", Theme.Body, Theme.Pale).Fit(nameW));
            col.Add(E.Ink(G.EpochName, Theme.Small));
            hero.Add(col);
            var id = E.Capsule(32).Margin(0, 0, 0, 10);
            id.Add(E.T("ID " + FounderId(), Theme.Body));
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
            float w = Mathf.Floor((InnerW - 16) / 3);
            foreach (var s in stats)
            {
                var c = StatBox(s.icon, s.label, s.value);
                c.style.flexGrow = 0;
                c.style.width = w;
                c.style.marginBottom = 8;
                grid.Add(c);
            }
            host.Add(grid);

            var ms = Block("Milestones", out var mrows);
            var cells = E.Div().Row().Ignore().Margin(8, 0, 6);
            cells.style.flexWrap = Wrap.Wrap;
            foreach (var m in G.Content.Milestones)
            {
                bool got = G.State.ReachedMilestones.Contains(m.Id);
                var c = E.Div().Col(Align.Center).Ignore().Size(112);
                c.style.marginBottom = 6;
                c.Add(E.Slot(got ? "trophy" : "lock", 56, got ? E.Cell.Legendary : E.Cell.Common, 0.62f, !got));
                c.Add(E.T(m.Name, Theme.Body, got ? (Color?)null : Theme.Pale).TwoLines(108).Margin(3));
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
            var seg = E.Div().Row(0, Align.FlexEnd).Ignore().Margin(8, 0, 8);
            string[] names = { "Smooth", "Balanced", "Cinematic" };
            for (int i = 0; i < names.Length; i++)
            {
                int q = i;
                var t = E.Tab(names[i], Prefs.Quality == i, Act(() => { Prefs.Quality = q; Prefs.Apply(); }));
                t.style.flexGrow = 1;
                t.style.flexBasis = 0;
                if (i > 0) t.style.marginLeft = 6;
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
            var r1 = E.Div().Row().Ignore().Margin(8);
            float devW = (InnerW - 20 - 3 * 8) / 4;
            r1.Add(Share(E.Button("Save now", Act(store.Save), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
            r1.Add(Share(E.Button("+50 all", Act(store.DebugGrant), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
            r1.Add(Share(E.Button("+1 min", Act(() => store.DebugFastForward(1)), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
            r1.Add(Share(E.Button("+10 min", Act(() => store.DebugFastForward(10)), E.Kind.Blue, 42, null, Theme.Num, null, devW), r1));
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
            }), E.Kind.Red, 42, "trash", Theme.Num, null, InnerW - 20);
            reset.style.marginTop = 8;
            reset.style.marginBottom = 8;
            drows.Add(reset);
            host.Add(dev);

            host.Add(E.Ink($"{G.Content.Branding.Title} · {G.Content.Branding.Subtitle} · v{Application.version}\nContent {G.Content.ContentVersion} · Save format {SaveCodec.CurrentVersion} · Unity {Application.unityVersion}",
                           Theme.Small, Theme.InkSoft).Align(TextAnchor.MiddleCenter).Margin(4));
        }

        /// <summary>A setting with the kit's ON/OFF switch.</summary>
        VisualElement ToggleRow(string label, string hint, bool value, Action<bool> set, bool separator)
        {
            var row = E.Line(separator, 56);
            var col = E.Div().Col().Grow().Ignore();
            col.Add(E.T(label, Theme.Num));
            col.Add(E.T(hint, Theme.Small, Theme.Pale).Fit(InnerW - 20 - 90));
            row.Add(col);
            var sw = E.Div().Row().Skin(value ? "btn_green" : "btn_grey").Pad(0, 5, 4, 7).Size(80, 36).Ignore();
            var knob = E.Div().Skin("slot").Size(24, 24).Ignore();
            var text = E.T(value ? "ON" : "OFF", Theme.Body).Grow().Align(TextAnchor.MiddleCenter);
            if (value) { sw.Add(text); sw.Add(knob); }
            else { sw.Add(knob); sw.Add(text); }
            row.Add(sw);
            Tap(row, () => set(!value));
            return row;
        }
    }
}
