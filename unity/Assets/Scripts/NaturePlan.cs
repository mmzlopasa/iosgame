using System;
using System.Collections.Generic;
using Founders.Core;

namespace Founders
{
    /// <summary>
    /// Where every tree, bush, rock and flower of the valley stands, worked out from the map the way
    /// nature grows (as in Foundation), not one random thing per cell:
    /// <list type="bullet">
    /// <item>Woods are a closed roof of crowns: mostly broadleaf, birch in groves and by the water, firs
    /// towards the mountains; tall and old inside, lower and thinner towards the edge, with an
    /// undergrowth of ferns, fallen wood and a glade here and there.</item>
    /// <item>Where wood meets meadow there is a belt of bushes and young trees; out in the meadow stand a few
    /// big lone trees and small groves, patches of flowers with ferns, and a few stones near the woods.
    /// Most of the meadow stays open grass (the grass itself is drawn by <see cref="GrassField"/>).</item>
    /// <item>Stony ground is one outcrop: big cliff stones stacked together in its heart, boulders around
    /// them and scree towards its edge, never stones scattered evenly over it.</item>
    /// <item>Beaches have driftwood and pebbles, reeds stand in clumps where the water is quiet.</item>
    /// </list>
    /// Pure C# (no Unity types) so the plan can be checked and previewed outside the game. Positions are
    /// metres on the ground: cell (x, y) is centred at (2x, −2y); heights come from <c>groundY</c>.
    /// </summary>
    public sealed class NaturePlan
    {
        public struct Item
        {
            /// <summary>A group of <see cref="NatureKit.Groups"/>; <see cref="Variant"/> picks one of its first models.</summary>
            public string Group;
            public int Variant;
            public float X, Z;
            /// <summary>Floating on the water (lilies) instead of standing on the ground.</summary>
            public bool OnWater;
            /// <summary>Metres above the ground (berries in their bush).</summary>
            public float Lift;
            /// <summary>Height in metres, or width when <see cref="ByWidth"/>.</summary>
            public float Size;
            public bool ByWidth;
            public float Yaw, LeanX, LeanZ;
            /// <summary>Sunk this share of its height into the ground.</summary>
            public float Sink;
            /// <summary>The cell it belongs to (gathering, clearing and building count by cell).</summary>
            public int Cx, Cy;
            public string Id;
            public bool Tree, Shadows;
            /// <summary>Recolouring: null, "iron", "copper", "berry" or "stone" (the Starter pack's pale palette stone made slate grey).</summary>
            public string Tint;
        }

        public const float Cell = 2f;
        public readonly List<Item> Items = new List<Item>();

        readonly int width, height, margin;
        readonly Func<int, int, TerrainType> type;
        readonly Func<int, int, bool> berries;
        readonly Func<float, float, float> groundY;
        int[,] forestDepth, toForest, toWater, toMountain, toRock, rockDepth, rockId;
        readonly List<(int x, int y, int size, int depth)> outcrops = new List<(int, int, int, int)>();

        NaturePlan(int width, int height, int margin, Func<int, int, TerrainType> type, Func<int, int, bool> berries, Func<float, float, float> groundY)
        {
            this.width = width;
            this.height = height;
            this.margin = margin;
            this.type = type;
            this.berries = berries ?? ((x, y) => false);
            this.groundY = groundY ?? ((x, z) => 0f);
        }

        /// <summary>The whole valley: the map and <paramref name="margin"/> cells of land around it.</summary>
        public static NaturePlan Make(int width, int height, int margin, Func<int, int, TerrainType> type,
                                      Func<int, int, bool> berries = null, Func<float, float, float> groundY = null)
        {
            var plan = new NaturePlan(width, height, margin, type, berries, groundY);
            plan.Measure();
            for (int cy = -margin; cy < height + margin; cy++)
                for (int cx = -margin; cx < width + margin; cx++)
                    plan.Populate(cx, cy);
            return plan;
        }

        // MARK: - The lie of the land

