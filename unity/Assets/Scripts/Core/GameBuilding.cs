using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    public enum PlacementIssueKind
    {
        NotBuildable, LockedByTech, LimitReached, OutOfBounds, Unexplored, InvalidTerrain, Overlaps, NeedsAdjacent,
        ConstructionQueueFull, Unaffordable,
    }

    /// <summary>Reasons a placement is rejected. Ordered from most to least fundamental.</summary>
    public sealed class PlacementIssue : IEquatable<PlacementIssue>
    {
        public readonly PlacementIssueKind Kind;
        public readonly string Name;
        public readonly int Number;
        public readonly TerrainType Terrain;
        public readonly ResourceBundle Missing;

        PlacementIssue(PlacementIssueKind kind, string name = null, int number = 0, TerrainType terrain = default, ResourceBundle missing = default)
        {
            Kind = kind; Name = name; Number = number; Terrain = terrain; Missing = missing;
        }

        public static readonly PlacementIssue NotBuildable = new PlacementIssue(PlacementIssueKind.NotBuildable);
        public static PlacementIssue LockedByTech(string name) => new PlacementIssue(PlacementIssueKind.LockedByTech, name);
        public static PlacementIssue LimitReached(int max) => new PlacementIssue(PlacementIssueKind.LimitReached, number: max);
        public static readonly PlacementIssue OutOfBounds = new PlacementIssue(PlacementIssueKind.OutOfBounds);
        public static readonly PlacementIssue Unexplored = new PlacementIssue(PlacementIssueKind.Unexplored);
        public static PlacementIssue InvalidTerrain(TerrainType t) => new PlacementIssue(PlacementIssueKind.InvalidTerrain, terrain: t);
        public static readonly PlacementIssue Overlaps = new PlacementIssue(PlacementIssueKind.Overlaps);
        public static PlacementIssue NeedsAdjacent(TerrainType t) => new PlacementIssue(PlacementIssueKind.NeedsAdjacent, terrain: t);
        public static PlacementIssue ConstructionQueueFull(int max) => new PlacementIssue(PlacementIssueKind.ConstructionQueueFull, number: max);
        public static PlacementIssue Unaffordable(ResourceBundle missing) => new PlacementIssue(PlacementIssueKind.Unaffordable, missing: missing);

        public string Message
        {
            get
            {
                switch (Kind)
                {
                    case PlacementIssueKind.NotBuildable: return "This structure cannot be built directly.";
                    case PlacementIssueKind.LockedByTech: return $"Requires the technology {Name}.";
                    case PlacementIssueKind.LimitReached: return $"Limit reached: only {Number} allowed.";
                    case PlacementIssueKind.OutOfBounds: return "Outside the settlement area.";
                    case PlacementIssueKind.Unexplored: return "This land is unexplored. Send scouts from the Map first.";
                    case PlacementIssueKind.InvalidTerrain: return $"Cannot build on {Terrain.DisplayName().ToLowerInvariant()}.";
                    case PlacementIssueKind.Overlaps: return "Something is already built here.";
                    case PlacementIssueKind.NeedsAdjacent: return $"Must be placed next to {Terrain.DisplayName().ToLowerInvariant()}.";
                    case PlacementIssueKind.ConstructionQueueFull: return $"Your people can only build {Number} things at once.";
                    default: return $"Not enough resources. Missing: {Missing.ShortDescription}";
                }
            }
        }

        public bool Equals(PlacementIssue o) =>
            o != null && Kind == o.Kind && Name == o.Name && Number == o.Number && Terrain == o.Terrain && Missing.Equals(o.Missing);
        public override bool Equals(object obj) => Equals(obj as PlacementIssue);
        public override int GetHashCode() => HashCode.Combine(Kind, Name, Number, Terrain);
        public override string ToString() => $"{Kind}({Message})";
    }

    public sealed partial class Game
    {
        // MARK: - Placement

        public bool IsRevealed(int x, int y) =>
            State.ExploredRegions.Any(id => Content.Region(id)?.Contains(x, y) ?? false);

        static List<(int x, int y)> Cells(int x, int y, Footprint fp)
        {
            var result = new List<(int, int)>();
            for (int dy = 0; dy < fp.Height; dy++)
                for (int dx = 0; dx < fp.Width; dx++) result.Add((x + dx, y + dy));
            return result;
        }

        /// <summary>Id of the building occupying a cell, if any.</summary>
        public int? BuildingIdAt(int x, int y)
        {
            foreach (var b in State.Buildings)
            {
                if (Content.Building(b.DefinitionId) == null) continue;
                var fp = FootprintOf(b);
                if (x >= b.X && x < b.X + fp.Width && y >= b.Y && y < b.Y + fp.Height) return b.Id;
            }
            return null;
        }

        public int Count(string definitionId, bool includeUnfinished = false) =>
            State.Buildings.Count(b => b.DefinitionId == definitionId && (includeUnfinished || b.IsOperational));

        public int ActiveConstructionCount() => State.Buildings.Count(b => b.Status != BuildingStatus.Complete);

        public bool IsUnlocked(BuildingDefinition def) => def.RequiresTech == null || State.Researched.Contains(def.RequiresTech);

        /// <summary>All reasons the building cannot be placed at (x, y). Empty = valid. Changes no state.</summary>
        public List<PlacementIssue> PlacementIssues(string buildingId, int x, int y, int rotation = 0)
        {
            var def = Content.Building(buildingId);
            if (def == null) return new List<PlacementIssue> { PlacementIssue.NotBuildable };
            var issues = new List<PlacementIssue>();
            if (!def.Buildable) issues.Add(PlacementIssue.NotBuildable);
            if (!IsUnlocked(def)) issues.Add(PlacementIssue.LockedByTech(Content.Technology(def.RequiresTech ?? "")?.Name ?? "?"));
            if (def.MaxCount.HasValue && Count(def.Id, true) >= def.MaxCount.Value) issues.Add(PlacementIssue.LimitReached(def.MaxCount.Value));

            var cells = Cells(x, y, Rotated(def.Footprint, rotation));
            var map = Content.Map;
            if (cells.Any(c => c.x < 0 || c.y < 0 || c.x >= map.Width || c.y >= map.Height))
            {
                issues.Add(PlacementIssue.OutOfBounds);
            }
            else
            {
                if (cells.Any(c => !IsRevealed(c.x, c.y))) issues.Add(PlacementIssue.Unexplored);
                foreach (var c in cells)
                {
                    var t = TerrainNow(c.x, c.y);
                    if (t.HasValue && !def.AllowedTerrain.Contains(t.Value))
                    {
                        issues.Add(PlacementIssue.InvalidTerrain(t.Value));
                        break;
                    }
                }
                if (cells.Any(c => BuildingIdAt(c.x, c.y) != null || DecorationAt(c.x, c.y) != null)) issues.Add(PlacementIssue.Overlaps);
                if (def.RequiresAdjacentTerrain.HasValue && !HasAdjacent(def.RequiresAdjacentTerrain.Value, cells))
                    issues.Add(PlacementIssue.NeedsAdjacent(def.RequiresAdjacentTerrain.Value));
            }

            int maxQueue = Content.Balance.MaxConcurrentConstructions;
            if (ActiveConstructionCount() >= maxQueue) issues.Add(PlacementIssue.ConstructionQueueFull(maxQueue));
            if (!State.Resources.Covers(def.Cost)) issues.Add(PlacementIssue.Unaffordable(State.Resources.Shortfall(def.Cost)));
            return issues;
        }

        bool HasAdjacent(TerrainType terrain, List<(int x, int y)> cells)
        {
            var occupied = new HashSet<(int, int)>(cells);
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var c in cells)
            {
                foreach (var (dx, dy) in dirs)
                {
                    int nx = c.x + dx, ny = c.y + dy;
                    if (occupied.Contains((nx, ny))) continue;
                    if (TerrainNow(nx, ny) == terrain && IsRevealed(nx, ny)) return true;
                }
            }
            return false;
        }

        /// <summary>Places a building and starts construction. Resources are spent only here,
        /// only after every check passed, and exactly once per placed building.</summary>
        public GameResult<int> PlaceBuilding(string buildingId, int x, int y, int rotation = 0)
        {
            Update();
            var def = Content.Building(buildingId);
            if (def == null) return GameError.UnknownContent(buildingId);
            rotation = ((rotation % 4) + 4) % 4;
            var issues = PlacementIssues(buildingId, x, y, rotation);
            if (issues.Count > 0) return GameError.Placement(issues[0]);
            State.Resources = State.Resources - def.Cost;
            int id = State.NextBuildingId;
            State.NextBuildingId += 1;
            State.Buildings.Add(new PlacedBuilding
            {
                Id = id, DefinitionId = def.Id, X = x, Y = y, Status = BuildingStatus.Constructing,
                RemainingWork = def.BuildSeconds, TotalWork = def.BuildSeconds,
                History = new List<string> { def.Id }, FoundedAtSimTime = State.SimTime, Rotation = rotation,
            });
            Notice(NoticeKind.Info, $"Construction started: {def.Name}.");
            return GameResult<int>.Success(id);
        }

        // MARK: - Construction

        /// <summary>Work units per second applied to each active construction.</summary>
        public double ConstructionSpeed()
        {
            var b = Content.Balance;
            int builders = State.Assignments[JobType.Construction];
            double speed = b.BaseConstructionSpeed + b.ConstructionSpeedPerBuilder * builders;
            foreach (var e in ActiveEffects())
                if (e.Kind == EffectKind.ConstructionSpeedMultiplier && builders >= (e.MinBuilders ?? 0)) speed *= e.Value ?? 1;
            return speed;
        }

        /// <summary>Estimated seconds to finish an amount of work at the current speed.</summary>
        public double EstimatedSeconds(double work)
        {
            double speed = ConstructionSpeed();
            return speed > 0 ? work / speed : double.PositiveInfinity;
        }

        void AdvanceConstruction(double dt)
        {
            double speed = ConstructionSpeed();
            var completed = new List<PlacedBuilding>();
            foreach (var b in State.Buildings)
            {
                if (b.Status == BuildingStatus.Complete) continue;
                b.RemainingWork -= speed * dt;
                if (b.RemainingWork <= 1e-9) completed.Add(b);
            }
            foreach (var b in completed) FinishConstruction(b);
        }

        /// <summary>Completes a construction or upgrade. Reachable once per building per job,
        /// because the status leaves Constructing/Upgrading here.</summary>
        void FinishConstruction(PlacedBuilding b)
        {
            bool wasUpgrade = b.Status == BuildingStatus.Upgrading;
            if (wasUpgrade && b.UpgradeTargetId != null)
            {
                b.DefinitionId = b.UpgradeTargetId;
                b.History.Add(b.UpgradeTargetId);
            }
            bool levelUp = wasUpgrade && b.PendingLevel > 0;
            if (levelUp)
            {
                b.Level = b.PendingLevel;
                b.PendingLevel = 0;
            }
            b.Status = BuildingStatus.Complete;
            b.RemainingWork = 0;
            b.UpgradeTargetId = null;

            var def = Content.Building(b.DefinitionId);
            string name = def?.Name ?? b.DefinitionId;
            if (levelUp)
            {
                Notice(NoticeKind.Success, $"{name} reached level {b.Level}.");
                return;
            }
            Notice(NoticeKind.Success, wasUpgrade ? $"Upgrade complete: {name}." : $"Construction complete: {name}.");
            if (Count(b.DefinitionId) == 1) AddLegacy("building", $"First {name}", def?.Description ?? "", b.DefinitionId);
        }

        /// <summary>Starts an in-place upgrade (keeps the building id, position and history).</summary>
        public GameResult UpgradeBuilding(int id)
        {
            Update();
            var b = State.Building(id);
            var def = b == null ? null : Content.Building(b.DefinitionId);
            if (b == null || def == null) return GameError.UnknownContent($"building #{id}");
            if (b.Status != BuildingStatus.Complete) return GameError.BuildingBusy;
            var target = def.UpgradesTo == null ? null : Content.Building(def.UpgradesTo);
            if (target == null) return GameError.NotUpgradeable;
            if (!IsUnlocked(target)) return GameError.LockedByTech(Content.Technology(target.RequiresTech ?? "")?.Name ?? "?");
            int maxQueue = Content.Balance.MaxConcurrentConstructions;
            if (ActiveConstructionCount() >= maxQueue) return GameError.ConstructionQueueFull(maxQueue);
            if (!State.Resources.Covers(target.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(target.Cost));
            State.Resources = State.Resources - target.Cost;
            b.Status = BuildingStatus.Upgrading;
            b.UpgradeTargetId = target.Id;
            b.RemainingWork = target.BuildSeconds;
            b.TotalWork = target.BuildSeconds;
            Notice(NoticeKind.Info, $"Upgrade started: {def.Name} → {target.Name}.");
            return GameResult.Success;
        }
    }
}
