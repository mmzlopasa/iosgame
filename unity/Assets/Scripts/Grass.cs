using System.Collections.Generic;
using Founders.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Founders
{
    /// <summary>
    /// Meadow grass and wildflowers: thousands of crossed photo cards (rendered from Poly Haven
    /// scans) drawn with GPU instancing, so the ground reads as living grass instead of a texture.
    /// Rebuilt when buildings change, so nothing grows through floors.
    /// </summary>
    public class GrassField : MonoBehaviour
    {
        sealed class Layer
        {
            public Material Material;
            public Material Top;
            public RenderParams TopParams;
            public float Aspect = 1f;
            public readonly List<Matrix4x4[]> Batches = new List<Matrix4x4[]>();
            public readonly List<int> Counts = new List<int>();
        }

        const int BatchSize = 1023;
        const float Radius = 58f;
        Mesh mesh, flat;
        Layer grass, flowers;
        RenderParams grassParams, flowerParams;
        WorldTerrain ground;
        Game game;

        public void Init(WorldTerrain ground, Game game)
        {
            this.ground = ground;
            this.game = game;
            mesh = CrossMesh();
            flat = FlatMesh();
            grass = NewLayer("GrassTuft");
            flowers = NewLayer("Wildflowers");
            Rebuild();
        }

        Layer NewLayer(string impostor)
        {
            var layer = new Layer { Material = Palette.Get($"I_{impostor}_side"), Top = Palette.Get($"I_{impostor}_top") };
            if (layer.Top != null && !layer.Top.enableInstancing) layer.Top = new Material(layer.Top) { enableInstancing = true };
            if (ModelLibrary.HasImpostor(impostor))
            {
                var info = ModelLibrary.Manifest.Obj("impostors").Obj(impostor);
                layer.Aspect = (float)(info.Num("width") / Mathf.Max(0.01f, (float)info.Num("height")));
            }
            if (layer.Material != null && !layer.Material.enableInstancing)
            {
                layer.Material = new Material(layer.Material) { enableInstancing = true };
            }
            return layer;
        }

        static RenderParams Params(Material m) => new RenderParams(m)
        {
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 400f),
            layer = 0,
        };

        /// <summary>Re-scatters grass (call when buildings are placed or removed).</summary>
        public void Rebuild()
        {
            if (grass.Material == null) return;
            var center = Grid.CellCenter((game.Content.Map.Width - 1) * 0.5f, (game.Content.Map.Height - 1) * 0.5f);
            var g = new List<Matrix4x4>();
            var f = new List<Matrix4x4>();
            var rng = new System.Random(4242);
            const float step = 0.62f;
            for (float z = -Radius; z <= Radius; z += step)
            {
                for (float x = -Radius; x <= Radius; x += step)
                {
                    float jx = ((float)rng.NextDouble() - 0.5f) * step, jz = ((float)rng.NextDouble() - 0.5f) * step;
                    double roll = rng.NextDouble();
                    float size = 0.75f + (float)rng.NextDouble() * 0.6f;
                    float yaw = (float)rng.NextDouble() * 180f;
                    var p = new Vector3(center.x + x + jx, 0, center.z + z + jz);
                    float d = new Vector2(x, z).magnitude;
                    if (d > Radius) continue;

                    var (cx, cy) = Grid.CellAt(p);
                    var type = ground.CellType(cx, cy);
                    float density = type switch
                    {
                        TerrainType.Grass => 0.8f,
                        TerrainType.Forest => 0.25f,
                        _ => 0f,
                    };
                    // Thin out with distance (it is barely visible there) and on high slopes.
                    density *= Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(30f, Radius, d));
                    if (roll > density) continue;
                    bool inside = cx >= 0 && cy >= 0 && cx < game.Content.Map.Width && cy < game.Content.Map.Height;
                    if (inside && game.BuildingIdAt(cx, cy) != null) continue;
                    float y = ground.HeightAt(p);
                    if (y < WorldTerrain.WaterLevel + 0.12f || y > 7f) continue;
                    p.y = y - 0.02f;

                    bool flower = roll < density * 0.05f;
                    var layer = flower ? flowers : grass;
                    float h = flower ? 0.32f * size : 0.3f * size;
                    var s = new Vector3(h * layer.Aspect, h, h * layer.Aspect);
                    (flower ? f : g).Add(Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), s));
                }
            }
            Fill(grass, g);
            Fill(flowers, f);
            grassParams = Params(grass.Material);
            if (flowers.Material != null) flowerParams = Params(flowers.Material);
            if (grass.Top != null) grass.TopParams = Params(grass.Top);
            if (flowers.Top != null) flowers.TopParams = Params(flowers.Top);
        }

        static void Fill(Layer layer, List<Matrix4x4> all)
        {
            layer.Batches.Clear();
            layer.Counts.Clear();
            for (int i = 0; i < all.Count; i += BatchSize)
            {
                int n = Mathf.Min(BatchSize, all.Count - i);
                var arr = new Matrix4x4[n];
                all.CopyTo(i, arr, 0, n);
                layer.Batches.Add(arr);
                layer.Counts.Add(n);
            }
        }

        void Update()
        {
            if (mesh == null || grass?.Material == null) return;
            Draw(grass, grassParams);
            if (flowers.Material != null) Draw(flowers, flowerParams);
        }

        void Draw(Layer layer, RenderParams side)
        {
            // The strategy camera looks down, so each tuft is a top-view card plus two crossed side cards.
            for (int i = 0; i < layer.Batches.Count; i++)
            {
                if (layer.Top != null) Graphics.RenderMeshInstanced(layer.TopParams, flat, 0, layer.Batches[i], layer.Counts[i]);
                Graphics.RenderMeshInstanced(side, mesh, 0, layer.Batches[i], layer.Counts[i]);
            }
        }

        /// <summary>Horizontal unit card a little above the ground (seen from the strategy camera).</summary>
        static Mesh FlatMesh()
        {
            var m = new Mesh { name = "GrassTop" };
            const float y = 0.3f;
            m.vertices = new[] { new Vector3(-0.5f, y, -0.5f), new Vector3(0.5f, y, -0.5f), new Vector3(-0.5f, y, 0.5f), new Vector3(0.5f, y, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Two unit cards crossed at 90° (pivot at the bottom); normals point up for soft, even lighting.</summary>
        static Mesh CrossMesh()
        {
            var m = new Mesh { name = "GrassCross" };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int k = 0; k < 2; k++)
            {
                var axis = k == 0 ? Vector3.right : Vector3.forward;
                int b = v.Count;
                v.Add(-axis * 0.5f); v.Add(axis * 0.5f); v.Add(-axis * 0.5f + Vector3.up); v.Add(axis * 0.5f + Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
            }
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            var n = new Vector3[v.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
