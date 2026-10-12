using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>Per-second production/consumption at one moment. Used by the simulation and shown by the UI.</summary>
    public sealed class EconomySnapshot
    {
        public ResourceBundle Production;
        public ResourceBundle Consumption;
        public double Spoilage;
        public bool CraftingActive;
        public double CraftingBonus;
        public ResourceBundle Net => Production - Consumption;
    }

    public struct CraftingStatus
    {
        public bool Active;
        public double Bonus;
        public ResourceBundle Input;
    }

    public sealed partial class Game
    {
        /// <summary>Within one simulation step the effects and approval are worked out once.</summary>
        List<Effect> stepEffects;
        int? stepApproval;
        bool simulating;

        /// <summary>Effects from researched technologies, active event modifiers and earned perks.</summary>
        internal List<Effect> ActiveEffects()
        {
            if (stepEffects != null) return stepEffects;
            var effects = new List<Effect>();
            foreach (var id in State.Researched)
            {
                var tech = Content.Technology(id);
                if (tech != null) effects.AddRange(tech.Effects);
            }
            foreach (var m in State.Modifiers) effects.Add(m.Effect);
            effects.AddRange(PerkEffects());
            return effects;
        }

        int CurrentApproval => stepApproval ?? Approval;

        /// <summary>Product of all multipliers of a kind (filtered by job/resource; null matches only null).</summary>
        public double Multiplier(EffectKind kind, JobType? job = null, ResourceType? resource = null)
        {
            double result = 1.0;
            foreach (var e in ActiveEffects())
                if (e.Kind == kind && e.Job == job && e.Resource == resource) result *= e.Value ?? 1;
            return result;
        }

        public IEnumerable<(PlacedBuilding building, BuildingDefinition def)> OperationalBuildings()
        {
            foreach (var b in State.Buildings)
            {
                if (!b.IsOperational) continue;
                var def = Content.Building(b.DefinitionId);
                if (def != null) yield return (b, def);
            }
        }

        public int HousingCapacity() => OperationalBuildings().Sum(x => HousingOf(x.building, x.def));

        public ResourceBundle StorageCapacity()
        {
            double extra = OperationalBuildings().Sum(x => x.def.StorageValue * LevelFactor(x.building.Level));
            double baseCap = Content.Balance.BaseStorage + extra;
            var caps = new ResourceBundle();
            foreach (var t in ResourceTypes.Stored)
                caps[t] = baseCap * Multiplier(EffectKind.StorageMultiplier, null, t);
            caps.Knowledge = Content.Balance.KnowledgeCap * Multiplier(EffectKind.StorageMultiplier, null, ResourceType.Knowledge);
            return caps;
        }

        public double ProtectedFood() => OperationalBuildings().Sum(x => x.def.ProtectedFoodValue * LevelFactor(x.building.Level));

        /// <summary>How many workers a job can take. Jobs with unlimited slots are limited only by population.</summary>
        public int JobCapacity(JobType job)
        {
            if (Content.JobConfig(job)?.UnlimitedSlots == true) return State.Population;
            return OperationalBuildings().Sum(x => SlotsOf(x.building, x.def, job));
        }

        /// <summary>Output per worker per second, including all current bonuses and penalties.</summary>
        public double RatePerWorker(JobType job) => RatePerWorker(job, ApprovalProductivity(CurrentApproval));

        double RatePerWorker(JobType job, double productivity)
        {
            var cfg = Content.JobConfig(job);
            if (cfg == null || cfg.Output == null) return 0;
            double rate = cfg.RatePerWorkerPerSecond * Multiplier(EffectKind.JobRateMultiplier, job) * SeasonJobMultiplier(job) * productivity
                          * RoadMultiplier(job);
            if (State.FoodShortage) rate *= Content.Balance.ShortageProductivityMultiplier;
            if (IsGathering(job)) rate *= 1 + GetCraftingStatus().Bonus;
            return rate;
        }

        internal static bool IsGathering(JobType job) =>
            job == JobType.Foraging || job == JobType.Woodcutting || job == JobType.Stonecutting || job == JobType.Farming
            || job == JobType.Fishing || job == JobType.Hunting || job == JobType.Mining || job == JobType.Herding;

        public CraftingStatus GetCraftingStatus()
        {
            var b = Content.Balance;
            int crafters = State.Assignments[JobType.Crafting];
            if (crafters <= 0) return new CraftingStatus();
            var input = b.CraftingInputPerCrafterPerSecond * crafters;
            // Crafting only works while there is enough material for at least one step.
            bool active = State.Resources.Covers(input * b.SimulationStepSeconds);
            return new CraftingStatus
            {
                Active = active,
                Bonus = active ? Math.Min(b.MaxCraftingBonus, b.CraftingBonusPerCrafter * crafters) : 0,
                Input = active ? input : ResourceBundle.Zero,
            };
        }

        /// <summary>Current per-second economy.</summary>
        public EconomySnapshot Economy() => EconomyCore(ApprovalProductivity(CurrentApproval));

        /// <summary>The economy at a given work speed (approval uses the neutral speed to avoid a loop).</summary>
        EconomySnapshot EconomyCore(double productivity)
        {
            var b = Content.Balance;
            var snap = new EconomySnapshot();
            var crafting = GetCraftingStatus();
            snap.CraftingActive = crafting.Active;
            snap.CraftingBonus = crafting.Bonus;

            var production = new ResourceBundle();
            foreach (var cfg in b.Jobs)
            {
                if (cfg.Output == null) continue;
                double made = SkilledWorkers(cfg.Job) * RatePerWorker(cfg.Job, productivity);
                // Shown as stopped when nothing is left within reach; the simulation takes what it finds (AdvanceNodes).
                if (!simulating && exhaustedJobs.Contains(cfg.Job)) made = 0;
                production[cfg.Output.Value] += made;
                if (cfg.Byproduct.HasValue) production[cfg.Byproduct.Value] += made * cfg.ByproductRatio;
            }
            snap.Production = production;

            var consumption = new ResourceBundle();
            consumption.Food = State.Population * b.FoodPerPersonPerSecond * Multiplier(EffectKind.ConsumptionMultiplier);
            snap.Spoilage = Math.Max(0, State.Resources.Food - ProtectedFood()) * b.SpoilageRatePerSecond * Multiplier(EffectKind.SpoilageMultiplier);
            consumption.Food += snap.Spoilage;
            consumption.Wood = FuelPerSecond;
            snap.Consumption = consumption + crafting.Input;
            return snap;
        }

        // MARK: - Jobs

        /// <summary>Sets the number of workers for a job. Validates capacity and idle population.</summary>
        public GameResult SetWorkers(JobType job, int count)
        {
            Update();
            int target = Math.Max(0, count);
            int current = State.Assignments[job];
            if (target > current)
            {
                int available = IdleWorkers;
                if (target - current > available) return GameError.TooManyWorkers(available);
                int capacity = JobCapacity(job);
                if (target > capacity) return GameError.NoJobSlots(capacity);
            }
            State.Assignments[job] = target;
            SyncPeople();
            EvaluateProgress();
            return GameResult.Success;
        }

        /// <summary>Keeps assignments within capacity after buildings change (e.g. a transform).</summary>
        internal void NormalizeAssignments()
        {
            foreach (var job in Enums.Jobs)
            {
                int cap = JobCapacity(job);
                if (State.Assignments[job] > cap) State.Assignments[job] = cap;
            }
            while (State.Assignments.Total > State.Population)
            {
                var busy = Enums.Jobs.Where(j => State.Assignments[j] > 0).ToList();
                if (busy.Count == 0) break;
                State.Assignments[busy[busy.Count - 1]] -= 1;
            }
            SyncPeople();
        }
    }
}
