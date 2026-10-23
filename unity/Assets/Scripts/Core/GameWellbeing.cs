using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>One line of the approval breakdown shown to the player.</summary>
    public struct ApprovalFactor
    {
        public string Label;
        public int Points;
        public ApprovalFactor(string label, int points) { Label = label; Points = points; }
    }

    /// <summary>
    /// Seasons and wellbeing (inspired by Manor Lords' approval and Foundation's needs):
    /// the year turns through four seasons that change foraging, farming, growth and the
    /// firewood people burn; approval (0–100) sums food security, food variety, warmth,
    /// housing quality and gathering places, and in turn speeds up or slows down work and
    /// growth. Everything is derived from the saved state, so it adds nothing to the save.
    /// </summary>
    public sealed partial class Game
    {
        int lastSeasonIndex = -1;
        bool wasFreezing;

        // MARK: - Seasons

        public bool HasSeasons => Content.Balance.SeasonSeconds > 0 && Content.Balance.Seasons.Count > 0;

        /// <summary>Index into the season list (0 when seasons are off).</summary>
        public int SeasonIndex => HasSeasons
            ? (int)(State.SimTime / Content.Balance.SeasonSeconds) % Content.Balance.Seasons.Count
            : 0;

        public SeasonDefinition Season => HasSeasons ? Content.Balance.Seasons[SeasonIndex] : null;

        /// <summary>0..1 progress through the current season.</summary>
        public double SeasonProgress => HasSeasons
            ? State.SimTime % Content.Balance.SeasonSeconds / Content.Balance.SeasonSeconds
            : 0;

        public int Year => HasSeasons
            ? (int)(State.SimTime / (Content.Balance.SeasonSeconds * Content.Balance.Seasons.Count)) + 1
            : 1;

        public double SecondsUntilNextSeason => HasSeasons
            ? Content.Balance.SeasonSeconds - State.SimTime % Content.Balance.SeasonSeconds
            : 0;

        double SeasonJobMultiplier(JobType job)
        {
            var s = Season;
            if (s == null) return 1;
            if (job == JobType.Foraging) return s.Foraging;
            if (job == JobType.Farming) return s.Farming;
            if (job == JobType.Fishing) return s.Fishing;
            if (job == JobType.Hunting) return s.Hunting;
            return 1;
        }

        /// <summary>Wood burnt per second to keep everyone warm this season.</summary>
        public double FuelPerSecond =>
            State.Population * (Season?.FuelPerPersonPerSecond ?? 0) * Multiplier(EffectKind.FuelMultiplier) + FiresFuelPerSecond();

        /// <summary>The season needs firewood and the woodpile is empty.</summary>
        public bool Freezing => FuelPerSecond > 0 && State.Resources.Wood <= 1e-6;

        // MARK: - Approval

        public List<ApprovalFactor> ApprovalFactors()
        {
            var list = new List<ApprovalFactor>();
            var econ = EconomyCore(1.0);

            // Food security
            if (State.FoodShortage) list.Add(new ApprovalFactor("Hunger", -30));
            else if (econ.Net.Food > 0.0005) list.Add(new ApprovalFactor("Food is plentiful", 10));
            else list.Add(new ApprovalFactor("Food is running low", -5));
            if (!State.FoodShortage && State.Resources.Food >= State.Population * 8)
                list.Add(new ApprovalFactor("Full stores", 5));

            // Food variety: each distinct source in use counts (foraging, fish, grain, herds…).
            var sources = new HashSet<string>();
            if (State.Assignments[JobType.Foraging] > 0) sources.Add("wild");
            if (State.Assignments[JobType.Farming] > 0) sources.Add("grain");
            if (State.Assignments[JobType.Fishing] > 0) sources.Add("fish");
            if (State.Assignments[JobType.Hunting] > 0) sources.Add("game");
            if (State.Assignments[JobType.Herding] > 0) sources.Add("livestock");
            foreach (var (_, def) in OperationalBuildings())
                if (!string.IsNullOrEmpty(def.FoodSource)) sources.Add(def.FoodSource);
            if (sources.Count > 1) list.Add(new ApprovalFactor($"Food variety ({sources.Count} kinds)", Math.Min(12, (sources.Count - 1) * 4)));

            // Warmth
            if (FuelPerSecond > 0)
                list.Add(Freezing ? new ApprovalFactor("Freezing: no firewood", -25) : new ApprovalFactor("Warm fires", 4));

            // Warmth of the homes (Frostpunk's heat)
            if (HasWarmth)
            {
                var warmth = Warmth();
                double pop = Math.Max(1, State.Population);
                int cold = warmth.People[(int)Exposure.Cold], freezing = warmth.Freezing;
                if (cold + freezing > 0)
                    list.Add(new ApprovalFactor($"Cold homes ({cold + freezing})", -(int)Math.Round(12 * cold / pop + 22 * freezing / pop)));
                else if (warmth.Temperature < Content.Balance.Warmth.Comfortable && warmth.People[(int)Exposure.Comfortable] == State.Population)
                    list.Add(new ApprovalFactor("Warm homes in the cold", 4));
            }
            if (HasHealth)
            {
                int sick = SickCount;
                if (sick > 0) list.Add(new ApprovalFactor($"Illness ({sick})", -Math.Min(10, 2 * sick)));
            }

            // Housing quality (people-weighted average comfort)
            int housed = 0, comfort = 0;
            foreach (var (b, def) in OperationalBuildings())
            {
                int homes = HousingOf(b, def);
                if (homes <= 0) continue;
                housed += homes;
                comfort += homes * ((def.Comfort ?? 0) + 3 * (b.Level - 1));
            }
            if (housed > 0)
            {
                int q = (int)Math.Round(comfort / (double)housed);
                if (q != 0) list.Add(new ApprovalFactor("Housing quality", Math.Min(15, q)));
            }

            NeedsApproval(list);
            double estates = EstatesApproval(list);

            // Gathering places
            int community = OperationalBuildings().Sum(x => x.def.Community ?? 0) + PartCommunity();
            if (community > 0) list.Add(new ApprovalFactor("Gathering places", Math.Min(15, community)));

            // Beauty and order: fences, lanterns, flowers, monuments (Foundation's splendor).
            int beauty = Beauty();
            if (beauty > 0) list.Add(new ApprovalFactor("Beauty of the town", Math.Min(15, beauty / 2)));

            // Customs, mourning, a roaring fire, promises kept and other events
            int laws = LawApproval();
            if (laws != 0) list.Add(new ApprovalFactor("Customs", laws));
            double Source(string id) => State.Modifiers.Where(m => m.SourceId == id && m.Effect.Kind == EffectKind.ApprovalBonus).Sum(m => m.Effect.Value ?? 0);
            double grief = Source("grief"), stoke = Source("stoke"), promise = Source("promise");
            if (Math.Abs(grief) > 0.5) list.Add(new ApprovalFactor("Mourning", (int)Math.Round(grief)));
            if (Math.Abs(stoke) > 0.5) list.Add(new ApprovalFactor("Roaring fire", (int)Math.Round(stoke)));
            if (Math.Abs(promise) > 0.5) list.Add(new ApprovalFactor("Promise kept", (int)Math.Round(promise)));
            double bonus = ActiveEffects().Where(e => e.Kind == EffectKind.ApprovalBonus).Sum(e => e.Value ?? 0) - laws - grief - stoke - promise - estates;
            if (Math.Abs(bonus) > 0.5) list.Add(new ApprovalFactor("Recent events", (int)Math.Round(bonus)));

            // Discontent drags hope down
            if (HasDiscontent && State.Discontent >= Content.Balance.Discontent.Grumbling)
                list.Add(new ApprovalFactor(DiscontentMood, -(int)Math.Round((State.Discontent - Content.Balance.Discontent.Grumbling) / 5)));

            return list;
        }

        public const int ApprovalBase = 40;

        public int Approval
        {
            get
            {
                // Asked for many times a frame by the UI (every job's rate): kept until something it depends on changes.
                var st = State;
                var key = (st.SimTime, ActiveEffects(), st.Resources, st.Population, st.Assignments.Version, st.Buildings.Count, st.NextBuildingId,
                           st.Decorations.Count, st.NextDecorationId, st.Discontent, st.FoodShortage, st, st.Modifiers.Count);
                if (approvalCache.HasValue && approvalKey.Equals(key)) return approvalCache.Value;
                int value = Math.Max(0, Math.Min(100, ApprovalBase + ApprovalFactors().Sum(f => f.Points)));
                approvalCache = value;
                approvalKey = key;
                return value;
            }
        }

        int? approvalCache;
        (double, List<Effect>, ResourceBundle, int, int, int, int, int, int, double, bool, GameState, int) approvalKey;

        /// <summary>Work speed from approval: 0.85 at 0 %, 1.0 at 50 %, 1.15 at 100 %.</summary>
        public double ApprovalProductivity(int approval) => Content.Balance.ApprovalAffectsWork ? 0.85 + 0.003 * approval : 1.0;

        /// <summary>Growth speed from approval: none below 30 %, 1.0 at 50 %, 1.5 at 100 %.</summary>
        public double ApprovalGrowth(int approval) =>
            !Content.Balance.ApprovalAffectsWork ? 1.0 : approval < 30 ? 0 : 0.5 + approval / 100.0;

        // MARK: - Season and warmth notices

        void TrackSeason()
        {
            if (HasSeasons)
            {
                int index = SeasonIndex;
                if (lastSeasonIndex >= 0 && index != lastSeasonIndex)
                {
                    var s = Season;
                    Notice(NoticeKind.Info, string.IsNullOrEmpty(s.Description) ? $"{s.Name} has come." : $"{s.Name} has come. {s.Description}");
                }
                lastSeasonIndex = index;
            }
            bool freezing = Freezing;
            if (freezing != wasFreezing)
            {
                wasFreezing = freezing;
                if (freezing) Notice(NoticeKind.Warning, "The woodpile is empty and people are freezing! Gather wood.");
                else Notice(NoticeKind.Success, "The fires are burning again.");
            }
        }
    }
}
