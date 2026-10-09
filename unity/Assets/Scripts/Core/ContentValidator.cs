using System;
using System.Collections.Generic;
using System.Linq;

namespace Eonward.Core
{
    /// <summary>Checks content for broken references, duplicate IDs and impossible chains.
    /// Returns human readable problems; an empty list means the content is usable.</summary>
    public static class ContentValidator
    {
        public static List<string> Validate(GameContent c)
        {
            var problems = new List<string>();

            void CheckUnique(IEnumerable<string> ids, string label)
            {
                var seen = new HashSet<string>();
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) problems.Add($"{label} has an empty id");
                    else if (!seen.Add(id)) problems.Add($"Duplicate {label} id '{id}'");
                }
            }
            CheckUnique(c.Buildings.Select(b => b.Id), "building");
            CheckUnique(c.Technologies.Select(t => t.Id), "technology");
            CheckUnique(c.Events.Select(e => e.Id), "event");
            CheckUnique(c.Objectives.Select(o => o.Id), "objective");
            CheckUnique(c.Milestones.Select(m => m.Id), "milestone");
            CheckUnique(c.Regions.Select(r => r.Id), "region");
            CheckUnique(c.Epochs.Select(e => e.Id), "epoch");

            var buildingIds = new HashSet<string>(c.Buildings.Select(b => b.Id));
            var techIds = new HashSet<string>(c.Technologies.Select(t => t.Id));
            var eventIds = new HashSet<string>(c.Events.Select(e => e.Id));
            var objectiveIds = new HashSet<string>(c.Objectives.Select(o => o.Id));
            var milestoneIds = new HashSet<string>(c.Milestones.Select(m => m.Id));
            var regionIds = new HashSet<string>(c.Regions.Select(r => r.Id));

            // Epochs
            var startEpoch = c.Epoch(c.StartEpoch);
            if (startEpoch == null)
            {
                problems.Add($"startEpoch '{c.StartEpoch}' does not exist");
                return problems;
            }
            var stageIds = new HashSet<string>(startEpoch.Stages.Select(s => s.Id));
            if (!stageIds.Contains(c.StartStage)) problems.Add($"startStage '{c.StartStage}' is not a stage of {c.StartEpoch}");

            // Balance
            var b = c.Balance;
            if (b.SimulationStepSeconds <= 0) problems.Add("simulationStepSeconds must be > 0");
            if (b.MaxOfflineSeconds < 0) problems.Add("maxOfflineSeconds must be >= 0");
            if (!b.StartResources.IsValid) problems.Add("startResources must be non-negative");
            foreach (var job in Enums.Jobs)
                if (c.JobConfig(job) == null) problems.Add($"balance.jobs is missing job '{job.Id()}'");
            if (b.StartAssignments.Sum(s => s.Count) > b.StartPopulation) problems.Add("startAssignments exceed startPopulation");

            // Map
            var map = c.Map;
            if (map.Rows.Count != map.Height) problems.Add($"map has {map.Rows.Count} rows, expected {map.Height}");
            for (int y = 0; y < map.Rows.Count; y++)
            {
                var row = map.Rows[y];
                if (row.Length != map.Width) problems.Add($"map row {y} has {row.Length} cells, expected {map.Width}");
                foreach (var ch in row)
                    if (Enums.TerrainFromSymbol(ch) == null) problems.Add($"map row {y} has unknown terrain symbol '{ch}'");
            }
            foreach (var sb in map.StartBuildings)
                if (!buildingIds.Contains(sb.BuildingId)) problems.Add($"start building '{sb.BuildingId}' does not exist");

