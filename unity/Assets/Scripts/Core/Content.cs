using System;
using System.Collections.Generic;
using System.Linq;

namespace Eonward.Core
{
    // MARK: - Basic enums

    public enum JobType { Foraging, Woodcutting, Stonecutting, Crafting, Construction, Research }

    public enum TerrainType { Grass, Forest, Rock, Water, Sand }

    public static class Enums
    {
        public static readonly JobType[] Jobs =
            { JobType.Foraging, JobType.Woodcutting, JobType.Stonecutting, JobType.Crafting, JobType.Construction, JobType.Research };

        public static string Id(this JobType j)
        {
            switch (j)
            {
                case JobType.Foraging: return "foraging";
                case JobType.Woodcutting: return "woodcutting";
                case JobType.Stonecutting: return "stonecutting";
                case JobType.Crafting: return "crafting";
                case JobType.Construction: return "construction";
                default: return "research";
            }
        }

        public static JobType ParseJob(string id)
        {
            foreach (var j in Jobs) if (j.Id() == id) return j;
            throw new FormatException($"Unknown job '{id}'.");
        }

        public static string Id(this TerrainType t) => t.ToString().ToLowerInvariant();
        public static string DisplayName(this TerrainType t) => t.ToString();

        public static TerrainType ParseTerrain(string id)
        {
            foreach (TerrainType t in Enum.GetValues(typeof(TerrainType))) if (t.Id() == id) return t;
            throw new FormatException($"Unknown terrain '{id}'.");
        }

        /// <summary>Character used in the map rows of content.json.</summary>
        public static TerrainType? TerrainFromSymbol(char c)
        {
            switch (c)
            {
                case '.': return TerrainType.Grass;
                case 'T': return TerrainType.Forest;
                case 'R': return TerrainType.Rock;
                case '~': return TerrainType.Water;
                case ',': return TerrainType.Sand;
                default: return null;
            }
        }

        internal static string FirstLower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        internal static T ParseEnum<T>(string id) where T : struct
        {
            foreach (T v in Enum.GetValues(typeof(T))) if (FirstLower(v.ToString()) == id) return v;
            throw new FormatException($"Unknown {typeof(T).Name} '{id}'.");
        }
    }

    // MARK: - Shared building blocks

    public struct Footprint : IEquatable<Footprint>
    {
        public int Width, Height;
        public Footprint(int w, int h) { Width = w; Height = h; }
        public bool Equals(Footprint o) => Width == o.Width && Height == o.Height;
        public override bool Equals(object obj) => obj is Footprint f && Equals(f);
        public override int GetHashCode() => Width * 397 ^ Height;
    }

    public sealed class JobSlot
    {
        public JobType Job;
        public int Count;
        public JobSlot(JobType job, int count) { Job = job; Count = count; }
        internal static JobSlot FromJson(JObj o) => new JobSlot(Enums.ParseJob(o.Str("job")), o.Int("count"));
    }

    public enum EffectKind
    {
        /// <summary>Multiplies the output of Job by Value.</summary>
        JobRateMultiplier,
        /// <summary>Multiplies the storage capacity of Resource by Value.</summary>
        StorageMultiplier,
        /// <summary>Multiplies food spoilage by Value.</summary>
        SpoilageMultiplier,
        /// <summary>Multiplies construction speed by Value when at least MinBuilders work on construction.</summary>
        ConstructionSpeedMultiplier,
        /// <summary>Multiplies food consumption by Value.</summary>
        ConsumptionMultiplier,
        /// <summary>Multiplies population growth speed by Value.</summary>
        GrowthMultiplier,
        /// <summary>Makes BuildingId available.</summary>
        UnlockBuilding,
        /// <summary>Adds Resources once (event outcomes and rewards).</summary>
        GrantResources,
    }

    /// <summary>A data-driven gameplay effect. Which optional fields are used depends on Kind.</summary>
    public sealed class Effect
    {
        public EffectKind Kind;
        public JobType? Job;
        public ResourceType? Resource;
        public double? Value;
        public string BuildingId;
        public int? MinBuilders;
        public ResourceBundle? Resources;
        /// <summary>For event effects: how long (game seconds) it lasts. Null = permanent.</summary>
        public double? DurationSeconds;

        public Effect(EffectKind kind) { Kind = kind; }

