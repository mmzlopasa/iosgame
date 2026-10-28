using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Where people can walk: a grid over the map cells. Buildings, standing forest, rock, ore,
    /// mountains, fences and walls block the way; water only where a bridge crosses it. Roads are
    /// cheaper, so people prefer them. Paths are found with A* (eight directions, no corner
    /// cutting past obstacles) and then straightened where the line of sight is clear.
    /// </summary>
    public sealed class Pathing
    {
        readonly int width, height;
        readonly bool[] blocked;
        readonly float[] cost;
        (int, int, int, int, int, int, int, GameState) key;

        public Pathing(int width, int height)
        {
            this.width = width;
            this.height = height;
            blocked = new bool[width * height];
            cost = new float[width * height];
        }

        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        public bool Walkable(int x, int y) => Inside(x, y) && !blocked[y * width + x];

        /// <summary>Rebuilds the grid when buildings, decorations or the land changed.</summary>
        public void Refresh(Game game)
        {
            var s = game.State;
            int turned = 0;
            foreach (var b in s.Buildings) if (b.Rotation != 0) turned++;
            var k = (s.NextBuildingId, s.Buildings.Count, s.NextDecorationId, s.Decorations.Count, s.NodeCleared.Count, turned, game.PathsVersion, s);
            if (k.Equals(key)) return;
            key = k;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var t = game.TerrainNow(x, y);
                    bool block = t == TerrainType.Water || t == TerrainType.Mountain || t == TerrainType.Rock || t == TerrainType.Ore
                                 || t == TerrainType.Forest || t == null;
                    float c = t == TerrainType.Sand ? 1.15f : 1f;
                    if (game.IsTrodden(x, y)) c = (float)game.Content.Balance.Paths.WalkCost;
                    var d = game.DecorationAt(x, y);
                    var def = d == null ? null : game.Content.Decoration(d.DefinitionId);
                    if (def != null)
                    {
                        if (def.IsBridge) block = false;
                        if (def.Blocks) block = true;
                        if (def.IsRoad) c = 0.55f;
                    }
                    blocked[y * width + x] = block;
                    cost[y * width + x] = c;
                }
            foreach (var b in s.Buildings)
            {
                var fp = game.FootprintOf(b);
                for (int dy = 0; dy < fp.Height; dy++)
                    for (int dx = 0; dx < fp.Width; dx++)
                        if (Inside(b.X + dx, b.Y + dy)) blocked[(b.Y + dy) * width + b.X + dx] = true;
            }
        }

        /// <summary>The walkable cell nearest to (x, y), searching outwards a few rings.</summary>
        public (int x, int y)? NearestWalkable(int x, int y, int maxRing = 6)
        {
            if (Walkable(x, y)) return (x, y);
            for (int r = 1; r <= maxRing; r++)
            {
                (int, int)? best = null;
                float bestD = float.MaxValue;
                for (int yy = y - r; yy <= y + r; yy++)
                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        if (Mathf.Max(Mathf.Abs(xx - x), Mathf.Abs(yy - y)) != r || !Walkable(xx, yy)) continue;
                        float d = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                        if (d < bestD) { bestD = d; best = (xx, yy); }
                    }
                if (best != null) return best;
            }
            return null;
        }

        static readonly (int dx, int dy, float len)[] Steps =
        {
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f),
        };

        // Reused buffers (paths are searched on the main thread one at a time).
        float[] g;
        int[] parent;
        int[] stamp;
        int search;
        readonly MinHeap open = new MinHeap();

        /// <summary>
        /// Cells from start to goal (both included), or null when the goal cannot be reached. A
        /// blocked start (someone standing in a doorway) may be left; a blocked goal is replaced
        /// by the nearest walkable cell.
        /// </summary>
        public List<(int x, int y)> FindPath((int x, int y) start, (int x, int y) goal, int maxExpand = 4000)
        {
            var s = NearestWalkable(start.x, start.y, 3);
            var t = NearestWalkable(goal.x, goal.y);
            if (s == null || t == null) return null;
            int n = width * height;
            if (g == null) { g = new float[n]; parent = new int[n]; stamp = new int[n]; }
            search++;
            int si = s.Value.y * width + s.Value.x, ti = t.Value.y * width + t.Value.x;
            open.Clear();
            g[si] = 0;
            parent[si] = -1;
            stamp[si] = search;
            open.Push(si, H(si, ti));
            int expanded = 0;
            while (open.Count > 0)
            {
                int cur = open.Pop(out float f);
                if (cur == ti) break;
                if (f - H(cur, ti) > g[cur] + 1e-3f) continue; // stale entry
                if (++expanded > maxExpand) return null;
                int cx = cur % width, cy = cur / width;
                foreach (var (dx, dy, len) in Steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!Walkable(nx, ny)) continue;
                    if (dx != 0 && dy != 0 && (!Walkable(cx + dx, cy) || !Walkable(cx, cy + dy))) continue; // no squeezing past corners
                    int ni = ny * width + nx;
                    float ng = g[cur] + len * 0.5f * (cost[cur] + cost[ni]);
                    if (stamp[ni] == search && g[ni] <= ng) continue;
                    stamp[ni] = search;
                    g[ni] = ng;
                    parent[ni] = cur;
                    open.Push(ni, ng + H(ni, ti));
                }
            }
            if (stamp[ti] != search) return null;
            var cells = new List<(int, int)>();
            for (int i = ti; i != -1; i = parent[i]) cells.Add((i % width, i / width));
            cells.Reverse();
            return cells;
        }

        float H(int a, int b)
        {
            int dx = Mathf.Abs(a % width - b % width), dy = Mathf.Abs(a / width - b / width);
            // Octile distance at road cost, so roads stay attractive without breaking the bound.
            return 0.55f * (Mathf.Max(dx, dy) + 0.4142f * Mathf.Min(dx, dy));
        }

        /// <summary>True when the straight line between two cells crosses only walkable cells of equal kind.</summary>
        public bool ClearLine((int x, int y) a, (int x, int y) b)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            float c0 = cost[y0 * width + x0];
            while (true)
            {
                if (!Walkable(x0, y0)) return false;
                // Keep to the road while on it: do not cut across the grass beside it.
                if (Mathf.Abs(cost[y0 * width + x0] - c0) > 0.01f) return false;
                if (x0 == x1 && y0 == y1) return true;
                int e2 = 2 * err;
                if (e2 > -dy && e2 < dx)
                {
                    // Diagonal step: both side cells must be open as well.
                    if (!Walkable(x0 + sx, y0) || !Walkable(x0, y0 + sy)) return false;
                }
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Drops the corners a straight line can skip (string pulling).</summary>
        public List<(int x, int y)> Straighten(List<(int x, int y)> cells)
        {
            if (cells == null || cells.Count < 3) return cells;
            var result = new List<(int, int)> { cells[0] };
            int anchor = 0;
            for (int i = 2; i < cells.Count; i++)
            {
                if (ClearLine(cells[anchor], cells[i])) continue;
                result.Add(cells[i - 1]);
                anchor = i - 1;
            }
            result.Add(cells[cells.Count - 1]);
            return result;
        }

        public bool IsRoad(int x, int y) => Inside(x, y) && cost[y * width + x] < 0.9f;

        /// <summary>A tiny binary heap of (cell, priority).</summary>
        sealed class MinHeap
        {
            readonly List<(int item, float p)> data = new List<(int, float)>();
            public int Count => data.Count;
            public void Clear() => data.Clear();

            public void Push(int item, float p)
            {
                data.Add((item, p));
                int i = data.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) / 2;
                    if (data[up].p <= p) break;
                    data[i] = data[up];
                    i = up;
                }
                data[i] = (item, p);
            }

            public int Pop(out float p)
            {
                var top = data[0];
                var last = data[data.Count - 1];
                data.RemoveAt(data.Count - 1);
                if (data.Count > 0)
                {
                    int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        float mp = last.p;
                        if (l < data.Count && data[l].p < mp) { m = l; mp = data[l].p; }
                        if (r < data.Count && data[r].p < mp) m = r;
                        if (m == i) break;
                        data[i] = data[m];
                        i = m;
                    }
                    data[i] = last;
                }
                p = top.p;
                return top.item;
            }
        }
    }
}
