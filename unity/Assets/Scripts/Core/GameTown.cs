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

        /// <summary>Capacity multiplier from the building level: +25 % per level above 1.</summary>
        public static double LevelFactor(int level) => 1 + 0.25 * Math.Max(0, level - 1);

        /// <summary>Level of the settlement's heart (the campfire, later the hearth); 1 without one.</summary>
        public int HeartLevel
        {
            get
            {
                int best = 0;
                foreach (var b in State.Buildings)
                    if (Content.Building(b.DefinitionId)?.Heart == true) best = Math.Max(best, b.Level);
                return Math.Max(1, best);
            }
        }

        public PlacedBuilding Heart => State.Buildings.FirstOrDefault(b => Content.Building(b.DefinitionId)?.Heart == true);

        public string HeartName => Heart is PlacedBuilding h ? DisplayName(h) : "Campfire";

        /// <summary>Highest level of the completed buildings of a definition (0 if none).</summary>
        public int BestLevel(string definitionId)
        {
            int best = 0;
            foreach (var b in State.Buildings)
                if (b.DefinitionId == definitionId && b.IsOperational) best = Math.Max(best, b.Level);
            return best;
        }

        /// <summary>The building's name at its level (a Tent becomes a Hut, a Cottage, a House…).</summary>
        public string DisplayName(PlacedBuilding b)
        {
            var def = Content.Building(b.DefinitionId);
            if (def == null) return b.DefinitionId;
            string name = def.Name;
            foreach (var look in def.Looks) if (b.Level >= look.Level && !string.IsNullOrEmpty(look.Name)) name = look.Name;
            return name;
        }

        /// <summary>The model the building shows at its level.</summary>
        public string SpriteOf(PlacedBuilding b) => Content.Building(b.DefinitionId)?.SpriteAt(b.Level) ?? "";

        // Rounded half up, so even a small hut gains a bed at level 2.
        public int HousingOf(PlacedBuilding b, BuildingDefinition def) => (int)Math.Floor(def.HousingValue * LevelFactor(b.Level) + 0.5);
        public int SlotsOf(PlacedBuilding b, BuildingDefinition def, JobType job) => (int)Math.Floor(def.Slots(job) * LevelFactor(b.Level) + 0.5);

        // MARK: - Occupancy

        Dictionary<(int, int), PlacedDecoration> decorationIndex;
        int decorationIndexStamp = -1, decorationIndexCount = -1;

        /// <summary>Road or decoration on a cell, if any.</summary>
        public PlacedDecoration DecorationAt(int x, int y)
        {
            if (decorationIndex == null || decorationIndexStamp != State.NextDecorationId || decorationIndexCount != State.Decorations.Count)
            {
                decorationIndex = new Dictionary<(int, int), PlacedDecoration>();
                foreach (var d in State.Decorations) decorationIndex[(d.X, d.Y)] = d;
                decorationIndexStamp = State.NextDecorationId;
                decorationIndexCount = State.Decorations.Count;
            }
            return decorationIndex.TryGetValue((x, y), out var found) ? found : null;
        }

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
            var t = TerrainNow(x, y);
            if (t.HasValue && !(def.IsBridge ? def.AllowedTerrain.Contains(t.Value) : AllowsTerrain(def.AllowedTerrain, t.Value))) issues.Add(PlacementIssue.InvalidTerrain(t.Value));
            if (BuildingIdAt(x, y) != null || DecorationAt(x, y) != null) issues.Add(PlacementIssue.Overlaps);
            if (def.IsBridge && t != TerrainType.Water && !NextToWater(x, y)) issues.Add(PlacementIssue.NeedsAdjacent(TerrainType.Water));
            else if (def.IsBridge && !BridgeAnchored(x, y)) issues.Add(PlacementIssue.NeedsAdjacent(TerrainType.Grass));
            if (!State.Resources.Covers(def.Cost)) issues.Add(PlacementIssue.Unaffordable(State.Resources.Shortfall(def.Cost)));
            return issues;
        }

        /// <summary>A bridge must start from the bank or continue another bridge.</summary>
        bool BridgeAnchored(int x, int y)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                var t = TerrainNow(nx, ny);
                if (t.HasValue && t.Value != TerrainType.Water) return true;
                var d = DecorationAt(nx, ny);
                if (d != null && (Content.Decoration(d.DefinitionId)?.IsBridge ?? false)) return true;
            }
            return false;
        }

        /// <summary>A bridge on the sandy bank must stand at the water's edge.</summary>
        bool NextToWater(int x, int y)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (TerrainNow(x + dx, y + dy) == TerrainType.Water) return true;
            return false;
        }

        public bool IsBridge(int x, int y)
        {
            var d = DecorationAt(x, y);
            return d != null && (Content.Decoration(d.DefinitionId)?.IsBridge ?? false);
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

        /// <summary>
        /// What still stands between a building and its next level: the heart must be at least as
        /// high (no hut outgrows the campfire), and the level's own requirements must be met.
        /// </summary>
        public List<RequirementStatus> LevelRequirements(PlacedBuilding b)
        {
            var list = new List<RequirementStatus>();
            var def = Content.Building(b.DefinitionId);
            var next = NextLevel(b);
            if (def == null || next == null) return list;
            if (!def.Heart)
                list.Add(new RequirementStatus { Text = $"{HeartName} level {HeartLevel}/{b.Level + 1}", Met = HeartLevel >= b.Level + 1 });
            list.AddRange(Status(next.Requirements));
            return list;
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
            var unmet = LevelRequirements(b).Where(r => !r.Met).Select(r => r.Text).ToList();
            if (unmet.Count > 0) return GameError.RequirementsNotMet(unmet);
            int maxQueue = Content.Balance.MaxConcurrentConstructions;
            if (ActiveConstructionCount() >= maxQueue) return GameError.ConstructionQueueFull(maxQueue);
            if (!State.Resources.Covers(next.Cost)) return GameError.Unaffordable(State.Resources.Shortfall(next.Cost));
            State.Resources = State.Resources - next.Cost;
            b.Status = BuildingStatus.Upgrading;
            b.UpgradeTargetId = null;
            b.PendingLevel = b.Level + 1;
            b.RemainingWork = next.BuildSeconds;
            b.TotalWork = next.BuildSeconds;
            Notice(NoticeKind.Info, $"Upgrade started: {DisplayName(b)} to level {b.PendingLevel}.");
            return GameResult.Success;
        }

        // MARK: - People and skills

        /// <summary>Skill level (0 up to the maximum, 10) of a person in a job: level n needs n² units of work.</summary>
        public int SkillLevel(Person p, JobType job) => SkillLevelFor(Experience(p, job));

        public static double Experience(Person p, JobType job) => p.Experience.TryGetValue(job.Id(), out var v) ? v : 0;

        int SkillLevelFor(double xp)
        {
            double unit = Math.Max(1, Content.Balance.SkillSecondsPerLevel);
            return Math.Min(Content.Balance.MaxSkillLevel, (int)Math.Floor(Math.Sqrt(xp / unit) + 1e-9));
        }

        /// <summary>Work (seconds) needed to reach a skill level.</summary>
        public double ExperienceFor(int level) => Math.Max(1, Content.Balance.SkillSecondsPerLevel) * level * level;

        /// <summary>0..1 progress towards the next skill level (1 at the maximum).</summary>
        public double SkillProgress(Person p, JobType job)
        {
            int level = SkillLevel(p, job);
            if (level >= Content.Balance.MaxSkillLevel) return 1;
            double from = ExperienceFor(level), to = ExperienceFor(level + 1);
            return Math.Max(0, Math.Min(1, (Experience(p, job) - from) / (to - from)));
        }

        /// <summary>The perks a person has earned in a job.</summary>
        public IEnumerable<PerkDefinition> Perks(Person p, JobType job)
        {
            var skill = Content.Skill(job);
            if (skill == null) yield break;
            int level = SkillLevel(p, job);
            foreach (var perk in skill.Perks) if (perk.Level <= level) yield return perk;
        }

        /// <summary>Output multiplier of one person in a job: skill level plus the perks earned.</summary>
        public double WorkerMultiplier(Person p, JobType job)
        {
            double m = 1 + Content.Balance.SkillBonusPerLevel * SkillLevel(p, job);
            if (Content.Balance.SkillBonusPerLevel > 0) foreach (var perk in Perks(p, job)) m += perk.Bonus;
            return m;
        }

        /// <summary>Settlement-wide perk effects: each counts once if anyone has earned it.</summary>
        IEnumerable<Effect> PerkEffects()
        {
            if (Content.Balance.SkillBonusPerLevel <= 0) yield break; // skills switched off
            foreach (var skill in Content.Skills)
            {
                if (skill.Perks.All(perk => perk.Effects.Count == 0)) continue;
                int best = 0;
                foreach (var p in State.People) best = Math.Max(best, SkillLevel(p, skill.Job));
                foreach (var perk in skill.Perks)
                    if (perk.Level <= best) foreach (var e in perk.Effects) yield return e;
            }
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
            var crafts = new[] { JobType.Woodcutting, JobType.Foraging, JobType.Stonecutting, JobType.Hunting, JobType.Fishing, JobType.Crafting, JobType.Herding };
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
            double rate = Multiplier(EffectKind.ExperienceMultiplier);
            foreach (var p in State.People)
            {
                if (p.Job == null || p.Sick) continue;
                p.Experience.TryGetValue(p.Job, out var xp);
                p.Experience[p.Job] = xp + dt * rate;
            }
        }

        /// <summary>Sum of each worker's skill multiplier in a job (count of workers when skills are off).</summary>
        double SkilledWorkers(JobType job)
        {
            int count = State.Assignments[job];
            double bonus = Content.Balance.SkillBonusPerLevel;
            if (count <= 0) return 0;
            if (bonus <= 0 || State.People.Count == 0) return ActiveWorkers(job);
            string id = job.Id();
            double sum = 0;
            int seen = 0;
            foreach (var p in State.People)
            {
                if (p.Job != id) continue;
                seen++;
                if (p.Sick) continue; // the sick keep their place but do not work
                sum += WorkerMultiplier(p, job);
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
