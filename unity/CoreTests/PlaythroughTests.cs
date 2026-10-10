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
            "foraging_camp", "stone_site", "shelter", "storage_pit", "wood_camp", "community_circle", "shelter", "toolmaker", "shelter",
        };

        [Test]
        public void ScriptedPlayerReachesStableVillage()
        {
            var (game, clock) = Fixtures.NewGame();
            int nextBuild = 0;
            double? reachedAt = null;

            for (int tick = 0; tick < 60 * 60 / 5; tick++) // up to 60 minutes, decisions every 5 s
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
                if (nextBuild < BuildOrder.Length)
                {
                    var cell = FirstValidCell(game, BuildOrder[nextBuild]);
                    if (cell != null)
                    {
                        Expect.Success(game.PlaceBuilding(BuildOrder[nextBuild], cell.Value.x, cell.Value.y));
                        nextBuild++;
                    }
                }
                var woven = game.Content.Building("woven_shelter");
                var shelter = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "shelter" && b.Status == BuildingStatus.Complete);
                if (game.IsUnlocked(woven) && shelter != null
                    && game.State.Resources.Covers(woven.Cost + new ResourceBundle(wood: 30, stone: 20)))
                {
                    game.UpgradeBuilding(shelter.Id);
                }
                AssignWorkers(game);
                game.Run(5, clock);
            }

            if (reachedAt == null)
            {
                var unmet = game.MilestoneRequirements("stable_village").Where(r => !r.Met).Select(r => r.Text);
                Assert.Fail($"Village not reached in 60 min. Unmet: {string.Join(", ", unmet)}. " +
                            $"State: {game.State.Resources} pop {game.State.Population}");
            }
            double t = reachedAt.Value;
            TestContext.Progress.WriteLine(
                $"PLAYTHROUGH: stable village reached after {(int)(t / 60)} min {(int)t % 60} s of game time; " +
                $"objectives {game.State.CompletedObjectives.Count}/{game.Content.Objectives.Count}");
            Assert.That(game.State.StageId, Is.EqualTo("village"));
            Assert.That(game.State.CompletedObjectives.Count, Is.EqualTo(game.Content.Objectives.Count));
            Assert.That(t, Is.LessThan(45 * 60), "first session should not take longer than ~45 min");
            Assert.That(t, Is.GreaterThan(5 * 60), "first session should not be trivially short");
        }

        static (int x, int y)? FirstValidCell(Game game, string id)
        {
            for (int y = 0; y < game.Content.Map.Height; y++)
                for (int x = 0; x < game.Content.Map.Width; x++)
                    if (game.PlacementIssues(id, x, y).Count == 0) return (x, y);
            return null;
        }

        static void AssignWorkers(Game game)
        {
            foreach (var job in Enums.Jobs) game.SetWorkers(job, 0);
            void Fill(JobType job, int wanted)
            {
                int n = Math.Min(wanted, Math.Min(game.JobCapacity(job), game.IdleWorkers + game.State.Assignments[job]));
                game.SetWorkers(job, n);
            }
            // Feed everyone first, then research, stone, wood, then help building.
            int foragers = 0;
            while (foragers < game.JobCapacity(JobType.Foraging) && game.IdleWorkers > 0 && game.Economy().Net.Food <= 0.05)
            {
                foragers++;
                game.SetWorkers(JobType.Foraging, foragers);
            }
            Fill(JobType.Research, 2);
            Fill(JobType.Stonecutting, 2);
            Fill(JobType.Woodcutting, 3);
            Fill(JobType.Research, 4);
            Fill(JobType.Construction, game.IdleWorkers);
        }
    }
}
