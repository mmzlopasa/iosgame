using System.Collections.Generic;
using Godot;

namespace Founders.People
{
    /// <summary>
    /// Turns the villagers (art/models/Person_*) into soft, rounded painted figures, as the Unity version
    /// did at import (PersonStyle.cs there): real proportions pushed towards a cartoon (bigger head and
    /// hands, sturdier arms, legs and body) and no blocky shapes or sharp rims.
    /// <list type="number">
    /// <item>Proportions: the skin moves around the joints it is bound to, so the skeleton and every clip
    /// stay as they are (a head point is scaled about the neck joint, an arm point pushed out from the
    /// bone's line; blended points move by their weights).</item>
    /// <item>Softening: the mesh is relaxed (Taubin smoothing: rounds without shrinking), hair and clothes
    /// never sink into the skin, and it is shaded with one smooth normal per point.</item>
    /// </list>
    /// Done once per model mesh, the first time a villager wears it.
    /// </summary>
    public static class PersonStyle
    {
        // Uniform scale about the bone's joint.
        public const float Head = 1.38f, Hand = 1.28f, Foot = 1.14f;
        // Thickness across the bone (its length stays).
        public const float Arm = 1.28f, Leg = 1.22f, Torso = 1.12f, Neck = 1.12f;

        const int SmoothPasses = 10;
        const float Lambda = 0.5f, Mu = -0.53f;

        static readonly Dictionary<string, string> Child = new Dictionary<string, string>
        {
            { "pelvis", "spine_01" }, { "spine_01", "spine_02" }, { "spine_02", "spine_03" }, { "spine_03", "neck_01" },
            { "neck_01", "Head" },
            { "upperarm_l", "lowerarm_l" }, { "lowerarm_l", "hand_l" }, { "upperarm_r", "lowerarm_r" }, { "lowerarm_r", "hand_r" },
            { "thigh_l", "calf_l" }, { "calf_l", "foot_l" }, { "thigh_r", "calf_r" }, { "calf_r", "foot_r" },
        };

        static (bool uniform, float k)? Rule(string bone)
        {
            if (bone == "Head") return (true, Head);
            if (bone.StartsWith("hand_")) return (true, Hand);
            if (bone.StartsWith("foot_") || bone.StartsWith("ball_")) return (true, Foot);
            if (bone.StartsWith("upperarm") || bone.StartsWith("lowerarm")) return (false, Arm);
            if (bone.StartsWith("thigh") || bone.StartsWith("calf")) return (false, Leg);
            if (bone.StartsWith("spine") || bone.StartsWith("pelvis")) return (false, Torso);
            if (bone.StartsWith("neck")) return (false, Neck);
            return null;
        }

        static float Strength(string zone) => zone == "Hair" ? 0.5f : zone == "Skin" ? 0.8f : 0.7f;
        static float Sink(string zone) => zone == "Hair" ? 0.002f : zone == "Skin" ? float.PositiveInfinity : 0.003f;

        static readonly Dictionary<Mesh, Mesh> styled = new Dictionary<Mesh, Mesh>();

        /// <summary>Swaps the villager's skinned meshes for their styled copies.</summary>
        public static void Apply(Node root)
        {
            var list = new List<MeshInstance3D>();
            Founders.Art.ModelLibrary.Collect(root, list);
            foreach (var mi in list)
            {
                if (mi.Mesh == null || mi.Skin == null || mi.Name.ToString().StartsWith("Prop_")) continue;
                if (!styled.TryGetValue(mi.Mesh, out var m))
                {
                    try { m = Style(mi.Mesh, mi.Skin, mi); }
                    catch (System.Exception e) { GD.Print($"[FOUNDERS] person style skipped: {e.Message}"); m = null; }
                    styled[mi.Mesh] = m;
                    GD.Print($"[FOUNDERS] person style {(m != null ? "applied" : "not applied")}: {mi.Name}");
                }
                if (m != null) mi.Mesh = m;
            }
        }

        static string Clean(string name)
        {
            int i = name.LastIndexOfAny(new[] { ':', '|', '/' });
            return i >= 0 ? name.Substring(i + 1) : name;
        }

