using System;
using System.Collections.Generic;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Builds painted low-poly meshes from simple parts (boxes, prisms, cones, blobs, blades) with a
    /// colour per part. Faces are flat shaded (each triangle has its own vertices), which gives the
    /// carved, hand-painted look of the game's art. Everything the world draws is made with it: trees,
    /// rocks, buildings, people.
    /// </summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        /// <summary>Transform applied to everything added (push/pop with <see cref="With"/>).</summary>
        public Transform3D Xform = Transform3D.Identity;
        /// <summary>Alpha written with every colour: 1 marks leaves (tinted by season, swayed by wind), 0 wood and stone.</summary>
        public float Tag;
        readonly Random rng;
        public bool Smooth;

        public MeshKit(int seed = 1) { rng = new Random(seed); }

        public float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        public int Count => verts.Count;

        public IDisposable With(Transform3D local)
        {
            var saved = Xform;
            Xform = Xform * local;
            return new Restore(this, saved);
        }

        sealed class Restore : IDisposable
        {
            readonly MeshKit kit; readonly Transform3D saved;
            public Restore(MeshKit k, Transform3D s) { kit = k; saved = s; }
            public void Dispose() => kit.Xform = saved;
        }

        /// <summary>One face; seen from the front its corners run clockwise (Godot's front faces), and its normal is (c−a)×(b−a).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            a = Xform * a; b = Xform * b; c = Xform * c;
            var n = (c - a).Cross(b - a);
            if (n.LengthSquared() < 1e-12f) return;
            n = n.Normalized();
            var cc = new Color(col.R, col.G, col.B, Tag);
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            colors.Add(cc); colors.Add(cc); colors.Add(cc);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Tri(a, b, c, col);
            Tri(a, c, d, col);
        }

        /// <summary>An axis-aligned box centred at <paramref name="c"/>.</summary>
        public void Box(Vector3 c, Vector3 size, Color col, float shadeSides = 0.9f)
        {
            var h = size / 2f;
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.X, y * h.Y, z * h.Z);
            var side = Shade(col, shadeSides);
            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), col);          // top
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), Shade(col, 0.6f)); // bottom
            Quad(P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), side);          // +z
            Quad(P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, -1), side);      // -z
            Quad(P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), P(1, -1, -1), Shade(col, shadeSides * 0.95f)); // +x
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Shade(col, shadeSides * 0.95f)); // -x
        }

        /// <summary>A prism or frustum along y from <paramref name="bottom"/> with n sides (a cone when top radius is 0).</summary>
        public void Prism(Vector3 bottom, float height, float r0, float r1, int sides, Color col, bool caps = true, float twist = 0f)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = twist + Mathf.Tau * i / sides, a1 = twist + Mathf.Tau * (i + 1) / sides;
                var b0 = bottom + new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0);
                var b1 = bottom + new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
                var t0 = bottom + new Vector3(Mathf.Cos(a0) * r1, height, Mathf.Sin(a0) * r1);
                var t1 = bottom + new Vector3(Mathf.Cos(a1) * r1, height, Mathf.Sin(a1) * r1);
                var shade = Shade(col, 0.85f + 0.15f * Mathf.Cos(a0 - 0.8f));
                if (r1 <= 0.0001f) Tri(b0, b1, t0, shade);
                else Quad(b0, b1, t1, t0, shade);
                if (caps)
                {
                    if (r1 > 0.0001f) Tri(bottom + Vector3.Up * height, t0, t1, col);
                    Tri(bottom, b1, b0, Shade(col, 0.6f));
                }
            }
        }

        /// <summary>A bent tube from a to b (trunks, branches, legs).</summary>
        public void Limb(Vector3 a, Vector3 b, float r0, float r1, int sides, Color col)
        {
            var up = (b - a);
            float len = up.Length();
            if (len < 1e-4f) return;
            var basis = BasisFromUp(up / len);
            using (With(new Transform3D(basis, a)))
                Prism(Vector3.Zero, len, r0, r1, sides, col);
        }

        public static Basis BasisFromUp(Vector3 up)
        {
            var x = Mathf.Abs(up.Dot(Vector3.Right)) > 0.9f ? Vector3.Forward : Vector3.Right;
            var z = x.Cross(up).Normalized();
            x = up.Cross(z).Normalized();
            return new Basis(x, up, z);
        }

        static readonly Vector3[] IcoV;
        static readonly int[] IcoF;

        static MeshKit()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            // One subdivision: rounder blobs that still read as carved.
            var cache = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cache.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f));
                return cache[key] = v.Count - 1;
            }
            var f2 = new List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                f2.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
            IcoV = v.ToArray();
            IcoF = f2.ToArray();
        }

        /// <summary>A lumpy rounded blob (crowns, bushes, rocks): <paramref name="lump"/> 0 = sphere.</summary>
        public void Blob(Vector3 c, Vector3 radii, Color col, float lump = 0.15f, int seed = 0, float flatBottom = -2f, float colorJitter = 0.06f)
        {
            var lr = new Random(seed * 7919 + 17);
            var p = new Vector3[IcoV.Length];
            for (int i = 0; i < IcoV.Length; i++)
            {
                var d = IcoV[i];
                float k = 1f + ((float)lr.NextDouble() - 0.5f) * 2f * lump;
                var q = new Vector3(d.X * radii.X, Mathf.Max(d.Y, flatBottom) * radii.Y, d.Z * radii.Z) * k;
                p[i] = c + q;
            }
            for (int i = 0; i < IcoF.Length; i += 3)
            {
                var n = IcoV[IcoF[i]] + IcoV[IcoF[i + 1]] + IcoV[IcoF[i + 2]];
                float light = 0.78f + 0.22f * Mathf.Clamp(n.Normalized().Dot(new Vector3(-0.3f, 0.9f, 0.3f).Normalized()), -1f, 1f);
                float j = 1f + ((float)lr.NextDouble() - 0.5f) * 2f * colorJitter;
                Tri(p[IcoF[i]], p[IcoF[i + 2]], p[IcoF[i + 1]], Shade(col, light * j));
            }
        }

        /// <summary>A thin double-sided leaf or blade from a base point, bending out along <paramref name="dir"/>.</summary>
        public void Blade(Vector3 root, Vector3 dir, float length, float width, Color col, Color tip, float bend = 0.3f)
        {
            dir = dir.Normalized();
            var side = dir.Cross(Vector3.Up);
            if (side.LengthSquared() < 1e-4f) side = Vector3.Right;
            side = side.Normalized() * width * 0.5f;
            var mid = root + dir * length * 0.5f + Vector3.Down * bend * length * 0.2f;
            var end = root + dir * length + Vector3.Down * bend * length * 0.6f;
            Quad(root - side, mid - side * 0.8f, mid + side * 0.8f, root + side, col);
            Tri(mid - side * 0.8f, end, mid + side * 0.8f, tip);
            // Back faces so blades show from both sides.
            Quad(root + side, mid + side * 0.8f, mid - side * 0.8f, root - side, Shade(col, 0.85f));
            Tri(mid + side * 0.8f, end, mid - side * 0.8f, Shade(tip, 0.85f));
        }

        public static Color Shade(Color c, float k) => new Color(Mathf.Clamp(c.R * k, 0, 1), Mathf.Clamp(c.G * k, 0, 1), Mathf.Clamp(c.B * k, 0, 1), c.A);

        public static Color Vary(Color c, Random rng, float amount)
        {
            float k = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * amount;
            float h = ((float)rng.NextDouble() - 0.5f) * amount * 0.3f;
            return new Color(Mathf.Clamp(c.R * k + h, 0, 1), Mathf.Clamp(c.G * k, 0, 1), Mathf.Clamp(c.B * k - h, 0, 1), c.A);
        }

        /// <summary>The bounds of what was added so far (local space).</summary>
        public Aabb Bounds()
        {
            if (verts.Count == 0) return new Aabb();
            var min = verts[0]; var max = verts[0];
            foreach (var v in verts) { min = min.Min(v); max = max.Max(v); }
            return new Aabb(min, max - min);
        }

        public ArrayMesh Commit(ArrayMesh into = null)
        {
            var mesh = into ?? new ArrayMesh();
            if (verts.Count == 0) return mesh;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }

        public void Clear()
        {
            verts.Clear(); normals.Clear(); colors.Clear();
            Xform = Transform3D.Identity;
        }
    }
}
