using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>A natural resource cell: a stand of trees, a stony outcrop or an ore vein.</summary>
    public sealed class ResourceNode
    {
        public int X, Y;
        public TerrainType Terrain;
        public ResourceType Resource;
        public JobType Job;
        public double Capacity, Remaining;
        public bool Ordered;
        public bool Depleted => Remaining <= 1e-6;
        public double Fraction => Capacity <= 0 ? 1 : Remaining / Capacity;
    }

    /// <summary>
    /// Survival gathering: wood, stone and ore come out of the land itself. Every forest, rock and
    /// ore cell holds a limited amount; gatherers take it from the cells nearest their workplace
    /// (or from the ones the player marked), forests grow back after a while, and a cleared
    /// forest is open land that can be built on. When nothing is left within reach, the camp stops.
    /// </summary>
    public sealed partial class Game
    {
        static readonly (TerrainType terrain, ResourceType resource, JobType job)[] NodeKinds =
        {
            (TerrainType.Forest, ResourceType.Wood, JobType.Woodcutting),
            (TerrainType.Rock, ResourceType.Stone, JobType.Stonecutting),
            (TerrainType.Ore, ResourceType.Ore, JobType.Mining),
        };

        /// <summary>Jobs whose last step found nothing left to gather within reach.</summary>
        readonly HashSet<JobType> exhaustedJobs = new HashSet<JobType>();

        public bool NodesEnabled => Content.Balance.ForestWood > 0 || Content.Balance.RockStone > 0 || Content.Balance.VeinOre > 0;

        int CellKey(int x, int y) => y * Content.Map.Width + x;

        double CapacityOf(TerrainType t)
        {
            var b = Content.Balance;
            return t == TerrainType.Forest ? b.ForestWood : t == TerrainType.Rock ? b.RockStone : t == TerrainType.Ore ? b.VeinOre : 0;
        }

        /// <summary>The terrain as it is now: a cut-down forest is open grass until it grows back.</summary>
        public TerrainType? TerrainNow(int x, int y)
        {
            var t = Content.Map.Terrain(x, y);
            if (t == TerrainType.Forest && State.NodeCleared.ContainsKey(CellKey(x, y))) return TerrainType.Grass;
            return t;
        }

        /// <summary>The resource node at a cell, or null if the cell holds nothing to gather.</summary>
        public ResourceNode NodeAt(int x, int y)
        {
            var map = Content.Map;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return null;
            var t = map.Terrain(x, y);
            foreach (var kind in NodeKinds)
            {
                if (kind.terrain != t) continue;
                double cap = CapacityOf(t.Value);
                int key = CellKey(x, y);
                double taken = State.NodeTaken.TryGetValue(key, out var v) ? v : 0;
                bool cleared = State.NodeCleared.ContainsKey(key);
                return new ResourceNode
                {
                    X = x, Y = y, Terrain = t.Value, Resource = kind.resource, Job = kind.job,
                    Capacity = cap, Remaining = cap <= 0 ? double.PositiveInfinity : cleared ? 0 : Math.Max(0, cap - taken),
                    Ordered = State.HarvestOrders.Contains(key),
                };
            }
            return null;
        }

        /// <summary>True when a forest, rock or ore cell has been used up.</summary>
        public bool IsDepleted(int x, int y) => NodeAt(x, y)?.Depleted ?? false;

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
            if (JobCapacity(node.Job) == 0)
                Notice(NoticeKind.Info, $"Marked. Build a workplace for {Content.JobConfig(node.Job)?.Name ?? node.Job.Id()} so someone can gather it.");
            return GameResult.Success;
        }

        /// <summary>Whether gatherers of a job found nothing within reach in the last step.</summary>
        public bool IsExhausted(JobType job) => exhaustedJobs.Contains(job);

        /// <summary>Total amount still standing within reach of the workplaces of a job.</summary>
        public double ReachableAmount(JobType job) => GatherCells(job).Sum(c => NodeAt(c.x, c.y)?.Remaining ?? 0);

        /// <summary>
        /// The cells a job gathers from, in order: the player's orders first, then the cells nearest
        /// to any of its workplaces within reach.
        /// </summary>
        public List<(int x, int y)> GatherCells(JobType job)
        {
            var result = new List<(int x, int y)>();
            int index = Array.FindIndex(NodeKinds, k => k.job == job);
            if (index < 0) return result;
            var kind = NodeKinds[index];
            int w = Content.Map.Width;
            foreach (int key in State.HarvestOrders)
            {
                int x = key % w, y = key / w;
                var n = NodeAt(x, y);
                if (n != null && n.Job == job && !n.Depleted) result.Add((x, y));
            }
            int reach = Content.Balance.GatherReach;
            var sites = OperationalBuildings().Where(p => p.def.Slots(job) > 0).Select(p => p.building).ToList();
            var near = new List<(int d, int x, int y)>();
            var seen = new HashSet<int>(result.Select(c => CellKey(c.x, c.y)));
            foreach (var site in sites)
            {
                var fp = FootprintOf(site);
                for (int y = site.Y - reach; y < site.Y + fp.Height + reach; y++)
                    for (int x = site.X - reach; x < site.X + fp.Width + reach; x++)
                    {
                        if (Content.Map.Terrain(x, y) != kind.terrain || !IsRevealed(x, y)) continue;
                        int key = CellKey(x, y);
                        if (!seen.Add(key) || IsDepleted(x, y)) continue;
                        int dx = Math.Max(0, Math.Max(site.X - x, x - (site.X + fp.Width - 1)));
                        int dy = Math.Max(0, Math.Max(site.Y - y, y - (site.Y + fp.Height - 1)));
                        near.Add((dx * dx + dy * dy, x, y));
                    }
            }
            result.AddRange(near.OrderBy(n => n.d).ThenBy(n => n.y).ThenBy(n => n.x).Select(n => (n.x, n.y)));
            return result;
        }

        /// <summary>
        /// Takes what the gatherers produced in this step out of the land. Returns how much could
        /// not be found (the caller removes it from the stockpile again).
        /// </summary>
        double DrainNodes(JobType job, double amount)
        {
            if (amount <= 0) return 0;
            var kind = NodeKinds.First(k => k.job == job);
            if (CapacityOf(kind.terrain) <= 0) return 0;
            double left = amount;
            foreach (var (x, y) in GatherCells(job))
            {
                var node = NodeAt(x, y);
                if (node == null || node.Depleted) continue;
                double take = Math.Min(left, node.Remaining);
                int key = CellKey(x, y);
                State.NodeTaken[key] = (State.NodeTaken.TryGetValue(key, out var v) ? v : 0) + take;
                left -= take;
                if (node.Remaining - take <= 1e-6)
                {
                    State.HarvestOrders.Remove(key);
                    if (node.Terrain == TerrainType.Forest) State.NodeCleared[key] = State.SimTime;
                }
                if (left <= 1e-9) break;
            }
            return left;
        }

        /// <summary>Gatherers draw their output from the land; cut forests grow back.</summary>
        void AdvanceNodes(double dt)
        {
            if (!NodesEnabled) return;
            foreach (var kind in NodeKinds)
            {
                var cfg = Content.JobConfig(kind.job);
                if (cfg?.Output == null || CapacityOf(kind.terrain) <= 0) continue;
                double made = SkilledWorkers(kind.job) * RatePerWorker(kind.job) * dt;
                if (made <= 0) { exhaustedJobs.Remove(kind.job); continue; }
                double missing = DrainNodes(kind.job, made);
                if (missing > 1e-9)
                {
                    var res = State.Resources;
                    res[cfg.Output.Value] = Math.Max(0, res[cfg.Output.Value] - missing);
                    State.Resources = res;
                    if (exhaustedJobs.Add(kind.job))
                        Notice(NoticeKind.Warning, NothingLeftText(kind.job));
                }
                else exhaustedJobs.Remove(kind.job);
            }
            double regrow = Content.Balance.TreeRegrowSeconds;
            if (State.NodeCleared.Count > 0 && regrow > 0)
            {
                var grown = State.NodeCleared.Where(kv => State.SimTime - kv.Value >= regrow).Select(kv => kv.Key).ToList();
                foreach (var key in grown)
                {
                    State.NodeCleared.Remove(key);
                    State.NodeTaken.Remove(key);
                }
            }
        }

        static string NothingLeftText(JobType job) => job switch
        {
            JobType.Woodcutting => "No trees left near the wood camps. Build a camp by another forest or mark trees to cut.",
            JobType.Stonecutting => "No rock left near the stone sites. Build one by another outcrop.",
            _ => "The ore vein near the mine is exhausted. Open a mine at another vein.",
        };
    }
}
