using System;
using System.Linq;
using Founders.Core;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The Trade tab of the map window: the routes that run, and every neighbour the scouts have met with what
    /// they buy and sell. A deal moves a batch at once; a route carries the good every minute.
    /// </summary>
    public partial class GameUi
    {
        string TradeKey() =>
            $"{G.LandsExplored}|{G.TradeRouteSlots()}|{G.State.TradeRoutes.Count}|{Math.Floor(G.State.Resources.Coins)}|" +
            string.Join(",", ResourceTypes.Stored.Select(r => G.State.Resources[r] >= (G.Content.Balance.Trade?.DealAmount ?? 10) ? "1" : "0"));

        void BuildTrade(VisualElement host)
        {
            var cfg = G.Content.Balance.Trade;
            if (cfg == null) return;
            int slots = G.TradeRouteSlots();
            double deal = cfg.DealAmount;

            var routes = Block("Trade routes", out var rrows, E.T($"{G.State.TradeRoutes.Count}/{slots}", Theme.Num, Theme.Pale));
            rrows.Add(TreasuryLine());
            if (slots <= 0)
                rrows.Add(Notice("info", "Build a Trading Post (research Trail Signs) to trade with the neighbours."));
            else if (G.State.TradeRoutes.Count == 0)
                rrows.Add(E.Ink($"A route carries {cfg.RouteAmountPerMinute:0} goods every minute. Open one from a neighbour below.", Theme.Body, Theme.Pale).Margin(6, 0, 4));
            for (int i = 0; i < G.State.TradeRoutes.Count; i++)
            {
                int index = i;
                var route = G.State.TradeRoutes[i];
                var n = G.Neighbour(route.NeighbourId);
                if (n == null) continue;
                var line = E.Line(true, 54);
                line.Add(E.RowIcon(IconFor(route.Resource)));
                line.Add(E.T($"{(route.Sell ? "Sell" : "Buy")} {route.Resource.DisplayName()} · {n.Name}", Theme.Body).Flex());
                double price = route.Sell ? G.SellPrice(n, route.Resource) : G.BuyPrice(n, route.Resource);
                line.Add(Live(() =>
                {
                    var t = G.Economy().Trade;
                    double rate = index < t.Count ? t[index] : 0;
                    if (rate <= 0) return index >= slots ? "Resting" : route.Sell ? "Nothing to sell" : "No coins or room";
                    return Fmt.PerMinute((route.Sell ? 1 : -1) * rate * price) + " coins";
                }, Theme.Num, route.Sell ? Theme.Up : Theme.Pale).NoShrink().Margin(0, 8));
                line.Add(E.Button("Close", Act(() => G.CloseTradeRoute(index)), E.Kind.Grey, 40, null, 15, null, 86));
                rrows.Add(line);
            }
            host.Add(routes);

            foreach (var n in G.KnownNeighbours())
            {
                var card = Block(n.Name, out var rows, E.T($"Route {n.RouteCost:0}", Theme.Num, Theme.Gold));
                rows.Add(E.Ink(n.Description, Theme.Body).Margin(6, 0, 4));
                foreach (var r in ResourceTypes.All)
                {
                    if (n.Buys[r] > 0) rows.Add(TradeRow(n, r, true, deal, slots));
                }
                foreach (var r in ResourceTypes.All)
                {
                    if (n.Sells[r] > 0) rows.Add(TradeRow(n, r, false, deal, slots));
                }
                host.Add(card);
            }
            var next = cfg.Neighbours.Where(n => !G.IsKnown(n)).OrderBy(n => n.LandsExplored).FirstOrDefault();
            if (next != null)
                host.Add(E.Ink($"Explore {next.LandsExplored} lands to meet more neighbours ({G.LandsExplored} so far).", Theme.Body, Theme.Pale).Margin(4, 0, 8));
        }

        /// <summary>The coins in the treasury, live.</summary>
        VisualElement TreasuryLine()
        {
            var line = E.Line(true);
            line.Add(E.RowIcon("coins"));
            line.Add(E.T("Treasury", Theme.Body).Flex());
            line.Add(Live(() => $"{Math.Floor(G.State.Resources.Coins):0}", Theme.Num, Theme.Gold));
            return line;
        }

        VisualElement TradeRow(NeighbourDefinition n, ResourceType r, bool sell, double deal, int slots)
        {
            var line = E.Line(true, 54);
            line.Add(E.RowIcon(IconFor(r)));
            double price = sell ? G.SellPrice(n, r) : G.BuyPrice(n, r);
            line.Add(E.T($"{(sell ? "They buy" : "They sell")} {r.DisplayName()} · {price.ToString("0.##", Inv)}", Theme.Body).Flex());
            string id = n.Id;
            bool can = slots > 0 && (sell ? G.State.Resources[r] >= deal : G.State.Resources.Coins >= price * deal);
            line.Add(E.Button($"{(sell ? "Sell" : "Buy")} {deal:0}", can ? Act(() => { if (sell) G.SellGoods(id, r, deal); else G.BuyGoods(id, r, deal); }) : null,
                              sell ? E.Kind.Green : E.Kind.Blue, 40, null, 15, null, 96).Margin(0, 6, 0, 0));
            bool route = slots > G.State.TradeRoutes.Count && G.State.Resources.Coins >= n.RouteCost
                         && !G.State.TradeRoutes.Any(t => t.NeighbourId == id && t.Resource == r && t.Sell == sell);
            line.Add(E.Button("Route", route ? Act(() => G.OpenTradeRoute(id, r, sell)) : null, E.Kind.Brown, 40, null, 15, null, 86));
            return line;
        }
    }
}
