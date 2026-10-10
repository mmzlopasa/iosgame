using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Founders.Core
{
    /// <summary>A condition with a player-facing description and whether it is met.</summary>
    public struct RequirementStatus
    {
        public string Text;
        public bool Met;
    }

    public enum ResearchState { Researched, InProgress, Available, Blocked }

    public struct ResearchStatus
    {
        public ResearchState State;
        public double Progress;
        public List<string> Missing;
    }

    public sealed partial class Game
    {
        // MARK: - Conditions

        public bool IsMet(Condition c)
        {
            double amount = c.Amount ?? 1;
            string id = c.Id ?? "";
            switch (c.Kind)
            {
                case ConditionKind.ResourceAtLeast:
                    return c.Resource.HasValue && State.Resources[c.Resource.Value] + 1e-9 >= amount;
                case ConditionKind.BuildingCount: return Count(id) >= amount;
                case ConditionKind.HousingAtLeast: return HousingCapacity() >= amount;
                case ConditionKind.PopulationAtLeast: return State.Population >= amount;
                case ConditionKind.TechResearched: return State.Researched.Contains(id);
                case ConditionKind.TechCountAtLeast: return State.Researched.Count >= amount;
                case ConditionKind.EventResolved: return State.ResolvedEvents.ContainsKey(id);
                case ConditionKind.ObjectiveCompleted: return State.CompletedObjectives.Contains(id);
                case ConditionKind.MilestoneReached: return State.ReachedMilestones.Contains(id);
                case ConditionKind.RegionExplored: return State.ExploredRegions.Contains(id);
                case ConditionKind.DistinctJobsAtLeast: return Enums.Jobs.Count(j => State.Assignments[j] > 0) >= amount;
                case ConditionKind.FoodBalancePositive: return Economy().Net.Food > 0;
                default: return false;
            }
        }

        public string Describe(Condition c)
        {
            int amount = (int)Math.Round(c.Amount ?? 1, MidpointRounding.AwayFromZero);
            string id = c.Id ?? "";
            switch (c.Kind)
            {
                case ConditionKind.ResourceAtLeast:
                {
                    var r = c.Resource ?? ResourceType.Food;
                    return $"{r.DisplayName()} {(int)Math.Floor(State.Resources[r])}/{amount}";
                }
                case ConditionKind.BuildingCount:
                    return $"{Content.Building(id)?.Name ?? (c.Id ?? "?")} {Count(id)}/{amount}";
                case ConditionKind.HousingAtLeast: return $"Housing {HousingCapacity()}/{amount}";
                case ConditionKind.PopulationAtLeast: return $"Population {State.Population}/{amount}";
                case ConditionKind.TechResearched: return $"Research {Content.Technology(id)?.Name ?? "?"}";
                case ConditionKind.TechCountAtLeast: return $"Technologies {State.Researched.Count}/{amount}";
                case ConditionKind.EventResolved: return $"Decide: {Content.Event(id)?.Title ?? "?"}";
                case ConditionKind.ObjectiveCompleted: return $"Complete {Content.Objective(id)?.Title ?? "?"}";
                case ConditionKind.MilestoneReached: return $"Reach {Content.Milestone(id)?.Name ?? "?"}";
                case ConditionKind.RegionExplored: return $"Explore {Content.Region(id)?.Name ?? "?"}";
                case ConditionKind.DistinctJobsAtLeast:
                    return $"Jobs in use {Enums.Jobs.Count(j => State.Assignments[j] > 0)}/{amount}";
                case ConditionKind.FoodBalancePositive:
                {
                    double perMinute = Economy().Net.Food * 60;
                    return $"Food balance {perMinute.ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture)}/min (must be positive)";
                }
                default: return c.Kind.ToString();
            }
        }

        public List<RequirementStatus> Status(List<Condition> conditions) =>
            conditions.Select(c => new RequirementStatus { Text = Describe(c), Met = IsMet(c) }).ToList();

        // MARK: - Objectives

        /// <summary>First objective not completed yet (the tutorial's current step).</summary>
        public ObjectiveDefinition CurrentObjective =>
            Content.Objectives.FirstOrDefault(o => !State.CompletedObjectives.Contains(o.Id));

        /// <summary>Completes objectives and opens events. Rewards are granted exactly once per objective.</summary>
        internal void EvaluateProgress()
        {
            bool changed = true;
            // Loop because completing one objective can satisfy another.
            while (changed)
            {
                changed = false;
                foreach (var obj in Content.Objectives)
                {
                    if (State.CompletedObjectives.Contains(obj.Id)) continue;
                    if (!obj.Conditions.All(IsMet)) continue;
                    State.CompletedObjectives.Add(obj.Id);
                    if (obj.Reward.HasValue) Apply(new Effect(EffectKind.GrantResources) { Resources = obj.Reward }, obj.Id);
                    string rewardText = obj.Reward.HasValue ? $" Reward: {obj.Reward.Value.ShortDescription}" : "";
                    Notice(NoticeKind.Success, $"Objective complete: {obj.Title}.{rewardText}");
                    changed = true;
                }
            }
            CheckEventTriggers();
        }

        // MARK: - Milestones

        public List<RequirementStatus> MilestoneRequirements(string id) =>
            Status(Content.Milestone(id)?.Requirements ?? new List<Condition>());

        public bool CanClaimMilestone(string id)
        {
            var ms = Content.Milestone(id);
            if (ms == null || State.ReachedMilestones.Contains(id)) return false;
            return ms.Requirements.All(IsMet);
        }

        /// <summary>Reaches a milestone: changes the settlement stage and transforms buildings in place,
        /// keeping their ids, positions and history. Can only happen once.</summary>
        public GameResult ClaimMilestone(string id)
        {
            Update();
            var ms = Content.Milestone(id);
            if (ms == null) return GameError.UnknownContent(id);
            if (State.ReachedMilestones.Contains(id)) return GameError.MilestoneAlreadyReached;
            var unmet = ms.Requirements.Where(c => !IsMet(c)).Select(Describe).ToList();
            if (unmet.Count > 0) return GameError.RequirementsNotMet(unmet);

            foreach (var t in ms.BuildingTransforms)
            {
                foreach (var b in State.Buildings.Where(b => b.DefinitionId == t.From))
                {
                    b.DefinitionId = t.To;
                    b.History.Add(t.To);
                }
            }
            State.StageId = ms.NewStage;
            State.ReachedMilestones.Add(id);
            NormalizeAssignments();
            AddLegacy("milestone", ms.Name, ms.LegacyText, id);
            Notice(NoticeKind.Milestone, $"{ms.Name}! {ms.Description}");
            EvaluateProgress();
            return GameResult.Success;
        }

        // MARK: - Research

        public ResearchStatus GetResearchStatus(string techId)
        {
            if (State.Researched.Contains(techId)) return new ResearchStatus { State = ResearchState.Researched, Progress = 1 };
            var active = State.ActiveResearch;
            if (active != null && active.TechId == techId)
            {
                double p = active.TotalSeconds > 0 ? 1 - active.RemainingSeconds / active.TotalSeconds : 1;
                return new ResearchStatus { State = ResearchState.InProgress, Progress = Math.Min(1, Math.Max(0, p)) };
            }
            var missing = MissingPrerequisites(techId);
            return missing.Count == 0
                ? new ResearchStatus { State = ResearchState.Available }
                : new ResearchStatus { State = ResearchState.Blocked, Missing = missing };
        }

        /// <summary>Names of prerequisites that are not researched yet.</summary>
        public List<string> MissingPrerequisites(string techId)
        {
            var tech = Content.Technology(techId);
            if (tech == null) return new List<string>();
            return tech.Prerequisites.Where(p => !State.Researched.Contains(p))
                .Select(p => Content.Technology(p)?.Name ?? p).ToList();
        }

        /// <summary>Starts research. Validates prerequisites, duplicates and cost before spending.</summary>
        public GameResult StartResearch(string techId)
        {
            Update();
            var tech = Content.Technology(techId);
            if (tech == null) return GameError.UnknownContent(techId);
            if (State.Researched.Contains(techId)) return GameError.ResearchAlreadyDone;
            if (State.ActiveResearch != null) return GameError.ResearchInProgress;
            var missing = MissingPrerequisites(techId);
            if (missing.Count > 0) return GameError.MissingPrerequisites(missing);
            if (!State.Resources.Covers(tech.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(tech.Cost));
            State.Resources = State.Resources - tech.Cost;
            State.ActiveResearch = new ActiveResearch { TechId = techId, RemainingSeconds = tech.ResearchSeconds, TotalSeconds = tech.ResearchSeconds };
            Notice(NoticeKind.Info, $"Research started: {tech.Name}.");
            return GameResult.Success;
        }

        void AdvanceResearch(double dt)
        {
            var active = State.ActiveResearch;
            if (active == null) return;
            active.RemainingSeconds -= dt;
            if (active.RemainingSeconds > 1e-9) return;
            State.ActiveResearch = null;
            if (State.Researched.Contains(active.TechId)) return;
            State.Researched.Add(active.TechId);
            var tech = Content.Technology(active.TechId);
            string name = tech?.Name ?? active.TechId;
            Notice(NoticeKind.Success, $"Research complete: {name}!");
            AddLegacy("technology", $"Discovered {name}", tech?.Description ?? "", active.TechId);
        }

        // MARK: - Events

        public EventDefinition PendingEvent => State.PendingEventId == null ? null : Content.Event(State.PendingEventId);

        /// <summary>Null when the option can be chosen, otherwise the player-facing reason.</summary>
        public string OptionBlocker(EventOption option)
        {
            if (option.RequiresBuilding != null && Count(option.RequiresBuilding) == 0)
                return $"Requires a {Content.Building(option.RequiresBuilding)?.Name ?? option.RequiresBuilding}.";
            if (option.Cost.HasValue && !State.Resources.Covers(option.Cost.Value))
                return $"Not enough resources. Missing: {State.Resources.Shortfall(option.Cost.Value).ShortDescription}";
            return null;
        }

        /// <summary>Opens the first event whose trigger is met (one at a time, each at most once).</summary>
        void CheckEventTriggers()
        {
            if (State.PendingEventId != null) return;
            foreach (var ev in Content.Events)
            {
                if (State.ResolvedEvents.ContainsKey(ev.Id)) continue;
                if (ev.Trigger.All(IsMet))
                {
                    State.PendingEventId = ev.Id;
                    Notice(NoticeKind.Warning, $"A decision awaits: {ev.Title}");
                    return;
                }
            }
        }

        /// <summary>Applies the chosen option's consequences exactly once.</summary>
        public GameResult ResolveEvent(string optionId)
        {
            Update();
            var ev = PendingEvent;
            if (ev == null) return GameError.NoPendingEvent;
            var option = ev.Options.FirstOrDefault(o => o.Id == optionId);
            if (option == null) return GameError.UnknownContent(optionId);
            var blocker = OptionBlocker(option);
            if (blocker != null) return GameError.OptionUnavailable(blocker);

            if (option.Cost.HasValue) State.Resources = State.Resources - option.Cost.Value;
            foreach (var effect in ev.BaseEffects.Concat(option.Effects)) Apply(effect, ev.Id);
            State.ResolvedEvents[ev.Id] = option.Id;
            State.PendingEventId = null;
            AddLegacy("decision", $"{ev.Title}: {option.Label}", option.LegacyText, ev.Id);
            Notice(NoticeKind.Success, $"Decision made: {option.Label}.");
            EvaluateProgress();
            return GameResult.Success;
        }

        internal void Apply(Effect effect, string sourceId)
        {
            if (effect.Kind == EffectKind.GrantResources)
            {
                var caps = StorageCapacity();
                var r = State.Resources + (effect.Resources ?? ResourceBundle.Zero);
                foreach (var t in ResourceTypes.All) r[t] = Math.Min(caps[t], Math.Max(0, r[t]));
                State.Resources = r;
                return;
            }
            double? expires = effect.DurationSeconds.HasValue ? State.SimTime + effect.DurationSeconds.Value : (double?)null;
            State.Modifiers.Add(new ActiveModifier { SourceId = sourceId, Effect = effect, ExpiresAtSimTime = expires });
        }

        // MARK: - Exploration

        public GameResult StartExpedition(string regionId)
        {
            Update();
            var region = Content.Region(regionId);
            if (region == null) return GameError.UnknownContent(regionId);
            if (State.ExploredRegions.Contains(regionId)) return GameError.RegionAlreadyExplored;
            if (State.ActiveExpedition != null) return GameError.ExpeditionInProgress;
            if (!State.Resources.Covers(region.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(region.Cost));
            State.Resources = State.Resources - region.Cost;
            State.ActiveExpedition = new ActiveExpedition
            {
                RegionId = regionId, RemainingSeconds = region.ExpeditionSeconds, TotalSeconds = region.ExpeditionSeconds,
            };
            Notice(NoticeKind.Info, $"Scouts set out for {region.Name}.");
            return GameResult.Success;
        }

        void AdvanceExpedition(double dt)
        {
            var exp = State.ActiveExpedition;
            if (exp == null) return;
            exp.RemainingSeconds -= dt;
            if (exp.RemainingSeconds > 1e-9) return;
            State.ActiveExpedition = null;
            var region = Content.Region(exp.RegionId);
            if (State.ExploredRegions.Contains(exp.RegionId) || region == null) return;
            State.ExploredRegions.Add(exp.RegionId);
            Notice(NoticeKind.Success, $"Scouts returned: {region.Name} explored.");
            AddLegacy("exploration", $"Explored {region.Name}", region.LegacyText, region.Id);
        }
    }
}
