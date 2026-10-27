using System;
using System.Linq;
using Founders.Core;

namespace Founders.UI
{
    /// <summary>
    /// The Estates tab of the People window: the Commons, the Clergy and the Crown, their favour and where it comes
    /// from, the boons of each rank, and a gift of coins to raise one that lags behind.
    /// </summary>
    public partial class GameUi
    {
        string EstatesKey() =>
            string.Join(",", G.Estates.Select(e => G.EstateRank(e))) + "|" +
            (G.State.Resources.Coins >= (G.Content.Balance.Estates?.GrantCost ?? 0) ? "1" : "0") + "|" +
            string.Join(",", G.ApprovalFactors().Where(f => f.Label.Contains("estates") || f.Label.Contains("forgotten")).Select(f => f.Label + f.Points));

        void BuildEstates(VisualElement host)
        {
            var cfg = G.Content.Balance.Estates;
            if (cfg == null) return;
            var intro = E.Inset().Col().Margin(0, 0, 8);
            intro.Add(E.Ink("Keep all three estates in favour. One left two ranks behind feels forgotten and hope falls; " +
                            "all three standing together lift it.", Theme.Body, Theme.Pale));
            foreach (var f in G.ApprovalFactors().Where(f => f.Label.Contains("estates") || f.Label.Contains("forgotten")))
                intro.Add(E.StatRow(null, f.Label, (f.Points > 0 ? "+" : "−") + Math.Abs(f.Points), null, false, f.Points > 0 ? Theme.Up : Theme.Bad));
            host.Add(intro);

            foreach (var estate in G.Estates)
            {
                var e = estate;
                int rank = G.EstateRank(e);
                var card = Block(e.Name, out var rows, E.T($"Rank {rank}/{e.Ranks.Count}", Theme.Num, rank > 0 ? Theme.Gold : Theme.Pale));
                rows.Add(E.Ink(e.Description, Theme.Body).Margin(6, 0, 4));
                var next = G.NextRank(e);
                if (next != null)
                {
                    double from = rank > 0 ? e.Ranks[rank - 1].Favour : 0;
                    rows.Add(LiveBar(() => (G.Favour(e) - from) / Math.Max(1, next.Favour - from), 14).Margin(4, 0, 4));
                    var line = E.Line(false);
                    line.Add(E.RowIcon(e.Icon));
                    line.Add(Live(() => $"Favour {Math.Floor(G.Favour(e)):0}/{next.Favour:0}", Theme.Body).Flex());
                    line.Add(Live(() => $"+{G.FavourPerHour(e):0.#}/h", Theme.Num, Theme.Up));
                    rows.Add(line);
                }
                for (int i = 0; i < e.Ranks.Count; i++)
                {
                    var r = e.Ranks[i];
                    rows.Add(RequirementRow(i < rank ? "check" : "lock", $"{r.Name} · {r.Description}", i < rank, null, true));
                }
                if (next != null)
                {
                    bool can = G.State.Resources.Coins >= cfg.GrantCost;
                    rows.Add(E.Button($"Gift {cfg.GrantCost:0} coins (+{cfg.GrantFavour:0} favour)", can ? Act(() => G.GrantFavour(e.Id)) : null,
                                      can ? E.Kind.Orange : E.Kind.Grey, 44, "coins").Margin(8, 0, 4));
                }
                host.Add(card);
            }
        }
    }
}
