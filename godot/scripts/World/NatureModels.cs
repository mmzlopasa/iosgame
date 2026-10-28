using System;
using System.Collections.Generic;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// The valley's plants and stones, modelled in code in the painted low-poly style: three variants
    /// of every group <see cref="NaturePlan"/> places. Each model stands on y = 0 and is one metre tall
    /// (or one metre wide for stones placed by width); the field scales it to the planned size.
    /// Vertex colour alpha 1 marks leaves and grass (the nature shader sways and tints them).
    /// </summary>
    public static class NatureModels
    {
        static readonly Dictionary<(string, int), ArrayMesh> cache = new Dictionary<(string, int), ArrayMesh>();

        public static readonly Color Bark = new Color("6b4a33");
        public static readonly Color BirchBark = new Color("e6e0d2");
        public static readonly Color Leaf = new Color("5f9a3c");
        public static readonly Color LeafLight = new Color("8cbf4f");
        public static readonly Color Fir = new Color("2f5e3a");
        public static readonly Color Stone = new Color("8d8f92");
        public static readonly Color Moss = new Color("6f8a45");

        /// <summary>True for groups whose height (not width) is planned.</summary>
        public static bool ByHeight(string group) => !(group == "boulder" || group == "slab" || group == "pebble" || group == "crag" || group == "cliff" || group == "rock");

        public static ArrayMesh Get(string group, int variant)
        {
            variant = Math.Abs(variant) % 3;
            if (cache.TryGetValue((group, variant), out var m)) return m;
            var kit = new MeshKit(group.GetHashCode() ^ (variant * 977));
            Build(kit, group, variant);
            m = Normalize(kit, ByHeight(group));
            return cache[(group, variant)] = m;
        }

        static ArrayMesh Normalize(MeshKit kit, bool byHeight)
        {
            var b = kit.Bounds();
            float size = byHeight ? Mathf.Max(0.01f, b.End.Y) : Mathf.Max(0.01f, Mathf.Max(b.Size.X, b.Size.Z));
            var built = kit.Commit();
            // Re-scale by building again with a scaling transform (keeps flat shading exact).
            var arrays = built.SurfaceGetArrays(0);
            var v = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
            for (int i = 0; i < v.Length; i++) v[i] /= size;
            arrays[(int)Mesh.ArrayType.Vertex] = v;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }

        static void Build(MeshKit k, string group, int v)
        {
            var rng = new Random(group.Length * 131 + v * 17);
            switch (group)
            {
                case "broadleaf": case "kit_tree": Broadleaf(k, rng, v); break;
                case "birch": Birch(k, rng, v); break;
                case "fir": case "fir_young": FirTree(k, rng, v); break;
                case "dead_tree": DeadTree(k, rng, v); break;
                case "bush": Bush(k, rng, v, false); break;
                case "bush_flower": Bush(k, rng, v, true); break;
                case "kit_berry": case "berry": Berry(k, rng); break;
                case "grass": Grass(k, rng, 9, 0.6f); break;
                case "grass_tall": Grass(k, rng, 14, 1f); break;
                case "reeds": Reeds(k, rng); break;
                case "flowers": Flowers(k, rng, v); break;
                case "fern": Fern(k, rng); break;
                case "clover": Clover(k, rng); break;
                case "lily": Lily(k, rng, v); break;
                case "wheat": Grass(k, rng, 16, 1f, new Color("d8b65a")); break;
                case "mushroom": Mushroom(k, rng, v); break;
                case "pebble": Rocks(k, rng, v, 3, 0.45f, 0.55f); break;
                case "boulder": case "rock": case "kit_rock": Rocks(k, rng, v, 1, 0.55f, 0.75f); break;
                case "crag": case "kit_cliff": Crag(k, rng, v, 1.6f); break;
                case "cliff": Crag(k, rng, v, 1.1f); break;
                case "slab": Rocks(k, rng, v, 1, 0.22f, 1f, moss: true); break;
                case "log": Log(k, rng); break;
                case "stump": Stump(k, rng); break;
                case "branch": Branch(k, rng); break;
                default: Bush(k, rng, v, false); break;
            }
        }

        static void Trunk(MeshKit k, Vector3 from, Vector3 to, float r, Color c)
        {
            k.Tag = 0f;
            k.Limb(from, to, r, r * 0.65f, 6, c);
        }

        static void Broadleaf(MeshKit k, Random rng, int v)
        {
            float h = 1f;
            var top = new Vector3(0, h * 0.5f, 0);
            Trunk(k, Vector3.Zero, top, 0.06f, Bark);
            // A few branches into the crown.
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + v;
                Trunk(k, top * 0.85f, top + new Vector3(Mathf.Cos(a) * 0.16f, 0.14f, Mathf.Sin(a) * 0.16f), 0.03f, Bark);
            }
            k.Tag = 1f;
            int blobs = 5 + v;
            for (int i = 0; i < blobs; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = i == 0 ? 0f : 0.12f + (float)rng.NextDouble() * 0.14f;
                float y = 0.66f + (float)rng.NextDouble() * 0.18f - d * 0.3f;
                float r = 0.2f + (float)rng.NextDouble() * 0.1f;
                var col = i % 2 == 0 ? Leaf : LeafLight;
                k.Blob(new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d), new Vector3(r, r * 0.85f, r), MeshKit.Vary(col, rng, 0.08f), 0.18f, i + v * 13);
            }
        }

        static void Birch(MeshKit k, Random rng, int v)
        {
            Trunk(k, Vector3.Zero, new Vector3(0.02f, 0.75f, 0), 0.035f, BirchBark);
            // Dark marks on the bark.
            for (int i = 0; i < 5; i++)
                k.Box(new Vector3(0.0f, 0.1f + i * 0.13f, 0.03f), new Vector3(0.05f, 0.012f, 0.012f), new Color("2d2a28"));
            k.Tag = 1f;
            for (int i = 0; i < 4; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = 0.04f + (float)rng.NextDouble() * 0.08f;
                float y = 0.62f + i * 0.08f;
                float r = 0.13f - i * 0.015f;
                k.Blob(new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d), new Vector3(r, r * 1.3f, r), MeshKit.Vary(new Color("8fbd55"), rng, 0.08f), 0.2f, i + v * 7);
            }
        }

        static void FirTree(MeshKit k, Random rng, int v)
        {
            Trunk(k, Vector3.Zero, new Vector3(0, 0.3f, 0), 0.04f, Bark);
            k.Tag = 1f;
            int tiers = 4 + (v % 2);
            for (int i = 0; i < tiers; i++)
            {
                float t = (float)i / tiers;
                float y = 0.14f + t * 0.66f;
                float r = Mathf.Lerp(0.3f, 0.09f, t);
                var col = MeshKit.Vary(i % 2 == 0 ? Fir : new Color("3a6e42"), rng, 0.06f);
                k.Prism(new Vector3(0, y, 0), 0.32f - t * 0.1f, r, 0f, 8, col, caps: true, twist: i * 0.4f);
            }
        }

        static void DeadTree(MeshKit k, Random rng, int v)
        {
            var c = new Color("7d6a58");
            Trunk(k, Vector3.Zero, new Vector3(0.02f, 0.85f, 0.01f), 0.05f, c);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.7f + v;
                float y = 0.35f + i * 0.13f;
                Trunk(k, new Vector3(0, y, 0), new Vector3(Mathf.Cos(a) * 0.22f, y + 0.18f, Mathf.Sin(a) * 0.22f), 0.022f, c);
            }
        }

        static void Bush(MeshKit k, Random rng, int v, bool flowers)
        {
            k.Tag = 1f;
            int n = 3 + v;
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = i == 0 ? 0 : 0.22f + (float)rng.NextDouble() * 0.15f;
                float r = 0.35f + (float)rng.NextDouble() * 0.15f;
                k.Blob(new Vector3(Mathf.Cos(a) * d, r * 0.7f, Mathf.Sin(a) * d), new Vector3(r, r * 0.75f, r), MeshKit.Vary(new Color("4f8a37"), rng, 0.1f), 0.2f, i * 3 + v, -0.4f);
            }
            if (!flowers) return;
            k.Tag = 0f;
            var petals = new[] { new Color("f2d24b"), new Color("e8789a"), new Color("f4f1e8") };
            for (int i = 0; i < 18; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = (float)rng.NextDouble() * 0.45f;
                float y = 0.35f + (float)rng.NextDouble() * 0.4f;
                k.Blob(new Vector3(Mathf.Cos(a) * d, y + 0.12f, Mathf.Sin(a) * d), Vector3.One * 0.05f, petals[v % petals.Length], 0.1f, i);
            }
        }

        static void Berry(MeshKit k, Random rng)
        {
            Bush(k, rng, 1, false);
            k.Tag = 0f;
            for (int i = 0; i < 20; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = 0.15f + (float)rng.NextDouble() * 0.35f;
                k.Blob(new Vector3(Mathf.Cos(a) * d, 0.3f + (float)rng.NextDouble() * 0.4f, Mathf.Sin(a) * d), Vector3.One * 0.045f, new Color("c41f3a"), 0.05f, i);
            }
        }

        static void Grass(MeshKit k, Random rng, int blades, float height, Color? color = null)
        {
            k.Tag = 1f;
            var c = color ?? new Color("6fa544");
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                var root = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)rng.NextDouble() * 0.15f;
                var dir = new Vector3(Mathf.Cos(a) * 0.35f, 1f, Mathf.Sin(a) * 0.35f);
                float len = height * (0.6f + (float)rng.NextDouble() * 0.4f);
                k.Blade(root, dir, len, 0.05f, MeshKit.Vary(c, rng, 0.12f), MeshKit.Vary(c * 1.2f, rng, 0.1f), 0.4f);
            }
        }

        static void Reeds(MeshKit k, Random rng)
        {
            Grass(k, rng, 12, 1f, new Color("7b9a4a"));
            k.Tag = 0f;
            for (int i = 0; i < 4; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                var p = new Vector3(Mathf.Cos(a) * 0.08f, 0, Mathf.Sin(a) * 0.08f);
                k.Limb(p, p + new Vector3(0, 0.95f, 0), 0.008f, 0.006f, 4, new Color("6c7a3a"));
                k.Blob(p + new Vector3(0, 0.9f, 0), new Vector3(0.025f, 0.08f, 0.025f), new Color("6b4a2c"), 0.05f, i);
            }
        }

        static void Flowers(MeshKit k, Random rng, int v)
        {
            var heads = new[] { new Color("9b7fd1"), new Color("f2d24b"), new Color("d94545") }; // lavender, buttercup, poppy
            Grass(k, rng, 6, 0.6f);
            k.Tag = 0f;
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)rng.NextDouble() * 0.3f;
                float h = 0.6f + (float)rng.NextDouble() * 0.4f;
                k.Limb(p, p + new Vector3(0, h, 0), 0.01f, 0.008f, 4, new Color("4f7d32"));
                k.Blob(p + new Vector3(0, h, 0), new Vector3(0.06f, 0.04f, 0.06f), MeshKit.Vary(heads[v % 3], rng, 0.08f), 0.1f, i);
            }
        }

        static void Fern(MeshKit k, Random rng)
        {
            k.Tag = 1f;
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.Tau / 9 + (float)rng.NextDouble() * 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0.9f, Mathf.Sin(a));
                k.Blade(Vector3.Zero, dir, 1f, 0.22f, MeshKit.Vary(new Color("4c8a3a"), rng, 0.1f), new Color("6aa548"), 0.8f);
            }
        }

        static void Clover(MeshKit k, Random rng)
        {
            k.Tag = 1f;
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                float d = (float)rng.NextDouble() * 0.6f;
                k.Blob(new Vector3(Mathf.Cos(a) * d, 0.25f, Mathf.Sin(a) * d), new Vector3(0.3f, 0.25f, 0.3f), MeshKit.Vary(new Color("5d9a42"), rng, 0.1f), 0.2f, i, -0.2f);
            }
            k.Tag = 0f;
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau;
                k.Blob(new Vector3(Mathf.Cos(a) * 0.4f, 0.55f, Mathf.Sin(a) * 0.4f), Vector3.One * 0.08f, new Color("f2f0ea"), 0.1f, i);
            }
        }

        static void Lily(MeshKit k, Random rng, int v)
        {
            k.Tag = 0f;
            k.Blob(new Vector3(0, 0.02f, 0), new Vector3(0.5f, 0.03f, 0.5f), new Color("4d8a3c"), 0.05f, v);
            if (v != 1) k.Blob(new Vector3(0.1f, 0.08f, 0.05f), new Vector3(0.12f, 0.08f, 0.12f), v == 0 ? new Color("f4f0ea") : new Color("eda1bf"), 0.15f, v + 4);
        }

        static void Mushroom(MeshKit k, Random rng, int v)
        {
            var cap = v == 0 ? new Color("c8352b") : v == 1 ? new Color("b48a5a") : new Color("d9c4a0");
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.3f;
                var p = new Vector3(Mathf.Cos(a) * 0.25f * i, 0, Mathf.Sin(a) * 0.25f * i);
                float s = 1f - i * 0.25f;
                k.Tag = 0f;
                k.Limb(p, p + new Vector3(0, 0.6f * s, 0), 0.08f * s, 0.06f * s, 6, new Color("efe6d4"));
                k.Blob(p + new Vector3(0, 0.62f * s, 0), new Vector3(0.3f, 0.18f, 0.3f) * s, cap, 0.08f, i, -0.1f);
                if (v == 0)
                    for (int d = 0; d < 4; d++)
                        k.Blob(p + new Vector3(Mathf.Cos(d * 1.6f) * 0.15f * s, 0.75f * s, Mathf.Sin(d * 1.6f) * 0.15f * s), Vector3.One * 0.035f * s, new Color("f7f3ea"), 0.05f, d);
            }
        }

        static void Rocks(MeshKit k, Random rng, int v, int count, float flat, float lump, bool moss = false)
        {
            k.Tag = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.2f;
                float d = i == 0 ? 0 : 0.6f;
                float r = i == 0 ? 0.5f : 0.3f;
                var c = MeshKit.Vary(Stone, rng, 0.08f);
                k.Blob(new Vector3(Mathf.Cos(a) * d, r * flat * 0.6f, Mathf.Sin(a) * d), new Vector3(r, r * flat, r * (0.8f + 0.2f * v)), c, lump * 0.35f, v * 5 + i, -0.5f, 0.1f);
                if (moss) k.Blob(new Vector3(Mathf.Cos(a) * d, r * flat * 1.05f, Mathf.Sin(a) * d), new Vector3(r * 0.7f, r * flat * 0.3f, r * 0.6f), Moss, 0.2f, i + 3, -0.2f);
            }
        }

        static void Crag(MeshKit k, Random rng, int v, float tall)
        {
            k.Tag = 0f;
            // Stacked blocky stones, tallest in the middle.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.9f + v;
                float d = i == 0 ? 0 : 0.35f;
                float h = (i == 0 ? tall : tall * 0.55f) * (0.8f + 0.2f * (float)rng.NextDouble());
                float r = i == 0 ? 0.42f : 0.3f;
                var c = MeshKit.Vary(i % 2 == 0 ? Stone : new Color("7f8184"), rng, 0.08f);
                k.Blob(new Vector3(Mathf.Cos(a) * d, h * 0.45f, Mathf.Sin(a) * d), new Vector3(r, h * 0.5f, r * 0.85f), c, 0.12f, v * 11 + i, -0.7f, 0.12f);
            }
            k.Blob(new Vector3(0, tall * 0.92f, 0), new Vector3(0.3f, 0.06f, 0.26f), Moss, 0.2f, v, -0.2f);
        }

        static void Log(MeshKit k, Random rng)
        {
            k.Tag = 0f;
            using (k.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2f), new Vector3(-0.5f, 0.12f, 0))))
                k.Prism(Vector3.Zero, 1f, 0.12f, 0.11f, 7, Bark);
            k.Blob(new Vector3(0.2f, 0.22f, 0), new Vector3(0.18f, 0.05f, 0.1f), Moss, 0.2f, 1, -0.3f);
        }

        static void Stump(MeshKit k, Random rng)
        {
            k.Tag = 0f;
            k.Prism(Vector3.Zero, 0.6f, 0.4f, 0.33f, 8, Bark);
            k.Prism(new Vector3(0, 0.6f, 0), 0.01f, 0.33f, 0.33f, 8, new Color("c9a77a"));
        }

        static void Branch(MeshKit k, Random rng)
        {
            k.Tag = 0f;
            var c = new Color("75573c");
            k.Limb(new Vector3(-0.5f, 0.04f, 0), new Vector3(0.5f, 0.06f, 0.05f), 0.03f, 0.02f, 5, c);
            k.Limb(new Vector3(0.1f, 0.05f, 0.02f), new Vector3(0.3f, 0.12f, 0.25f), 0.015f, 0.01f, 4, c);
        }
    }
}
