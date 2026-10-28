using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>What one home needs and has (Manor Lords' burgage plots, Foundation's needs).</summary>
    public struct HomeNeeds
    {
        public PlacedBuilding Building;
        public HouseTier Tier;
        public int Occupants;
        public bool Food, Water, Market, Goods;
        /// <summary>Every need of the home's tier is met.</summary>
        public bool AllMet;
        public double TaxPerSecond;
    }

    public sealed class NeedsReport
    {
        public List<HomeNeeds> Homes = new List<HomeNeeds>();
        /// <summary>People living in homes whose every need is met, and those who lack water or goods.</summary>
        public int Content, WithoutWater, WithoutGoods, Housed;
        public double TaxPerSecond;
        /// <summary>Household goods the homes use up per second (only what the store can give).</summary>
        public double GoodsPerSecond;
    }

    /// <summary>
    /// Needs, house tiers and taxes. A home's level sets its tier (hut, house, burgher house); each tier needs
    /// more (food, then water, then household goods from a market) and pays more tax. Homes that have what
    /// they need pay in full and lift approval; those that lack it pay half and grumble. Raising a home into a
    /// tier needs that tier's water or market nearby, so towns grow around wells and markets.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasNeeds => Content.Balance.Needs != null;

        NeedsReport stepNeeds;

        public HouseTier TierFor(int level)
        {
            var tiers = Content.Balance.Needs?.Tiers;
            if (tiers == null || tiers.Count == 0) return null;
            HouseTier tier = tiers[0];
            foreach (var t in tiers) if (level >= t.Level) tier = t;
            return tier;
        }

        public TaxLevel CurrentTax
        {
            get
            {
                var n = Content.Balance.Needs;
                if (n == null || n.TaxLevels.Count == 0) return null;
                return n.TaxLevels.FirstOrDefault(t => t.Id == State.TaxLevel) ?? n.TaxLevels.FirstOrDefault(t => t.Id == n.DefaultTax)
                       ?? n.TaxLevels[0];
            }
        }

        /// <summary>Sets the tax (low, normal, high): more coins for less approval.</summary>
        public GameResult SetTaxLevel(string id)
        {
            Update();
            var n = Content.Balance.Needs;
            if (n == null || n.TaxLevels.All(t => t.Id != id)) return GameError.UnknownContent(id);
            State.TaxLevel = id;
            return GameResult.Success;
        }

        static double CellDistance(PlacedBuilding a, (int w, int h) fa, double x, double y)
        {
            double cx = a.X + fa.w / 2.0, cy = a.Y + fa.h / 2.0;
            double dx = Math.Max(0, Math.Abs(cx - x) - fa.w / 2.0), dy = Math.Max(0, Math.Abs(cy - y) - fa.h / 2.0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>A well, a water building or the river or lake within reach of the home.</summary>
        public bool HasWater(PlacedBuilding home)
        {
            var n = Content.Balance.Needs;
            if (n == null) return true;
            var fp = FootprintOf(home);
            var size = (fp.Width, fp.Height);
            foreach (var d in State.Decorations)
                if (Content.Decoration(d.DefinitionId)?.Water == true && CellDistance(home, size, d.X + 0.5, d.Y + 0.5) <= n.WaterReach)
                    return true;
            foreach (var (b, def) in OperationalBuildings())
                if (def.Water && b.Id != home.Id)
                {
                    var bf = FootprintOf(b);
                    if (CellDistance(home, size, b.X + bf.Width / 2.0, b.Y + bf.Height / 2.0) <= n.WaterReach + Math.Max(bf.Width, bf.Height) / 2.0)
                        return true;
                }
            int r = (int)Math.Ceiling(n.RiverReach);
            var map = Content.Map;
            for (int y = home.Y - r; y < home.Y + fp.Height + r; y++)
                for (int x = home.X - r; x < home.X + fp.Width + r; x++)
                    if (map.Terrain(x, y) == TerrainType.Water && CellDistance(home, size, x + 0.5, y + 0.5) <= n.RiverReach)
                        return true;
            return false;
        }

        /// <summary>A finished market (stall or hall) within reach of the home.</summary>
        public bool HasMarket(PlacedBuilding home)
        {
            var n = Content.Balance.Needs;
            if (n == null) return true;
            var fp = FootprintOf(home);
            foreach (var (b, def) in OperationalBuildings())
                if (def.Market)
                {
                    var bf = FootprintOf(b);
                    if (CellDistance(home, (fp.Width, fp.Height), b.X + bf.Width / 2.0, b.Y + bf.Height / 2.0) <= n.MarketReach + Math.Max(bf.Width, bf.Height) / 2.0)
                        return true;
                }
            return false;
        }

        /// <summary>Every home, its tier, what it has and what it pays (the best homes fill first).</summary>
        public NeedsReport Needs()
        {
            if (stepNeeds != null) return stepNeeds;
            var n = Content.Balance.Needs;
            if (n == null) return new NeedsReport();
            // The UI asks every frame: keep the report until anything it depends on changes.
            long key = State.Population;
            foreach (var b in State.Buildings) key = key * 31 + b.Id * 7 + b.Level * 3 + (b.IsOperational ? 1 : 0);
            key = key * 31 + State.Decorations.Count;
            key = key * 31 + State.NextDecorationId;
            key = key * 31 + (State.FoodShortage ? 1 : 0) + (State.Resources.Goods > 1e-6 ? 2 : 0);
            key = key * 31 + (State.TaxLevel?.GetHashCode() ?? 0);
            if (cachedNeeds != null && key == cachedNeedsKey && ReferenceEquals(cachedNeedsOwner, State)) return cachedNeeds;
            cachedNeeds = BuildNeeds(n);
            cachedNeedsKey = key;
            cachedNeedsOwner = State;
            return cachedNeeds;
        }

        NeedsReport cachedNeeds;
        long cachedNeedsKey;
        object cachedNeedsOwner;

        NeedsReport BuildNeeds(NeedsConfig n)
        {
            var report = new NeedsReport();
            double taxRate = CurrentTax?.Multiplier ?? 1;
            bool food = !State.FoodShortage;
            bool goodsInStore = State.Resources.Goods > 1e-6;
            int left = State.Population;
            double goodsWanted = 0;
            var homes = OperationalBuildings().Where(x => HousingOf(x.building, x.def) > 0)
                .OrderByDescending(x => x.building.Level).ThenBy(x => x.building.Id).ToList();
            foreach (var (b, def) in homes)
            {
                var tier = TierFor(b.Level);
                int living = Math.Min(HousingOf(b, def), left);
                left -= living;
                var h = new HomeNeeds { Building = b, Tier = tier, Occupants = living, Food = food };
                h.Water = !tier.NeedsWater || HasWater(b);
                h.Market = !tier.NeedsGoods || HasMarket(b);
                h.Goods = !tier.NeedsGoods || (h.Market && goodsInStore);
                h.AllMet = h.Food && h.Water && h.Goods;
                h.TaxPerSecond = living * tier.TaxPerPersonPerHour / 3600.0 * (h.AllMet ? 1 : 0.5) * taxRate;
                report.Homes.Add(h);
                report.Housed += living;
                if (h.AllMet) report.Content += living;
                if (!h.Water) report.WithoutWater += living;
                if (!h.Goods) report.WithoutGoods += living;
                report.TaxPerSecond += h.TaxPerSecond;
                if (tier.NeedsGoods && h.Market) goodsWanted += living * n.GoodsPerPersonPerSecond;
            }
            report.GoodsPerSecond = goodsInStore ? goodsWanted : 0;
            return report;
        }

        /// <summary>Approval from needs met or missing, and from the tax level.</summary>
        void NeedsApproval(List<ApprovalFactor> list)
        {
            var n = Content.Balance.Needs;
            if (n == null) return;
            var r = Needs();
            if (r.Housed > 0)
            {
                double share = r.Content / (double)r.Housed;
                int value = (int)Math.Round(share * n.MetApproval - (1 - share) * n.UnmetApproval);
                if (value != 0)
                    list.Add(new ApprovalFactor(value > 0 ? $"Needs met ({r.Content}/{r.Housed})" : $"Needs not met ({r.Housed - r.Content}/{r.Housed})", value));
            }
            var tax = CurrentTax;
            if (tax != null && tax.Approval != 0) list.Add(new ApprovalFactor($"{tax.Name} taxes", tax.Approval));
        }

        /// <summary>What raising a home to <paramref name="level"/> needs nearby (water, a market).</summary>
        IEnumerable<RequirementStatus> HomeLevelRequirements(PlacedBuilding b, BuildingDefinition def, int level)
        {
            if (!HasNeeds || def.HousingValue <= 0) yield break;
            var tier = TierFor(level);
            var now = TierFor(b.Level);
            if (tier == null || tier == now) yield break;
            if (tier.NeedsWater) yield return new RequirementStatus { Text = $"{tier.Name}: a well or the river nearby", Met = HasWater(b) };
            if (tier.NeedsGoods) yield return new RequirementStatus { Text = $"{tier.Name}: a market nearby", Met = HasMarket(b) };
        }
    }
}
