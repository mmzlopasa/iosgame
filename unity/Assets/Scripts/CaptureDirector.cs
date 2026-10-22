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
    ///
    /// The tour runs in landscape (the screen is turned with <see cref="Screen.orientation"/>, as the CI
    /// simulator device stays upright), then turns the screen to portrait for a second set of the UI
    /// screens, saved with a <c>p_</c> prefix. <c>captures/orientation.txt</c> says which way the screen
    /// is turned, so the workflow can name and rotate its own simulator screenshots to match.
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
            StartCoroutine(Tour());
        }

        IEnumerator Tour()
        {
            yield return Turn(false);
            yield return Run();
            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            Debug.Log("[FOUNDERS] capture finished");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>Turns the screen to portrait or landscape and waits until it has turned (or ten seconds have passed).</summary>
        IEnumerator Turn(bool portrait)
        {
            File.WriteAllText(Path.Combine(outDir, "orientation.txt"), portrait ? "portrait" : "landscape");
            Screen.orientation = portrait ? ScreenOrientation.Portrait : ScreenOrientation.LandscapeLeft;
            float until = Time.realtimeSinceStartup + 10f;
            while ((Screen.height > Screen.width) != portrait && Time.realtimeSinceStartup < until) yield return null;
            // Let the panel re-scale and the HUD rebuild for the new shape.
            for (int i = 0; i < 10; i++) yield return null;
            yield return new WaitForSeconds(1f);
            bool ok = (Screen.height > Screen.width) == portrait;
            Debug.Log($"[FOUNDERS] capture: turned {(portrait ? "portrait" : "landscape")} {(ok ? "" : "(the screen did not turn) ")}{Screen.width}x{Screen.height}");
        }

        /// <summary>The UI screens again in portrait (names start with p_).</summary>
        IEnumerator PortraitShots(Game game)
        {
            ResolvePending();
            store.ActivePanel = Founders.Panel.None;
            store.SelectedBuildingId = null;
            store.SelectedNode = null;
            store.EventMinimized = false;
            SetTime(game, 1, 0.3f);
            yield return new WaitForSeconds(1f);

            // The loading screen (the montage fills the tall screen).
            ui.Loading.Show();
            ui.Loading.Set(0.64f, "Kindling the hearth…");
            for (int i = 0; i < 100 && !ui.Loading.VideoPlaying; i++) yield return new WaitForSeconds(0.1f);
            yield return new WaitForSeconds(1f);
            yield return Shot("p_00_loading", true);
            ui.Loading.Hide();
            yield return new WaitForSeconds(0.6f);

            // The HUD with a notice.
            Focus(Camp(), 24f);
            store.Toasts.Add(new Toast { Notice = new GameNotice(NoticeKind.Success, "A newcomer joined the camp"), CreatedAt = Time.unscaledTime });
            yield return Shot("p_01_hud", true);
            store.Toasts.Clear();

            // Cards (bottom sheets): the heart, a building, a resource, placing and the road tool.
            store.FocusBuilding(game.Heart.Id);
            yield return Shot("p_02_heart_card", true);
            var hut = game.State.Buildings.FirstOrDefault(b => game.Content.Building(b.DefinitionId)?.UpgradesTo != null) ?? game.State.Buildings.First();
            store.FocusBuilding(hut.Id);
            yield return Shot("p_03_building", true);
            var camp = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "wood_camp");
            if (camp != null)
            {
                store.FocusBuilding(camp.Id);
                yield return Shot("p_03b_workplace", true);
            }
            store.SelectedBuildingId = null;
            var stand = Enumerable.Range(0, 400).Select(i => (x: i % 20, y: i / 20))
                .FirstOrDefault(c => game.NodeAt(c.x, c.y)?.Terrain == TerrainType.Forest && game.IsRevealed(c.x, c.y));
            store.SelectedNode = (stand.x, stand.y);
            Focus(Grid.CellCenter(stand.x, stand.y), 16f);
            yield return Shot("p_04_node", true);
            store.SelectedNode = null;
            game.DebugGrant(new ResourceBundle(100, 100, 100, 0));
            store.BeginPlacement("wood_camp");
            yield return Shot("p_05_placing", true);
            store.CancelPlacement();
            store.BeginDecorating("dirt_path");
            yield return Shot("p_06_road_tool", true);
            store.EndDecorating();

            // The build sheet and the windows.
            Focus(Camp(), 24f);
            yield return ShowPanel("p_07_build", Founders.Panel.Build);
            ui.SelectTab("Roads");
            yield return ShowPanel("p_07b_build_roads", Founders.Panel.Build);
            yield return ShowPanel("p_08_people", Founders.Panel.People);
            ui.ShowPeople();
            yield return Shot("p_08b_people_wellbeing", true);
            store.ActivePanel = Founders.Panel.None;
            yield return ShowPanel("p_09_research", Founders.Panel.Research);
            yield return ShowPanel("p_10_goals", Founders.Panel.Objectives);
            yield return ShowPanel("p_11_world", Founders.Panel.Map);
            yield return ShowPanel("p_12_profile", Founders.Panel.Profile);
            yield return ShowPanel("p_13_settings", Founders.Panel.Settings);
            ui.ShowWeather();
            yield return Shot("p_14_weather", true);
            store.ActivePanel = Founders.Panel.None;
            game.State.LastLawSimTime = null;
            ui.ShowCustoms("long_days");
            yield return Shot("p_15_customs", true);
            store.ActivePanel = Founders.Panel.None;

            // Dialogs: an event, a petition, the story, a milestone and the welcome back.
            var ev = game.Content.Events.FirstOrDefault();
            if (ev != null)
            {
                game.State.PendingEventId = ev.Id;
                store.EventMinimized = false;
                store.Refresh();
                yield return Shot("p_16_event", true);
                game.State.PendingEventId = null;
                store.Refresh();
            }
            ui.ShowPetition("warm_homes");
            yield return Shot("p_17_petition", true);
            game.State.PendingPetition = null;
            var chapter = game.CurrentChapter;
            if (chapter != null)
            {
                game.State.PendingStory = chapter.Id;
                store.Refresh();
                yield return Shot("p_18_story", true);
                game.DismissStory();
            }
            store.Celebration = "The Stable Village: the camp has become a village.";
            yield return Shot("p_19_milestone", true);
            store.Celebration = null;
            var report = new OfflineReport { Seconds = 5 * 3600 + 24 * 60, Gained = new ResourceBundle(226, 1944, 291, 1166) };
            report.Events.Add("Hide Shelter finished");
            report.Events.Add("2 newcomers joined the camp");
            store.Offline = report;
            yield return Shot("p_20_welcome_back", true);
            store.Offline = null;
            store.Refresh();
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

        /// <summary>
        /// A close view of one villager, nearer than the strategy camera goes: the rig pauses and the
        /// camera follows the person at <paramref name="offset"/> (their own space: x right, z ahead),
        /// looking at <paramref name="heightFrac"/> of their height, <paramref name="ahead"/> metres in front.
        /// </summary>
        IEnumerator CloseUp(string name, Villager v, Vector3 offset, float heightFrac, float ahead)
        {
            if (v == null)
            {
                Debug.LogWarning($"[FOUNDERS] capture: nobody for {name}");
                yield break;
            }
            var rig = store.Rig;
            var cam = rig.Cam;
            float height = 1.7f;
            var body = v.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body != null) height = Mathf.Max(0.5f, body.bounds.max.y - v.transform.position.y);
            rig.enabled = false;
            NatureField.KeepNearCamera = true;
            cam.ResetProjectionMatrix();
            bool following = true;
            IEnumerator Follow()
            {
                while (following && v != null)
                {
                    var t = v.transform;
                    var look = t.position + Vector3.up * (height * heightFrac) + t.forward * ahead;
                    cam.transform.position = look + t.rotation * offset;
                    cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position);
                    yield return null;
                }
            }
            var follow = StartCoroutine(Follow());
            yield return Shot(name, false);
            following = false;
            StopCoroutine(follow);
            NatureField.KeepNearCamera = false;
            rig.enabled = true;
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

        /// <summary>Places a costly building with the stores filled past their caps and finishes it (capture only).</summary>
        void BuildRich(string id)
        {
            var game = store.Game;
            game.State.Resources = new ResourceBundle(600, 800, 900, 200, 120, 40);
            var cell = FindCell(id);
            if (cell == null) { Debug.LogWarning($"[FOUNDERS] capture: no cell for {id}"); return; }
            var r = game.PlaceBuilding(id, cell.Value.Item1, cell.Value.Item2);
            if (!r.Ok) { Debug.LogWarning($"[FOUNDERS] capture: {id} {r.Error}"); return; }
            for (int i = 0; i < 6 && game.State.Building(r.Value)?.Status != BuildingStatus.Complete; i++) game.DebugFastForward(120);
        }

        /// <summary>Raises the heart (campfire/hearth) to a level at once (capture only: the real climb takes days).</summary>
        void Heart(int level)
        {
            var heart = store.Game.Heart;
            if (heart != null && heart.Level < level) heart.Level = level;
        }

        /// <summary>Researches a technology through the real rules, lifting the heart if the tech waits for it.</summary>
        void Research(string id)
        {
            var game = store.Game;
            var tech = game.Content.Technology(id);
            if (tech == null || game.State.Researched.Contains(id)) return;
            if (tech.HeartLevel.HasValue) Heart(tech.HeartLevel.Value);
            game.DebugGrant(tech.Cost);
            var r = game.StartResearch(id);
            if (!r.Ok)
            {
                Debug.LogWarning($"[FOUNDERS] capture: research {id}: {r.Error}");
                game.State.Researched.Add(id);
                return;
            }
            game.DebugFastForward(tech.ResearchSeconds + 1);
        }

        /// <summary>Average frames per second over a few seconds (logged; the simulator is far slower than a phone).</summary>
        IEnumerator MeasureFps(string label)
        {
            int frames = 0;
            float start = Time.realtimeSinceStartup;
            float worst = 0f;
            while (Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
                frames++;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            }
            float t = Time.realtimeSinceStartup - start;
            Debug.Log($"[FOUNDERS] fps {label}: {frames / t:0.0} avg, worst frame {worst * 1000f:0} ms");
        }

        /// <summary>Answers any waiting decision so dialogs do not cover the scenery shots.</summary>
        void ResolvePending()
        {
            var game = store.Game;
            for (int i = 0; i < 4 && game.PendingEvent != null; i++)
            {
                var option = game.PendingEvent.Options.FirstOrDefault(o => game.OptionBlocker(o) == null);
                if (option == null) break;
                game.ResolveEvent(option.Id);
            }
            game.DismissStory();
            if (game.PendingPetition != null) game.AnswerPetition(true);
            store.Celebration = null;
            store.Refresh();
        }

        /// <summary>Moves game time on to the start of a year (capture only: the story gates need years).</summary>
        static void JumpToYear(Game game, int year)
        {
            double start = (year - 1) * 4 * game.Content.Balance.SeasonSeconds + 1;
            if (game.State.SimTime < start) game.State.SimTime = start;
        }

        /// <summary>Jumps game time to the middle of a season at a given point of the day (capture only).</summary>
        void SetTime(Game game, int season, float dayPhase)
        {
            double seasonLen = game.Content.Balance.SeasonSeconds;
            double year = seasonLen * 4;
            double start = System.Math.Floor(game.State.SimTime / year) * year + season * seasonLen;
            // A day about a third into the season (with one-day seasons: the season's own day).
            double day = System.Math.Floor(seasonLen * 0.35 / Atmosphere.DayLength) * Atmosphere.DayLength;
            game.State.SimTime = start + day + dayPhase * Atmosphere.DayLength;
            store.Refresh();
        }

        IEnumerator ShowPanel(string name, Panel panel)
        {
            store.ActivePanel = panel;
            yield return Shot(name, true);
            store.ActivePanel = Founders.Panel.None;
        }

        /// <summary>A free cell near the heart where a decoration may stand.</summary>
        (int x, int y)? DecorationCell(string id, int minRing = 2)
        {
            var game = store.Game;
            var heart = game.Heart;
            if (heart == null) return null;
            for (int r = minRing; r < 9; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = heart.X + dx, y = heart.Y + dy;
                        if (game.DecorationIssues(id, x, y).Count == 0) return (x, y);
                    }
            return null;
        }

        IEnumerator SurvivalShots(Game game)
        {
            Research("herbal_healing");
            BuildRich("healers_hut");
            var hut = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "healers_hut" && b.Status == BuildingStatus.Complete);
            game.DebugGrant(new ResourceBundle(0, 60, 40, 0));
            for (int i = 0; i < 3; i++)
            {
                var cell = DecorationCell("fire_brazier", 3 + i);
                if (cell != null) game.PlaceDecoration("fire_brazier", cell.Value.x, cell.Value.y);
            }
            if (hut != null)
            {
                if (game.IdleWorkers == 0)
                {
                    var donor = Enums.Jobs.Where(j => j != JobType.Construction && game.State.Assignments[j] > 1).OrderByDescending(j => game.State.Assignments[j]).FirstOrDefault();
                    game.SetWorkers(donor, game.State.Assignments[donor] - 1);
                }
                game.SetWorkers(JobType.Healing, 1);
            }
            // Two people fall ill: one under the healer's care, one left at home.
            foreach (var p in game.State.People.Where(p => p.Job == null || p.Job == "foraging").Take(2)) p.Sick = true;
            SetTime(game, 3, 0.8f); // winter, dusk
            store.Refresh();
            store.World.ShowHeat = true;
            Focus(Camp(), 15f);
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[FOUNDERS] capture: {game.Temperature:0.0}°C, cold {game.Warmth().Cold}, sick {game.SickCount}, heart {game.HeartFire()}");
            yield return Shot("06u_heat_map", true);
            ui.ShowWeather();
            yield return Shot("06x_weather", true);
            store.ActivePanel = Founders.Panel.None;
            ui.ShowPeople();
            yield return Shot("06x2_people_health", true);
            store.ActivePanel = Founders.Panel.None;
            Heart(12);
            game.State.LastLawSimTime = null;
            game.SignLaw("day_of_rest");
            game.State.LastLawSimTime = null;
            ui.ShowCustoms("long_days");
            yield return Shot("06y_customs", true);
            ui.ShowCustoms("long_days", true);
            yield return Shot("06y2_customs_sign", true);
            store.ActivePanel = Founders.Panel.None;
            game.State.Discontent = 58;
            ui.ShowPetition("warm_homes");
            yield return Shot("06z_petition", true);
            game.AnswerPetition(true);
            store.Refresh();
            store.World.ShowHeat = false;
            game.DebugGrant(new ResourceBundle(0, 400, 0, 0));
            var stoke = game.StokeHeart();
            if (!stoke.Ok) Debug.LogWarning($"[FOUNDERS] capture: stoke {stoke.Error}");
            store.Refresh();
            Focus(Grid.CellCenter(game.Heart.X, game.Heart.Y), 13f);
            yield return Shot("06v_stoked_heart", false);
            ui.ShowHeart();
            yield return Shot("06v2_heart_card", true);
            store.SelectedBuildingId = null;
            store.Refresh();
            yield return new WaitForSeconds(0.5f); // the UI turns the heat map off with the selection
            store.World.ShowHeat = false;
            if (hut != null)
            {
                Focus(Grid.CellCenter(hut.X, hut.Y), 11f);
                yield return Shot("06w_healers_hut", false);
            }
            foreach (var p in game.State.People) p.Sick = false;
            game.State.StokedUntilSimTime = 0;
            game.State.StokeReadySimTime = 0;
            game.State.Modifiers.RemoveAll(m => m.SourceId == "stoke");
            store.Refresh();
        }

        IEnumerator Run()
        {
            var game = store.Game;
            yield return new WaitForSeconds(2f);

            // 0. The loading screen.
            ui.Loading.Show();
            ui.Loading.Set(0.64f, "Kindling the hearth…");
            for (int i = 0; i < 150 && !ui.Loading.VideoPlaying; i++) yield return new WaitForSeconds(0.1f);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("00_loading", true);
            ui.Loading.Hide();
            yield return new WaitForSeconds(0.6f);

            // 1. The opening story scene, then the starting camp as a new player sees it.
            Focus(Camp(), 26f);
            store.Refresh();
            yield return Shot("00b_story", true);
            game.DismissStory();
            store.Refresh();
            yield return Shot("01_start_camp", true);
            Focus(Camp(), 12f);
            yield return Shot("02_campfire_close", false);
            // Research is open at the first campfire level (the first two techs).
            yield return ShowPanel("02b_research_first_fire", Founders.Panel.Research);
            // A crossing over the river beside the camp, before the town grows around it:
            // bank to bank, on the sand, over the water, on the sand again.
            var map = game.Content.Map;
            for (int by = 4; by < 12; by++)
            {
                int wx = Enumerable.Range(0, 20).FirstOrDefault(x => map.Terrain(x, by) == TerrainType.Water);
                if (wx == 0 || map.Terrain(wx + 1, by) == TerrainType.Water) continue;
                Decorate("wooden_bridge", wx - 1, by);
                Decorate("wooden_bridge", wx, by);
                Decorate("wooden_bridge", wx + 1, by);
                store.Refresh();
                SetTime(game, 1, 0.3f);  // summer, day
                Focus(Grid.CellCenter(wx, by), 14f);
                yield return new WaitForSeconds(1.5f);
                yield return Shot("07f_bridge", false);
                break;
            }

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
                Research(tech);
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
            yield return MeasureFps("camp");

            // Levels change how buildings look: a tent at 1, hut at 5, cottage at 10, timber house at 15, two storeys at 20.
            Heart(20);
            int lvIndex = 0;
            foreach (var home in game.State.Buildings.Where(b => b.DefinitionId == "shelter").ToList())
                home.Level = new[] { 20, 15, 10, 5, 1 }[Mathf.Min(4, lvIndex++)];
            foreach (var b in game.State.Buildings.Where(b => b.DefinitionId == "wood_camp" || b.DefinitionId == "storage_pit")) b.Level = 12;
            Heart(5);
            game.Heart.Level = 9;
            store.World.RebuildAll();
            store.Refresh();
            Focus(Camp(), 15f);
            yield return Shot("04b_levels", false);
            store.FocusBuilding(game.Heart.Id);
            yield return Shot("04c_heart_card", true);
            store.SelectedBuildingId = null;

            // Tap a berry bush or a tree: the card sends gatherers there.
            var bush = game.GatherCells(JobType.Foraging).Concat(game.GatherCells(JobType.Woodcutting)).FirstOrDefault();
            store.SelectedNode = bush;
            store.SendGatherer(bush.x, bush.y);
            Focus(Grid.CellCenter(bush.x, bush.y), 12f);
            yield return new WaitForSeconds(6f);
            yield return Shot("04d_send_gatherers", true);
            store.SelectedNode = null;

            // Placing a wood camp shows how many trees it would reach.
            game.DebugGrant(new ResourceBundle(100, 100, 100, 0));
            store.BeginPlacement("wood_camp");
            yield return Shot("04e_placing", true);
            store.CancelPlacement();

            // 3. Village milestone: the campfire at level 10.
            Heart(10);
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
            store.Refresh();
            yield return Shot("05b_chapter_two", true);
            game.DismissStory();
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
                Research(tech);
                var ev = game.PendingEvent;
                if (ev != null)
                {
                    var option = ev.Options.FirstOrDefault(o => game.OptionBlocker(o) == null);
                    if (option != null) game.ResolveEvent(option.Id);
                }
            }
            foreach (var id in new[] { "farm_field", "farm_field", "granary", "pottery_kiln", "animal_pen", "pasture", "copper_smelter" })
            {
                Build(id);
                store.Refresh();
                yield return null;
            }
            // The first chain: smelters cast bronze from ore and wood (the workshop and the Bronze Dawn need it).
            game.State.Population = Mathf.Max(game.State.Population, 20);
            game.DebugGrant(new ResourceBundle(food: 200, wood: 120, ore: 100));
            game.SetWorkers(JobType.Smelting, Mathf.Min(game.JobCapacity(JobType.Smelting), game.IdleWorkers + game.State.Assignments[JobType.Smelting]));
            for (int i = 0; i < 6 && game.Made(ResourceType.Bronze) < 25; i++) game.DebugFastForward(120);
            var smelter = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "copper_smelter");
            if (smelter != null)
            {
                store.SelectedBuildingId = smelter.Id;
                store.Refresh();
                Focus(Grid.CellCenter(smelter.X, smelter.Y), 14f);
                yield return Shot("06a_smelter", true);
                store.SelectedBuildingId = null;
            }
            Build("bronze_workshop");
            store.Refresh();
            yield return null;
            var woven = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "shelter");
            if (woven != null) { woven.DefinitionId = "daub_house"; woven.History.Add("daub_house"); }
            game.State.Population = Mathf.Max(game.State.Population, 20);
            var pending = game.PendingEvent;
            if (pending != null) game.ResolveEvent(pending.Options.First(o => game.OptionBlocker(o) == null).Id);
            Heart(20);
            if (game.State.Chapter < 3) game.State.Chapter = 3;
            if (game.CanClaimMilestone("first_civilization")) game.ClaimMilestone("first_civilization");
            else Debug.LogWarning("[FOUNDERS] capture: first_civilization not claimable: " +
                                  string.Join(", ", game.MilestoneRequirements("first_civilization").Where(r => !r.Met).Select(r => r.Text)));
            store.World.RebuildAll();
            store.Refresh();
            Focus(Camp(), 26f);
            yield return Shot("06b_bronze_dawn", true);
            store.Celebration = null;
            game.DismissStory();
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
            ResolvePending();
            SetTime(game, 1, 0.3f);   // a summer morning for the town shots
            yield return new WaitForSeconds(1f);
            Focus(Camp() + new Vector3(2f, 0, -5f), 20f);
            yield return Shot("06g_town_layout", false);
            store.BeginDecorating("dirt_path");
            yield return Shot("06h_road_tool", true);
            store.EndDecorating();
            var sel = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "wood_camp") ?? game.State.Buildings.First();
            store.SelectedBuildingId = sel.Id;
            store.Refresh();
            yield return Shot("06i_building_levels", true);
            store.SelectedBuildingId = null;
            store.HudHidden = true;
            yield return Shot("06j_hud_hidden", true);
            store.HudHidden = false;

            // Work up close: builders at a new site, woodcutters in the forest.
            game.DebugGrant(new ResourceBundle(200, 200, 200, 0));
            var siteCell = FindCell("longhouse");
            if (siteCell != null && game.PlaceBuilding("longhouse", siteCell.Value.Item1, siteCell.Value.Item2).Ok)
            {
                store.Refresh();
                yield return new WaitForSeconds(12f);
                Focus(Grid.CellCenter(siteCell.Value.Item1 + 0.5f, siteCell.Value.Item2 + 0.5f), 10f);
                yield return Shot("06k_builders", false);
            }
            // A woodcutter mid-swing: put people to work in the forest and wait for an axe to rise.
            store.SetWorkers(JobType.Woodcutting, game.JobCapacity(JobType.Woodcutting));
            Villager chopper = null;
            for (int i = 0; i < 300 && chopper == null; i++)
            {
                chopper = store.World.FindVillager("Chop", "Prop_Axe");
                yield return new WaitForSeconds(0.1f);
            }
            var camp = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "wood_camp");
            if (chopper != null || camp != null)
            {
                Focus(chopper != null ? chopper.transform.position : Grid.CellCenter(camp.X, camp.Y), 8f);
                yield return new WaitForSeconds(0.4f);
                yield return Shot("06l_woodcutters", false);
            }
            // Up close (nearer than the strategy camera goes): a woodcutter at the trunk, a quarrier at
            // the rock, and a villager's face, to judge the people and where they work.
            chopper = store.World.FindVillager("Chop", "Prop_Axe") ?? chopper;
            yield return CloseUp("06l2_woodcutter_close", chopper, new Vector3(-2.3f, 0.5f, 2.1f), 0.55f, 0.6f);
            store.SetWorkers(JobType.Stonecutting, game.JobCapacity(JobType.Stonecutting));
            Villager quarrier = null;
            for (int i = 0; i < 300 && quarrier == null; i++)
            {
                quarrier = store.World.FindWorker(JobType.Stonecutting, "Work");
                yield return new WaitForSeconds(0.1f);
            }
            yield return CloseUp("06l3_quarry_close", quarrier, new Vector3(-1.9f, 0.6f, 1.7f), 0.3f, 0.5f);
            Villager sitter = null;
            for (int i = 0; i < 100 && sitter == null; i++)
            {
                sitter = store.World.AnyVillager();
                if (sitter != null && sitter.Clip != "Idle" && sitter.Clip != "Talk" && sitter.Clip != "Watch" && i < 99) sitter = null;
                yield return new WaitForSeconds(0.1f);
            }
            yield return CloseUp("06l4_villager_portrait", sitter, new Vector3(0.3f, 0.02f, 1.55f), 0.93f, 0f);

            // 3d. The Middle Ages: the charter, stone houses, the market hall, the mill and the church.
            game.State.Chapter = Mathf.Max(game.State.Chapter, 5);
            Heart(30);
            foreach (var tech in new[] { "written_law", "masonry", "ironworking", "watermill", "guilds", "burgher_rights", "monastic_orders", "animal_breeding" })
                if (!game.State.Researched.Contains(tech)) game.State.Researched.Add(tech);
            game.State.Population = Mathf.Max(game.State.Population, 40);
            if (game.CanClaimMilestone("the_charter")) game.ClaimMilestone("the_charter");
            else
            {
                // Capture only: the layout above is smaller than a real charter town.
                game.State.ReachedMilestones.Add("the_charter");
                game.State.EpochId = "epoch_medieval_kingdoms";
                game.State.StageId = "charter_town";
                game.State.Chapter = 6;
                game.State.PendingStory = "ch6_market_town";
            }
            foreach (var id in new[] { "medieval_cottage", "medieval_house", "medieval_cottage", "medieval_townhouse", "medieval_market",
                                       "medieval_smithy", "medieval_mill", "medieval_church", "medieval_barn" })
            {
                BuildRich(id);
                store.Refresh();
                yield return null;
            }
            store.World.RebuildAll();
            store.Celebration = null;
            store.Refresh();
            yield return Shot("06n_chapter_six", true);
            ResolvePending();
            SetTime(game, 1, 0.32f);
            yield return new WaitForSeconds(1.5f);
            var church = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "medieval_church");
            var townCenter = church != null ? Grid.CellCenter(church.X, church.Y) : Camp();
            Focus(Vector3.Lerp(townCenter, Camp(), 0.5f), 34f);
            yield return Shot("06m_medieval_town", false);
            if (church != null)
            {
                Focus(Grid.CellCenter(church.X + 1f, church.Y + 2.5f), 20f);
                yield return Shot("06o_church", false);
            }
            var mill = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "medieval_mill");
            if (mill != null)
            {
                Focus(Grid.CellCenter(mill.X + 0.5f, mill.Y + 0.5f), 15f);
                yield return Shot("06p_watermill", false);
            }

            // 3d'. Herders among their animals at the pasture and the barn.
            store.SetWorkers(JobType.Herding, game.JobCapacity(JobType.Herding));
            yield return new WaitForSeconds(8f);
            var pasture = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "pasture");
            if (pasture != null)
            {
                Focus(Grid.CellCenter(pasture.X + 0.5f, pasture.Y + 0.5f), 12f);
                yield return Shot("06s_herders", false);
            }
            var barn = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "medieval_barn");
            if (barn != null)
            {
                Focus(Grid.CellCenter(barn.X + 1f, barn.Y + 1f), 16f);
                yield return Shot("06t_barn", false);
            }

            // 3e. Survival: tap a stand of trees and send the woodcutters there; hunger over the heads.
            var stand = Enumerable.Range(0, 400).Select(i => (x: i % 20, y: i / 20))
                .FirstOrDefault(c => game.NodeAt(c.x, c.y)?.Terrain == TerrainType.Forest && game.IsRevealed(c.x, c.y));
            store.SelectedNode = (stand.x, stand.y);
            store.SendGatherer(stand.x, stand.y);
            store.Refresh();
            Focus(Grid.CellCenter(stand.x, stand.y), 18f);
            yield return Shot("06q_tree_order", true);
            store.SelectedNode = null;
            game.State.FoodShortage = true;
            store.Refresh();
            Focus(Camp(), 12f);
            yield return Shot("06r_hunger", false);
            game.State.FoodShortage = false;
            game.DebugGrant(new ResourceBundle(200, 0, 0, 0));
            store.Refresh();

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

            // 3d. Survival (Frostpunk): a winter evening, the heat map, braziers, a healer's hut and the stoked heart.
            yield return SurvivalShots(game);

            ResolvePending();
            SetTime(game, 1, 0.3f);
            yield return new WaitForSeconds(1f);

            // 4. Landscape views: the river and the forest edge.
            Focus(Grid.CellCenter(9.5f, 9f), 16f);
            yield return Shot("07_river", false);
            Focus(Grid.CellCenter(3f, 6f), 14f);
            yield return Shot("08_forest_edge", false);
            Focus(Grid.CellCenter(15f, 33f), 40f);
            yield return Shot("07b_lake", false);
            Focus(Grid.CellCenter(43f, 26f), 60f);
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
            yield return MeasureFps("overview");
            yield return Shot("09b_valley", false);

            // 5. UI screens.
            Focus(Camp(), 24f);
            store.EventMinimized = true;
            yield return ShowPanel("10_build_panel", Founders.Panel.Build);
            yield return ShowPanel("11_people", Founders.Panel.People);
            yield return ShowPanel("12_research", Founders.Panel.Research);
            ui.SelectTab("Roads");
            yield return ShowPanel("10b_build_roads", Founders.Panel.Build);
            yield return ShowPanel("13_goals", Founders.Panel.Objectives);
            yield return ShowPanel("14_world", Founders.Panel.Map);
            // The founder's profile, its dialogs and the account (with a stand-in cloud that is signed in and bound).
            store.RenameFounder("Scyde");
            store.SetAvatar("face_f2");
            store.Cloud.Demo("scyde");
            yield return null;
            ui.ShowProfile(0);
            yield return Shot("15_profile", true);
            ui.ShowProfile(1);
            yield return Shot("15b_profile_stats", true);
            ui.ShowProfile(2);
            yield return Shot("15c_profile_achievements", true);
            ui.ShowProfile(0);
            ui.ShowProfileDialog("avatar");
            yield return Shot("15d_portrait", true);
            ui.ShowProfileDialog("rename");
            yield return Shot("15e_rename", true);
            ui.ShowProfileDialog(null);
            ui.ShowSettings(0);
            yield return Shot("16_settings", true);
            ui.ShowSettings(1);
            yield return Shot("16b_account", true);
            ui.ShowSettings(2);
            yield return Shot("16c_about", true);
            ui.ShowProfileDialog("bind");
            yield return Shot("16d_bind_account", true);
            ui.ShowProfileDialog(null);
            store.ActivePanel = Founders.Panel.None;
            store.Cloud.DemoChoice();
            yield return Shot("16e_cloud_choice", true);
            store.Cloud.KeepDevice();
            yield return null;

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

        }
    }
}
