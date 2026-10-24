using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Builds a Unity Terrain from the content map: a carved river and lake with sandy banks,
    /// stony hills (quarries), mountains with ore veins at their feet, the valley walls beyond
    /// the playable area, and photo-texture layers (grass, forest floor, mud, sand, riverbed,
    /// rock) blended per sample. Roads and trodden ground are painted on top.
    /// World units: metres. Cell (x, y) of the map is centred at (x·S, 0, −y·S).
    /// </summary>
    public sealed class WorldTerrain
    {
        /// <summary>Side of the terrain in metres: the valley plus 64 m of surrounding land on each side.</summary>
        public static float Size { get; private set; } = 384f;
        const float HeightRange = 90f;
        const float BaseDepth = 5f;          // world y = 0 sits this far above the terrain bottom
        /// <summary>Heightmap samples per side (2^n + 1): about 0.4 m apart, so river banks stay round.</summary>
        static int HeightRes = 1025;
        static int SplatRes = 1024;
        public const float WaterLevel = -0.38f;

        static readonly string[] LayerNames = { "Grass", "ForestFloor", "Mud", "Sand", "Riverbed", "RockGround" };
        /// <summary>
        /// The layers the terrain really draws. Mobile URP renders a terrain with more than four
        /// layers in two passes, so forest floor folds into grass and mud, and the riverbed into mud.
        /// </summary>
        static readonly string[] OutputLayers = { "Grass", "Mud", "Sand", "RockGround" };
        static readonly (int to, float amount)[][] Fold =
        {
            new[] { (0, 1f) },             // grass
            new[] { (0, 0.65f), (1, 0.35f) }, // forest floor
            new[] { (1, 1f) },             // mud
            new[] { (2, 1f) },             // sand
            new[] { (1, 0.7f), (2, 0.3f) },  // riverbed
            new[] { (3, 1f) },             // rock
        };
        const int LGrass = 0, LForest = 1, LMud = 2, LSand = 3, LRiver = 4, LRock = 5;

        public Terrain Terrain { get; private set; }
        readonly MapDefinition map;
        readonly Vector3 origin;
        float[,] signedWaterDistance; // metres; negative inside water
        float[,] mountainDepth;       // metres inside the mountains (0 outside)
        float[,] hillDepth;           // metres inside the stony hills (0 outside)
        TerrainType[,] fine;          // terrain type per heightmap sample
        float[,] worldY;              // ground height (world y) per heightmap sample
        float[,,] baseSplat;          // layer weights without roads and trodden ground

        public WorldTerrain(MapDefinition map, Transform parent)
        {
            this.map = map;
            Size = Mathf.Max(map.Width, map.Height) * Grid.CellSize + 128f;
            HeightRes = Size <= 256f ? 513 : 1025;
            SplatRes = HeightRes - 1;
            var center = Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f);
            origin = new Vector3(center.x - Size * 0.5f, -BaseDepth, center.z - Size * 0.5f);

            ClassifySamples();
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes };
            data.size = new Vector3(Size, HeightRange, Size);
            data.SetHeights(0, 0, BuildHeights());
            var layers = new List<TerrainLayer>();
            foreach (var n in OutputLayers)
            {
                var l = NatureKit.Current?.GroundFor(n) ?? Resources.Load<TerrainLayer>("TerrainLayers/" + n);
                if (l == null) l = new TerrainLayer { diffuseTexture = Texture2D.grayTexture, tileSize = new Vector2(5, 5) };
                // The player's own ground (Custom/Textures/Terrain/<Layer>.png, optional <Layer>_normal.png).
                var albedo = CustomAssets.Texture("Textures/Terrain/" + n);
                if (albedo != null)
                {
                    l = new TerrainLayer
                    {
                        name = n + "_Custom", diffuseTexture = albedo, normalMapTexture = CustomAssets.Texture("Textures/Terrain/" + n + "_normal"),
                        tileSize = l.tileSize, normalScale = 1f, smoothness = l.smoothness, metallic = 0f,
                    };
                }
                layers.Add(l);
            }
            data.terrainLayers = layers.ToArray();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            Terrain = go.GetComponent<Terrain>();
            var mat = Resources.Load<Material>(Palette.ResourceFolder + "/TerrainLit");
            if (mat != null) Terrain.materialTemplate = mat;
            Terrain.heightmapPixelError = 4f;
            Terrain.basemapDistance = 300f;
            // Per-instance terrain drawing needs shader variants Unity strips from player builds;
            // the classic path renders with the variants the material keeps.
            Terrain.drawInstanced = false;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            PaintSplat(null, null);

            var shader = Terrain.materialTemplate != null ? Terrain.materialTemplate.shader : null;
            Debug.Log($"[FOUNDERS] terrain: material={(mat != null ? mat.name : "none")} shader={(shader != null ? shader.name : "none")} " +
                      $"supported={(shader != null && shader.isSupported)} layers={string.Join(",", data.terrainLayers.Select(l => l != null && l.diffuseTexture != null ? l.diffuseTexture.name : "null"))} " +
                      $"height(center)={HeightAt(center):0.00} water={WaterLevel}");
        }

        /// <summary>Ground height (world y) at a world position.</summary>
        public float HeightAt(Vector3 world) => Terrain.SampleHeight(world) + origin.y;

        public Vector3 OnGround(Vector3 world) => new Vector3(world.x, HeightAt(world), world.z);

        // MARK: - Map classification

        /// <summary>Terrain type of any grid cell, including the land around the playable map.</summary>
        public TerrainType CellType(int cx, int cy)
        {
            if (cx >= 0 && cy >= 0 && cx < map.Width && cy < map.Height)
                return map.Terrain(cx, cy) ?? TerrainType.Grass;
            // Outside the map the land continues from the nearest edge cell.
            int ex = Mathf.Clamp(cx, 0, map.Width - 1), ey = Mathf.Clamp(cy, 0, map.Height - 1);
            var edge = map.Terrain(ex, ey) ?? TerrainType.Grass;
            // The river keeps flowing beyond the north and south edges.
            if (cx == ex && (edge == TerrainType.Water || edge == TerrainType.Sand)) return edge;
            if (edge == TerrainType.Mountain || edge == TerrainType.Ore) return TerrainType.Mountain;
            float n = Noise(cx, cy, 0.22f);
            if (edge == TerrainType.Rock) return n > 0.5f ? TerrainType.Rock : TerrainType.Forest;
            if (cx < 0) return Noise(cx, cy, 0.25f) > 0.2f ? TerrainType.Forest : TerrainType.Grass;
            return n > 0.3f ? TerrainType.Forest : TerrainType.Grass;
        }

        /// <summary>True for map cells and the land around them that only mountains cover.</summary>
        public bool IsMountain(int cx, int cy) => CellType(cx, cy) == TerrainType.Mountain;

        static float Noise(float x, float y, float f) => Mathf.PerlinNoise(x * f + 31.7f, y * f + 12.3f);

        Vector2 SampleWorld(int i, int j, int res) =>
            new Vector2(origin.x + j * Size / (res - 1), origin.z + i * Size / (res - 1));

        static (int, int) CellOf(Vector2 world) =>
            (Mathf.RoundToInt(world.x / Grid.CellSize), Mathf.RoundToInt(-world.y / Grid.CellSize));

        void ClassifySamples()
        {
            fine = new TerrainType[HeightRes, HeightRes];
            var water = new bool[HeightRes, HeightRes];
            var mountain = new bool[HeightRes, HeightRes];
            var hill = new bool[HeightRes, HeightRes];
            for (int i = 0; i < HeightRes; i++)
            {
                for (int j = 0; j < HeightRes; j++)
                {
                    var (cx, cy) = CellOf(SampleWorld(i, j, HeightRes));
                    var t = CellType(cx, cy);
                    fine[i, j] = t;
                    water[i, j] = t == TerrainType.Water;
                    mountain[i, j] = t == TerrainType.Mountain;
                    hill[i, j] = t == TerrainType.Rock;
                }
            }
            // Signed distance to the river edge (two-pass chamfer), in metres.
            float step = Size / (HeightRes - 1);
            var outside = Chamfer(water, true, step);
            var inside = Chamfer(water, false, step);
            signedWaterDistance = new float[HeightRes, HeightRes];
            for (int i = 0; i < HeightRes; i++)
                for (int j = 0; j < HeightRes; j++)
                    signedWaterDistance[i, j] = water[i, j] ? -inside[i, j] : outside[i, j];
            // The map is made of 2 m cells: round off the stepped shores and let them wander a little.
            signedWaterDistance = Blur(Blur(signedWaterDistance, 2), 2);
            for (int i = 0; i < HeightRes; i++)
                for (int j = 0; j < HeightRes; j++)
                {
                    var w = SampleWorld(i, j, HeightRes);
                    signedWaterDistance[i, j] += (Mathf.PerlinNoise(w.x * 0.21f + 13f, w.y * 0.21f + 7f) - 0.5f) * 0.5f;
                }
            mountainDepth = Chamfer(mountain, false, step);
            hillDepth = Chamfer(hill, false, step);
        }

        /// <summary>Separable box blur with the given radius (in samples).</summary>
        static float[,] Blur(float[,] src, int r)
        {
            int n = src.GetLength(0);
            var tmp = new float[n, n];
            var dst = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = Mathf.Max(0, j - r); k <= Mathf.Min(n - 1, j + r); k++) { sum += src[i, k]; c++; }
                    tmp[i, j] = sum / c;
                }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = Mathf.Max(0, i - r); k <= Mathf.Min(n - 1, i + r); k++) { sum += tmp[k, j]; c++; }
                    dst[i, j] = sum / c;
                }
            return dst;
        }

        /// <summary>Distance from each sample to the nearest sample where mask == target.</summary>
        static float[,] Chamfer(bool[,] mask, bool target, float step)
        {
            int n = mask.GetLength(0);
            var d = new float[n, n];
            const float big = 1e6f;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    d[i, j] = mask[i, j] == target ? 0f : big;
            float a = step, b = step * 1.4142f;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float v = d[i, j];
                    if (i > 0) { v = Mathf.Min(v, d[i - 1, j] + a); if (j > 0) v = Mathf.Min(v, d[i - 1, j - 1] + b); if (j < n - 1) v = Mathf.Min(v, d[i - 1, j + 1] + b); }
                    if (j > 0) v = Mathf.Min(v, d[i, j - 1] + a);
                    d[i, j] = v;
                }
            for (int i = n - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                {
                    float v = d[i, j];
                    if (i < n - 1) { v = Mathf.Min(v, d[i + 1, j] + a); if (j < n - 1) v = Mathf.Min(v, d[i + 1, j + 1] + b); if (j > 0) v = Mathf.Min(v, d[i + 1, j - 1] + b); }
                    if (j < n - 1) v = Mathf.Min(v, d[i, j + 1] + a);
                    d[i, j] = v;
                }
            return d;
        }

        // MARK: - Heights

        float[,] BuildHeights()
        {
            var h = new float[HeightRes, HeightRes];
            worldY = new float[HeightRes, HeightRes];
            var mapMin = Grid.CellCenter(-0.5f, map.Height - 0.5f);
            var mapMax = Grid.CellCenter(map.Width - 0.5f, -0.5f);
            for (int i = 0; i < HeightRes; i++)
            {
                for (int j = 0; j < HeightRes; j++)
                {
                    var w = SampleWorld(i, j, HeightRes);
                    float s = signedWaterDistance[i, j];
                    float y;
                    if (s <= 0f)
                    {
                        y = Mathf.Lerp(-0.45f, -1.5f, Mathf.Clamp01(-s / 2.5f));
                    }
                    else
                    {
                        // A short bank: the beach cells beside the water are level ground, so what
                        // is built on the sand stands as firmly as on the meadow.
                        y = Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 1.1f)));
                    }
                    // Fine ground relief.
                    y += (Mathf.PerlinNoise(w.x * 0.18f + 3f, w.y * 0.18f + 9f) - 0.5f) * 0.18f * Mathf.Clamp01(s / 3f);
                    var t = fine[i, j];
                    // Stony hills: rounded mounds that rise gently from their edge (quarries stay buildable).
                    float hd = hillDepth[i, j];
                    if (hd > 0f)
                        y += Mathf.Min(hd, 8f) * 0.32f * (0.75f + 0.5f * Mathf.PerlinNoise(w.x * 0.07f + 4f, w.y * 0.07f + 6f))
                             + 0.2f + Mathf.PerlinNoise(w.x * 0.45f, w.y * 0.45f) * 0.35f;
                    if (t == TerrainType.Ore)
                        y += 0.2f + Mathf.PerlinNoise(w.x * 0.5f + 2f, w.y * 0.5f) * 0.45f;
                    // Mountains: steep ridged peaks that grow with the distance from their foot.
                    float md = mountainDepth[i, j];
                    float peaks = 0f;
                    if (md > 0f)
                    {
                        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(w.x * 0.03f + 11f, w.y * 0.03f + 4f) - 1f);
                        float detail = Mathf.PerlinNoise(w.x * 0.11f + 2f, w.y * 0.11f + 8f);
                        // A steady climb from the foot (no plateau), with ridges and crags on top.
                        float climb = md * (0.55f + 0.35f * detail);
                        peaks = climb + (1f - Mathf.Exp(-md / 12f)) * 16f * ridge * ridge;
                    }
                    // Hills grow with distance from the playable area (but the river valley stays low).
                    float dx = Mathf.Max(0f, Mathf.Max(mapMin.x - w.x, w.x - mapMax.x));
                    float dz = Mathf.Max(0f, Mathf.Max(mapMin.z - w.y, w.y - mapMax.z));
                    float outsideDist = Mathf.Sqrt(dx * dx + dz * dz);
                    float walls = 0f;
                    if (outsideDist > 0f)
                    {
                        // Rolling foothills first, then ridged mountains that wall in the valley.
                        float hills = outsideDist * 0.12f * (0.6f + Mathf.PerlinNoise(w.x * 0.035f + 5f, w.y * 0.035f + 1f));
                        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(w.x * 0.028f + 11f, w.y * 0.028f + 4f) - 1f);
                        float detail = Mathf.PerlinNoise(w.x * 0.09f + 2f, w.y * 0.09f + 8f);
                        float rise = Mathf.Pow(Mathf.Clamp01((outsideDist - 8f) / 40f), 1.5f);
                        float mountains = rise * (20f + 26f * ridge * ridge) * (0.75f + 0.5f * detail);
                        walls = (hills + mountains) * Mathf.Clamp01(s / 10f);
                    }
                    y += Mathf.Max(peaks, walls);
                    worldY[i, j] = y;
                }
            }
            // One soft pass removes the cell steps along mountain and hill edges (water stays carved).
            var smooth = (float[,])worldY.Clone();
            for (int i = 1; i < HeightRes - 1; i++)
                for (int j = 1; j < HeightRes - 1; j++)
                {
                    if (signedWaterDistance[i, j] < 1.5f) continue;
                    float sum = 0f;
                    for (int a = -1; a <= 1; a++)
                        for (int b = -1; b <= 1; b++) sum += worldY[i + a, j + b];
                    smooth[i, j] = sum / 9f;
                }
            worldY = smooth;
            for (int i = 0; i < HeightRes; i++)
                for (int j = 0; j < HeightRes; j++)
                    h[i, j] = Mathf.Clamp01((worldY[i, j] + BaseDepth) / HeightRange);
            return h;
        }

        // MARK: - Texture layers

        /// <summary>A road segment between two cell centres. Stone roads are paved, others are beaten earth.</summary>
        public struct RoadSegment
        {
            public Vector3 A, B;
            public bool Stone;
            public RoadSegment(Vector3 a, Vector3 b, bool stone) { A = a; B = b; Stone = stone; }
        }

        void BuildBaseSplat()
        {
            int L = LayerNames.Length;
            baseSplat = new float[SplatRes, SplatRes, L];
            float stepH = (float)(HeightRes - 1) / (SplatRes - 1);
            var wts = new float[L];
            for (int i = 0; i < SplatRes; i++)
            {
                for (int j = 0; j < SplatRes; j++)
                {
                    var w = SampleWorld(i, j, SplatRes);
                    int hi = Mathf.Clamp(Mathf.RoundToInt(i * stepH), 0, HeightRes - 1);
                    int hj = Mathf.Clamp(Mathf.RoundToInt(j * stepH), 0, HeightRes - 1);
                    float s = signedWaterDistance[hi, hj];
                    var t = fine[hi, hj];
                    float n1 = Mathf.PerlinNoise(w.x * 0.11f + 7f, w.y * 0.11f + 3f);
                    float n2 = Mathf.PerlinNoise(w.x * 0.37f + 1f, w.y * 0.37f + 5f);
                    System.Array.Clear(wts, 0, L);
                    float gy = worldY[hi, hj];

                    if (s <= 0f) wts[LRiver] = 1f;
                    else
                    {
                        switch (t)
                        {
                            case TerrainType.Forest: wts[LForest] = 1f; wts[LGrass] = 0.25f * n1; break;
                            case TerrainType.Rock: wts[LRock] = 1f; wts[LGrass] = 0.45f * n2; break;
                            case TerrainType.Ore: wts[LRock] = 1f; wts[LMud] = 0.6f + 0.6f * n2; break;
                            case TerrainType.Mountain:
                                // Alpine grass on the lower slopes, bare rock above.
                                wts[LRock] = 1.2f;
                                wts[LGrass] = 0.6f * n1 * (1f - Mathf.Clamp01((gy - 2f) / 5f));
                                wts[LForest] = 0.25f * n2 * (1f - Mathf.Clamp01((gy - 3f) / 6f));
                                break;
                            case TerrainType.Sand: wts[LSand] = 1f; wts[LMud] = 0.3f * n2; break;
                            default:
                                wts[LGrass] = 1f;
                                wts[LMud] = Mathf.Clamp01(n1 * 1.6f - 0.95f) * 0.8f;
                                wts[LForest] = Mathf.Clamp01(n2 * 1.4f - 0.9f) * 0.6f;
                                break;
                        }
                        // Wet sand and mud along the banks.
                        float bank = 1f - Mathf.Clamp01((s - 0.2f) / 2.2f);
                        wts[LSand] += bank * 1.4f;
                        wts[LMud] += bank * 0.4f * n2;
                    }

                    // High ground turns to bare rock on steep faces and grass thins out on ridges.
                    if (gy > 2.5f)
                    {
                        float gx = worldY[hi, Mathf.Min(HeightRes - 1, hj + 1)] - worldY[hi, Mathf.Max(0, hj - 1)];
                        float gz = worldY[Mathf.Min(HeightRes - 1, hi + 1), hj] - worldY[Mathf.Max(0, hi - 1), hj];
                        float slope = new Vector2(gx, gz).magnitude / (2f * Size / (HeightRes - 1));
                        float rock = Mathf.Clamp01((slope - 0.45f) * 2.2f) + Mathf.Clamp01((gy - 18f) / 10f);
                        rock = Mathf.Clamp01(rock + (n2 - 0.5f) * 0.3f);
                        for (int l = 0; l < L; l++) wts[l] *= 1f - rock;
                        wts[LRock] += rock * 1.5f;
                    }

                    float sum = 0f;
                    for (int l = 0; l < L; l++) sum += wts[l];
                    for (int l = 0; l < L; l++) baseSplat[i, j, l] = sum > 0 ? wts[l] / sum : (l == 0 ? 1 : 0);
                }
            }
        }

        /// <summary>Splat samples (rows i0..i1, columns j0..j1) the last paint stamped; i0 &gt; i1 when none.</summary>
        (int i0, int i1, int j0, int j1) stamped = (0, -1, 0, -1);
        bool painted;

        /// <summary>
        /// Paints the texture layers: the natural ground plus trodden mud around
        /// <paramref name="buildings"/> (world positions with a radius), worn footpaths along
        /// <paramref name="paths"/> and the player's <paramref name="roads"/>.
        /// Only the patch around the village is worked out and sent to the GPU again (what is stamped
        /// now and what the last paint stamped): the whole valley took a long freeze on a phone.
        /// </summary>
        public void PaintSplat(List<(Vector3 center, float radius)> buildings, List<(Vector3 a, Vector3 b)> paths,
                               List<RoadSegment> roads = null)
        {
            if (baseSplat == null) BuildBaseSplat();
            int L = LayerNames.Length;
            float px = Size / (SplatRes - 1);
            var add = new float[L];
            var stamps = new List<(int i0, int i1, int j0, int j1, System.Func<Vector2, int, int, bool> weights)>();
            float[,,] maps = null;
            int ri0 = 0, rj0 = 0;

            // Each stamp only touches the samples inside its bounding box.
            void Stamp(Vector2 min, Vector2 max, System.Func<Vector2, int, int, bool> weights)
            {
                int j0 = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / px), 0, SplatRes - 1);
                int j1 = Mathf.Clamp(Mathf.CeilToInt((max.x - origin.x) / px), 0, SplatRes - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt((min.y - origin.z) / px), 0, SplatRes - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((max.y - origin.z) / px), 0, SplatRes - 1);
                stamps.Add((i0, i1, j0, j1, weights));
            }

            float N2(Vector2 w) => Mathf.PerlinNoise(w.x * 0.37f + 1f, w.y * 0.37f + 5f);

            if (buildings != null)
                foreach (var (c, r) in buildings)
                {
                    var c2 = new Vector2(c.x, c.z);
                    float reach = r + 1.6f;
                    Stamp(c2 - Vector2.one * reach, c2 + Vector2.one * reach, (w, i, j) =>
                    {
                        float k = 1f - Mathf.Clamp01((Vector2.Distance(w, c2) - r) / 1.6f);
                        if (k <= 0f) return false;
                        add[LMud] = k * (0.8f + 0.4f * N2(w));
                        return true;
                    });
                }
            if (paths != null)
                foreach (var (a, b) in paths)
                {
                    var a2 = new Vector2(a.x, a.z);
                    var b2 = new Vector2(b.x, b.z);
                    Stamp(Vector2.Min(a2, b2) - Vector2.one, Vector2.Max(a2, b2) + Vector2.one, (w, i, j) =>
                    {
                        float k = 1f - Mathf.Clamp01((DistanceToSegment(w, a2, b2) - 0.35f) / 0.6f);
                        if (k <= 0f) return false;
                        add[LMud] = k * 1.6f;
                        return true;
                    });
                }
            if (roads != null)
                foreach (var road in roads)
                {
                    var a2 = new Vector2(road.A.x, road.A.z);
                    var b2 = new Vector2(road.B.x, road.B.z);
                    float half = road.Stone ? 0.85f : 0.75f;
                    Stamp(Vector2.Min(a2, b2) - Vector2.one * 1.6f, Vector2.Max(a2, b2) + Vector2.one * 1.6f, (w, i, j) =>
                    {
                        float d = DistanceToSegment(w, a2, b2);
                        float k = 1f - Mathf.Clamp01((d - half) / 0.45f);
                        if (k <= 0f) return false;
                        // Roads replace the ground underneath instead of blending into it.
                        for (int l = 0; l < L; l++) maps[i - ri0, j - rj0, l] *= 1f - k * 0.9f;
                        if (road.Stone) { add[LRock] = k * 2.2f; add[LSand] = k * 0.4f * N2(w); }
                        else { add[LMud] = k * 2f; add[LSand] = k * 0.5f * N2(w); }
                        return true;
                    });
                }

            // What is stamped now; the patch redrawn also covers the last stamps, so they turn back to bare ground.
            var now = (i0: SplatRes, i1: -1, j0: SplatRes, j1: -1);
            foreach (var st in stamps)
                now = (Mathf.Min(now.i0, st.i0), Mathf.Max(now.i1, st.i1), Mathf.Min(now.j0, st.j0), Mathf.Max(now.j1, st.j1));
            var region = painted
                ? (i0: Mathf.Min(now.i0, stamped.i0), i1: Mathf.Max(now.i1, stamped.i1), j0: Mathf.Min(now.j0, stamped.j0), j1: Mathf.Max(now.j1, stamped.j1))
                : (i0: 0, i1: SplatRes - 1, j0: 0, j1: SplatRes - 1);
            painted = true;
            stamped = now;
            if (region.i0 > region.i1 || region.j0 > region.j1) return;

            ri0 = region.i0;
            rj0 = region.j0;
            int h = region.i1 - region.i0 + 1, wd = region.j1 - region.j0 + 1;
            maps = new float[h, wd, L];
            for (int i = 0; i < h; i++)
                for (int j = 0; j < wd; j++)
                    for (int l = 0; l < L; l++) maps[i, j, l] = baseSplat[ri0 + i, rj0 + j, l];

            foreach (var st in stamps)
                for (int i = st.i0; i <= st.i1; i++)
                    for (int j = st.j0; j <= st.j1; j++)
                    {
                        System.Array.Clear(add, 0, L);
                        if (!st.weights(SampleWorld(i, j, SplatRes), i, j)) continue;
                        float sum = 0f;
                        for (int l = 0; l < L; l++) { maps[i - ri0, j - rj0, l] += add[l]; sum += maps[i - ri0, j - rj0, l]; }
                        for (int l = 0; l < L; l++) maps[i - ri0, j - rj0, l] /= sum;
                    }

            var output = new float[h, wd, OutputLayers.Length];
            for (int i = 0; i < h; i++)
                for (int j = 0; j < wd; j++)
                    for (int l = 0; l < L; l++)
                    {
                        float v = maps[i, j, l];
                        if (v <= 0f) continue;
                        foreach (var (to, amount) in Fold[l]) output[i, j, to] += v * amount;
                    }
            Terrain.terrainData.SetAlphamaps(rj0, ri0, output);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
