using System;
using System.Collections.Generic;
using System.Globalization;

namespace Founders.Core
{
    /// <summary>Resources. Ore (from mines) and hides (from hunters) join the first four; bronze, planks and tools
    /// are processed goods (made by recipes, see <see cref="JobConfig.Inputs"/>). Household goods are what homes of the
    /// higher tiers use up; coins come from taxes and trade (kept in the treasury, not the storehouse).</summary>
    public enum ResourceType { Food, Wood, Stone, Knowledge, Ore, Hides, Bronze, Planks, Tools, Goods, Flour, Coins }

    public static class ResourceTypes
    {
        public static readonly ResourceType[] All =
            { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge, ResourceType.Ore, ResourceType.Hides, ResourceType.Bronze,
              ResourceType.Planks, ResourceType.Tools, ResourceType.Goods, ResourceType.Flour, ResourceType.Coins };

        /// <summary>Material goods kept in storage (everything except knowledge).</summary>
        public static readonly ResourceType[] Stored =
            { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Ore, ResourceType.Hides, ResourceType.Bronze, ResourceType.Planks, ResourceType.Tools,
              ResourceType.Goods, ResourceType.Flour };

        public static string Id(this ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Food: return "food";
                case ResourceType.Wood: return "wood";
                case ResourceType.Stone: return "stone";
                case ResourceType.Ore: return "ore";
                case ResourceType.Hides: return "hides";
                case ResourceType.Bronze: return "bronze";
                case ResourceType.Planks: return "planks";
                case ResourceType.Tools: return "tools";
                case ResourceType.Goods: return "goods";
                case ResourceType.Flour: return "flour";
                case ResourceType.Coins: return "coins";
                default: return "knowledge";
            }
        }

