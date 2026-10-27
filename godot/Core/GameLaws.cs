using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    public enum LawState { Signed, Available, Locked }

    /// <summary>Where a law stands and, if it cannot be signed, why not.</summary>
    public sealed class LawStatus
    {
        public LawDefinition Law;
        public LawState State;
        public List<string> Reasons = new List<string>();
    }

    /// <summary>One line of the discontent breakdown (points per hour).</summary>
    public struct DiscontentFactor
    {
        public string Label;
        public double PerHour;
        public DiscontentFactor(string label, double perHour) { Label = label; PerHour = perHour; }
    }

    /// <summary>
    /// The Book of Customs (Frostpunk's Book of Laws), discontent and the people's petitions.
    /// Laws are signed one at a time with a pause between them and last for good; many trade
    /// work or food for content, or the other way round. Discontent rises with hunger, cold,
    /// illness, homelessness and harsh laws and settles when people are content. When it runs high
    /// the people petition, and a promise kept calms them while a broken one enrages them. At the
    /// very top families leave.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasDiscontent => Content.Balance.Discontent != null;

        // MARK: - Laws

        public bool LawSigned(string id) => State.Laws.Contains(id);

        public double LawCooldownLeft
        {
            get
            {
                var d = Content.Balance.Discontent;
                if (d == null || !State.LastLawSimTime.HasValue) return 0;
                return Math.Max(0, State.LastLawSimTime.Value + d.LawCooldownSeconds - State.SimTime);
            }
        }

        public LawStatus LawStatusOf(LawDefinition law)
        {
            var s = new LawStatus { Law = law };
            if (LawSigned(law.Id)) { s.State = LawState.Signed; return s; }
            if (HeartLevel < law.HeartLevel) s.Reasons.Add($"{HeartName} level {law.HeartLevel}");
            if (law.RequiresLaw != null && !LawSigned(law.RequiresLaw))
                s.Reasons.Add($"Sign {Content.Law(law.RequiresLaw)?.Name ?? law.RequiresLaw} first");
            if (law.RequiresBuilding != null && !OperationalBuildings().Any(x => x.def.Id == law.RequiresBuilding))
                s.Reasons.Add($"Needs a {Content.Building(law.RequiresBuilding)?.Name ?? law.RequiresBuilding}");
            foreach (var other in State.Laws)
            {
                var o = Content.Law(other);
                if (o == null) continue;
                if (law.Excludes.Contains(other) || o.Excludes.Contains(law.Id)) s.Reasons.Add($"Ruled out by {o.Name}");
            }
            s.State = s.Reasons.Count == 0 ? LawState.Available : LawState.Locked;
            return s;
        }

        public List<LawStatus> LawStatuses() => Content.Laws.Select(LawStatusOf).ToList();

        /// <summary>Signs a law for good.</summary>
        public GameResult SignLaw(string id)
        {
            Update();
            var law = Content.Law(id);
            if (law == null) return GameError.UnknownContent(id);
            var status = LawStatusOf(law);
            if (status.State == LawState.Signed) return GameError.NotReady($"{law.Name} is already the custom.");
            if (status.State == LawState.Locked) return GameError.RequirementsNotMet(status.Reasons);
            if (LawCooldownLeft > 0) return GameError.NotReady($"The people need time before the next law ({Duration(LawCooldownLeft)}).");
            State.Laws.Add(law.Id);
            State.LastLawSimTime = State.SimTime;
            if (HasDiscontent) AddDiscontent(law.OnSign);
            Notice(NoticeKind.Milestone, $"New custom: {law.Name}.");
            AddLegacy("law", $"Custom: {law.Name}", law.LegacyText ?? law.Description, law.Id);
            EvaluateProgress();
            return GameResult.Success;
        }

        IEnumerable<Effect> LawEffects()
        {
            foreach (var id in State.Laws)
            {
                var law = Content.Law(id);
                if (law == null) continue;
                foreach (var e in law.Effects) yield return e;
            }
        }

        /// <summary>Lasting approval from the signed laws.</summary>
        int LawApproval() => (int)Math.Round(LawEffects().Where(e => e.Kind == EffectKind.ApprovalBonus).Sum(e => e.Value ?? 0));

        // MARK: - Discontent

        internal void AddDiscontent(double points) => State.Discontent = Math.Max(0, Math.Min(100, State.Discontent + points));

        public List<DiscontentFactor> DiscontentFactors()
        {
            var list = new List<DiscontentFactor>();
            var d = Content.Balance.Discontent;
            if (d == null) return list;
            double pop = Math.Max(1, State.Population);
            if (State.FoodShortage) list.Add(new DiscontentFactor("Hunger", d.Hunger));
            var warmth = Warmth();
            int cold = warmth.People[(int)Exposure.Cold], freezing = warmth.Freezing;
            if (cold > 0) list.Add(new DiscontentFactor($"Cold homes ({cold})", d.Cold * cold / pop));
            if (freezing > 0) list.Add(new DiscontentFactor($"Freezing ({freezing})", d.Freezing * freezing / pop));
            int sick = SickCount;
            if (sick > 0) list.Add(new DiscontentFactor($"Illness ({sick})", d.Sick * sick / pop));
            int homeless = Math.Max(0, State.Population - HousingCapacity());
            if (homeless > 0) list.Add(new DiscontentFactor($"No home ({homeless})", d.Homeless * homeless / pop));
            double laws = 0;
            foreach (var e in ActiveEffects()) if (e.Kind == EffectKind.DiscontentPerHour) laws += e.Value ?? 0;
            if (Math.Abs(laws) > 0.01) list.Add(new DiscontentFactor(laws > 0 ? "Hard customs" : "Calming customs", laws));
            list.Add(new DiscontentFactor("Time heals", -d.Calm));
            int approval = CurrentApproval;
            if (approval > d.CalmAbove) list.Add(new DiscontentFactor("Content people", -(approval - d.CalmAbove) * d.CalmPerApproval));
            return list;
        }

        /// <summary>Points per hour discontent is changing by now.</summary>
        public double DiscontentPerHour => DiscontentFactors().Sum(f => f.PerHour);

        public string DiscontentMood
        {
            get
            {
                var d = Content.Balance.Discontent;
                if (d == null) return "Calm";
                if (State.Discontent >= 100) return "Revolt";
                if (State.Discontent >= d.Unrest) return "Unrest";
                if (State.Discontent >= d.Grumbling) return "Grumbling";
                return "Calm";
            }
        }

        /// <summary>Work speed under discontent.</summary>
        public double DiscontentWork
        {
            get
            {
                var d = Content.Balance.Discontent;
                if (d == null) return 1;
                if (State.Discontent >= d.Unrest) return d.UnrestWork;
                if (State.Discontent >= d.Grumbling) return d.GrumblingWork;
                return 1;
            }
        }

        /// <summary>No family grows while there is unrest.</summary>
        bool UnrestStopsGrowth => HasDiscontent && State.Discontent >= Content.Balance.Discontent.Unrest;

        void AdvanceDiscontent(double dt)
        {
            var d = Content.Balance.Discontent;
            if (d == null) return;
            AddDiscontent(DiscontentPerHour * dt / 3600);
            if (State.Discontent >= 100 - 1e-9 && State.Population > 2)
            {
                State.LeaveProgress += dt / Math.Max(1, d.LeaveSeconds);
                // Families leave only while the player is there to see it coming.
                if (simulatingOffline) State.LeaveProgress = Math.Min(State.LeaveProgress, 0.95);
                if (State.LeaveProgress >= 1)
                {
                    State.LeaveProgress = 0;
                    int leaving = Math.Min(2, State.Population - 2);
                    for (int i = 0; i < leaving; i++)
                    {
                        var p = State.People.LastOrDefault(x => x.Job == null && !x.Sick) ?? State.People.LastOrDefault(x => !x.Sick) ?? State.People.Last();
                        RemovePerson(p);
                    }
                    AddDiscontent(-10);
                    Notice(NoticeKind.Warning, $"A family has had enough and left the village ({leaving} people).");
                    AddLegacy("leave", "A family left", "Angry and cold, a family walked away into the wilds. The village remembered why.", null);
                }
            }
            else State.LeaveProgress = 0;
        }

        // MARK: - Petitions

        public PetitionDefinition PendingPetition => State.PendingPetition == null ? null : Content.Petition(State.PendingPetition);

        public PetitionDefinition ActivePetitionDefinition => State.ActivePetition == null ? null : Content.Petition(State.ActivePetition.PetitionId);

        public List<RequirementStatus> PetitionGoalStatus(PetitionDefinition p) => Status(p.Goal);

        void AdvancePetitions()
        {
            var d = Content.Balance.Discontent;
            if (d == null) return;
            var active = ActivePetitionDefinition;
            if (State.ActivePetition != null && active == null) State.ActivePetition = null;
            if (active != null)
            {
                if (active.Goal.All(IsMet))
                {
                    State.ActivePetition = null;
                    AddDiscontent(-d.KeptRelief);
                    if (Math.Abs(d.KeptApproval) > 0.01)
                        State.Modifiers.Add(new ActiveModifier
                        {
                            SourceId = "promise", Effect = new Effect(EffectKind.ApprovalBonus) { Value = d.KeptApproval },
                            ExpiresAtSimTime = State.SimTime + d.KeptSeconds,
                        });
                    Notice(NoticeKind.Success, $"Promise kept: {active.Promise} The people trust you more.");
                    AddLegacy("promise", $"Promise kept: {active.Title}", active.Promise, active.Id);
                }
                else if (State.SimTime >= State.ActivePetition.DeadlineSimTime)
                {
                    State.ActivePetition = null;
                    AddDiscontent(d.BrokenAnger);
                    Notice(NoticeKind.Warning, $"Promise broken: {active.Title}. Discontent rises.");
                    AddLegacy("promise", $"Promise broken: {active.Title}", "Words were given and not kept; the village did not forget.", active.Id);
                }
                return;
            }
            if (State.PendingPetition != null || State.PendingEventId != null || State.Discontent < d.PetitionAt) return;
            if (State.LastPetitionSimTime.HasValue && State.SimTime - State.LastPetitionSimTime.Value < d.PetitionCooldownSeconds) return;
            // The first demand that is not already met; they take turns.
            int start = State.LastPetitionSimTime.HasValue ? (int)(State.LastPetitionSimTime.Value / Math.Max(1, d.PetitionCooldownSeconds)) : 0;
            for (int i = 0; i < Content.Petitions.Count; i++)
            {
                var p = Content.Petitions[(start + i) % Content.Petitions.Count];
                if (p.Goal.All(IsMet)) continue;
                State.PendingPetition = p.Id;
                State.LastPetitionSimTime = State.SimTime;
                Notice(NoticeKind.Warning, $"The people petition you: {p.Title}");
                return;
            }
        }

        /// <summary>Promise what the people ask (they calm down now and expect it by the deadline) or refuse.</summary>
        public GameResult AnswerPetition(bool promise)
        {
            Update();
            var p = PendingPetition;
            var d = Content.Balance.Discontent;
            if (p == null || d == null) return GameError.NoPendingEvent;
            State.PendingPetition = null;
            if (promise)
            {
                State.ActivePetition = new ActivePetition { PetitionId = p.Id, DeadlineSimTime = State.SimTime + p.DeadlineSeconds };
                AddDiscontent(-d.PromiseRelief);
                Notice(NoticeKind.Info, $"You promised: {p.Promise} ({Duration(p.DeadlineSeconds)}).");
            }
            else
            {
                AddDiscontent(d.RefusedAnger);
                Notice(NoticeKind.Warning, "You turned the people away. Discontent rises.");
            }
            AdvancePetitions(); // a promise that is already as good as kept
            return GameResult.Success;
        }
    }
}
