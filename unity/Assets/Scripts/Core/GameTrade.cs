using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    // MARK: - Content

    /// <summary>One exchange a neighbouring people will make: what the caravan takes out and brings home.</summary>
    public sealed class TradeDeal
    {
        public ResourceBundle Give, Get;

        internal static TradeDeal FromJson(JObj o) => new TradeDeal
        {
            Give = ResourceBundle.FromJson(o.Obj("give")),
            Get = ResourceBundle.FromJson(o.Obj("get")),
        };
    }

    /// <summary>A neighbouring people beyond the valley that caravans from the trading post can reach.</summary>
    public sealed class TradePartnerDefinition
    {
        public string Id, Name, Description;
        /// <summary>Side of the valley the road leaves by: north, east, south or west.</summary>
        public string Direction;
        /// <summary>Game seconds for one round trip at a level 1 trading post.</summary>
        public double TripSeconds;
        /// <summary>Gifts that open the route, paid once.</summary>
        public ResourceBundle OpenCost;
        public string RequiresTech;
        public List<TradeDeal> Deals = new List<TradeDeal>();
        public string LegacyText;

        /// <summary>Map step (x, y) of the direction; y grows southwards like the map rows.</summary>
        public (int dx, int dy) Heading => Direction switch
        {
            "north" => (0, -1),
            "south" => (0, 1),
            "west" => (-1, 0),
            _ => (1, 0),
        };

        internal static TradePartnerDefinition FromJson(JObj o) => new TradePartnerDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Direction = o.Str("direction"),
            TripSeconds = o.Num("tripSeconds"),
            OpenCost = ResourceBundle.FromJson(o.OptObj("openCost")),
            RequiresTech = o.OptStr("requiresTech"),
            Deals = o.Objs("deals").Select(TradeDeal.FromJson).ToList(),
            LegacyText = o.OptStr("legacyText"),
        };
    }

    // MARK: - State

    /// <summary>An open trade route: the deal it runs and where its caravan is.</summary>
    public sealed class TradeRoute
    {
        public string PartnerId;
        /// <summary>Index of the chosen deal (the next caravan loads it).</summary>
        public int Deal;
        /// <summary>False: the caravan stays home after the trip it is on.</summary>
        public bool Active = true;
        /// <summary>The caravan is on the road with the goods of <see cref="LoadedDeal"/>.</summary>
        public bool Loaded;
        public int LoadedDeal;
        /// <summary>Game seconds into the current trip.</summary>
        public double Elapsed;
        public int Trips;
        /// <summary>Everything this route has brought home.</summary>
        public ResourceBundle Received;

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { ["partnerId"] = PartnerId, ["deal"] = Deal, ["active"] = Active, ["trips"] = Trips };
            if (Loaded)
            {
                d["loaded"] = true;
                d["loadedDeal"] = LoadedDeal;
                d["elapsed"] = Elapsed;
            }
            if (!Received.IsZero) d["received"] = Received.ToJson();
            return d;
        }

        internal static TradeRoute FromJson(JObj o) => new TradeRoute
        {
            PartnerId = o.Str("partnerId"),
            Deal = o.OptInt("deal") ?? 0,
            Active = o.OptBool("active", true),
            Loaded = o.OptBool("loaded", false),
            LoadedDeal = o.OptInt("loadedDeal") ?? 0,
            Elapsed = o.OptNum("elapsed") ?? 0,
            Trips = o.OptInt("trips") ?? 0,
            Received = o.Has("received") ? ResourceBundle.FromJson(o.Obj("received")) : ResourceBundle.Zero,
        };
    }

    public enum TradeRouteState { OnTheRoad, WaitingForGoods, Paused, NoTradingPost }

    public struct TradeRouteStatus
    {
        public TradeRouteState State;
        /// <summary>0..1 through the trip (on the road).</summary>
        public double Progress;
        public double SecondsLeft;
        /// <summary>Goods still missing for the next load (waiting).</summary>
        public ResourceBundle Missing;
    }

    /// <summary>
    /// Markets and trade (after Foundation): a market serves the homes around it, which makes
    /// their families happier; a trading post sends caravans along trade routes to the peoples
    /// beyond the valley, taking out goods the town has plenty of and bringing home what it lacks.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>A trading post runs one more route every this many levels.</summary>
        public const int TradeLevelsPerRoute = 4;
        /// <summary>Approval when every home is near a market.</summary>
        public const int MarketApproval = 8;

        // MARK: - Market

        /// <summary>Cells a market reaches at its level: its base reach, one more every three levels.</summary>
        public static int MarketReachOf(PlacedBuilding b, BuildingDefinition def) => (def.MarketReach ?? 0) + (b.Level - 1) / 3;

        /// <summary>People living in homes within reach of a market, and all people with a home.</summary>
        public (int served, int housed) MarketCoverage()
        {
            var markets = OperationalBuildings().Where(x => (x.def.MarketReach ?? 0) > 0).ToList();
            int served = 0, housed = 0;
            foreach (var (b, def) in OperationalBuildings())
            {
                int homes = HousingOf(b, def);
                if (homes <= 0) continue;
                housed += homes;
                var fp = FootprintOf(b);
                double hx = b.X + fp.Width / 2.0, hy = b.Y + fp.Height / 2.0;
                foreach (var (m, mdef) in markets)
                {
                    var mfp = FootprintOf(m);
                    double dx = m.X + mfp.Width / 2.0 - hx, dy = m.Y + mfp.Height / 2.0 - hy;
                    if (Math.Sqrt(dx * dx + dy * dy) <= MarketReachOf(m, mdef)) { served += homes; break; }
                }
            }
            return (served, housed);
        }

        /// <summary>Approval from markets: up to <see cref="MarketApproval"/>, by the share of families served.</summary>
        public int MarketApprovalPoints()
        {
            var (served, housed) = MarketCoverage();
            if (served <= 0 || housed <= 0) return 0;
            return Math.Max(1, (int)Math.Round(MarketApproval * served / (double)housed));
        }

        // MARK: - Trading post

        /// <summary>The operational trading post with the highest level, or null.</summary>
        public PlacedBuilding TradingPost() =>
            OperationalBuildings().Where(x => (x.def.TradeRoutes ?? 0) > 0).OrderByDescending(x => x.building.Level).Select(x => x.building).FirstOrDefault();

        /// <summary>How many routes the trading posts can run at once.</summary>
        public int TradeRouteSlots() =>
            OperationalBuildings().Where(x => (x.def.TradeRoutes ?? 0) > 0).Sum(x => x.def.TradeRoutes.Value + x.building.Level / TradeLevelsPerRoute);

        /// <summary>Level of the best trading post at which it runs one more route, or null at the top.</summary>
        public int? NextRouteLevel()
        {
            var post = TradingPost();
            if (post == null) return null;
            var def = Content.Building(post.DefinitionId);
            int next = (post.Level / TradeLevelsPerRoute + 1) * TradeLevelsPerRoute;
            return def != null && next <= def.MaxLevel ? next : (int?)null;
        }

        /// <summary>Game seconds for one round trip: a better trading post (more carts and pack animals) is quicker.</summary>
        public double TripSeconds(TradePartnerDefinition partner)
        {
            var post = TradingPost();
            int level = post?.Level ?? 1;
            return partner.TripSeconds / (1 + 0.08 * (level - 1));
        }

        public TradeRoute Route(string partnerId) => State.TradeRoutes.FirstOrDefault(r => r.PartnerId == partnerId);

        /// <summary>Why a route to this people cannot be opened now (null = it can).</summary>
        public string TradeRouteBlocker(string partnerId)
        {
            var partner = Content.TradePartner(partnerId);
            if (partner == null) return "Unknown people.";
            if (Route(partnerId) != null) return "The route is already open.";
            if (TradingPost() == null) return "Build a Trading Post first.";
            if (partner.RequiresTech != null && !State.Researched.Contains(partner.RequiresTech))
                return $"Research {Content.Technology(partner.RequiresTech)?.Name ?? partner.RequiresTech} first.";
            if (State.TradeRoutes.Count >= TradeRouteSlots())
            {
                int? next = NextRouteLevel();
                return next.HasValue ? $"All routes are busy: a level {next} Trading Post runs one more." : "All routes are busy.";
            }
            return null;
        }

        /// <summary>Opens a trade route: pays the opening gifts and sends the first caravan when goods are ready.</summary>
        public GameResult OpenTradeRoute(string partnerId)
        {
            Update();
            var partner = Content.TradePartner(partnerId);
            if (partner == null) return GameError.UnknownContent(partnerId);
            string blocker = TradeRouteBlocker(partnerId);
            if (blocker != null) return GameError.NotReady(blocker);
            if (!State.Resources.Covers(partner.OpenCost)) return GameError.Unaffordable(State.Resources.Shortfall(partner.OpenCost));
            State.Resources = State.Resources - partner.OpenCost;
            State.TradeRoutes.Add(new TradeRoute { PartnerId = partnerId });
            Notice(NoticeKind.Success, $"A trade route to the {partner.Name} is open.");
            AddLegacy("trade", $"Trade route to the {partner.Name}", partner.LegacyText ?? partner.Description, partner.Id);
            AdvanceTrade(0);
            EvaluateProgress();
            return GameResult.Success;
        }

        /// <summary>Chooses what the route trades from the next caravan on.</summary>
        public GameResult SetTradeDeal(string partnerId, int deal)
        {
            Update();
            var partner = Content.TradePartner(partnerId);
            var route = Route(partnerId);
            if (partner == null || route == null) return GameError.UnknownContent(partnerId);
            if (deal < 0 || deal >= partner.Deals.Count) return GameError.OptionUnavailable("That trade is not offered.");
            route.Deal = deal;
            AdvanceTrade(0);
            return GameResult.Success;
        }

        /// <summary>Pauses or resumes a route. A caravan on the road still comes home with its goods.</summary>
        public GameResult SetTradeRouteActive(string partnerId, bool active)
        {
            Update();
            var route = Route(partnerId);
            if (route == null) return GameError.UnknownContent(partnerId);
            route.Active = active;
            AdvanceTrade(0);
            return GameResult.Success;
        }

        public TradeRouteStatus RouteStatus(TradeRoute route)
        {
            var partner = Content.TradePartner(route.PartnerId);
            if (partner == null) return new TradeRouteStatus { State = TradeRouteState.Paused };
            if (route.Loaded)
            {
                double total = TripSeconds(partner);
                return new TradeRouteStatus
                {
                    State = TradingPost() == null ? TradeRouteState.NoTradingPost : TradeRouteState.OnTheRoad,
                    Progress = Math.Min(1, route.Elapsed / Math.Max(1, total)),
                    SecondsLeft = Math.Max(0, total - route.Elapsed),
                };
            }
            if (TradingPost() == null) return new TradeRouteStatus { State = TradeRouteState.NoTradingPost };
            if (!route.Active) return new TradeRouteStatus { State = TradeRouteState.Paused };
            var give = partner.Deals[Math.Min(route.Deal, partner.Deals.Count - 1)].Give;
            return new TradeRouteStatus { State = TradeRouteState.WaitingForGoods, Missing = State.Resources.Shortfall(give) };
        }

        /// <summary>Goods all routes bring home per second on average (for the economy overview).</summary>
        public ResourceBundle TradeIncomePerSecond()
        {
            var sum = ResourceBundle.Zero;
            foreach (var r in State.TradeRoutes)
            {
                var p = Content.TradePartner(r.PartnerId);
                if (p == null || !r.Active) continue;
                var deal = p.Deals[Math.Min(r.Deal, p.Deals.Count - 1)];
                sum = sum + (deal.Get - deal.Give) * (1 / Math.Max(1, TripSeconds(p)));
            }
            return sum;
        }

        /// <summary>Caravans load at the trading post, travel, and unload what they traded for.</summary>
        void AdvanceTrade(double dt)
        {
            if (State.TradeRoutes.Count == 0 || TradingPost() == null) return;
            foreach (var route in State.TradeRoutes)
            {
                var partner = Content.TradePartner(route.PartnerId);
                if (partner == null || partner.Deals.Count == 0) continue;
                if (route.Loaded)
                {
                    route.Elapsed += dt;
                    if (route.Elapsed + 1e-9 < TripSeconds(partner)) continue;
                    var deal = partner.Deals[Math.Min(route.LoadedDeal, partner.Deals.Count - 1)];
                    var caps = StorageCapacity();
                    var res = State.Resources + deal.Get;
                    foreach (var t in ResourceTypes.All) res[t] = Math.Min(caps[t], res[t]);
                    State.Resources = res;
                    route.Received = route.Received + deal.Get;
                    route.Trips += 1;
                    route.Loaded = false;
                    route.Elapsed = 0;
                    if (!simulatingOffline) Notice(NoticeKind.Success, $"The caravan from the {partner.Name} is back with {deal.Get.ShortDescription}.");
                }
                if (route.Loaded || !route.Active) continue;
                int d = Math.Min(route.Deal, partner.Deals.Count - 1);
                var give = partner.Deals[d].Give;
                if (!State.Resources.Covers(give)) continue;
                State.Resources = State.Resources - give;
                route.Loaded = true;
                route.LoadedDeal = d;
                route.Elapsed = 0;
            }
        }
    }
}
