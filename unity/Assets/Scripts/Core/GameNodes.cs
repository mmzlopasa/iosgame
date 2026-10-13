using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>A natural resource cell: a stand of trees, a stony outcrop, an ore vein, a berry bush, fish or game.</summary>
    public sealed class ResourceNode
    {
        public int X, Y;
        public NodeDefinition Kind;
        public TerrainType Terrain;
        public ResourceType Resource;
        public JobType Job;
        public double Capacity, Remaining;
        public bool Ordered;
        /// <summary>Used up and waiting to grow back (forests, bushes, fish and game return).</summary>
        public bool Regrowing;
        public bool Depleted => Remaining <= 1e-6;
        public double Fraction => Capacity <= 0 ? 1 : Remaining / Capacity;
    }

    /// <summary>
    /// Survival gathering: wood, stone, ore and wild food come out of the land itself. Every
    /// forest, rock and ore cell, berry bush, fishing water and game forest holds a limited
    /// amount; gatherers take it from the cells nearest their workplace (or from the ones the
    /// player marked), forests, bushes, fish and game come back after a while, and a cleared
    /// forest is open land that can be built on. When nothing is left within reach, the work stops.
    /// </summary>
    public sealed partial class Game
    {
        List<NodeDefinition> nodeKinds;

        /// <summary>Every kind of node: the three classic ones from the balance plus the extra list.</summary>
        public List<NodeDefinition> NodeKinds
        {
            get
            {
                if (nodeKinds != null) return nodeKinds;
                var b = Content.Balance;
                nodeKinds = new List<NodeDefinition>
                {
                    new NodeDefinition { Id = "trees", Name = "Forest", Terrain = TerrainType.Forest, Resource = ResourceType.Wood, Job = JobType.Woodcutting,
                                         Capacity = b.ForestWood, RegrowSeconds = b.TreeRegrowSeconds, ClearsTerrain = true },
                    new NodeDefinition { Id = "rock", Name = "Stone Outcrop", Terrain = TerrainType.Rock, Resource = ResourceType.Stone, Job = JobType.Stonecutting,
                                         Capacity = b.RockStone },
                    new NodeDefinition { Id = "ore", Name = "Ore Vein", Terrain = TerrainType.Ore, Resource = ResourceType.Ore, Job = JobType.Mining,
                                         Capacity = b.VeinOre },
                };
                nodeKinds.AddRange(b.Nodes);
                return nodeKinds;
            }
        }

        /// <summary>Jobs whose last step found nothing left to gather within reach.</summary>
        readonly HashSet<JobType> exhaustedJobs = new HashSet<JobType>();
        readonly Dictionary<JobType, List<(int x, int y)>> gatherCache = new Dictionary<JobType, List<(int x, int y)>>();
        long gatherStamp = long.MinValue;

        long GatherStamp()
        {
            var s = State;
            int complete = 0, operational = 0;
            foreach (var b in s.Buildings)
            {
                if (b.Status == BuildingStatus.Complete) complete++;
                if (b.IsOperational) operational++;
            }
            unchecked
            {
                long h = 17;
                foreach (long v in new long[] { s.Buildings.Count, s.NextBuildingId, complete, operational, s.Decorations.Count, s.NextDecorationId,
                                                s.HarvestOrders.Count, s.HarvestOrders.Count > 0 ? s.HarvestOrders[0] : -1, s.ExploredRegions.Count,
                                                s.NodeCleared.Count, s.ReachedMilestones.Count })
                    h = h * 1000003 + v;
                return h;
            }
        }

        public bool NodesEnabled => NodeKinds.Any(k => k.Capacity > 0);

        int CellKey(int x, int y) => y * Content.Map.Width + x;

        int NodeKey(NodeDefinition kind, int x, int y) => kind.Slot * Content.Map.Width * Content.Map.Height + CellKey(x, y);

        /// <summary>Whether a cell holds this kind at all (berry bushes grow only on some grass cells).</summary>
        public static bool HasFeature(NodeDefinition kind, int x, int y)
        {
            if (kind.Density >= 1) return true;
            uint id = 2166136261u;
            foreach (char ch in kind.Id) id = (id ^ ch) * 16777619u; // stable across runtimes (string hashes are not)
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ id * 2654435761u;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h % 10000) / 10000.0 < kind.Density;
        }

        /// <summary>The terrain as it is now: a cut-down forest is open grass until it grows back.</summary>
        public TerrainType? TerrainNow(int x, int y)
        {
            var t = Content.Map.Terrain(x, y);
            if (t == TerrainType.Forest && State.NodeCleared.ContainsKey(CellKey(x, y))) return TerrainType.Grass;
            return t;
        }

        NodeDefinition KindAt(int x, int y, JobType? job)
        {
            var t = Content.Map.Terrain(x, y);
            if (t == null) return null;
            foreach (var kind in NodeKinds)
            {
                if (kind.Terrain != t || (job.HasValue && kind.Job != job.Value)) continue;
                if (!HasFeature(kind, x, y)) continue;
                // The extra kinds exist only when they hold something; trees, rock and ore are always there.
                if (kind.Capacity <= 0 && NodeKinds.IndexOf(kind) >= 3) continue;
                return kind;
            }
            return null;
        }

        /// <summary>
        /// The resource node at a cell (the main one, or the one a job gathers there), or null if the
        /// cell holds nothing to gather.
        /// </summary>
        public ResourceNode NodeAt(int x, int y, JobType? job = null)
        {
            var map = Content.Map;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return null;
            var kind = KindAt(x, y, job);
            if (kind == null) return null;
            double cap = kind.Capacity;
            int key = NodeKey(kind, x, y);
            double taken = State.NodeTaken.TryGetValue(key, out var v) ? v : 0;
            bool cleared = State.NodeCleared.ContainsKey(key);
            return new ResourceNode
            {
                X = x, Y = y, Kind = kind, Terrain = kind.Terrain, Resource = kind.Resource, Job = kind.Job,
                Capacity = cap, Remaining = cap <= 0 ? double.PositiveInfinity : cleared ? 0 : Math.Max(0, cap - taken),
                Ordered = State.HarvestOrders.Contains(CellKey(x, y)), Regrowing = cleared,
            };
        }

        /// <summary>True when a forest, rock or ore cell has been used up.</summary>
        public bool IsDepleted(int x, int y) => NodeAt(x, y)?.Depleted ?? false;

        /// <summary>Seconds until a used-up node is back (0 when it is not regrowing or never comes back).</summary>
        public double RegrowSecondsLeft(ResourceNode n)
        {
            if (n?.Kind == null || n.Kind.RegrowSeconds <= 0) return 0;
            return State.NodeCleared.TryGetValue(NodeKey(n.Kind, n.X, n.Y), out var at) ? Math.Max(0, n.Kind.RegrowSeconds - (State.SimTime - at)) : 0;
        }

        /// <summary>Marks a node to be harvested before any other (tap it again to cancel).</summary>
        public GameResult ToggleHarvestOrder(int x, int y)
        {
            var node = NodeAt(x, y);
            if (node == null) return GameError.UnknownContent($"no resource at {x},{y}");
            if (!IsRevealed(x, y)) return GameError.Placement(PlacementIssue.Unexplored);
            int key = CellKey(x, y);
            if (State.HarvestOrders.Remove(key)) return GameResult.Success;
            if (node.Depleted) return GameError.NothingLeft;
            State.HarvestOrders.Add(key);
            gatherCache.Clear();
            if (JobCapacity(node.Job) == 0)
                Notice(NoticeKind.Info, $"Marked. Build a workplace for {Content.JobConfig(node.Job)?.Name ?? node.Job.Id()} so someone can gather it.");
            return GameResult.Success;
        }

        /// <summary>
        /// The player sends someone to a node right away: it is marked to be gathered first and,
        /// if nobody works that job yet, an idle person (or one from the largest other gathering
        /// crew) takes it up.
        /// </summary>
        public GameResult SendGatherer(int x, int y)
        {
            Update();
            var node = NodeAt(x, y);
            if (node == null) return GameError.UnknownContent($"no resource at {x},{y}");
            if (!IsRevealed(x, y)) return GameError.Placement(PlacementIssue.Unexplored);
            if (node.Depleted) return GameError.NothingLeft;
            int key = CellKey(x, y);
            if (!State.HarvestOrders.Contains(key)) State.HarvestOrders.Insert(0, key);
            else
            {
                State.HarvestOrders.Remove(key);
                State.HarvestOrders.Insert(0, key);
            }
            gatherCache.Clear();
            var job = node.Job;
            int capacity = JobCapacity(job);
            if (capacity == 0)
            {
                Notice(NoticeKind.Info, $"Marked. Build a workplace for {Content.JobConfig(job)?.Name ?? job.Id()} so someone can gather it.");
                return GameResult.Success;
            }
            if (State.Assignments[job] < capacity)
            {
                if (IdleWorkers > 0) State.Assignments[job] += 1;
                else
                {
                    // Borrow a hand from the largest crew of another gathering job.
                    var donor = Enums.Jobs.Where(j => j != job && IsGathering(j) && State.Assignments[j] > 1)
                                     .OrderByDescending(j => State.Assignments[j]).Cast<JobType?>().FirstOrDefault();
                    if (donor.HasValue)
                    {
                        State.Assignments[donor.Value] -= 1;
                        State.Assignments[job] += 1;
                    }
                }
                SyncPeople();
            }
            string who = Content.Skill(job)?.Title ?? Content.JobConfig(job)?.Name ?? job.Id();
            Notice(NoticeKind.Info, State.Assignments[job] > 0 ? $"{who}s are on their way." : $"Marked. Assign someone to {Content.JobConfig(job)?.Name ?? job.Id()}.");
            EvaluateProgress();
            return GameResult.Success;
        }

        /// <summary>How many cells of a job's resource a workplace placed here would reach (shown while placing).</summary>
        public int NodesInReach(JobType job, int x, int y, Footprint fp)
        {
            if (!NodeKinds.Any(k => k.Job == job)) return -1;
            int reach = Content.Balance.GatherReach, count = 0;
            for (int yy = y - reach; yy < y + fp.Height + reach; yy++)
                for (int xx = x - reach; xx < x + fp.Width + reach; xx++)
                {
                    if (!RevealedFast(xx, yy)) continue;
                    var n = NodeAt(xx, yy, job);
                    if (n != null && !n.Depleted && n.Job == job) count++;
                }
            return count;
        }

        /// <summary>Whether gatherers of a job found nothing within reach in the last step.</summary>
        public bool IsExhausted(JobType job) => exhaustedJobs.Contains(job);

        /// <summary>Total amount still standing within reach of the workplaces of a job.</summary>
        public double ReachableAmount(JobType job) => GatherCells(job).Sum(c => NodeAt(c.x, c.y, job)?.Remaining ?? 0);

        bool[] revealedGrid;
        int revealedStamp = -1;

        bool RevealedFast(int x, int y)
        {
            var map = Content.Map;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
            if (revealedGrid == null || revealedStamp != State.ExploredRegions.Count)
            {
                revealedGrid = new bool[map.Width * map.Height];
                for (int yy = 0; yy < map.Height; yy++)
                    for (int xx = 0; xx < map.Width; xx++) revealedGrid[yy * map.Width + xx] = IsRevealed(xx, yy);
                revealedStamp = State.ExploredRegions.Count;
            }
            return revealedGrid[y * map.Width + x];
        }

        /// <summary>
        /// The cells a job gathers from, in order: the player's orders first, then the cells nearest
        /// to any of its workplaces within reach.
        /// </summary>
        public List<(int x, int y)> GatherCells(JobType job)
        {
            // The cells only change when something is built, finished, taken down, marked, explored,
            // used up or grown back: until then the list is kept (working it out is the heaviest
            // part of a step, and a long absence is thousands of steps).
            long stamp = GatherStamp();
            if (stamp != gatherStamp)
            {
                gatherCache.Clear();
                gatherStamp = stamp;
            }
            if (gatherCache.TryGetValue(job, out var cached)) return cached;
            var result = new List<(int x, int y)>();
            var kinds = NodeKinds.Where(k => k.Job == job).ToList();
            if (kinds.Count == 0) return result;
            int w = Content.Map.Width;
            foreach (int key in State.HarvestOrders)
            {
                int x = key % w, y = key / w;
                var n = NodeAt(x, y, job);
                if (n != null && !n.Depleted) result.Add((x, y));
            }
            int reach = Content.Balance.GatherReach;
            var sites = OperationalBuildings().Where(p => SlotsOf(p.building, p.def, job) > 0).Select(p => p.building).ToList();
            var near = new List<(int d, int x, int y)>();
            var seen = new HashSet<int>(result.Select(c => CellKey(c.x, c.y)));
            foreach (var site in sites)
            {
                var fp = FootprintOf(site);
                for (int y = site.Y - reach; y < site.Y + fp.Height + reach; y++)
                    for (int x = site.X - reach; x < site.X + fp.Width + reach; x++)
                    {
                        if (!RevealedFast(x, y)) continue;
                        var t = Content.Map.Terrain(x, y);
                        bool match = false;
                        foreach (var k in kinds) if (k.Terrain == t && HasFeature(k, x, y)) { match = true; break; }
                        if (!match) continue;
                        int key = CellKey(x, y);
                        if (!seen.Add(key)) continue;
                        var n = NodeAt(x, y, job);
                        if (n == null || n.Depleted) continue;
                        // A building or a road on top of a bush: nothing to pick there.
                        if (t == TerrainType.Grass && (BuildingIdAt(x, y) != null || DecorationAt(x, y) != null)) continue;
                        int dx = Math.Max(0, Math.Max(site.X - x, x - (site.X + fp.Width - 1)));
                        int dy = Math.Max(0, Math.Max(site.Y - y, y - (site.Y + fp.Height - 1)));
                        near.Add((dx * dx + dy * dy, x, y));
                    }
            }
            result.AddRange(near.OrderBy(n => n.d).ThenBy(n => n.y).ThenBy(n => n.x).Select(n => (n.x, n.y)));
            gatherCache[job] = result;
            return result;
        }

        /// <summary>
        /// Takes what the gatherers produced in this step out of the land. Returns how much could
        /// not be found (the caller removes it from the stockpile again).
        /// </summary>
        double DrainNodes(JobType job, double amount)
        {
            if (amount <= 0) return 0;
            double left = amount;
            foreach (var (x, y) in GatherCells(job))
            {
                var node = NodeAt(x, y, job);
                if (node == null || node.Depleted) continue;
                double take = Math.Min(left, node.Remaining);
                int key = NodeKey(node.Kind, x, y);
                State.NodeTaken[key] = (State.NodeTaken.TryGetValue(key, out var v) ? v : 0) + take;
                left -= take;
                if (node.Remaining - take <= 1e-6)
                {
                    State.HarvestOrders.Remove(CellKey(x, y));
                    if (node.Kind.RegrowSeconds > 0) State.NodeCleared[key] = State.SimTime;
                    gatherCache.Remove(job);
                }
                if (left <= 1e-9) break;
            }
            return left;
        }

        /// <summary>Gatherers draw their output from the land; forests, bushes, fish and game come back.</summary>
        void AdvanceNodes(double dt)
        {
            if (!NodesEnabled) return;
            foreach (var job in NodeKinds.Where(k => k.Capacity > 0).Select(k => k.Job).Distinct())
            {
                var cfg = Content.JobConfig(job);
                if (cfg?.Output == null) continue;
                double made = SkilledWorkers(job) * RatePerWorker(job) * dt;
                if (made <= 0) { exhaustedJobs.Remove(job); continue; }
                double missing = DrainNodes(job, made);
                if (missing > 1e-9)
                {
                    var res = State.Resources;
                    res[cfg.Output.Value] = Math.Max(0, res[cfg.Output.Value] - missing);
                    if (cfg.Byproduct.HasValue)
                        res[cfg.Byproduct.Value] = Math.Max(0, res[cfg.Byproduct.Value] - missing * cfg.ByproductRatio);
                    State.Resources = res;
                    if (exhaustedJobs.Add(job))
                        Notice(NoticeKind.Warning, NothingLeftText(job));
                }
                else exhaustedJobs.Remove(job);
            }
            if (State.NodeCleared.Count > 0)
            {
                int cells = Content.Map.Width * Content.Map.Height;
                var regrown = new List<int>();
                foreach (var kv in State.NodeCleared)
                {
                    int slot = kv.Key / cells, cell = kv.Key % cells;
                    var t = Content.Map.Terrain(cell % Content.Map.Width, cell / Content.Map.Width);
                    var kind = NodeKinds.FirstOrDefault(k => k.Slot == slot && k.Terrain == t);
                    double regrow = kind?.RegrowSeconds ?? Content.Balance.TreeRegrowSeconds;
                    if (!(regrow > 0 && State.SimTime - kv.Value >= regrow)) continue;
                    // No forest grows back under a house built on the clearing (it would stand in the woods
                    // and be cut down by the woodcutters); it returns once the building is gone.
                    if (kind != null && kind.ClearsTerrain && BuildingIdAt(cell % Content.Map.Width, cell / Content.Map.Width) != null) continue;
                    regrown.Add(kv.Key);
                }
                foreach (var key in regrown)
                {
                    State.NodeCleared.Remove(key);
                    State.NodeTaken.Remove(key);
                }
                if (regrown.Count > 0) gatherCache.Clear();
            }
        }

        static string NothingLeftText(JobType job) => job switch
        {
            JobType.Woodcutting => "No trees left near the wood camps. Build a camp by another forest or tap trees to cut.",
            JobType.Stonecutting => "No rock left near the stone sites. Build one by another outcrop.",
            JobType.Foraging => "The berry bushes near camp are picked clean. They ripen again soon; forage further out meanwhile.",
            JobType.Fishing => "The water by the weir is fished out for now. The fish come back in a while.",
            JobType.Hunting => "The game near the lodge has fled. Hunt in other woods or wait for the herds to return.",
            _ => "The ore vein near the mine is exhausted. Open a mine at another vein.",
        };
    }
}
