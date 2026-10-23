using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    // MARK: - Basic enums

    public enum JobType { Foraging, Woodcutting, Stonecutting, Crafting, Construction, Research, Farming, Fishing, Hunting, Mining, Herding, Healing, Smelting,
                          Sawing, Toolmaking, Baking, Weaving }

    public enum TerrainType { Grass, Forest, Rock, Water, Sand, Mountain, Ore }

    public static class Enums
    {
        public static readonly JobType[] Jobs =
            { JobType.Foraging, JobType.Farming, JobType.Herding, JobType.Fishing, JobType.Hunting, JobType.Woodcutting, JobType.Stonecutting, JobType.Mining,
              JobType.Smelting, JobType.Sawing, JobType.Toolmaking, JobType.Baking, JobType.Weaving, JobType.Crafting, JobType.Construction, JobType.Research, JobType.Healing };

        public static string Id(this JobType j)
        {
            switch (j)
            {
                case JobType.Foraging: return "foraging";
                case JobType.Woodcutting: return "woodcutting";
                case JobType.Stonecutting: return "stonecutting";
                case JobType.Crafting: return "crafting";
                case JobType.Construction: return "construction";
                case JobType.Farming: return "farming";
                case JobType.Fishing: return "fishing";
                case JobType.Hunting: return "hunting";
                case JobType.Mining: return "mining";
                case JobType.Herding: return "herding";
                case JobType.Healing: return "healing";
                case JobType.Smelting: return "smelting";
                case JobType.Sawing: return "sawing";
                case JobType.Toolmaking: return "toolmaking";
                case JobType.Baking: return "baking";
                case JobType.Weaving: return "weaving";
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
                case 'M': return TerrainType.Mountain;
                case 'O': return TerrainType.Ore;
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
        /// <summary>Adds Value points to approval (usually temporary, from events).</summary>
        ApprovalBonus,
        /// <summary>Multiplies the work of every job (and of the builders) by Value: long days, rest days.</summary>
        WorkRateMultiplier,
        /// <summary>Multiplies how often people fall ill by Value.</summary>
        SicknessMultiplier,
        /// <summary>Multiplies how fast the sick get well by Value.</summary>
        RecoveryMultiplier,
        /// <summary>Adds Value °C to the warmth the heart gives off.</summary>
        HeatBonus,
        /// <summary>Adds Value points of discontent per hour (negative calms people down).</summary>
        DiscontentPerHour,
        /// <summary>Multiplies the firewood burnt for warmth by Value.</summary>
        FuelMultiplier,
        /// <summary>Multiplies the experience people gain at work by Value.</summary>
        ExperienceMultiplier,
        /// <summary>Multiplies the grief (lost approval, discontent) a death brings by Value.</summary>
        GriefMultiplier,
        /// <summary>Multiplies how fast untreated illness turns deadly by Value.</summary>
        DeathMultiplier,
        /// <summary>Multiplies how fast scouts explore a land by Value (lookout towers, trail signs).</summary>
        ExpeditionSpeedMultiplier,
        /// <summary>Multiplies what the neighbours pay for the town's goods by Value (guilds, a trading house).</summary>
        TradePriceMultiplier,
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
        RegionsExploredAtLeast, // amount (lands explored beyond the home clearing)
        DistinctJobsAtLeast,    // amount (jobs with at least one worker)
        FoodBalancePositive,    // net food rate > 0
        YearAtLeast,            // amount (game years since founding; one year = four seasons)
        RoadCellsAtLeast,       // amount
        BeautyAtLeast,          // amount (sum of decoration beauty)
        ChapterAtLeast,         // amount (story chapter number)
        HeartLevelAtLeast,      // amount (level of the settlement's heart: campfire, then hearth)
        BuildingLevelAtLeast,   // id, amount (a completed building of that id at this level or higher)
        ColdPeopleAtMost,       // amount (people living in cold or freezing homes, or without one)
        SickAtMost,             // amount (people who are ill)
        LawEnacted,             // id
        DiscontentAtMost,       // amount
        MadeAtLeast,            // resource, amount (made by recipes since the founding, e.g. bronze cast)
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
        /// <summary>Housing quality: raises approval for the people living here (0 = bare shelter).</summary>
        public int? Comfort;
        /// <summary>Gathering place value (hearths, circles, lodges): raises approval for everyone.</summary>
        public int? Community;
        /// <summary>Food source kind counted for food variety (e.g. "fish", "grain", "meat").</summary>
        public string FoodSource;
        /// <summary>Animals kept here (herding buildings): cow, sheep, pig, horse, llama.</summary>
        public List<string> Animals = new List<string>();
        /// <summary>Upgrades to level 2, 3… (index 0 = cost of level 2). Each level adds 25 % capacity.</summary>
        public List<LevelDefinition> Levels = new List<LevelDefinition>();
        public int MaxLevel => 1 + Levels.Count;
        /// <summary>The settlement's heart (the campfire, later the hearth): its level opens the eras,
        /// and no other building may rise above it (as the furnace in Whiteout Survival).</summary>
        public bool Heart;
        /// <summary>How the building looks from a level on (a tent becomes a hut, a cottage, a house).</summary>
        public List<LevelLook> Looks = new List<LevelLook>();
        /// <summary>How much warmer than outside it is inside this home (°C), before levels.</summary>
        public double Insulation;
        /// <summary>A fire that warms the homes around it (the heart's own warmth is set in balance.warmth).</summary>
        public HeatSource Heat;
        /// <summary>Lasting effects while the building stands and works (a herb garden keeps sickness away, a
        /// lookout speeds the scouts). Each finished building of the kind adds its effects once.</summary>
        public List<Effect> Effects = new List<Effect>();
        /// <summary>A market: homes within reach can get household goods (higher house tiers need it).</summary>
        public bool Market;
        /// <summary>A source of water for the homes around it (a well house).</summary>
        public bool Water;
        /// <summary>Trade routes to the neighbours this building keeps running (a trading post, a market hall).</summary>
        public int TradeRoutes;
        /// <summary>A monument's parts, added one by one once the plot is laid out (empty for ordinary buildings).</summary>
        public List<MonumentPart> Parts = new List<MonumentPart>();
        public bool IsMonument => Parts.Count > 0;
        public MonumentPart Part(string id) => Parts.FirstOrDefault(p => p.Id == id);

        /// <summary>The model shown at a level: the last look whose level has been reached.</summary>
        public string SpriteAt(int level)
        {
            string sprite = Sprite;
            foreach (var look in Looks) if (level >= look.Level) sprite = look.Sprite;
            return sprite;
        }

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
                Comfort = o.OptInt("comfort"),
                Community = o.OptInt("community"),
                FoodSource = o.OptStr("foodSource"),
                Animals = o.Has("animals") ? o.Strs("animals") : new List<string>(),
                Levels = o.Has("levels") ? o.Objs("levels").Select(LevelDefinition.FromJson).ToList() : new List<LevelDefinition>(),
                Heart = o.OptBool("heart", false),
                Looks = o.Has("looks") ? o.Objs("looks").Select(l => new LevelLook { Level = l.Int("level"), Sprite = l.Str("sprite"), Name = l.OptStr("name") })
                                                    .OrderBy(l => l.Level).ToList() : new List<LevelLook>(),
                Insulation = o.OptNum("insulation") ?? 0,
                Heat = o.Has("heat") ? HeatSource.FromJson(o.Obj("heat")) : null,
                Effects = o.Has("effects") ? o.Objs("effects").Select(Effect.FromJson).ToList() : new List<Effect>(),
                Market = o.OptBool("market", false),
                Water = o.OptBool("water", false),
                TradeRoutes = o.OptInt("tradeRoutes") ?? 0,
                Parts = o.Has("parts") ? o.Objs("parts").Select(MonumentPart.FromJson).ToList() : new List<MonumentPart>(),
            };
        }
    }

    /// <summary>
    /// One part of a monument (a manor's hall, an abbey's cloister): built on the monument's plot after the parts it
    /// needs, each with its own cost, building time and lasting effects.
    /// </summary>
    public sealed class MonumentPart
    {
        public string Id, Name, Description;
        public ResourceBundle Cost;
        public double BuildSeconds;
        /// <summary>Parts that must stand first (the joins of the monument's plan).</summary>
        public List<string> Requires = new List<string>();
        public List<Effect> Effects = new List<Effect>();
        /// <summary>Gathering-place points (approval), like a building's community.</summary>
        public int Community;
        /// <summary>The estate the part pleases, and the favour per hour it brings.</summary>
        public string Estate;
        public double Favour;

        internal static MonumentPart FromJson(JObj o) => new MonumentPart
        {
            Id = o.Str("id"), Name = o.Str("name"), Description = o.OptStr("description") ?? "",
            Cost = ResourceBundle.FromJson(o.OptObj("cost")), BuildSeconds = o.Num("buildSeconds"),
            Requires = o.Has("requires") ? o.Strs("requires") : new List<string>(),
            Effects = o.Has("effects") ? o.Objs("effects").Select(Effect.FromJson).ToList() : new List<Effect>(),
            Community = o.OptInt("community") ?? 0, Estate = o.OptStr("estate"), Favour = o.OptNum("favour") ?? 0,
        };
    }

    /// <summary>A fire that warms the cells around it and burns firewood while it does.</summary>
    public sealed class HeatSource
    {
        /// <summary>°C added at the fire; it fades to half at the edge of the radius.</summary>
        public double Heat;
        /// <summary>Cells reached.</summary>
        public double Radius;
        public double WoodPerSecond;

        internal static HeatSource FromJson(JObj o) => new HeatSource
        {
            Heat = o.Num("heat"), Radius = o.Num("radius"), WoodPerSecond = o.OptNum("woodPerSecond") ?? 0,
        };
    }

    public sealed class LevelDefinition
    {
        public ResourceBundle Cost;
        public double BuildSeconds;
        /// <summary>What must be true before this level can be built (people, research, other buildings).</summary>
        public List<Condition> Requirements = new List<Condition>();

        internal static LevelDefinition FromJson(JObj o) => new LevelDefinition
        {
            Cost = ResourceBundle.FromJson(o.OptObj("cost")),
            BuildSeconds = o.Num("buildSeconds"),
            Requirements = o.Has("requirements") ? o.Objs("requirements").Select(Condition.FromJson).ToList() : new List<Condition>(),
        };
    }

    /// <summary>From this level on the building is drawn with another model (and may take another name).</summary>
    public sealed class LevelLook
    {
        public int Level;
        public string Sprite, Name;
    }

    /// <summary>
    /// A kind of natural resource to gather from the land: trees, rock, ore, berry bushes, fish,
    /// game. Density below 1 means only some cells of the terrain hold it (e.g. berry bushes on
    /// grass). Cells that share a terrain use different slots so their stocks are kept apart.
    /// </summary>
    public sealed class NodeDefinition
    {
        public string Id, Name;
        public TerrainType Terrain;
        public ResourceType Resource;
        public JobType Job;
        public double Capacity;
        /// <summary>Seconds until a used-up cell is full again (0 = never).</summary>
        public double RegrowSeconds;
        public double Density = 1;
        public int Slot;
        /// <summary>Cutting the last of it clears the land (a felled forest is open ground).</summary>
        public bool ClearsTerrain;

        internal static NodeDefinition FromJson(JObj o) => new NodeDefinition
        {
            Id = o.Str("id"),
            Name = o.OptStr("name") ?? o.Str("id"),
            Terrain = Enums.ParseTerrain(o.Str("terrain")),
            Resource = ResourceTypes.Parse(o.Str("resource")),
            Job = Enums.ParseJob(o.Str("job")),
            Capacity = o.Num("capacity"),
            RegrowSeconds = o.OptNum("regrowSeconds") ?? 0,
            Density = o.OptNum("density") ?? 1,
            Slot = o.OptInt("slot") ?? 0,
        };
    }

    /// <summary>A perk a worker earns at a skill level of a job (Foundation-style specialists).</summary>
    public sealed class PerkDefinition
    {
        public int Level;
        public string Name, Description;
        /// <summary>Extra output of the person who has the perk (0.1 = +10 %).</summary>
        public double Bonus;
        /// <summary>Settlement-wide effects while at least one person has the perk.</summary>
        public List<Effect> Effects = new List<Effect>();

        internal static PerkDefinition FromJson(JObj o) => new PerkDefinition
        {
            Level = o.Int("level"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Bonus = o.OptNum("bonus") ?? 0,
            Effects = o.Has("effects") ? o.Objs("effects").Select(Effect.FromJson).ToList() : new List<Effect>(),
        };
    }

    /// <summary>The skill of one job: a title per rank and the perks on the way to mastery.</summary>
    public sealed class SkillDefinition
    {
        public JobType Job;
        public string Title;
        public List<PerkDefinition> Perks = new List<PerkDefinition>();

        internal static SkillDefinition FromJson(JObj o) => new SkillDefinition
        {
            Job = Enums.ParseJob(o.Str("job")),
            Title = o.Str("title"),
            Perks = o.Objs("perks").Select(PerkDefinition.FromJson).OrderBy(p => p.Level).ToList(),
        };
    }

    /// <summary>Roads, paths, fences, walls, lanterns, plants and monuments placed freely on cells.</summary>
    public sealed class DecorationDefinition
    {
        public string Id, Name, Description, Category, Sprite;
        public ResourceBundle Cost;
        /// <summary>Beauty adds to approval (Foundation's splendor).</summary>
        public int Beauty;
        public string RequiresTech;
        public List<TerrainType> AllowedTerrain = new List<TerrainType>();
        /// <summary>Roads: villagers walk faster and adjacent workplaces work better.</summary>
        public bool IsRoad;
        /// <summary>Bridges span water and must touch land or another bridge.</summary>
        public bool IsBridge;
        /// <summary>Fences and walls: people walk around them.</summary>
        public bool Blocks;
        /// <summary>Gives water to the homes around it (the well).</summary>
        public bool Water;
        /// <summary>Braziers and fire pits warm the homes around them.</summary>
        public HeatSource Heat;

        internal static DecorationDefinition FromJson(JObj o) => new DecorationDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.OptStr("description") ?? "",
            Category = o.Str("category"),
            Sprite = o.OptStr("sprite") ?? o.Str("id"),
            Cost = ResourceBundle.FromJson(o.OptObj("cost")),
            Beauty = o.OptInt("beauty") ?? 0,
            RequiresTech = o.OptStr("requiresTech"),
            AllowedTerrain = o.Has("allowedTerrain") ? o.Strs("allowedTerrain").Select(Enums.ParseTerrain).ToList()
                                                     : new List<TerrainType> { TerrainType.Grass, TerrainType.Sand, TerrainType.Forest },
            IsRoad = o.OptBool("road", false),
            IsBridge = o.OptBool("bridge", false),
            Water = o.OptBool("water", false),
            Blocks = o.OptBool("blocks", false),
            Heat = o.Has("heat") ? HeatSource.FromJson(o.Obj("heat")) : null,
        };
    }

    public sealed class TechnologyDefinition
    {
        public string Id, Name, Description;
        /// <summary>Optional: the era (epoch) the settlement must have reached before this can be studied.</summary>
        public string Epoch;
        /// <summary>Optional: the heart (campfire/hearth) must have reached this level first.</summary>
        public int? HeartLevel;
        public ResourceBundle Cost;
        public double ResearchSeconds;
        public List<string> Prerequisites = new List<string>();
        public List<Effect> Effects = new List<Effect>();

        internal static TechnologyDefinition FromJson(JObj o) => new TechnologyDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Epoch = o.OptStr("epoch"),
            HeartLevel = o.OptInt("heartLevel"),
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
        /// <summary>Optional: the milestone opens a new era (epoch).</summary>
        public string NewEpoch;
        public List<BuildingTransform> BuildingTransforms = new List<BuildingTransform>();
        public string LegacyText;

        internal static MilestoneDefinition FromJson(JObj o) => new MilestoneDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Requirements = o.Objs("requirements").Select(Condition.FromJson).ToList(),
            NewStage = o.Str("newStage"),
            NewEpoch = o.OptStr("newEpoch"),
            BuildingTransforms = o.Objs("buildingTransforms").Select(t => new BuildingTransform { From = t.Str("from"), To = t.Str("to") }).ToList(),
            LegacyText = o.Str("legacyText"),
        };
    }

    /// <summary>
    /// A chapter of the founders' story. Each chapter is one generation: it opens with a story
    /// scene and ends when its requirements are met, which always include a minimum game year,
    /// so the eras arrive slowly. Locked chapters (eras still in development) are shown but
    /// cannot be reached yet.
    /// </summary>
    public sealed class ChapterDefinition
    {
        public string Id, Title, Era, Story, Goal, LegacyText;
        public int Number;
        public bool Locked;
        public List<Condition> Requirements = new List<Condition>();

        internal static ChapterDefinition FromJson(JObj o) => new ChapterDefinition
        {
            Id = o.Str("id"),
            Number = o.Int("number"),
            Title = o.Str("title"),
            Era = o.Str("era"),
            Story = o.Str("story"),
            Goal = o.Str("goal"),
            LegacyText = o.OptStr("legacyText"),
            Locked = o.OptBool("locked", false),
            Requirements = o.Has("requirements") ? o.Objs("requirements").Select(Condition.FromJson).ToList() : new List<Condition>(),
        };
    }

    public sealed class RegionDefinition
    {
        public string Id, Name, Description;
        public int X, Y, Width, Height;
        public bool RevealedAtStart;
        /// <summary>The land around the campfire: it moves to wherever the player founds the settlement.</summary>
        public bool Home;
        public ResourceBundle Cost;
        public double ExpeditionSeconds;
        public string LegacyText;
        /// <summary>People found out there who join the settlement (Frostpunk's survivors).</summary>
        public int Survivors;
        /// <summary>Supplies the scouts bring back.</summary>
        public ResourceBundle Loot;
        /// <summary>What the scouts tell when they return.</summary>
        public string Finding;

        public bool Contains(int cx, int cy) => cx >= X && cx < X + Width && cy >= Y && cy < Y + Height;

        internal static RegionDefinition FromJson(JObj o) => new RegionDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.Str("description"),
            X = o.Int("x"), Y = o.Int("y"), Width = o.Int("width"), Height = o.Int("height"),
            RevealedAtStart = o.Bool("revealedAtStart"),
            Home = o.OptBool("home", false),
            Cost = ResourceBundle.FromJson(o.OptObj("cost")),
            ExpeditionSeconds = o.Num("expeditionSeconds"),
            LegacyText = o.Str("legacyText"),
            Survivors = o.OptInt("survivors") ?? 0,
            Loot = ResourceBundle.FromJson(o.OptObj("loot")),
            Finding = o.OptStr("finding"),
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
        /// <summary>The name players see (generated valleys); null = made from <see cref="Name"/>.</summary>
        public string Title;
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
        /// <summary>A second good made alongside the output (hunters bring hides).</summary>
        public ResourceType? Byproduct;
        public double ByproductRatio;
        /// <summary>
        /// A recipe: what one unit of output takes (e.g. smelting: 2 ore + 1 wood → 1 bronze). A job with inputs
        /// only works while the stockpile holds them and there is room for the output; see <see cref="Game.Processing"/>.
        /// </summary>
        public ResourceBundle Inputs;
        public bool IsRecipe => !Inputs.IsZero;

        internal static JobConfig FromJson(JObj o) => new JobConfig
        {
            Job = Enums.ParseJob(o.Str("job")),
            Name = o.Str("name"),
            Description = o.Str("description"),
            Output = o.Has("output") ? ResourceTypes.Parse(o.Str("output")) : (ResourceType?)null,
            RatePerWorkerPerSecond = o.Num("ratePerWorkerPerSecond"),
            UnlimitedSlots = o.Bool("unlimitedSlots"),
            Byproduct = o.Has("byproduct") ? ResourceTypes.Parse(o.Str("byproduct")) : (ResourceType?)null,
            ByproductRatio = o.OptNum("byproductRatio") ?? 0,
            Inputs = o.Has("inputs") ? ResourceBundle.FromJson(o.Obj("inputs")) : ResourceBundle.Zero,
        };
    }

    /// <summary>One season of the year and how it changes the economy.</summary>
    public sealed class SeasonDefinition
    {
        public string Id, Name, Description;
        public double Foraging = 1, Farming = 1, Growth = 1, Fishing = 1, Hunting = 1;
        /// <summary>Wood burnt per person per second to keep warm.</summary>
        public double FuelPerPersonPerSecond;

        internal static SeasonDefinition FromJson(JObj o) => new SeasonDefinition
        {
            Id = o.Str("id"),
            Name = o.Str("name"),
            Description = o.OptStr("description") ?? "",
            Foraging = o.OptNum("foraging") ?? 1,
            Farming = o.OptNum("farming") ?? 1,
            Growth = o.OptNum("growth") ?? 1,
            Fishing = o.OptNum("fishing") ?? 1,
            Hunting = o.OptNum("hunting") ?? 1,
            FuelPerPersonPerSecond = o.OptNum("fuelPerPersonPerSecond") ?? 0,
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
        /// <summary>Most builders that can work on one site at once (0 = no limit).</summary>
        public double MaxBuildersPerSite;
        public double CraftingBonusPerCrafter;
        public double MaxCraftingBonus;
        public ResourceBundle CraftingInputPerCrafterPerSecond;
        /// <summary>Extra output of every gatherer while there are tools in store (0.15 = +15 %).</summary>
        public double ToolBonus;
        /// <summary>Tools each working gatherer wears out per second (they are used up as they go).</summary>
        public double ToolWearPerWorkerPerSecond;
        public double MaxOfflineSeconds;
        public double SimulationStepSeconds;
        public List<JobConfig> Jobs = new List<JobConfig>();
        /// <summary>Length of one season in seconds of game time (0 disables seasons).</summary>
        public double SeasonSeconds;
        public List<SeasonDefinition> Seasons = new List<SeasonDefinition>();
        /// <summary>Whether approval changes work speed and growth (off in some rule tests).</summary>
        public bool ApprovalAffectsWork = true;
        /// <summary>Extra output per skill level (0–5) of each worker.</summary>
        public double SkillBonusPerLevel = 0.1;
        /// <summary>Seconds of work in a job to reach skill level 1 (level n needs n² times this).</summary>
        public double SkillSecondsPerLevel = 120;
        /// <summary>Extra output for workplaces next to a road.</summary>
        public double RoadWorkBonus = 0.1;
        /// <summary>Wood in one forest cell, stone in one rock cell, ore in one ore cell (0 = endless).</summary>
        public double ForestWood = 0, RockStone = 0, VeinOre = 0;
        /// <summary>Seconds until a cut forest cell has grown back.</summary>
        public double TreeRegrowSeconds = 1440;
        /// <summary>How far (cells) gatherers walk from their workplace to find trees, rock or ore.</summary>
        public int GatherReach = 7;
        /// <summary>Highest skill level a person can reach in a job.</summary>
        public int MaxSkillLevel = 10;
        /// <summary>Food, game and fish to gather besides wood, stone and ore.</summary>
        public List<NodeDefinition> Nodes = new List<NodeDefinition>();
        /// <summary>Weather: temperatures through the seasons and the day, cold snaps (null = always mild).</summary>
        public ClimateConfig Climate;
        /// <summary>How warm homes are and how the heart warms them (null = no cold homes).</summary>
        public WarmthConfig Warmth;
        /// <summary>Illness, healing and death (null = nobody falls ill).</summary>
        public HealthConfig Health;
        /// <summary>Discontent, petitions and laws' cooldown (null = always content).</summary>
        public DiscontentConfig Discontent;
        /// <summary>Needs, house tiers and taxes (null = off).</summary>
        public NeedsConfig Needs;
        /// <summary>Most coins the treasury holds.</summary>
        public double TreasuryCap;
        /// <summary>Trade with the neighbouring villages (null = no trade).</summary>
        public TradeConfig Trade;
        /// <summary>The three estates of the realm (null = none).</summary>
        public EstatesConfig Estates;
        /// <summary>Paths worn by people's feet (null = none).</summary>
        public PathsConfig Paths;
        /// <summary>Zones painted with the brush (null = none).</summary>
        public ZonesConfig Zones;

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
            MaxBuildersPerSite = o.OptNum("maxBuildersPerSite") ?? 0,
            CraftingBonusPerCrafter = o.Num("craftingBonusPerCrafter"),
            MaxCraftingBonus = o.Num("maxCraftingBonus"),
            CraftingInputPerCrafterPerSecond = ResourceBundle.FromJson(o.Obj("craftingInputPerCrafterPerSecond")),
            ToolBonus = o.OptNum("toolBonus") ?? 0,
            ToolWearPerWorkerPerSecond = o.OptNum("toolWearPerWorkerPerSecond") ?? 0,
            MaxOfflineSeconds = o.Num("maxOfflineSeconds"),
            SimulationStepSeconds = o.Num("simulationStepSeconds"),
            Jobs = o.Objs("jobs").Select(JobConfig.FromJson).ToList(),
            SeasonSeconds = o.OptNum("seasonSeconds") ?? 0,
            ApprovalAffectsWork = !o.Has("approvalAffectsWork") || o.Bool("approvalAffectsWork"),
            SkillBonusPerLevel = o.OptNum("skillBonusPerLevel") ?? 0.1,
            SkillSecondsPerLevel = o.OptNum("skillSecondsPerLevel") ?? 120,
            RoadWorkBonus = o.OptNum("roadWorkBonus") ?? 0.1,
            ForestWood = o.OptNum("forestWood") ?? 0,
            RockStone = o.OptNum("rockStone") ?? 0,
            VeinOre = o.OptNum("veinOre") ?? 0,
            TreeRegrowSeconds = o.OptNum("treeRegrowSeconds") ?? 1440,
            GatherReach = (int)(o.OptNum("gatherReach") ?? 7),
            MaxSkillLevel = o.OptInt("maxSkillLevel") ?? 10,
            Nodes = o.Has("nodes") ? o.Objs("nodes").Select(NodeDefinition.FromJson).ToList() : new List<NodeDefinition>(),
            Seasons = o.Has("seasons") ? o.Objs("seasons").Select(SeasonDefinition.FromJson).ToList() : new List<SeasonDefinition>(),
            Climate = o.Has("climate") ? ClimateConfig.FromJson(o.Obj("climate")) : null,
            Warmth = o.Has("warmth") ? WarmthConfig.FromJson(o.Obj("warmth")) : null,
            Health = o.Has("health") ? HealthConfig.FromJson(o.Obj("health")) : null,
            Needs = o.Has("needs") ? NeedsConfig.FromJson(o.Obj("needs")) : null,
            TreasuryCap = o.OptNum("treasuryCap") ?? 5000,
            Trade = o.Has("trade") ? TradeConfig.FromJson(o.Obj("trade")) : null,
            Estates = o.Has("estates") ? EstatesConfig.FromJson(o.Obj("estates")) : null,
            Paths = o.Has("paths") ? PathsConfig.FromJson(o.Obj("paths")) : null,
            Zones = o.Has("zones") ? ZonesConfig.FromJson(o.Obj("zones")) : null,
            Discontent = o.Has("discontent") ? DiscontentConfig.FromJson(o.Obj("discontent")) : null,
        };
    }

    /// <summary>A spell of hard weather that may fall on an hour of some seasons.</summary>
    public sealed class ColdSnapDefinition
    {
        public string Id, Name;
        /// <summary>Degrees colder than usual.</summary>
        public double Drop;
        /// <summary>Chance per weather period (0–1) in the listed seasons.</summary>
        public double Chance;
        public List<string> Seasons = new List<string>();

        internal static ColdSnapDefinition FromJson(JObj o) => new ColdSnapDefinition
        {
            Id = o.Str("id"), Name = o.Str("name"), Drop = o.Num("drop"), Chance = o.Num("chance"), Seasons = o.Strs("seasons"),
        };
    }

    /// <summary>
    /// The weather (Frostpunk's thermometer): each season has a usual temperature, the day is
    /// warmer than the night, every weather period (an hour) varies a little, and cold snaps
    /// fall on some periods. All of it follows from the settlement and the time alone, so it can
    /// be forecast.
    /// </summary>
    public sealed class ClimateConfig
    {
        /// <summary>Usual temperature (°C) per season id.</summary>
        public Dictionary<string, double> SeasonTemperature = new Dictionary<string, double>();
        /// <summary>Half the difference between the warmest afternoon and the coldest night.</summary>
        public double DailySwing;
        /// <summary>Length of a day in seconds (it matches the sky).</summary>
        public double DaySeconds;
        /// <summary>Length of one weather period in seconds.</summary>
        public double PeriodSeconds;
        /// <summary>Largest random change of a period (±°C).</summary>
        public double Noise;
        /// <summary>How many periods ahead the forecast shows.</summary>
        public int ForecastPeriods;
        public List<ColdSnapDefinition> ColdSnaps = new List<ColdSnapDefinition>();

        internal static ClimateConfig FromJson(JObj o)
        {
            var c = new ClimateConfig
            {
                DailySwing = o.Num("dailySwing"), DaySeconds = o.Num("daySeconds"), PeriodSeconds = o.Num("periodSeconds"),
                Noise = o.Num("noise"), ForecastPeriods = o.Int("forecastPeriods"),
                ColdSnaps = o.Objs("coldSnaps").Select(ColdSnapDefinition.FromJson).ToList(),
            };
            var t = o.Obj("seasonTemperature");
            foreach (var k in t.Keys) c.SeasonTemperature[k] = t.Num(k);
            return c;
        }
    }

    /// <summary>How warm a home feels, and the heart's warmth (grows with its level, as Frostpunk's generator).</summary>
    public sealed class WarmthConfig
    {
        /// <summary>At or above: comfortable. Below Cold: cold. Below Freezing: freezing. In between: chilly.</summary>
        public double Comfortable, Cold, Freezing;
        public double HeartHeat, HeartHeatPerLevel, HeartRadius, HeartRadiusPerLevel;
        /// <summary>Extra insulation per building level.</summary>
        public double InsulationPerLevel;
        /// <summary>Most warmth all fires together can add to one place.</summary>
        public double MaxHeat;
        /// <summary>Stoking the heart (Frostpunk's overdrive): how long, how long until it can be done again, and what it gives.</summary>
        public double StokeSeconds, StokeCooldownSeconds, StokeHeat, StokeRadius, StokeWoodPerSecond;
        public double StokeApproval;

        internal static WarmthConfig FromJson(JObj o) => new WarmthConfig
        {
            Comfortable = o.Num("comfortable"), Cold = o.Num("cold"), Freezing = o.Num("freezing"),
            HeartHeat = o.Num("heartHeat"), HeartHeatPerLevel = o.Num("heartHeatPerLevel"),
            HeartRadius = o.Num("heartRadius"), HeartRadiusPerLevel = o.Num("heartRadiusPerLevel"),
            InsulationPerLevel = o.Num("insulationPerLevel"), MaxHeat = o.Num("maxHeat"),
            StokeSeconds = o.Num("stokeSeconds"), StokeCooldownSeconds = o.Num("stokeCooldownSeconds"),
            StokeHeat = o.Num("stokeHeat"), StokeRadius = o.Num("stokeRadius"), StokeWoodPerSecond = o.Num("stokeWoodPerSecond"),
            StokeApproval = o.OptNum("stokeApproval") ?? 0,
        };
    }

    /// <summary>A house tier: from which home level it starts, what it needs and what it pays.</summary>
    public sealed class HouseTier
    {
        public int Level;
        public string Name;
        public bool NeedsWater, NeedsGoods;
        public double TaxPerPersonPerHour;

        internal static HouseTier FromJson(JObj o)
        {
            var needs = o.Has("needs") ? o.Strs("needs") : new List<string>();
            return new HouseTier
            {
                Level = o.Int("level"), Name = o.Str("name"), NeedsWater = needs.Contains("water"), NeedsGoods = needs.Contains("goods"),
                TaxPerPersonPerHour = o.OptNum("taxPerPersonPerHour") ?? 0,
            };
        }
    }

    /// <summary>A tax level the player picks: how much it raises and how people take it.</summary>
    public sealed class TaxLevel
    {
        public string Id, Name;
        public double Multiplier;
        public int Approval;

        internal static TaxLevel FromJson(JObj o) => new TaxLevel
        {
            Id = o.Str("id"), Name = o.Str("name"), Multiplier = o.Num("multiplier"), Approval = o.OptInt("approval") ?? 0,
        };
    }

    /// <summary>Needs of the homes (Manor Lords, Foundation): water and goods for the higher tiers, and taxes.</summary>
    public sealed class NeedsConfig
    {
        /// <summary>Cells from a home to a well (or water building), to the river or lake, and to a market.</summary>
        public double WaterReach, RiverReach, MarketReach;
        public double GoodsPerPersonPerSecond;
        /// <summary>Approval when every home has what it needs, and lost when none has.</summary>
        public double MetApproval, UnmetApproval;
        public List<HouseTier> Tiers = new List<HouseTier>();
        public List<TaxLevel> TaxLevels = new List<TaxLevel>();
        public string DefaultTax;

        internal static NeedsConfig FromJson(JObj o) => new NeedsConfig
        {
            WaterReach = o.Num("waterReach"), RiverReach = o.Num("riverReach"), MarketReach = o.Num("marketReach"),
            GoodsPerPersonPerSecond = o.Num("goodsPerPersonPerSecond"),
            MetApproval = o.Num("metApproval"), UnmetApproval = o.Num("unmetApproval"),
            Tiers = o.Objs("tiers").Select(HouseTier.FromJson).OrderBy(t => t.Level).ToList(),
            TaxLevels = o.Objs("taxLevels").Select(TaxLevel.FromJson).ToList(),
            DefaultTax = o.OptStr("defaultTax") ?? "normal",
        };
    }

    /// <summary>Trade with the neighbouring villages (Manor Lords' trade routes): one-off deals and lasting routes.</summary>
    public sealed class TradeConfig
    {
        /// <summary>Goods a route carries each minute.</summary>
        public double RouteAmountPerMinute;
        /// <summary>How many goods one deal moves.</summary>
        public double DealAmount;
        public List<NeighbourDefinition> Neighbours = new List<NeighbourDefinition>();

        internal static TradeConfig FromJson(JObj o) => new TradeConfig
        {
            RouteAmountPerMinute = o.Num("routeAmountPerMinute"),
            DealAmount = o.OptNum("dealAmount") ?? 10,
            Neighbours = o.Objs("neighbours").Select(NeighbourDefinition.FromJson).ToList(),
        };
    }

    /// <summary>A neighbouring village: what it pays for goods, what it sells, and what a lasting route costs.</summary>
    public sealed class NeighbourDefinition
    {
        public string Id, Name, Description;
        /// <summary>Lands the scouts must have explored before the village is met.</summary>
        public int LandsExplored;
        /// <summary>Coins paid per unit for the town's goods, and asked per unit for theirs.</summary>
        public ResourceBundle Buys, Sells;
        /// <summary>Coins to open a lasting route with them.</summary>
        public double RouteCost;

        internal static NeighbourDefinition FromJson(JObj o) => new NeighbourDefinition
        {
            Id = o.Str("id"), Name = o.Str("name"), Description = o.OptStr("description") ?? "",
            LandsExplored = o.OptInt("landsExplored") ?? 0,
            Buys = ResourceBundle.FromJson(o.OptObj("buys")), Sells = ResourceBundle.FromJson(o.OptObj("sells")),
            RouteCost = o.OptNum("routeCost") ?? 0,
        };
    }

    /// <summary>Zones painted with the brush: homes, woodcutting, forestry and pastures.</summary>
    public sealed class ZonesConfig
    {
        /// <summary>Game seconds between the villagers' looks at the homes zones.</summary>
        public double CheckSeconds;
        /// <summary>Villagers build a new home when this many beds or fewer are free.</summary>
        public int FreeBeds;
        /// <summary>Painted pasture cells per extra herder.</summary>
        public int PastureCellsPerHerder;
        /// <summary>How much faster felled trees grow back in a forestry zone.</summary>
        public double ForestryRegrowMultiplier;

        internal static ZonesConfig FromJson(JObj o) => new ZonesConfig
        {
            CheckSeconds = o.OptNum("checkSeconds") ?? 60, FreeBeds = o.OptInt("freeBeds") ?? 1,
            PastureCellsPerHerder = o.OptInt("pastureCellsPerHerder") ?? 0, ForestryRegrowMultiplier = o.OptNum("forestryRegrowMultiplier") ?? 1,
        };
    }

    /// <summary>Paths worn by people's feet (Manor Lords' organic towns).</summary>
    public sealed class PathsConfig
    {
        /// <summary>Wear a cell gets per person walking that way each hour; a path from this much wear; most wear kept.</summary>
        public double WearPerPersonPerHour, Threshold, MaxWear;
        /// <summary>Share of the wear that grasses over each hour.</summary>
        public double DecayPerHour;
        /// <summary>Extra construction speed on sites beside a path (0.1 = +10%).</summary>
        public double ConstructionBonus;
        /// <summary>How much cheaper walking a path is than open ground (1 = the same).</summary>
        public double WalkCost;

        internal static PathsConfig FromJson(JObj o) => new PathsConfig
        {
            WearPerPersonPerHour = o.Num("wearPerPersonPerHour"), Threshold = o.Num("threshold"), MaxWear = o.Num("maxWear"),
            DecayPerHour = o.OptNum("decayPerHour") ?? 0, ConstructionBonus = o.OptNum("constructionBonus") ?? 0,
            WalkCost = o.OptNum("walkCost") ?? 1,
        };
    }

    /// <summary>The three estates of the realm (Manor Lords' development paths): labour, clergy and the crown.</summary>
    public sealed class EstatesConfig
    {
        /// <summary>Coins a gift to an estate costs, and the favour it brings.</summary>
        public double GrantCost, GrantFavour;
        /// <summary>Ranks between the most and the least favoured estate before the least grumbles.</summary>
        public int ImbalanceRanks;
        /// <summary>Approval lost while one estate is left behind, and won while all stand together (rank 1 or more).</summary>
        public int ImbalanceApproval, HarmonyApproval;
        public List<EstateDefinition> Estates = new List<EstateDefinition>();

        internal static EstatesConfig FromJson(JObj o) => new EstatesConfig
        {
            GrantCost = o.Num("grantCost"), GrantFavour = o.Num("grantFavour"),
            ImbalanceRanks = o.OptInt("imbalanceRanks") ?? 2,
            ImbalanceApproval = o.OptInt("imbalanceApproval") ?? 0, HarmonyApproval = o.OptInt("harmonyApproval") ?? 0,
            Estates = o.Objs("estates").Select(EstateDefinition.FromJson).ToList(),
        };
    }

    /// <summary>An estate: where its favour comes from, and what each rank of favour grants the town.</summary>
    public sealed class EstateDefinition
    {
        public string Id, Name, Description, Icon;
        /// <summary>Favour per hour from each finished building of a kind (building id → favour).</summary>
        public Dictionary<string, double> Buildings = new Dictionary<string, double>();
        /// <summary>Favour per hour from each worker at a job.</summary>
        public Dictionary<JobType, double> Jobs = new Dictionary<JobType, double>();
        /// <summary>Favour per coin of tax collected, and per hour from each trade route that runs.</summary>
        public double TaxShare, RouteFavour;
        public List<EstateRank> Ranks = new List<EstateRank>();

        internal static EstateDefinition FromJson(JObj o)
        {
            var e = new EstateDefinition
            {
                Id = o.Str("id"), Name = o.Str("name"), Description = o.OptStr("description") ?? "", Icon = o.OptStr("icon") ?? "approval",
                TaxShare = o.OptNum("taxShare") ?? 0, RouteFavour = o.OptNum("routeFavour") ?? 0,
                Ranks = o.Objs("ranks").Select(EstateRank.FromJson).OrderBy(r => r.Favour).ToList(),
            };
            var b = o.OptObj("buildings");
            if (b != null) foreach (var k in b.Keys) e.Buildings[k] = b.Num(k);
            var j = o.OptObj("jobs");
            if (j != null) foreach (var k in j.Keys) e.Jobs[Enums.ParseJob(k)] = j.Num(k);
            return e;
        }
    }

    public sealed class EstateRank
    {
        public double Favour;
        public string Name, Description;
        public List<Effect> Effects = new List<Effect>();

        internal static EstateRank FromJson(JObj o) => new EstateRank
        {
            Favour = o.Num("favour"), Name = o.Str("name"), Description = o.OptStr("description") ?? "",
            Effects = o.Has("effects") ? o.Objs("effects").Select(Effect.FromJson).ToList() : new List<Effect>(),
        };
    }

    /// <summary>Illness and healing (Frostpunk's sick and its medical posts).</summary>
    public sealed class HealthConfig
    {
        /// <summary>Chance per second for one person to fall ill: per cold, per freezing, per hungry person.</summary>
        public double ColdRate, FreezingRate, HungerRate;
        /// <summary>Seconds to get well: untreated (in a warm enough home) and under a healer's care.</summary>
        public double RecoverSeconds, TreatedRecoverSeconds;
        /// <summary>How many sick one healer can look after.</summary>
        public int PatientsPerHealer;
        /// <summary>Seconds of untreated illness in the cold (or hunger) until it kills; freezing is twice as fast.</summary>
        public double DeathSeconds;
        /// <summary>Approval lost for a while when someone dies, and for how long.</summary>
        public double GriefApproval, GriefSeconds;

        internal static HealthConfig FromJson(JObj o) => new HealthConfig
        {
            ColdRate = o.Num("coldRate"), FreezingRate = o.Num("freezingRate"), HungerRate = o.Num("hungerRate"),
            RecoverSeconds = o.Num("recoverSeconds"), TreatedRecoverSeconds = o.Num("treatedRecoverSeconds"),
            PatientsPerHealer = o.Int("patientsPerHealer"), DeathSeconds = o.Num("deathSeconds"),
            GriefApproval = o.Num("griefApproval"), GriefSeconds = o.Num("griefSeconds"),
        };
    }

    /// <summary>Discontent (Frostpunk's second meter): what stirs it, what calms it, and what it does.</summary>
    public sealed class DiscontentConfig
    {
        /// <summary>Points per hour while the whole settlement suffers it (scaled by the share of people).</summary>
        public double Hunger, Cold, Freezing, Sick, Homeless;
        /// <summary>Points per hour it settles by itself, and extra calm per approval point above CalmAbove.</summary>
        public double Calm, CalmAbove, CalmPerApproval;
        /// <summary>Points added at once when someone dies.</summary>
        public double Death;
        /// <summary>Grumbling and unrest thresholds and the work they cost.</summary>
        public double Grumbling, GrumblingWork, Unrest, UnrestWork;
        /// <summary>At the top, a family leaves every this many seconds.</summary>
        public double LeaveSeconds;
        /// <summary>Seconds between two laws.</summary>
        public double LawCooldownSeconds;
        /// <summary>People petition when discontent reaches this, at most once per PetitionCooldownSeconds.</summary>
        public double PetitionAt, PetitionCooldownSeconds;
        /// <summary>Discontent change when a petition is promised, kept, broken or refused.</summary>
        public double PromiseRelief, KeptRelief, BrokenAnger, RefusedAnger;
        /// <summary>Approval for a while after a promise is kept.</summary>
        public double KeptApproval, KeptSeconds;

        internal static DiscontentConfig FromJson(JObj o) => new DiscontentConfig
        {
            Hunger = o.Num("hunger"), Cold = o.Num("cold"), Freezing = o.Num("freezing"), Sick = o.Num("sick"), Homeless = o.Num("homeless"),
            Calm = o.Num("calm"), CalmAbove = o.Num("calmAbove"), CalmPerApproval = o.Num("calmPerApproval"), Death = o.Num("death"),
            Grumbling = o.Num("grumbling"), GrumblingWork = o.Num("grumblingWork"), Unrest = o.Num("unrest"), UnrestWork = o.Num("unrestWork"),
            LeaveSeconds = o.Num("leaveSeconds"), LawCooldownSeconds = o.Num("lawCooldownSeconds"),
            PetitionAt = o.Num("petitionAt"), PetitionCooldownSeconds = o.Num("petitionCooldownSeconds"),
            PromiseRelief = o.Num("promiseRelief"), KeptRelief = o.Num("keptRelief"), BrokenAnger = o.Num("brokenAnger"), RefusedAnger = o.Num("refusedAnger"),
            KeptApproval = o.Num("keptApproval"), KeptSeconds = o.Num("keptSeconds"),
        };
    }

    /// <summary>
    /// A custom of the settlement (Frostpunk's Book of Laws): signed once, it lasts. Some pairs
    /// exclude each other, some follow from another, and the heart's level opens them.
    /// </summary>
    public sealed class LawDefinition
    {
        public string Id, Name, Description, Branch;
        public int HeartLevel;
        public string RequiresLaw, RequiresBuilding;
        public List<string> Excludes = new List<string>();
        /// <summary>Lasting effects while the law stands (approval bonuses here are permanent).</summary>
        public List<Effect> Effects = new List<Effect>();
        /// <summary>Discontent change the moment it is signed.</summary>
        public double OnSign;
        public string LegacyText;

        internal static LawDefinition FromJson(JObj o) => new LawDefinition
        {
            Id = o.Str("id"), Name = o.Str("name"), Description = o.Str("description"), Branch = o.Str("branch"),
            HeartLevel = o.OptInt("heartLevel") ?? 1,
            RequiresLaw = o.OptStr("requiresLaw"), RequiresBuilding = o.OptStr("requiresBuilding"),
            Excludes = o.Has("excludes") ? o.Strs("excludes") : new List<string>(),
            Effects = o.Has("effects") ? o.Objs("effects").Select(Effect.FromJson).ToList() : new List<Effect>(),
            OnSign = o.OptNum("onSign") ?? 0,
            LegacyText = o.OptStr("legacyText"),
        };
    }

    /// <summary>What the people demand when discontent runs high (Frostpunk's promises).</summary>
    public sealed class PetitionDefinition
    {
        public string Id, Title, Text, Promise;
        /// <summary>The promise is kept when all of these hold before the deadline.</summary>
        public List<Condition> Goal = new List<Condition>();
        public double DeadlineSeconds;

        internal static PetitionDefinition FromJson(JObj o) => new PetitionDefinition
        {
            Id = o.Str("id"), Title = o.Str("title"), Text = o.Str("text"), Promise = o.Str("promise"),
            Goal = o.Objs("goal").Select(Condition.FromJson).ToList(),
            DeadlineSeconds = o.Num("deadlineSeconds"),
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
        public List<DecorationDefinition> Decorations = new List<DecorationDefinition>();
        public List<ChapterDefinition> Chapters = new List<ChapterDefinition>();
        public List<SkillDefinition> Skills = new List<SkillDefinition>();
        public List<LawDefinition> Laws = new List<LawDefinition>();
        public List<PetitionDefinition> Petitions = new List<PetitionDefinition>();
        public MapDefinition Map;
        /// <summary>The content as loaded, when this copy carries a generated valley instead (see <see cref="ForMap"/>).</summary>
        public GameContent Base { get; private set; }

        /// <summary>
        /// The content for a settlement's map: the hand-drawn valley as loaded, or a valley drawn by
        /// <see cref="MapGenerator"/> from the settings in its name. An unknown name gives the loaded content
        /// (a save from an unknown map then fails validation as before).
        /// </summary>
        public GameContent ForMap(string mapName)
        {
            var root = Base ?? this;
            if (string.IsNullOrEmpty(mapName) || mapName == Map.Name) return this;
            if (mapName == root.Map.Name) return root;
            return MapSettings.IsGeneratedName(mapName) && MapSettings.TryParse(mapName, out var settings)
                ? MapGenerator.ContentFor(root, settings) : root;
        }

        /// <summary>A copy of this content on another map, with that map's lands to explore.</summary>
        internal GameContent WithMap(MapDefinition map, List<RegionDefinition> regions)
        {
            var c = (GameContent)MemberwiseClone();
            c.Base = Base ?? this;
            c.Map = map;
            c.Regions = regions;
            return c;
        }

        public LawDefinition Law(string id) => Laws.FirstOrDefault(l => l.Id == id);
        public PetitionDefinition Petition(string id) => Petitions.FirstOrDefault(p => p.Id == id);
        public SkillDefinition Skill(JobType job) => Skills.FirstOrDefault(s => s.Job == job);
        public DecorationDefinition Decoration(string id) => Decorations.FirstOrDefault(d => d.Id == id);

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
                Chapters = o.Has("chapters") ? o.Objs("chapters").Select(ChapterDefinition.FromJson).OrderBy(c => c.Number).ToList() : new List<ChapterDefinition>(),
                Decorations = o.Has("decorations") ? o.Objs("decorations").Select(DecorationDefinition.FromJson).ToList() : new List<DecorationDefinition>(),
                Skills = o.Has("skills") ? o.Objs("skills").Select(SkillDefinition.FromJson).ToList() : new List<SkillDefinition>(),
                Laws = o.Has("laws") ? o.Objs("laws").Select(LawDefinition.FromJson).ToList() : new List<LawDefinition>(),
                Petitions = o.Has("petitions") ? o.Objs("petitions").Select(PetitionDefinition.FromJson).ToList() : new List<PetitionDefinition>(),
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
