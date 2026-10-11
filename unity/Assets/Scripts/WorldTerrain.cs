using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Builds a Unity Terrain from the content map: a carved river channel with sandy banks,
    /// slightly raised rock outcrops, gentle hills beyond the playable area, and photo-texture
    /// layers (grass, forest floor, mud, sand, riverbed, rock) blended per cell.
    /// World units: metres. Cell (x, y) of the map is centred at (x·S, 0, −y·S).
    /// </summary>
    public sealed class WorldTerrain
    {
        public const float Size = 128f;
        const float HeightRange = 52f;
        const float BaseDepth = 5f;          // world y = 0 sits this far above the terrain bottom
        const int HeightRes = 257;
        const int SplatRes = 256;
        public const float WaterLevel = -0.38f;

        static readonly string[] LayerNames = { "Grass", "ForestFloor", "Mud", "Sand", "Riverbed", "RockGround" };
        const int LGrass = 0, LForest = 1, LMud = 2, LSand = 3, LRiver = 4, LRock = 5;

        public Terrain Terrain { get; private set; }
        readonly MapDefinition map;
        readonly Vector3 origin;
        float[,] signedWaterDistance; // metres; negative inside water
        TerrainType[,] fine;          // terrain type per heightmap sample
        float[,] worldY;              // ground height (world y) per heightmap sample

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
            int clampedY = Mathf.Clamp(cy, 0, map.Height - 1);
            if (cx >= 0 && cx < map.Width)
            {
                var t = map.Terrain(cx, clampedY) ?? TerrainType.Grass;
                // Beyond the north/south edges the river keeps flowing; other land turns to forest.
                if (cy != clampedY && t != TerrainType.Water && t != TerrainType.Sand)
                    return Noise(cx, cy, 0.3f) > 0.35f ? TerrainType.Forest : TerrainType.Grass;
                return t;
            }
            if (cx < 0) return Noise(cx, cy, 0.25f) > 0.2f ? TerrainType.Forest : TerrainType.Grass;
            // East of the map: the rocky ridge continues into wooded hills.
            float n = Noise(cx, cy, 0.22f);
            return n > 0.62f ? TerrainType.Rock : n > 0.3f ? TerrainType.Forest : TerrainType.Grass;
        }

        static float Noise(float x, float y, float f) => Mathf.PerlinNoise(x * f + 31.7f, y * f + 12.3f);

        Vector2 SampleWorld(int i, int j, int res) =>
            new Vector2(origin.x + j * Size / (res - 1), origin.z + i * Size / (res - 1));

        static (int, int) CellOf(Vector2 world) =>
            (Mathf.RoundToInt(world.x / Grid.CellSize), Mathf.RoundToInt(-world.y / Grid.CellSize));

        void ClassifySamples()
        {
            fine = new TerrainType[HeightRes, HeightRes];
            var water = new bool[HeightRes, HeightRes];
            for (int i = 0; i < HeightRes; i++)
            {
                for (int j = 0; j < HeightRes; j++)
                {
                    var (cx, cy) = CellOf(SampleWorld(i, j, HeightRes));
                    var t = CellType(cx, cy);
                    fine[i, j] = t;
                    water[i, j] = t == TerrainType.Water;
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
                    if (fine[i, j] == TerrainType.Rock)
                        y += 0.25f + Mathf.PerlinNoise(w.x * 0.45f, w.y * 0.45f) * 0.5f;
                    // Hills grow with distance from the playable area (but the river valley stays low).
                    float dx = Mathf.Max(0f, Mathf.Max(mapMin.x - w.x, w.x - mapMax.x));
                    float dz = Mathf.Max(0f, Mathf.Max(mapMin.z - w.y, w.y - mapMax.z));
                    float outsideDist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (outsideDist > 0f)
                    {
                        // Rolling foothills first, then ridged mountains that wall in the valley.
                        float hills = outsideDist * 0.12f * (0.6f + Mathf.PerlinNoise(w.x * 0.035f + 5f, w.y * 0.035f + 1f));
                        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(w.x * 0.028f + 11f, w.y * 0.028f + 4f) - 1f);
                        float detail = Mathf.PerlinNoise(w.x * 0.09f + 2f, w.y * 0.09f + 8f);
                        float rise = Mathf.Pow(Mathf.Clamp01((outsideDist - 10f) / 38f), 1.5f);
                        float mountains = rise * (20f + 26f * ridge * ridge) * (0.75f + 0.5f * detail);
                        y += (hills + mountains) * Mathf.Clamp01(s / 10f);
                    }
                    worldY[i, j] = y;
                    h[i, j] = Mathf.Clamp01((y + BaseDepth) / HeightRange);
                }
            }
            return h;
        }

        // MARK: - Texture layers

        /// <summary>
        /// Paints the texture layers. Trodden mud appears around <paramref name="buildings"/>
        /// (world positions with a radius) and along <paramref name="paths"/> (world segments).
        /// </summary>
        public void PaintSplat(List<(Vector3 center, float radius)> buildings, List<(Vector3 a, Vector3 b)> paths)
        {
            int L = LayerNames.Length;
            var maps = new float[SplatRes, SplatRes, L];
            float stepH = (float)(HeightRes - 1) / (SplatRes - 1);
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
                    var wts = new float[L];

                    if (s <= 0f) wts[LRiver] = 1f;
                    else
                    {
                        switch (t)
                        {
                            case TerrainType.Forest: wts[LForest] = 1f; wts[LGrass] = 0.25f * n1; break;
                            case TerrainType.Rock: wts[LRock] = 1f; wts[LGrass] = 0.3f * n2; break;
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
                    float gy = worldY[hi, hj];
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

                    if (buildings != null)
                    {
                        foreach (var (c, r) in buildings)
                        {
                            float d = Vector2.Distance(w, new Vector2(c.x, c.z));
                            float k = 1f - Mathf.Clamp01((d - r) / 1.6f);
                            wts[LMud] += k * (0.8f + 0.4f * n2);
                        }
                    }
                    if (paths != null)
                    {
                        foreach (var (a, b) in paths)
                        {
                            float d = DistanceToSegment(w, new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                            float k = 1f - Mathf.Clamp01((d - 0.35f) / 0.6f);
                            wts[LMud] += k * 1.6f;
                        }
                    }

                    float sum = 0f;
                    for (int l = 0; l < L; l++) sum += wts[l];
                    for (int l = 0; l < L; l++) maps[i, j, l] = sum > 0 ? wts[l] / sum : (l == 0 ? 1 : 0);
                }
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
