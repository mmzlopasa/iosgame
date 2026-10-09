using System.Collections.Generic;
using System.Linq;

namespace Eonward.Core
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

        public string PendingEventId;
        /// <summary>eventId → optionId</summary>
        public Dictionary<string, string> ResolvedEvents = new Dictionary<string, string>();
        public List<ActiveModifier> Modifiers = new List<ActiveModifier>();

        public List<string> CompletedObjectives = new List<string>();
        public List<string> ReachedMilestones = new List<string>();

        public List<LegacyEntry> Legacy = new List<LegacyEntry>();
        public int NextLegacyId;

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
