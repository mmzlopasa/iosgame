using System.Collections.Generic;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Plain-colour and particle materials (feedback markers, people, fire). Generated as URP
    /// assets by the editor build script and loaded from Resources. Textured materials for
    /// terrain, models and trees come from <see cref="ModelLibrary"/>.
    /// </summary>
    public static class Palette
    {
        public const string ResourceFolder = "Materials";

        public struct Spec
        {
            public string Name;
            public Color Color;
            public float Smoothness;
            public float Metallic;
            public Color Emission;
            public bool Particle;
            public bool Additive;
            public string Texture;

            public Spec(string name, Color color, float smoothness = 0.3f, float metallic = 0f)
            {
                Name = name;
                Color = color;
                Smoothness = smoothness;
                Metallic = metallic;
                Emission = Color.black;
                Particle = false;
                Additive = false;
                Texture = null;
            }
        }

        public static readonly Spec[] All =
        {
            // Water: dark, very smooth surface that reflects the HDRI sky.
            new Spec("Water", new Color(0.10f, 0.17f, 0.17f), 0.95f),
            new Spec("Earth", new Color(0.30f, 0.23f, 0.16f), 0.15f),
            new Spec("Ash", new Color(0.16f, 0.15f, 0.14f), 0.1f),
            // People (placeholder until real character models)
            new Spec("Skin", new Color(0.62f, 0.44f, 0.32f), 0.35f),
            new Spec("Tunic", new Color(0.42f, 0.30f, 0.20f), 0.2f),
            new Spec("Tunic2", new Color(0.36f, 0.30f, 0.24f), 0.2f),
            new Spec("Hair", new Color(0.12f, 0.09f, 0.07f), 0.4f),
            new Spec("Fish", new Color(0.52f, 0.53f, 0.50f), 0.65f),
            // Fire
            new Spec("Ember", new Color(1.00f, 0.42f, 0.10f)) { Emission = new Color(1.0f, 0.38f, 0.08f) * 4f },
            // Feedback
            new Spec("GhostOk", new Color(0.30f, 0.90f, 0.40f)) { Emission = new Color(0.15f, 0.7f, 0.25f) },
            new Spec("GhostBad", new Color(0.95f, 0.25f, 0.20f)) { Emission = new Color(0.7f, 0.12f, 0.1f) },
            new Spec("Select", new Color(1.00f, 0.85f, 0.30f)) { Emission = new Color(0.9f, 0.7f, 0.2f) },
            new Spec("BarBack", new Color(0.08f, 0.07f, 0.06f)),
            new Spec("BarFill", new Color(1.00f, 0.80f, 0.30f)) { Emission = new Color(1f, 0.75f, 0.25f) },
            // Particles
            new Spec("Smoke", new Color(0.62f, 0.60f, 0.58f, 0.55f)) { Particle = true, Texture = "Cloud" },
            new Spec("Mist", new Color(0.92f, 0.93f, 0.95f, 0.92f)) { Particle = true, Texture = "Cloud" },
            new Spec("Flame", new Color(1f, 0.55f, 0.2f, 1f)) { Particle = true, Additive = true, Texture = "SoftDot" },
            new Spec("Spark", new Color(1f, 0.7f, 0.3f, 1f)) { Particle = true, Additive = true, Texture = "SoftDot" },
        };

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Get(string name)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = Resources.Load<Material>(ResourceFolder + "/" + name);
            if (material == null)
            {
                // Editor play mode before the build script ran: plain URP Lit stand-in.
                material = Create(Find(name) ?? new Spec(name, Color.magenta));
            }
            cache[name] = material;
            return material;
        }

        static Spec? Find(string name)
        {
            foreach (var spec in All) if (spec.Name == name) return spec;
            return null;
        }

        static Material Create(Spec spec)
        {
            var shader = Shader.Find(spec.Particle ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            var m = new Material(shader) { name = spec.Name };
            m.SetColor("_BaseColor", spec.Color);
            m.color = spec.Color;
            return m;
        }
    }

    /// <summary>
    /// Real 3D models (decimated Poly Haven scans) and tree billboards, described by
    /// Assets/Art/Resources/art_manifest.json. Models are imported without materials;
    /// their generated URP materials are assigned here per sub-mesh.
    /// </summary>
    public static class ModelLibrary
    {
        static JObj manifest;
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Material[]> modelMaterials = new Dictionary<string, Material[]>();
        static readonly Dictionary<string, Mesh> quads = new Dictionary<string, Mesh>();

        public static string MaterialKey(string modelId, string materialName) =>
            ("M_" + modelId + "__" + materialName).Replace(' ', '_').Replace('.', '_');

        public static JObj Manifest
        {
            get
            {
                if (manifest != null) return manifest;
                var asset = Resources.Load<TextAsset>("art_manifest");
                manifest = asset != null ? new JObj(Json.Parse(asset.text)) : new JObj(new Dictionary<string, object>
                {
                    ["models"] = new Dictionary<string, object>(), ["impostors"] = new Dictionary<string, object>(),
                    ["textures"] = new Dictionary<string, object>(),
                });
                return manifest;
            }
        }

        public static bool HasModel(string id) => Manifest.Obj("models").Has(id);
        public static bool HasImpostor(string name) => Manifest.Obj("impostors").Has(name);

        /// <summary>Material for a photo texture, e.g. "Bark", "Hide", "Thatch" (T_ prefix).</summary>
        public static Material Surface(string textureName) => Palette.Get("T_" + textureName);

        /// <summary>Real-world height of a model in metres (from the manifest).</summary>
        public static Vector3 Size(string id)
        {
            if (!HasModel(id)) return Vector3.one;
            var s = Manifest.Obj("models").Obj(id).Arr("size");
            return new Vector3((float)(double)s[0], (float)(double)s[1], (float)(double)s[2]);
        }

        /// <summary>
        /// Places a model. <paramref name="scale"/> multiplies its real size; <paramref name="tint"/>
        /// optionally replaces all materials (e.g. for the placement preview).
        /// </summary>
        public static GameObject Place(string id, Transform parent, Vector3 position, float scale = 1f, float yaw = 0f,
                                       Vector3? axisScale = null, Material tint = null)
        {
            var prefab = Prefab(id);
            GameObject go;
            if (prefab == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = Palette.Get("GhostBad");
                go.name = "Missing_" + id;
            }
            else
            {
                go = Object.Instantiate(prefab);
                go.name = id;
                var mats = tint != null ? null : Materials(id);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    int n = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
                    var arr = new Material[n];
                    for (int i = 0; i < n; i++) arr[i] = tint != null ? tint : (mats != null && i < mats.Length ? mats[i] : Palette.Get("Earth"));
                    r.sharedMaterials = arr;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var s = axisScale ?? Vector3.one;
            go.transform.localScale = new Vector3(s.x, s.y, s.z) * scale;
            return go;
        }

        static GameObject Prefab(string id)
        {
            if (prefabs.TryGetValue(id, out var p)) return p;
            p = HasModel(id) ? Resources.Load<GameObject>(Manifest.Obj("models").Obj(id).Str("resource")) : null;
            prefabs[id] = p;
            return p;
        }

        static Material[] Materials(string id)
        {
            if (modelMaterials.TryGetValue(id, out var m)) return m;
            var model = Manifest.Obj("models").Obj(id);
            var order = model.Strs("materialOrder");
            m = new Material[order.Count];
            for (int i = 0; i < order.Count; i++) m[i] = Palette.Get(MaterialKey(id, order[i]));
            modelMaterials[id] = m;
            return m;
        }

        /// <summary>
        /// A tree drawn as three crossed photo billboards plus a top-down canopy card: rendered from
        /// a film-quality scan, cheap enough for dozens of trees on an iPhone.
        /// </summary>
        public static GameObject Tree(string name, Transform parent, Vector3 position, float heightMetres, float yaw)
        {
            var root = new GameObject("Tree_" + name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (!HasImpostor(name)) return root;
            var info = Manifest.Obj("impostors").Obj(name);
            float w = (float)info.Num("width"), h = (float)info.Num("height");
            float k = heightMetres / Mathf.Max(0.01f, h);
            w *= k;
            for (int i = 0; i < 3; i++)
            {
                var card = Card(root.transform, $"I_{name}_{(i == 1 ? "side2" : "side")}", w, heightMetres);
                card.transform.localRotation = Quaternion.Euler(0, i * 60f, 0);
            }
            var top = Card(root.transform, $"I_{name}_top", w, w);
            top.transform.localPosition = new Vector3(0, heightMetres * 0.62f, 0);
            top.transform.localRotation = Quaternion.Euler(90, 0, 0);
            return root;
        }

        static GameObject Card(Transform parent, string material, float width, float height)
        {
            var go = new GameObject(material);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Get(material);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.transform.localScale = new Vector3(width, height, 1f);
            return go;
        }

        /// <summary>Unit quad standing on its bottom edge (pivot at bottom centre).</summary>
        static Mesh Quad()
        {
            if (quads.TryGetValue("q", out var q)) return q;
            q = new Mesh { name = "BillboardQuad" };
            q.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
            q.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            q.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            q.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            q.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            q.RecalculateBounds();
            quads["q"] = q;
            return q;
        }
    }

    /// <summary>Procedural meshes for shapes Unity has no primitive for.</summary>
    public static class Meshes
    {
        static Mesh cone, prism, dome;

        static Mesh FromTriangles(string name, List<Vector3> tris, List<Vector2> uvs)
        {
            var mesh = new Mesh { name = name };
            var indices = new int[tris.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetVertices(tris);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Cone with base radius 0.5 at y=0 and apex at y=1 (UVs wrap around).</summary>
        public static Mesh Cone()
        {
            if (cone != null) return cone;
            const int n = 14;
            var t = new List<Vector3>();
            var uv = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                t.Add(p1); t.Add(p0); t.Add(Vector3.up);
                uv.Add(new Vector2((i + 1f) / n * 3f, 0)); uv.Add(new Vector2((float)i / n * 3f, 0)); uv.Add(new Vector2((i + 0.5f) / n * 3f, 2f));
            }
            return cone = FromTriangles("Cone", t, uv);
        }

        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static Mesh Indexed(string name, List<Vector3> v, List<Vector2> uv, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Hide tent over a ring of poles (radius 0.5 at the base, height 1): the cover sags between
        /// poles, leaves a smoke hole at the top and a door gap at the front (−z). Both sides are
        /// built so the dark interior shows through the doorway.
        /// </summary>
        public static Mesh Tipi(int poles = 9)
        {
            string key = "tipi" + poles;
            if (cache.TryGetValue(key, out var m)) return m;
            const int perPole = 4, rings = 6;
            int seg = poles * perPole;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            const float top = 0.9f;
            for (int j = 0; j <= rings; j++)
            {
                float h = top * j / rings;
                for (int i = 0; i <= seg; i++)
                {
                    float a = -Mathf.PI * 0.5f + (i + 0.5f) * Mathf.PI * 2f / seg;
                    float between = Mathf.Abs(Mathf.Sin((i % perPole) / (float)perPole * Mathf.PI));
                    float r = 0.5f * (1f - h) * (1f - 0.06f * between);
                    v.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(i / (float)seg * 3.2f, h * 2.2f));
                }
            }
            int row = seg + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    // Door: the two segments facing −z, in the lower half.
                    if (j < rings / 2 && (i == 0 || i == seg - 1)) continue;
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            int n = v.Count, count = tris.Count;
            v.AddRange(v.GetRange(0, n));
            uv.AddRange(uv.GetRange(0, n));
            for (int k = 0; k < count; k += 3)
                tris.AddRange(new[] { tris[k] + n, tris[k + 2] + n, tris[k + 1] + n });
            return cache[key] = Indexed("Tipi", v, uv, tris);
        }

        /// <summary>Open cylinder wall (radius 0.5, height 1) with a door gap at −z; double sided.</summary>
        public static Mesh Wall(float doorFraction = 0.12f)
        {
            string key = "wall" + doorFraction;
            if (cache.TryGetValue(key, out var m)) return m;
            const int seg = 28;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            float gap = doorFraction * Mathf.PI * 2f;
            for (int i = 0; i <= seg; i++)
            {
                float a = -Mathf.PI * 0.5f + gap * 0.5f + i * (Mathf.PI * 2f - gap) / seg;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                v.Add(p); v.Add(p + Vector3.up);
                uv.Add(new Vector2(i / (float)seg * 4f, 0)); uv.Add(new Vector2(i / (float)seg * 4f, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
            int n = v.Count, count = tris.Count;
            v.AddRange(v.GetRange(0, n));
            uv.AddRange(uv.GetRange(0, n));
            for (int k = 0; k < count; k += 3)
                tris.AddRange(new[] { tris[k] + n, tris[k + 2] + n, tris[k + 1] + n });
            return cache[key] = Indexed("Wall", v, uv, tris);
        }

        /// <summary>Smooth conical thatch roof (radius 0.5, height 1) with a slightly bulging profile.</summary>
        public static Mesh Roof()
        {
            if (cache.TryGetValue("roof", out var m)) return m;
            const int seg = 28, rings = 6;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                float r = 0.5f * (1f - t) * (1f + 0.12f * Mathf.Sin(t * Mathf.PI));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
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
            // Underside so the eaves look solid from low angles.
            int n = v.Count, count = tris.Count;
            v.AddRange(v.GetRange(0, n));
            uv.AddRange(uv.GetRange(0, n));
            for (int k = 0; k < count; k += 3)
                tris.AddRange(new[] { tris[k] + n, tris[k + 2] + n, tris[k + 1] + n });
            return cache["roof"] = Indexed("Roof", v, uv, tris);
        }

        /// <summary>Gabled thatch roof for a longhouse: 1 wide (x), 1 high, 1 long (z), with hipped ends.</summary>
        public static Mesh LongRoof()
        {
            if (cache.TryGetValue("longroof", out var m)) return m;
            var v = new List<Vector3>
            {
                new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f),
                new Vector3(0, 1, -0.3f), new Vector3(0, 1, 0.3f),
            };
            var uv = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 0), new Vector2(0, 0), new Vector2(0.6f, 1.6f), new Vector2(2.4f, 1.6f),
            };
            var tris = new List<int>
            {
                0, 4, 3, 3, 4, 5, // left slope
                1, 2, 4, 2, 5, 4, // right slope
                0, 1, 4,          // front hip
                2, 3, 5,          // back hip
            };
            // Flat-shade by un-sharing vertices.
            var fv = new List<Vector3>(); var fuv = new List<Vector2>(); var ft = new List<int>();
            for (int k = 0; k < tris.Count; k++) { fv.Add(v[tris[k]]); fuv.Add(uv[tris[k]]); ft.Add(k); }
            int n = fv.Count;
            for (int k = 0; k < n; k++) { fv.Add(fv[k]); fuv.Add(fuv[k]); }
            for (int k = 0; k < n; k += 3) ft.AddRange(new[] { k + n, k + 2 + n, k + 1 + n });
            return cache["longroof"] = Indexed("LongRoof", fv, fuv, ft);
        }

        /// <summary>Triangular prism (lean-to roof): 1 wide (x), 1 high (y), 1 deep (z), base at y=0.</summary>
        public static Mesh Prism()
        {
            if (prism != null) return prism;
            var l = new Vector3(-0.5f, 0, -0.5f); var r = new Vector3(0.5f, 0, -0.5f); var top = new Vector3(0, 1, -0.5f);
            var l2 = new Vector3(-0.5f, 0, 0.5f); var r2 = new Vector3(0.5f, 0, 0.5f); var top2 = new Vector3(0, 1, 0.5f);
            var t = new List<Vector3> { l, top, r, r2, top2, l2, l, l2, top2, l, top2, top, r, top, top2, r, top2, r2 };
            var uv = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0),
                new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1.5f), new Vector2(0, 0), new Vector2(2, 1.5f), new Vector2(0, 1.5f),
                new Vector2(0, 0), new Vector2(0, 1.5f), new Vector2(2, 1.5f), new Vector2(0, 0), new Vector2(2, 1.5f), new Vector2(2, 0),
            };
            return prism = FromTriangles("Prism", t, uv);
        }

        /// <summary>Half sphere (radius 0.5, base at y=0) for woven huts.</summary>
        public static Mesh Dome()
        {
            if (dome != null) return dome;
            const int seg = 18, rings = 8;
            var t = new List<Vector3>();
            var uv = new List<Vector2>();
            Vector3 P(int i, int j)
            {
                float a = i * Mathf.PI * 2 / seg, b = j * Mathf.PI * 0.5f / rings;
                return new Vector3(Mathf.Cos(a) * Mathf.Cos(b) * 0.5f, Mathf.Sin(b) * 0.5f, Mathf.Sin(a) * Mathf.Cos(b) * 0.5f);
            }
            Vector2 U(int i, int j) => new Vector2(i * 4f / seg, j * 2f / rings);
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < seg; i++)
                {
                    t.Add(P(i, j)); t.Add(P(i, j + 1)); t.Add(P(i + 1, j));
                    uv.Add(U(i, j)); uv.Add(U(i, j + 1)); uv.Add(U(i + 1, j));
                    t.Add(P(i + 1, j)); t.Add(P(i, j + 1)); t.Add(P(i + 1, j + 1));
                    uv.Add(U(i + 1, j)); uv.Add(U(i, j + 1)); uv.Add(U(i + 1, j + 1));
                }
            }
            var mesh = FromTriangles("Dome", t, uv);
            dome = mesh;
            return dome;
        }
    }

    /// <summary>Small helpers for assembling objects from parts.</summary>
    public static class Parts
    {
        public static GameObject Group(string name, Transform parent, Vector3 position, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return go;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material,
                                      Vector3 euler = default, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, string material,
                                      Vector3 euler = default) =>
            Prim(type, parent, position, scale, Palette.Get(material), euler);

        public static GameObject Shape(Mesh mesh, Transform parent, Vector3 position, Vector3 scale, Material material,
                                       Vector3 euler = default)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
