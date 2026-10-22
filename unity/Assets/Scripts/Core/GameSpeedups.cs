using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>What a speed-up can be used on. General ones work on anything.</summary>
    public enum SpeedupKind { General, Construction, Research, Exploration }

    /// <summary>A running job a speed-up can shorten: a building site, the research or the scouts.</summary>
    public readonly struct SpeedupTask : IEquatable<SpeedupTask>
    {
        public readonly SpeedupKind Kind;
        /// <summary>The building being built or upgraded (Construction only).</summary>
        public readonly int BuildingId;

        SpeedupTask(SpeedupKind kind, int buildingId) { Kind = kind; BuildingId = buildingId; }

        public static SpeedupTask Building(int id) => new SpeedupTask(SpeedupKind.Construction, id);
        public static readonly SpeedupTask Research = new SpeedupTask(SpeedupKind.Research, 0);
        public static readonly SpeedupTask Expedition = new SpeedupTask(SpeedupKind.Exploration, 0);

        public bool Equals(SpeedupTask o) => Kind == o.Kind && BuildingId == o.BuildingId;
        public override bool Equals(object obj) => obj is SpeedupTask o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Kind, BuildingId);
        public override string ToString() => Kind == SpeedupKind.Construction ? $"build#{BuildingId}" : Kind.ToString();
    }

    /// <summary>One stack of speed-ups the player owns.</summary>
    public struct SpeedupStack
    {
        public SpeedupKind Kind;
        public int Minutes;
        public int Count;
    }

    /// <summary>
    /// Speed-ups: items that take time off a running construction, research or scouting trip
    /// (like the speed-ups in Whiteout Survival). They are never bought: the settlement earns them
    /// by building, levelling up, researching, exploring, completing goals and reaching milestones.
    /// The sizes and rewards live here so the whole system reads in one place.
    /// </summary>
    public static class Speedups
    {
        /// <summary>The sizes that exist, smallest first.</summary>
        public static readonly int[] Sizes = { 1, 5, 15, 60, 180 };

        public static readonly SpeedupKind[] Kinds =
            { SpeedupKind.General, SpeedupKind.Construction, SpeedupKind.Research, SpeedupKind.Exploration };

        /// <summary>Stable save key, e.g. "construction_15".</summary>
        public static string Key(SpeedupKind kind, int minutes) => $"{Enums.FirstLower(kind.ToString())}_{minutes}";

        public static bool TryParse(string key, out SpeedupKind kind, out int minutes)
        {
            kind = default;
            minutes = 0;
            int i = key?.LastIndexOf('_') ?? -1;
            if (i <= 0 || !int.TryParse(key.Substring(i + 1), out minutes) || !Sizes.Contains(minutes)) return false;
            string name = key.Substring(0, i);
            foreach (var k in Kinds)
                if (Enums.FirstLower(k.ToString()) == name) { kind = k; return true; }
            return false;
        }

        public static string KindName(SpeedupKind kind)
        {
            switch (kind)
            {
                case SpeedupKind.Construction: return "Building";
                case SpeedupKind.Research: return "Research";
                case SpeedupKind.Exploration: return "Scouting";
                default: return "General";
            }
        }

        public static string SizeName(int minutes) => minutes >= 60 ? $"{minutes / 60} h" : $"{minutes} min";

        /// <summary>"15 min Building speed-up".</summary>
        public static string Name(SpeedupKind kind, int minutes) => $"{SizeName(minutes)} {KindName(kind)} speed-up";

        /// <summary>A short list for notices: "2× 5 min Building, 1× 1 min General".</summary>
        public static string Describe(IEnumerable<SpeedupStack> items) =>
            string.Join(", ", items.Select(s => $"{s.Count}× {SizeName(s.Minutes)} {KindName(s.Kind)}"));

        static SpeedupStack S(SpeedupKind kind, int minutes, int count = 1) =>
            new SpeedupStack { Kind = kind, Minutes = minutes, Count = count };

        // MARK: - Rewards (what each achievement earns)

        /// <summary>Every new settlement starts with a few, so the player meets them early.</summary>
        public static readonly SpeedupStack[] StarterPack =
            { S(SpeedupKind.General, 5, 3), S(SpeedupKind.Construction, 5, 2), S(SpeedupKind.Research, 5, 1) };

        public static readonly SpeedupStack[] BuildingFinished = { S(SpeedupKind.Construction, 1, 2) };

        /// <summary>A building raised to a level: bigger levels earn bigger speed-ups.</summary>
        public static SpeedupStack[] LevelReached(int level) => level >= 4
            ? new[] { S(SpeedupKind.Construction, 60), S(SpeedupKind.General, 15) }
            : level == 3
                ? new[] { S(SpeedupKind.Construction, 15, 2), S(SpeedupKind.General, 5) }
                : new[] { S(SpeedupKind.Construction, 15) };

        /// <summary>A building replaced by its better form (hut → house).</summary>
        public static readonly SpeedupStack[] BuildingImproved = { S(SpeedupKind.Construction, 15), S(SpeedupKind.General, 5) };

        public static readonly SpeedupStack[] ResearchFinished = { S(SpeedupKind.Research, 15), S(SpeedupKind.General, 1, 2) };

        public static readonly SpeedupStack[] RegionExplored = { S(SpeedupKind.Exploration, 15), S(SpeedupKind.General, 5) };

        public static readonly SpeedupStack[] ObjectiveCompleted = { S(SpeedupKind.General, 5) };

        public static readonly SpeedupStack[] MilestoneReached =
        {
            S(SpeedupKind.General, 60), S(SpeedupKind.Construction, 60), S(SpeedupKind.Research, 60), S(SpeedupKind.Exploration, 60),
        };

        public static readonly SpeedupStack[] ChapterFinished = { S(SpeedupKind.General, 15, 2), S(SpeedupKind.Construction, 180) };
    }

    public sealed partial class Game
    {
        // MARK: - Speed-ups: inventory

        public int SpeedupCount(SpeedupKind kind, int minutes) =>
            State.Speedups.TryGetValue(Speedups.Key(kind, minutes), out var n) ? n : 0;

        /// <summary>Everything owned, kind by kind, smallest first.</summary>
        public List<SpeedupStack> OwnedSpeedups() =>
            (from kind in Speedups.Kinds
             from minutes in Speedups.Sizes
             let n = SpeedupCount(kind, minutes)
             where n > 0
             select new SpeedupStack { Kind = kind, Minutes = minutes, Count = n }).ToList();

        /// <summary>Total minutes owned that could be used on a task of this kind.</summary>
        public int SpeedupMinutesFor(SpeedupKind kind) =>
            OwnedSpeedups().Where(s => Fits(s.Kind, kind)).Sum(s => s.Count * s.Minutes);

        static bool Fits(SpeedupKind item, SpeedupKind task) => item == SpeedupKind.General || item == task;

        /// <summary>Adds speed-ups to the inventory (rewards; never bought).</summary>
        internal void GrantSpeedups(IEnumerable<SpeedupStack> items, string reason)
        {
            var list = items.Where(s => s.Count > 0).ToList();
            if (list.Count == 0) return;
            foreach (var s in list)
            {
                string key = Speedups.Key(s.Kind, s.Minutes);
                State.Speedups[key] = (State.Speedups.TryGetValue(key, out var n) ? n : 0) + s.Count;
            }
            Notice(NoticeKind.Success, $"{reason} Speed-ups earned: {Speedups.Describe(list)}.");
        }

        /// <summary>Saves from before speed-ups get the starter pack once (new games get it on founding).</summary>
        void EnsureStarterSpeedups()
        {
            if (State.SpeedupStarterGiven) return;
            State.SpeedupStarterGiven = true;
            foreach (var s in Speedups.StarterPack)
            {
                string key = Speedups.Key(s.Kind, s.Minutes);
                State.Speedups[key] = (State.Speedups.TryGetValue(key, out var n) ? n : 0) + s.Count;
            }
        }

        // MARK: - Speed-ups: running jobs

        /// <summary>The jobs running now that a speed-up could shorten, research and scouts first.</summary>
        public List<SpeedupTask> SpeedupTasks()
        {
            var list = new List<SpeedupTask>();
            if (State.ActiveResearch != null) list.Add(SpeedupTask.Research);
            if (State.ActiveExpedition != null) list.Add(SpeedupTask.Expedition);
            foreach (var b in State.Buildings)
                if (b.Status != BuildingStatus.Complete) list.Add(SpeedupTask.Building(b.Id));
            return list;
        }

        public bool IsRunning(SpeedupTask task)
        {
            switch (task.Kind)
            {
                case SpeedupKind.Research: return State.ActiveResearch != null;
                case SpeedupKind.Exploration: return State.ActiveExpedition != null;
                case SpeedupKind.Construction:
                    var b = State.Building(task.BuildingId);
                    return b != null && b.Status != BuildingStatus.Complete;
                default: return false;
            }
        }

        /// <summary>The job's name for the speed-up window.</summary>
        public string TaskName(SpeedupTask task)
        {
            switch (task.Kind)
            {
                case SpeedupKind.Research:
                    var r = State.ActiveResearch;
                    return r == null ? "Research" : Content.Technology(r.TechId)?.Name ?? r.TechId;
                case SpeedupKind.Exploration:
                    var e = State.ActiveExpedition;
                    return e == null ? "Scouts" : Content.Region(e.RegionId)?.Name ?? e.RegionId;
                default:
                    var b = State.Building(task.BuildingId);
                    if (b == null) return "Construction";
                    var def = Content.Building(b.Status == BuildingStatus.Upgrading && b.UpgradeTargetId != null ? b.UpgradeTargetId : b.DefinitionId);
                    return def?.Name ?? b.DefinitionId;
            }
        }

        public double TaskProgress(SpeedupTask task)
        {
            switch (task.Kind)
            {
                case SpeedupKind.Research:
                    var r = State.ActiveResearch;
                    return r == null ? 1 : Math.Max(0, Math.Min(1, 1 - r.RemainingSeconds / Math.Max(1, r.TotalSeconds)));
                case SpeedupKind.Exploration:
                    var e = State.ActiveExpedition;
                    return e == null ? 1 : Math.Max(0, Math.Min(1, 1 - e.RemainingSeconds / Math.Max(1, e.TotalSeconds)));
                default:
                    return State.Building(task.BuildingId)?.Progress ?? 1;
            }
        }

        /// <summary>Building work one minute of a speed-up removes: a minute at today's pace, never less than
        /// a minute at the base pace (so a speed-up still helps a site nobody works on).</summary>
        double WorkPerSecondForSpeedups() => Math.Max(ConstructionSpeed(), Math.Max(0.1, Content.Balance.BaseConstructionSpeed));

        /// <summary>Seconds a speed-up has to cover to finish the job (0 when done).</summary>
        public double TaskSecondsLeft(SpeedupTask task)
        {
            switch (task.Kind)
            {
                case SpeedupKind.Research: return Math.Max(0, State.ActiveResearch?.RemainingSeconds ?? 0);
                case SpeedupKind.Exploration: return Math.Max(0, State.ActiveExpedition?.RemainingSeconds ?? 0);
                default:
                    var b = State.Building(task.BuildingId);
                    if (b == null || b.Status == BuildingStatus.Complete) return 0;
                    return Math.Max(0, b.RemainingWork) / WorkPerSecondForSpeedups();
            }
        }

        /// <summary>Uses speed-ups of one size on a running job. A job they finish completes at once.</summary>
        public GameResult UseSpeedup(SpeedupTask task, SpeedupKind kind, int minutes, int count = 1)
        {
            Update();
            if (!IsRunning(task)) return GameError.NotReady("That job has already finished.");
            if (!Fits(kind, task.Kind))
                return GameError.NotReady($"{Speedups.KindName(kind)} speed-ups cannot be used on {Speedups.KindName(task.Kind).ToLowerInvariant()}.");
            if (count < 1 || SpeedupCount(kind, minutes) < count) return GameError.NotReady("You don't have that speed-up.");
            Spend(kind, minutes, count);
            string name = TaskName(task);
            bool done = Shorten(task, minutes * 60.0 * count);
            Notice(NoticeKind.Info, done ? $"{name} finished early!" : $"{name}: {Speedups.SizeName(minutes * count)} taken off.");
            EvaluateProgress();
            return GameResult.Success;
        }

        /// <summary>
        /// Picks speed-ups to finish the job with the least waste (the job's own kind before general ones,
        /// big ones while they fit, then the smallest one that covers the rest) and uses them.
        /// </summary>
        public GameResult UseSpeedupsToFinish(SpeedupTask task)
        {
            Update();
            if (!IsRunning(task)) return GameError.NotReady("That job has already finished.");
            var plan = FinishPlan(task);
            if (plan.Count == 0) return GameError.NotReady("You have no speed-ups for this.");
            foreach (var s in plan) Spend(s.Kind, s.Minutes, s.Count);
            string name = TaskName(task);
            bool done = Shorten(task, plan.Sum(s => s.Minutes * 60.0 * s.Count));
            Notice(NoticeKind.Info, done ? $"{name} finished early! Used {Speedups.Describe(plan)}." : $"{name} sped up: used {Speedups.Describe(plan)}.");
            EvaluateProgress();
            return GameResult.Success;
        }

        /// <summary>The speed-ups <see cref="UseSpeedupsToFinish"/> would use (it may not cover everything).</summary>
        public List<SpeedupStack> FinishPlan(SpeedupTask task)
        {
            double left = TaskSecondsLeft(task);
            var owned = new Dictionary<(SpeedupKind, int), int>();
            foreach (var s in OwnedSpeedups().Where(s => Fits(s.Kind, task.Kind))) owned[(s.Kind, s.Minutes)] = s.Count;
            var used = new Dictionary<(SpeedupKind, int), int>();
            // The job's own kind first, general ones only after.
            var order = new[] { task.Kind, SpeedupKind.General };
            void Use((SpeedupKind, int) key)
            {
                owned[key] -= 1;
                used[key] = (used.TryGetValue(key, out var had) ? had : 0) + 1;
                left -= key.Item2 * 60.0;
            }
            // Large ones while they do not overshoot.
            foreach (var kind in order)
                foreach (int m in Speedups.Sizes.Reverse())
                    while (left > 1e-6 && owned.TryGetValue((kind, m), out var n) && n > 0 && m * 60.0 <= left + 1e-6) Use((kind, m));
            // Then whatever is smallest that still covers the rest; failing that, everything left.
            while (left > 1e-6)
            {
                var cover = owned.Where(kv => kv.Value > 0 && kv.Key.Item2 * 60.0 >= left)
                    .OrderBy(kv => kv.Key.Item2).ThenBy(kv => kv.Key.Item1 == SpeedupKind.General ? 1 : 0).ToList();
                if (cover.Count > 0) { Use(cover[0].Key); break; }
                var any = owned.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Key.Item2).ToList();
                if (any.Count == 0) break;
                Use(any[0].Key);
            }
            return used.Select(kv => new SpeedupStack { Kind = kv.Key.Item1, Minutes = kv.Key.Item2, Count = kv.Value })
                .OrderBy(s => s.Kind).ThenByDescending(s => s.Minutes).ToList();
        }

        void Spend(SpeedupKind kind, int minutes, int count)
        {
            string key = Speedups.Key(kind, minutes);
            int left = SpeedupCount(kind, minutes) - count;
            if (left > 0) State.Speedups[key] = left;
            else State.Speedups.Remove(key);
        }

        /// <summary>Takes seconds off a job; finishes it when nothing is left. Returns true when it finished.</summary>
        bool Shorten(SpeedupTask task, double seconds)
        {
            switch (task.Kind)
            {
                case SpeedupKind.Research:
                {
                    var r = State.ActiveResearch;
                    r.RemainingSeconds -= seconds;
                    if (r.RemainingSeconds > 1e-9) return false;
                    CompleteResearch();
                    return true;
                }
                case SpeedupKind.Exploration:
                {
                    var e = State.ActiveExpedition;
                    e.RemainingSeconds -= seconds;
                    if (e.RemainingSeconds > 1e-9) return false;
                    CompleteExpedition();
                    return true;
                }
                default:
                {
                    var b = State.Building(task.BuildingId);
                    b.RemainingWork -= seconds * WorkPerSecondForSpeedups();
                    if (b.RemainingWork > 1e-9) return false;
                    FinishConstruction(b);
                    return true;
                }
            }
        }
    }
}
