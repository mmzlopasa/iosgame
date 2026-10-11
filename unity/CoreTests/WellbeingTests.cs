using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Seasons, firewood, approval, farming and the step into a new era.</summary>
    public class WellbeingTests
    {
        [Test]
        public void SeasonsTurnInOrderAndWrapIntoNewYears()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            double len = game.Content.Balance.SeasonSeconds;
            Assert.That(game.Season.Id, Is.EqualTo("spring"));
            Assert.That(game.Year, Is.EqualTo(1));
            game.Run(len + 1, clock);
            Assert.That(game.Season.Id, Is.EqualTo("summer"));
            game.Run(len * 3, clock);
            Assert.That(game.Season.Id, Is.EqualTo("spring"));
            Assert.That(game.Year, Is.EqualTo(2));
            Assert.That(game.DrainNotices().Any(n => n.Message.StartsWith("Winter has come")), Is.True);
        }

        [Test]
        public void WinterBurnsFirewoodAndAnEmptyWoodpileFreezes()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            game.State.SimTime = game.Content.Balance.SeasonSeconds * 3 + 1; // winter
            Assert.That(game.Season.Id, Is.EqualTo("winter"));
            double fuel = game.FuelPerSecond;
            Assert.That(fuel, Is.EqualTo(game.State.Population * 0.01).Within(1e-9));
            Assert.That(game.Economy().Consumption.Wood, Is.EqualTo(fuel).Within(1e-9));

            foreach (var job in Enums.Jobs) game.SetWorkers(job, 0);
            var r = game.State.Resources; r.Wood = 0.1; game.State.Resources = r;
            game.Run(5, clock);
            Assert.That(game.Freezing, Is.True);
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Freezing") && f.Points < 0), Is.True);
        }

        [Test]
        public void ApprovalRewardsComfortGatheringPlacesAndVariety()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            int before = game.Approval;
            game.AddCompleted("community_circle", 3, 9);
            Assert.That(game.Approval, Is.GreaterThan(before));

            foreach (var b in game.State.Buildings.Where(b => b.DefinitionId == "shelter")) b.DefinitionId = "woven_shelter";
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "Housing quality" && f.Points > 0), Is.True);

            game.TestResearch("fishing");
            game.AddCompleted("fishing_weir", 8, 5);
            game.SetWorkers(JobType.Foraging, 1);
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Food variety")), Is.True);
        }

        [Test]
        public void ApprovalChangesWorkSpeed()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            Assert.That(game.ApprovalProductivity(100), Is.GreaterThan(game.ApprovalProductivity(50)));
            Assert.That(game.ApprovalProductivity(50), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(game.ApprovalGrowth(10), Is.EqualTo(0));
        }

        [Test]
        public void FarmsFollowTheSeasons()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.TestResearch("permanent_settlement", "agriculture");
            game.AddCompleted("farm_field", 4, 4);
            Assert.That(game.JobCapacity(JobType.Farming), Is.EqualTo(4));
            Expect.Success(game.SetWorkers(JobType.Foraging, 0));
            Expect.Success(game.SetWorkers(JobType.Farming, 2));
            double len = game.Content.Balance.SeasonSeconds;
            game.State.SimTime = len * 2 + 1; // autumn
            double autumn = game.RatePerWorker(JobType.Farming);
            game.State.SimTime = len * 3 + 1; // winter
            Assert.That(game.RatePerWorker(JobType.Farming), Is.EqualTo(0));
            Assert.That(autumn, Is.GreaterThan(0));
        }

        [Test]
        public void BronzeDawnOpensTheSecondEra()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.State.ReachedMilestones.Add("stable_village");
            game.State.StageId = "village";
            game.TestResearch("bronze_working");
            game.AddCompleted("farm_field", 3, 3);
            game.AddCompleted("farm_field", 3, 5);
            game.AddCompleted("granary", 6, 4);
            game.State.Population = 20;
            Assert.That(game.CanClaimMilestone("first_civilization"), Is.False, "the Bronze Dawn cannot come before year 5");
            game.State.SimTime = 4 * 4 * game.Content.Balance.SeasonSeconds;
            Assert.That(game.Year, Is.EqualTo(5));
            Assert.That(game.CanClaimMilestone("first_civilization"), Is.True);
            Expect.Success(game.ClaimMilestone("first_civilization"));
            Assert.That(game.State.EpochId, Is.EqualTo("epoch_first_civilizations"));
            Assert.That(game.StageName, Is.EqualTo("Farming Village"));
            Assert.That(game.State.Legacy.Any(l => l.Title == "The Bronze Dawn"), Is.True);
        }
    }
}
