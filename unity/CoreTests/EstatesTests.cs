using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>The three estates: favour from the town, gifts of coins, ranks and their boons, balance.</summary>
    public class EstatesTests
    {
        static (Game game, ManualClock clock) Town(ResourceBundle stock)
        {
            var (game, clock) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.State.Resources = stock;
            return (game, clock);
        }

        [Test]
        public void ThreeEstatesStartWithoutFavour()
        {
            var (game, _) = Town(new ResourceBundle(food: 500));
            Assert.That(game.Estates.Select(e => e.Id), Is.EqualTo(new[] { "commons", "clergy", "crown" }));
            foreach (var e in game.Estates)
            {
                Assert.That(game.EstateRank(e), Is.EqualTo(0));
                Assert.That(game.NextRank(e), Is.SameAs(e.Ranks[0]));
            }
        }

        [Test]
        public void WorkersWinTheCommonsFavour()
        {
            var (game, clock) = Town(new ResourceBundle(food: 5000));
            var commons = game.Estate("commons");
            int cutters = game.JobCapacity(JobType.Woodcutting);
            Assume.That(cutters, Is.GreaterThan(0));
            Expect.Success(game.SetWorkers(JobType.Woodcutting, cutters));
            Assert.That(game.FavourPerHour(commons), Is.EqualTo(cutters * 0.4).Within(1e-9));
            game.Run(3600, clock);
            Assert.That(game.Favour(commons), Is.EqualTo(cutters * 0.4).Within(0.05));
        }

        [Test]
        public void ShrinesWinTheClergyAndTaxesTheCrown()
        {
            var (game, _) = Town(new ResourceBundle(food: 500));
            var clergy = game.Estate("clergy");
            var crown = game.Estate("crown");
            double before = game.FavourPerHour(clergy);
            game.TestResearch("ancestor_rites");
            game.AddCompleted("ancestor_shrine", 6, 12);
            Assert.That(game.FavourPerHour(clergy), Is.EqualTo(before + 4).Within(1e-9));
            Assert.That(game.FavourPerHour(crown), Is.EqualTo(game.Needs().TaxPerSecond * 3600 * 0.2).Within(1e-9));
        }

        [Test]
        public void GiftsOfCoinsRaiseAnEstateAndItsBoonsApply()
        {
            var (game, _) = Town(new ResourceBundle(food: 500, coins: 100));
            var crown = game.Estate("crown");
            double cap = game.StorageCapacity().Coins;
            Expect.Success(game.GrantFavour("crown"));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(60).Within(1e-9));
            Assert.That(game.EstateRank(crown), Is.EqualTo(0));
            Expect.Success(game.GrantFavour("crown"));
            Assert.That(game.EstateRank(crown), Is.EqualTo(1), "40 favour reaches the Royal Charter");
            Assert.That(game.StorageCapacity().Coins, Is.EqualTo(cap * 1.5).Within(1e-6));
            Assert.That(game.GrantFavour("crown").Ok, Is.False, "no coins left");
            Assert.That(game.GrantFavour("nobody").Ok, Is.False);
        }

        [Test]
        public void OneEstateLeftBehindGrumblesAndAllTogetherLiftHope()
        {
            var (game, _) = Town(new ResourceBundle(food: 500));
            game.State.Favour["commons"] = 150;
            Assert.That(game.ApprovalFactors().Any(f => f.Label.Contains("feel forgotten") && f.Points < 0), Is.True);
            Assert.That(game.ApprovalFactors().Single(f => f.Label.Contains("feel forgotten")).Label, Does.Contain("Clergy").And.Contain("Crown"));

            game.State.Favour["clergy"] = 40;
            game.State.Favour["crown"] = 40;
            Assert.That(game.ApprovalFactors().Any(f => f.Label.Contains("feel forgotten")), Is.False);
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "The estates stand together" && f.Points > 0), Is.True);
        }

        [Test]
        public void TheTopRankStopsTheGifts()
        {
            var (game, _) = Town(new ResourceBundle(food: 500, coins: 1000));
            var clergy = game.Estate("clergy");
            game.State.Favour["clergy"] = clergy.Ranks.Last().Favour;
            Assert.That(game.EstateRank(clergy), Is.EqualTo(clergy.Ranks.Count));
            Assert.That(game.NextRank(clergy), Is.Null);
            Assert.That(game.GrantFavour("clergy").Ok, Is.False);
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "Favour of the estates" && f.Points == 4), Is.True);
            Assert.That(game.ApprovalFactors().Any(f => f.Label == "Recent events"), Is.False, "the boons are not counted twice");
        }

        [Test]
        public void FavourSurvivesSaveAndLoad()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500));
            game.State.Favour["crown"] = 77.5;
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(loaded.State.Favour["crown"], Is.EqualTo(77.5));
        }
    }
}
