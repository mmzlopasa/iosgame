using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// The three estates of the realm (Manor Lords' development paths): the commons who work, the clergy who pray
    /// and teach, and the crown that taxes and trades. Each wins favour from what the town does for it (workers,
    /// shrines and churches, taxes and trade routes) or from gifts of coins; every rank of favour grants a lasting
    /// boon. An estate left far behind the others grumbles; all three standing together lift the town.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasEstates => Content.Balance.Estates != null;

        public IReadOnlyList<EstateDefinition> Estates => Content.Balance.Estates?.Estates ?? new List<EstateDefinition>();

        public EstateDefinition Estate(string id) => Estates.FirstOrDefault(e => e.Id == id);

        public double Favour(EstateDefinition e) => State.Favour.TryGetValue(e.Id, out var f) ? f : 0;

        /// <summary>Ranks reached (0 = none yet).</summary>
        public int EstateRank(EstateDefinition e)
        {
            double f = Favour(e);
            int rank = 0;
            foreach (var r in e.Ranks) if (f >= r.Favour - 1e-9) rank++;
            return rank;
        }

        /// <summary>The next rank to reach, or null at the top.</summary>
        public EstateRank NextRank(EstateDefinition e)
        {
            int rank = EstateRank(e);
            return rank < e.Ranks.Count ? e.Ranks[rank] : null;
        }

        /// <summary>Favour an estate wins each hour from the town as it stands.</summary>
        public double FavourPerHour(EstateDefinition e)
        {
            double f = 0;
            if (e.Buildings.Count > 0)
                foreach (var (_, def) in OperationalBuildings())
                    if (e.Buildings.TryGetValue(def.Id, out var v)) f += v;
            foreach (var kv in e.Jobs) f += ActiveWorkers(kv.Key) * kv.Value;
            if (e.TaxShare > 0 && HasNeeds) f += Needs().TaxPerSecond * 3600 * e.TaxShare;
            if (e.RouteFavour > 0) f += Math.Min(State.TradeRoutes.Count, TradeRouteSlots()) * e.RouteFavour;
            return f;
        }

        /// <summary>A gift of coins to an estate for its favour.</summary>
        public GameResult GrantFavour(string estateId)
        {
            Update();
            var cfg = Content.Balance.Estates;
            var e = Estate(estateId);
            if (cfg == null || e == null) return GameError.UnknownContent(estateId);
            if (NextRank(e) == null) return GameError.NotReady($"The {e.Name} want for nothing more.");
            if (State.Resources.Coins < cfg.GrantCost - 1e-9)
                return GameError.Unaffordable(ResourceBundle.Of(ResourceType.Coins, cfg.GrantCost - State.Resources.Coins));
            State.Resources.Coins -= cfg.GrantCost;
            AddFavour(e, cfg.GrantFavour);
            return GameResult.Success;
        }

        void AddFavour(EstateDefinition e, double amount)
        {
            if (e.Ranks.Count == 0 || amount <= 0) return;
            int before = EstateRank(e);
            State.Favour[e.Id] = Math.Min(e.Ranks[e.Ranks.Count - 1].Favour, Favour(e) + amount);
            int after = EstateRank(e);
            for (int r = before; r < after; r++)
                Notice(NoticeKind.Success, $"The {e.Name} grant {e.Ranks[r].Name}: {e.Ranks[r].Description}");
        }

        void AdvanceEstates(double dt)
        {
            if (!HasEstates) return;
            foreach (var e in Estates)
            {
                double f = FavourPerHour(e);
                if (f > 0) AddFavour(e, f / 3600 * dt);
            }
        }

        /// <summary>The boons of every rank the estates have granted.</summary>
        IEnumerable<Effect> EstateEffects()
        {
            if (!HasEstates) yield break;
            foreach (var e in Estates)
            {
                int rank = EstateRank(e);
                for (int i = 0; i < rank; i++)
                    foreach (var fx in e.Ranks[i].Effects) yield return fx;
            }
        }

        /// <summary>Approval from the estates' boons, and from their balance (one left behind, or all together).</summary>
        double EstatesApproval(List<ApprovalFactor> list)
        {
            var cfg = Content.Balance.Estates;
            if (cfg == null || cfg.Estates.Count == 0) return 0;
            double boons = EstateEffects().Where(x => x.Kind == EffectKind.ApprovalBonus).Sum(x => x.Value ?? 0);
            if (Math.Abs(boons) > 0.5) list.Add(new ApprovalFactor("Favour of the estates", (int)Math.Round(boons)));
            var ranks = cfg.Estates.Select(e => (e, rank: EstateRank(e))).ToList();
            int top = ranks.Max(x => x.rank), low = ranks.Min(x => x.rank);
            if (top - low >= cfg.ImbalanceRanks && cfg.ImbalanceApproval != 0)
            {
                var left = ranks.Where(x => x.rank == low).Select(x => x.e.Name);
                list.Add(new ApprovalFactor($"The {string.Join(" and the ", left)} feel forgotten", -cfg.ImbalanceApproval));
            }
            else if (low >= 1 && top - low <= 1 && cfg.HarmonyApproval != 0)
                list.Add(new ApprovalFactor("The estates stand together", cfg.HarmonyApproval));
            return boons;
        }
    }
}