            // Buildings
            foreach (var bld in c.Buildings)
            {
                if (c.Epoch(bld.Epoch) == null) problems.Add($"building '{bld.Id}' uses unknown epoch '{bld.Epoch}'");
                if (bld.Footprint.Width < 1 || bld.Footprint.Height < 1) problems.Add($"building '{bld.Id}' has an empty footprint");
                if (bld.BuildSeconds <= 0) problems.Add($"building '{bld.Id}' needs buildSeconds > 0");
                if (!bld.Cost.IsValid) problems.Add($"building '{bld.Id}' has a negative cost");
                if (bld.AllowedTerrain.Count == 0) problems.Add($"building '{bld.Id}' has no allowed terrain");
                if (bld.RequiresTech != null && !techIds.Contains(bld.RequiresTech))
                    problems.Add($"building '{bld.Id}' requires unknown tech '{bld.RequiresTech}'");
                if (bld.UpgradesTo != null)
                {
                    var target = c.Building(bld.UpgradesTo);
                    if (target == null) problems.Add($"building '{bld.Id}' upgrades to unknown '{bld.UpgradesTo}'");
                    else if (!target.Footprint.Equals(bld.Footprint)) problems.Add($"upgrade '{bld.Id}' → '{bld.UpgradesTo}' changes footprint");
                }
                if (bld.SuccessorId != null)
                {
                    var succ = c.Building(bld.SuccessorId);
                    if (succ != null && !succ.Footprint.Equals(bld.Footprint))
                        problems.Add($"successor '{bld.Id}' → '{bld.SuccessorId}' changes footprint");
                }
            }

            // Technologies: references and cycles
            foreach (var tech in c.Technologies)
            {
                foreach (var p in tech.Prerequisites)
                    if (!techIds.Contains(p)) problems.Add($"tech '{tech.Id}' has unknown prerequisite '{p}'");
                if (tech.ResearchSeconds <= 0) problems.Add($"tech '{tech.Id}' needs researchSeconds > 0");
                problems.AddRange(ValidateEffects(tech.Effects, $"tech '{tech.Id}'", buildingIds));
            }
            var cycle = FindPrerequisiteCycle(c.Technologies);
            if (cycle != null) problems.Add("technology prerequisites form a cycle: " + string.Join(" → ", cycle));

