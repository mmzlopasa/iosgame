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
            public float Aspect = 1f;
            public InstanceSet Side, TopSet;
        }

        /// <summary>Reaches the corners of the valley (half its width times √2).</summary>
        float Radius;
        // Tufts are sent to the GPU every frame: on a big valley they stand further apart, so their
        // number (and the frame rate) stays what it was on the first, small valley.
        float Step;
        Mesh mesh, flat;
        Layer grass, flowers;
        WorldTerrain ground;
        Game game;

        // With the nature packs: their grass and flower clumps instead of the photo cards.
        List<NatureField.KitModel> kitGrass, kitFlowers;
        readonly Dictionary<(Mesh, int, Material), InstanceSet> kitSets = new Dictionary<(Mesh, int, Material), InstanceSet>();

        public void Init(WorldTerrain ground, Game game)
        {
            this.ground = ground;
            this.game = game;
            mesh = CrossMesh();
            flat = FlatMesh();
            grass = NewLayer("GrassTuft");
            flowers = NewLayer("Wildflowers");
            var kit = NatureKit.Current;
            if (kit != null && kit.Has("grass"))
            {
                kitGrass = kit.Group("grass").ConvertAll(NatureField.Kit);
                kitFlowers = kit.Group("flowers").ConvertAll(NatureField.Kit);
            }
            float half = Mathf.Max(game.Content.Map.Width, game.Content.Map.Height) * Grid.CellSize * 0.5f;
            Radius = Mathf.Max(96f, half * 1.42f);
            Step = Mathf.Max(1f, Radius / 96f) * (kitGrass != null ? 1.6f : 1f);
            Rebuild();
        }

        Layer NewLayer(string impostor)
        {
            var layer = new Layer { Material = Palette.Get($"I_{impostor}_side"), Top = Palette.Get($"I_{impostor}_top") };
            if (ModelLibrary.HasImpostor(impostor))
            {
                var info = ModelLibrary.Manifest.Obj("impostors").Obj(impostor);
                layer.Aspect = (float)(info.Num("width") / Mathf.Max(0.01f, (float)info.Num("height")));
            }
            return layer;
        }

        /// <summary>Re-scatters grass (call when buildings, roads or decorations change).</summary>
        public void Rebuild()
        {
            if (grass.Material == null && kitGrass == null) return;
            kitSets.Clear();
            // Shared materials, so the season tint reaches the grass and the farm crops alike.
            foreach (var layer in new[] { grass, flowers })
            {
                layer.Side = layer.Material != null ? new InstanceSet(mesh, 0, layer.Material, false) : null;
                layer.TopSet = layer.Top != null ? new InstanceSet(flat, 0, layer.Top, false) : null;
            }
            var used = WorldView.OccupiedCells(game);
            var center = Grid.CellCenter((game.Content.Map.Width - 1) * 0.5f, (game.Content.Map.Height - 1) * 0.5f);
            var rng = new System.Random(4242);
            int index = 0;
            for (float z = -Radius; z <= Radius; z += Step)
            {
                for (float x = -Radius; x <= Radius; x += Step)
                {
                    float jx = ((float)rng.NextDouble() - 0.5f) * Step, jz = ((float)rng.NextDouble() - 0.5f) * Step;
                    double roll = rng.NextDouble();
                    float size = 1.0f + (float)rng.NextDouble() * 0.7f;
                    float yaw = (float)rng.NextDouble() * 180f;
                    var p = new Vector3(center.x + x + jx, 0, center.z + z + jz);
                    float d = new Vector2(x, z).magnitude;
                    if (d > Radius) continue;

                    var (cx, cy) = Grid.CellAt(p);
                    var type = ground.CellType(cx, cy);
                    float density = type switch
                    {
                        TerrainType.Grass => 0.8f,
                        TerrainType.Forest => 0.15f,
                        TerrainType.Rock => 0.12f,
                        TerrainType.Mountain => 0.1f,
                        _ => 0f,
                    };
                    // Thinner far from the valley centre (it is barely visible there).
                    density *= Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(Radius * 0.66f, Radius, d));
                    if (roll > density) continue;
                    if (used.Contains((cx, cy))) continue;
                    float y = ground.HeightAt(p);
                    if (y < WorldTerrain.WaterLevel + 0.12f || y > 14f) continue;
                    p.y = y - 0.02f;

                    bool flower = roll < density * 0.05f;
                    if (kitGrass != null)
                    {
                        var models = flower && kitFlowers.Count > 0 ? kitFlowers : kitGrass;
                        AddKit(models[rng.Next(models.Count)], p, (flower ? 0.35f : 0.4f) * size, yaw * 2f, index++);
                        continue;
                    }
                    var layer = flower ? flowers : grass;
                    float h = flower ? 0.32f * size : 0.3f * size;
                    var m = Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), new Vector3(h * layer.Aspect, h, h * layer.Aspect));
                    layer.Side?.Add(m, index);
                    layer.TopSet?.Add(m, index);
                    index++;
                }
            }
            foreach (var layer in new[] { grass, flowers })
            {
                layer.Side?.Build();
                layer.TopSet?.Build();
            }
            foreach (var set in kitSets.Values) set.Build();
        }

        /// <summary>One clump of the packs, scaled to the height wanted and standing on the ground.</summary>
        void AddKit(NatureField.KitModel model, Vector3 p, float height, float yaw, int index)
        {
            var b = model.Bounds;
            if (model.Near.Count == 0 || b.size.y < 0.001f) return;
            float scale = height / b.size.y;
            var root = Matrix4x4.TRS(p - Vector3.up * (b.min.y * scale), Quaternion.Euler(0, yaw, 0), Vector3.one * scale);
            foreach (var part in model.Near)
            {
                var key = (part.Mesh, part.SubMesh, part.Material);
                if (!kitSets.TryGetValue(key, out var set)) kitSets[key] = set = new InstanceSet(part.Mesh, part.SubMesh, part.Material, false);
                set.Add(root * part.Local, index);
            }
        }

        void Update()
        {
            if (kitSets.Count > 0)
            {
                foreach (var set in kitSets.Values) set.Draw();
                return;
            }
            if (mesh == null || grass?.Side == null) return;
            // The strategy camera looks down, so each tuft is a top-view card plus two crossed side cards.
            grass.TopSet?.Draw();
            grass.Side?.Draw();
            flowers.TopSet?.Draw();
            flowers.Side?.Draw();
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
