using System;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>A scripted player that uses only public commands. Proves the first session can
    /// be completed and reports how long it takes (balance smoke test).</summary>
    public class PlaythroughTests
    {
        static readonly string[] BuildOrder =
        {
            "foraging_camp", "wood_camp", "stone_site", "storage_pit", "shelter", "hunters_lodge", "community_circle", "shelter", "toolmaker",
            "shelter", "foraging_camp", "wood_camp", "shelter", "storage_pit", "shelter",
        };

        /// <summary>
        /// The road to the village now runs through the campfire's levels (Whiteout Survival
        /// style): the scripted player levels the campfire, the storage and the homes it asks for,
        /// researches, feeds everyone and builds. Proves level 10 can be reached and measures how long.
        /// </summary>
        [Test]
        public void ScriptedPlayerReachesStableVillage()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            int nextBuild = 0;
            double? reachedAt = null;
            const double Hours = 12;

            for (int tick = 0; tick < Hours * 3600 / 10; tick++) // decisions every 10 s
            {
                var ev = game.PendingEvent;
                if (ev != null)
                {
                    var choice = ev.Options.FirstOrDefault(o => game.OptionBlocker(o) == null && o.Id == "open_stores")
                                 ?? ev.Options.First(o => game.OptionBlocker(o) == null);
                    Expect.Success(game.ResolveEvent(choice.Id));
                }
                if (game.CanClaimMilestone("stable_village"))
                {
                    Expect.Success(game.ClaimMilestone("stable_village"));
                    reachedAt = game.State.SimTime;
                    break;
                }
                if (game.State.ActiveResearch == null)
                {
                    var tech = game.Content.Technologies.FirstOrDefault(t =>
                        game.GetResearchStatus(t.Id).State == ResearchState.Available && game.State.Resources.Covers(t.Cost));
                    if (tech != null) Expect.Success(game.StartResearch(tech.Id));
                }
                RaiseTheHeart(game);
                if (game.State.ActiveExpedition == null)
                {
                    var region = game.Content.Regions.Where(r => game.IsFrontier(r) && game.State.Resources.Covers(game.ExpeditionCost(r)))
                                     .OrderBy(r => game.ExpeditionCost(r).Entries.Sum(kv => kv.Value)).FirstOrDefault();
                    if (region != null) game.StartExpedition(region.Id);
                }
                // A gathering crew with nothing left in reach moves on: a new camp by fresh land.
                foreach (var (job, camp) in new[] { (JobType.Woodcutting, "wood_camp"), (JobType.Stonecutting, "stone_site"),
                                                    (JobType.Foraging, "foraging_camp"), (JobType.Hunting, "hunters_lodge") })
                    if (game.IsExhausted(job) && game.Count(camp, true) < 5 && game.ActiveConstructionCount() < 2) TryBuild(game, camp);
                // New homes only while the food keeps up (a bigger family is more mouths).
                if (game.HousingCapacity() <= game.State.Population && game.ActiveConstructionCount() < 2 && game.Economy().Net.Food > 0.1)
                {
                    var home = game.State.Buildings.Where(b => b.DefinitionId == "shelter" && b.Status == BuildingStatus.Complete)
                                   .OrderBy(b => b.Level).FirstOrDefault();
                    if (home == null || !game.UpgradeLevel(home.Id).Ok) TryBuild(game, "shelter");
                }
                if (nextBuild < BuildOrder.Length && TryBuild(game, BuildOrder[nextBuild])) nextBuild++;
                AssignWorkers(game);
                if (tick % 360 == 0)
                    TestContext.Progress.WriteLine($"DBG t={game.State.SimTime / 3600:0.0}h heart={game.HeartLevel} {game.Season?.Id} pop={game.State.Population}/{game.HousingCapacity()} res={game.State.Resources} net={game.Economy().Net} " +
                        string.Join(" ", Enums.Jobs.Where(j => game.State.Assignments[j] > 0).Select(j => $"{j}={game.State.Assignments[j]}/{game.JobCapacity(j)}{(game.IsExhausted(j) ? "!" : "")}")) +
                        $" builds={string.Join(",", game.State.Buildings.Select(b => b.DefinitionId + b.Level))}");
                game.Run(10, clock);
            }

            var heart = game.Heart;
            if (reachedAt == null)
            {
                var unmet = game.MilestoneRequirements("stable_village").Where(r => !r.Met).Select(r => r.Text);
                var level = game.LevelRequirements(heart).Where(r => !r.Met).Select(r => r.Text);
                Assert.Fail($"Village not reached in {Hours} h. Heart level {heart.Level}. Unmet: {string.Join(", ", unmet)}. " +
                            $"Next heart level needs: {string.Join(", ", level)} {game.NextLevel(heart)?.Cost}. " +
                            $"State: {game.State.Resources} pop {game.State.Population} storage {game.StorageCapacity().Wood}");
            }
            double t = reachedAt.Value;
            TestContext.Progress.WriteLine(
                $"PLAYTHROUGH: stable village (campfire level 10) reached after {t / 3600:0.0} h of game time; " +
                $"objectives {game.State.CompletedObjectives.Count}/{game.Content.Objectives.Count}, population {game.State.Population}");
            Assert.That(game.State.StageId, Is.EqualTo("village"));
            // Every goal of the camp is done; the goals after the founding belong to the later eras.
            int campGoals = game.Content.Objectives.FindIndex(o => o.Id == "obj_village") + 1;
            Assert.That(game.Content.Objectives.Take(campGoals).All(o => game.State.CompletedObjectives.Contains(o.Id)), Is.True);
            Assert.That(t, Is.LessThan(Hours * 3600));
            // About three hours (the thinner woods leave more meadow and berry bushes: a little under).
            Assert.That(t, Is.GreaterThan(2.5 * 3600), "the first era change should take hours, not minutes");
        }

        /// <summary>Levels the heart, or whatever its next level is waiting for.</summary>
        static void RaiseTheHeart(Game game)
        {
            var heart = game.Heart;
            if (heart == null || heart.Status != BuildingStatus.Complete || game.NextLevel(heart) == null) return;
            if (game.UpgradeLevel(heart.Id).Ok) return;
            foreach (var r in game.NextLevel(heart).Requirements.Where(r => !game.IsMet(r)))
            {
                if (r.Kind != ConditionKind.BuildingLevelAtLeast) continue;
                var best = game.State.Buildings.Where(b => b.DefinitionId == r.Id).OrderByDescending(b => b.Level).FirstOrDefault();
                if (best == null) TryBuild(game, r.Id);
                else if (best.Status == BuildingStatus.Complete) game.UpgradeLevel(best.Id);
            }
            // Not enough room in the stores for the next level: dig another pit.
            var cost = game.NextLevel(heart).Cost;
            var caps = game.StorageCapacity();
            if (cost.Entries.Any(kv => kv.Value > caps[kv.Key]) && game.ActiveConstructionCount() == 0)
            {
                var pit = game.State.Buildings.Where(b => b.DefinitionId == "storage_pit" && b.Status == BuildingStatus.Complete)
                              .OrderBy(b => b.Level).FirstOrDefault();
                if (pit == null || !game.UpgradeLevel(pit.Id).Ok) TryBuild(game, "storage_pit");
            }
        }

        static bool TryBuild(Game game, string id)
        {
            var cell = FirstValidCell(game, id);
            return cell != null && game.PlaceBuilding(id, cell.Value.x, cell.Value.y).Ok;
        }

        static (int x, int y)? FirstValidCell(Game game, string id)
        {
            var heart = game.Heart;
            int cx = heart?.X ?? 6, cy = heart?.Y ?? 7;
            // Gatherers' camps go where the most of their resource lies within reach (as a player would).
            var def = game.Content.Building(id);
            var job = def.JobSlots?.Select(s => (JobType?)s.Job).FirstOrDefault(j => game.NodeKinds.Any(k => k.Job == j && k.Capacity > 0));
            (int x, int y)? best = null;
            int bestScore = -1;
            for (int r = 1; r < 14; r++)
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != r || game.PlacementIssues(id, x, y).Count > 0) continue;
                        // This player keeps the woods and the quarry stone: it builds only where the ground is open
                        // (or where the building belongs, a quarry on its rock), never over a resource.
                        if (game.TerrainNow(x, y) is TerrainType t && !def.AllowedTerrain.Contains(t)) continue;
                        if (job == null) return (x, y);
                        int score = 0;
                        for (int yy = y - 4; yy <= y + 4; yy++)
                            for (int xx = x - 4; xx <= x + 4; xx++)
                                if (game.NodeAt(xx, yy, job.Value) is ResourceNode n && !n.Depleted) score++;
                        score -= r; // stay close to home
                        if (score > bestScore) { bestScore = score; best = (x, y); }
                    }
            return best;
        }

        static void AssignWorkers(Game game)
        {
            foreach (var job in Enums.Jobs) game.SetWorkers(job, 0);
            void Fill(JobType job, int wanted)
            {
                int n = Math.Min(wanted, Math.Min(game.JobCapacity(job), game.IdleWorkers + game.State.Assignments[job]));
                game.SetWorkers(job, n);
            }
            // Feed everyone first, then research, stone, wood, hides, then help building.
            foreach (var food in new[] { JobType.Foraging, JobType.Hunting, JobType.Fishing })
            {
                int n = 0;
                while (n < game.JobCapacity(food) && game.IdleWorkers > 0 && game.Economy().Net.Food <= 0.08)
                {
                    n++;
                    game.SetWorkers(food, n);
                }
            }
            Fill(JobType.Research, 2);
            Fill(JobType.Woodcutting, 4);
            Fill(JobType.Stonecutting, 3);
            Fill(JobType.Hunting, Math.Max(game.State.Assignments[JobType.Hunting], 1));
            Fill(JobType.Woodcutting, 8);
            Fill(JobType.Construction, game.IdleWorkers);
        }
    }
}