        public static string DisplayName(this ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Food: return "Food";
                case ResourceType.Wood: return "Wood";
                case ResourceType.Stone: return "Stone";
                case ResourceType.Ore: return "Ore";
                case ResourceType.Hides: return "Hides";
                case ResourceType.Bronze: return "Bronze";
                case ResourceType.Planks: return "Planks";
                case ResourceType.Tools: return "Tools";
                case ResourceType.Goods: return "Goods";
                case ResourceType.Flour: return "Flour";
                case ResourceType.Coins: return "Coins";
                default: return "Knowledge";
            }
        }

        public static ResourceType Parse(string id)
        {
            foreach (var t in All) if (t.Id() == id) return t;
            throw new FormatException($"Unknown resource '{id}'.");
        }
    }

    /// <summary>An amount of each resource. Used for stockpiles, costs, rewards and rates.
    /// Missing keys read as zero so content JSON stays short.</summary>
    public struct ResourceBundle : IEquatable<ResourceBundle>
    {
        public double Food, Wood, Stone, Knowledge, Ore, Hides, Bronze, Planks, Tools, Goods, Flour, Coins;

        public const double Epsilon = 1e-9;
        public static readonly ResourceBundle Zero = new ResourceBundle();

        public ResourceBundle(double food = 0, double wood = 0, double stone = 0, double knowledge = 0, double ore = 0, double hides = 0, double bronze = 0,
                              double planks = 0, double tools = 0, double goods = 0, double coins = 0, double flour = 0)
        {
            Food = food; Wood = wood; Stone = stone; Knowledge = knowledge; Ore = ore; Hides = hides; Bronze = bronze; Planks = planks; Tools = tools;
            Goods = goods; Coins = coins; Flour = flour;
        }

        public double this[ResourceType t]
        {
            get
            {
                switch (t)
                {
                    case ResourceType.Food: return Food;
                    case ResourceType.Wood: return Wood;
                    case ResourceType.Stone: return Stone;
                    case ResourceType.Ore: return Ore;
                    case ResourceType.Hides: return Hides;
                    case ResourceType.Bronze: return Bronze;
                    case ResourceType.Planks: return Planks;
                    case ResourceType.Tools: return Tools;
                    case ResourceType.Goods: return Goods;
                    case ResourceType.Flour: return Flour;
                    case ResourceType.Coins: return Coins;
                    default: return Knowledge;
                }
            }
            set
            {
                switch (t)
                {
                    case ResourceType.Food: Food = value; break;
                    case ResourceType.Wood: Wood = value; break;
                    case ResourceType.Stone: Stone = value; break;
                    case ResourceType.Ore: Ore = value; break;
                    case ResourceType.Hides: Hides = value; break;
                    case ResourceType.Bronze: Bronze = value; break;
                    case ResourceType.Planks: Planks = value; break;
                    case ResourceType.Tools: Tools = value; break;
                    case ResourceType.Goods: Goods = value; break;
                    case ResourceType.Flour: Flour = value; break;
                    case ResourceType.Coins: Coins = value; break;
                    default: Knowledge = value; break;
                }
            }
        }

        /// <summary>A bundle of one resource.</summary>
        public static ResourceBundle Of(ResourceType t, double amount)
        {
            var b = new ResourceBundle();
            b[t] = amount;
            return b;
        }

        public bool IsZero
        {
            get
            {
                foreach (var t in ResourceTypes.All) if (Math.Abs(this[t]) >= Epsilon) return false;
                return true;
            }
        }

        public bool IsValid
        {
            get
            {
                foreach (var t in ResourceTypes.All)
                    if (double.IsNaN(this[t]) || double.IsInfinity(this[t]) || this[t] < 0) return false;
                return true;
            }
        }

        /// <summary>Non-zero entries in canonical order.</summary>
        public List<KeyValuePair<ResourceType, double>> Entries
        {
            get
            {
                var list = new List<KeyValuePair<ResourceType, double>>();
                foreach (var t in ResourceTypes.All)
                    if (Math.Abs(this[t]) >= Epsilon) list.Add(new KeyValuePair<ResourceType, double>(t, this[t]));
                return list;
            }
        }

        /// <summary>True when every resource in <paramref name="cost"/> is available.</summary>
        public bool Covers(ResourceBundle cost)
        {
            foreach (var t in ResourceTypes.All) if (this[t] + Epsilon < cost[t]) return false;
            return true;
        }

        /// <summary>How much is missing to pay <paramref name="cost"/>.</summary>
        public ResourceBundle Shortfall(ResourceBundle cost)
        {
            var missing = new ResourceBundle();
            foreach (var t in ResourceTypes.All) missing[t] = Math.Max(0, cost[t] - this[t]);
            return missing;
        }

        public static ResourceBundle operator +(ResourceBundle a, ResourceBundle b)
        {
            var r = a;
            foreach (var t in ResourceTypes.All) r[t] += b[t];
            return r;
        }

        public static ResourceBundle operator -(ResourceBundle a, ResourceBundle b)
        {
            var r = a;
            foreach (var t in ResourceTypes.All) r[t] -= b[t];
            return r;
        }

        public static ResourceBundle operator *(ResourceBundle a, double k)
        {
            var r = a;
            foreach (var t in ResourceTypes.All) r[t] *= k;
            return r;
        }

        /// <summary>Human readable, e.g. "12 Wood  5 Stone".</summary>
        public string ShortDescription
        {
            get
            {
                var parts = new List<string>();
                foreach (var e in Entries)
                    parts.Add($"{Math.Round(e.Value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)} {e.Key.DisplayName()}");
                return parts.Count == 0 ? "Free" : string.Join("  ", parts);
            }
        }

        public bool Equals(ResourceBundle o) =>
            Food == o.Food && Wood == o.Wood && Stone == o.Stone && Knowledge == o.Knowledge && Ore == o.Ore && Hides == o.Hides && Bronze == o.Bronze
            && Planks == o.Planks && Tools == o.Tools && Goods == o.Goods && Flour == o.Flour && Coins == o.Coins;
        public override bool Equals(object obj) => obj is ResourceBundle o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(HashCode.Combine(Food, Wood, Stone, Knowledge, Ore, Hides, Bronze), Planks, Tools, Goods, HashCode.Combine(Flour, Coins));
        public static bool operator ==(ResourceBundle a, ResourceBundle b) => a.Equals(b);
        public static bool operator !=(ResourceBundle a, ResourceBundle b) => !a.Equals(b);
        public override string ToString() => $"(food {Food}, wood {Wood}, stone {Stone}, knowledge {Knowledge}, ore {Ore}, hides {Hides}, bronze {Bronze}, planks {Planks}, tools {Tools}, goods {Goods}, flour {Flour}, coins {Coins})";

        public static ResourceBundle FromJson(JObj o)
        {
            if (o == null) return Zero;
            return new ResourceBundle(o.OptNum("food") ?? 0, o.OptNum("wood") ?? 0, o.OptNum("stone") ?? 0, o.OptNum("knowledge") ?? 0,
                                      o.OptNum("ore") ?? 0, o.OptNum("hides") ?? 0, o.OptNum("bronze") ?? 0,
                                      o.OptNum("planks") ?? 0, o.OptNum("tools") ?? 0,
                                      o.OptNum("goods") ?? 0, o.OptNum("coins") ?? 0, o.OptNum("flour") ?? 0);
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { ["food"] = Food, ["wood"] = Wood, ["stone"] = Stone, ["knowledge"] = Knowledge };
            // Newer resources are written only when present (older saves and content stay identical).
            if (Ore != 0) d["ore"] = Ore;
            if (Hides != 0) d["hides"] = Hides;
            if (Bronze != 0) d["bronze"] = Bronze;
            if (Planks != 0) d["planks"] = Planks;
            if (Tools != 0) d["tools"] = Tools;
            if (Goods != 0) d["goods"] = Goods;
            if (Flour != 0) d["flour"] = Flour;
            if (Coins != 0) d["coins"] = Coins;
            return d;
        }
    }
}
