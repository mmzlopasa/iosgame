using System;
using System.Collections.Generic;
using Founders.Core;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Every tree, bush, flower and stone of the valley where <see cref="NaturePlan"/> says it grows,
    /// drawn as one MultiMesh per model. Things are hidden where the player builds (clearings) and
    /// thin out where gatherers work (standing share per cell). People ask it for the tree or rock
    /// to work at and for free ground to stand on.
    /// </summary>
    public sealed partial class Nature : Node3D
    {
        public const float TrunkRadius = 0.22f;
        const int Margin = 16;

        sealed class Item
        {
            public int Index;
            public Vector3 Position;
            public float Height, Radius;
            public int Cx, Cy;
            public bool Tree;
            public string Id;
            public MultiMesh Set;
            public int Slot;
            public Transform3D Xform;
            public bool Blocked, Cleared;
            public bool Gone => Blocked || Cleared;
        }

        readonly List<Item> items = new List<Item>();
        readonly Dictionary<(int, int), List<Item>> byCell = new Dictionary<(int, int), List<Item>>();
        ShaderMaterial material;

        public int Count => items.Count;

        public void Build(Terrain ground, Func<int, int, bool> berries)
        {
            Name = "Nature";
            var map = ground.Map;
            material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/nature.gdshader") };
            var plan = NaturePlan.Make(map.Width, map.Height, Margin, ground.CellType, berries,
                (x, zUnity) => ground.HeightAt(x, -zUnity));

            // Collect the transforms per model first, then make one MultiMesh for each.
            var groups = new Dictionary<(string, int), List<(Item item, Transform3D xf, Color tint)>>();
            foreach (var p in plan.Items)
            {
                string group = p.Group;
                var mesh = NatureModels.Get(group, p.Variant);
                var aabb = mesh.GetAabb();
                float measure = NatureModels.ByHeight(group) ? Mathf.Max(0.01f, aabb.End.Y) : Mathf.Max(0.01f, Mathf.Max(aabb.Size.X, aabb.Size.Z));
                float scale = p.Size / measure;
                var pos = new Vector3(p.X, 0f, -p.Z);
                pos.Y = p.OnWater ? Terrain.WaterLevel + 0.01f : ground.HeightAt(pos) + p.Lift;
                pos.Y += -aabb.Position.Y * scale - p.Sink * aabb.Size.Y * scale;
                var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(p.LeanX), Mathf.DegToRad(-p.Yaw), Mathf.DegToRad(p.LeanZ))).Scaled(Vector3.One * scale);
                var item = new Item
                {
                    Index = items.Count, Position = new Vector3(p.X, pos.Y, -p.Z), Height = aabb.Size.Y * scale, Cx = p.Cx, Cy = p.Cy,
                    Tree = p.Tree, Id = p.Id, Radius = p.Tree ? TrunkRadius : 0.5f * Mathf.Max(aabb.Size.X, aabb.Size.Z) * scale,
                };
                items.Add(item);
                if (!byCell.TryGetValue((p.Cx, p.Cy), out var list)) byCell[(p.Cx, p.Cy)] = list = new List<Item>();
                list.Add(item);
                var tint = p.Tint switch
                {
                    "iron" => new Color(0.95f, 0.58f, 0.42f),
                    "copper" => new Color(0.62f, 0.85f, 0.7f),
                    "berry" => new Color(0.95f, 0.3f, 0.35f),
                    _ => Colors.White,
                };
                var key = (group, Math.Abs(p.Variant) % 3);
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<(Item, Transform3D, Color)>();
                g.Add((item, new Transform3D(basis, pos), tint));
            }
            foreach (var kv in groups)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = NatureModels.Get(kv.Key.Item1, kv.Key.Item2),
                    InstanceCount = kv.Value.Count,
                };
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var (item, xf, tint) = kv.Value[i];
                    item.Set = mm;
                    item.Slot = i;
                    item.Xform = xf;
                    mm.SetInstanceTransform(i, xf);
                    mm.SetInstanceColor(i, tint);
                }
                bool small = kv.Key.Item1 is "grass" or "grass_tall" or "clover" or "flowers" or "fern" or "pebble" or "lily" or "mushroom" or "branch";
                AddChild(new MultiMeshInstance3D
                {
                    Name = $"{kv.Key.Item1}_{kv.Key.Item2}",
                    Multimesh = mm,
                    MaterialOverride = material,
                    CastShadow = small ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                    // Small plants vanish in the distance (nobody sees them from high up, the iPhone saves the work).
                    VisibilityRangeEnd = small ? 90f : 0f,
                });
            }
            GD.Print($"[FOUNDERS] nature: {items.Count} objects in {groups.Count} sets");
        }

        void Apply(Item item)
        {
            var xf = item.Gone ? new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.0001f), item.Xform.Origin - Vector3.Up * 50f) : item.Xform;
            item.Set.SetInstanceTransform(item.Slot, xf);
        }

        // ------------------------------------------------------------------ standing share (gatherers)

        public void SetStanding(Func<int, int, float> standing)
        {
            foreach (var kv in byCell) Stand(kv.Value, standing(kv.Key.Item1, kv.Key.Item2));
        }

        public void SetStandingCell(int x, int y, float f)
        {
            if (byCell.TryGetValue((x, y), out var list)) Stand(list, f);
        }

        void Stand(List<Item> list, float f)
        {
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                var item = list[i];
                bool b = f <= 0f || (float)i / n >= f;
                if (item.Blocked == b) continue;
                item.Blocked = b;
                Apply(item);
            }
        }

        // ------------------------------------------------------------------ clearings

        public struct Clearing
        {
            public Vector2 Min, Max;
            public float Margin, Ragged;
        }

        readonly HashSet<Item> cleared = new HashSet<Item>();

        static float CrownRadius(Item item) => Mathf.Min(2.4f, 0.24f * item.Height);

        public void SetClearings(List<Clearing> clearings)
        {
            var now = new HashSet<Item>();
            foreach (var z in clearings)
            {
                float reach = z.Margin + z.Ragged + 2.4f;
                var (x0, y0) = Grid.CellAt(new Vector3(z.Min.X - reach, 0f, z.Min.Y - reach));
                var (x1, y1) = Grid.CellAt(new Vector3(z.Max.X + reach, 0f, z.Max.Y + reach));
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        if (!byCell.TryGetValue((cx, cy), out var list)) continue;
                        foreach (var item in list)
                        {
                            if (now.Contains(item)) continue;
                            float dx = Mathf.Max(0f, Mathf.Max(z.Min.X - item.Position.X, item.Position.X - z.Max.X));
                            float dz = Mathf.Max(0f, Mathf.Max(z.Min.Y - item.Position.Z, item.Position.Z - z.Max.Y));
                            float size = item.Tree ? CrownRadius(item) : item.Height < 0.45f ? 0.15f : item.Radius * 0.8f;
                            float give = z.Ragged * ((((uint)item.Index * 2654435761u) >> 8) % 1000u / 1000f);
                            float r = z.Margin + size - give;
                            if (r > 0f && dx * dx + dz * dz < r * r) now.Add(item);
                        }
                    }
            }
            foreach (var item in cleared)
                if (!now.Contains(item)) { item.Cleared = false; Apply(item); }
            foreach (var item in now)
                if (!item.Cleared) { item.Cleared = true; Apply(item); }
            cleared.Clear();
            cleared.UnionWith(now);
        }

        // ------------------------------------------------------------------ for people

        public struct Target
        {
            public int Index;
            public Vector3 Position;
            public float Radius, Height;
            public string Id;
        }

        static bool IsRock(string id) => id == "kit_rock" || id == "kit_cliff" || id == "kit_stone";
        static bool IsBush(string id) => id == "kit_berry" || id == "kit_berry_bush";

        /// <summary>What a job works at in a cell: "tree", "rock" or "bush" (the biggest one still standing).</summary>
        public bool TryTarget(int cx, int cy, string kind, out Target target)
        {
            target = default;
            if (!byCell.TryGetValue((cx, cy), out var list)) return false;
            Item best = null;
            foreach (var item in list)
            {
                if (item.Gone) continue;
                bool match = kind == "tree" ? item.Tree : kind == "rock" ? IsRock(item.Id) : kind == "bush" && IsBush(item.Id);
                if (!match) continue;
                if (best == null || item.Height > best.Height) best = item;
            }
            if (best == null) return false;
            target = new Target { Index = best.Index, Position = best.Position, Radius = best.Radius, Height = best.Height, Id = best.Id };
            return true;
        }

        /// <summary>True when a person can stand at <paramref name="p"/> (no trunk, rock or bush in the way).</summary>
        public bool Clear(Vector3 p, float clearance, int ignore = -1)
        {
            var (cx, cy) = Grid.CellAt(p);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!byCell.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                    foreach (var item in list)
                    {
                        if (item.Gone || item.Index == ignore) continue;
                        if (!item.Tree && !IsRock(item.Id) && !IsBush(item.Id)) continue;
                        float r = (item.Tree ? TrunkRadius : item.Radius * 0.8f) + clearance;
                        float ddx = item.Position.X - p.X, ddz = item.Position.Z - p.Z;
                        if (ddx * ddx + ddz * ddz < r * r) return false;
                    }
                }
            return true;
        }

        public void SetSeason(Color leafTint, float snow, float wind, float autumn)
        {
            if (material == null) return;
            material.SetShaderParameter("autumn", autumn);
            material.SetShaderParameter("leaf_tint", new Vector3(leafTint.R, leafTint.G, leafTint.B));
            material.SetShaderParameter("snow", snow);
            material.SetShaderParameter("wind", wind);
        }
    }
}
