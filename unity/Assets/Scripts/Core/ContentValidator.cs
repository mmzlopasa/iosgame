using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
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
            foreach (var cfg in b.Jobs.Where(j => j.IsRecipe))
            {
                // A recipe turns its inputs into one output; it must not feed on itself or take negative amounts.
                if (cfg.Output == null) problems.Add($"recipe job '{cfg.Job.Id()}' has inputs but no output");
                else if (cfg.Inputs[cfg.Output.Value] > 0) problems.Add($"recipe job '{cfg.Job.Id()}' uses its own output");
                if (!cfg.Inputs.IsValid) problems.Add($"recipe job '{cfg.Job.Id()}' has negative inputs");
                if (cfg.Inputs.Knowledge > 0) problems.Add($"recipe job '{cfg.Job.Id()}' cannot use knowledge");
                if (cfg.Byproduct.HasValue) problems.Add($"recipe job '{cfg.Job.Id()}' cannot have a byproduct");
            }
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
                foreach (var lv in bld.Levels)
                {
                    if (!lv.Cost.IsValid) problems.Add($"building '{bld.Id}' has a level with a negative cost");
                    if (lv.BuildSeconds <= 0) problems.Add($"building '{bld.Id}' has a level without buildSeconds");
                }
                foreach (var look in bld.Looks)
                    if (look.Level < 1 || string.IsNullOrEmpty(look.Sprite)) problems.Add($"building '{bld.Id}' has a broken look");
                if (bld.SuccessorId != null)
                {
                    var succ = c.Building(bld.SuccessorId);
                    if (succ != null && !succ.Footprint.Equals(bld.Footprint))
                        problems.Add($"successor '{bld.Id}' → '{bld.SuccessorId}' changes footprint");
                }
            }

            // Technologies: references and cycles
            int heartTop = c.Buildings.Where(x => x.Heart).Select(x => x.MaxLevel).DefaultIfEmpty(1).Max();
            foreach (var tech in c.Technologies)
            {
                if (tech.HeartLevel.HasValue && tech.HeartLevel.Value > heartTop)
                    problems.Add($"tech '{tech.Id}' needs heart level {tech.HeartLevel.Value}, but the heart stops at {heartTop}");
                foreach (var p in tech.Prerequisites)
                    if (!techIds.Contains(p)) problems.Add($"tech '{tech.Id}' has unknown prerequisite '{p}'");
                if (tech.ResearchSeconds <= 0) problems.Add($"tech '{tech.Id}' needs researchSeconds > 0");
                if (tech.Epoch != null && c.Epoch(tech.Epoch) == null) problems.Add($"tech '{tech.Id}' uses unknown epoch '{tech.Epoch}'");
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
                        case ConditionKind.BuildingLevelAtLeast:
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
                        case ConditionKind.MadeAtLeast:
                            if (cond.Resource == null) problems.Add($"{owner}: madeAtLeast needs a resource");
                            else if (!b.Jobs.Any(j => j.IsRecipe && j.Output == cond.Resource))
                                problems.Add($"{owner}: no recipe makes {cond.Resource.Value.Id()}");
                            break;
                    }
                }
            }

            foreach (var bld in c.Buildings)
                foreach (var lv in bld.Levels) CheckConditions(lv.Requirements, $"building '{bld.Id}' level");
            foreach (var skill in c.Skills)
                foreach (var perk in skill.Perks)
                {
                    if (perk.Level < 1 || perk.Level > b.MaxSkillLevel) problems.Add($"perk '{perk.Name}' of {skill.Job.Id()} has level {perk.Level} outside 1–{b.MaxSkillLevel}");
                    problems.AddRange(ValidateEffects(perk.Effects, $"perk '{perk.Name}'", buildingIds));
                }
            foreach (var n in b.Nodes)
                if (n.Capacity < 0 || n.Density <= 0 || n.Density > 1) problems.Add($"node '{n.Id}' needs capacity >= 0 and density in (0, 1]");

            // Laws and petitions
            CheckUnique(c.Laws.Select(l => l.Id), "law");
            CheckUnique(c.Petitions.Select(p => p.Id), "petition");
            var lawIds = new HashSet<string>(c.Laws.Select(l => l.Id));
            foreach (var law in c.Laws)
            {
                if (law.RequiresLaw != null && !lawIds.Contains(law.RequiresLaw)) problems.Add($"law '{law.Id}' requires unknown law '{law.RequiresLaw}'");
                if (law.RequiresLaw == law.Id) problems.Add($"law '{law.Id}' requires itself");
                if (law.RequiresBuilding != null && !buildingIds.Contains(law.RequiresBuilding))
                    problems.Add($"law '{law.Id}' requires unknown building '{law.RequiresBuilding}'");
                foreach (var x in law.Excludes)
                {
                    if (!lawIds.Contains(x)) problems.Add($"law '{law.Id}' excludes unknown law '{x}'");
                    if (x == law.RequiresLaw) problems.Add($"law '{law.Id}' both requires and excludes '{x}'");
                }
                if (law.Effects.Any(e => e.DurationSeconds.HasValue)) problems.Add($"law '{law.Id}' effects must be lasting");
                problems.AddRange(ValidateEffects(law.Effects, $"law '{law.Id}'", buildingIds));
            }
            foreach (var p in c.Petitions)
            {
                if (p.Goal.Count == 0) problems.Add($"petition '{p.Id}' has no goal");
                if (p.DeadlineSeconds <= 0) problems.Add($"petition '{p.Id}' needs a deadline");
                CheckConditions(p.Goal, $"petition '{p.Id}'");
                foreach (var g in p.Goal)
                    if (g.Kind == ConditionKind.LawEnacted && !lawIds.Contains(g.Id ?? "")) problems.Add($"petition '{p.Id}': unknown law '{g.Id}'");
            }
            if (b.Climate != null)
            {
                foreach (var s in b.Seasons)
                    if (!b.Climate.SeasonTemperature.ContainsKey(s.Id)) problems.Add($"climate has no temperature for season '{s.Id}'");
                if (b.Climate.PeriodSeconds <= 0 || b.Climate.DaySeconds <= 0) problems.Add("climate needs periodSeconds and daySeconds > 0");
                foreach (var snap in b.Climate.ColdSnaps)
                {
                    if (snap.Chance < 0 || snap.Chance > 1) problems.Add($"cold snap '{snap.Id}' chance must be in 0–1");
                    foreach (var sid in snap.Seasons)
                        if (!b.Seasons.Any(s => s.Id == sid)) problems.Add($"cold snap '{snap.Id}' uses unknown season '{sid}'");
                }
            }
            if (b.Warmth != null && !(b.Warmth.Comfortable > b.Warmth.Cold && b.Warmth.Cold > b.Warmth.Freezing))
                problems.Add("warmth thresholds must be comfortable > cold > freezing");
            if (b.Health != null && (b.Health.RecoverSeconds <= 0 || b.Health.TreatedRecoverSeconds <= 0 || b.Health.DeathSeconds <= 0))
                problems.Add("health needs recoverSeconds, treatedRecoverSeconds and deathSeconds > 0");
            foreach (var r in c.Regions)
                if (r.Survivors < 0 || !r.Loot.IsValid) problems.Add($"region '{r.Id}' has negative survivors or loot");
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
                if (string.IsNullOrEmpty(ms.NewEpoch))
                {
                    // A stage within the current era: any era may hold it (later eras have stages of their own).
                    if (!stageIds.Contains(ms.NewStage) && !c.Epochs.Any(e => e.Stages.Any(st => st.Id == ms.NewStage)))
                        problems.Add($"milestone '{ms.Id}' targets unknown stage '{ms.NewStage}'");
                }
                else
                {
                    var epoch = c.Epoch(ms.NewEpoch);
                    if (epoch == null) problems.Add($"milestone '{ms.Id}' targets unknown epoch '{ms.NewEpoch}'");
                    else if (!epoch.Stages.Any(st => st.Id == ms.NewStage))
                        problems.Add($"milestone '{ms.Id}' targets stage '{ms.NewStage}', which is not in {ms.NewEpoch}");
                }
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
            if (c.Regions.Count(r => r.Home) > 1) problems.Add("only one region can be the home clearing");
            if (!c.Map.StartBuildings.Any(b => b.BuildingId == "campfire")) problems.Add("the map must suggest where the campfire goes");
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
                    case EffectKind.WorkRateMultiplier:
                    case EffectKind.SicknessMultiplier:
                    case EffectKind.RecoveryMultiplier:
                    case EffectKind.FuelMultiplier:
                    case EffectKind.ExperienceMultiplier:
                    case EffectKind.GriefMultiplier:
                    case EffectKind.DeathMultiplier:
                        if (e.Value == null) problems.Add($"{owner}: {Enums.FirstLower(e.Kind.ToString())} needs value");
                        break;
                    case EffectKind.UnlockBuilding:
                        if (!buildingIds.Contains(e.BuildingId ?? "")) problems.Add($"{owner}: unlocks unknown building '{e.BuildingId ?? "nil"}'");
                        break;
                    case EffectKind.GrantResources:
                        if (e.Resources == null) problems.Add($"{owner}: grantResources needs resources");
                        break;
                }
                if (e.Kind == EffectKind.ApprovalBonus || e.Kind == EffectKind.DiscontentPerHour || e.Kind == EffectKind.HeatBonus)
                {
                    // Points and degrees are additive and may be negative.
                    if (e.Value == null || double.IsNaN(e.Value.Value) || double.IsInfinity(e.Value.Value) || Math.Abs(e.Value.Value) > 100)
                        problems.Add($"{owner}: {Enums.FirstLower(e.Kind.ToString())} needs a value between -100 and 100");
                }
                else if (e.Value.HasValue && (double.IsNaN(e.Value.Value) || double.IsInfinity(e.Value.Value) || e.Value.Value < 0))
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
