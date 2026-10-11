using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Town building: rotated buildings, building levels, roads and decorations placed in the
    /// player's own layout, demolition, and villagers as named people whose skills grow with
    /// the work they do (Manor Lords / Foundation style).
    /// </summary>
    public sealed partial class Game
    {
        static readonly string[] FemaleNames =
            { "Asha", "Ena", "Mira", "Runa", "Tala", "Yara", "Cora", "Lia", "Nessa", "Oda", "Sela", "Vala", "Ilka", "Maren", "Kesi", "Brin" };
        static readonly string[] MaleNames =
            { "Toru", "Bran", "Kael", "Oren", "Dag", "Sev", "Uri", "Aren", "Bel", "Ivo", "Hald", "Joren", "Teo", "Wren", "Zed", "Moro" };

        // MARK: - Footprints and levels

        /// <summary>Footprint of a definition at a rotation (odd quarter turns swap the sides).</summary>
        public static Footprint Rotated(Footprint fp, int rotation) =>
            (rotation & 1) == 1 ? new Footprint(fp.Height, fp.Width) : fp;

        public Footprint FootprintOf(PlacedBuilding b)
        {
            var def = Content.Building(b.DefinitionId);
            return def == null ? new Footprint(1, 1) : Rotated(def.Footprint, b.Rotation);
        }

        /// <summary>Capacity multiplier from the building level: +50 % per level above 1.</summary>
        public static double LevelFactor(int level) => 1 + 0.5 * Math.Max(0, level - 1);

        public int HousingOf(PlacedBuilding b, BuildingDefinition def) => (int)Math.Floor(def.HousingValue * LevelFactor(b.Level) + 1e-9);
        public int SlotsOf(PlacedBuilding b, BuildingDefinition def, JobType job) => (int)Math.Floor(def.Slots(job) * LevelFactor(b.Level) + 1e-9);

        // MARK: - Occupancy

        /// <summary>Road or decoration on a cell, if any.</summary>
        public PlacedDecoration DecorationAt(int x, int y) => State.Decorations.FirstOrDefault(d => d.X == x && d.Y == y);

        public bool IsRoad(int x, int y)
        {
            var d = DecorationAt(x, y);
            return d != null && (Content.Decoration(d.DefinitionId)?.IsRoad ?? false);
        }

        public int RoadCount() => State.Decorations.Count(d => Content.Decoration(d.DefinitionId)?.IsRoad ?? false);

        public int Beauty() => State.Decorations.Sum(d => Content.Decoration(d.DefinitionId)?.Beauty ?? 0);

        /// <summary>True when a road touches the building's footprint (workplaces there work better).</summary>
        public bool HasRoadAccess(PlacedBuilding b)
        {
            var fp = FootprintOf(b);
            for (int y = b.Y - 1; y <= b.Y + fp.Height; y++)
                for (int x = b.X - 1; x <= b.X + fp.Width; x++)
                {
                    bool inside = x >= b.X && x < b.X + fp.Width && y >= b.Y && y < b.Y + fp.Height;
                    bool corner = (x == b.X - 1 || x == b.X + fp.Width) && (y == b.Y - 1 || y == b.Y + fp.Height);
                    if (!inside && !corner && IsRoad(x, y)) return true;
                }
            return false;
        }

        // MARK: - Roads and decorations

        public List<PlacementIssue> DecorationIssues(string decorationId, int x, int y)
        {
            var def = Content.Decoration(decorationId);
            var issues = new List<PlacementIssue>();
            if (def == null) { issues.Add(PlacementIssue.NotBuildable); return issues; }
            if (def.RequiresTech != null && !State.Researched.Contains(def.RequiresTech))
                issues.Add(PlacementIssue.LockedByTech(Content.Technology(def.RequiresTech)?.Name ?? "?"));
            var map = Content.Map;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) { issues.Add(PlacementIssue.OutOfBounds); return issues; }
            if (!IsRevealed(x, y)) issues.Add(PlacementIssue.Unexplored);
            var t = map.Terrain(x, y);
            if (t.HasValue && !def.AllowedTerrain.Contains(t.Value)) issues.Add(PlacementIssue.InvalidTerrain(t.Value));
            if (BuildingIdAt(x, y) != null || DecorationAt(x, y) != null) issues.Add(PlacementIssue.Overlaps);
            if (!State.Resources.Covers(def.Cost)) issues.Add(PlacementIssue.Unaffordable(State.Resources.Shortfall(def.Cost)));
            return issues;
        }

        /// <summary>Places a road tile or decoration. Instant (like Manor Lords roads), costs its price.</summary>
        public GameResult<int> PlaceDecoration(string decorationId, int x, int y, int rotation = 0)
        {
            Update();
            var def = Content.Decoration(decorationId);
            if (def == null) return GameError.UnknownContent(decorationId);
            var issues = DecorationIssues(decorationId, x, y);
            if (issues.Count > 0) return GameError.Placement(issues[0]);
            State.Resources = State.Resources - def.Cost;
            int id = State.NextDecorationId++;
            State.Decorations.Add(new PlacedDecoration { Id = id, DefinitionId = def.Id, X = x, Y = y, Rotation = ((rotation % 4) + 4) % 4 });
            if (def.IsRoad && RoadCount() == 1) AddLegacy("road", "The first road", "A beaten path became the first road of the settlement.", def.Id);
            EvaluateProgress();
            return GameResult<int>.Success(id);
        }

        /// <summary>Removes a road tile or decoration; half of its cost comes back.</summary>
        public GameResult RemoveDecoration(int id)
        {
            Update();
            var d = State.Decorations.FirstOrDefault(x => x.Id == id);
            if (d == null) return GameError.UnknownContent($"decoration #{id}");
            State.Decorations.Remove(d);
            var def = Content.Decoration(d.DefinitionId);
            if (def != null) Refund(def.Cost * 0.5);
            return GameResult.Success;
        }

        void Refund(ResourceBundle amount)
        {
            var caps = StorageCapacity();
            var r = State.Resources + amount;
            foreach (var t in ResourceTypes.All) r[t] = Math.Min(Math.Max(caps[t], State.Resources[t]), r[t]);
            State.Resources = r;
        }

        // MARK: - Demolish and levels

        /// <summary>Tears a building down (not the starting hearth); half of its cost comes back.</summary>
        public GameResult DemolishBuilding(int id)
        {
            Update();
            var b = State.Building(id);
            var def = b == null ? null : Content.Building(b.DefinitionId);
            if (b == null || def == null) return GameError.UnknownContent($"building #{id}");
            if (!def.Buildable) return GameError.NotUpgradeable;
            State.Buildings.Remove(b);
            Refund(def.Cost * 0.5);
            NormalizeAssignments();
            SyncPeople();
            Notice(NoticeKind.Info, $"{def.Name} was taken down.");
            return GameResult.Success;
        }

        public LevelDefinition NextLevel(PlacedBuilding b)
        {
            var def = Content.Building(b.DefinitionId);
            if (def == null || b.Level >= def.MaxLevel) return null;
            return def.Levels[b.Level - 1];
        }

        /// <summary>Raises a building one level (more housing, job slots, storage and comfort).</summary>
        public GameResult UpgradeLevel(int id)
        {
            Update();
            var b = State.Building(id);
            var def = b == null ? null : Content.Building(b.DefinitionId);
            if (b == null || def == null) return GameError.UnknownContent($"building #{id}");
            if (b.Status != BuildingStatus.Complete) return GameError.BuildingBusy;
            var next = NextLevel(b);
            if (next == null) return GameError.NotUpgradeable;
            int maxQueue = Content.Balance.MaxConcurrentConstructions;
            if (ActiveConstructionCount() >= maxQueue) return GameError.ConstructionQueueFull(maxQueue);
            if (!State.Resources.Covers(next.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(next.Cost));
            State.Resources = State.Resources - next.Cost;
            b.Status = BuildingStatus.Upgrading;
            b.UpgradeTargetId = null;
            b.PendingLevel = b.Level + 1;
            b.RemainingWork = next.BuildSeconds;
            b.TotalWork = next.BuildSeconds;
            Notice(NoticeKind.Info, $"Upgrade started: {def.Name} to level {b.PendingLevel}.");
            return GameResult.Success;
        }

        // MARK: - People and skills

        /// <summary>Skill level (0–5) of a person in a job.</summary>
        public int SkillLevel(Person p, JobType job)
        {
            double xp = p.Experience.TryGetValue(job.Id(), out var v) ? v : 0;
            double unit = Math.Max(1, Content.Balance.SkillSecondsPerLevel);
            return Math.Min(5, (int)Math.Floor(Math.Sqrt(xp / unit)));
        }

        /// <summary>The skill title shown in the UI, e.g. "Woodcutter" for the best skill.</summary>
        public JobType? BestSkill(Person p)
        {
            JobType? best = null;
            double most = 0;
            foreach (var job in Enums.Jobs)
            {
                double xp = p.Experience.TryGetValue(job.Id(), out var v) ? v : 0;
                if (xp > most) { most = xp; best = job; }
            }
            return best;
        }

        Person NewPerson()
        {
            int id = State.NextPersonId++;
            bool female = id % 2 == 1;
            var names = female ? FemaleNames : MaleNames;
            var p = new Person { Id = id, Female = female, Name = names[(id * 7 + 3) % names.Length] };
            if (id >= names.Length * 2) p.Name += " " + (char)('A' + id / (names.Length * 2) % 26) + ".";
            // Everyone arrives with a little experience in one craft: the valley's first specialists.
            var crafts = new[] { JobType.Woodcutting, JobType.Foraging, JobType.Stonecutting, JobType.Hunting, JobType.Fishing, JobType.Crafting };
            p.Experience[crafts[(id * 5 + 1) % crafts.Length].Id()] = Content.Balance.SkillSecondsPerLevel * (1 + id % 3);
            return p;
        }

        /// <summary>Keeps the list of people in step with the population and the job counts.</summary>
        internal void SyncPeople()
        {
            while (State.People.Count < State.Population) State.People.Add(NewPerson());
            while (State.People.Count > State.Population)
            {
                var leaver = State.People.LastOrDefault(p => p.Job == null) ?? State.People.Last();
                State.People.Remove(leaver);
            }
            foreach (var job in Enums.Jobs)
            {
                string id = job.Id();
                int wanted = State.Assignments[job];
                var working = State.People.Where(p => p.Job == id).ToList();
                if (working.Count > wanted)
                {
                    foreach (var p in working.OrderBy(p => SkillLevel(p, job)).ThenBy(p => p.Id).Take(working.Count - wanted))
                        p.Job = null;
                }
            }
            foreach (var job in Enums.Jobs)
            {
                string id = job.Id();
                int missing = State.Assignments[job] - State.People.Count(p => p.Job == id);
                if (missing <= 0) continue;
                // The best-skilled idle people take the job ("+" picks the best fit, as in Foundation).
                foreach (var p in State.People.Where(p => p.Job == null).OrderByDescending(p => SkillLevel(p, job)).ThenBy(p => p.Id).Take(missing).ToList())
                    p.Job = id;
            }
        }

        void AdvanceSkills(double dt)
        {
            foreach (var p in State.People)
            {
                if (p.Job == null) continue;
                p.Experience.TryGetValue(p.Job, out var xp);
                p.Experience[p.Job] = xp + dt;
            }
        }

        /// <summary>Sum of each worker's skill multiplier in a job (count of workers when skills are off).</summary>
        double SkilledWorkers(JobType job)
        {
            int count = State.Assignments[job];
            double bonus = Content.Balance.SkillBonusPerLevel;
            if (count <= 0) return 0;
            if (bonus <= 0 || State.People.Count == 0) return count;
            string id = job.Id();
            double sum = 0;
            int seen = 0;
            foreach (var p in State.People)
            {
                if (p.Job != id) continue;
                sum += 1 + bonus * SkillLevel(p, job);
                seen++;
            }
            return sum + Math.Max(0, count - seen);
        }

        /// <summary>Road bonus for a job: the share of its slots in road-connected workplaces.</summary>
        double RoadMultiplier(JobType job)
        {
            double bonus = Content.Balance.RoadWorkBonus;
            if (bonus <= 0 || State.Decorations.Count == 0) return 1;
            int total = 0, connected = 0;
            foreach (var b in State.Buildings)
            {
                if (!b.IsOperational) continue;
                var def = Content.Building(b.DefinitionId);
                if (def == null) continue;
                int slots = SlotsOf(b, def, job);
                if (slots <= 0) continue;
                total += slots;
                if (HasRoadAccess(b)) connected += slots;
            }
            return total == 0 ? 1 : 1 + bonus * connected / total;
        }
    }
}