        static Mesh Style(Mesh src, Skin skin, MeshInstance3D mi)
        {
            if (src is not ArrayMesh am || am.GetBlendShapeCount() > 0) return null;
            int binds = skin.GetBindCount();
            var skeleton = mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton);
            var joint = new Vector3[binds];
            var names = new string[binds];
            var index = new Dictionary<string, int>();
            for (int i = 0; i < binds; i++)
            {
                string n = skin.GetBindName(i).ToString();
                if (string.IsNullOrEmpty(n) && skeleton != null && skin.GetBindBone(i) >= 0) n = skeleton.GetBoneName(skin.GetBindBone(i));
                names[i] = Clean(n ?? "");
                joint[i] = skin.GetBindPose(i).AffineInverse().Origin;
                index[names[i]] = i;
            }
            var rules = new (bool uniform, float k)?[binds];
            var axis = new Vector3[binds];
            int ruled = 0;
            for (int i = 0; i < binds; i++)
            {
                rules[i] = Rule(names[i]);
                if (rules[i] != null) ruled++;
                axis[i] = Child.TryGetValue(names[i], out var c) && index.TryGetValue(c, out int ci)
                    ? (joint[ci] - joint[i]).Normalized()
                    : Vector3.Up;
            }
            if (ruled == 0) return null;

