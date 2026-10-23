using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Founders.Core
{
    /// <summary>
    /// What the player asked of a generated valley (Foundation's map generator): how much of each
    /// feature, from <see cref="Min"/> to <see cref="Max"/>, and the seed that draws the rest. The whole
    /// choice fits in a short code ("3204213K9TQZ7") that rebuilds exactly the same valley anywhere.
    /// </summary>
    public sealed class MapSettings
    {
        public const int Min = 0, Max = 5;
        public const string NamePrefix = "gen_";
        /// <summary>Base-36 digits of the seed.</summary>
        public const int SeedDigits = 6;
        public static readonly int SeedRange = (int)Math.Pow(36, SeedDigits) - 1;

        public enum Feature { Lake, River, Island, Mountain, Hill, Forest, Ore }
        public static readonly Feature[] Features = (Feature[])Enum.GetValues(typeof(Feature));

        readonly int[] levels = new int[Features.Length];
        public int Seed;

        public int this[Feature f]
        {
            get => levels[(int)f];
            set => levels[(int)f] = Math.Max(Min, Math.Min(Max, value));
        }

        public static MapSettings Of(int seed, int lake, int river, int island, int mountain, int hill, int forest, int ore)
        {
            var s = new MapSettings { Seed = Math.Abs(seed) % (SeedRange + 1) };
            s[Feature.Lake] = lake; s[Feature.River] = river; s[Feature.Island] = island; s[Feature.Mountain] = mountain;
            s[Feature.Hill] = hill; s[Feature.Forest] = forest; s[Feature.Ore] = ore;
            return s;
        }

        public MapSettings Clone()
        {
            var c = new MapSettings { Seed = Seed };
            Array.Copy(levels, c.levels, levels.Length);
            return c;
        }

        const string Digits36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>The map code: one digit per feature, then the seed in base 36.</summary>
        public string Code
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var l in levels) sb.Append((char)('0' + l));
                var seed = new char[SeedDigits];
                int v = Seed;
                for (int i = SeedDigits - 1; i >= 0; i--) { seed[i] = Digits36[v % 36]; v /= 36; }
                sb.Append(seed);
                return sb.ToString();
            }
        }

        /// <summary>The map's name in content and saves.</summary>
        public string MapName => NamePrefix + Code;

        public static bool TryParse(string code, out MapSettings settings)
        {
            settings = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            code = code.Trim().ToUpperInvariant();
            if (code.StartsWith(NamePrefix.ToUpperInvariant(), StringComparison.Ordinal)) code = code.Substring(NamePrefix.Length);
            code = code.Replace("-", "").Replace(" ", "");
            if (code.Length != Features.Length + SeedDigits) return false;
            var s = new MapSettings();
            for (int i = 0; i < Features.Length; i++)
            {
                int l = code[i] - '0';
                if (l < Min || l > Max) return false;
                s.levels[i] = l;
            }
            int seed = 0;
            for (int i = Features.Length; i < code.Length; i++)
            {
                int d = Digits36.IndexOf(code[i]);
                if (d < 0) return false;
                seed = seed * 36 + d;
            }
            s.Seed = seed;
            settings = s;
            return true;
        }

        public static bool IsGeneratedName(string mapName) =>
            mapName != null && mapName.StartsWith(NamePrefix, StringComparison.Ordinal);

        public override string ToString() => Code;
    }

    /// <summary>A ready-made valley in the generator's list (Foundation's "original maps").</summary>
    public sealed class MapPreset
    {
        public string Id, Name, Description;
        /// <summary>Null for the hand-drawn valley of content.json.</summary>
        public MapSettings Settings;

        /// <summary>The hand-drawn valley every game had before the generator.</summary>
        public const string ClassicId = "classic";

        public static readonly MapPreset[] All =
        {
            new MapPreset { Id = ClassicId, Name = "Long Valley", Description = "The hand-drawn valley: a river into a great lake, stony hills, iron mountains in the east." },
            new MapPreset { Id = "riverlands", Name = "Riverlands", Description = "Rivers wind through open meadows.", Settings = MapSettings.Of(4127, 1, 5, 0, 1, 2, 2, 2) },
            new MapPreset { Id = "lakeland", Name = "Lakeland", Description = "Wide lakes full of islands.", Settings = MapSettings.Of(90311, 5, 2, 4, 1, 2, 3, 2) },
            new MapPreset { Id = "highlands", Name = "Highlands", Description = "Rolling stony hills, rich in stone.", Settings = MapSettings.Of(55021, 1, 2, 0, 2, 5, 2, 3) },
            new MapPreset { Id = "mountain_pass", Name = "Mountain Pass", Description = "A narrow vale between high peaks, rich in ore.", Settings = MapSettings.Of(707707, 1, 3, 0, 5, 3, 2, 5) },
            new MapPreset { Id = "wildwood", Name = "Wildwood", Description = "Deep forests with small clearings.", Settings = MapSettings.Of(31337, 2, 2, 1, 1, 1, 5, 2) },
            new MapPreset { Id = "open_plains", Name = "Open Plains", Description = "Wide grassland with little wood or stone.", Settings = MapSettings.Of(24680, 1, 2, 0, 1, 1, 1, 1) },
        };

        public static MapPreset Get(string id) => All.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Draws a whole valley (128 × 128 cells of 2 m) from <see cref="MapSettings"/>: mountain ranges along
    /// some edges with ore veins at their feet, stony hills, forests, rivers from edge to edge, lakes
    /// with islands and sandy banks; then cuts it into lands to explore and finds a good spot to settle.
    /// Deterministic: the same settings always give the same valley, on every device.
    /// The terrain, nature and water are then built from the map exactly as for the hand-drawn valley.
    /// </summary>
    public static class MapGenerator
    {
        public const int Size = 128;
        const int Tile = 16;

        // MARK: - Content for a map

        static readonly object cacheLock = new object();
        static readonly List<(GameContent root, string name, GameContent content)> cache = new List<(GameContent, string, GameContent)>();

        /// <summary>The content with a generated valley instead of the hand-drawn one (cached: generating takes a few ms).</summary>
        public static GameContent ContentFor(GameContent root, MapSettings settings)
        {
            string name = settings.MapName;
            lock (cacheLock)
            {
                foreach (var (r, n, c) in cache)
                    if (ReferenceEquals(r, root) && n == name) return c;
            }
            var map = Generate(settings);
            var regions = new List<RegionDefinition>();
            var home = root.Regions.FirstOrDefault(r => r.Home);
            if (home != null) regions.Add(home);
            regions.AddRange(BuildRegions(map, settings.Seed));
            var content = root.WithMap(map, regions);
            lock (cacheLock)
            {
                cache.Add((root, name, content));
                if (cache.Count > 4) cache.RemoveAt(0);
            }
            return content;
        }

        // MARK: - Noise

        static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                h *= 2654435761u;
                return h ^ (h >> 15);
            }
        }

        static double Rand(int x, int y, uint seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216.0;

        static double Smooth(double t) => t * t * (3 - 2 * t);

        /// <summary>Smooth value noise in [0, 1] with features about <paramref name="scale"/> cells apart.</summary>
        static double Noise(double x, double y, double scale, uint seed)
        {
            x /= scale; y /= scale;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = Smooth(x - ix), fy = Smooth(y - iy);
            double a = Rand(ix, iy, seed), b = Rand(ix + 1, iy, seed), c = Rand(ix, iy + 1, seed), d = Rand(ix + 1, iy + 1, seed);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        static double Fbm(double x, double y, double scale, uint seed) =>
            Noise(x, y, scale, seed) * 0.6 + Noise(x, y, scale / 2.3, seed + 101) * 0.28 + Noise(x, y, scale / 5.1, seed + 202) * 0.12;

        /// <summary>A small deterministic random sequence (xorshift).</summary>
        sealed class Rng
        {
            uint s;
            public Rng(uint seed) { s = seed * 2654435761u ^ 0x9E3779B9u; if (s == 0) s = 1; for (int i = 0; i < 4; i++) Next(); }
            public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
            public double Value() => (Next() & 0xFFFFFF) / 16777216.0;
            public double Range(double a, double b) => a + (b - a) * Value();
            public int Int(int n) => (int)(Next() % (uint)Math.Max(1, n));
        }

        // MARK: - The valley

        static readonly int[] MountainEdges = { 0, 1, 1, 2, 2, 3 };
        static readonly double[] MountainDepth = { 0, 7, 10, 13, 16, 20 };
        static readonly double[] HillShare = { 0.025, 0.05, 0.08, 0.11, 0.15, 0.2 };
        static readonly double[] ForestShare = { 0.08, 0.18, 0.28, 0.38, 0.48, 0.58 };
        static readonly double[] VeinThreshold = { 0.8, 0.7, 0.6, 0.52, 0.44, 0.36 };
        static readonly int[] MinOre = { 24, 36, 50, 70, 95, 125 };
        static readonly int[] RiverWidth = { 0, 2, 2, 3, 3, 4 };
        static readonly int[] Tributaries = { 0, 0, 0, 1, 1, 2 };
        static readonly double[][] LakeRadii =
        {
            new double[0], new[] { 5.0 }, new[] { 8.0 }, new[] { 11.0, 5.0 }, new[] { 13.0, 8.0 }, new[] { 16.0, 10.0, 6.0 },
        };
        static readonly int[] IslandCount = { 0, 1, 2, 3, 4, 6 };
        static readonly double[] IslandRadius = { 0, 2.5, 3, 3.4, 3.8, 4.2 };

        sealed class Lake { public double X, Y, Rx, Ry; }

        /// <summary>Draws the valley. Legend as in content.json: '.' grass 'T' forest ',' sand '~' water 'R' rock 'M' mountain 'O' ore.</summary>
        public static MapDefinition Generate(MapSettings s)
        {
            const int W = Size, H = Size;
            uint seed = (uint)s.Seed * 7919u + 17u;
            var rng = new Rng(seed);
            var g = new char[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) g[y, x] = '.';

            // 1. Mountain ranges along some edges (0 north, 1 east, 2 south, 3 west), a massif inland when very high.
            int ml = s[MapSettings.Feature.Mountain];
            var edges = new List<int> { 0, 1, 2, 3 };
            for (int i = edges.Count - 1; i > 0; i--) { int j = rng.Int(i + 1); (edges[i], edges[j]) = (edges[j], edges[i]); }
            edges = edges.Take(MountainEdges[ml]).ToList();
            double depth = MountainDepth[ml];
            Lake massif = null;
            if (ml >= 4)
                massif = new Lake { X = rng.Range(40, 88), Y = rng.Range(40, 88), Rx = ml == 5 ? 9 : 6.5 };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double m = -1;
                    foreach (int e in edges)
                    {
                        double dist = e == 0 ? y : e == 1 ? W - 1 - x : e == 2 ? H - 1 - y : x;
                        double along = e == 0 || e == 2 ? x : y;
                        double reach = depth * (0.55 + 0.9 * Noise(along, e * 50, 14, seed + 11));
                        m = Math.Max(m, (reach - dist) / Math.Max(1, depth));
                    }
                    if (massif != null)
                    {
                        double d = Math.Sqrt((x - massif.X) * (x - massif.X) + (y - massif.Y) * (y - massif.Y));
                        m = Math.Max(m, (massif.Rx * (0.7 + 0.6 * Noise(x, y, 6, seed + 12)) - d) / massif.Rx);
                    }
                    if (m + (Noise(x, y, 7, seed + 13) - 0.5) * 0.5 > 0) g[y, x] = 'M';
                }

            // 2. Ore veins at the mountains' feet.
            int ol = s[MapSettings.Feature.Ore];
            int ore = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (g[y, x] != '.' || !Near(g, x, y, 'M', false)) continue;
                    if (Noise(x, y, 3, seed + 21) > VeinThreshold[ol]) { g[y, x] = 'O'; ore++; }
                }

            // 3. Stony hills: the highest share of a hill noise, more of it near the mountains.
            var hill = new double[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    hill[y, x] = Fbm(x, y, 9, seed + 31);
            MarkShare(g, hill, '.', 'R', HillShare[s[MapSettings.Feature.Hill]]);

            // Not enough ore at the mountains: ore cores in the hills.
            if (ore < MinOre[ol])
            {
                var cores = new List<(double v, int x, int y)>();
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                        if (g[y, x] == 'R') cores.Add((hill[y, x] + Noise(x, y, 2.5, seed + 32) * 0.15, x, y));
                foreach (var c in cores.OrderByDescending(c => c.v))
                {
                    if (ore >= MinOre[ol]) break;
                    g[c.y, c.x] = 'O';
                    ore++;
                }
            }

            // 4. Forests.
            var forest = new double[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    forest[y, x] = Noise(x, y, 11, seed + 41) * 0.7 + Noise(x, y, 4, seed + 42) * 0.3;
            MarkShare(g, forest, '.', 'T', ForestShare[s[MapSettings.Feature.Forest]]);

            // 5. Water: rivers from edge to edge, lakes (on the river when there is one), islands in them.
            var water = new bool[H, W];
            int rl = s[MapSettings.Feature.River];
            var river = new List<(double x, double y)>();
            if (rl > 0)
            {
                // The river runs between the two edges with the fewest mountains.
                var free = new List<int> { 0, 1, 2, 3 }.OrderBy(e => edges.Contains(e) ? 1 : 0).ThenBy(_ => rng.Value()).ToList();
                int from = free[0];
                int to = (from + 2) % 4;
                if (edges.Contains(to) && !edges.Contains((from + 1) % 4)) to = (from + 1) % 4;
                river = Course(EdgePoint(from, rng), EdgePoint(to, rng), rng, seed + 51);
                Stamp(water, river, RiverWidth[rl] * 0.5);
                for (int t = 0; t < Tributaries[rl]; t++)
                {
                    int e = free[Math.Min(free.Count - 1, 1 + t)];
                    var join = river[(int)(river.Count * rng.Range(0.3, 0.7))];
                    var brook = Course(EdgePoint(e, rng), join, rng, seed + 52 + (uint)t);
                    Stamp(water, brook, Math.Max(1, RiverWidth[rl] - 1) * 0.5);
                }
            }
            int ll = s[MapSettings.Feature.Lake], il = s[MapSettings.Feature.Island];
            var radii = LakeRadii[ll].ToList();
            // Islands need a lake big enough to hold them.
            if (il > 0)
            {
                double need = 8 + 2 * il;
                if (radii.Count == 0) radii.Add(need);
                else radii[0] = Math.Max(radii[0], need);
            }
            var lakes = new List<Lake>();
            for (int i = 0; i < radii.Count; i++)
            {
                double r = radii[i];
                var lake = new Lake { Rx = r * rng.Range(1.0, 1.35), Ry = r * rng.Range(0.7, 1.0) };
                if (rng.Value() < 0.5) (lake.Rx, lake.Ry) = (lake.Ry, lake.Rx);
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    if (i == 0 && river.Count > 0 && attempt < 15)
                    {
                        var p = river[(int)(river.Count * rng.Range(0.3, 0.75))];
                        lake.X = p.x; lake.Y = p.y;
                    }
                    else
                    {
                        lake.X = rng.Range(lake.Rx + 6, W - lake.Rx - 6);
                        lake.Y = rng.Range(lake.Ry + 6, H - lake.Ry - 6);
                    }
                    bool inside = lake.X - lake.Rx > 3 && lake.X + lake.Rx < W - 3 && lake.Y - lake.Ry > 3 && lake.Y + lake.Ry < H - 3;
                    bool apart = lakes.All(o => Math.Abs(o.X - lake.X) > o.Rx + lake.Rx + 4 || Math.Abs(o.Y - lake.Y) > o.Ry + lake.Ry + 4);
                    if (inside && apart && MountainShare(g, lake) < 0.25) break;
                }
                lakes.Add(lake);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        double dx = (x - lake.X) / lake.Rx, dy = (y - lake.Y) / lake.Ry;
                        if (dx * dx + dy * dy + (Noise(x, y, 4, seed + 61 + (uint)i) - 0.5) * 0.45 < 1.0) water[y, x] = true;
                    }
            }
            // A spring-fed pond at least, so fishers and mills always have water.
            if (!AnyTrue(water))
            {
                var pond = new Lake { X = rng.Range(30, 98), Y = rng.Range(30, 98), Rx = 4, Ry = 3.2 };
                for (int a = 0; a < 20 && MountainShare(g, pond) > 0; a++) { pond.X = rng.Range(20, 108); pond.Y = rng.Range(20, 108); }
                lakes.Add(pond);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        double dx = (x - pond.X) / pond.Rx, dy = (y - pond.Y) / pond.Ry;
                        if (dx * dx + dy * dy < 1.0) water[y, x] = true;
                    }
            }
            // Islands: round patches of land inside the biggest lakes.
            int islands = IslandCount[il];
            var islandLakes = lakes.OrderByDescending(l => l.Rx * l.Ry).Take(2).ToList();
            for (int i = 0; i < islands && islandLakes.Count > 0; i++)
            {
                var lake = islandLakes[i % islandLakes.Count];
                double r = IslandRadius[il] * rng.Range(0.75, 1.2);
                double ang = rng.Range(0, Math.PI * 2), dist = rng.Range(0, 0.5);
                double cx = lake.X + Math.Cos(ang) * lake.Rx * dist, cy = lake.Y + Math.Sin(ang) * lake.Ry * dist;
                for (int y = (int)(cy - r - 2); y <= cy + r + 2; y++)
                    for (int x = (int)(cx - r - 2); x <= cx + r + 2; x++)
                    {
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d + (Noise(x, y, 2.5, seed + 71) - 0.5) * 0.5 < 1.0) water[y, x] = false;
                    }
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (water[y, x] && g[y, x] != 'M') g[y, x] = '~';
            // Sandy banks.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    char c = g[y, x];
                    if (c == '~' || c == 'M' || c == 'O') continue;
                    if (Near(g, x, y, '~', true) && Rand(x, y, seed + 81) < 0.6) g[y, x] = ',';
                }

            // 6. Somewhere to settle: the best clearing; if the valley has none, one is opened in the middle.
            var start = SuggestStart(g);
            if (start == null)
            {
                for (int y = 58; y < 70; y++)
                    for (int x = 58; x < 70; x++) g[y, x] = '.';
                start = SuggestStart(g) ?? (64, 64);
            }

            var rows = new List<string>(H);
            for (int y = 0; y < H; y++)
            {
                var sb = new StringBuilder(W);
                for (int x = 0; x < W; x++) sb.Append(g[y, x]);
                rows.Add(sb.ToString());
            }
            var (fx, fy) = start.Value;
            var map = new MapDefinition
            {
                Name = s.MapName,
                Title = Title(s),
                Width = W, Height = H, Rows = rows,
                Traits = Traits(g, s, lakes.Count, islands),
                StartBuildings = new List<StartBuilding> { new StartBuilding { BuildingId = "campfire", X = fx, Y = fy } },
            };
            var (sx, sy) = SettlementSpot(g, fx, fy) ?? (fx + 1, fy + 1);
            foreach (var (dx, dy) in Game.SettlementPattern)
                map.StartBuildings.Add(new StartBuilding { BuildingId = "shelter", X = sx + dx, Y = sy + dy });
            return map;
        }

        static bool AnyTrue(bool[,] a)
        {
            foreach (var v in a) if (v) return true;
            return false;
        }

        static bool Near(char[,] g, int x, int y, char c, bool diagonal)
        {
            int h = g.GetLength(0), w = g.GetLength(1);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diagonal && dx != 0 && dy != 0)) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && g[ny, nx] == c) return true;
                }
            return false;
        }

        /// <summary>Turns the <paramref name="share"/> of <paramref name="from"/> cells with the highest field value into <paramref name="to"/>.</summary>
        static void MarkShare(char[,] g, double[,] field, char from, char to, double share)
        {
            var values = new List<double>();
            int h = g.GetLength(0), w = g.GetLength(1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (g[y, x] == from) values.Add(field[y, x]);
            if (values.Count == 0) return;
            values.Sort();
            double threshold = values[Math.Max(0, Math.Min(values.Count - 1, (int)(values.Count * (1 - share))))];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (g[y, x] == from && field[y, x] >= threshold) g[y, x] = to;
        }

        static double MountainShare(char[,] g, Lake l)
        {
            int n = 0, m = 0;
            for (int y = (int)(l.Y - l.Ry); y <= l.Y + l.Ry; y++)
                for (int x = (int)(l.X - l.Rx); x <= l.X + l.Rx; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    n++;
                    if (g[y, x] == 'M') m++;
                }
            return n == 0 ? 1 : m / (double)n;
        }

        /// <summary>A point just outside an edge, away from the corners.</summary>
        static (double x, double y) EdgePoint(int edge, Rng rng)
        {
            double along = rng.Range(Size * 0.2, Size * 0.8);
            switch (edge)
            {
                case 0: return (along, -2);
                case 1: return (Size + 1, along);
                case 2: return (along, Size + 1);
                default: return (-2, along);
            }
        }

        /// <summary>A meandering course from a to b, sampled every half cell.</summary>
        static List<(double x, double y)> Course((double x, double y) a, (double x, double y) b, Rng rng, uint seed)
        {
            var pts = new List<(double, double)>();
            double dx = b.x - a.x, dy = b.y - a.y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double nx = -dy / len, ny = dx / len;
            double amp = rng.Range(5, 11), waves = rng.Range(1.2, 2.6), phase = rng.Range(0, Math.PI * 2);
            int steps = (int)(len * 2);
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                // The bends fade at both ends so the river meets its edge (or the river it joins) where planned.
                double fade = Math.Sin(Math.PI * t);
                double off = fade * (amp * Math.Sin(t * waves * Math.PI * 2 + phase) + (Noise(t * 60, 0, 6, seed) - 0.5) * 14);
                pts.Add((a.x + dx * t + nx * off, a.y + dy * t + ny * off));
            }
            return pts;
        }

        static void Stamp(bool[,] water, List<(double x, double y)> course, double half)
        {
            foreach (var (px, py) in course)
                for (int y = (int)Math.Floor(py - half - 1); y <= py + half + 1; y++)
                    for (int x = (int)Math.Floor(px - half - 1); x <= px + half + 1; x++)
                    {
                        if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                        double cx = x - px, cy = y - py;
                        if (cx * cx + cy * cy <= half * half + 0.25) water[y, x] = true;
                    }
        }

        // MARK: - Where to settle

        static bool Settleable(char[,] g, int x, int y) =>
            x >= 0 && y >= 0 && x < Size && y < Size && g[y, x] != '~' && g[y, x] != 'M';

        static bool Open(char[,] g, int x, int y) => Settleable(g, x, y) && (g[y, x] == '.' || g[y, x] == ',');

        /// <summary>The same rules as <see cref="Game.CampfireIssue"/>: dry land with room for the shelters.</summary>
        static bool CampfireFits(char[,] g, int x, int y)
        {
            if (x < 1 || y < 1 || x >= Size - 1 || y >= Size - 1 || !Open(g, x, y)) return false;
            int room = 0;
            int r = Game.SettlementReach;
            for (int yy = y - r; yy <= y + r; yy++)
                for (int xx = x - r; xx <= x + r; xx++)
                    if ((xx != x || yy != y) && Settleable(g, xx, yy)) room++;
            return room >= 12 && SettlementSpot(g, x, y) != null;
        }

        static (int x, int y)? SettlementSpot(char[,] g, int fx, int fy)
        {
            for (int r = 1; r <= Game.SettlementReach; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        bool ok = true;
                        foreach (var (px, py) in Game.SettlementPattern)
                        {
                            int cx = fx + dx + px, cy = fy + dy + py;
                            if (!Open(g, cx, cy) || (cx == fx && cy == fy) ||
                                Math.Max(Math.Abs(cx - fx), Math.Abs(cy - fy)) > Game.SettlementReach) { ok = false; break; }
                        }
                        if (ok) return (fx + dx, fy + dy);
                    }
            return null;
        }

        /// <summary>
        /// The clearing the game suggests: open meadow with wood, stone and water within reach, not too far
        /// from the middle of the valley.
        /// </summary>
        static (int x, int y)? SuggestStart(char[,] g)
        {
            // Distance (cells, capped) from every cell to the nearest forest, rock and water.
            var dForest = Distance(g, 'T');
            var dRock = Distance(g, 'R');
            var dWater = Distance(g, '~');
            (int, int)? best = null;
            double bestScore = double.MinValue;
            for (int y = 8; y < Size - 8; y += 2)
                for (int x = 8; x < Size - 8; x += 2)
                {
                    if (!Open(g, x, y)) continue;
                    int open = 0;
                    for (int yy = y - 5; yy <= y + 5; yy++)
                        for (int xx = x - 5; xx <= x + 5; xx++)
                            if (Open(g, xx, yy)) open++;
                    if (open < 60) continue;
                    double score = open
                        - Math.Max(0, dForest[y, x] - 4) * 3 - Math.Max(0, dRock[y, x] - 6) * 2 - Math.Max(0, dWater[y, x] - 5) * 2
                        - (Math.Abs(x - Size / 2) + Math.Abs(y - Size / 2)) * 0.35;
                    if (score > bestScore && CampfireFits(g, x, y)) { bestScore = score; best = (x, y); }
                }
            return best;
        }

        /// <summary>City-block distance to the nearest cell of a kind (two passes), capped at 40.</summary>
        static int[,] Distance(char[,] g, char c)
        {
            var d = new int[Size, Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int v = g[y, x] == c ? 0 : 40;
                    if (x > 0) v = Math.Min(v, d[y, x - 1] + 1);
                    if (y > 0) v = Math.Min(v, d[y - 1, x] + 1);
                    d[y, x] = v;
                }
            for (int y = Size - 1; y >= 0; y--)
                for (int x = Size - 1; x >= 0; x--)
                {
                    int v = d[y, x];
                    if (x < Size - 1) v = Math.Min(v, d[y, x + 1] + 1);
                    if (y < Size - 1) v = Math.Min(v, d[y + 1, x] + 1);
                    d[y, x] = v;
                }
            return d;
        }

        // MARK: - Names and traits

        static string Title(MapSettings s)
        {
            int Lv(MapSettings.Feature f) => s[f];
            var feats = new (MapSettings.Feature f, string name)[]
            {
                (MapSettings.Feature.Island, "Isles"), (MapSettings.Feature.Lake, "Lakeland"), (MapSettings.Feature.River, "Riverlands"),
                (MapSettings.Feature.Mountain, "Mountain Vale"), (MapSettings.Feature.Hill, "Highlands"), (MapSettings.Feature.Forest, "Wildwood"),
            };
            var top = feats.OrderByDescending(p => Lv(p.f)).First();
            return Lv(top.f) >= 3 ? top.name : "Open Valley";
        }

        static List<string> Traits(char[,] g, MapSettings s, int lakes, int islands)
        {
            int count(char c) { int n = 0; foreach (var v in g) if (v == c) n++; return n; }
            var t = new List<string>();
            if (s[MapSettings.Feature.River] > 0) t.Add(s[MapSettings.Feature.River] >= 4 ? "winding rivers" : "river valley");
            if (s[MapSettings.Feature.Lake] > 0) t.Add(s[MapSettings.Feature.Lake] >= 4 ? "great lakes" : lakes > 1 ? "lakes" : "a lake");
            else if (count('~') > 0 && s[MapSettings.Feature.River] == 0) t.Add("a quiet pond");
            if (islands > 0) t.Add(islands > 2 ? "islands" : "an island");
            int n = Size * Size;
            if (count('T') > n * 0.35) t.Add("deep forests"); else t.Add("forest edge");
            if (count('R') > n * 0.08) t.Add("hills of stone"); else if (count('R') > 0) t.Add("stony knolls");
            if (count('M') > 0) t.Add(s[MapSettings.Feature.Mountain] >= 4 ? "high peaks" : "iron mountains");
            return t;
        }

        // MARK: - Lands to explore (as tools/map/generate_map.py does for the hand-drawn valley)

        static readonly string[] ForestNames = { "Whispering Pines", "Old Spruce Wood", "Mossy Hollow", "Elk Woods", "Shadow Firs", "Owl Thicket",
            "Deepwood", "Needle Grove", "Wolf Forest", "Fern Dell", "Silent Timber", "Bear Wood", "Lichen Wood", "Pine Ridge Forest", "Hunter's Wood",
            "Darkfir", "Greenmantle", "Bramble Wood", "Antler Grove", "Wild Spruces", "Thornwood", "Crow Forest", "Resin Wood", "Lynx Woods", "Tall Pines" };
        static readonly string[] RockNames = { "Hills of Stone", "Flint Rise", "Grey Knolls", "Boulder Field", "Broken Hills", "Quarry Downs",
            "Rocky Heights", "Stone Teeth", "Windy Crags", "Cairn Hills", "Slate Slopes", "Pebble Heath", "Granite Knoll", "Split Rock", "Echo Rocks", "Tumbled Stones" };
        static readonly string[] MountainNames = { "Iron Mountains", "Copper Peaks", "Eagle Crags", "Frost Heights", "Thunder Peaks", "Red Cliffs",
            "High Scree", "Storm Ridge", "Cold Summits", "Giant's Steps", "Raven Peaks", "Rust Mountains", "Cloud Ridge", "Sky Wall", "Snow Horns",
            "Stone Crown", "Grey Giants", "Ore Hills" };
        static readonly string[] WaterNames = { "Great Lake", "Reed Shallows", "Heron Bay", "Mirror Water", "Fishers' Shore", "Willow Banks",
            "Otter Pools", "Misty Mere", "Pike Waters", "Silver Shallows" };
        static readonly string[] MeadowNames = { "Great Meadow", "Sunny Fields", "Bee Pastures", "Clover Plain", "Golden Grass", "Windy Downs",
            "Lark Fields", "Deer Meadows", "Flower Vale", "Open Steppe", "Quiet Plain", "Hare Fields", "Bluebell Lea", "Green Vale", "Long Meadow",
            "Daisy Fields", "Riverside Lea", "Barley Flats", "Heather Moor", "Wide Plain", "Calm Lea", "Grazing Lands", "Swallow Fields",
            "Butterfly Vale", "Thistle Plain" };

        static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["forest"] = "Thick woods full of timber and game.",
            ["rock"] = "Stony hills: good stone for quarries.",
            ["mountain"] = "Steep peaks; ore shows in the scree at their feet.",
            ["water"] = "Open water and reedy banks, rich in fish.",
            ["meadow"] = "Open grassland: room to farm, herd and build.",
        };

        static ResourceBundle Bundle(params (ResourceType t, double v)[] parts)
        {
            var b = ResourceBundle.Zero;
            foreach (var (t, v) in parts) b[t] = v;
            return b;
        }

        static readonly Dictionary<string, (string text, ResourceBundle loot, int survivors)?[]> Findings =
            new Dictionary<string, (string, ResourceBundle, int)?[]>
            {
                ["forest"] = new (string, ResourceBundle, int)?[]
                {
                    ("In the woods the scouts found a cache of dry firewood.", Bundle((ResourceType.Wood, 40)), 0),
                    ("A hunter lost in the woods followed the scouts home.", ResourceBundle.Zero, 1),
                    ("The scouts carried back a load of fallen timber.", Bundle((ResourceType.Wood, 30)), 0),
                },
                ["rock"] = new (string, ResourceBundle, int)?[]
                {
                    ("The scouts carried back fine flint from the hills.", Bundle((ResourceType.Stone, 30)), 0),
                    ("Among the rocks the scouts found a shelter of old hides.", Bundle((ResourceType.Hides, 10)), 0),
                },
                ["mountain"] = new (string, ResourceBundle, int)?[]
                {
                    ("The mountains hide copper-green rocks and old hides in a cave.", Bundle((ResourceType.Ore, 20), (ResourceType.Hides, 10)), 0),
                    ("Scouts came back with red stones that melt in fire.", Bundle((ResourceType.Ore, 15)), 0),
                },
                ["water"] = new (string, ResourceBundle, int)?[]
                {
                    ("On the shore the scouts found a fisher's abandoned catch.", Bundle((ResourceType.Food, 30)), 0),
                    ("Two people hiding in the reeds joined the village.", ResourceBundle.Zero, 2),
                },
                ["meadow"] = new (string, ResourceBundle, int)?[]
                {
                    ("The scouts gathered wild grain in the meadows.", Bundle((ResourceType.Food, 25)), 0),
                    ("A wanderer crossing the plain joined the village.", ResourceBundle.Zero, 1),
                    null,
                },
            };

        /// <summary>The valley cut into 16 × 16 lands, each named after what covers most of it.</summary>
        public static List<RegionDefinition> BuildRegions(MapDefinition map, int seed)
        {
            var pools = new Dictionary<string, Queue<string>>
            {
                ["forest"] = new Queue<string>(ForestNames), ["rock"] = new Queue<string>(RockNames), ["mountain"] = new Queue<string>(MountainNames),
                ["water"] = new Queue<string>(WaterNames), ["meadow"] = new Queue<string>(MeadowNames),
            };
            var used = new HashSet<string>();
            var rng = new Rng((uint)seed * 31u + 9u);
            var regions = new List<RegionDefinition>();
            for (int ty = 0; ty < map.Height / Tile; ty++)
                for (int tx = 0; tx < map.Width / Tile; tx++)
                {
                    var counts = new Dictionary<char, int>();
                    for (int y = ty * Tile; y < ty * Tile + Tile; y++)
                        for (int x = tx * Tile; x < tx * Tile + Tile; x++)
                        {
                            char c = map.Rows[y][x];
                            counts[c] = counts.TryGetValue(c, out var k) ? k + 1 : 1;
                        }
                    int n = Tile * Tile;
                    int Count(char c) => counts.TryGetValue(c, out var k) ? k : 0;
                    string kind = Count('M') + Count('O') > n * 0.3 ? "mountain"
                        : Count('~') > n * 0.25 ? "water"
                        : Count('R') > n * 0.18 ? "rock"
                        : Count('T') > n * 0.35 ? "forest" : "meadow";
                    var pool = pools[kind];
                    string name = null;
                    while (pool.Count > 0)
                    {
                        var cand = pool.Dequeue();
                        if (!used.Contains(cand)) { name = cand; break; }
                    }
                    if (name == null)
                    {
                        string b = kind == "forest" ? "Woods" : kind == "rock" ? "Hills" : kind == "mountain" ? "Peaks" : kind == "water" ? "Waters" : "Fields";
                        int k = 2;
                        while (used.Contains($"{b} {k}")) k++;
                        name = $"{b} {k}";
                    }
                    used.Add(name);
                    var cost = Bundle((ResourceType.Food, 10 + (kind == "mountain" || kind == "rock" ? 5 : 0)));
                    if (kind == "mountain") cost[ResourceType.Wood] = 10;
                    var region = new RegionDefinition
                    {
                        Id = $"land_{tx}_{ty}", Name = name, Description = Descriptions[kind],
                        X = tx * Tile, Y = ty * Tile, Width = Tile, Height = Tile, RevealedAtStart = false,
                        Cost = cost, ExpeditionSeconds = kind == "mountain" ? 900 : kind == "rock" ? 720 : 600,
                        LegacyText = $"Scouts mapped {name}.", Loot = ResourceBundle.Zero,
                    };
                    var options = Findings[kind];
                    var find = options[rng.Int(options.Length)];
                    if (find.HasValue)
                    {
                        var (text, loot, survivors) = find.Value;
                        region.Finding = text;
                        region.Loot = loot;
                        region.Survivors = survivors;
                    }
                    regions.Add(region);
                }
            return regions;
        }
    }
}