            // Conditions
            void CheckConditions(List<Condition> conds, string owner)
            {
                foreach (var cond in conds)
                {
                    string id = cond.Id ?? "nil";
                    switch (cond.Kind)
                    {
                        case ConditionKind.BuildingCount:
                            if (!buildingIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown building '{id}'");
                            break;
                        case ConditionKind.TechResearched:
                            if (!techIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown tech '{id}'");
                            break;
                        case ConditionKind.EventResolved:
                            if (!eventIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown event '{id}'");
                            break;
                        case ConditionKind.ObjectiveCompleted:
                            if (!objectiveIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown objective '{id}'");
                            break;
                        case ConditionKind.MilestoneReached:
                            if (!milestoneIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown milestone '{id}'");
                            break;
                        case ConditionKind.RegionExplored:
                            if (!regionIds.Contains(cond.Id ?? "")) problems.Add($"{owner}: unknown region '{id}'");
                            break;
                        case ConditionKind.ResourceAtLeast:
                            if (cond.Resource == null) problems.Add($"{owner}: resourceAtLeast needs a resource");
                            break;
                    }
                }
            }

            foreach (var ev in c.Events)
            {
                CheckConditions(ev.Trigger, $"event '{ev.Id}'");
                if (ev.Options.Count < 2 || ev.Options.Count > 3) problems.Add($"event '{ev.Id}' must have 2–3 options");
                problems.AddRange(ValidateEffects(ev.BaseEffects, $"event '{ev.Id}'", buildingIds));
                foreach (var opt in ev.Options)
                {
                    if (opt.RequiresBuilding != null && !buildingIds.Contains(opt.RequiresBuilding))
                        problems.Add($"event option '{opt.Id}' requires unknown building '{opt.RequiresBuilding}'");
                    problems.AddRange(ValidateEffects(opt.Effects, $"event option '{opt.Id}'", buildingIds));
                }
                if (ev.Options.All(o => o.RequiresBuilding != null))
                    problems.Add($"event '{ev.Id}' needs at least one option without requirements");
            }
            foreach (var obj in c.Objectives)
            {
                if (obj.Conditions.Count == 0) problems.Add($"objective '{obj.Id}' has no conditions");
                CheckConditions(obj.Conditions, $"objective '{obj.Id}'");
            }
            foreach (var ms in c.Milestones)
            {
                CheckConditions(ms.Requirements, $"milestone '{ms.Id}'");
                if (!stageIds.Contains(ms.NewStage)) problems.Add($"milestone '{ms.Id}' targets unknown stage '{ms.NewStage}'");
                foreach (var t in ms.BuildingTransforms)
                {
                    var from = c.Building(t.From);
                    var to = c.Building(t.To);
                    if (from == null || to == null)
                    {
                        problems.Add($"milestone '{ms.Id}' transforms unknown buildings {t.From} → {t.To}");
                        continue;
                    }
                    if (!from.Footprint.Equals(to.Footprint)) problems.Add($"milestone transform {t.From} → {t.To} changes footprint");
                }
            }
            foreach (var r in c.Regions)
                if (r.X < 0 || r.Y < 0 || r.X + r.Width > map.Width || r.Y + r.Height > map.Height)
                    problems.Add($"region '{r.Id}' lies outside the map");
            if (!c.Regions.Any(r => r.RevealedAtStart)) problems.Add("at least one region must be revealed at start");

            return problems;
        }

        static List<string> ValidateEffects(List<Effect> effects, string owner, HashSet<string> buildingIds)
        {
            var problems = new List<string>();
            foreach (var e in effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.JobRateMultiplier:
                        if (e.Job == null || e.Value == null) problems.Add($"{owner}: jobRateMultiplier needs job and value");
                        break;
                    case EffectKind.StorageMultiplier:
                        if (e.Resource == null || e.Value == null) problems.Add($"{owner}: storageMultiplier needs resource and value");
                        break;
                    case EffectKind.SpoilageMultiplier:
                    case EffectKind.ConsumptionMultiplier:
                    case EffectKind.GrowthMultiplier:
                    case EffectKind.ConstructionSpeedMultiplier:
                        if (e.Value == null) problems.Add($"{owner}: {Enums.FirstLower(e.Kind.ToString())} needs value");
                        break;
                    case EffectKind.UnlockBuilding:
                        if (!buildingIds.Contains(e.BuildingId ?? "")) problems.Add($"{owner}: unlocks unknown building '{e.BuildingId ?? "nil"}'");
                        break;
                    case EffectKind.GrantResources:
                        if (e.Resources == null) problems.Add($"{owner}: grantResources needs resources");
                        break;
                }
                if (e.Value.HasValue && (double.IsNaN(e.Value.Value) || double.IsInfinity(e.Value.Value) || e.Value.Value < 0))
                    problems.Add($"{owner}: effect value must be finite and >= 0");
            }
            return problems;
        }

        /// <summary>Returns one cycle (as a list of ids) if the prerequisite graph is not acyclic.</summary>
        public static List<string> FindPrerequisiteCycle(List<TechnologyDefinition> techs)
        {
            var byId = new Dictionary<string, TechnologyDefinition>();
            foreach (var t in techs) if (!byId.ContainsKey(t.Id)) byId[t.Id] = t;
            var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
            var stack = new List<string>();

            List<string> Visit(string id)
            {
                state.TryGetValue(id, out int s);
                if (s == 2) return null;
                if (s == 1)
                {
                    int start = stack.IndexOf(id);
                    if (start >= 0)
                    {
                        var cycle = stack.GetRange(start, stack.Count - start);
                        cycle.Add(id);
                        return cycle;
                    }
                    return new List<string> { id };
                }
                state[id] = 1;
                stack.Add(id);
                if (byId.TryGetValue(id, out var tech))
                {
                    foreach (var p in tech.Prerequisites)
                    {
                        var found = Visit(p);
                        if (found != null) return found;
                    }
                }
                stack.RemoveAt(stack.Count - 1);
                state[id] = 2;
                return null;
            }

            foreach (var t in techs)
            {
                var cycle = Visit(t.Id);
                if (cycle != null) return cycle;
            }
            return null;
        }
    }
}
