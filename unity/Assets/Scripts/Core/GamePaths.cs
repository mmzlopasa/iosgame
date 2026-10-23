using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Organic paths (Manor Lords' grid-less towns): people walk between the heart of the town and the homes and
    /// workplaces, and their feet wear the ground. A cell walked enough becomes a trodden path: people walk it
    /// faster, and building sites beside it are reached sooner, so the town grows along the ways people already
    /// take. Paths no one walks any more grass over again.
    /// </summary>
    public sealed partial class Game
    {
        public bool HasPaths => Content.Balance.Paths != null;

        /// <summary>Changes whenever a cell becomes or stops being a path (for views that redraw the ground).</summary>
        public int PathsVersion { get; private set; }

        double pathClock;

        public double Wear(int x, int y) => State.Wear.TryGetValue(CellKey(x, y), out var w) ? w : 0;

        public bool IsTrodden(int x, int y) => HasPaths && Wear(x, y) >= Content.Balance.Paths.Threshold;

        /// <summary>Every trodden cell.</summary>
        public IEnumerable<(int x, int y)> TroddenCells()
        {
            if (!HasPaths) yield break;
            double t = Content.Balance.Paths.Threshold;
            int w = Content.Map.Width;
            foreach (var kv in State.Wear)
                if (kv.Value >= t) yield return (kv.Key % w, kv.Key / w);
        }

        /// <summary>A building site touching a trodden path (its builders get there sooner).</summary>
        public bool BesidePath(PlacedBuilding b)
        {
            if (!HasPaths || State.Wear.Count == 0) return false;
            var fp = FootprintOf(b);
            for (int y = b.Y - 1; y <= b.Y + fp.Height; y++)
                for (int x = b.X - 1; x <= b.X + fp.Width; x++)
                    if (IsTrodden(x, y)) return true;
            return false;
        }

        /// <summary>Construction speed on a site: paths beside it help.</summary>
        double SiteSpeed(PlacedBuilding b) => BesidePath(b) ? 1 + Content.Balance.Paths.ConstructionBonus : 1;

        // MARK: - Foot traffic

        /// <summary>One way people walk often: its cells and how many people walk it.</summary>
        struct Walk
        {
            public List<int> Cells;
            public double People;
        }

        List<Walk> walks;
        string walksKey;

        /// <summary>The ways people walk now: from the heart to every home (its people) and to every workplace (its workers).</summary>
        List<Walk> Walks()
        {
            var heart = Heart;
            string key = heart == null ? "" :
                $"{heart.X},{heart.Y}|{State.Population}|{State.NextBuildingId}|{State.Buildings.Count}|{string.Join(",", State.Buildings.Select(b => b.IsOperational ? 1 : 0))}|" +
                string.Join(",", Enums.Jobs.Select(j => State.Assignments[j]));
            if (walks != null && key == walksKey) return walks;
            walksKey = key;
            walks = new List<Walk>();
            if (heart == null) return walks;

            // Cells under buildings are not walked (people go round them or inside).
            var built = new HashSet<int>();
            foreach (var b in State.Buildings)
            {
                var fp = FootprintOf(b);
                for (int y = b.Y; y < b.Y + fp.Height; y++)
                    for (int x = b.X; x < b.X + fp.Width; x++) built.Add(CellKey(x, y));
            }
            var hf = FootprintOf(heart);
            double hx = heart.X + hf.Width / 2.0, hy = heart.Y + hf.Height / 2.0;

            int left = State.Population;
            foreach (var (b, def) in OperationalBuildings())
            {
                if (b == heart) continue;
                double people = 0;
                int homes = HousingOf(b, def);
                if (homes > 0)
                {
                    int living = Math.Min(homes, left);
                    left -= living;
                    people += living;
                }
                if (def.JobSlots != null)
                    foreach (var slot in def.JobSlots)
                    {
                        int cap = JobCapacity(slot.Job);
                        if (cap > 0) people += State.Assignments[slot.Job] * (double)slot.Count / cap;
                    }
                if (people <= 0) continue;
                var fp = FootprintOf(b);
                var cells = Line(hx, hy, b.X + fp.Width / 2.0, b.Y + fp.Height / 2.0, built);
                if (cells.Count > 0) walks.Add(new Walk { Cells = cells, People = people });
            }
            return walks;
        }

        /// <summary>The cells a straight walk crosses, without built-on cells and land no one can walk.</summary>
        List<int> Line(double x0, double y0, double x1, double y1, HashSet<int> built)
        {
            var cells = new List<int>();
            double dx = x1 - x0, dy = y1 - y0;
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2);
            int last = -1;
            for (int i = 0; i <= steps; i++)
            {
                double t = steps == 0 ? 0 : i / (double)steps;
                int x = (int)Math.Floor(x0 + dx * t), y = (int)Math.Floor(y0 + dy * t);
                if (x < 0 || y < 0 || x >= Content.Map.Width || y >= Content.Map.Height) continue;
                int k = CellKey(x, y);
                if (k == last || built.Contains(k)) continue;
                last = k;
                var terrain = TerrainNow(x, y);
                if (terrain == null || terrain == TerrainType.Water || terrain == TerrainType.Mountain || terrain == TerrainType.Rock
                    || terrain == TerrainType.Ore || terrain == TerrainType.Forest) continue;
                cells.Add(k);
            }
            return cells;
        }

        /// <summary>Feet wear the ground (applied once a game minute; cheap even for long offline catch-ups).</summary>
        void AdvancePaths(double dt)
        {
            var cfg = Content.Balance.Paths;
            if (cfg == null) return;
            pathClock += dt;
            if (pathClock < 60) return;
            double hours = pathClock / 3600;
            pathClock = 0;
            double t = cfg.Threshold;
            // Old wear grasses over; then this minute's walking adds to it.
            if (State.Wear.Count > 0 && cfg.DecayPerHour > 0)
            {
                double keep = Math.Pow(1 - cfg.DecayPerHour, hours);
                foreach (var k in State.Wear.Keys.ToList())
                {
                    double before = State.Wear[k], after = before * keep;
                    if (before >= t && after < t) PathsVersion++;
                    if (after < 0.01) State.Wear.Remove(k);
                    else State.Wear[k] = after;
                }
            }
            foreach (var walk in Walks())
            {
                double add = walk.People * cfg.WearPerPersonPerHour * hours;
                foreach (var k in walk.Cells)
                {
                    double before = State.Wear.TryGetValue(k, out var w) ? w : 0;
                    double after = Math.Min(cfg.MaxWear, before + add);
                    if (before < t && after >= t) PathsVersion++;
                    State.Wear[k] = after;
                }
            }
        }
    }
}
