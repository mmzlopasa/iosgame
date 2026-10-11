using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Founders.Core;
using Founders.UI;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Automated screenshots for development (used by CI in the iOS Simulator). Enabled when
    /// <c>capture.flag</c> exists in the persistent data folder or with the <c>-capture</c>
    /// argument. Starts a fresh camp (the player's save is not touched), advances it with the
    /// real game rules, captures a series of views into <c>captures/</c>, then quits.
    /// </summary>
    public class CaptureDirector : MonoBehaviour
    {
        GameStore store;
        GameUi ui;
        string outDir;

        public static bool Requested =>
            File.Exists(Path.Combine(Application.persistentDataPath, "capture.flag")) ||
            System.Environment.GetCommandLineArgs().Contains("-capture");

        public void Begin(GameStore store, GameUi ui)
        {
            this.store = store;
            this.ui = ui;
            outDir = Path.Combine(Application.persistentDataPath, "captures");
            Directory.CreateDirectory(outDir);
            StartCoroutine(Run());
        }

        IEnumerator Shot(string name, bool ui)
        {
            this.ui.Hidden = !ui;
            for (int i = 0; i < 20; i++) yield return null; // let camera moves, particles and LOD settle
            yield return new WaitForSeconds(0.8f);
            // On iOS/Android the path is relative to the persistent data folder.
            bool mobile = Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android;
            ScreenCapture.CaptureScreenshot(mobile ? "captures/" + name + ".png" : Path.Combine(outDir, name + ".png"));
            yield return null;
            yield return new WaitForSeconds(0.6f);
            Debug.Log($"[FOUNDERS] captured {name}");
        }

        void Focus(Vector3 p, float zoom) => store.Rig.SetFocus(store.World.Ground.OnGround(p), true, zoom);

        Vector3 Camp()
        {
            var first = store.Game.State.Buildings.FirstOrDefault();
            return first == null ? Vector3.zero : Grid.CellCenter(first.X, first.Y);
        }

        (int, int)? FindCell(string id)
        {
            var game = store.Game;
            var c = game.State.Buildings.First();
            (int, int)? best = null;
            int bestD = int.MaxValue;
            for (int y = 0; y < game.Content.Map.Height; y++)
                for (int x = 0; x < game.Content.Map.Width; x++)
                {
                    int d = Mathf.Abs(x - c.X) + Mathf.Abs(y - c.Y);
                    if (d >= bestD || d < 2) continue;
                    if (game.PlacementIssues(id, x, y).All(i => i.Kind == PlacementIssueKind.Unaffordable)) { best = (x, y); bestD = d; }
                }
            return best;
        }

        void Build(string id)
        {
            var game = store.Game;
            game.DebugGrant(new ResourceBundle(120, 120, 120, 50));
            var cell = FindCell(id);
            if (cell == null) { Debug.LogWarning($"[FOUNDERS] capture: no cell for {id}"); return; }
            var r = game.PlaceBuilding(id, cell.Value.Item1, cell.Value.Item2);
            if (!r.Ok) Debug.LogWarning($"[FOUNDERS] capture: {id} {r.Error}");
            game.DebugFastForward(70);
        }

        void Decorate(string id, int x, int y)
        {
            var game = store.Game;
            game.DebugGrant(new ResourceBundle(40, 40, 40, 0));
            var r = game.PlaceDecoration(id, x, y);
            if (!r.Ok) Debug.LogWarning($"[FOUNDERS] capture: {id} at {x},{y}: {r.Error}");
        }

        /// <summary>Lays out roads, fences, lights and monuments around the camp (the town-builder tools).</summary>
        void LayOutTown()
        {
            var game = store.Game;
            var c = game.State.Buildings.First();
            bool Free(int x, int y) => game.DecorationIssues("bush", x, y).All(i => i.Kind == PlacementIssueKind.Unaffordable);
            for (int x = c.X - 7; x <= c.X + 7; x++)
                if (Free(x, c.Y + 3)) Decorate("dirt_path", x, c.Y + 3);
            for (int y = c.Y - 5; y <= c.Y + 9; y++)
                if (y != c.Y + 3 && Free(c.X + 1, y)) Decorate("stone_road", c.X + 1, y);
            for (int x = c.X + 3; x <= c.X + 6; x++)
                if (Free(x, c.Y + 5)) Decorate("wattle_fence", x, c.Y + 5);
            for (int x = c.X - 6; x <= c.X - 3; x++)
                if (Free(x, c.Y + 5)) Decorate("palisade", x, c.Y + 5);
            var extras = new[] { "torch_post", "oil_lantern", "torch_post", "oil_lantern", "well", "totem", "standing_stone",
                                 "bush", "young_tree", "flower_bed", "bush", "young_tree", "flower_bed", "stone_wall", "stone_wall" };
            int k = 0;
            for (int r = 1; r < 9 && k < extras.Length; r++)
                for (int y = c.Y - r; y <= c.Y + r && k < extras.Length; y++)
                    for (int x = c.X - r; x <= c.X + r && k < extras.Length; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x - c.X), Mathf.Abs(y - c.Y)) != r || (x + y) % 3 != 0 || !Free(x, y)) continue;
                        Decorate(extras[k++], x, y);
                    }
            store.Refresh();
        }

        /// <summary>Jumps game time to the middle of a season at a given point of the day (capture only).</summary>
        void SetTime(Game game, int season, float dayPhase)
        {
            double seasonLen = game.Content.Balance.SeasonSeconds;
            double year = seasonLen * 4;
            double start = System.Math.Floor(game.State.SimTime / year) * year + season * seasonLen + seasonLen * 0.35;
            double t = System.Math.Floor(start / Atmosphere.DayLength) * Atmosphere.DayLength + dayPhase * Atmosphere.DayLength;
            if (t < start) t += Atmosphere.DayLength;
            game.State.SimTime = t;
            store.Refresh();
        }

        IEnumerator ShowPanel(string name, Panel panel)
        {
            store.ActivePanel = panel;
            yield return Shot(name, true);
            store.ActivePanel = Founders.Panel.None;
        }

        IEnumerator Run()
        {
            var game = store.Game;
            yield return new WaitForSeconds(2f);

            // 0. The loading screen.
            ui.Loading.Show();
            ui.Loading.Set(0.64f, "Kindling the hearth…");
            yield return Shot("00_loading", true);
            ui.Loading.Hide();
            yield return new WaitForSeconds(0.6f);

            // 1. The starting camp as a new player sees it.
            Focus(Camp(), 26f);
            yield return Shot("01_start_camp", true);
            Focus(Camp(), 12f);
            yield return Shot("02_campfire_close", false);

            // 2. Grow the camp with the real rules.
            foreach (var id in new[] { "foraging_camp", "wood_camp", "stone_site", "storage_pit", "shelter", "toolmaker", "community_circle", "shelter" })
            {
                Build(id);
                store.Refresh();
                yield return null;
            }
            game.SetWorkers(JobType.Research, 1);
            bool eventShot = false;
            foreach (var tech in new[] { "improved_stone_tools", "food_preservation", "structural_weaving", "shared_labor", "permanent_settlement",
                                          "fishing", "hide_working", "oral_tradition", "longhouses" })
            {
                game.DebugGrant(new ResourceBundle(0, 0, 0, 60));
                game.StartResearch(tech);
                game.DebugFastForward(80);
                var ev = game.PendingEvent;
                if (ev != null)
                {
                    if (!eventShot)
                    {
                        eventShot = true;
                        store.Refresh();
                        store.EventMinimized = false;
                        Focus(Camp(), 22f);
                        yield return Shot("03_event", true);
                    }
                    var option = ev.Options.FirstOrDefault(o => game.OptionBlocker(o) == null);
                    if (option != null) game.ResolveEvent(option.Id);
                }
            }
            store.Refresh();
            Focus(Camp(), 24f);
            yield return Shot("04_camp_grown", false);

            // 3. Village milestone (population grows over time).
            for (int i = 0; i < 12 && !game.CanClaimMilestone("stable_village"); i++)
            {
                game.DebugGrant(new ResourceBundle(120, 0, 0, 0));
                game.DebugFastForward(120);
            }
            if (game.CanClaimMilestone("stable_village")) game.ClaimMilestone("stable_village");
            store.Refresh();
            Focus(Camp(), 22f);
            yield return Shot("05_milestone", true);
            store.Celebration = null;
            store.Toasts.Clear();
            // The new buildings unlocked by the later research.
            foreach (var id in new[] { "longhouse", "elders_lodge", "drying_rack", "fishing_weir" })
            {
                Build(id);
                store.Refresh();
                yield return null;
            }
            store.Refresh();
            yield return Shot("06_village", false);

            // 3b. Era II: farming, pottery and bronze.
            foreach (var tech in new[] { "agriculture", "animal_husbandry", "pottery", "copper_smelting", "bronze_working" })
            {
                game.DebugGrant(new ResourceBundle(0, 0, 0, 130));
                game.StartResearch(tech);
                game.DebugFastForward(125);
                var ev = game.PendingEvent;
                if (ev != null)
                {
                    var option = ev.Options.FirstOrDefault(o => game.OptionBlocker(o) == null);
                    if (option != null) game.ResolveEvent(option.Id);
                }
            }
            foreach (var id in new[] { "farm_field", "farm_field", "granary", "pottery_kiln", "animal_pen", "copper_smelter", "bronze_workshop" })
            {
                Build(id);
                store.Refresh();
                yield return null;
            }
            var woven = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "shelter");
            if (woven != null) { woven.DefinitionId = "daub_house"; woven.History.Add("daub_house"); }
            game.State.Population = Mathf.Max(game.State.Population, 20);
            var pending = game.PendingEvent;
            if (pending != null) game.ResolveEvent(pending.Options.First(o => game.OptionBlocker(o) == null).Id);
            if (game.CanClaimMilestone("first_civilization")) game.ClaimMilestone("first_civilization");
            else Debug.LogWarning("[FOUNDERS] capture: first_civilization not claimable: " +
                                  string.Join(", ", game.MilestoneRequirements("first_civilization").Where(r => !r.Met).Select(r => r.Text)));
            store.World.RebuildAll();
            store.Refresh();
            Focus(Camp(), 26f);
            yield return Shot("06b_bronze_dawn", true);
            store.Celebration = null;
            store.Toasts.Clear();
            yield return Shot("06c_era2_village", false);

            // 3b'. The town builder: roads, fences, lights, monuments; the valley's new workplaces.
            LayOutTown();
            foreach (var region in game.Content.Regions)
                if (!game.State.ExploredRegions.Contains(region.Id)) game.State.ExploredRegions.Add(region.Id);
            if (!game.State.Researched.Contains("improved_stone_tools")) game.State.Researched.Add("improved_stone_tools");
            foreach (var id in new[] { "mine", "hunters_lodge", "fishing_weir" })
            {
                Build(id);
                store.Refresh();
                yield return null;
            }
            store.World.RebuildAll();
            store.Refresh();
            Focus(Camp() + new Vector3(2f, 0, -5f), 20f);
            yield return Shot("06g_town_layout", false);

            // 3c. Day, night and the seasons.
            SetTime(game, 2, 0.3f);   // autumn, morning
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06d_autumn", false);
            SetTime(game, 3, 0.35f);  // winter, day
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06e_winter", true);
            SetTime(game, 1, 0.88f);  // summer, night
            yield return new WaitForSeconds(1.5f);
            yield return Shot("06f_night", false);
            SetTime(game, 1, 0.3f);
            yield return new WaitForSeconds(1f);

            // 4. Landscape views: the river and the forest edge.
            var map = game.Content.Map;
            Focus(Grid.CellCenter(9.5f, 9f), 16f);
            yield return Shot("07_river", false);
            Focus(Grid.CellCenter(3f, 6f), 14f);
            yield return Shot("08_forest_edge", false);
            Focus(Grid.CellCenter(15f, 33f), 40f);
            yield return Shot("07b_lake", false);
            Focus(Grid.CellCenter(50f, 22f), 50f);
            yield return Shot("07c_mountains", false);
            Focus(Grid.CellCenter(31f, 13f), 34f);
            yield return Shot("07d_stone_hills", false);
            var mine = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "mine");
            if (mine != null)
            {
                Focus(Grid.CellCenter(mine.X, mine.Y), 13f);
                yield return Shot("07e_mine", false);
            }
            Focus(Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f), 80f);
            yield return Shot("09_overview", true);

            // 5. UI screens.
            Focus(Camp(), 24f);
            store.EventMinimized = true;
            yield return ShowPanel("10_build_panel", Founders.Panel.Build);
            yield return ShowPanel("11_people", Founders.Panel.People);
            yield return ShowPanel("12_research", Founders.Panel.Research);
            yield return ShowPanel("13_goals", Founders.Panel.Objectives);
            yield return ShowPanel("14_world", Founders.Panel.Map);
            yield return ShowPanel("15_profile", Founders.Panel.Profile);
            yield return ShowPanel("16_settings", Founders.Panel.Settings);

            var hut = game.State.Buildings.FirstOrDefault(b => game.Content.Building(b.DefinitionId)?.UpgradesTo != null) ?? game.State.Buildings.First();
            store.FocusBuilding(hut.Id);
            yield return Shot("17_building", true);
            store.SelectedBuildingId = null;

            var report = new OfflineReport { Seconds = 5 * 3600 + 24 * 60, Gained = new ResourceBundle(226, 1944, 291, 1166) };
            report.Events.Add("Hide Shelter finished");
            report.Events.Add("Research complete: Improved Stone Tools");
            report.Events.Add("2 newcomers joined the camp");
            store.Offline = report;
            yield return Shot("18_welcome_back", true);
            store.Offline = null;

            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            Debug.Log("[FOUNDERS] capture finished");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
