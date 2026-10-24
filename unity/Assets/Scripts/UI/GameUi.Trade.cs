using System;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// Trade Routes window (opened from the Trading Post): every people beyond the valley with what
    /// they trade, the route's caravan on its way, the deal it runs, and the button that opens a route.
    /// </summary>
    public partial class GameUi
    {
        /// <summary>Opens the Trade Routes window.</summary>
        public void ShowTrade() => OpenPanel(Panel.Trade);

        static string DirectionName(string d) => d switch
        {
            "north" => "north",
            "south" => "south",
            "west" => "west",
            _ => "east",
        };

        string TradeKey()
        {
            var parts = G.Content.TradePartners.Select(p =>
            {
                var r = G.Route(p.Id);
                if (r == null) return $"{p.Id}:{G.TradeRouteBlocker(p.Id) ?? ""}:{(G.State.Resources.Covers(p.OpenCost) ? 1 : 0)}";
                var st = G.RouteStatus(r);
                string goods = string.Join("", p.Deals.Select(d => G.State.Resources.Covers(d.Give) ? "1" : "0"));
                return $"{p.Id}:{st.State}:{r.Deal}:{r.Active}:{r.Trips}:{goods}";
            });
            return $"{G.TradeRouteSlots()}|{G.TradingPost()?.Level}|" + string.Join(",", parts);
        }

        /// <summary>Goods as icon + amount pairs, without the red "you are short" of costs.</summary>
        VisualElement GoodsLine(ResourceBundle goods, float size = Theme.Body)
        {
            var row = E.Div().Row().Ignore();
            row.style.flexWrap = Wrap.Wrap;
            foreach (var kv in goods.Entries)
            {
                var item = E.Div().Row().Ignore();
                item.style.marginRight = 6;
                item.Add(E.Icon(IconFor(kv.Key), size + 5));
                item.Add(E.T(Fmt.Compact(kv.Value), size).Margin(0, 0, 0, 1));
                row.Add(item);
            }
            return row;
        }

        void BuildTradePanel(VisualElement host)
        {
            var post = G.TradingPost();
            if (post == null)
            {
                host.Add(E.Ink("Build a Trading Post: caravans leave from there for the peoples beyond the valley.", Theme.Body).Margin(4, 0, 8));
                return;
            }
            int used = G.State.TradeRoutes.Count, slots = G.TradeRouteSlots();
            int? next = G.NextRouteLevel();
            string more = used >= slots && next.HasValue ? $" A level {next} Trading Post runs one more." : "";
            host.Add(E.Ink($"Send what you have plenty of, bring home what you lack. Each caravan loads its goods at the Trading Post, " +
                           $"travels to its people and comes back with what it traded for.{more}", Theme.Body, Theme.InkSoft).Margin(2, 0, 8));

            foreach (var partner in G.Content.TradePartners)
            {
                var route = G.Route(partner.Id);
                string pid = partner.Id;
                VisualElement chip = null;
                if (route != null)
                {
                    var st = G.RouteStatus(route);
                    chip = st.State switch
                    {
                        TradeRouteState.OnTheRoad => Live(() => $"On the road {Fmt.Clock(G.Route(pid) is TradeRoute r ? G.RouteStatus(r).SecondsLeft : 0)}", Theme.Num, Theme.Up),
                        TradeRouteState.WaitingForGoods => E.T("Waiting for goods", Theme.Num, Theme.Gold),
                        TradeRouteState.Paused => E.T("Resting", Theme.Num, Theme.Pale),
                        _ => E.T("No Trading Post", Theme.Num, Theme.Bad),
                    };
                }
                var card = Block(partner.Name, out var rows, chip);
                rows.Add(E.Ink(partner.Description, Theme.Body).Margin(6, 0, 2));
                rows.Add(E.Ink($"The road leaves the valley to the {DirectionName(partner.Direction)}. A round trip takes {Fmt.Clock(G.TripSeconds(partner))}.",
                               Theme.Small, Theme.InkSoft).Margin(0, 0, 4));

                if (route == null)
                {
                    // Not open yet: what they trade, and the gifts that open the road.
                    var offers = E.Div().Col().Ignore().Margin(2, 0, 4);
                    foreach (var deal in partner.Deals) offers.Add(DealRow(deal, false, null, false));
                    rows.Add(offers);
                    string blocker = G.TradeRouteBlocker(pid);
                    var bottom = E.Line(true, 58);
                    var gifts = E.Div().Col().Grow().Ignore();
                    gifts.Add(E.T("Gifts to open the road", Theme.Small, Theme.Pale));
                    gifts.Add(CostLine(partner.OpenCost));
                    bottom.Add(gifts);
                    bool can = blocker == null && G.State.Resources.Covers(partner.OpenCost);
                    bottom.Add(E.Button("Open route", can ? Act(() => store.OpenTradeRoute(pid)) : null, can ? E.Kind.Green : E.Kind.Grey,
                                        Theme.ButtonH, null, 16, SubLine("clock", Fmt.Clock(G.TripSeconds(partner))), 170));
                    rows.Add(bottom);
                    if (blocker != null) rows.Add(E.Ink(blocker, Theme.Small, Theme.Bad).Margin(4));
                    host.Add(card);
                    continue;
                }

                // An open route: the caravan's progress, the deals to choose from, pause and resume.
                var status = G.RouteStatus(route);
                if (status.State == TradeRouteState.OnTheRoad)
                    rows.Add(LiveBar(() => G.Route(pid) is TradeRoute r ? G.RouteStatus(r).Progress : 0, 12).Margin(4, 0, 6));
                else if (status.State == TradeRouteState.WaitingForGoods && !status.Missing.IsZero)
                {
                    var wait = E.Div().Row().Ignore().Margin(2, 0, 4);
                    wait.Add(E.T("Still needed: ", Theme.Small, Theme.Gold));
                    wait.Add(GoodsLine(status.Missing, Theme.Small));
                    rows.Add(wait);
                }
                for (int i = 0; i < partner.Deals.Count; i++)
                {
                    int index = i;
                    rows.Add(DealRow(partner.Deals[i], true, route.Deal == i ? null : Act(() => store.SetTradeDeal(pid, index)), route.Deal == i));
                }
                var foot = E.Line(true, 54);
                string brought = route.Trips == 0 ? "No caravan back yet." : $"{route.Trips} trips · brought home {route.Received.ShortDescription}";
                foot.Add(E.Ink(brought, Theme.Small, Theme.InkSoft).Grow());
                bool active = route.Active;
                foot.Add(E.Button(active ? "Rest" : "Resume", Act(() => store.SetTradeRouteActive(pid, !active)), active ? E.Kind.Brown : E.Kind.Green,
                                  Theme.ButtonH, null, 15, null, 130));
                rows.Add(foot);
                host.Add(card);
            }
        }

        /// <summary>One deal: what goes out ➜ what comes back, and (on an open route) the button that picks it.</summary>
        VisualElement DealRow(TradeDeal deal, bool open, Action choose, bool chosen)
        {
            var row = E.Line(true, open ? 52 : 36);
            var give = E.Div().Row().Ignore();
            give.Add(E.T("Send", Theme.Small, Theme.Pale).Margin(0, 6));
            give.Add(GoodsLine(deal.Give));
            row.Add(give);
            row.Add(E.Icon("next", 18).Margin(0, 6, 0, 6));
            var get = E.Div().Row().Grow().Ignore();
            get.Add(E.T("Get", Theme.Small, Theme.Pale).Margin(0, 6));
            get.Add(GoodsLine(deal.Get));
            row.Add(get);
            if (!open) return row;
            if (chosen)
            {
                var on = E.Div().Row().Ignore();
                on.Add(E.Icon("check", 22));
                on.Add(E.T("Trading", Theme.Num, Theme.Up).Margin(0, 0, 0, 4));
                row.Add(on);
            }
            else row.Add(E.Button("Choose", choose, E.Kind.Blue, 40, null, 14, null, 110));
            return row;
        }
    }
}
