using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Markets serve the homes around them; a trading post runs caravans along trade routes.</summary>
    public class TradeTests
    {
        /// <summary>A settlement with a finished trading post, nobody at work and plenty of food.</summary>
        static (Game game, ManualClock clock) Post(double stone = 100, double wood = 100, int level = 1)
        {
            var (game, clock) = Fixtures.NewGame();
            game.TestResearch("barter", "trade_routes");
            game.AddCompleted("trading_post", 20, 20).Level = level;
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.State.Resources = new ResourceBundle(food: 300, wood: wood, stone: stone);
            return (game, clock);
        }

        static TradePartnerDefinition Pine(Game g) => g.Content.TradePartner("pine_valley");

        [Test]
        public void ResearchUnlocksTheMarketAndTheTradingPost()
        {
            var c = Fixtures.Content();
            Assert.That(c.Building("market").RequiresTech, Is.EqualTo("barter"));
            Assert.That(c.Building("trading_post").RequiresTech, Is.EqualTo("trade_routes"));
            Assert.That(c.Technology("trade_routes").Prerequisites, Does.Contain("barter"));
            Assert.That(c.TradePartners.Count, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void ARouteNeedsATradingPost()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(food: 200, wood: 200, stone: 200));
            var r = game.OpenTradeRoute("pine_valley");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error.Kind, Is.EqualTo(GameErrorKind.NotReady));
            Assert.That(game.State.TradeRoutes, Is.Empty);
        }

        [Test]
        public void OpeningPaysTheGiftsAndTheCaravanTradesEveryTrip()
        {
            var (game, clock) = Post(stone: 100);
            double food = game.State.Resources.Food;
            Expect.Success(game.OpenTradeRoute("pine_valley"));
            Expect.Success(game.SetTradeDeal("pine_valley", 1)); // 20 stone → 30 wood
            var partner = Pine(game);
            Assert.That(game.State.Resources.Food, Is.EqualTo(food - partner.OpenCost.Food - partner.Deals[0].Give.Food).Within(1e-6));
            // The first caravan loads the deal chosen first (food for wood) only when the food is there: it was.
            var route = game.Route("pine_valley");
            Assert.That(route.Loaded, Is.True);
            Assert.That(game.State.Legacy.Any(l => l.Kind == "trade"), Is.True);

            // After the first caravan is back, the next ones carry stone for wood.
            game.Run(partner.TripSeconds + 1, clock);
            Assert.That(route.Trips, Is.EqualTo(1));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(135).Within(1e-6));
            Assert.That(route.Loaded, Is.True);
            Assert.That(route.LoadedDeal, Is.EqualTo(1));
            Assert.That(game.State.Resources.Stone, Is.EqualTo(60).Within(1e-6));
            game.Run(partner.TripSeconds, clock);
            Assert.That(route.Trips, Is.EqualTo(2));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(165).Within(1e-6));
            Assert.That(route.Received.Wood, Is.EqualTo(65).Within(1e-6));
        }

        [Test]
        public void TheCaravanWaitsForGoodsAndNeverTakesMoreThanThereIs()
        {
            var (game, clock) = Post(stone: 30); // 20 go as gifts to open the route
            Expect.Success(game.OpenTradeRoute("pine_valley"));
            var route = game.Route("pine_valley");
            Assert.That(route.Loaded, Is.True, "food for the first trip");
            Expect.Success(game.SetTradeDeal("pine_valley", 1)); // 20 stone → 30 wood from the next trip on
            game.Run(Pine(game).TripSeconds + 1, clock);
            Assert.That(route.Trips, Is.EqualTo(1));
            Assert.That(route.Loaded, Is.False, "only 10 stone: the caravan waits");
            var status = game.RouteStatus(route);
            Assert.That(status.State, Is.EqualTo(TradeRouteState.WaitingForGoods));
            Assert.That(status.Missing.Stone, Is.EqualTo(10).Within(1e-6));
            Assert.That(game.State.Resources.Stone, Is.EqualTo(10).Within(1e-6));
            game.TestGrant(new ResourceBundle(stone: 10));
            game.Run(1, clock);
            Assert.That(route.Loaded, Is.True);
            Assert.That(game.State.Resources.Stone, Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void APausedRouteBringsItsCaravanHomeAndThenRests()
        {
            var (game, clock) = Post();
            Expect.Success(game.OpenTradeRoute("pine_valley"));
            Expect.Success(game.SetTradeRouteActive("pine_valley", false));
            var route = game.Route("pine_valley");
            Assert.That(route.Loaded, Is.True);
            game.Run(Pine(game).TripSeconds * 3, clock);
            Assert.That(route.Trips, Is.EqualTo(1));
            Assert.That(route.Loaded, Is.False);
            Assert.That(game.RouteStatus(route).State, Is.EqualTo(TradeRouteState.Paused));
        }

        [Test]
        public void ABetterTradingPostRunsMoreRoutesAndFasterCaravans()
        {
            var (game, _) = Post();
            Expect.Success(game.OpenTradeRoute("pine_valley"));
            Assert.That(game.TradeRouteBlocker("reed_folk"), Does.Contain("level 4"));
            Assert.That(game.OpenTradeRoute("reed_folk").Ok, Is.False);

            var (big, _) = Post(level: 4);
            big.TestGrant(new ResourceBundle(wood: 100));
            Assert.That(big.TradeRouteSlots(), Is.EqualTo(2));
            Assert.That(big.TripSeconds(Pine(big)), Is.LessThan(Pine(big).TripSeconds));
            Expect.Success(big.OpenTradeRoute("pine_valley"));
            Expect.Success(big.OpenTradeRoute("reed_folk"));
        }

        [Test]
        public void SomePeoplesNeedResearchFirst()
        {
            var (game, _) = Post();
            game.TestGrant(new ResourceBundle(food: 200, wood: 200, stone: 200));
            Assert.That(game.TradeRouteBlocker("copper_merchants"), Does.Contain("Copper Smelting"));
            game.TestResearch("copper_smelting");
            Assert.That(game.TradeRouteBlocker("copper_merchants"), Is.Null);
        }

        [Test]
        public void RoutesSurviveSaving()
        {
            var (game, clock) = Post();
            game.TestGrant(new ResourceBundle(wood: 100));
            Expect.Success(game.OpenTradeRoute("hill_clan"));
            Expect.Success(game.SetTradeDeal("hill_clan", 2));
            game.Run(50, clock);
            var copy = game.State.Clone();
            var a = game.Route("hill_clan");
            var b = copy.TradeRoutes.Single();
            Assert.That(b.PartnerId, Is.EqualTo("hill_clan"));
            Assert.That(b.Deal, Is.EqualTo(2));
            Assert.That(b.Loaded, Is.EqualTo(a.Loaded));
            Assert.That(b.Elapsed, Is.EqualTo(a.Elapsed).Within(1e-9));
            Assert.That(SaveCodec.Validate(copy, game.Content), Is.Empty);
        }

        [Test]
        public void HomesNearAMarketAreHappier()
        {
            var (game, _) = Fixtures.NewGame();
            game.State.Buildings.Clear();
            game.AddCompleted("shelter", 10, 10);
            game.AddCompleted("shelter", 40, 40);
            var (served, housed) = game.MarketCoverage();
            Assert.That(served, Is.EqualTo(0));
            Assert.That(game.MarketApprovalPoints(), Is.EqualTo(0));

            game.AddCompleted("market", 13, 10);
            (served, housed) = game.MarketCoverage();
            Assert.That(served, Is.GreaterThan(0));
            Assert.That(served, Is.LessThan(housed), "the far shelter is out of reach");
            int half = game.MarketApprovalPoints();
            Assert.That(half, Is.GreaterThan(0).And.LessThan(Game.MarketApproval));
            Assert.That(game.ApprovalFactors().Any(f => f.Label.StartsWith("Market")), Is.True);

            game.AddCompleted("market", 43, 40);
            Assert.That(game.MarketApprovalPoints(), Is.EqualTo(Game.MarketApproval));
        }
    }
}
