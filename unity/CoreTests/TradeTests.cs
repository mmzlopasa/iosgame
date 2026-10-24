using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Trade with the neighbouring villages: deals, lasting routes, prices and limits.</summary>
    public class TradeTests
    {
        /// <summary>A town with nobody at work, the given lands explored, a trading post (optional) and the stock.</summary>
        static (Game game, ManualClock clock) Town(int lands, bool post, ResourceBundle stock)
        {
            var (game, clock) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            foreach (var r in game.Content.Regions.Where(r => !r.Home).Take(lands)) game.State.ExploredRegions.Add(r.Id);
            if (post)
            {
                game.TestResearch("trail_signs");
                game.AddCompleted("trading_post", 8, 12);
            }
            game.State.Population = 0;
            game.State.Resources = stock;
            return (game, clock);
        }

        [Test]
        public void NeighboursAreMetByExploring()
        {
            var (game, _) = Town(0, true, new ResourceBundle(food: 500));
            Assert.That(game.KnownNeighbours(), Is.Empty);
            game.State.ExploredRegions.Add(game.Content.Regions.First(r => !r.Home).Id);
            Assert.That(game.KnownNeighbours().Select(n => n.Id), Is.EqualTo(new[] { "marsh_folk" }));
            Assert.That(game.SellGoods("hill_clan", ResourceType.Food, 10).Ok, Is.False, "not met yet");
        }

        [Test]
        public void NoTradeWithoutATradingPost()
        {
            var (game, _) = Town(1, false, new ResourceBundle(food: 500, wood: 100));
            Assert.That(game.TradeRouteSlots(), Is.EqualTo(0));
            Assert.That(game.SellGoods("marsh_folk", ResourceType.Wood, 10).Ok, Is.False);
        }

        [Test]
        public void DealsTurnGoodsIntoCoinsAndBack()
        {
            var (game, _) = Town(1, true, new ResourceBundle(food: 500, wood: 100));
            Expect.Success(game.SellGoods("marsh_folk", ResourceType.Wood, 10));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(90).Within(1e-9));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(6).Within(1e-9));
            Assert.That(game.SellGoods("marsh_folk", ResourceType.Food, 10).Ok, Is.False, "they do not want food");
            Assert.That(game.SellGoods("marsh_folk", ResourceType.Wood, 500).Ok, Is.False, "not that much wood");

            Assert.That(game.BuyGoods("marsh_folk", ResourceType.Hides, 10).Ok, Is.False, "30 coins needed");
            Expect.Success(game.BuyGoods("marsh_folk", ResourceType.Hides, 2));
            Assert.That(game.State.Resources.Hides, Is.EqualTo(2).Within(1e-9));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.State.Resources.IsValid, Is.True);
        }

        [Test]
        public void GuildsRaiseWhatNeighboursPay()
        {
            var (game, _) = Town(1, true, new ResourceBundle(food: 500));
            var marsh = game.Neighbour("marsh_folk");
            Assert.That(game.SellPrice(marsh, ResourceType.Wood), Is.EqualTo(0.6).Within(1e-9));
            game.TestResearch("guilds");
            Assert.That(game.SellPrice(marsh, ResourceType.Wood), Is.EqualTo(0.6 * 1.15).Within(1e-9));
            Assert.That(game.BuyPrice(marsh, ResourceType.Hides), Is.EqualTo(3.0), "their prices stay");
        }

        [Test]
        public void ARouteSellsEveryMinuteUntilTheStockRunsOut()
        {
            var (game, clock) = Town(1, true, new ResourceBundle(food: 500, wood: 20, coins: 30));
            Expect.Success(game.OpenTradeRoute("marsh_folk", ResourceType.Wood, true));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(0).Within(1e-9), "the route cost 30 coins");
            game.Run(60, clock);
            Assert.That(game.State.Resources.Wood, Is.EqualTo(14).Within(1e-6));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(6 * 0.6).Within(1e-6));
            Assert.That(game.Economy().Trade.Single(), Is.EqualTo(0.1).Within(1e-9));

            game.Run(600, clock);
            Assert.That(game.State.Resources.Wood, Is.EqualTo(0).Within(1e-6));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(20 * 0.6).Within(1e-6));
            Assert.That(game.Economy().Trade.Single(), Is.EqualTo(0), "nothing left to carry");
        }

        [Test]
        public void ABuyingRouteStopsWhenTheCoinsRunOut()
        {
            var (game, clock) = Town(1, true, new ResourceBundle(food: 500, coins: 30 + 9));
            Expect.Success(game.OpenTradeRoute("marsh_folk", ResourceType.Hides, false));
            game.Run(600, clock);
            Assert.That(game.State.Resources.Hides, Is.EqualTo(3).Within(1e-6));
            Assert.That(game.State.Resources.Coins, Is.EqualTo(0).Within(1e-6));
            Assert.That(game.State.Resources.IsValid, Is.True);
        }

        [Test]
        public void OneRoutePerTradingPost()
        {
            var (game, _) = Town(3, true, new ResourceBundle(food: 500, coins: 500));
            Assert.That(game.TradeRouteSlots(), Is.EqualTo(1));
            Expect.Success(game.OpenTradeRoute("marsh_folk", ResourceType.Wood, true));
            Assert.That(game.OpenTradeRoute("hill_clan", ResourceType.Food, true).Ok, Is.False, "every route in use");
            game.AddCompleted("trading_post", 11, 12);
            Expect.Success(game.OpenTradeRoute("hill_clan", ResourceType.Food, true));
            Assert.That(game.OpenTradeRoute("hill_clan", ResourceType.Ore, true).Ok, Is.False, "they sell ore, not buy it");
            Expect.Success(game.CloseTradeRoute(0));
            Assert.That(game.State.TradeRoutes.Single().NeighbourId, Is.EqualTo("hill_clan"));
        }

        [Test]
        public void RoutesSurviveSaveAndLoad()
        {
            var (game, clock) = Town(1, true, new ResourceBundle(food: 500, coins: 100));
            Expect.Success(game.OpenTradeRoute("marsh_folk", ResourceType.Food, false));
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            var route = loaded.State.TradeRoutes.Single();
            Assert.That(route.NeighbourId, Is.EqualTo("marsh_folk"));
            Assert.That(route.Resource, Is.EqualTo(ResourceType.Food));
            Assert.That(route.Sell, Is.False);
        }

        [Test]
        public void BuyingHereAndSellingThereNeverPays()
        {
            var content = Fixtures.Content();
            Assert.That(ContentValidator.Validate(content), Is.Empty);
            foreach (var t in ResourceTypes.All)
            {
                double top = content.Balance.Trade.Neighbours.Max(n => n.Buys[t]) * 1.15 * 1.1; // guilds and the crown's market rights
                foreach (var n in content.Balance.Trade.Neighbours.Where(n => n.Sells[t] > 0))
                    Assert.That(top, Is.LessThan(n.Sells[t]), $"{t} from {n.Id}");
            }
        }
    }
}
