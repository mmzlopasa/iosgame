using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Founders
{
    /// <summary>
    /// One mesh + material drawn many times with GPU instancing. Instances are grouped by patch of
    /// land (<see cref="Tile"/> metres square) and cut into batches of at most 1023, so every batch
    /// covers a small patch that Unity culls as a whole when it is off screen.
    /// Single instances can be hidden (and shown again) without rebuilding anything: hidden ones
    /// are left out of what is sent to the GPU, so they cost nothing to draw.
    /// </summary>
    public sealed class InstanceSet
    {
        const int BatchSize = 1023;
        const float Tile = 40f;

        sealed class Batch
        {
            public Matrix4x4[] All;
            public bool[] Hidden;
            public Matrix4x4[] Shown;
            public int Count;
            public bool Dirty;
            public RenderParams Params;
            public Bounds Bounds;
        }

        public readonly Mesh Mesh;
        public readonly int SubMesh;
        public readonly Material Material;
        readonly ShadowCastingMode shadows;
        /// <summary>Batches further than this from the camera are not drawn (0 = always drawn).</summary>
        public float MaxDistance;
        readonly List<(Matrix4x4 m, int owner)> pending = new List<(Matrix4x4, int)>();
        readonly List<Batch> batches = new List<Batch>();
        readonly Dictionary<int, List<int>> slots = new Dictionary<int, List<int>>();

        public InstanceSet(Mesh mesh, int subMesh, Material material, ShadowCastingMode shadows)
        {
            Mesh = mesh;
            SubMesh = subMesh;
            Material = material;
            this.shadows = shadows;
            if (material != null) material.enableInstancing = true;
        }

        public InstanceSet(Mesh mesh, int subMesh, Material material, bool castShadows)
            : this(mesh, subMesh, material, castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off) { }

        public int Count => pending.Count;

        public void Add(Matrix4x4 m, int owner) => pending.Add((m, owner));

        static long TileKey(Vector3 p) =>
            ((long)(Mathf.FloorToInt(p.z / Tile) + 4096) << 20) | (long)(Mathf.FloorToInt(p.x / Tile) + 4096);

        /// <summary>Sorts the added instances into culled batches. Call once after adding.</summary>
        public void Build()
        {
            pending.Sort((a, b) => TileKey(a.m.GetColumn(3)).CompareTo(TileKey(b.m.GetColumn(3))));
            batches.Clear();
            slots.Clear();
            var meshBounds = Mesh.bounds;
            int start = 0;
            while (start < pending.Count)
            {
                long tile = TileKey(pending[start].m.GetColumn(3));
                int n = 1;
                while (n < BatchSize && start + n < pending.Count && TileKey(pending[start + n].m.GetColumn(3)) == tile) n++;
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
                batches.Add(new Batch
                {
                    All = arr,
                    Hidden = new bool[n],
                    Shown = (Matrix4x4[])arr.Clone(),
                    Count = n,
                    Bounds = bounds,
                    Params = new RenderParams(Material)
                    {
                        shadowCastingMode = shadows,
                        receiveShadows = true,
                        worldBounds = bounds,
                        layer = 0,
                    },
                });
                start += n;
            }
        }

        public void SetHidden(int owner, bool hidden)
        {
            if (!slots.TryGetValue(owner, out var list)) return;
            foreach (int packed in list)
            {
                var b = batches[packed >> 10];
                int k = packed & 1023;
                if (b.Hidden[k] == hidden) continue;
                b.Hidden[k] = hidden;
                b.Dirty = true;
            }
        }

        public void Draw(Vector3 camera)
        {
            if (Material == null) return;
            float max2 = MaxDistance * MaxDistance;
            foreach (var b in batches)
            {
                if (b.Dirty)
                {
                    int n = 0;
                    for (int k = 0; k < b.All.Length; k++)
                        if (!b.Hidden[k]) b.Shown[n++] = b.All[k];
                    b.Count = n;
                    b.Dirty = false;
                }
                if (b.Count == 0) continue;
                if (MaxDistance > 0f && b.Bounds.SqrDistance(camera) > max2) continue;
                Graphics.RenderMeshInstanced(b.Params, Mesh, SubMesh, b.Shown, b.Count);
            }
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
            /// <summary>The simple far version of a kit object (its last LOD), drawn instead of <see cref="Sets"/> from afar.</summary>
            public List<InstanceSet> FarSets;
            /// <summary>A kit tree's flat card, drawn instead of <see cref="FarSets"/> far away.</summary>
            public List<InstanceSet> CardSets;
            /// <summary>The last LOD casting only the shadow of a kit tree close to the camera.</summary>
            public List<InstanceSet> ShadowSets;
            /// <summary>Kit objects only: distances (metres from the camera) where it changes to far, to card and disappears.</summary>
            public float FarAt, CardAt, MaxAt;
            /// <summary>0 = detailed, 1 = far model, 2 = card, 3 = not drawn.</summary>
            public int Tier;
            public bool Shadow;
        }

        /// <summary>A kit prefab cut into instanced parts: the detailed model and its far LOD.</summary>
        internal sealed class KitModel
        {
            public List<Part> Near = new List<Part>();
            public List<Part> Far = new List<Part>();
            /// <summary>The flat card of a tree ("&lt;tree&gt;_Card" in the pack), for far away.</summary>
            public List<Part> Card = new List<Part>();
            public Bounds Bounds;
        }

        static readonly Dictionary<GameObject, KitModel> kitModels = new Dictionary<GameObject, KitModel>();
        /// <summary>Kit trees further than this from the camera are drawn with their simple far model.</summary>
        public const float FarDistance = 40f;
        /// <summary>Kit trees further than this are flat cards (and in the fog).</summary>
        public const float CardDistance = 95f;
        /// <summary>Nothing of the packs is drawn beyond this (the fog has hidden it).</summary>
        public const float DrawDistance = 240f;
        /// <summary>Kit trees closer than this cast a shadow (a little beyond the shadow distance).</summary>
        const float ShadowReach = 65f;

        readonly List<InstanceSet> drawOrder = new List<InstanceSet>();
        readonly Dictionary<string, List<Part>> models = new Dictionary<string, List<Part>>();
        readonly List<Item> items = new List<Item>();
        readonly List<Item> trees = new List<Item>();
        /// <summary>Objects of the packs, which change model with the distance to the camera.</summary>
        readonly List<Item> kitItems = new List<Item>();
        Vector3 lastCamera = new Vector3(1e6f, 0, 0);
        Vector3 lastForward;
        bool forceCheck = true;
        readonly Dictionary<(int, int), List<Item>> byCell = new Dictionary<(int, int), List<Item>>();
        float nextCameraCheck;

        public int Count => items.Count;

        InstanceSet Set(Mesh mesh, int sub, Material material, bool shadows) =>
            Set(mesh, sub, material, shadows ? ShadowCastingMode.On : ShadowCastingMode.Off);

        readonly Dictionary<(Mesh, int, Material, ShadowCastingMode), InstanceSet> modeSets = new Dictionary<(Mesh, int, Material, ShadowCastingMode), InstanceSet>();

        InstanceSet Set(Mesh mesh, int sub, Material material, ShadowCastingMode mode)
        {
            var key = (mesh, sub, material, mode);
            if (!modeSets.TryGetValue(key, out var set))
            {
                modeSets[key] = set = new InstanceSet(mesh, sub, material, mode);
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
                // The second LOD up close: from the strategy camera it looks the same as the first
                // at a fraction of the triangles (the first LOD of a tree is ~10 000).
                near = lods[lods.Length > 2 ? 1 : 0].renderers;
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
            var card = kit != null ? kit.Prefab(prefab.name + "_Card") : null;
            if (card != null)
            {
                rootInverse = card.transform.worldToLocalMatrix;
                Collect(card.GetComponentsInChildren<MeshRenderer>(true), model.Card, false);
            }
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
            float height = b.size.y * scale;
            // Trees cast their shadow with their simplest model (below); other objects with their own.
            bool shadowOwn = castShadows && !tree;
            foreach (var p in model.Near)
                Add(item, Set(p.Mesh, p.SubMesh, tint != null ? tint : p.Material, shadowOwn && p.Shadows), root * p.Local);
            List<InstanceSet> Sets(List<Part> parts, ShadowCastingMode mode)
            {
                var list = new List<InstanceSet>(parts.Count);
                foreach (var p in parts)
                {
                    var set = Set(p.Mesh, p.SubMesh, tint != null ? tint : p.Material, mode);
                    set.Add(root * p.Local, item.Index);
                    if (!list.Contains(set)) list.Add(set);
                }
                return list;
            }
            if (model.Far.Count > 0) item.FarSets = Sets(model.Far, shadowOwn ? ShadowCastingMode.On : ShadowCastingMode.Off);
            if (tree)
            {
                if (model.Card.Count > 0)
                {
                    // The card is a little shorter than the tree it stands for: scale it to the same height.
                    var cb = Kit(KitCardPrefab(prefab)).Bounds;
                    float k = cb.size.y > 0.01f ? b.size.y / cb.size.y : 1f;
                    var cardRoot = Matrix4x4.TRS(pos + Vector3.up * (-cb.min.y * k * scale + b.min.y * scale), Quaternion.Euler(0, yaw, 0), Vector3.one * scale * k);
                    item.CardSets = new List<InstanceSet>(model.Card.Count);
                    foreach (var p in model.Card)
                    {
                        var set = Set(p.Mesh, p.SubMesh, tint != null ? tint : p.Material, false);
                        set.Add(cardRoot * p.Local, item.Index);
                        if (!item.CardSets.Contains(set)) item.CardSets.Add(set);
                    }
                }
                if (castShadows && model.Far.Count > 0) item.ShadowSets = Sets(model.Far, ShadowCastingMode.ShadowsOnly);
                item.FarAt = FarDistance;
                item.CardAt = CardDistance;
                item.MaxAt = DrawDistance;
            }
            else
            {
                // Small things are not seen from afar; boulders and cliffs are.
                item.FarAt = height < 0.7f ? 14f : 26f;
                item.CardAt = DrawDistance;
                item.MaxAt = height < 0.4f ? 55f : height < 1.5f ? 95f : 170f;
            }
            kitItems.Add(item);
        }

        /// <summary>The card prefab of a kit tree (exists when <see cref="KitModel.Card"/> has parts).</summary>
        static GameObject KitCardPrefab(GameObject tree) => NatureKit.Current.Prefab(tree.name + "_Card");

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
            // Kit objects start hidden; the camera check brings in what is near enough, with the right model.
            foreach (var item in kitItems)
            {
                item.Tier = 3;
                Apply(item);
            }
            forceCheck = true;
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
            if (item.MaxAt <= 0f)
            {
                foreach (var set in item.Sets) set.SetHidden(item.Index, hidden);
                return;
            }
            // Which model stands for the object at this distance (falling back to the next one that exists).
            int tier = item.Tier;
            if (tier == 2 && item.CardSets == null) tier = 1;
            if (tier == 1 && item.FarSets == null) tier = 0;
            foreach (var set in item.Sets) set.SetHidden(item.Index, hidden || tier != 0);
            if (item.FarSets != null) foreach (var set in item.FarSets) set.SetHidden(item.Index, hidden || tier != 1);
            if (item.CardSets != null) foreach (var set in item.CardSets) set.SetHidden(item.Index, hidden || tier != 2);
            if (item.ShadowSets != null) foreach (var set in item.ShadowSets) set.SetHidden(item.Index, item.Blocked || !item.Shadow);
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

        /// <summary>
        /// Hides trees whose crown would fill the screen when the camera flies low over them, and
        /// gives every object of the packs the model that fits its distance (or none, far away).
        /// </summary>
        void UpdateNearCamera()
        {
            if (Time.unscaledTime < nextCameraCheck) return;
            nextCameraCheck = Time.unscaledTime + 0.15f;
            var cam = Camera.main;
            if (cam == null) return;
            var c = cam.transform.position;
            var fwd = cam.transform.forward;
            if (!forceCheck && (c - lastCamera).sqrMagnitude < 0.5f && Vector3.Dot(fwd, lastForward) > 0.9995f) return;
            forceCheck = false;
            lastCamera = c;
            lastForward = fwd;
            foreach (var tv in trees)
            {
                if (tv.MaxAt > 0f) continue;
                bool hide = !KeepNearCamera && Occludes(c, fwd, tv.Position, tv.Height);
                if (hide == tv.NearCamera) continue;
                tv.NearCamera = hide;
                Apply(tv);
            }
            foreach (var item in kitItems)
            {
                float d = (item.Position - c).magnitude;
                int tier = d < item.FarAt ? 0 : d < item.CardAt ? 1 : d < item.MaxAt ? 2 : 3;
                bool hide = item.Tree && tier < 3 && !KeepNearCamera && Occludes(c, fwd, item.Position, item.Height);
                bool shadow = item.ShadowSets != null && d < ShadowReach;
                if (tier == item.Tier && hide == item.NearCamera && shadow == item.Shadow) continue;
                item.Tier = tier;
                item.NearCamera = hide;
                item.Shadow = shadow;
                Apply(item);
            }
        }

        void Update()
        {
            UpdateNearCamera();
            var cam = Camera.main;
            var c = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var set in drawOrder) set.Draw(c);
        }
    }
}