        public static Effect FromJson(JObj o) => new Effect(Enums.ParseEnum<EffectKind>(o.Str("kind")))
        {
            Job = o.Has("job") ? Enums.ParseJob(o.Str("job")) : (JobType?)null,
            Resource = o.Has("resource") ? ResourceTypes.Parse(o.Str("resource")) : (ResourceType?)null,
            Value = o.OptNum("value"),
            BuildingId = o.OptStr("buildingId"),
            MinBuilders = o.OptInt("minBuilders"),
            Resources = o.Has("resources") ? ResourceBundle.FromJson(o.Obj("resources")) : (ResourceBundle?)null,
            DurationSeconds = o.OptNum("durationSeconds"),
        };

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { ["kind"] = Enums.FirstLower(Kind.ToString()) };
            if (Job.HasValue) d["job"] = Job.Value.Id();
            if (Resource.HasValue) d["resource"] = Resource.Value.Id();
            if (Value.HasValue) d["value"] = Value.Value;
            if (BuildingId != null) d["buildingId"] = BuildingId;
            if (MinBuilders.HasValue) d["minBuilders"] = MinBuilders.Value;
            if (Resources.HasValue) d["resources"] = Resources.Value.ToJson();
            if (DurationSeconds.HasValue) d["durationSeconds"] = DurationSeconds.Value;
            return d;
        }
    }

    public enum ConditionKind
    {
        ResourceAtLeast,        // resource, amount
        BuildingCount,          // id, amount (completed buildings)
        HousingAtLeast,         // amount
        PopulationAtLeast,      // amount
        TechResearched,         // id
        TechCountAtLeast,       // amount
        EventResolved,          // id
        ObjectiveCompleted,     // id
        MilestoneReached,       // id
        RegionExplored,         // id
        DistinctJobsAtLeast,    // amount (jobs with at least one worker)
        FoodBalancePositive,    // net food rate > 0
    }

    public sealed class Condition
    {
        public ConditionKind Kind;
        public string Id;
        public ResourceType? Resource;
        public double? Amount;

        public Condition(ConditionKind kind) { Kind = kind; }

        internal static Condition FromJson(JObj o) => new Condition(Enums.ParseEnum<ConditionKind>(o.Str("kind")))
        {
            Id = o.OptStr("id"),
            Resource = o.Has("resource") ? ResourceTypes.Parse(o.Str("resource")) : (ResourceType?)null,
            Amount = o.OptNum("amount"),
        };
    }

    // MARK: - Definitions

    public sealed class BuildingDefinition
    {
        public string Id, Name, Description, Epoch;
        public Footprint Footprint;
        public ResourceBundle Cost;
        public double BuildSeconds;
        /// <summary>Key of the visual model used by the presentation layer.</summary>
        public string Sprite;
        public List<TerrainType> AllowedTerrain = new List<TerrainType>();
        /// <summary>False for starting/upgrade-only buildings that never appear in the build menu.</summary>
        public bool Buildable;
        public int? Housing;
        public double? Storage;
        public double? ProtectedFood;
        public List<JobSlot> JobSlots;
        public int? MaxCount;
        public string RequiresTech;
        /// <summary>Placement requires a neighbouring cell of this terrain.</summary>
        public TerrainType? RequiresAdjacentTerrain;
        /// <summary>In-place upgrade target (same footprint).</summary>
        public string UpgradesTo;
        /// <summary>Building this structure becomes later (keeps location and identity).</summary>
        public string SuccessorId;

        public int HousingValue => Housing ?? 0;
        public double StorageValue => Storage ?? 0;
        public double ProtectedFoodValue => ProtectedFood ?? 0;

        public int Slots(JobType job) => (JobSlots ?? new List<JobSlot>()).Where(s => s.Job == job).Sum(s => s.Count);

        internal static BuildingDefinition FromJson(JObj o)
        {
            var fp = o.Obj("footprint");
            return new BuildingDefinition
            {
                Id = o.Str("id"),
                Name = o.Str("name"),
                Description = o.Str("description"),
                Epoch = o.Str("epoch"),
                Footprint = new Footprint(fp.Int("width"), fp.Int("height")),
                Cost = ResourceBundle.FromJson(o.OptObj("cost")),
                BuildSeconds = o.Num("buildSeconds"),
                Sprite = o.Str("sprite"),
                AllowedTerrain = o.Strs("allowedTerrain").Select(Enums.ParseTerrain).ToList(),
                Buildable = o.Bool("buildable"),
                Housing = o.OptInt("housing"),
                Storage = o.OptNum("storage"),
                ProtectedFood = o.OptNum("protectedFood"),
                JobSlots = o.Has("jobSlots") ? o.Objs("jobSlots").Select(JobSlot.FromJson).ToList() : null,
                MaxCount = o.OptInt("maxCount"),
                RequiresTech = o.OptStr("requiresTech"),
                RequiresAdjacentTerrain = o.Has("requiresAdjacentTerrain") ? Enums.ParseTerrain(o.Str("requiresAdjacentTerrain")) : (TerrainType?)null,
                UpgradesTo = o.OptStr("upgradesTo"),
                SuccessorId = o.OptStr("successorId"),
            };
        }
    }

    public sealed class TechnologyDefinition
    {
        public string Id, Name, Description;
        public ResourceBundle Cost;
        public double ResearchSeconds;
        public List<string> Prerequisites = new List<string>();
        public List<Effect> Effects = new List<Effect>();

        internal static TechnologyDefinition FromJson(JObj o) => new TechnologyDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Cost = ResourceBundle.FromJson(o.OptObj("cost")),
            ResearchSeconds = o.Num("researchSeconds"),
            Prerequisites = o.Strs("prerequisites"),
            Effects = o.Objs("effects").Select(Effect.FromJson).ToList(),
        };
    }

    public sealed class EventOption
    {
        public string Id, Label;
        /// <summary>Plain-language trade-off shown before the player confirms.</summary>
        public string TradeOff;
        public ResourceBundle? Cost;
        public string RequiresBuilding;
        public List<Effect> Effects = new List<Effect>();
        public string LegacyText;

        internal static EventOption FromJson(JObj o) => new EventOption
        {
            Id = o.Str("id"),
            Label = o.Str("label"),
            TradeOff = o.Str("tradeOff"),
            Cost = o.Has("cost") ? ResourceBundle.FromJson(o.Obj("cost")) : (ResourceBundle?)null,
            RequiresBuilding = o.OptStr("requiresBuilding"),
            Effects = o.Objs("effects").Select(Effect.FromJson).ToList(),
            LegacyText = o.Str("legacyText"),
        };
    }

    public sealed class EventDefinition
    {
        public string Id, Title, Text;
        /// <summary>All must hold for the event to appear (at most once).</summary>
        public List<Condition> Trigger = new List<Condition>();
        /// <summary>Applied when the player decides, whichever option is chosen.</summary>
        public List<Effect> BaseEffects = new List<Effect>();
        public List<EventOption> Options = new List<EventOption>();

        internal static EventDefinition FromJson(JObj o) => new EventDefinition
        {
            Id = o.Str("id"),
            Title = o.Str("title"),
            Text = o.Str("text"),
            Trigger = o.Objs("trigger").Select(Condition.FromJson).ToList(),
            BaseEffects = o.Objs("baseEffects").Select(Effect.FromJson).ToList(),
            Options = o.Objs("options").Select(EventOption.FromJson).ToList(),
        };
    }

    public sealed class ObjectiveDefinition
    {
        public string Id, Title, Hint;
        public List<Condition> Conditions = new List<Condition>();
        public ResourceBundle? Reward;

        internal static ObjectiveDefinition FromJson(JObj o) => new ObjectiveDefinition
        {
            Id = o.Str("id"),
            Title = o.Str("title"),
            Hint = o.Str("hint"),
            Conditions = o.Objs("conditions").Select(Condition.FromJson).ToList(),
            Reward = o.Has("reward") ? ResourceBundle.FromJson(o.Obj("reward")) : (ResourceBundle?)null,
        };
    }

    public sealed class BuildingTransform
    {
        public string From, To;
    }

    public sealed class MilestoneDefinition
    {
        public string Id, Name, Description;
        public List<Condition> Requirements = new List<Condition>();
        public string NewStage;
        public List<BuildingTransform> BuildingTransforms = new List<BuildingTransform>();
        public string LegacyText;

        internal static MilestoneDefinition FromJson(JObj o) => new MilestoneDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Requirements = o.Objs("requirements").Select(Condition.FromJson).ToList(),
            NewStage = o.Str("newStage"),
            BuildingTransforms = o.Objs("buildingTransforms").Select(t => new BuildingTransform { From = t.Str("from"), To = t.Str("to") }).ToList(),
            LegacyText = o.Str("legacyText"),
        };
    }

    public sealed class RegionDefinition
    {
        public string Id, Name, Description;
        public int X, Y, Width, Height;
        public bool RevealedAtStart;
        public ResourceBundle Cost;
        public double ExpeditionSeconds;
        public string LegacyText;

        public bool Contains(int cx, int cy) => cx >= X && cx < X + Width && cy >= Y && cy < Y + Height;

        internal static RegionDefinition FromJson(JObj o) => new RegionDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            X = o.Int("x"), Y = o.Int("y"), Width = o.Int("width"), Height = o.Int("height"),
            RevealedAtStart = o.Bool("revealedAtStart"),
            Cost = ResourceBundle.FromJson(o.OptObj("cost")),
            ExpeditionSeconds = o.Num("expeditionSeconds"),
            LegacyText = o.Str("legacyText"),
        };
    }

    public sealed class StartBuilding
    {
        public string BuildingId;
        public int X, Y;
    }

    public sealed class MapDefinition
    {
        public string Name;
        public int Width, Height;
        public List<string> Rows = new List<string>();
        public List<StartBuilding> StartBuildings = new List<StartBuilding>();
        /// <summary>Persistent geographic traits recorded in the Legacy Timeline.</summary>
        public List<string> Traits = new List<string>();

        public TerrainType? Terrain(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || y >= Rows.Count) return null;
            var row = Rows[y];
            if (x >= row.Length) return null;
            return Enums.TerrainFromSymbol(row[x]);
        }

        internal static MapDefinition FromJson(JObj o) => new MapDefinition
        {
            Name = o.Str("name"),
            Width = o.Int("width"),
            Height = o.Int("height"),
            Rows = o.Strs("rows"),
            StartBuildings = o.Objs("startBuildings").Select(b => new StartBuilding { BuildingId = b.Str("buildingId"), X = b.Int("x"), Y = b.Int("y") }).ToList(),
            Traits = o.Strs("traits"),
        };
    }

    public sealed class JobConfig
    {
        public JobType Job;
        public string Name, Description;
        public ResourceType? Output;
        public double RatePerWorkerPerSecond;
        /// <summary>Jobs like construction have no building slots.</summary>
        public bool UnlimitedSlots;

        internal static JobConfig FromJson(JObj o) => new JobConfig
        {
            Job = Enums.ParseJob(o.Str("job")),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Output = o.Has("output") ? ResourceTypes.Parse(o.Str("output")) : (ResourceType?)null,
            RatePerWorkerPerSecond = o.Num("ratePerWorkerPerSecond"),
            UnlimitedSlots = o.Bool("unlimitedSlots"),
        };
    }

    public sealed class StageDefinition
    {
        public string Id, Name;
    }

    public sealed class EpochDefinition
    {
        public string Id, Name;
        public int Index;
        public List<StageDefinition> Stages = new List<StageDefinition>();
        public bool Playable;
    }

    public sealed class Branding
    {
        public string Title, Subtitle, Tagline;
    }

    /// <summary>All tunable numbers (content.json → "balance").</summary>
    public sealed class BalanceConfig
    {
        public ResourceBundle StartResources;
        public int StartPopulation;
        public List<JobSlot> StartAssignments = new List<JobSlot>();
        public double BaseStorage;
        public double KnowledgeCap;
        public double FoodPerPersonPerSecond;
        public double GrowthSecondsPerPerson;
        public double GrowthMinFood;
        public double ShortageProductivityMultiplier;
        public double SpoilageRatePerSecond;
        public double BaseConstructionSpeed;
        public double ConstructionSpeedPerBuilder;
        public int MaxConcurrentConstructions;
        public double CraftingBonusPerCrafter;
        public double MaxCraftingBonus;
        public ResourceBundle CraftingInputPerCrafterPerSecond;
        public double MaxOfflineSeconds;
        public double SimulationStepSeconds;
        public List<JobConfig> Jobs = new List<JobConfig>();

        internal static BalanceConfig FromJson(JObj o) => new BalanceConfig
        {
            StartResources = ResourceBundle.FromJson(o.Obj("startResources")),
            StartPopulation = o.Int("startPopulation"),
            StartAssignments = o.Objs("startAssignments").Select(JobSlot.FromJson).ToList(),
            BaseStorage = o.Num("baseStorage"),
            KnowledgeCap = o.Num("knowledgeCap"),
            FoodPerPersonPerSecond = o.Num("foodPerPersonPerSecond"),
            GrowthSecondsPerPerson = o.Num("growthSecondsPerPerson"),
            GrowthMinFood = o.Num("growthMinFood"),
            ShortageProductivityMultiplier = o.Num("shortageProductivityMultiplier"),
            SpoilageRatePerSecond = o.Num("spoilageRatePerSecond"),
            BaseConstructionSpeed = o.Num("baseConstructionSpeed"),
            ConstructionSpeedPerBuilder = o.Num("constructionSpeedPerBuilder"),
            MaxConcurrentConstructions = o.Int("maxConcurrentConstructions"),
            CraftingBonusPerCrafter = o.Num("craftingBonusPerCrafter"),
            MaxCraftingBonus = o.Num("maxCraftingBonus"),
            CraftingInputPerCrafterPerSecond = ResourceBundle.FromJson(o.Obj("craftingInputPerCrafterPerSecond")),
            MaxOfflineSeconds = o.Num("maxOfflineSeconds"),
            SimulationStepSeconds = o.Num("simulationStepSeconds"),
            Jobs = o.Objs("jobs").Select(JobConfig.FromJson).ToList(),
        };
    }

    // MARK: - Content root

    public sealed class ContentException : Exception
    {
        public readonly List<string> Problems;
        public ContentException(List<string> problems)
            : base("Content is invalid:\n- " + string.Join("\n- ", problems)) { Problems = problems; }
    }

    public sealed class GameContent
    {
        public int ContentVersion;
        public Branding Branding;
        public string StartEpoch, StartStage;
        public List<EpochDefinition> Epochs = new List<EpochDefinition>();
        public BalanceConfig Balance;
        public List<BuildingDefinition> Buildings = new List<BuildingDefinition>();
        public List<TechnologyDefinition> Technologies = new List<TechnologyDefinition>();
        public List<EventDefinition> Events = new List<EventDefinition>();
        public List<ObjectiveDefinition> Objectives = new List<ObjectiveDefinition>();
        public List<MilestoneDefinition> Milestones = new List<MilestoneDefinition>();
        public List<RegionDefinition> Regions = new List<RegionDefinition>();
        public MapDefinition Map;

        public BuildingDefinition Building(string id) => Buildings.FirstOrDefault(b => b.Id == id);
        public TechnologyDefinition Technology(string id) => Technologies.FirstOrDefault(t => t.Id == id);
        public EventDefinition Event(string id) => Events.FirstOrDefault(e => e.Id == id);
        public ObjectiveDefinition Objective(string id) => Objectives.FirstOrDefault(o => o.Id == id);
        public MilestoneDefinition Milestone(string id) => Milestones.FirstOrDefault(m => m.Id == id);
        public RegionDefinition Region(string id) => Regions.FirstOrDefault(r => r.Id == id);
        public EpochDefinition Epoch(string id) => Epochs.FirstOrDefault(e => e.Id == id);
        public JobConfig JobConfig(JobType job) => Balance.Jobs.FirstOrDefault(j => j.Job == job);

        /// <summary>Parses content without validating it.</summary>
        public static GameContent Parse(string json)
        {
            var o = new JObj(Json.Parse(json));
            var branding = o.Obj("branding");
            return new GameContent
            {
                ContentVersion = o.Int("contentVersion"),
                Branding = new Branding { Title = branding.Str("title"), Subtitle = branding.Str("subtitle"), Tagline = branding.Str("tagline") },
                StartEpoch = o.Str("startEpoch"),
                StartStage = o.Str("startStage"),
                Epochs = o.Objs("epochs").Select(e => new EpochDefinition
                {
                    Id = e.Str("id"),
                    Index = e.Int("index"),
                    Name = e.Str("name"),
                    Playable = e.OptBool("playable", false),
                    Stages = e.Objs("stages").Select(s => new StageDefinition { Id = s.Str("id"), Name = s.Str("name") }).ToList(),
                }).ToList(),
                Balance = BalanceConfig.FromJson(o.Obj("balance")),
                Buildings = o.Objs("buildings").Select(BuildingDefinition.FromJson).ToList(),
                Technologies = o.Objs("technologies").Select(TechnologyDefinition.FromJson).ToList(),
                Events = o.Objs("events").Select(EventDefinition.FromJson).ToList(),
                Objectives = o.Objs("objectives").Select(ObjectiveDefinition.FromJson).ToList(),
                Milestones = o.Objs("milestones").Select(MilestoneDefinition.FromJson).ToList(),
                Regions = o.Objs("regions").Select(RegionDefinition.FromJson).ToList(),
                Map = MapDefinition.FromJson(o.Obj("map")),
            };
        }

        /// <summary>Parses and validates content. Throws if anything is inconsistent.</summary>
        public static GameContent Load(string json)
        {
            var content = Parse(json);
            var problems = ContentValidator.Validate(content);
            if (problems.Count > 0) throw new ContentException(problems);
            return content;
        }
    }
}
