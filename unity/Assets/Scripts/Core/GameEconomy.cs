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
        /// <summary>Effects from researched technologies plus active event modifiers.</summary>
        internal List<Effect> ActiveEffects()
        {
            var effects = new List<Effect>();
            foreach (var id in State.Researched)
            {
                var tech = Content.Technology(id);
                if (tech != null) effects.AddRange(tech.Effects);
            }
            effects.AddRange(State.Modifiers.Select(m => m.Effect));
            return effects;
        }

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

        public int HousingCapacity() => OperationalBuildings().Sum(x => x.def.HousingValue);

        public ResourceBundle StorageCapacity()
        {
            double extra = OperationalBuildings().Sum(x => x.def.StorageValue);
            double baseCap = Content.Balance.BaseStorage + extra;
            var caps = new ResourceBundle();
            foreach (var t in new[] { ResourceType.Food, ResourceType.Wood, ResourceType.Stone })
                caps[t] = baseCap * Multiplier(EffectKind.StorageMultiplier, null, t);
            caps.Knowledge = Content.Balance.KnowledgeCap * Multiplier(EffectKind.StorageMultiplier, null, ResourceType.Knowledge);
            return caps;
        }

        public double ProtectedFood() => OperationalBuildings().Sum(x => x.def.ProtectedFoodValue);

        /// <summary>How many workers a job can take. Jobs with unlimited slots are limited only by population.</summary>
        public int JobCapacity(JobType job)
        {
            if (Content.JobConfig(job)?.UnlimitedSlots == true) return State.Population;
            return OperationalBuildings().Sum(x => x.def.Slots(job));
        }

        /// <summary>Output per worker per second, including all current bonuses and penalties.</summary>
        public double RatePerWorker(JobType job)
        {
            var cfg = Content.JobConfig(job);
            if (cfg == null || cfg.Output == null) return 0;
            double rate = cfg.RatePerWorkerPerSecond * Multiplier(EffectKind.JobRateMultiplier, job);
            if (State.FoodShortage) rate *= Content.Balance.ShortageProductivityMultiplier;
            if (IsGathering(job)) rate *= 1 + GetCraftingStatus().Bonus;
            return rate;
        }

        internal static bool IsGathering(JobType job) =>
            job == JobType.Foraging || job == JobType.Woodcutting || job == JobType.Stonecutting;

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
        public EconomySnapshot Economy()
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
                production[cfg.Output.Value] += State.Assignments[cfg.Job] * RatePerWorker(cfg.Job);
            }
            snap.Production = production;

            var consumption = new ResourceBundle();
            consumption.Food = State.Population * b.FoodPerPersonPerSecond * Multiplier(EffectKind.ConsumptionMultiplier);
            snap.Spoilage = Math.Max(0, State.Resources.Food - ProtectedFood()) * b.SpoilageRatePerSecond * Multiplier(EffectKind.SpoilageMultiplier);
            consumption.Food += snap.Spoilage;
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
        }
    }
}