            int surfaces = am.GetSurfaceCount();
            var arrays = new Godot.Collections.Array[surfaces];
            var pos = new Vector3[surfaces][];
            var zones = new string[surfaces];
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = -lo;
            for (int s = 0; s < surfaces; s++)
            {
                arrays[s] = am.SurfaceGetArrays(s);
                var verts = arrays[s][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones = arrays[s][(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[s][(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = verts.Length > 0 ? bones.Length / verts.Length : 4;
                for (int v = 0; v < verts.Length; v++)
                {
                    var p = verts[v];
                    var move = Vector3.Zero;
                    for (int k = 0; k < per; k++)
                    {
                        int b = bones[v * per + k];
                        float w = weights[v * per + k];
                        if (w <= 0f || b < 0 || b >= binds || rules[b] == null) continue;
                        var rel = p - joint[b];
                        if (!rules[b].Value.uniform) rel -= axis[b] * rel.Dot(axis[b]);
                        move += w * (rules[b].Value.k - 1f) * rel;
                    }
                    verts[v] = p + move;
                    lo = lo.Min(verts[v]);
                    hi = hi.Max(verts[v]);
                }
                pos[s] = verts;
                string mat = am.SurfaceGetMaterial(s)?.ResourceName ?? am.SurfaceGetName(s) ?? "";
                zones[s] = mat.StartsWith("Hair") ? "Hair" : mat.StartsWith("Skin") ? "Skin" : mat.StartsWith("Main") ? "Main" : "Rest";
            }

            Soften(arrays, pos, zones, Mathf.Max(0.01f, (hi - lo).Length()));

            var outMesh = new ArrayMesh();
            for (int s = 0; s < surfaces; s++)
            {
                var a = arrays[s];
                var verts = pos[s];
                var bones = a[(int)Mesh.ArrayType.Bones].AsInt32Array();
                int per = verts.Length > 0 ? bones.Length / verts.Length : 4;
                var flags = per == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : (Mesh.ArrayFormat)0;
                outMesh.AddSurfaceFromArrays(am.SurfaceGetPrimitiveType(s), a, null, null, flags);
                outMesh.SurfaceSetMaterial(s, am.SurfaceGetMaterial(s));
                outMesh.SurfaceSetName(s, am.SurfaceGetName(s));
            }
            return outMesh;
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Relaxes all surfaces together (points that share a position are one point).</summary>
        static void Soften(Godot.Collections.Array[] arrays, Vector3[][] pos, string[] zones, float height)
        {
            int surfaces = arrays.Length;
            float step = Mathf.Max(1e-7f, height * 1e-5f);
            var ids = new Dictionary<(int, int, int), int>();
            var of = new int[surfaces][];
            var normals = new Vector3[surfaces][];
            for (int s = 0; s < surfaces; s++)
            {
                of[s] = new int[pos[s].Length];
                normals[s] = arrays[s][(int)Mesh.ArrayType.Normal].AsVector3Array();
                for (int v = 0; v < pos[s].Length; v++)
                {
                    var p = pos[s][v];
                    var key = (Mathf.RoundToInt(p.X / step), Mathf.RoundToInt(p.Y / step), Mathf.RoundToInt(p.Z / step));
                    if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
                    of[s][v] = id;
                }
            }
            int count = ids.Count;
            var strength = new float[count];
            var sink = new float[count];
            for (int i = 0; i < count; i++) sink[i] = float.PositiveInfinity;
            var edges = new HashSet<long>();
            var tris = new int[surfaces][];
            for (int s = 0; s < surfaces; s++)
            {
                var index = arrays[s][(int)Mesh.ArrayType.Index];
                tris[s] = index.VariantType == Variant.Type.Nil ? null : index.AsInt32Array();
                if (tris[s] == null) continue;
                float st = Strength(zones[s]), sk = Sink(zones[s]);
                var t = tris[s];
                for (int i = 0; i + 2 < t.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = of[s][t[i + e]], b = of[s][t[i + (e + 1) % 3]];
                        strength[a] = Mathf.Max(strength[a], st);
                        sink[a] = Mathf.Min(sink[a], sk);
                        if (a != b) edges.Add(Key(a, b));
                    }
            }
            var p0 = new Vector3[count];
            var n0 = new Vector3[count];
            var shared = new int[count];
            for (int s = 0; s < surfaces; s++)
                for (int v = 0; v < pos[s].Length; v++)
                {
                    int id = of[s][v];
                    p0[id] += pos[s][v];
                    if (normals[s].Length == pos[s].Length) n0[id] += normals[s][v];
                    shared[id]++;
                }
            for (int i = 0; i < count; i++)
            {
                if (shared[i] > 0) p0[i] /= shared[i];
                n0[i] = n0[i].LengthSquared() > 0 ? n0[i].Normalized() : Vector3.Up;
            }
            var p = (Vector3[])p0.Clone();
            var ea = new int[edges.Count];
            var eb = new int[edges.Count];
            int ei = 0;
            foreach (var k in edges) { ea[ei] = (int)(k >> 32); eb[ei] = (int)(k & 0xffffffff); ei++; }
            var degree = new int[count];
            for (int i = 0; i < ea.Length; i++) { degree[ea[i]]++; degree[eb[i]]++; }
            var avg = new Vector3[count];
            for (int pass = 0; pass < SmoothPasses; pass++)
                foreach (float f in new[] { Lambda, Mu })
                {
                    System.Array.Clear(avg, 0, count);
                    for (int i = 0; i < ea.Length; i++) { avg[ea[i]] += p[eb[i]]; avg[eb[i]] += p[ea[i]]; }
                    for (int i = 0; i < count; i++)
                        if (degree[i] > 0) p[i] += f * strength[i] * (avg[i] / degree[i] - p[i]);
                }
            for (int i = 0; i < count; i++)
            {
                float depth = -(p[i] - p0[i]).Dot(n0[i]);
                float over = depth - sink[i] * height;
                if (over > 0f) p[i] += n0[i] * over;
            }

            // One smooth normal per point (area-weighted face normals).
            var acc = new Vector3[count];
            for (int s = 0; s < surfaces; s++)
            {
                var t = tris[s];
                if (t == null) continue;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = of[s][t[i]], b = of[s][t[i + 1]], c = of[s][t[i + 2]];
                    // Godot's front faces wind clockwise: the face normal is (c - a) x (b - a).
                    var fn = (p[c] - p[a]).Cross(p[b] - p[a]);
                    acc[a] += fn; acc[b] += fn; acc[c] += fn;
                }
            }
            for (int s = 0; s < surfaces; s++)
            {
                var nn = new Vector3[pos[s].Length];
                for (int v = 0; v < pos[s].Length; v++)
                {
                    int id = of[s][v];
                    pos[s][v] = p[id];
                    var n = acc[id];
                    // Keep the file's facing (in case its winding differs): flip to agree with the old normal.
                    if (n.Dot(n0[id]) < 0f) n = -n;
                    nn[v] = n.LengthSquared() > 1e-12f ? n.Normalized() : n0[id];
                }
                arrays[s][(int)Mesh.ArrayType.Vertex] = pos[s];
                if (normals[s].Length == pos[s].Length) arrays[s][(int)Mesh.ArrayType.Normal] = nn;
            }
        }
    }
}
