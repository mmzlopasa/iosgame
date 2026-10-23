using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Founders
{
    /// <summary>
    /// One mesh + material drawn many times with GPU instancing. Instances are sorted along a
    /// Z-order curve before they are cut into batches of 1023, so every batch covers a small
    /// patch of land and Unity can cull it as a whole when it is off screen.
    /// Single instances can be hidden (and shown again) without rebuilding anything.
    /// </summary>
    public sealed class InstanceSet
    {
        const int BatchSize = 1023;
        const float Cell = 16f;

        public readonly Mesh Mesh;
        public readonly int SubMesh;
        public readonly Material Material;
        readonly ShadowCastingMode shadows;
        readonly List<(Matrix4x4 m, int owner)> pending = new List<(Matrix4x4, int)>();
        readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
        readonly List<Matrix4x4[]> visible = new List<Matrix4x4[]>();
        readonly List<RenderParams> renderParams = new List<RenderParams>();
        readonly Dictionary<int, List<int>> slots = new Dictionary<int, List<int>>();
        static readonly Matrix4x4 Hidden = Matrix4x4.TRS(new Vector3(0, -1000f, 0), Quaternion.identity, Vector3.zero);

        public InstanceSet(Mesh mesh, int subMesh, Material material, bool castShadows)
        {
            Mesh = mesh;
            SubMesh = subMesh;
            Material = material;
            shadows = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (material != null) material.enableInstancing = true;
        }

        public int Count => pending.Count;

        public void Add(Matrix4x4 m, int owner) => pending.Add((m, owner));

        static uint Spread(uint x)
        {
            x &= 0xffff;
            x = (x | (x << 8)) & 0x00ff00ff;
            x = (x | (x << 4)) & 0x0f0f0f0f;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;
            return x;
        }

        static uint Morton(Vector3 p) =>
            Spread((uint)Mathf.Clamp(Mathf.FloorToInt(p.x / Cell) + 512, 0, 65535)) |
            (Spread((uint)Mathf.Clamp(Mathf.FloorToInt(p.z / Cell) + 512, 0, 65535)) << 1);

        /// <summary>Sorts the added instances into culled batches. Call once after adding.</summary>
        public void Build()
        {
            pending.Sort((a, b) => Morton(a.m.GetColumn(3)).CompareTo(Morton(b.m.GetColumn(3))));
            batches.Clear();
            visible.Clear();
            renderParams.Clear();
            slots.Clear();
            var meshBounds = Mesh.bounds;
            for (int start = 0; start < pending.Count; start += BatchSize)
            {
                int n = Mathf.Min(BatchSize, pending.Count - start);
                var arr = new Matrix4x4[n];
                var bounds = new Bounds();
                for (int k = 0; k < n; k++)
                {
                    var (m, owner) = pending[start + k];
                    arr[k] = m;
                    var c = m.MultiplyPoint3x4(meshBounds.center);
                    float r = meshBounds.extents.magnitude * m.lossyScale.magnitude * 0.6f;
                    var b = new Bounds(c, Vector3.one * (2f * r));
                    if (k == 0) bounds = b; else bounds.Encapsulate(b);
                    if (!slots.TryGetValue(owner, out var list)) slots[owner] = list = new List<int>(2);
                    list.Add(batches.Count << 10 | k);
                }
                batches.Add(arr);
                visible.Add((Matrix4x4[])arr.Clone());
                renderParams.Add(new RenderParams(Material)
                {
                    shadowCastingMode = shadows,
                    receiveShadows = true,
                    worldBounds = bounds,
                    layer = 0,
                });
            }
        }

        public void SetHidden(int owner, bool hidden)
        {
            if (!slots.TryGetValue(owner, out var list)) return;
            foreach (int packed in list)
            {
                int b = packed >> 10, k = packed & 1023;
                visible[b][k] = hidden ? Hidden : batches[b][k];
            }
        }

        public void Draw()
        {
            if (Material == null) return;
            for (int i = 0; i < visible.Count; i++)
                Graphics.RenderMeshInstanced(renderParams[i], Mesh, SubMesh, visible[i], visible[i].Length);
        }
    }

    /// <summary>
    /// Trees, rocks and undergrowth of the whole valley (thousands of objects) drawn with GPU
    /// instancing instead of GameObjects. Trees close to the camera are hidden so a low camera
    /// never looks into a crown, and anything standing where the player builds disappears.
    /// </summary>
    public class NatureField : MonoBehaviour
    {
        internal sealed class Part
        {
            public Mesh Mesh;
            public int SubMesh;
            public Material Material;
            public Matrix4x4 Local;
            public bool Shadows;
        }

        sealed class Item
        {
            public int Index;
            public Vector3 Position;
            public float Height;
            /// <summary>Half the width on the ground (a trunk for trees): how close people can stand.</summary>
            public float Radius;
            public string Id;
            public int Cx, Cy;
            public bool Tree;
            public bool NearCamera, Blocked;
            public readonly List<InstanceSet> Sets = new List<InstanceSet>(4);
            /// <summary>The simple far version of a kit tree (its last LOD), drawn instead of <see cref="Sets"/> from afar.</summary>
            public List<InstanceSet> FarSets;
            public bool Far;
        }

        /// <summary>A kit prefab cut into instanced parts: the detailed model and its far LOD.</summary>
        internal sealed class KitModel
        {
            public List<Part> Near = new List<Part>();
            public List<Part> Far = new List<Part>();
            public Bounds Bounds;
        }

        static readonly Dictionary<GameObject, KitModel> kitModels = new Dictionary<GameObject, KitModel>();
        /// <summary>Kit trees further than this from the camera are drawn with their simple far model.</summary>
        public const float FarDistance = 55f;

        readonly Dictionary<(Mesh, int, Material, bool), InstanceSet> sets = new Dictionary<(Mesh, int, Material, bool), InstanceSet>();
        readonly List<InstanceSet> drawOrder = new List<InstanceSet>();
        readonly Dictionary<string, List<Part>> models = new Dictionary<string, List<Part>>();
        readonly List<Item> items = new List<Item>();
        readonly List<Item> trees = new List<Item>();
        readonly Dictionary<(int, int), List<Item>> byCell = new Dictionary<(int, int), List<Item>>();
        float nextCameraCheck;

        public int Count => items.Count;

        InstanceSet Set(Mesh mesh, int sub, Material material, bool shadows)
        {
            var key = (mesh, sub, material, shadows);
            if (!sets.TryGetValue(key, out var set))
            {
                sets[key] = set = new InstanceSet(mesh, sub, material, shadows);
                drawOrder.Add(set);
            }
            return set;
        }

        Item NewItem(Vector3 position, float height, int cx, int cy, bool tree, string id = null, float radius = 0f)
        {
            var item = new Item { Index = items.Count, Position = position, Height = height, Cx = cx, Cy = cy, Tree = tree, Id = id, Radius = radius };
            items.Add(item);
            if (tree) trees.Add(item);
            if (!byCell.TryGetValue((cx, cy), out var list)) byCell[(cx, cy)] = list = new List<Item>();
            list.Add(item);
            return item;
        }

        void Add(Item item, InstanceSet set, Matrix4x4 m)
        {
            set.Add(m, item.Index);
            if (!item.Sets.Contains(set)) item.Sets.Add(set);
        }

        /// <summary>A billboard tree (three crossed photo cards and a canopy card, as <see cref="ModelLibrary.Tree"/>).</summary>
        public void AddTree(string kind, Vector3 position, float height, float yaw, int cx, int cy)
        {
            if (!ModelLibrary.HasImpostor(kind)) return;
            var info = ModelLibrary.Manifest.Obj("impostors").Obj(kind);
            float w = (float)info.Num("width") * height / Mathf.Max(0.01f, (float)info.Num("height"));
            var item = NewItem(position, height, cx, cy, true, kind, TrunkRadius);
            var quad = ModelLibrary.Quad();
            var root = Matrix4x4.TRS(position, Quaternion.Euler(0, yaw, 0), Vector3.one);
            for (int i = 0; i < 3; i++)
            {
                var m = Palette.Get($"I_{kind}_{(i == 1 ? "side2" : "side")}");
                Add(item, Set(quad, 0, m, true), root * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, i * 60f, 0), new Vector3(w, height, 1f)));
            }
            var top = Palette.Get($"I_{kind}_top");
            Add(item, Set(quad, 0, top, true),
                root * Matrix4x4.TRS(new Vector3(0, height * 0.62f, 0), Quaternion.Euler(90, 0, 0), new Vector3(w, w, 1f)));
        }

        /// <summary>A scanned model (rock, shrub, stump…), optionally with one material for all its parts.</summary>
        public void AddModel(string id, Vector3 position, float scale, float yaw, int cx, int cy, bool castShadows = true,
                             Material material = null, Vector3? axisScale = null)
        {
            var parts = Parts(id);
            if (parts == null) return;
            var size = ModelLibrary.Size(id) * scale;
            var axes = axisScale ?? Vector3.one;
            var item = NewItem(position, size.y * axes.y, cx, cy, false, id, 0.5f * Mathf.Max(size.x * axes.x, size.z * axes.z));
            var root = Matrix4x4.TRS(position, Quaternion.Euler(0, yaw, 0), (axisScale ?? Vector3.one) * scale);
            foreach (var p in parts)
                Add(item, Set(p.Mesh, p.SubMesh, material != null ? material : p.Material, castShadows && p.Shadows), root * p.Local);
        }

        List<Part> Parts(string id)
        {
            if (models.TryGetValue(id, out var parts)) return parts;
            var prefab = ModelLibrary.Prefab(id);
            if (prefab == null) return models[id] = null;
            parts = new List<Part>();
            var mats = ModelLibrary.Materials(id);
            var rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var local = rootInverse * mf.transform.localToWorldMatrix;
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                    parts.Add(new Part
                    {
                        Mesh = mf.sharedMesh, SubMesh = i, Local = local, Shadows = true,
                        Material = mats != null && i < mats.Length ? mats[i] : Palette.Get("Earth"),
                    });
            }
            return models[id] = parts;
        }

        // MARK: - Nature packs (Ultimate Nature PRO and Starter)

        public enum Fit { Height, Width }

        internal static KitModel Kit(GameObject prefab)
        {
            if (kitModels.TryGetValue(prefab, out var model)) return model;
            model = new KitModel();
            var kit = NatureKit.Current;
            var rootInverse = prefab.transform.worldToLocalMatrix;
            var lod = prefab.GetComponentInChildren<LODGroup>(true);
            var lods = lod != null ? lod.GetLODs() : null;
            IEnumerable<Renderer> near, far = null;
            if (lods != null && lods.Length > 0)
            {
                near = lods[0].renderers;
                if (lods.Length > 1) far = lods[lods.Length - 1].renderers;
            }
            else near = prefab.GetComponentsInChildren<MeshRenderer>(true);
            bool first = true;
            void Collect(IEnumerable<Renderer> renderers, List<Part> into, bool measure)
            {
                if (renderers == null) return;
                foreach (var r in renderers)
                {
                    if (r == null || !(r is MeshRenderer)) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var local = rootInverse * r.transform.localToWorldMatrix;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                    {
                        var src = i < mats.Length ? mats[i] : null;
                        var mat = kit != null ? kit.Convert(src) : src;
                        if (mat == null) continue;
                        into.Add(new Part { Mesh = mf.sharedMesh, SubMesh = i, Local = local, Material = mat, Shadows = r.shadowCastingMode != ShadowCastingMode.Off });
                    }
                    if (!measure) continue;
                    var b = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = local.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        if (first) { model.Bounds = new Bounds(corner, Vector3.zero); first = false; }
                        else model.Bounds.Encapsulate(corner);
                    }
                }
            }
            Collect(near, model.Near, true);
            Collect(far, model.Far, false);
            return kitModels[prefab] = model;
        }

        /// <summary>
        /// One object of the nature packs, scaled so its height (or width) is <paramref name="size"/>
        /// metres, standing on the ground at <paramref name="position"/>. Trees switch to their far
        /// model away from the camera.
        /// </summary>
        public void AddKit(GameObject prefab, string id, Vector3 position, float size, Fit fit, float yaw, int cx, int cy,
                           bool tree = false, bool castShadows = true, Material tint = null, float sink = 0f)
        {
            if (prefab == null) return;
            var model = Kit(prefab);
            if (model.Near.Count == 0) return;
            var b = model.Bounds;
            float measure = fit == Fit.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            float scale = size / Mathf.Max(0.01f, measure);
            // The model's lowest point on the ground (a little sunk for rocks so they sit in it).
            var pos = position + Vector3.up * (-b.min.y * scale - sink * b.size.y * scale);
            float radius = tree ? TrunkRadius : 0.5f * Mathf.Max(b.size.x, b.size.z) * scale;
            var item = NewItem(position, b.size.y * scale, cx, cy, tree, id, radius);
            var root = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * scale);
            foreach (var p in model.Near)
                Add(item, Set(p.Mesh, p.SubMesh, tint != null ? tint : p.Material, castShadows && p.Shadows), root * p.Local);
            if (tree && model.Far.Count > 0)
            {
                item.FarSets = new List<InstanceSet>(model.Far.Count);
                foreach (var p in model.Far)
                {
                    var set = Set(p.Mesh, p.SubMesh, tint != null ? tint : p.Material, false);
                    set.Add(root * p.Local, item.Index);
                    if (!item.FarSets.Contains(set)) item.FarSets.Add(set);
                }
                item.Far = true;
            }
        }

        // MARK: - Work targets (where people cut, quarry and pick)

        /// <summary>Trunk radius of the billboard trees (people stand at the trunk, not at the crown).</summary>
        public const float TrunkRadius = 0.22f;

        /// <summary>A tree, rock or bush standing in a cell that someone can work at.</summary>
        public struct Target
        {
            public int Index;
            public Vector3 Position;
            public float Radius, Height;
            public string Id;
        }

        static bool IsRock(string id) => id != null && (id.StartsWith("sn_rock") || id.Contains("boulder") || id.StartsWith("k_rock") || id.StartsWith("rock_") || id == "kit_rock");
        static bool IsBush(string id) => id == "sn_bush" || id == "kit_berry_bush";

        /// <summary>
        /// What a job works at in a cell: "tree", "rock" or "bush". Returns the biggest one still
        /// standing (hidden objects, cut down or built over, do not count).
        /// </summary>
        public bool TryTarget(int cx, int cy, string kind, out Target target)
        {
            target = default;
            if (!byCell.TryGetValue((cx, cy), out var list)) return false;
            Item best = null;
            foreach (var item in list)
            {
                if (item.Blocked) continue;
                bool match = kind == "tree" ? item.Tree : kind == "rock" ? IsRock(item.Id) : kind == "bush" && IsBush(item.Id);
                if (!match) continue;
                if (best == null || item.Height > best.Height) best = item;
            }
            if (best == null) return false;
            target = new Target { Index = best.Index, Position = best.Position, Radius = best.Radius, Height = best.Height, Id = best.Id };
            return true;
        }

        /// <summary>
        /// True when a person can stand at <paramref name="p"/>: no trunk, rock or bush (other than
        /// <paramref name="ignore"/>) closer than their own radius plus <paramref name="clearance"/>.
        /// </summary>
        public bool Clear(Vector3 p, float clearance, int ignore = -1)
        {
            var (cx, cy) = Grid.CellAt(p);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!byCell.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                    foreach (var item in list)
                    {
                        if (item.Blocked || item.Index == ignore) continue;
                        if (!item.Tree && !IsRock(item.Id) && !IsBush(item.Id)) continue;
                        float r = (item.Tree ? TrunkRadius : item.Radius * 0.8f) + clearance;
                        float ddx = item.Position.x - p.x, ddz = item.Position.z - p.z;
                        if (ddx * ddx + ddz * ddz < r * r) return false;
                    }
                }
            return true;
        }

        /// <summary>Sorts everything into culled batches. Call after adding all objects.</summary>
        public void Build()
        {
            foreach (var set in drawOrder) set.Build();
            // Kit trees start with their far model; the camera check brings the detailed ones in.
            foreach (var item in trees)
                if (item.FarSets != null) Apply(item);
        }

        /// <summary>
        /// Shows how much of each cell is still standing: 1 = everything, 0 = nothing (built on, cut
        /// down or mined out). In between, the cell's trees and rocks disappear one by one.
        /// </summary>
        public void SetStanding(Func<int, int, float> standing)
        {
            foreach (var kv in byCell) Stand(kv.Value, standing(kv.Key.Item1, kv.Key.Item2));
        }

        /// <summary>Like <see cref="SetStanding"/> for one cell (cheap: used while gatherers work).</summary>
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

        void Apply(Item item)
        {
            bool hidden = item.Blocked || item.NearCamera;
            if (item.FarSets == null)
            {
                foreach (var set in item.Sets) set.SetHidden(item.Index, hidden);
                return;
            }
            foreach (var set in item.Sets) set.SetHidden(item.Index, hidden || item.Far);
            foreach (var set in item.FarSets) set.SetHidden(item.Index, hidden || !item.Far);
        }

        /// <summary>
        /// True when something of this height standing at <paramref name="p"/> would block the view:
        /// its crown is right in front of the lens, or it stands between a close camera and the
        /// middle of the view.
        /// </summary>
        public static bool Occludes(Vector3 c, Vector3 fwd, Vector3 p, float height)
        {
            float dx = c.x - p.x, dz = c.z - p.z;
            var crown = p + Vector3.up * height * 0.6f;
            if (dx * dx + dz * dz < 400f)
            {
                float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
                if ((c - crown).magnitude < height * 0.55f + 3f || (c.y < p.y + height + 1.5f && horizontal < 4f)) return true;
            }
            if (fwd.y < -0.1f && c.y - p.y < 22f)
            {
                float t = Vector3.Dot(crown - c, fwd);
                float groundT = (c.y - p.y) / -fwd.y; // where the view ray reaches this object's ground level
                if (t > 0f && t < groundT && (crown - (c + fwd * t)).magnitude < height * 0.4f + 1.5f) return true;
            }
            return false;
        }

        /// <summary>Keeps trees near the camera (close-up captures look at people working under them).</summary>
        public static bool KeepNearCamera;

        /// <summary>Hides trees whose crown would fill the screen when the camera flies low over them.</summary>
        void UpdateNearCamera()
        {
            if (Time.unscaledTime < nextCameraCheck) return;
            nextCameraCheck = Time.unscaledTime + 0.15f;
            var cam = Camera.main;
            if (cam == null) return;
            var c = cam.transform.position;
            var fwd = cam.transform.forward;
            const float far2 = FarDistance * FarDistance;
            foreach (var tv in trees)
            {
                bool hide = !KeepNearCamera && Occludes(c, fwd, tv.Position, tv.Height);
                bool far = tv.FarSets != null && (tv.Position - c).sqrMagnitude > far2;
                if (hide == tv.NearCamera && far == tv.Far) continue;
                tv.NearCamera = hide;
                tv.Far = far;
                Apply(tv);
            }
        }

        void Update()
        {
            UpdateNearCamera();
            foreach (var set in drawOrder) set.Draw();
        }
    }
}
