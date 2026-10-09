using System;
using System.Collections.Generic;
using System.Globalization;

namespace Eonward.Core
{
    /// <summary>The four primary resources of the first epoch.</summary>
    public enum ResourceType { Food, Wood, Stone, Knowledge }

    public static class ResourceTypes
    {
        public static readonly ResourceType[] All = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge };

        public static string Id(this ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Food: return "food";
                case ResourceType.Wood: return "wood";
                case ResourceType.Stone: return "stone";
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
        public double Food, Wood, Stone, Knowledge;

        public const double Epsilon = 1e-9;
        public static readonly ResourceBundle Zero = new ResourceBundle();

        public ResourceBundle(double food = 0, double wood = 0, double stone = 0, double knowledge = 0)
        {
            Food = food; Wood = wood; Stone = stone; Knowledge = knowledge;
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
                    default: Knowledge = value; break;
                }
            }
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

        public bool Equals(ResourceBundle o) => Food == o.Food && Wood == o.Wood && Stone == o.Stone && Knowledge == o.Knowledge;
        public override bool Equals(object obj) => obj is ResourceBundle o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Food, Wood, Stone, Knowledge);
        public static bool operator ==(ResourceBundle a, ResourceBundle b) => a.Equals(b);
        public static bool operator !=(ResourceBundle a, ResourceBundle b) => !a.Equals(b);
        public override string ToString() => $"(food {Food}, wood {Wood}, stone {Stone}, knowledge {Knowledge})";

        public static ResourceBundle FromJson(JObj o)
        {
            if (o == null) return Zero;
            return new ResourceBundle(o.OptNum("food") ?? 0, o.OptNum("wood") ?? 0, o.OptNum("stone") ?? 0, o.OptNum("knowledge") ?? 0);
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["food"] = Food, ["wood"] = Wood, ["stone"] = Stone, ["knowledge"] = Knowledge,
        };
    }
}
