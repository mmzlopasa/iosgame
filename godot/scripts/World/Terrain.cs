using System;
using System.Collections.Generic;
using Founders.Core;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// The valley's ground: a carved river and lake with sandy banks, stony hills, mountains with ore
    /// at their feet and the valley walls beyond the playable area (the shapes of the Unity version's
    /// WorldTerrain). It is drawn as painted ground: every sample gets a colour from its ground layers
    /// (meadow, forest floor, mud, sand, riverbed, rock), and the terrain shader adds brush texture,
    /// season tints and soft light. Roads, trodden paths and mud around buildings are painted on top.
    /// </summary>
    public sealed class Terrain
    {
        public const float WaterLevel = -0.38f;
        /// <summary>Metres between height samples.</summary>
        const float Step = 1f;
        const int Chunk = 48;

        public float Size { get; }
        public readonly MapDefinition Map;
        readonly Vector3 origin;
        readonly int res;
        float[,] signedWaterDistance, mountainDepth, hillDepth, worldY;
        TerrainType[,] fine;
        Color[,] baseColor, color;
        float[,] grassiness, paint;

        public Node3D Root { get; }
        readonly List<(MeshInstance3D mesh, int i0, int j0)> chunks = new List<(MeshInstance3D, int, int)>();
        public ShaderMaterial Material { get; }

        // Painted ground layers.
        static readonly Color Meadow = new Color("86b552");
        static readonly Color ForestFloor = new Color("5f7d38");
        static readonly Color Mud = new Color("8c6c49");
        static readonly Color Sand = new Color("dcc590");
        static readonly Color Riverbed = new Color("6d6a55");
        static readonly Color Rock = new Color("8f8b83");
        static readonly Color StoneRoad = new Color("a39d92");
        static readonly Color DirtRoad = new Color("9b7a52");

        public Terrain(MapDefinition map, Node3D parent)
        {
            Map = map;
            Size = Mathf.Max(map.Width, map.Height) * Grid.CellSize + 128f;
            res = Mathf.RoundToInt(Size / Step) + 1;
            var center = Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f);
            origin = new Vector3(center.X - Size * 0.5f, 0f, center.Z - Size * 0.5f);

            ClassifySamples();
            BuildHeights();
            BuildBaseColors();
            color = (Color[,])baseColor.Clone();

            Root = new Node3D { Name = "Terrain" };
            parent.AddChild(Root);
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
            Material.SetShaderParameter("brush", PaintNoise());
            BuildChunks();
        }

        static NoiseTexture2D PaintNoise() => new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.05f, Seed = 9 },
        };

        // ------------------------------------------------------------------ lookup

        Vector2 SampleWorld(int i, int j) => new Vector2(origin.X + j * Step, origin.Z + i * Step);

        /// <summary>Ground height (world y) at a world position (bilinear between samples).</summary>
        public float HeightAt(Vector3 world) => HeightAt(world.X, world.Z);

        public float HeightAt(float x, float z)
        {
            float fj = Mathf.Clamp((x - origin.X) / Step, 0, res - 1.001f);
            float fi = Mathf.Clamp((z - origin.Z) / Step, 0, res - 1.001f);
            int i = (int)fi, j = (int)fj;
            float ti = fi - i, tj = fj - j;
            float a = Mathf.Lerp(worldY[i, j], worldY[i, j + 1], tj);
            float b = Mathf.Lerp(worldY[i + 1, j], worldY[i + 1, j + 1], tj);
            return Mathf.Lerp(a, b, ti);
        }

        public Vector3 OnGround(Vector3 world) => new Vector3(world.X, HeightAt(world), world.Z);

        /// <summary>Terrain type of any grid cell, including the land around the playable map.</summary>
        public TerrainType CellType(int cx, int cy)
        {
            if (cx >= 0 && cy >= 0 && cx < Map.Width && cy < Map.Height)
                return Map.Terrain(cx, cy) ?? TerrainType.Grass;
            int ex = Mathf.Clamp(cx, 0, Map.Width - 1), ey = Mathf.Clamp(cy, 0, Map.Height - 1);
            var edge = Map.Terrain(ex, ey) ?? TerrainType.Grass;
            if (cx == ex && (edge == TerrainType.Water || edge == TerrainType.Sand)) return edge;
            if (edge == TerrainType.Mountain || edge == TerrainType.Ore) return TerrainType.Mountain;
            float n = Noise(cx, cy, 0.22f);
            if (edge == TerrainType.Rock) return n > 0.5f ? TerrainType.Rock : TerrainType.Forest;
            if (cx < 0) return Noise(cx, cy, 0.25f) > 0.2f ? TerrainType.Forest : TerrainType.Grass;
            return n > 0.3f ? TerrainType.Forest : TerrainType.Grass;
        }

        static float Noise(float x, float y, float f) => Perlin.At(x * f + 31.7f, y * f + 12.3f);

        // ------------------------------------------------------------------ classification

        void ClassifySamples()
        {
            fine = new TerrainType[res, res];
            var water = new bool[res, res];
            var mountain = new bool[res, res];
            var hill = new bool[res, res];
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                {
                    var (cx, cy) = Grid.CellAt(new Vector3(SampleWorld(i, j).X, 0, SampleWorld(i, j).Y));
                    var t = CellType(cx, cy);
                    fine[i, j] = t;
                    water[i, j] = t == TerrainType.Water;
                    mountain[i, j] = t == TerrainType.Mountain;
                    hill[i, j] = t == TerrainType.Rock;
                }
            var outside = Chamfer(water, true, Step);
            var inside = Chamfer(water, false, Step);
            signedWaterDistance = new float[res, res];
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                    signedWaterDistance[i, j] = water[i, j] ? -inside[i, j] : outside[i, j];
            // The map is made of 2 m cells: round off the stepped shores and let them wander a little.
            signedWaterDistance = Blur(Blur(signedWaterDistance, 1), 1);
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                {
                    var w = SampleWorld(i, j);
                    signedWaterDistance[i, j] += (Perlin.At(w.X * 0.21f + 13f, w.Y * 0.21f + 7f) - 0.5f) * 0.5f;
                }
            mountainDepth = Chamfer(mountain, false, Step);
            hillDepth = Chamfer(hill, false, Step);
        }

        static float[,] Blur(float[,] src, int r)
        {
            int n = src.GetLength(0);
            var tmp = new float[n, n];
            var dst = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = Math.Max(0, j - r); k <= Math.Min(n - 1, j + r); k++) { sum += src[i, k]; c++; }
                    tmp[i, j] = sum / c;
                }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float sum = 0f; int c = 0;
                    for (int k = Math.Max(0, i - r); k <= Math.Min(n - 1, i + r); k++) { sum += tmp[k, j]; c++; }
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
                    if (i > 0) { v = Math.Min(v, d[i - 1, j] + a); if (j > 0) v = Math.Min(v, d[i - 1, j - 1] + b); if (j < n - 1) v = Math.Min(v, d[i - 1, j + 1] + b); }
                    if (j > 0) v = Math.Min(v, d[i, j - 1] + a);
                    d[i, j] = v;
                }
            for (int i = n - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                {
                    float v = d[i, j];
                    if (i < n - 1) { v = Math.Min(v, d[i + 1, j] + a); if (j < n - 1) v = Math.Min(v, d[i + 1, j + 1] + b); if (j > 0) v = Math.Min(v, d[i + 1, j - 1] + b); }
                    if (j < n - 1) v = Math.Min(v, d[i, j + 1] + a);
                    d[i, j] = v;
                }
            return d;
        }

        // ------------------------------------------------------------------ heights

        void BuildHeights()
        {
            worldY = new float[res, res];
            var mapMin = Grid.CellCenter(-0.5f, -0.5f);
            var mapMax = Grid.CellCenter(Map.Width - 0.5f, Map.Height - 0.5f);
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                {
                    var w = SampleWorld(i, j);
                    float s = signedWaterDistance[i, j];
                    float y = s <= 0f
                        ? Mathf.Lerp(-0.45f, -1.5f, Mathf.Clamp(-s / 2.5f, 0f, 1f))
                        : Mathf.Lerp(-0.45f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(s / 1.1f, 0f, 1f)));
                    y += (Perlin.At(w.X * 0.18f + 3f, w.Y * 0.18f + 9f) - 0.5f) * 0.18f * Mathf.Clamp(s / 3f, 0f, 1f);
                    var t = fine[i, j];
                    float hd = hillDepth[i, j];
                    if (hd > 0f)
                        y += Mathf.Min(hd, 8f) * 0.32f * (0.75f + 0.5f * Perlin.At(w.X * 0.07f + 4f, w.Y * 0.07f + 6f))
                             + 0.2f + Perlin.At(w.X * 0.45f, w.Y * 0.45f) * 0.35f;
                    if (t == TerrainType.Ore)
                        y += 0.2f + Perlin.At(w.X * 0.5f + 2f, w.Y * 0.5f) * 0.45f;
                    float md = mountainDepth[i, j];
                    float peaks = 0f;
                    if (md > 0f)
                    {
                        float ridge = 1f - Mathf.Abs(2f * Perlin.At(w.X * 0.03f + 11f, w.Y * 0.03f + 4f) - 1f);
                        float detail = Perlin.At(w.X * 0.11f + 2f, w.Y * 0.11f + 8f);
                        float climb = md * (0.55f + 0.35f * detail);
                        peaks = climb + (1f - Mathf.Exp(-md / 12f)) * 16f * ridge * ridge;
                    }
                    float dx = Mathf.Max(0f, Mathf.Max(mapMin.X - w.X, w.X - mapMax.X));
                    float dz = Mathf.Max(0f, Mathf.Max(mapMin.Z - w.Y, w.Y - mapMax.Z));
                    float outsideDist = Mathf.Sqrt(dx * dx + dz * dz);
                    float walls = 0f;
                    if (outsideDist > 0f)
                    {
                        float hills = outsideDist * 0.12f * (0.6f + Perlin.At(w.X * 0.035f + 5f, w.Y * 0.035f + 1f));
                        float ridge = 1f - Mathf.Abs(2f * Perlin.At(w.X * 0.028f + 11f, w.Y * 0.028f + 4f) - 1f);
                        float detail = Perlin.At(w.X * 0.09f + 2f, w.Y * 0.09f + 8f);
                        float rise = Mathf.Pow(Mathf.Clamp((outsideDist - 8f) / 40f, 0f, 1f), 1.5f);
                        float mountains = rise * (20f + 26f * ridge * ridge) * (0.75f + 0.5f * detail);
                        walls = (hills + mountains) * Mathf.Clamp(s / 10f, 0f, 1f);
                    }
                    y += Mathf.Max(peaks, walls);
                    worldY[i, j] = y;
                }
            // Soft passes remove the cell steps and round sharp rims; the water stays carved.
            for (int pass = 0; pass < 2; pass++)
            {
                var smooth = (float[,])worldY.Clone();
                for (int i = 1; i < res - 1; i++)
                    for (int j = 1; j < res - 1; j++)
                    {
                        if (signedWaterDistance[i, j] < 1.5f) continue;
                        float sum = 0f;
                        for (int a = -1; a <= 1; a++)
                            for (int b = -1; b <= 1; b++) sum += worldY[i + a, j + b];
                        smooth[i, j] = sum / 9f;
                    }
                worldY = smooth;
            }
        }

        // ------------------------------------------------------------------ colours

        void BuildBaseColors()
        {
            baseColor = new Color[res, res];
            grassiness = new float[res, res];
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                {
                    var w = SampleWorld(i, j);
                    float s = signedWaterDistance[i, j];
                    var t = fine[i, j];
                    float n1 = Perlin.At(w.X * 0.11f + 7f, w.Y * 0.11f + 3f);
                    float n2 = Perlin.At(w.X * 0.37f + 1f, w.Y * 0.37f + 5f);
                    float gy = worldY[i, j];
                    float wGrass = 0, wForest = 0, wMud = 0, wSand = 0, wRiver = 0, wRock = 0;
                    if (s <= 0f) wRiver = 1f;
                    else
                    {
                        switch (t)
                        {
                            case TerrainType.Forest: wForest = 1f; wGrass = 0.25f * n1; break;
                            case TerrainType.Rock: wRock = 1f; wGrass = 0.45f * n2; break;
                            case TerrainType.Ore: wRock = 1f; wMud = 0.6f + 0.6f * n2; break;
                            case TerrainType.Mountain:
                                wRock = 1.2f;
                                wGrass = 0.6f * n1 * (1f - Mathf.Clamp((gy - 2f) / 5f, 0f, 1f));
                                wForest = 0.25f * n2 * (1f - Mathf.Clamp((gy - 3f) / 6f, 0f, 1f));
                                break;
                            case TerrainType.Sand: wSand = 1f; wMud = 0.3f * n2; break;
                            default:
                                wGrass = 1f;
                                wMud = Mathf.Clamp(n1 * 1.6f - 0.95f, 0f, 1f) * 0.8f;
                                wForest = Mathf.Clamp(n2 * 1.4f - 0.9f, 0f, 1f) * 0.6f;
                                break;
                        }
                        float bank = 1f - Mathf.Clamp((s - 0.2f) / 2.2f, 0f, 1f);
                        wSand += bank * 1.4f;
                        wMud += bank * 0.4f * n2;
                    }
                    if (gy > 2.5f)
                    {
                        float gx = worldY[i, Math.Min(res - 1, j + 1)] - worldY[i, Math.Max(0, j - 1)];
                        float gz = worldY[Math.Min(res - 1, i + 1), j] - worldY[Math.Max(0, i - 1), j];
                        float slope = new Vector2(gx, gz).Length() / (2f * Step);
                        float rock = Mathf.Clamp((slope - 0.45f) * 2.2f, 0f, 1f) + Mathf.Clamp((gy - 18f) / 10f, 0f, 1f);
                        rock = Mathf.Clamp(rock + (n2 - 0.5f) * 0.3f, 0f, 1f);
                        float keep = 1f - rock;
                        wGrass *= keep; wForest *= keep; wMud *= keep; wSand *= keep; wRiver *= keep; wRock *= keep;
                        wRock += rock * 1.5f;
                    }
                    float sum = wGrass + wForest + wMud + wSand + wRiver + wRock;
                    if (sum <= 0f) { wGrass = 1f; sum = 1f; }
                    var c = (Meadow * wGrass + ForestFloor * wForest + Mud * wMud + Sand * wSand + Riverbed * wRiver + Rock * wRock) / sum;
                    // Small painterly variation in hue and value.
                    float v = 0.93f + 0.14f * n1;
                    baseColor[i, j] = new Color(c.R * v, c.G * v, c.B * (0.96f + 0.08f * n2));
                    grassiness[i, j] = (wGrass + wForest * 0.7f) / sum;
                }
        }

        /// <summary>
        /// Paints trodden mud around buildings, worn footpaths and the player's roads over the natural
        /// ground, then sends the changed chunks to the GPU again.
        /// </summary>
        public void Paint(List<(Vector3 center, float radius)> buildings, List<(Vector3 a, Vector3 b)> paths, List<RoadSegment> roads)
        {
            var next = (Color[,])baseColor.Clone();
            var dirty = new HashSet<(int, int)>();
            void Blend(Vector2 min, Vector2 max, Func<Vector2, (Color c, float k)> f)
            {
                int j0 = Mathf.Clamp(Mathf.FloorToInt((min.X - origin.X) / Step), 0, res - 1);
                int j1 = Mathf.Clamp(Mathf.CeilToInt((max.X - origin.X) / Step), 0, res - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt((min.Y - origin.Z) / Step), 0, res - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((max.Y - origin.Z) / Step), 0, res - 1);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        var (c, k) = f(SampleWorld(i, j));
                        if (k <= 0f) continue;
                        next[i, j] = next[i, j].Lerp(c, Mathf.Clamp(k, 0f, 1f));
                    }
            }
            float N2(Vector2 w) => Perlin.At(w.X * 0.37f + 1f, w.Y * 0.37f + 5f);
            if (buildings != null)
                foreach (var (c, r) in buildings)
                {
                    var c2 = new Vector2(c.X, c.Z);
                    float reach = r + 1.6f;
                    Blend(c2 - Vector2.One * reach, c2 + Vector2.One * reach, w =>
                        (Mud, (1f - Mathf.Clamp((w.DistanceTo(c2) - r) / 1.6f, 0f, 1f)) * (0.45f + 0.25f * N2(w))));
                }
            if (paths != null)
                foreach (var (a, b) in paths)
                {
                    var a2 = new Vector2(a.X, a.Z);
                    var b2 = new Vector2(b.X, b.Z);
                    Blend(a2.Min(b2) - Vector2.One, a2.Max(b2) + Vector2.One, w =>
                        (Mud, (1f - Mathf.Clamp((DistanceToSegment(w, a2, b2) - 0.35f) / 0.6f, 0f, 1f)) * 0.75f));
                }
            if (roads != null)
                foreach (var road in roads)
                {
                    var a2 = new Vector2(road.A.X, road.A.Z);
                    var b2 = new Vector2(road.B.X, road.B.Z);
                    float half = road.Stone ? 0.85f : 0.75f;
                    Blend(a2.Min(b2) - Vector2.One * 1.6f, a2.Max(b2) + Vector2.One * 1.6f, w =>
                    {
                        float k = 1f - Mathf.Clamp((DistanceToSegment(w, a2, b2) - half) / 0.45f, 0f, 1f);
                        var c = road.Stone ? StoneRoad.Lerp(Sand, 0.2f * N2(w)) : DirtRoad.Lerp(Sand, 0.25f * N2(w));
                        return (c, k * 0.95f);
                    });
                }
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                    if (next[i, j] != color[i, j]) dirty.Add((i / Chunk, j / Chunk));
            color = next;
            foreach (var (mesh, i0, j0) in chunks)
                if (dirty.Contains((i0 / Chunk, j0 / Chunk))) mesh.Mesh = ChunkMesh(i0, j0);
        }

        public struct RoadSegment
        {
            public Vector3 A, B;
            public bool Stone;
            public RoadSegment(Vector3 a, Vector3 b, bool stone) { A = a; B = b; Stone = stone; }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / Mathf.Max(1e-4f, ab.LengthSquared()), 0f, 1f);
            return p.DistanceTo(a + ab * t);
        }

        // ------------------------------------------------------------------ meshes

        void BuildChunks()
        {
            for (int i0 = 0; i0 < res - 1; i0 += Chunk)
                for (int j0 = 0; j0 < res - 1; j0 += Chunk)
                {
                    var mi = new MeshInstance3D { Mesh = ChunkMesh(i0, j0), MaterialOverride = Material, Name = $"Ground_{i0}_{j0}" };
                    Root.AddChild(mi);
                    chunks.Add((mi, i0, j0));
                }
        }

        Vector3 Normal(int i, int j)
        {
            float hl = worldY[i, Math.Max(0, j - 1)], hr = worldY[i, Math.Min(res - 1, j + 1)];
            float hd = worldY[Math.Max(0, i - 1), j], hu = worldY[Math.Min(res - 1, i + 1), j];
            return new Vector3(hl - hr, 2f * Step, hd - hu).Normalized();
        }

        ArrayMesh ChunkMesh(int i0, int j0)
        {
            int i1 = Math.Min(res - 1, i0 + Chunk), j1 = Math.Min(res - 1, j0 + Chunk);
            int rows = i1 - i0 + 1, cols = j1 - j0 + 1;
            var verts = new Vector3[rows * cols];
            var normals = new Vector3[rows * cols];
            var colors = new Color[rows * cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int k = i * cols + j;
                    var w = SampleWorld(i0 + i, j0 + j);
                    verts[k] = new Vector3(w.X, worldY[i0 + i, j0 + j], w.Y);
                    normals[k] = Normal(i0 + i, j0 + j);
                    var c = color[i0 + i, j0 + j];
                    // Alpha carries how much meadow is here (the shader tints it for the season).
                    colors[k] = new Color(c.R, c.G, c.B, grassiness[i0 + i, j0 + j]);
                }
            var idx = new int[(rows - 1) * (cols - 1) * 6];
            int n = 0;
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < cols - 1; j++)
                {
                    int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                    // Clockwise seen from above (Godot's front faces).
                    idx[n++] = a; idx[n++] = b; idx[n++] = c;
                    idx[n++] = b; idx[n++] = d; idx[n++] = c;
                }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.Index] = idx;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }

        /// <summary>Lowest ground (or water surface) under a footprint.</summary>
        public float LowestGround(Vector3 center, Footprint fp)
        {
            float hx = fp.Width * Grid.CellSize * 0.45f, hz = fp.Height * Grid.CellSize * 0.45f;
            float low = center.Y;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    low = Mathf.Min(low, Mathf.Max(WaterLevel, HeightAt(center + new Vector3(i * hx, 0, j * hz))));
            return low;
        }

        /// <summary>The point where a ray meets the ground (or the water), if it does.</summary>
        public bool Raycast(Vector3 from, Vector3 dir, out Vector3 hit)
        {
            hit = default;
            dir = dir.Normalized();
            float t = 0f, step = 0.5f;
            var prev = from;
            for (int k = 0; k < 2000; k++)
            {
                var p = from + dir * t;
                float g = Mathf.Max(HeightAt(p), WaterLevel);
                if (p.Y <= g)
                {
                    // Refine between the last point above and this one.
                    var lo = prev; var hi = p;
                    for (int r = 0; r < 8; r++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (mid.Y <= Mathf.Max(HeightAt(mid), WaterLevel)) hi = mid; else lo = mid;
                    }
                    hit = hi;
                    return true;
                }
                prev = p;
                t += step;
                if (t > 30f) step = 1.5f;
            }
            return false;
        }
    }
}
