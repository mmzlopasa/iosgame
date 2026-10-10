using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Founders.Core;
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
        Hud hud;
        string outDir;

        public static bool Requested =>
            File.Exists(Path.Combine(Application.persistentDataPath, "capture.flag")) ||
            System.Environment.GetCommandLineArgs().Contains("-capture");

        public void Begin(GameStore store, Hud hud)
        {
            this.store = store;
            this.hud = hud;
            outDir = Path.Combine(Application.persistentDataPath, "captures");
            Directory.CreateDirectory(outDir);
            StartCoroutine(Run());
        }

        IEnumerator Shot(string name, bool ui)
        {
            hud.Hidden = !ui;
            for (int i = 0; i < 20; i++) yield return null; // let camera moves, particles and LOD settle
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
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

        IEnumerator Run()
        {
            var game = store.Game;
            yield return new WaitForSeconds(2f);

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
            foreach (var tech in new[] { "improved_stone_tools", "food_preservation", "structural_weaving", "shared_labor", "permanent_settlement" })
            {
                game.DebugGrant(new ResourceBundle(0, 0, 0, 60));
                game.StartResearch(tech);
                game.DebugFastForward(70);
                var ev = game.PendingEvent;
                if (ev != null)
                {
                    var option = ev.Options.FirstOrDefault(o => game.OptionBlocker(o) == null);
                    if (option != null) game.ResolveEvent(option.Id);
                }
            }
            store.Refresh();
            Focus(Camp(), 24f);
            yield return Shot("03_camp_grown", false);

            // 3. Village milestone (population grows over time).
            for (int i = 0; i < 12 && !game.CanClaimMilestone("stable_village"); i++)
            {
                game.DebugGrant(new ResourceBundle(120, 0, 0, 0));
                game.DebugFastForward(120);
            }
            if (game.CanClaimMilestone("stable_village")) game.ClaimMilestone("stable_village");
            store.Celebration = null;
            store.Refresh();
            yield return new WaitForSeconds(0.5f);
            Focus(Camp(), 22f);
            yield return Shot("04_village", false);

            // 4. Landscape views: the river and the forest edge.
            var map = game.Content.Map;
            Focus(Grid.CellCenter(9.5f, 9f), 16f);
            yield return Shot("05_river", false);
            Focus(Grid.CellCenter(3f, 6f), 14f);
            yield return Shot("06_forest_edge", false);
            Focus(Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f), 48f);
            yield return Shot("07_overview", true);

            // 5. UI.
            Focus(Camp(), 24f);
            store.ActivePanel = Panel.Build;
            yield return Shot("08_build_panel", true);
            store.ActivePanel = Panel.None;

            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            Debug.Log("[FOUNDERS] capture finished");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
