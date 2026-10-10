using System.Collections.Generic;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Shared look of the game. Materials are generated as assets by the editor build
    /// script (so their shaders and keywords ship in the build) and loaded from Resources.
    /// In the editor without generated assets they are created on the fly.
    /// PLACEHOLDER ART: everything here is primitive/procedural until real models arrive.
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

            public Spec(string name, Color color, float smoothness = 0.15f, float metallic = 0f)
            {
                Name = name;
                Color = color;
                Smoothness = smoothness;
                Metallic = metallic;
                Emission = Color.black;
                Particle = false;
            }
        }

        public static readonly Spec[] All =
        {
            // Terrain
            new Spec("Grass", new Color(0.47f, 0.62f, 0.32f)),
            new Spec("Grass2", new Color(0.43f, 0.59f, 0.30f)),
            new Spec("Grass3", new Color(0.51f, 0.65f, 0.35f)),
            new Spec("ForestFloor", new Color(0.30f, 0.45f, 0.25f)),
            new Spec("Sand", new Color(0.81f, 0.73f, 0.53f)),
            new Spec("Water", new Color(0.27f, 0.50f, 0.66f), 0.85f),
            new Spec("RockGround", new Color(0.55f, 0.53f, 0.49f)),
            new Spec("Boulder", new Color(0.60f, 0.58f, 0.54f), 0.1f),
            new Spec("DarkRock", new Color(0.42f, 0.40f, 0.37f), 0.1f),
            new Spec("Dirt", new Color(0.43f, 0.33f, 0.23f)),
            new Spec("Path", new Color(0.66f, 0.55f, 0.38f)),
            new Spec("FarGround", new Color(0.33f, 0.46f, 0.26f)),
            new Spec("Fog", new Color(0.13f, 0.15f, 0.15f)),
            // Nature
            new Spec("Bark", new Color(0.36f, 0.25f, 0.16f)),
            new Spec("Leaves", new Color(0.25f, 0.47f, 0.24f)),
            new Spec("Leaves2", new Color(0.31f, 0.53f, 0.26f)),
            new Spec("Pine", new Color(0.18f, 0.37f, 0.25f)),
            new Spec("Berry", new Color(0.69f, 0.18f, 0.27f), 0.4f),
            // Materials of the camp
            new Spec("Wood", new Color(0.48f, 0.34f, 0.21f)),
            new Spec("DarkWood", new Color(0.34f, 0.24f, 0.15f)),
            new Spec("Hide", new Color(0.70f, 0.55f, 0.38f)),
            new Spec("Weave", new Color(0.77f, 0.65f, 0.44f)),
            new Spec("Thatch", new Color(0.72f, 0.60f, 0.33f)),
            new Spec("Stone", new Color(0.62f, 0.60f, 0.56f), 0.1f),
            new Spec("Basket", new Color(0.66f, 0.50f, 0.28f)),
            new Spec("Ash", new Color(0.24f, 0.22f, 0.21f)),
            new Spec("Ember", new Color(1.00f, 0.50f, 0.15f)) { Emission = new Color(1.0f, 0.45f, 0.10f) * 2.5f },
            new Spec("Flame", new Color(1.00f, 0.80f, 0.35f)) { Emission = new Color(1.0f, 0.75f, 0.30f) * 2.5f },
            // People
            new Spec("Skin", new Color(0.80f, 0.60f, 0.45f)),
            new Spec("Tunic", new Color(0.55f, 0.40f, 0.26f)),
            new Spec("Tunic2", new Color(0.45f, 0.36f, 0.30f)),
            new Spec("Hair", new Color(0.20f, 0.14f, 0.10f)),
            // Feedback
            new Spec("GhostOk", new Color(0.30f, 0.90f, 0.40f)) { Emission = new Color(0.15f, 0.6f, 0.2f) },
            new Spec("GhostBad", new Color(0.95f, 0.25f, 0.20f)) { Emission = new Color(0.6f, 0.12f, 0.1f) },
            new Spec("Select", new Color(1.00f, 0.85f, 0.30f)) { Emission = new Color(0.7f, 0.55f, 0.15f) },
            new Spec("BarBack", new Color(0.08f, 0.07f, 0.06f)),
            new Spec("BarFill", new Color(1.00f, 0.80f, 0.30f)) { Emission = new Color(0.8f, 0.6f, 0.2f) },
            // Particles
            new Spec("Smoke", new Color(0.55f, 0.55f, 0.58f, 0.6f)) { Particle = true },
            new Spec("Spark", new Color(1f, 0.6f, 0.2f)) { Particle = true },
        };

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D softDot;

        public static Material Get(string name)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = Resources.Load<Material>(ResourceFolder + "/" + name);
            if (material == null)
            {
                foreach (var spec in All)
                {
                    if (spec.Name == name) { material = Create(spec); break; }
                }
            }
            if (material == null) material = Create(new Spec(name, Color.magenta));
            var spec2 = Find(name);
            if (spec2.HasValue && spec2.Value.Particle)
            {
                material = new Material(material) { mainTexture = SoftDot() };
            }
            cache[name] = material;
            return material;
        }

        static Spec? Find(string name)
        {
            foreach (var spec in All) if (spec.Name == name) return spec;
            return null;
        }

        /// <summary>Builds a material from a spec. Used by the editor build script and as a runtime fallback.</summary>
        public static Material Create(Spec spec)
        {
            if (spec.Particle)
            {
                var shader = Shader.Find("Mobile/Particles/Alpha Blended")
                             ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                             ?? Shader.Find("Sprites/Default");
                var pm = new Material(shader) { name = spec.Name };
                if (pm.HasProperty("_TintColor")) pm.SetColor("_TintColor", spec.Color);
                pm.color = spec.Color;
                return pm;
            }

            var m = new Material(Shader.Find("Standard")) { name = spec.Name, color = spec.Color };
            m.SetFloat("_Glossiness", spec.Smoothness);
            m.SetFloat("_Metallic", spec.Metallic);
            if (spec.Emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", spec.Emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        /// <summary>Round soft particle texture, generated once.</summary>
        public static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int size = 32;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            softDot.SetPixels(pixels);
            softDot.Apply();
            return softDot;
        }
    }

    /// <summary>Procedural low-poly meshes (flat shaded) for shapes Unity has no primitive for.</summary>
    public static class Meshes
    {
        static Mesh cone, prism, rock;

        static Mesh FromTriangles(string name, List<Vector3> tris)
        {
            var mesh = new Mesh { name = name };
            var indices = new int[tris.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetVertices(tris);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Cone with base radius 0.5 at y=0 and apex at y=1.</summary>
        public static Mesh Cone()
        {
            if (cone != null) return cone;
            const int n = 10;
            var t = new List<Vector3>();
            var apex = new Vector3(0, 1, 0);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                t.Add(p1); t.Add(p0); t.Add(apex);
                t.Add(p0); t.Add(p1); t.Add(Vector3.zero);
            }
            return cone = FromTriangles("Cone", t);
        }

        /// <summary>Triangular prism (tent / roof): 1 wide (x), 1 high (y), 1 deep (z), base at y=0.</summary>
        public static Mesh Prism()
        {
            if (prism != null) return prism;
            var l = new Vector3(-0.5f, 0, -0.5f); var r = new Vector3(0.5f, 0, -0.5f); var top = new Vector3(0, 1, -0.5f);
            var l2 = new Vector3(-0.5f, 0, 0.5f); var r2 = new Vector3(0.5f, 0, 0.5f); var top2 = new Vector3(0, 1, 0.5f);
            var t = new List<Vector3>
            {
                l, top, r,            // front
                r2, top2, l2,         // back
                l, l2, top2, l, top2, top,   // left slope
                r, top, top2, r, top2, r2,   // right slope
                l, r, r2, l, r2, l2,         // bottom
            };
            return prism = FromTriangles("Prism", t);
        }

        /// <summary>Lumpy low-poly rock (deformed octahedron sphere), radius ~0.5.</summary>
        public static Mesh Rock()
        {
            if (rock != null) return rock;
            var dirs = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var faces = new List<Vector3[]>
            {
                new[] { dirs[0], dirs[4], dirs[3] }, new[] { dirs[0], dirs[3], dirs[5] },
                new[] { dirs[0], dirs[5], dirs[2] }, new[] { dirs[0], dirs[2], dirs[4] },
                new[] { dirs[1], dirs[3], dirs[4] }, new[] { dirs[1], dirs[5], dirs[3] },
                new[] { dirs[1], dirs[2], dirs[5] }, new[] { dirs[1], dirs[4], dirs[2] },
            };
            // One subdivision step for a rounder silhouette.
            var sub = new List<Vector3[]>();
            foreach (var f in faces)
            {
                var a = ((f[0] + f[1]) * 0.5f).normalized;
                var b = ((f[1] + f[2]) * 0.5f).normalized;
                var c = ((f[2] + f[0]) * 0.5f).normalized;
                sub.Add(new[] { f[0], a, c }); sub.Add(new[] { a, f[1], b });
                sub.Add(new[] { c, b, f[2] }); sub.Add(new[] { a, b, c });
            }
            var t = new List<Vector3>();
            foreach (var f in sub)
            {
                foreach (var v in f)
                {
                    float wobble = 0.85f + 0.3f * Mathf.PerlinNoise(v.x * 3.1f + 7f, v.z * 2.7f + v.y * 1.3f + 3f);
                    t.Add(v * 0.5f * wobble);
                }
            }
            return rock = FromTriangles("Rock", t);
        }
    }

    /// <summary>Small helpers for assembling placeholder models from parts.</summary>
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

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, string material,
                                      Vector3 euler = default, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Palette.Get(material);
            return go;
        }

        public static GameObject Shape(Mesh mesh, Transform parent, Vector3 position, Vector3 scale, string material,
                                      Vector3 euler = default)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Palette.Get(material);
            return go;
        }
    }
}
