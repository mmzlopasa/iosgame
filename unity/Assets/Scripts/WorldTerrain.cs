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
        public const float Size = 256f;     // the 128 m valley plus 64 m of surrounding land on each side
        const float HeightRange = 90f;
        const float BaseDepth = 5f;          // world y = 0 sits this far above the terrain bottom
        const int HeightRes = 513;
        const int SplatRes = 512;
        public const float WaterLevel = -0.38f;

        static readonly string[] LayerNames = { "Grass", "ForestFloor", "Mud", "Sand", "Riverbed", "RockGround" };
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
            var center = Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f);
            origin = new Vector3(center.x - Size * 0.5f, -BaseDepth, center.z - Size * 0.5f);

            ClassifySamples();
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes };
            data.size = new Vector3(Size, HeightRange, Size);
            data.SetHeights(0, 0, BuildHeights());
            var layers = new List<TerrainLayer>();
            foreach (var n in LayerNames)
            {
                var l = Resources.Load<TerrainLayer>("TerrainLayers/" + n);
                layers.Add(l != null ? l : new TerrainLayer { diffuseTexture = Texture2D.grayTexture, tileSize = new Vector2(5, 5) });
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
            mountainDepth = Chamfer(mountain, false, step);
            hillDepth = Chamfer(hill, false, step);
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
                        y = Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 2.2f)));
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
                        peaks = (1f - Mathf.Exp(-md / 7f)) * (9f + 24f * ridge * ridge) * (0.75f + 0.5f * detail) + md * 0.12f;
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
                                wts[LRock] = 1f;
                                wts[LGrass] = 0.7f * n1 * (1f - Mathf.Clamp01((gy - 4f) / 10f));
                                wts[LForest] = 0.3f * n2;
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

        /// <summary>
        /// Paints the texture layers: the natural ground plus trodden mud around
        /// <paramref name="buildings"/> (world positions with a radius), worn footpaths along
        /// <paramref name="paths"/> and the player's <paramref name="roads"/>.
        /// </summary>
        public void PaintSplat(List<(Vector3 center, float radius)> buildings, List<(Vector3 a, Vector3 b)> paths,
                               List<RoadSegment> roads = null)
        {
            if (baseSplat == null) BuildBaseSplat();
            int L = LayerNames.Length;
            var maps = (float[,,])baseSplat.Clone();
            float px = Size / (SplatRes - 1);
            var add = new float[L];

            // Each stamp only touches the samples inside its bounding box.
            void Stamp(Vector2 min, Vector2 max, System.Func<Vector2, int, int, bool> weights)
            {
                int j0 = Mathf.Clamp(Mathf.FloorToInt((min.x - origin.x) / px), 0, SplatRes - 1);
                int j1 = Mathf.Clamp(Mathf.CeilToInt((max.x - origin.x) / px), 0, SplatRes - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt((min.y - origin.z) / px), 0, SplatRes - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((max.y - origin.z) / px), 0, SplatRes - 1);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        System.Array.Clear(add, 0, L);
                        if (!weights(SampleWorld(i, j, SplatRes), i, j)) continue;
                        float sum = 0f;
                        for (int l = 0; l < L; l++) { maps[i, j, l] += add[l]; sum += maps[i, j, l]; }
                        for (int l = 0; l < L; l++) maps[i, j, l] /= sum;
                    }
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
                        for (int l = 0; l < L; l++) maps[i, j, l] *= 1f - k * 0.9f;
                        if (road.Stone) { add[LRock] = k * 2.2f; add[LSand] = k * 0.4f * N2(w); }
                        else { add[LMud] = k * 2f; add[LSand] = k * 0.5f * N2(w); }
                        return true;
                    });
                }
            Terrain.terrainData.SetAlphamaps(0, 0, maps);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