        int W => width + 2 * margin;
        int H => height + 2 * margin;
        TerrainType T(int cx, int cy) => type(cx, cy);
        bool In(int cx, int cy) => cx >= -margin && cy >= -margin && cx < width + margin && cy < height + margin;
        int Get(int[,] a, int cx, int cy) => In(cx, cy) ? a[cy + margin, cx + margin] : 99;

        /// <summary>Distance in cells (8 neighbours) from every cell to the nearest one where <paramref name="from"/> holds, capped.</summary>
        int[,] Distance(Func<int, int, bool> from, int cap)
        {
            var d = new int[H, W];
            var queue = new Queue<(int, int)>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (from(x - margin, y - margin)) { d[y, x] = 0; queue.Enqueue((x, y)); }
                    else d[y, x] = cap;
                }
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                int next = d[y, x] + 1;
                if (next >= cap) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H || d[ny, nx] <= next) continue;
                        d[ny, nx] = next;
                        queue.Enqueue((nx, ny));
                    }
            }
            return d;
        }

        int[,] land;
        int[,] toLand() => land ?? (land = Distance((x, y) => T(x, y) != TerrainType.Water, 8));

        void Measure()
        {
            forestDepth = Distance((x, y) => T(x, y) != TerrainType.Forest, 9);
            toForest = Distance((x, y) => T(x, y) == TerrainType.Forest, 12);
            toWater = Distance((x, y) => T(x, y) == TerrainType.Water, 8);
            toMountain = Distance((x, y) => T(x, y) == TerrainType.Mountain || T(x, y) == TerrainType.Ore, 10);
            toRock = Distance((x, y) => T(x, y) == TerrainType.Rock, 6);
            rockDepth = Distance((x, y) => T(x, y) != TerrainType.Rock, 12);
            // Each stony patch is one outcrop with its heart where it is deepest.
            rockId = new int[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (rockId[y, x] != 0 || T(x - margin, y - margin) != TerrainType.Rock) continue;
                    int id = outcrops.Count + 1, size = 0, bx = x, by = y, best = -1;
                    var queue = new Queue<(int, int)>();
                    queue.Enqueue((x, y));
                    rockId[y, x] = id;
                    while (queue.Count > 0)
                    {
                        var (px, py) = queue.Dequeue();
                        size++;
                        // The deepest cell, nearest the middle of the patch on ties.
                        int depth = rockDepth[py, px];
                        if (depth > best) { best = depth; bx = px; by = py; }
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = px + dx, ny = py + dy;
                                if (nx < 0 || ny < 0 || nx >= W || ny >= H || rockId[ny, nx] != 0) continue;
                                if (T(nx - margin, ny - margin) != TerrainType.Rock) continue;
                                rockId[ny, nx] = id;
                                queue.Enqueue((nx, ny));
                            }
                    }
                    outcrops.Add((bx - margin, by - margin, size, best));
                }
        }

        // MARK: - Noise (smooth fields: neighbouring cells get alike values, so things come in patches)

        static readonly int[] Perm = MakePerm();

        static int[] MakePerm()
        {
            var p = new int[512];
            var r = new Random(1234);
            var a = new int[256];
            for (int i = 0; i < 256; i++) a[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
            for (int i = 0; i < 512; i++) p[i] = a[i & 255];
            return p;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        /// <summary>Perlin noise in about 0..1.</summary>
        public static float Noise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = Perm[Perm[xi] + yi], ab = Perm[Perm[xi] + yi + 1], ba = Perm[Perm[xi + 1] + yi], bb = Perm[Perm[xi + 1] + yi + 1];
            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Math.Max(0f, Math.Min(1f, Lerp(x1, x2, v) * 0.7f + 0.5f));
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // MARK: - One cell

        int cx, cy;
        float ccx, ccz;
        Random rng;
        readonly List<(float x, float z)> taken = new List<(float, float)>(6);

        float R01() => (float)rng.NextDouble();
        float Range(float a, float b) => a + (b - a) * R01();
        bool Chance(float p) => rng.NextDouble() < p;
        float Field(float scale, float seed) => Noise(cx * scale + seed, cy * scale + seed * 1.37f);

        (float x, float z) Anywhere(float spread = 0.95f) => (ccx + (R01() * 2f - 1f) * spread, ccz + (R01() * 2f - 1f) * spread);

        /// <summary>A spot in the cell at least this far from what already stands in it.</summary>
        (float x, float z) Spaced(float apart)
        {
            var p = Anywhere();
            for (int i = 0; i < 8 && taken.Exists(q => Sq(q.x - p.x, q.z - p.z) < apart * apart); i++) p = Anywhere();
            taken.Add(p);
            return p;
        }

        static float Sq(float x, float z) => x * x + z * z;

        /// <summary>Something small lying beside a bigger thing (stones by a boulder, a mushroom by a log).</summary>
        (float x, float z) Beside((float x, float z) p, float min, float max, bool stayInCell = true)
        {
            double a = R01() * Math.PI * 2;
            float d = Range(min, max);
            float x = p.x + (float)Math.Cos(a) * d, z = p.z + (float)Math.Sin(a) * d;
            if (stayInCell)
            {
                x = Math.Max(ccx - 0.98f, Math.Min(ccx + 0.98f, x));
                z = Math.Max(ccz - 0.98f, Math.Min(ccz + 0.98f, z));
            }
            return (x, z);
        }

        /// <summary>Towards the open side of a wood's edge cell (bushes grow in the light).</summary>
        (float x, float z) TowardsOpen(int[,] depthTowardsOpen)
        {
            float sx = 0f, sz = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Get(depthTowardsOpen, cx + dx, cy + dy) == 0) { sx += dx; sz -= dy; }
            float len = (float)Math.Sqrt(sx * sx + sz * sz);
            if (len < 0.01f) return Anywhere();
            return (ccx + sx / len * Range(0.4f, 0.95f) + (R01() - 0.5f) * 0.8f, ccz + sz / len * Range(0.4f, 0.95f) + (R01() - 0.5f) * 0.8f);
        }

        float Yaw() => R01() * 360f;

        void Add(string group, (float x, float z) p, float size, bool byWidth = false, string id = null, bool shadows = true,
                 string tint = null, float sink = 0f, int? variant = null, float lean = 0f, int? cellX = null, int? cellY = null)
        {
            Items.Add(new Item
            {
                Group = group, Variant = variant ?? rng.Next(3), X = p.x, Z = p.z, Size = size, ByWidth = byWidth,
                Yaw = Yaw(), LeanX = (R01() - 0.5f) * 2f * lean, LeanZ = (R01() - 0.5f) * 2f * lean, Sink = sink,
                Cx = cellX ?? cx, Cy = cellY ?? cy, Id = id ?? "kit_" + group, Tree = false, Shadows = shadows, Tint = tint,
            });
        }

        void AddTree(string group, (float x, float z) p, float size, int? variant = null)
        {
            Items.Add(new Item
            {
                Group = group, Variant = variant ?? rng.Next(3), X = p.x, Z = p.z, Size = size, Yaw = Yaw(),
                Cx = cx, Cy = cy, Id = "kit_tree", Tree = true, Shadows = true,
            });
        }

        /// <summary>A stone half sunk into the ground and leaning a little, as stones lie outdoors.</summary>
        void Stone((float x, float z) p, float width, string tint = null, string id = "kit_rock", string group = "boulder")
            => Add(group, p, width, true, id, true, tint ?? "stone", Range(0.28f, 0.4f), lean: 12f);

        void Populate(int x, int y)
        {
            cx = x;
            cy = y;
            ccx = x * Cell;
            ccz = -y * Cell;
            rng = new Random(x * 73856093 ^ y * 19349663 ^ 0x5bd1e995);
            taken.Clear();
            bool inside = x >= 0 && y >= 0 && x < width && y < height;
            switch (T(x, y))
            {
                case TerrainType.Forest: Forest(inside); break;
                case TerrainType.Grass: Meadow(inside); break;
                case TerrainType.Rock: Rocks(inside); break;
                case TerrainType.Mountain: Mountain(inside); break;
                case TerrainType.Ore: Ore(); break;
                case TerrainType.Sand: Beach(inside); break;
                case TerrainType.Water: Water(inside); break;
            }
        }

        // MARK: - Trees

        /// <summary>The kind of tree that grows here: broadleaf woods, birch groves and by the water, firs towards the mountains.</summary>
        string Species()
        {
            if (Get(toMountain, cx, cy) <= 4 || Field(0.045f, 2.3f) > 0.74f) return Chance(0.12f) ? "birch" : "fir";
            if (Field(0.08f, 3.7f) > 0.66f || (Get(toWater, cx, cy) <= 2 && Chance(0.5f))) return Chance(0.15f) ? "broadleaf" : "birch";
            if (Chance(0.06f)) return Chance(0.5f) ? "birch" : "fir";
            return "broadleaf";
        }

        /// <summary>How tall the wood's roof is here (it rises and falls gently across a wood).</summary>
        float Canopy(string species)
        {
            float roof = 8.5f + 4f * Field(0.06f, 1.1f);
            return species == "fir" ? roof * 1.25f : species == "birch" ? roof * 1.05f : roof;
        }

        void Forest(bool inside)
        {
            int depth = Get(forestDepth, cx, cy);
            bool glade = depth >= 3 && Field(0.11f, 4.3f) > 0.76f;
            // The valley walls beyond the map: trees only, a little thinner.
            float thin = inside ? 1f : 0.45f;
            float p = glade ? 0.18f : depth == 1 ? 0.6f : depth == 2 ? 0.82f : 0.94f;
            float age = depth == 1 ? Range(0.62f, 0.8f) : depth == 2 ? Range(0.8f, 0.95f) : Range(0.9f, 1.12f);
            if (Chance(p * thin))
            {
                var spot = Spaced(1.2f);
                var species = Species();
                if (depth >= 3 && Chance(1f / 70f)) AddTree("dead_tree", spot, Range(6f, 9f));
                else if (depth == 1 && Chance(0.25f))
                {
                    bool fir = species == "fir";
                    AddTree(fir ? "fir_young" : species, spot, fir ? Range(2.5f, 4f) : Range(4.5f, 6f));
                }
                else AddTree(species, spot, Canopy(species) * age);
            }
            // A lower tree under the roof now and then: the wood has layers, not one height.
            if (depth >= 3 && !glade && Chance(0.18f * thin))
            {
                var species = Species();
                AddTree(species, Spaced(1.2f), Canopy(species) * Range(0.55f, 0.7f));
            }
            if (!inside) return;

            if (depth == 1)
            {
                // The edge of the wood: bushes in the light on its open side.
                if (Chance(0.35f)) Add(Chance(0.2f) ? "bush_flower" : "bush", TowardsOpen(forestDepth), Range(1.0f, 1.6f));
                if (Chance(0.15f)) Add("fern", Anywhere(), Range(0.5f, 0.8f), shadows: false);
                return;
            }
            if (glade)
            {
                // A glade: ferns and tall grass in the sun, sometimes flowers.
                if (Chance(0.5f)) Add("fern", Anywhere(), Range(0.5f, 0.9f), shadows: false);
                if (Chance(0.35f)) Add("grass_tall", Anywhere(), Range(0.6f, 0.9f), shadows: false);
                if (Chance(0.12f)) Add("flowers", Anywhere(), Range(0.3f, 0.45f), shadows: false, variant: FlowerKind());
                return;
            }
            // The floor of the wood: ferns in the shade, fallen wood with mushrooms on its damp side.
            if (Chance(0.3f))
            {
                var f = Anywhere();
                Add("fern", f, Range(0.5f, 0.9f), shadows: false);
                if (Chance(0.4f)) Add("fern", Beside(f, 0.4f, 0.8f), Range(0.4f, 0.7f), shadows: false);
            }
            if (Chance(0.06f)) Add("bush", Anywhere(), Range(0.9f, 1.3f));
            // Mossy boulders in the woods, a few together where the ground turns stony.
            if (Field(0.1f, 13.7f) > 0.78f && Chance(0.3f))
            {
                var s = Anywhere(0.6f);
                Stone(s, Range(1.1f, 2.3f), id: "kit_stone");
                if (Chance(0.5f)) Stone(Beside(s, 0.6f, 1.0f), Range(0.5f, 1.0f), id: "kit_stone");
                if (Chance(0.4f)) Add("fern", Beside(s, 0.4f, 0.9f), Range(0.5f, 0.8f), shadows: false);
            }
            else if (Chance(1f / 45f)) Stone(Anywhere(0.6f), Range(0.7f, 1.4f), id: "kit_stone");
            if (Chance(1f / 40f))
            {
                var w = Anywhere();
                Add("log", w, Range(2.2f, 3.4f), byWidth: true);
                if (Chance(0.5f)) Add("mushroom", Beside(w, 0.4f, 0.9f), Range(0.15f, 0.25f), shadows: false);
            }
            else if (Chance(1f / 50f)) Add("stump", Anywhere(), Range(0.5f, 0.8f));
            else if (Chance(1f / 12f)) Add("branch", Anywhere(), Range(0.8f, 1.4f), byWidth: true, shadows: false);
            else if (Chance(1f / 30f)) Add("mushroom", Anywhere(), Range(0.15f, 0.25f), shadows: false);
        }

        // MARK: - Meadows

        /// <summary>One kind of flower per patch, the same across neighbouring patches.</summary>
        int FlowerKind() => (int)(Field(0.04f, 71f) * 2.99f);

        void Meadow(bool inside)
        {
            if (inside && berries(cx, cy))
            {
                // Berry bushes the foragers pick (the same cells the game counts as food).
                var p = Anywhere(0.45f);
                Add("bush", p, Range(1.0f, 1.3f), id: "kit_berry_bush");
                for (int i = 0; i < 9; i++)
                {
                    var b = (p.x + (R01() - 0.5f) * 0.9f, p.z + (R01() - 0.5f) * 0.9f);
                    Items.Add(new Item
                    {
                        Group = "pebble", Variant = rng.Next(3), X = b.Item1, Z = b.Item2, Size = 0.07f, ByWidth = true,
                        Yaw = Yaw(), Cx = cx, Cy = cy, Id = "kit_berry", Shadows = false, Tint = "berry", Lift = Range(0.45f, 0.85f),
                    });
                }
                return;
            }
            int woods = Get(toForest, cx, cy);
            int water = Get(toWater, cx, cy);
            int rock = Get(toRock, cx, cy);
            if (!inside)
            {
                // Beyond the map: a few big trees and bushes, so the land around reads as countryside.
                if (Chance(woods <= 2 ? 0.25f : 0.05f)) AddTree(Species(), Anywhere(), Range(8f, 11f));
                return;
            }
            if (woods == 1)
            {
                // The belt where wood meets meadow: bushes and young trees spreading out into the light.
                float r = R01();
                if (r < 0.38f) Add(Chance(0.25f) ? "bush_flower" : "bush", Anywhere(), Range(0.9f, 1.5f));
                else if (r < 0.5f) { var s = Species(); AddTree(s == "fir" ? "fir_young" : s, Anywhere(), s == "fir" ? Range(2.5f, 4f) : Range(5f, 7.5f)); }
                else if (r < 0.62f) Add(Chance(0.5f) ? "fern" : "grass_tall", Anywhere(), Range(0.5f, 0.9f), shadows: false);
                return;
            }
            if (woods == 2)
            {
                float r = R01();
                if (r < 0.12f) Add("bush", Anywhere(), Range(0.8f, 1.3f));
                else if (r < 0.17f) AddTree(Species(), Anywhere(), Range(6.5f, 9f));
                else if (r < 0.24f) Add("grass_tall", Anywhere(), Range(0.5f, 0.8f), shadows: false);
                return;
            }
            // Damp ground by the water: tall grass and reeds.
            if (water <= 1 && Chance(0.3f))
            {
                Add(Chance(0.35f) ? "reeds" : "grass_tall", Anywhere(), Range(0.7f, 1.1f), shadows: false);
                return;
            }
            // Stones rolled down from the outcrops into the grass beside them.
            if (rock <= 2 && Chance(rock == 1 ? 0.3f : 0.12f))
            {
                var s = Anywhere();
                Add("pebble", s, Range(0.3f, 0.55f), byWidth: true, shadows: false, sink: 0.15f);
                if (Chance(0.3f)) Stone(Beside(s, 0.3f, 0.8f), Range(0.5f, 0.9f), id: "kit_stone");
                return;
            }
            // Big lone trees and small groves out in the meadow (they grow wide in the open).
            float grove = Field(0.09f, 5.3f);
            if (grove > 0.74f && Chance(0.3f) || Chance(0.005f))
            {
                var t = Anywhere(0.5f);
                AddTree(Species(), t, Range(9f, 12.5f));
                if (Chance(0.35f)) Add("bush", Beside(t, 0.6f, 0.95f), Range(0.8f, 1.2f));
                return;
            }
            // Patches of flowers with ferns and clover, here and there.
            if (Field(0.13f, 6.1f) > 0.79f && Chance(0.55f))
            {
                var f = Anywhere(0.6f);
                int n = 2 + rng.Next(3);
                int kind = FlowerKind();
                for (int i = 0; i < n; i++) Add("flowers", i == 0 ? f : Beside(f, 0.2f, 0.9f), Range(0.3f, 0.5f), shadows: false, variant: kind);
                if (Chance(0.4f)) Add("fern", Beside(f, 0.3f, 0.9f), Range(0.5f, 0.8f), shadows: false);
                if (Chance(0.15f)) Add("clover", Beside(f, 0.3f, 0.9f), 0.25f, shadows: false);
                return;
            }
            // A few stones lying in the grass not far from the woods.
            if (woods <= 9 && Field(0.17f, 7.9f) > 0.8f && Chance(0.42f))
            {
                // A little field of boulders in the grass, as glaciers left them.
                var s = Anywhere(0.6f);
                Stone(s, Range(0.9f, 1.8f), id: "kit_stone");
                int more = rng.Next(3);
                for (int i = 0; i < more; i++) Stone(Beside(s, 0.6f, 1.1f), Range(0.4f, 0.9f), id: "kit_stone");
                Add("pebble", Beside(s, 0.5f, 0.9f), Range(0.25f, 0.45f), byWidth: true, shadows: false, sink: 0.15f);
                if (Chance(0.35f)) Add("grass_tall", Beside(s, 0.4f, 0.8f), Range(0.5f, 0.8f), shadows: false);
                return;
            }
            if (Field(0.1f, 10.1f) > 0.6f && Chance(0.05f)) Add("clover", Anywhere(), 0.25f, shadows: false);
        }

        // MARK: - Stone

        void Rocks(bool inside)
        {
            int id = rockId[cy + margin, cx + margin];
            var o = outcrops[id - 1];
            int depth = Get(rockDepth, cx, cy);
            if (o.x == cx && o.y == cy) Outcrop(o.size, o.depth);
            else if (depth >= 2)
            {
                // Inside the outcrop: boulders and broken stone everywhere, the quarry's riches in sight.
                if (Chance(0.45f)) Stone(Anywhere(0.7f), Range(1.2f, 2.4f));
                if (Chance(0.25f)) Stone(Anywhere(0.7f), Range(0.6f, 1.1f));
                if (Chance(0.6f)) Add("pebble", Anywhere(), Range(0.3f, 0.6f), byWidth: true, shadows: false, sink: 0.12f);
                if (Chance(0.3f)) Add("pebble", Anywhere(), Range(0.25f, 0.45f), byWidth: true, shadows: false, sink: 0.12f);
                if (Chance(0.15f)) Add("grass", Anywhere(), Range(0.3f, 0.5f), shadows: false);
            }
            else
            {
                // The edge: scree, a bush and grass between the stones, now and then a hardy tree.
                if (Chance(0.3f)) Stone(Anywhere(), Range(0.6f, 1.3f));
                if (Chance(0.5f)) Add("pebble", Anywhere(), Range(0.3f, 0.55f), byWidth: true, shadows: false, sink: 0.12f);
                if (Chance(0.3f)) Add("grass", Anywhere(), Range(0.3f, 0.5f), shadows: false);
                if (Chance(0.08f)) Add("bush", Anywhere(), Range(0.8f, 1.2f));
                if (Chance(0.05f)) AddTree(Chance(0.5f) ? "birch" : "fir", Anywhere(), Range(5f, 8f));
            }
        }

        /// <summary>
        /// The heart of an outcrop: big cliff stones leaning together (a crag, as in Foundation), boulders
        /// at their foot. Each stone belongs to the cell it stands in, so quarrying takes it apart.
        /// </summary>
        void Outcrop(int size, int depth)
        {
            float radius = (float)Math.Sqrt(size / Math.PI) * Cell; // metres
            // Crags: tall stones standing together in the middle, the biggest first.
            int crags = Math.Max(1, Math.Min(5, (int)Math.Round(size / 30f)));
            float tall = Math.Max(3f, Math.Min(8f, radius * 0.6f));
            for (int i = 0; i < crags; i++)
            {
                double a = i * Math.PI * 2 / crags + R01() * 0.8;
                float d = i == 0 ? 0f : Range(0.2f, 0.4f) * radius;
                Piece("crag", a, d, tall * (i == 0 ? 1f : Range(0.55f, 0.8f)), false, Range(0.12f, 0.22f), 5f);
            }
            // Flat mossy slabs around their foot, then boulders and scree towards the edge.
            int slabs = 1 + crags;
            for (int i = 0; i < slabs; i++)
                Piece("slab", R01() * Math.PI * 2, Range(0.35f, 0.6f) * radius, Math.Max(2.5f, Math.Min(7f, radius * Range(0.4f, 0.6f))), true, Range(0.25f, 0.4f), 8f);
            int boulders = 2 + crags * 2;
            for (int i = 0; i < boulders; i++)
                Piece("boulder", R01() * Math.PI * 2, Range(0.5f, 0.85f) * radius, Range(1.2f, 2.6f), true, Range(0.28f, 0.4f), 12f);
        }

        /// <summary>One stone of an outcrop, belonging to the stony cell it stands in.</summary>
        void Piece(string group, double angle, float distance, float size, bool byWidth, float sink, float lean)
        {
            var p = (ccx + (float)Math.Cos(angle) * distance, ccz + (float)Math.Sin(angle) * distance);
            var (px, py) = CellOf(p);
            if (T(px, py) != TerrainType.Rock) { px = cx; py = cy; }
            Add(group, p, size, byWidth, "kit_rock", true, "stone", sink, lean: lean, cellX: px, cellY: py);
        }

        static (int x, int y) CellOf((float x, float z) p) => ((int)Math.Round(p.x / Cell), (int)Math.Round(-p.z / Cell));

        void Mountain(bool inside)
        {
            var p = Anywhere();
            float y = groundY(p.x, p.z);
            // Firs climb the lower slopes in groups; crags on the valley walls beyond the map; stones in the scree.
            if (y < 7f && Field(0.15f, 55f) > 0.45f && Chance(0.5f)) AddTree("fir", p, Range(7f, 10f) * Lerp(1f, 0.75f, Math.Max(0f, y) / 7f));
            else if (!inside && Field(0.12f, 61f) > 0.55f && Chance(0.5f)) Add("cliff", p, Range(4f, 7f), byWidth: true, id: "kit_cliff", sink: 0.4f);
            else if (Chance(0.4f)) Stone(p, Range(1.2f, 2.8f));
        }

        void Ore()
        {
            // Veins of rusty iron and green copper stone at the mountain foot, broken stone around them.
            // A deposit reads from afar (as on Foundation's maps): big veined boulders, now and then a
            // rusty or green crag standing among them, the ground strewn with broken ore.
            string tint = Field(0.25f, 40f) < 0.35f ? "copper" : "iron";
            var b = Anywhere(0.6f);
            float big = Range(1.2f, 2.1f);
            Stone(b, big, tint);
            if (Chance(0.5f)) Stone(Beside(b, big * 0.6f, big * 1.0f), Range(0.6f, 1.1f), tint);
            if (Chance(0.25f)) Add("crag", Beside(b, 0.3f, 0.8f), Range(1.6f, 2.8f), id: "kit_rock", tint: tint, sink: Range(0.15f, 0.25f), lean: 6f);
            int bits = 3 + rng.Next(3);
            for (int i = 0; i < bits; i++) Add("pebble", Beside(b, big * 0.5f, big * 1.2f), Range(0.3f, 0.6f), byWidth: true, shadows: false, tint: tint, sink: 0.1f);
            if (Chance(0.4f)) Add("pebble", Anywhere(), Range(0.3f, 0.5f), byWidth: true, shadows: false, sink: 0.1f);
        }

        // MARK: - Water's edge

        void Beach(bool inside)
        {
            int water = Get(toWater, cx, cy);
            if (water <= 1)
            {
                // Reeds in clumps where the bank meets the water, driftwood washed up.
                if (Field(0.2f, 77f) > 0.5f && Chance(0.6f))
                {
                    var r = Anywhere();
                    int n = 1 + rng.Next(3);
                    for (int i = 0; i < n; i++) Add("reeds", i == 0 ? r : Beside(r, 0.3f, 0.7f), Range(1.0f, 1.6f), shadows: false);
                }
                if (Chance(0.05f)) Add(Chance(0.3f) ? "log" : "branch", Anywhere(), Range(1.2f, 2.6f), byWidth: true, shadows: false);
                if (Chance(0.12f)) Add("pebble", Anywhere(), Range(0.25f, 0.5f), byWidth: true, shadows: false, sink: 0.1f);
                return;
            }
            // Dry sand: driftwood, pebbles, a tuft of grass near the meadow.
            if (Chance(0.04f)) Add("branch", Anywhere(), Range(1.2f, 2.2f), byWidth: true, shadows: false);
            if (Chance(0.08f)) Add("pebble", Anywhere(), Range(0.25f, 0.45f), byWidth: true, shadows: false, sink: 0.1f);
            if (Get(toForest, cx, cy) <= 3 && Chance(0.1f)) Add("bush", Anywhere(), Range(0.8f, 1.2f));
            else if (Chance(0.08f)) Add("grass", Anywhere(), Range(0.3f, 0.5f), shadows: false);
        }

        void Water(bool inside)
        {
            // Water lilies in pads of a few in the quiet water of the lake (not in the running stream).
            int open = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (T(cx + dx, cy + dy) == TerrainType.Water) open++;
            // Only in sheltered water near the shore, in a few beds.
            if (!inside || open < 9 || Get(toLand(), cx, cy) > 4 || Field(0.18f, 91f) < 0.68f || !Chance(0.25f)) return;
            float x0 = ccx + (R01() - 0.5f) * 1.2f, z0 = ccz + (R01() - 0.5f) * 1.2f;
            int n = 1 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var p = i == 0 ? (x0, z0) : (x0 + (R01() - 0.5f) * 1.1f, z0 + (R01() - 0.5f) * 1.1f);
                Add("lily", p, Range(0.6f, 1.0f), byWidth: true, shadows: false);
                var item = Items[Items.Count - 1];
                item.OnWater = true;
                Items[Items.Count - 1] = item;
            }
        }
    }
}
