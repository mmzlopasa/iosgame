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
        sealed class Part
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
            public int Cx, Cy;
            public bool Tree;
            public bool NearCamera, Blocked;
            public readonly List<InstanceSet> Sets = new List<InstanceSet>(4);
        }

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

        Item NewItem(Vector3 position, float height, int cx, int cy, bool tree)
        {
            var item = new Item { Index = items.Count, Position = position, Height = height, Cx = cx, Cy = cy, Tree = tree };
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
            var item = NewItem(position, height, cx, cy, true);
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
            var item = NewItem(position, ModelLibrary.Size(id).y * scale, cx, cy, false);
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

        /// <summary>Sorts everything into culled batches. Call after adding all objects.</summary>
        public void Build()
        {
            foreach (var set in drawOrder) set.Build();
        }

        /// <summary>Hides everything standing on cells the player built on (and shows it again when cleared).</summary>
        public void SetBlocked(Func<int, int, bool> blocked)
        {
            foreach (var kv in byCell)
            {
                bool b = blocked(kv.Key.Item1, kv.Key.Item2);
                foreach (var item in kv.Value)
                {
                    if (item.Blocked == b) continue;
                    item.Blocked = b;
                    Apply(item);
                }
            }
        }

        void Apply(Item item)
        {
            bool hidden = item.Blocked || item.NearCamera;
            foreach (var set in item.Sets) set.SetHidden(item.Index, hidden);
        }

        /// <summary>Hides trees whose crown would fill the screen when the camera flies low over them.</summary>
        void UpdateNearCamera()
        {
            if (Time.unscaledTime < nextCameraCheck) return;
            nextCameraCheck = Time.unscaledTime + 0.15f;
            var cam = Camera.main;
            if (cam == null) return;
            var c = cam.transform.position;
            var fwd = cam.transform.forward;
            foreach (var tv in trees)
            {
                var p = tv.Position;
                float dx = c.x - p.x, dz = c.z - p.z;
                bool hide = false;
                var crown = p + Vector3.up * tv.Height * 0.6f;
                if (dx * dx + dz * dz < 400f)
                {
                    float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
                    hide = (c - crown).magnitude < tv.Height * 0.55f + 3f || (c.y < p.y + tv.Height + 1.5f && horizontal < 4f);
                }
                // Crowns standing between the camera and the middle of the view hide too.
                if (!hide && fwd.y < -0.1f && c.y - p.y < 22f)
                {
                    float t = Vector3.Dot(crown - c, fwd);
                    float groundT = (c.y - p.y) / -fwd.y; // where the view ray reaches this tree's ground level
                    if (t > 0f && t < groundT)
                    {
                        float off = (crown - (c + fwd * t)).magnitude;
                        hide = off < tv.Height * 0.4f + 1.5f;
                    }
                }
                if (hide == tv.NearCamera) continue;
                tv.NearCamera = hide;
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
