using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Needs, house tiers and taxes: water, household goods from a market, coins.</summary>
    public class NeedsTests
    {
        /// <summary>A home far from the river at a level, nobody at work, and the given stock.</summary>
        static (Game game, ManualClock clock, PlacedBuilding home) Town(int level, ResourceBundle stock)
        {
            var (game, clock) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            var (x, y) = DryCell(game);
            var home = game.AddCompleted("shelter", x, y);
            home.Level = level;
            game.State.Population = game.HousingOf(home, game.Content.Building("shelter"));
            game.State.Resources = stock;
            return (game, clock, home);
        }

        /// <summary>A grass cell further than the river reach from any water.</summary>
        static (int x, int y) DryCell(Game game)
        {
            var map = game.Content.Map;
            for (int y = 2; y < map.Height - 2; y++)
                for (int x = 2; x < map.Width - 2; x++)
                {
                    if (map.Terrain(x, y) != TerrainType.Grass) continue;
                    bool dry = true;
                    for (int dy = -5; dy <= 5 && dry; dy++)
                        for (int dx = -5; dx <= 5 && dry; dx++)
                            if (map.Terrain(x + dx, y + dy) == TerrainType.Water) dry = false;
                    if (dry && !game.State.Buildings.Any(b => System.Math.Abs(b.X - x) < 12 && System.Math.Abs(b.Y - y) < 12)) return (x, y);
                }
            Assert.Fail("no dry meadow on the map");
            return (0, 0);
        }

        [Test]
        public void TiersFollowTheHomeLevel()
        {
            var (game, _, _) = Town(1, new ResourceBundle(food: 500));
            Assert.That(game.TierFor(1).Name, Is.EqualTo("Huts"));
            Assert.That(game.TierFor(5).NeedsWater, Is.True);
            Assert.That(game.TierFor(9).NeedsGoods, Is.False);
            Assert.That(game.TierFor(10).NeedsGoods, Is.True);
        }

        [Test]
        public void HutsPayTaxWithFoodAlone()
        {
            var (game, clock, home) = Town(1, new ResourceBundle(food: 500));
            var needs = game.Needs();
            Assert.That(needs.Content, Is.EqualTo(game.State.Population));
            double perSecond = game.State.Population * game.TierFor(1).TaxPerPersonPerHour / 3600.0;
            Assert.That(needs.TaxPerSecond, Is.EqualTo(perSecond).Within(1e-9));
            game.Run(600, clock);
            Assert.That(game.State.Resources.Coins, Is.EqualTo(perSecond * 600).Within(1e-6));
        }

        [Test]
        public void HousesNeedWaterToPayInFull()
        {
            var (game, _, home) = Town(5, new ResourceBundle(food: 500));
            var h = game.Needs().Homes.Single(x => x.Building == home);
            Assert.That(h.Water, Is.False);
            Assert.That(h.AllMet, Is.False);
            double full = h.Occupants * game.TierFor(5).TaxPerPersonPerHour / 3600.0;
            Assert.That(h.TaxPerSecond, Is.EqualTo(full / 2).Within(1e-9), "half tax without water");
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Needs not met") && f.Points < 0), Is.True);

            game.State.Decorations.Add(new PlacedDecoration { Id = 900, DefinitionId = "well", X = home.X - 2, Y = home.Y });
            game.State.NextDecorationId = 901;
            h = game.Needs().Homes.Single(x => x.Building == home);
            Assert.That(h.Water, Is.True);
            Assert.That(h.TaxPerSecond, Is.EqualTo(full).Within(1e-9));
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Needs met") && f.Points > 0), Is.True);
        }

        [Test]
        public void ARiverNearbyIsWaterEnough()
        {
            var (game, _) = Fixtures.NewGame();
            var map = game.Content.Map;
            (int x, int y)? bank = null;
            for (int y = 1; y < map.Height - 1 && bank == null; y++)
                for (int x = 1; x < map.Width - 1 && bank == null; x++)
                    if (map.Terrain(x, y) == TerrainType.Grass && map.Terrain(x + 1, y) == TerrainType.Water) bank = (x, y);
            Assume.That(bank, Is.Not.Null);
            var home = game.AddCompleted("shelter", bank.Value.x, bank.Value.y);
            Assert.That(game.HasWater(home), Is.True);
        }

        [Test]
        public void BurghersNeedAMarketAndUseUpGoods()
        {
            var (game, clock, home) = Town(10, new ResourceBundle(food: 500, goods: 10));
            game.State.Decorations.Add(new PlacedDecoration { Id = 900, DefinitionId = "well", X = home.X - 2, Y = home.Y });
            var h = game.Needs().Homes.Single(x => x.Building == home);
            Assert.That(h.Water, Is.True);
            Assert.That(h.Market, Is.False);
            Assert.That(h.Goods, Is.False, "goods in store do not help without a market");
            Assert.That(game.Needs().GoodsPerSecond, Is.EqualTo(0));

            game.TestResearch("permanent_settlement");
            game.AddCompleted("market_stall", home.X + 3, home.Y);
            h = game.Needs().Homes.Single(x => x.Building == home);
            Assert.That(h.AllMet, Is.True);
            double use = h.Occupants * game.Content.Balance.Needs.GoodsPerPersonPerSecond;
            Assert.That(game.Needs().GoodsPerSecond, Is.EqualTo(use).Within(1e-12));
            game.Run(100, clock);
            Assert.That(game.State.Resources.Goods, Is.EqualTo(10 - use * 100).Within(1e-6));

            // Run out: the goods need is no longer met, and nothing goes below zero.
            game.State.Resources = new ResourceBundle(food: 500, goods: 0.001);
            game.Run(100, clock);
            Assert.That(game.State.Resources.Goods, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.Needs().Homes.Single(x => x.Building == home).Goods, Is.False);
        }

        [Test]
        public void RaisingAHomeIntoATierNeedsWhatTheTierNeeds()
        {
            var (game, _, home) = Town(4, new ResourceBundle(food: 500));
            game.Heart.Level = 30;
            var water = game.LevelRequirements(home).Where(r => r.Text.Contains("well")).ToList();
            Assert.That(water.Count, Is.EqualTo(1));
            Assert.That(water[0].Met, Is.False);
            home.Level = 6;
            Assert.That(game.LevelRequirements(home).Any(r => r.Text.Contains("well")), Is.False, "only the step into a tier asks");
            home.Level = 9;
            Assert.That(game.LevelRequirements(home).Any(r => r.Text.Contains("market")), Is.True);
        }

        [Test]
        public void TaxLevelsTradeCoinsForApproval()
        {
            var (game, _, _) = Town(1, new ResourceBundle(food: 500));
            double fair = game.Needs().TaxPerSecond;
            Expect.Success(game.SetTaxLevel("high"));
            Assert.That(game.Needs().TaxPerSecond, Is.EqualTo(fair * 1.6).Within(1e-9));
            Assert.That(game.ApprovalFactors().Single(f => f.Label.EndsWith("taxes")).Points, Is.LessThan(0));
            Expect.Success(game.SetTaxLevel("low"));
            Assert.That(game.Needs().TaxPerSecond, Is.EqualTo(fair * 0.5).Within(1e-9));
            Assert.That(game.SetTaxLevel("nonsense").Ok, Is.False);

            var store = new MemorySaveStore();
            var clock = new ManualClock();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.State.TaxLevel, Is.EqualTo("low"));
        }

        [Test]
        public void WeaversMakeGoodsFromHides()
        {
            var (game, clock) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.TestResearch("hide_working");
            game.AddCompleted("weavers_hut", 8, 12);
            game.State.Resources = new ResourceBundle(food: 500, wood: 50, hides: 20);
            Expect.Success(game.SetWorkers(JobType.Weaving, 2));
            game.Run(100, clock);
            double goods = 2 * 0.04 * 100;
            Assert.That(game.State.Resources.Goods, Is.EqualTo(goods).Within(1e-6));
            Assert.That(game.State.Resources.Hides, Is.EqualTo(20 - goods * 0.5).Within(1e-6));
        }
    }
}
