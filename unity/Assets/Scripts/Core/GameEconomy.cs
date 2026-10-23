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
        /// <summary>State of every recipe job (smelting…) in this snapshot.</summary>
        public Dictionary<JobType, ProcessingStatus> Processing = new Dictionary<JobType, ProcessingStatus>();
        /// <summary>Goods per second each trade route carries now (same order as State.TradeRoutes).</summary>
        public List<double> Trade = new List<double>();
        public ResourceBundle Net => Production - Consumption;
    }

    /// <summary>Why a recipe job is or is not making its good.</summary>
    public enum ProcessingState { Working, NoWorkers, MissingInput, OutputFull }

    public struct ProcessingStatus
    {
        public ProcessingState State;
        /// <summary>The input that ran out (MissingInput), or the one that limits the work most.</summary>
        public ResourceType? Missing;
        /// <summary>Output per second now, and what the workers could make with every input at hand.</summary>
        public double Rate, Capacity;
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
            // The UI asks for multipliers many times a frame: the list is kept until anything it is made of changes.
            int ranks = 0;
            if (HasEstates) foreach (var e in Estates) ranks = ranks * 8 + EstateRank(e);
            var buildings = BuildingEffects();
            var key = (State.SimTime, State.Researched.Count, State.Modifiers.Count, State.Laws.Count, ranks, State.People.Count, buildings, State);
            if (effectsCache != null && effectsCacheKey.Equals(key)) return effectsCache;
            var effects = new List<Effect>();
            foreach (var id in State.Researched)
            {
                var tech = Content.Technology(id);
                if (tech != null) effects.AddRange(tech.Effects);
            }
            foreach (var m in State.Modifiers) effects.Add(m.Effect);
            effects.AddRange(buildings);
            effects.AddRange(PerkEffects());
            effects.AddRange(LawEffects());
            effects.AddRange(EstateEffects());
            effectsCache = effects;
            effectsCacheKey = key;
            return effects;
        }

        List<Effect> effectsCache;
        static readonly List<Effect> noEffects = new List<Effect>();
        (double, int, int, int, int, int, List<Effect>, GameState) effectsCacheKey;

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

        /// <summary>Building kinds with lasting effects (content does not change while playing).</summary>
        Dictionary<string, List<Effect>> effectsByBuilding;
        List<Effect> buildingEffects;
        bool hasMonuments;
        int buildingEffectsKey = -1;
        object buildingEffectsOwner;

        /// <summary>Lasting effects of the finished buildings that have any (herb garden, lookout tower…).
        /// Kept until a building is added, removed or finished, so the UI can ask for multipliers every frame.</summary>
        List<Effect> BuildingEffects()
        {
            if (effectsByBuilding == null)
            {
                effectsByBuilding = Content.Buildings.Where(d => d.Effects.Count > 0).ToDictionary(d => d.Id, d => d.Effects);
                hasMonuments = Content.Buildings.Any(d => d.IsMonument);
            }
            if (effectsByBuilding.Count == 0 && !hasMonuments) return noEffects;
            int operational = 0;
            foreach (var b in State.Buildings) if (b.IsOperational) operational++;
            int parts = 0;
            foreach (var b in State.Buildings) parts += b.Parts.Count;
            int key = (State.Buildings.Count * 4096 + operational) * 64 + parts;
            if (buildingEffects != null && key == buildingEffectsKey && ReferenceEquals(buildingEffectsOwner, State.Buildings)) return buildingEffects;
            var list = new List<Effect>();
            foreach (var b in State.Buildings)
                if (b.IsOperational && effectsByBuilding.TryGetValue(b.DefinitionId, out var effects)) list.AddRange(effects);
            if (hasMonuments) list.AddRange(PartEffects());
            buildingEffects = list;
            buildingEffectsKey = key;
            buildingEffectsOwner = State.Buildings;
            return list;
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
            caps.Coins = Content.Balance.TreasuryCap * Multiplier(EffectKind.StorageMultiplier, null, ResourceType.Coins);
            return caps;
        }

        public double ProtectedFood() => OperationalBuildings().Sum(x => x.def.ProtectedFoodValue * LevelFactor(x.building.Level));

        /// <summary>How many workers a job can take. Jobs with unlimited slots are limited only by population.</summary>
        public int JobCapacity(JobType job)
        {
            if (Content.JobConfig(job)?.UnlimitedSlots == true) return State.Population;
            return OperationalBuildings().Sum(x => SlotsOf(x.building, x.def, job)) + PastureSlots(job);
        }

        /// <summary>Output per worker per second, including all current bonuses and penalties.</summary>
        public double RatePerWorker(JobType job) => RatePerWorker(job, ApprovalProductivity(CurrentApproval));

        double RatePerWorker(JobType job, double productivity)
        {
            var cfg = Content.JobConfig(job);
            if (cfg == null || cfg.Output == null) return 0;
            double rate = cfg.RatePerWorkerPerSecond * Multiplier(EffectKind.JobRateMultiplier, job) * SeasonJobMultiplier(job) * productivity
                          * RoadMultiplier(job) * WorkRate;
            if (State.FoodShortage) rate *= Content.Balance.ShortageProductivityMultiplier;
            if (IsGathering(job)) rate *= 1 + GetCraftingStatus().Bonus + ToolBonus;
            return rate;
        }

        /// <summary>Extra output of the gatherers while the store holds tools (bronze axes, sickles, picks).</summary>
        public double ToolBonus => State.Resources.Tools > 1e-6 ? Content.Balance.ToolBonus : 0;

        /// <summary>Tools the working gatherers wear out per second (never more than the store holds for one step).</summary>
        double ToolWear(double dt)
        {
            var b = Content.Balance;
            if (b.ToolWearPerWorkerPerSecond <= 0 || State.Resources.Tools <= 1e-6) return 0;
            int gatherers = 0;
            foreach (var job in Enums.Jobs) if (IsGathering(job)) gatherers += ActiveWorkers(job);
            return Math.Min(gatherers * b.ToolWearPerWorkerPerSecond, State.Resources.Tools / dt);
        }

        internal static bool IsGathering(JobType job) =>
            job == JobType.Foraging || job == JobType.Woodcutting || job == JobType.Stonecutting || job == JobType.Farming
            || job == JobType.Fishing || job == JobType.Hunting || job == JobType.Mining || job == JobType.Herding;

        public CraftingStatus GetCraftingStatus()
        {
            var b = Content.Balance;
            int crafters = ActiveWorkers(JobType.Crafting);
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

        /// <summary>Work speed of everyone from the customs and from discontent.</summary>
        public double WorkRate => Multiplier(EffectKind.WorkRateMultiplier) * DiscontentWork;

        /// <summary>Current per-second economy.</summary>
        public EconomySnapshot Economy() => EconomyCore(ApprovalProductivity(CurrentApproval));

        /// <summary>The economy for a simulation step of <paramref name="dt"/> seconds (recipes are limited to what that step can take).</summary>
        EconomySnapshot Economy(double dt) => EconomyCore(ApprovalProductivity(CurrentApproval), dt);

        /// <summary>What a recipe job is doing now (for the UI: "no ore", "storage full"…).</summary>
        public ProcessingStatus Processing(JobType job) =>
            Economy().Processing.TryGetValue(job, out var st) ? st : new ProcessingStatus { State = ProcessingState.NoWorkers };

        /// <summary>The economy at a given work speed (approval uses the neutral speed to avoid a loop).
        /// <paramref name="dt"/> is the step the recipes must be able to pay for (the normal step when shown).</summary>
        EconomySnapshot EconomyCore(double productivity, double dt = 0)
        {
            if (!(dt > 0)) dt = Content.Balance.SimulationStepSeconds;
            var b = Content.Balance;
            var snap = new EconomySnapshot();
            var crafting = GetCraftingStatus();
            snap.CraftingActive = crafting.Active;
            snap.CraftingBonus = crafting.Bonus;

            var production = new ResourceBundle();
            foreach (var cfg in b.Jobs)
            {
                if (cfg.Output == null || cfg.IsRecipe) continue;
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
            consumption += crafting.Input;
            consumption.Tools += ToolWear(dt);
            if (HasNeeds)
            {
                var needs = Needs();
                production.Coins += needs.TaxPerSecond;
                consumption.Goods += Math.Min(needs.GoodsPerSecond, State.Resources.Goods / dt);
            }
            ApplyRecipes(snap, ref production, ref consumption, productivity, dt);
            ApplyTrade(snap, ref production, ref consumption, dt);
            snap.Production = production;
            snap.Consumption = consumption;
            return snap;
        }

        /// <summary>
        /// Recipe jobs turn inputs into a good. They only use what is in the stockpile at the start of the step,
        /// after everything else that step consumes (food, firewood, crafting), and stop when the output has no
        /// room left. Goods gathered in the same step are not used, so a recipe can never spend what the land
        /// later fails to deliver; nothing is ever made from nothing and no stock goes below zero.
        /// Prototype simplification: one shared stockpile, no carrying between buildings (docs/TECHNICAL_ARCHITECTURE.md).
        /// </summary>
        void ApplyRecipes(EconomySnapshot snap, ref ResourceBundle production, ref ResourceBundle consumption, double productivity, double dt)
        {
            var caps = StorageCapacity();
            foreach (var cfg in Content.Balance.Jobs)
            {
                if (cfg.Output == null || !cfg.IsRecipe) continue;
                var status = new ProcessingStatus();
                double capacity = SkilledWorkers(cfg.Job) * RatePerWorker(cfg.Job, productivity);
                status.Capacity = capacity;
                if (capacity <= 0)
                {
                    status.State = ProcessingState.NoWorkers;
                    snap.Processing[cfg.Job] = status;
                    continue;
                }
                double rate = capacity;
                foreach (var input in cfg.Inputs.Entries)
                {
                    double left = Math.Max(0, State.Resources[input.Key] - consumption[input.Key] * dt);
                    double affordable = left / (input.Value * dt);
                    if (affordable < rate) { rate = affordable; status.Missing = input.Key; }
                }
                var output = cfg.Output.Value;
                double room = Math.Max(0, caps[output] - State.Resources[output] - (production[output] - consumption[output]) * dt) / dt;
                bool full = room < rate;
                if (full) rate = room;
                if (rate < 1e-9) rate = 0;

                status.Rate = rate;
                status.State = rate <= 0 ? (full ? ProcessingState.OutputFull : ProcessingState.MissingInput)
                    : full ? ProcessingState.OutputFull : ProcessingState.Working;
                if (status.State == ProcessingState.OutputFull) status.Missing = null;
                snap.Processing[cfg.Job] = status;
                production[output] += rate;
                consumption += cfg.Inputs * rate;
            }
        }

        /// <summary>Counts what the recipes made (for goals such as "cast the first bronze").</summary>
        void RecordMade(EconomySnapshot snap, double dt)
        {
            foreach (var kv in snap.Processing)
            {
                if (kv.Value.Rate <= 0) continue;
                var output = Content.JobConfig(kv.Key).Output.Value.Id();
                State.Made[output] = (State.Made.TryGetValue(output, out var v) ? v : 0) + kv.Value.Rate * dt;
            }
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
