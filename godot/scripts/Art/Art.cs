using System;
using System.Collections.Generic;
using Founders.Core;
using Founders.World;
using Godot;
using Json = Founders.Core.Json;

namespace Founders.Art
{
    /// <summary>
    /// Helpers for code carried over from the Unity version (building and decoration models). That code
    /// places things in Unity's space: left-handed, z pointing north, yaw turning clockwise. Here a
    /// Unity-space point (x, y, z) is (x, y, −z) and a Unity rotation is mirrored the same way, so the
    /// carried-over numbers build the same buildings.
    /// </summary>
    public static class U
    {
        public const float Deg2Rad = Mathf.Pi / 180f, Rad2Deg = 180f / Mathf.Pi;

        public static Vector3 ToGodot(Vector3 u) => new Vector3(u.X, u.Y, -u.Z);

        /// <summary>Unity's Quaternion.Euler (degrees, applied z, x, y) as a Godot basis.</summary>
        public static Basis Euler(Vector3 deg) =>
            Basis.FromEuler(new Vector3(-deg.X * Deg2Rad, -deg.Y * Deg2Rad, deg.Z * Deg2Rad), EulerOrder.Yxz);

        public static Basis Yaw(float deg) => new Basis(Vector3.Up, -deg * Deg2Rad);

        /// <summary>The rotation that turns Unity-space direction <paramref name="from"/> onto <paramref name="to"/>, as a Godot basis.</summary>
        public static Basis FromTo(Vector3 from, Vector3 to)
        {
            var a = ToGodot(from).Normalized();
            var b = ToGodot(to).Normalized();
            var axis = a.Cross(b);
            float d = Mathf.Clamp(a.Dot(b), -1f, 1f);
            if (axis.LengthSquared() < 1e-8f)
                return d > 0 ? Basis.Identity : new Basis(Mathf.Abs(a.X) < 0.9f ? Vector3.Right.Cross(a).Normalized() : Vector3.Up.Cross(a).Normalized(), Mathf.Pi);
            return new Basis(axis.Normalized(), Mathf.Acos(d));
        }

        public static float DeltaAngle(float current, float target)
        {
            float d = Repeat(target - current, 360f);
            if (d > 180f) d -= 360f;
            return d;
        }

        public static float Repeat(float t, float length) => Mathf.Clamp(t - Mathf.Floor(t / length) * length, 0f, length);
        public static float Clamp01(float v) => Mathf.Clamp(v, 0f, 1f);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a.Lerp(b, Mathf.Clamp(t, 0f, 1f));
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        /// <summary>Puts a node under a parent at a Unity-space position, rotation and scale.</summary>
        public static T Put<T>(T node, Node3D parent, Vector3 pos, Basis rot, Vector3 scale) where T : Node3D
        {
            parent?.AddChild(node);
            node.Transform = new Transform3D(rot * Basis.FromScale(scale), ToGodot(pos));
            return node;
        }

        /// <summary>Moves a node under another parent keeping its local transform (Unity's SetParent(t, false)).</summary>
        public static void Reparent(Node3D node, Node3D parent)
        {
            var local = node.Transform;
            node.GetParent()?.RemoveChild(node);
            parent.AddChild(node);
            node.Transform = local;
        }

        public static void SetRotation(Node3D node, Vector3 euler)
        {
            var s = node.Transform.Basis.Scale;
            node.Transform = new Transform3D(Euler(euler) * Basis.FromScale(s), node.Transform.Origin);
        }

        public static void SetScale(Node3D node, Vector3 scale)
        {
            var rot = node.Transform.Basis.Orthonormalized();
            node.Transform = new Transform3D(rot * Basis.FromScale(scale), node.Transform.Origin);
        }

        public static void SetLocalPosition(Node3D node, Vector3 pos) => node.Position = ToGodot(pos);

        /// <summary>Turns a node to a (Godot-space) rotation, keeping its scale.</summary>
        public static void SetBasis(Node3D node, Basis rot)
        {
            var s = node.Transform.Basis.Scale;
            node.Transform = new Transform3D(rot.Orthonormalized() * Basis.FromScale(s), node.Transform.Origin);
        }

        public static float Distance(Vector3 a, Vector3 b) => a.DistanceTo(b);
        public static float Distance2(Vector2 a, Vector2 b) => a.DistanceTo(b);
        public static float Dot(Vector3 a, Vector3 b) => a.Dot(b);
        public static Vector3 Cross(Vector3 a, Vector3 b) => a.Cross(b);
        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }
        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static Color LerpColor(Color a, Color b, float t) => a.Lerp(b, Clamp01(t));

