using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    public enum BuildingStatus { Constructing, Complete, Upgrading }

    /// <summary>A building placed in the settlement. Id is a stable instance id that survives
    /// upgrades and era transitions, so the same structure keeps its identity and history.</summary>
    public sealed class PlacedBuilding
    {
        public int Id;
        public string DefinitionId;
        public int X, Y;
        public BuildingStatus Status;
        public double RemainingWork, TotalWork;
        public string UpgradeTargetId;
        /// <summary>Every definition this building has been, oldest first.</summary>
        public List<string> History = new List<string>();
        public double FoundedAtSimTime;
        /// <summary>Quarter turns (0–3). Odd turns swap the footprint's width and height.</summary>
        public int Rotation;
        /// <summary>Building level (1 = as built). Upgrading to <see cref="PendingLevel"/> raises it.</summary>
        public int Level = 1;
        public int PendingLevel;

        public bool IsOperational => Status != BuildingStatus.Constructing;

        public double Progress
        {
            get
            {
                if (Status == BuildingStatus.Complete || TotalWork <= 0) return 1;
                return System.Math.Min(1, System.Math.Max(0, 1 - RemainingWork / TotalWork));
            }
        }

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = Id, ["definitionId"] = DefinitionId, ["x"] = X, ["y"] = Y,
                ["status"] = Enums.FirstLower(Status.ToString()),
                ["remainingWork"] = RemainingWork, ["totalWork"] = TotalWork,
                ["history"] = History.Cast<object>().ToList(), ["foundedAtSimTime"] = FoundedAtSimTime,
            };
            if (UpgradeTargetId != null) d["upgradeTargetId"] = UpgradeTargetId;
            if (Rotation != 0) d["rotation"] = Rotation;
            if (Level != 1) d["level"] = Level;
            if (PendingLevel != 0) d["pendingLevel"] = PendingLevel;
            return d;
        }

        internal static PlacedBuilding FromJson(JObj o) => new PlacedBuilding
        {
            Id = o.Int("id"),
            DefinitionId = o.Str("definitionId"),
            X = o.Int("x"), Y = o.Int("y"),
            Status = Enums.ParseEnum<BuildingStatus>(o.Str("status")),
            RemainingWork = o.Num("remainingWork"),
            TotalWork = o.Num("totalWork"),
            UpgradeTargetId = o.OptStr("upgradeTargetId"),
            History = o.Strs("history"),
            FoundedAtSimTime = o.Num("foundedAtSimTime"),
            Rotation = o.OptInt("rotation") ?? 0,
            Level = o.OptInt("level") ?? 1,
            PendingLevel = o.OptInt("pendingLevel") ?? 0,
        };
    }

    /// <summary>A villager: a named person with a job and skills that grow with work.</summary>
    public sealed class Person
    {
        public int Id;
        public string Name;
        public bool Female;
        /// <summary>Job id while working, null when idle.</summary>
        public string Job;
        /// <summary>Seconds worked per job id.</summary>
        public Dictionary<string, double> Experience = new Dictionary<string, double>();
        /// <summary>Ill: keeps the job but does not work until well again.</summary>
        public bool Sick;
        /// <summary>0..1 towards getting well.</summary>
        public double Recovery;
        /// <summary>0..1 towards death while the illness goes untreated in the cold or hunger.</summary>
        public double Severity;

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = Id, ["name"] = Name, ["female"] = Female,
                ["experience"] = Experience.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
            };
            if (Job != null) d["job"] = Job;
            if (Sick)
            {
                d["sick"] = true;
                d["recovery"] = Recovery;
                d["severity"] = Severity;
            }
            return d;
        }

        internal static Person FromJson(JObj o)
        {
            var p = new Person
            {
                Id = o.Int("id"), Name = o.Str("name"), Female = o.OptBool("female", false), Job = o.OptStr("job"),
                Sick = o.OptBool("sick", false), Recovery = o.OptNum("recovery") ?? 0, Severity = o.OptNum("severity") ?? 0,
            };
            if (o.Has("experience"))
            {
                var e = o.Obj("experience");
                foreach (var k in e.Keys) p.Experience[k] = e.Num(k);
            }
            return p;
        }
    }

    /// <summary>A placed road tile, fence, wall, lantern, plant or monument.</summary>
    public sealed class PlacedDecoration
    {
        public int Id;
        public string DefinitionId;
        public int X, Y, Rotation;

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["id"] = Id, ["definitionId"] = DefinitionId, ["x"] = X, ["y"] = Y, ["rotation"] = Rotation,
        };

        internal static PlacedDecoration FromJson(JObj o) => new PlacedDecoration
        {
            Id = o.Int("id"), DefinitionId = o.Str("definitionId"), X = o.Int("x"), Y = o.Int("y"), Rotation = o.OptInt("rotation") ?? 0,
        };
    }

    /// <summary>A lasting trade route: a good carried to (sold) or from (bought) a neighbouring village every minute.</summary>
    public sealed class TradeRoute
    {
        public string NeighbourId;
        public ResourceType Resource;
        public bool Sell;

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["neighbourId"] = NeighbourId, ["resource"] = Resource.Id(), ["sell"] = Sell,
        };

        internal static TradeRoute FromJson(JObj o) => new TradeRoute
        {
            NeighbourId = o.Str("neighbourId"), Resource = ResourceTypes.Parse(o.Str("resource")), Sell = o.Bool("sell"),
        };
    }

    /// <summary>Job → worker count, keyed by the stable job id.</summary>
    public sealed class JobAssignments
    {
        public Dictionary<string, int> Counts = new Dictionary<string, int>();

        public int this[JobType job]
        {
            get => Counts.TryGetValue(job.Id(), out var n) ? n : 0;
            set => Counts[job.Id()] = System.Math.Max(0, value);
        }

        public int Total => Enums.Jobs.Sum(j => this[j]);
    }

    public sealed class ActiveResearch
    {
        public string TechId;
        public double RemainingSeconds, TotalSeconds;
    }

    public sealed class ActiveExpedition
    {
        public string RegionId;
        public double RemainingSeconds, TotalSeconds;
    }

    /// <summary>A temporary or permanent effect from an event decision.</summary>
    public sealed class ActiveModifier
    {
        public string SourceId;
        public Effect Effect;
        /// <summary>Game (simulation) time at which it ends. Null = permanent.</summary>
        public double? ExpiresAtSimTime;
    }

    /// <summary>A promise made to the people, with the time it must be kept by.</summary>
    public sealed class ActivePetition
    {
        public string PetitionId;
        public double DeadlineSimTime;
    }

    /// <summary>One entry of the Legacy Timeline: a decision or event and its lasting meaning.</summary>
    public sealed class LegacyEntry
    {
        public int Id;
        public double SimTime;
        public string EpochId, Kind, Title, Detail, RefId;
    }

    /// <summary>The complete, saveable state of one settlement. Plain data only — rules live in Game.</summary>
    public sealed class GameState
    {
        public string SettlementId, SettlementName, MapName, EpochId, StageId;

        /// <summary>Wall-clock time (seconds since 1970) up to which the simulation has been advanced.</summary>
        public double LastUpdateWallTime;
        /// <summary>Total simulated game time in seconds. Drives modifier expiry deterministically.</summary>
        public double SimTime;

        public ResourceBundle Resources;
        public int Population;
        public double GrowthProgress;
        public JobAssignments Assignments = new JobAssignments();
        public bool FoodShortage;

        public List<PlacedBuilding> Buildings = new List<PlacedBuilding>();
        public int NextBuildingId;

        public List<string> Researched = new List<string>();
        public ActiveResearch ActiveResearch;

        public List<string> ExploredRegions = new List<string>();
        public ActiveExpedition ActiveExpedition;
        /// <summary>False until the player has chosen where the campfire and the first shelters stand.</summary>
        public bool Founded = true;
        /// <summary>Top-left cell of the home clearing (the land around the campfire); -1 = where the map suggests.</summary>
        public int HomeX = -1, HomeY = -1;

        public string PendingEventId;
        /// <summary>eventId → optionId</summary>
        public Dictionary<string, string> ResolvedEvents = new Dictionary<string, string>();
        public List<ActiveModifier> Modifiers = new List<ActiveModifier>();

        public List<string> CompletedObjectives = new List<string>();
        public List<string> ReachedMilestones = new List<string>();

        public List<LegacyEntry> Legacy = new List<LegacyEntry>();
        public int NextLegacyId;

        public List<Person> People = new List<Person>();
        public int NextPersonId;
        public List<PlacedDecoration> Decorations = new List<PlacedDecoration>();
        public int NextDecorationId;
        /// <summary>Number of the current story chapter (1 = the first).</summary>
        public int Chapter = 1;
        /// <summary>Chapter whose opening scene the player has not seen yet, or null.</summary>
        public string PendingStory;
        /// <summary>Amount taken from each natural resource cell (key: y·width + x).</summary>
        public Dictionary<int, double> NodeTaken = new Dictionary<int, double>();
        /// <summary>Forest cells that were cut down, with the time they were cleared (they grow back).</summary>
        public Dictionary<int, double> NodeCleared = new Dictionary<int, double>();
        /// <summary>Cells the player marked to be harvested first, in order.</summary>
        public List<int> HarvestOrders = new List<int>();

        /// <summary>Discontent 0–100 (Frostpunk's second meter).</summary>
        public double Discontent;
        /// <summary>Signed laws, in order.</summary>
        public List<string> Laws = new List<string>();
        /// <summary>Game time of the last law (laws need time between them). Null before the first.</summary>
        public double? LastLawSimTime;
        /// <summary>The heart burns hot until this time; it can be stoked again from StokeReadySimTime.</summary>
        public double StokedUntilSimTime, StokeReadySimTime;
        /// <summary>Towards the next person falling ill (1 = someone does).</summary>
        public double IllnessProgress;
        /// <summary>Towards the next family leaving while discontent is at the top.</summary>
        public double LeaveProgress;
        public int Deaths;
        /// <summary>A petition waiting for the player's answer.</summary>
        public string PendingPetition;
        public ActivePetition ActivePetition;
        /// <summary>When the people last petitioned (null = never).</summary>
        public double? LastPetitionSimTime;
        /// <summary>The tax level the player chose (null = the content's default).</summary>
        public string TaxLevel;
        /// <summary>Lasting trade routes, oldest first (only as many run as the trading places allow).</summary>
        public List<TradeRoute> TradeRoutes = new List<TradeRoute>();
        /// <summary>Favour of each estate of the realm (estate id → favour).</summary>
        public Dictionary<string, double> Favour = new Dictionary<string, double>();
        /// <summary>Ground worn by feet (cell y·width + x → wear); enough wear makes a path.</summary>
        public Dictionary<int, double> Wear = new Dictionary<int, double>();
        /// <summary>Goods made by recipes since the founding, by resource id (e.g. bronze cast).</summary>
        public Dictionary<string, double> Made = new Dictionary<string, double>();
        /// <summary>Speed-ups owned: key (see Speedups.Key) → count.</summary>
        public Dictionary<string, int> Speedups = new Dictionary<string, int>();
        /// <summary>The starter speed-ups were given (older saves get them once on load).</summary>
        public bool SpeedupStarterGiven;

        public PlacedBuilding Building(int id) => Buildings.FirstOrDefault(b => b.Id == id);

        // MARK: - JSON

        static List<object> Strings(List<string> list) => list.Cast<object>().ToList();

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>
            {
                ["settlementId"] = SettlementId,
                ["settlementName"] = SettlementName,
                ["mapName"] = MapName,
                ["epochId"] = EpochId,
                ["stageId"] = StageId,
                ["lastUpdateWallTime"] = LastUpdateWallTime,
                ["simTime"] = SimTime,
                ["resources"] = Resources.ToJson(),
                ["population"] = Population,
                ["growthProgress"] = GrowthProgress,
                ["assignments"] = new Dictionary<string, object>
                {
                    ["counts"] = Assignments.Counts.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
                },
                ["foodShortage"] = FoodShortage,
                ["buildings"] = Buildings.Select(b => (object)b.ToJson()).ToList(),
                ["nextBuildingId"] = NextBuildingId,
                ["researched"] = Strings(Researched),
                ["exploredRegions"] = Strings(ExploredRegions),
                ["founded"] = Founded,
                ["homeX"] = HomeX,
                ["homeY"] = HomeY,
                ["resolvedEvents"] = ResolvedEvents.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
                ["modifiers"] = Modifiers.Select(m =>
                {
                    var md = new Dictionary<string, object> { ["sourceId"] = m.SourceId, ["effect"] = m.Effect.ToJson() };
                    if (m.ExpiresAtSimTime.HasValue) md["expiresAtSimTime"] = m.ExpiresAtSimTime.Value;
                    return (object)md;
                }).ToList(),
                ["completedObjectives"] = Strings(CompletedObjectives),
                ["reachedMilestones"] = Strings(ReachedMilestones),
                ["legacy"] = Legacy.Select(l =>
                {
                    var ld = new Dictionary<string, object>
                    {
                        ["id"] = l.Id, ["simTime"] = l.SimTime, ["epochId"] = l.EpochId,
                        ["kind"] = l.Kind, ["title"] = l.Title, ["detail"] = l.Detail,
                    };
                    if (l.RefId != null) ld["refId"] = l.RefId;
                    return (object)ld;
                }).ToList(),
                ["nextLegacyId"] = NextLegacyId,
            };
            if (People.Count > 0)
            {
                d["people"] = People.Select(p => (object)p.ToJson()).ToList();
                d["nextPersonId"] = NextPersonId;
            }
            if (Chapter != 1) d["chapter"] = Chapter;
            if (NodeTaken.Count > 0) d["nodeTaken"] = NodeTaken.Select(kv => (object)new List<object> { kv.Key, kv.Value }).ToList();
            if (NodeCleared.Count > 0) d["nodeCleared"] = NodeCleared.Select(kv => (object)new List<object> { kv.Key, kv.Value }).ToList();
            if (HarvestOrders.Count > 0) d["harvestOrders"] = HarvestOrders.Select(x => (object)x).ToList();
            if (PendingStory != null) d["pendingStory"] = PendingStory;
            if (Decorations.Count > 0 || NextDecorationId > 0)
            {
                d["decorations"] = Decorations.Select(x => (object)x.ToJson()).ToList();
                d["nextDecorationId"] = NextDecorationId;
            }
            if (ActiveResearch != null)
                d["activeResearch"] = new Dictionary<string, object>
                {
                    ["techId"] = ActiveResearch.TechId,
                    ["remainingSeconds"] = ActiveResearch.RemainingSeconds,
                    ["totalSeconds"] = ActiveResearch.TotalSeconds,
                };
            if (ActiveExpedition != null)
                d["activeExpedition"] = new Dictionary<string, object>
                {
                    ["regionId"] = ActiveExpedition.RegionId,
                    ["remainingSeconds"] = ActiveExpedition.RemainingSeconds,
                    ["totalSeconds"] = ActiveExpedition.TotalSeconds,
                };
            if (PendingEventId != null) d["pendingEventId"] = PendingEventId;
            if (Discontent > 0) d["discontent"] = Discontent;
            if (Laws.Count > 0) d["laws"] = Strings(Laws);
            if (LastLawSimTime.HasValue) d["lastLawSimTime"] = LastLawSimTime.Value;
            if (StokedUntilSimTime > 0) d["stokedUntilSimTime"] = StokedUntilSimTime;
            if (StokeReadySimTime > 0) d["stokeReadySimTime"] = StokeReadySimTime;
            if (IllnessProgress > 0) d["illnessProgress"] = IllnessProgress;
            if (LeaveProgress > 0) d["leaveProgress"] = LeaveProgress;
            if (Deaths > 0) d["deaths"] = Deaths;
            if (PendingPetition != null) d["pendingPetition"] = PendingPetition;
            if (ActivePetition != null)
                d["activePetition"] = new Dictionary<string, object>
                {
                    ["petitionId"] = ActivePetition.PetitionId, ["deadlineSimTime"] = ActivePetition.DeadlineSimTime,
                };
            if (LastPetitionSimTime.HasValue) d["lastPetitionSimTime"] = LastPetitionSimTime.Value;
            if (TaxLevel != null) d["taxLevel"] = TaxLevel;
            if (TradeRoutes.Count > 0) d["tradeRoutes"] = TradeRoutes.Select(x => (object)x.ToJson()).ToList();
            if (Favour.Count > 0) d["favour"] = Favour.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            if (Wear.Count > 0) d["wear"] = Wear.Select(kv => (object)new List<object> { kv.Key, Math.Round(kv.Value, 3) }).ToList();
            if (Made.Count > 0) d["made"] = Made.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            if (Speedups.Count > 0) d["speedups"] = Speedups.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            if (SpeedupStarterGiven) d["speedupStarterGiven"] = true;
            return d;
        }

        public static GameState FromJson(JObj o)
        {
            var s = new GameState
            {
                SettlementId = o.Str("settlementId"),
                SettlementName = o.Str("settlementName"),
                MapName = o.Str("mapName"),
                EpochId = o.Str("epochId"),
                StageId = o.Str("stageId"),
                LastUpdateWallTime = o.Num("lastUpdateWallTime"),
                SimTime = o.Num("simTime"),
                Resources = ResourceBundle.FromJson(o.Obj("resources")),
                Population = o.Int("population"),
                GrowthProgress = o.Num("growthProgress"),
                FoodShortage = o.Bool("foodShortage"),
                Buildings = o.Objs("buildings").Select(PlacedBuilding.FromJson).ToList(),
                NextBuildingId = o.Int("nextBuildingId"),
                Researched = o.Strs("researched"),
                ExploredRegions = o.Strs("exploredRegions"),
                Founded = o.OptBool("founded", true),
                HomeX = o.OptInt("homeX") ?? -1,
                HomeY = o.OptInt("homeY") ?? -1,
                PendingEventId = o.OptStr("pendingEventId"),
                CompletedObjectives = o.Strs("completedObjectives"),
                ReachedMilestones = o.Strs("reachedMilestones"),
                NextLegacyId = o.Int("nextLegacyId"),
            };
            var counts = o.Obj("assignments").Obj("counts");
            foreach (var key in counts.Keys) s.Assignments.Counts[key] = counts.Int(key);
            var resolved = o.Obj("resolvedEvents");
            foreach (var key in resolved.Keys) s.ResolvedEvents[key] = resolved.Str(key);
            if (o.Has("activeResearch"))
            {
                var r = o.Obj("activeResearch");
                s.ActiveResearch = new ActiveResearch { TechId = r.Str("techId"), RemainingSeconds = r.Num("remainingSeconds"), TotalSeconds = r.Num("totalSeconds") };
            }
            if (o.Has("activeExpedition"))
            {
                var e = o.Obj("activeExpedition");
                s.ActiveExpedition = new ActiveExpedition { RegionId = e.Str("regionId"), RemainingSeconds = e.Num("remainingSeconds"), TotalSeconds = e.Num("totalSeconds") };
            }
            s.Modifiers = o.Objs("modifiers").Select(m => new ActiveModifier
            {
                SourceId = m.Str("sourceId"),
                Effect = Effect.FromJson(m.Obj("effect")),
                ExpiresAtSimTime = m.OptNum("expiresAtSimTime"),
            }).ToList();
            if (o.Has("people")) s.People = o.Objs("people").Select(Person.FromJson).ToList();
            s.NextPersonId = o.OptInt("nextPersonId") ?? 0;
            if (o.Has("decorations")) s.Decorations = o.Objs("decorations").Select(PlacedDecoration.FromJson).ToList();
            s.NextDecorationId = o.OptInt("nextDecorationId") ?? 0;
            s.Chapter = o.OptInt("chapter") ?? 1;
            if (o.Has("nodeTaken"))
                foreach (var pair in o.Arr("nodeTaken")) { var l = (List<object>)pair; s.NodeTaken[Convert.ToInt32(l[0])] = Convert.ToDouble(l[1]); }
            if (o.Has("nodeCleared"))
                foreach (var pair in o.Arr("nodeCleared")) { var l = (List<object>)pair; s.NodeCleared[Convert.ToInt32(l[0])] = Convert.ToDouble(l[1]); }
            if (o.Has("harvestOrders")) s.HarvestOrders = o.Arr("harvestOrders").Select(x => Convert.ToInt32(x)).ToList();
            s.PendingStory = o.OptStr("pendingStory");
            s.Discontent = o.OptNum("discontent") ?? 0;
            if (o.Has("laws")) s.Laws = o.Strs("laws");
            s.LastLawSimTime = o.OptNum("lastLawSimTime");
            s.StokedUntilSimTime = o.OptNum("stokedUntilSimTime") ?? 0;
            s.StokeReadySimTime = o.OptNum("stokeReadySimTime") ?? 0;
            s.IllnessProgress = o.OptNum("illnessProgress") ?? 0;
            s.LeaveProgress = o.OptNum("leaveProgress") ?? 0;
            s.Deaths = o.OptInt("deaths") ?? 0;
            s.PendingPetition = o.OptStr("pendingPetition");
            if (o.Has("activePetition"))
            {
                var ap = o.Obj("activePetition");
                s.ActivePetition = new ActivePetition { PetitionId = ap.Str("petitionId"), DeadlineSimTime = ap.Num("deadlineSimTime") };
            }
            s.LastPetitionSimTime = o.OptNum("lastPetitionSimTime");
            s.TaxLevel = o.OptStr("taxLevel");
            if (o.Has("tradeRoutes")) s.TradeRoutes = o.Objs("tradeRoutes").Select(TradeRoute.FromJson).ToList();
            if (o.Has("favour"))
            {
                var fav = o.Obj("favour");
                foreach (var key in fav.Keys) s.Favour[key] = fav.Num(key);
            }
            if (o.Has("wear"))
                foreach (var pair in o.Arr("wear")) { var l = (List<object>)pair; s.Wear[Convert.ToInt32(l[0])] = Convert.ToDouble(l[1]); }
            if (o.Has("made"))
            {
                var made = o.Obj("made");
                foreach (var key in made.Keys) s.Made[key] = made.Num(key);
            }
            if (o.Has("speedups"))
            {
                var sp = o.Obj("speedups");
                foreach (var key in sp.Keys) s.Speedups[key] = sp.Int(key);
            }
            s.SpeedupStarterGiven = o.OptBool("speedupStarterGiven", false);
            s.Legacy = o.Objs("legacy").Select(l => new LegacyEntry
            {
                Id = l.Int("id"), SimTime = l.Num("simTime"), EpochId = l.Str("epochId"),
                Kind = l.Str("kind"), Title = l.Str("title"), Detail = l.Str("detail"), RefId = l.OptStr("refId"),
            }).ToList();
            return s;
        }

        /// <summary>Deep copy via JSON (used by tests and for safe snapshots).</summary>
        public GameState Clone() => FromJson(new JObj(Json.Parse(Json.Write(ToJson()))));
    }
}
