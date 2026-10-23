using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Trade with the neighbouring villages (Manor Lords' trade routes, Anno's specialised towns). The scouts meet
    /// neighbours as they explore; a trading post (or a market hall) lets the town deal with them. A deal sells or
    /// buys a batch at once for coins; a lasting route carries a good every minute for as long as there is stock
    /// (selling) or coins and room (buying). Each neighbour pays well for some goods and sells others, so a town
    /// that makes what they want gets rich.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasTrade => Content.Balance.Trade != null;

        public NeighbourDefinition Neighbour(string id) => Content.Balance.Trade?.Neighbours.FirstOrDefault(n => n.Id == id);

        /// <summary>The villages the scouts have met (enough lands explored).</summary>
        public IEnumerable<NeighbourDefinition> KnownNeighbours() =>
            Content.Balance.Trade?.Neighbours.Where(IsKnown) ?? Enumerable.Empty<NeighbourDefinition>();

        public bool IsKnown(NeighbourDefinition n) => n != null && LandsExplored >= n.LandsExplored;

        /// <summary>Routes the finished trading places keep running.</summary>
        public int TradeRouteSlots()
        {
            int slots = 0;
            foreach (var (_, def) in OperationalBuildings()) slots += def.TradeRoutes;
            return slots;
        }

        /// <summary>Coins a neighbour pays for one unit of the town's good (0 = they do not want it).</summary>
        public double SellPrice(NeighbourDefinition n, ResourceType r) => n.Buys[r] * Multiplier(EffectKind.TradePriceMultiplier);

        /// <summary>Coins a neighbour asks for one unit of its good (0 = they do not sell it).</summary>
        public double BuyPrice(NeighbourDefinition n, ResourceType r) => n.Sells[r];

        GameResult CanTrade(NeighbourDefinition n, string id)
        {
            if (n == null) return GameError.UnknownContent(id);
            if (!IsKnown(n)) return GameError.NotReady($"Explore {n.LandsExplored} lands to meet {n.Name}.");
            if (TradeRouteSlots() <= 0) return GameError.NotReady("Build a trading post first.");
            return GameResult.Success;
        }

        /// <summary>Sells a batch of a good to a neighbour for coins.</summary>
        public GameResult SellGoods(string neighbourId, ResourceType r, double amount)
        {
            Update();
            var n = Neighbour(neighbourId);
            var can = CanTrade(n, neighbourId);
            if (!can.Ok) return can;
            double price = SellPrice(n, r);
            if (price <= 0) return GameError.NotReady($"{n.Name} do not want {r.DisplayName()}.");
            if (amount <= 0 || State.Resources[r] < amount - 1e-9) return GameError.Unaffordable(ResourceBundle.Of(r, amount - State.Resources[r]));
            double room = StorageCapacity().Coins - State.Resources.Coins;
            if (room < price * amount - 1e-9) return GameError.NotReady("The treasury is full.");
            State.Resources[r] -= amount;
            State.Resources.Coins += price * amount;
            return GameResult.Success;
        }

        /// <summary>Buys a batch of a neighbour's good with coins.</summary>
        public GameResult BuyGoods(string neighbourId, ResourceType r, double amount)
        {
            Update();
            var n = Neighbour(neighbourId);
            var can = CanTrade(n, neighbourId);
            if (!can.Ok) return can;
            double price = BuyPrice(n, r);
            if (price <= 0) return GameError.NotReady($"{n.Name} do not sell {r.DisplayName()}.");
            double cost = price * amount;
            if (amount <= 0 || State.Resources.Coins < cost - 1e-9) return GameError.Unaffordable(ResourceBundle.Of(ResourceType.Coins, cost - State.Resources.Coins));
            if (StorageCapacity()[r] - State.Resources[r] < amount - 1e-9) return GameError.NotReady("There is no room in the stores.");
            State.Resources.Coins -= cost;
            State.Resources[r] += amount;
            return GameResult.Success;
        }

        /// <summary>Opens a lasting route: the good goes to (or comes from) the neighbour every minute.</summary>
        public GameResult OpenTradeRoute(string neighbourId, ResourceType r, bool sell)
        {
            Update();
            var n = Neighbour(neighbourId);
            var can = CanTrade(n, neighbourId);
            if (!can.Ok) return can;
            if ((sell ? SellPrice(n, r) : BuyPrice(n, r)) <= 0)
                return GameError.NotReady(sell ? $"{n.Name} do not want {r.DisplayName()}." : $"{n.Name} do not sell {r.DisplayName()}.");
            if (State.TradeRoutes.Count >= TradeRouteSlots()) return GameError.NotReady("Every trade route is in use. Build another trading post.");
            if (State.TradeRoutes.Any(t => t.NeighbourId == n.Id && t.Resource == r && t.Sell == sell)) return GameError.NotReady("That route already runs.");
            if (State.Resources.Coins < n.RouteCost - 1e-9) return GameError.Unaffordable(ResourceBundle.Of(ResourceType.Coins, n.RouteCost - State.Resources.Coins));
            State.Resources.Coins -= n.RouteCost;
            State.TradeRoutes.Add(new TradeRoute { NeighbourId = n.Id, Resource = r, Sell = sell });
            return GameResult.Success;
        }

        public GameResult CloseTradeRoute(int index)
        {
            Update();
            if (index < 0 || index >= State.TradeRoutes.Count) return GameError.UnknownContent($"route #{index}");
            State.TradeRoutes.RemoveAt(index);
            return GameResult.Success;
        }

        /// <summary>
        /// The routes' goods and coins for one step: selling takes only what is left after everything else, buying
        /// only what the coins and the stores allow; routes beyond the trading places' count rest.
        /// </summary>
        void ApplyTrade(EconomySnapshot snap, ref ResourceBundle production, ref ResourceBundle consumption, double dt)
        {
            var cfg = Content.Balance.Trade;
            if (cfg == null || State.TradeRoutes.Count == 0) return;
            int slots = TradeRouteSlots();
            var caps = StorageCapacity();
            double per = cfg.RouteAmountPerMinute / 60.0;
            for (int i = 0; i < State.TradeRoutes.Count; i++)
            {
                var route = State.TradeRoutes[i];
                var n = Neighbour(route.NeighbourId);
                double rate = 0;
                if (i < slots && IsKnown(n))
                {
                    var r = route.Resource;
                    if (route.Sell)
                    {
                        double price = SellPrice(n, r);
                        double left = Math.Max(0, State.Resources[r] - consumption[r] * dt) / dt;
                        double room = Math.Max(0, caps.Coins - State.Resources.Coins - (production.Coins - consumption.Coins) * dt) / dt;
                        rate = price > 0 ? Math.Min(per, Math.Min(left, room / price)) : 0;
                        consumption[r] += rate;
                        production.Coins += rate * price;
                    }
                    else
                    {
                        double price = BuyPrice(n, r);
                        double coins = Math.Max(0, State.Resources.Coins - consumption.Coins * dt) / dt;
                        double room = Math.Max(0, caps[r] - State.Resources[r] - (production[r] - consumption[r]) * dt) / dt;
                        rate = price > 0 ? Math.Min(per, Math.Min(coins / price, room)) : 0;
                        if (rate < 1e-9) rate = 0;
                        consumption.Coins += rate * price;
                        production[r] += rate;
                    }
                }
                if (rate < 1e-9) rate = 0;
                snap.Trade.Add(rate);
            }
        }
    }
}