        /// <summary>Frame time and running time, set once per frame by the game (Unity's Time.deltaTime / Time.time).</summary>
        public static float Dt = 1f / 60f;
        public static float Time;
    }

    public enum PrimitiveType { Cube, Cylinder, Sphere, Capsule, Quad, Plane }

    /// <summary>Plain-colour materials (people, cloth, fire, feedback markers), as the Unity version's palette.</summary>
    public static class Palette
    {
        static readonly Dictionary<string, (Color color, float rough, Color emission)> Specs = new Dictionary<string, (Color, float, Color)>
        {
            ["Water"] = (new Color(0.16f, 0.46f, 0.52f), 0.4f, Colors.Black),
            ["Earth"] = (new Color(0.30f, 0.23f, 0.16f), 1f, Colors.Black),
            ["Ash"] = (new Color(0.16f, 0.15f, 0.14f), 1f, Colors.Black),
            ["Skin"] = (new Color(0.62f, 0.44f, 0.32f), 0.8f, Colors.Black),
            ["Tunic"] = (new Color(0.42f, 0.30f, 0.20f), 1f, Colors.Black),
            ["Tunic2"] = (new Color(0.36f, 0.30f, 0.24f), 1f, Colors.Black),
            ["Hair"] = (new Color(0.12f, 0.09f, 0.07f), 0.8f, Colors.Black),
            ["Fish"] = (new Color(0.52f, 0.53f, 0.50f), 0.5f, Colors.Black),
            ["Banner"] = (new Color(0.55f, 0.08f, 0.07f), 1f, Colors.Black),
            ["Cloth"] = (new Color(0.86f, 0.82f, 0.72f), 1f, Colors.Black),
            ["Awning"] = (new Color(0.64f, 0.18f, 0.13f), 1f, Colors.Black),
            ["Awning2"] = (new Color(0.80f, 0.58f, 0.22f), 1f, Colors.Black),
            ["Awning3"] = (new Color(0.26f, 0.38f, 0.56f), 1f, Colors.Black),
            ["Apple"] = (new Color(0.72f, 0.14f, 0.08f), 0.6f, Colors.Black),
            ["Cabbage"] = (new Color(0.38f, 0.56f, 0.22f), 0.8f, Colors.Black),
            ["Grain"] = (new Color(0.84f, 0.67f, 0.32f), 1f, Colors.Black),
            ["Ember"] = (new Color(1.00f, 0.42f, 0.10f), 1f, new Color(1.0f, 0.38f, 0.08f) * 3f),
            ["GhostOk"] = (new Color(0.30f, 0.90f, 0.40f), 1f, new Color(0.15f, 0.7f, 0.25f)),
            ["GhostBad"] = (new Color(0.95f, 0.25f, 0.20f), 1f, new Color(0.7f, 0.12f, 0.1f)),
            ["Select"] = (new Color(1.00f, 0.85f, 0.30f), 1f, new Color(0.9f, 0.7f, 0.2f)),
            ["Zone_homes"] = (new Color(0.95f, 0.70f, 0.30f), 1f, new Color(0.45f, 0.30f, 0.10f)),
            ["Zone_woodcutting"] = (new Color(0.85f, 0.45f, 0.25f), 1f, new Color(0.40f, 0.18f, 0.08f)),
            ["Zone_forestry"] = (new Color(0.30f, 0.70f, 0.35f), 1f, new Color(0.10f, 0.32f, 0.12f)),
            ["Zone_pasture"] = (new Color(0.70f, 0.85f, 0.35f), 1f, new Color(0.30f, 0.40f, 0.12f)),
            ["BarBack"] = (new Color(0.08f, 0.07f, 0.06f), 1f, Colors.Black),
            ["BarFill"] = (new Color(1.00f, 0.80f, 0.30f), 1f, new Color(1f, 0.75f, 0.25f)),
        };

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Get(string name)
        {
            if (cache.TryGetValue(name, out var m)) return m;
            var spec = Specs.TryGetValue(name, out var s) ? s : (new Color(1, 0, 1), 1f, Colors.Black);
            var mat = new StandardMaterial3D
            {
                AlbedoColor = spec.Item1,
                Roughness = spec.Item2,
                MetallicSpecular = 0.2f,
            };
            if (spec.Item3 != Colors.Black)
            {
                mat.EmissionEnabled = true;
                mat.Emission = spec.Item3;
            }
            if (name.StartsWith("Ghost") || name.StartsWith("Zone_"))
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.AlbedoColor = new Color(spec.Item1, 0.55f);
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            }
            return cache[name] = mat;
        }
    }

    /// <summary>
    /// The game's models (CC0 scans from Poly Haven, Kenney and Quaternius kits, and the detailed
    /// buildings modelled for the game) described by data/art_manifest.json, with their textures.
    /// </summary>
    public static class ModelLibrary
    {
        static JObj manifest;
        static readonly Dictionary<string, PackedScene> scenes = new Dictionary<string, PackedScene>();
        static readonly Dictionary<string, Material[]> materials = new Dictionary<string, Material[]>();
        static readonly Dictionary<string, Material> surfaces = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, (Aabb box, bool ok)> bounds = new Dictionary<string, (Aabb, bool)>();

        public static JObj Manifest
        {
            get
            {
                if (manifest == null)
                    manifest = new JObj(Json.Parse(Godot.FileAccess.GetFileAsString("res://data/art_manifest.json")));
                return manifest;
            }
        }

        public static bool HasModel(string id) => Manifest.Obj("models").Has(id);

        /// <summary>The model's real size in metres (x, y, z), as measured for the Unity version.</summary>
        public static Vector3 Size(string id)
        {
            if (!HasModel(id)) return Vector3.One;
            var s = Manifest.Obj("models").Obj(id).Arr("size");
            return new Vector3((float)(double)s[0], (float)(double)s[1], (float)(double)s[2]);
        }

        /// <summary>"Art/Textures/x.png" in the manifest is res://art/textures/x.png here.</summary>
        public static Texture2D Texture(string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath)) return null;
            if (textures.TryGetValue(manifestPath, out var t)) return t;
            string path = "res://art/textures/" + manifestPath.Substring(manifestPath.IndexOf("Textures/", StringComparison.Ordinal) + "Textures/".Length);
            t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            return textures[manifestPath] = t;
        }

        static Material MakeMaterial(JObj spec)
        {
            var m = new StandardMaterial3D
            {
                AlbedoTexture = Texture(spec.Str("albedo")),
                Roughness = 0.92f,
                MetallicSpecular = 0.15f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            };
            var normal = spec.Has("normal") ? Texture(spec.Str("normal")) : null;
            if (normal != null)
            {
                m.NormalEnabled = true;
                m.NormalTexture = normal;
                m.NormalScale = 0.6f;
            }
            if (spec.Has("alphaClip") && spec.Bool("alphaClip"))
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                m.AlphaScissorThreshold = 0.45f;
            }
            if (spec.Has("doubleSided") && spec.Bool("doubleSided")) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            // Painted shading and part colours baked into the vertices (tools/godot/houses).
            if (spec.Has("vertexColor") && spec.Bool("vertexColor")) m.VertexColorUseAsAlbedo = true;
            return m;
        }

        public static Material[] Materials(string id)
        {
            if (materials.TryGetValue(id, out var m)) return m;
            var model = Manifest.Obj("models").Obj(id);
            var order = model.Strs("materialOrder");
            var specs = model.Obj("materials");
            m = new Material[order.Count];
            for (int i = 0; i < order.Count; i++) m[i] = specs.Has(order[i]) ? MakeMaterial(specs.Obj(order[i])) : Palette.Get("Earth");
            return materials[id] = m;
        }

        static readonly Dictionary<string, Material> tinted = new Dictionary<string, Material>();

        /// <summary>A model's first material recoloured (e.g. rusty iron ore or green copper ore on a boulder).</summary>
        public static Material Tinted(string modelId, string key, Color color)
        {
            if (tinted.TryGetValue(key, out var m) && m != null) return m;
            var mats = HasModel(modelId) ? Materials(modelId) : null;
            var src = mats != null && mats.Length > 0 && mats[0] != null ? mats[0] : Palette.Get("Earth");
            var copy = (Material)src.Duplicate();
            if (copy is BaseMaterial3D b) b.AlbedoColor = color;
            return tinted[key] = copy;
        }

        public static Material IronOre => Tinted("sn_rock_1", "IronOre", new Color(0.95f, 0.58f, 0.42f));
        public static Material Berry => Tinted("sn_rock_1", "Berry", new Color(0.78f, 0.12f, 0.22f));
        public static Material CopperOre => Tinted("sn_rock_1", "CopperOre", new Color(0.62f, 0.85f, 0.7f));

        /// <summary>A tiling textured surface (Bark, Hide, Thatch, RoughWood, Mud, DarkRock, ...) for shapes and primitives.</summary>
        public static Material Surface(string name)
        {
            if (surfaces.TryGetValue(name, out var m)) return m;
            var tex = Manifest.Obj("textures");
            var mat = new StandardMaterial3D
            {
                Roughness = 1f,
                MetallicSpecular = 0.1f,
                Uv1Triplanar = true,
                Uv1WorldTriplanar = true,
                Uv1Scale = new Vector3(0.6f, 0.6f, 0.6f),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            if (tex.Has(name)) mat.AlbedoTexture = Texture(tex.Obj(name).Str("albedo"));
            else mat.AlbedoColor = new Color(0.5f, 0.4f, 0.3f);
            return surfaces[name] = mat;
        }

        static PackedScene Scene(string id)
        {
            if (scenes.TryGetValue(id, out var s)) return s;
            // Models made for the Godot version are glTF (tools/godot/houses); the rest FBX.
            string path = $"res://art/models/{id}.glb";
            if (!ResourceLoader.Exists(path)) path = $"res://art/models/{id}.fbx";
            s = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            if (s == null) GD.Print($"[FOUNDERS] model {id} missing.");
            return scenes[id] = s;
        }

        public static void Collect(Node n, List<MeshInstance3D> into)
        {
            if (n is MeshInstance3D mi) into.Add(mi);
            foreach (var c in n.GetChildren()) Collect(c, into);
        }

        /// <summary>
        /// A model instance: standing on its pivot like in Unity, scaled to its real size, its materials
        /// from the manifest (or <paramref name="tint"/> on every surface, for the placement preview).
        /// </summary>
        public static Node3D Instance(string id, Material tint = null)
        {
            var scene = HasModel(id) ? Scene(id) : null;
            var holder = new Node3D { Name = id };
            if (scene == null)
            {
                var box = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 0.5f }, MaterialOverride = Palette.Get("GhostBad") };
                holder.AddChild(box);
                return holder;
            }
            var inst = scene.Instantiate<Node3D>();
            // Unity mirrored the files' x when importing them; turning half round gives the same look here.
            inst.RotateY(Mathf.Pi);
            holder.AddChild(inst);
            var meshes = new List<MeshInstance3D>();
            Collect(inst, meshes);
            var mats = tint != null ? null : Materials(id);
            var aabb = new Aabb();
            bool first = true;
            foreach (var mi in meshes)
            {
                if (mi.Mesh == null) continue;
                int n = mi.Mesh.GetSurfaceCount();
                for (int i = 0; i < n; i++)
                {
                    Material mat = tint;
                    if (mat == null)
                    {
                        // By name when the file kept the material names, else by order.
                        var own = mi.Mesh.SurfaceGetMaterial(i);
                        string name = own?.ResourceName ?? "";
                        var order = Manifest.Obj("models").Obj(id).Strs("materialOrder");
                        int k = order.IndexOf(name);
                        if (k < 0) k = Math.Min(i, mats.Length - 1);
                        mat = mats.Length > 0 ? mats[k] : Palette.Get("Earth");
                    }
                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            }
            // Scale to the manifest size (the file units differ from tool to tool).
            if (!bounds.TryGetValue(id, out var b))
            {
                var probe = scene.Instantiate<Node3D>();
                var list = new List<MeshInstance3D>();
                Collect(probe, list);
                foreach (var mi in list)
                {
                    if (mi.Mesh == null) continue;
                    var box = Local(mi, probe) * mi.Mesh.GetAabb();
                    aabb = first ? box : aabb.Merge(box);
                    first = false;
                }
                probe.Free();
                bounds[id] = b = (aabb, !first);
            }
            if (b.ok)
            {
                var size = Size(id);
                float k = size.Y > 0.001f && b.box.Size.Y > 0.0001f ? size.Y / b.box.Size.Y
                        : Mathf.Max(size.X, size.Z) / Mathf.Max(0.0001f, Mathf.Max(b.box.Size.X, b.box.Size.Z));
                inst.Scale = Vector3.One * k;
            }
            return holder;
        }

        /// <summary>The meshes' box in <paramref name="root"/>'s space (rest pose for skinned meshes).</summary>
        public static Aabb LocalBounds(Node3D root, Func<MeshInstance3D, bool> filter = null)
        {
            var list = new List<MeshInstance3D>();
            Collect(root, list);
            var box = new Aabb();
            bool first = true;
            foreach (var mi in list)
            {
                if (mi.Mesh == null || (filter != null && !filter(mi))) continue;
                var b = Local(mi, root) * mi.Mesh.GetAabb();
                box = first ? b : box.Merge(b);
                first = false;
            }
            return box;
        }

        public static Transform3D Local(Node3D node, Node3D root)
        {
            var t = Transform3D.Identity;
            Node n = node;
            while (n != null && n != root)
            {
                if (n is Node3D n3) t = n3.Transform * t;
                n = n.GetParent();
            }
            return t;
        }

        static ShaderMaterial plantMaterial;

        /// <summary>
        /// A plant on a building (crops, a shade tree, flowers): the painted nature models, standing
        /// <paramref name="heightMetres"/> tall at a Unity-space position.
        /// </summary>
        public static Node3D Tree(string name, Node3D parent, Vector3 position, float heightMetres, float yaw)
        {
            string group = name switch
            {
                "GrassTuft" => "wheat",
                "LeafTree" => "broadleaf",
                "Wildflowers" => "flowers",
                "Bush" => "bush",
                "Fir" => "fir",
                _ => "grass_tall",
            };
            plantMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/nature.gdshader") };
            var mesh = NatureModels.Get(group, Math.Abs((int)yaw) % 3);
            var mi = new MeshInstance3D { Name = "Tree_" + name, Mesh = mesh, MaterialOverride = plantMaterial };
            float k = heightMetres / Mathf.Max(0.01f, mesh.GetAabb().End.Y);
            return U.Put(mi, parent, position, U.Yaw(yaw), Vector3.One * k);
        }

        /// <summary>Places a model (Unity-space position, yaw and per-axis scale, as the Unity version).</summary>
        public static Node3D Place(string id, Node3D parent, Vector3 position, float scale = 1f, float yaw = 0f, Vector3? axisScale = null, Material tint = null)
        {
            var node = Instance(id, tint);
            var s = (axisScale ?? Vector3.One) * scale;
            return U.Put(node, parent, position, U.Yaw(yaw), s);
        }
    }

    /// <summary>Procedural meshes the Unity version made by hand (tipi, round wall, roofs, prism, dome, cone).</summary>
    public static class Meshes
    {
        static readonly Dictionary<string, ArrayMesh> cache = new Dictionary<string, ArrayMesh>();

        /// <summary>Builds from Unity-space triangles: z is mirrored and the winding turned so faces still face out.</summary>
        static ArrayMesh FromTriangles(List<Vector3> tris, List<Vector2> uvs, bool smooth = true)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            if (!smooth) st.SetSmoothGroup(uint.MaxValue);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                // Unity: clockwise front faces in a left-handed space; mirrored they are counter-clockwise,
                // and Godot wants clockwise: swap two corners.
                foreach (int k in new[] { i, i + 2, i + 1 })
                {
                    if (uvs != null) st.SetUV(uvs[k]);
                    st.AddVertex(U.ToGodot(tris[k]));
                }
            }
            st.GenerateNormals();
            st.GenerateTangents();
            return st.Commit();
        }

        static ArrayMesh Indexed(List<Vector3> v, List<Vector2> uv, List<int> tris)
        {
            var t = new List<Vector3>(); var u = new List<Vector2>();
            foreach (int i in tris) { t.Add(v[i]); u.Add(uv[i]); }
            return FromTriangles(t, u);
        }

        public static ArrayMesh Cone()
        {
            if (cache.TryGetValue("cone", out var m)) return m;
            const int n = 14;
            var t = new List<Vector3>(); var uv = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.Pi * 2 / n, a1 = (i + 1) * Mathf.Pi * 2 / n;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                t.Add(p1); t.Add(p0); t.Add(Vector3.Up);
                uv.Add(new Vector2((i + 1f) / n * 3f, 0)); uv.Add(new Vector2((float)i / n * 3f, 0)); uv.Add(new Vector2((i + 0.5f) / n * 3f, 2f));
            }
            return cache["cone"] = FromTriangles(t, uv);
        }

        public static ArrayMesh Tipi(int poles = 9)
        {
            string key = "tipi" + poles;
            if (cache.TryGetValue(key, out var m)) return m;
            const int perPole = 4, rings = 6;
            int seg = poles * perPole;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            const float top = 0.9f;
            for (int j = 0; j <= rings; j++)
            {
                float h = top * j / rings;
                for (int i = 0; i <= seg; i++)
                {
                    float a = -Mathf.Pi * 0.5f + (i + 0.5f) * Mathf.Pi * 2f / seg;
                    float between = Mathf.Abs(Mathf.Sin((i % perPole) / (float)perPole * Mathf.Pi));
                    float r = 0.5f * (1f - h) * (1f - 0.06f * between);
                    v.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(i / (float)seg * 3.2f, h * 2.2f));
                }
            }
            int row = seg + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    if (j < rings / 2 && (i == 0 || i == seg - 1)) continue;
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            Doubled(v, uv, tris);
            return cache[key] = Indexed(v, uv, tris);
        }

        static void Doubled(List<Vector3> v, List<Vector2> uv, List<int> tris)
        {
            int n = v.Count, count = tris.Count;
            v.AddRange(v.GetRange(0, n));
            uv.AddRange(uv.GetRange(0, n));
            for (int k = 0; k < count; k += 3)
                tris.AddRange(new[] { tris[k] + n, tris[k + 2] + n, tris[k + 1] + n });
        }

        public static ArrayMesh Wall(float doorFraction = 0.12f)
        {
            string key = "wall" + doorFraction;
            if (cache.TryGetValue(key, out var m)) return m;
            const int seg = 28;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            float gap = doorFraction * Mathf.Pi * 2f;
            for (int i = 0; i <= seg; i++)
            {
                float a = -Mathf.Pi * 0.5f + gap * 0.5f + i * (Mathf.Pi * 2f - gap) / seg;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                v.Add(p); v.Add(p + Vector3.Up);
                uv.Add(new Vector2(i / (float)seg * 4f, 0)); uv.Add(new Vector2(i / (float)seg * 4f, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
            Doubled(v, uv, tris);
            return cache[key] = Indexed(v, uv, tris);
        }

        public static ArrayMesh Roof()
        {
            if (cache.TryGetValue("roof", out var m)) return m;
            const int seg = 28, rings = 6;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                float r = 0.5f * (1f - t) * (1f + 0.12f * Mathf.Sin(t * Mathf.Pi));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.Pi * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * r, t, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(i / (float)seg * 4f, t * 1.6f));
                }
            }
            int row = seg + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            Doubled(v, uv, tris);
            return cache["roof"] = Indexed(v, uv, tris);
        }

        public static ArrayMesh LongRoof()
        {
            if (cache.TryGetValue("longroof", out var m)) return m;
            var v = new List<Vector3>
            {
                new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f),
                new Vector3(0, 1, -0.3f), new Vector3(0, 1, 0.3f),
            };
            var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 0), new Vector2(0, 0), new Vector2(0.6f, 1.6f), new Vector2(2.4f, 1.6f) };
            var tris = new List<int> { 0, 4, 3, 3, 4, 5, 1, 2, 4, 2, 5, 4, 0, 1, 4, 2, 3, 5 };
            Doubled(v, uv, tris);
            return cache["longroof"] = Indexed(v, uv, tris);
        }

        public static ArrayMesh Prism()
        {
            if (cache.TryGetValue("prism", out var m)) return m;
            var l = new Vector3(-0.5f, 0, -0.5f); var r = new Vector3(0.5f, 0, -0.5f); var top = new Vector3(0, 1, -0.5f);
            var l2 = new Vector3(-0.5f, 0, 0.5f); var r2 = new Vector3(0.5f, 0, 0.5f); var top2 = new Vector3(0, 1, 0.5f);
            var t = new List<Vector3> { l, top, r, r2, top2, l2, l, l2, top2, l, top2, top, r, top, top2, r, top2, r2 };
            var uv = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1.5f), new Vector2(0, 0), new Vector2(2, 1.5f), new Vector2(0, 1.5f),
                new Vector2(0, 0), new Vector2(0, 1.5f), new Vector2(2, 1.5f), new Vector2(0, 0), new Vector2(2, 1.5f), new Vector2(2, 0),
            };
            return cache["prism"] = FromTriangles(t, uv);
        }

        public static ArrayMesh Dome()
        {
            if (cache.TryGetValue("dome", out var m)) return m;
            const int seg = 18, rings = 8;
            var t = new List<Vector3>(); var uv = new List<Vector2>();
            Vector3 P(int i, int j)
            {
                float a = i * Mathf.Pi * 2 / seg, b = j * Mathf.Pi * 0.5f / rings;
                return new Vector3(Mathf.Cos(a) * Mathf.Cos(b) * 0.5f, Mathf.Sin(b) * 0.5f, Mathf.Sin(a) * Mathf.Cos(b) * 0.5f);
            }
            Vector2 UV(int i, int j) => new Vector2(i * 4f / seg, j * 2f / rings);
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    t.Add(P(i, j)); t.Add(P(i, j + 1)); t.Add(P(i + 1, j));
                    uv.Add(UV(i, j)); uv.Add(UV(i, j + 1)); uv.Add(UV(i + 1, j));
                    t.Add(P(i + 1, j)); t.Add(P(i, j + 1)); t.Add(P(i + 1, j + 1));
                    uv.Add(UV(i + 1, j)); uv.Add(UV(i, j + 1)); uv.Add(UV(i + 1, j + 1));
                }
            return cache["dome"] = FromTriangles(t, uv);
        }
    }

    /// <summary>Small helpers for assembling objects from parts (Unity-space numbers).</summary>
    public static class Parts
    {
        static readonly Dictionary<PrimitiveType, Mesh> prims = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>Unity's primitives: a 1 m cube, a cylinder and capsule 2 m tall and 1 m wide, a 1 m sphere, a 1 m quad.</summary>
        public static Mesh PrimMesh(PrimitiveType type)
        {
            if (prims.TryGetValue(type, out var m)) return m;
            m = type switch
            {
                PrimitiveType.Cube => new BoxMesh { Size = Vector3.One },
                PrimitiveType.Cylinder => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 2f, RadialSegments = 16, Rings = 1 },
                PrimitiveType.Sphere => new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 },
                PrimitiveType.Capsule => new CapsuleMesh { Radius = 0.5f, Height = 2f, RadialSegments = 12, Rings = 4 },
                PrimitiveType.Plane => new PlaneMesh { Size = new Vector2(10, 10) },
                _ => new QuadMesh { Size = Vector2.One },
            };
            return prims[type] = m;
        }

        public static Node3D Group(string name, Node3D parent, Vector3 position, float yaw = 0f) =>
            U.Put(new Node3D { Name = name }, parent, position, U.Yaw(yaw), Vector3.One);

        public static MeshInstance3D Prim(PrimitiveType type, Node3D parent, Vector3 position, Vector3 scale, Material material,
                                          Vector3 euler = default, bool castShadows = true)
        {
            var mi = new MeshInstance3D
            {
                Mesh = PrimMesh(type),
                MaterialOverride = material,
                CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            return U.Put(mi, parent, position, U.Euler(euler), scale);
        }

        public static MeshInstance3D Prim(PrimitiveType type, Node3D parent, Vector3 position, Vector3 scale, string material, Vector3 euler = default) =>
            Prim(type, parent, position, scale, Palette.Get(material), euler);

        public static MeshInstance3D Shape(Mesh mesh, Node3D parent, Vector3 position, Vector3 scale, Material material, Vector3 euler = default)
        {
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
            return U.Put(mi, parent, position, U.Euler(euler), scale);
        }
    }

    /// <summary>Fire, smoke, sparks and lamp light (as the Unity version's particle effects).</summary>
    public static class Effects
    {
        static Texture2D softDot;

        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            var g = new Gradient();
            g.SetColor(0, new Color(1, 1, 1, 1));
            g.SetColor(1, new Color(1, 1, 1, 0));
            softDot = new GradientTexture2D
            {
                Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                Width = 64, Height = 64,
            };
            return softDot;
        }

        static GpuParticles3D System(Node3D parent, string name, Vector3 at, Color a, Color b, bool additive, float size)
        {
            var draw = new QuadMesh { Size = new Vector2(size, size) };
            draw.Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                AlbedoTexture = SoftDot(),
                VertexColorUseAsAlbedo = true,
            };
            var p = new GpuParticles3D
            {
                Name = name,
                DrawPass1 = draw,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                LocalCoords = false,
            };
            return U.Put(p, parent, at, Basis.Identity, Vector3.One);
        }

        static Gradient Ramp(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            g.Offsets = Array.ConvertAll(keys, k => k.t);
            g.Colors = Array.ConvertAll(keys, k => k.c);
            return g;
        }

        public static void Fire(Node3D parent, Vector3 at, float scale)
        {
            var flames = System(parent, "Flames", at, Colors.White, Colors.White, true, 0.5f * scale);
            flames.Amount = 40;
            flames.Lifetime = 0.7f;
            flames.Preprocess = 1f;
            flames.ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 8f,
                Gravity = Vector3.Zero,
                InitialVelocityMin = 0.5f * scale,
                InitialVelocityMax = 1.1f * scale,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.14f * scale,
                ScaleMin = 0.7f, ScaleMax = 1.2f,
                ScaleCurve = new CurveTexture { Curve = Curve(1f, 0.1f) },
                ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 0.85f, 0.5f, 0f)), (0.15f, new Color(1f, 0.7f, 0.3f, 1f)), (1f, new Color(1f, 0.35f, 0.08f, 0f))) },
            };
            var sparks = System(parent, "Sparks", at + new Vector3(0, 0.2f * scale, 0), Colors.White, Colors.White, true, 0.05f);
            sparks.Amount = 12;
            sparks.Lifetime = 1.4f;
            sparks.ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 25f, Gravity = Vector3.Zero,
                InitialVelocityMin = 0.8f, InitialVelocityMax = 1.8f,
                TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.6f,
                Color = new Color(1f, 0.7f, 0.3f),
            };
            var smoke = System(parent, "Smoke", at + new Vector3(0, 0.6f * scale, 0), Colors.White, Colors.White, false, 0.65f * scale);
            smoke.Amount = 24;
            smoke.Lifetime = 5f;
            smoke.Preprocess = 4f;
            smoke.ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 6f, Gravity = new Vector3(0.25f, 0.05f, 0.1f),
                InitialVelocityMin = 0.5f, InitialVelocityMax = 0.9f,
                AngleMin = 0f, AngleMax = 360f,
                ScaleCurve = new CurveTexture { Curve = Curve(0.6f, 3.5f) },
                ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(0.3f, 0.28f, 0.27f, 0f)), (0.2f, new Color(0.45f, 0.44f, 0.44f, 0.45f)), (1f, new Color(0.7f, 0.7f, 0.72f, 0f))) },
            };
            var light = new OmniLight3D
            {
                Name = "FireLight", LightColor = new Color(1f, 0.55f, 0.22f), OmniRange = 7f * scale, LightEnergy = 1.6f * scale,
                ShadowEnabled = false,
            };
            U.Put(light, parent, at + new Vector3(0, 0.6f * scale, 0), Basis.Identity, Vector3.One);
            light.AddChild(new Flicker { Light = light, Base = light.LightEnergy });
        }

        /// <summary>A lazy thread of smoke from a chimney (Unity-space position on the parent).</summary>
        public static void Chimney(Node3D parent, Vector3 at)
        {
            var smoke = System(parent, "ChimneySmoke", at, Colors.White, Colors.White, false, 0.45f);
            smoke.Amount = 14;
            smoke.Lifetime = 6f;
            smoke.Preprocess = 5f;
            smoke.ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 5f, Gravity = new Vector3(0.18f, 0.04f, 0.08f),
                InitialVelocityMin = 0.35f, InitialVelocityMax = 0.6f,
                AngleMin = 0f, AngleMax = 360f,
                ScaleCurve = new CurveTexture { Curve = Curve(0.5f, 3f) },
                ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(0.5f, 0.48f, 0.47f, 0f)), (0.15f, new Color(0.62f, 0.61f, 0.6f, 0.4f)), (1f, new Color(0.85f, 0.85f, 0.87f, 0f))) },
            };
        }

        public static void Torch(Node3D parent, Vector3 at, float scale, bool lantern)
        {
            if (!lantern)
            {
                var flames = System(parent, "TorchFlame", at, Colors.White, Colors.White, true, 0.75f * scale);
                flames.Amount = 16;
                flames.Lifetime = 0.4f;
                flames.Preprocess = 1f;
                flames.ProcessMaterial = new ParticleProcessMaterial
                {
                    Direction = Vector3.Up, Spread = 6f, Gravity = Vector3.Zero,
                    InitialVelocityMin = 0.4f, InitialVelocityMax = 0.7f,
                    ScaleCurve = new CurveTexture { Curve = Curve(1f, 0.1f) },
                    ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 0.8f, 0.35f, 0.9f)), (1f, new Color(1f, 0.45f, 0.1f, 0f))) },
                };
            }
            var light = new OmniLight3D
            {
                Name = "LampLight", LightColor = lantern ? new Color(1f, 0.7f, 0.38f) : new Color(1f, 0.58f, 0.25f),
                OmniRange = 7f, LightEnergy = 1.4f, ShadowEnabled = false,
            };
            U.Put(light, parent, at + new Vector3(0, 0.15f, 0), Basis.Identity, Vector3.One);
            light.AddChild(new Flicker { Light = light, Base = light.LightEnergy, NightOnly = true });
        }

        static Curve Curve(float a, float b)
        {
            var c = new Curve { MaxValue = Mathf.Max(1f, Mathf.Max(a, b)) };
            c.AddPoint(new Vector2(0, a));
            c.AddPoint(new Vector2(1, b));
            return c;
        }
    }

    /// <summary>Makes a light flicker like a flame; lamps only shine at night.</summary>
    public sealed partial class Flicker : Node
    {
        public Light3D Light;
        public float Base = 1f;
        public bool NightOnly;
        float t;

        public override void _Ready() => t = (float)GD.RandRange(0, 10);

        public override void _Process(double delta)
        {
            if (Light == null) return;
            t += (float)delta;
            float k = 0.85f + 0.15f * Mathf.Sin(t * 13f) * Mathf.Sin(t * 7.3f + 1f);
            float night = NightOnly ? Mathf.Clamp(Atmosphere.Nightness * 2.2f, 0f, 1f) : 0.55f + 0.45f * Mathf.Clamp(Atmosphere.Nightness * 2f, 0f, 1f);
            Light.LightEnergy = Base * k * night;
            Light.Visible = Light.LightEnergy > 0.02f;
        }
    }
}
