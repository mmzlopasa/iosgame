using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FoundersEditor
{
    /// <summary>
    /// Turns the villagers (Assets/Art/Resources/Models/Person_*.fbx) into soft, rounded painted
    /// figures: real-life proportions pushed a little towards a cartoon (bigger head and hands,
    /// sturdier arms, legs and body), and no blocky shapes or sharp rims anywhere.
    /// <list type="number">
    /// <item>Proportions: the skin moves around the joints it is bound to, so the skeleton and every
    /// clip stay exactly as they are (a head vertex is scaled about the neck joint, an arm vertex is
    /// pushed out from the bone's line; blended vertices move by their weights).</item>
    /// <item>Rounder heads and hands: every hair and skin triangle of the near mesh is split in four
    /// (neighbours split to match, so no cracks open).</item>
    /// <item>Softening: the mesh is relaxed (Taubin smoothing: rounds without shrinking), hair and
    /// clothes never sink into the skin, and it is shaded with one smooth normal per point.</item>
    /// </list>
    /// The same rules and numbers are in tools/assets/person_style.py (Blender preview).
    /// </summary>
    public class PersonStylePostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        // Uniform scale about the bone's joint.
        public const float Head = 1.35f, Hand = 1.25f, Foot = 1.12f;
        // Thickness across the bone (its length stays).
        public const float Arm = 1.25f, Leg = 1.2f, Torso = 1.1f, Neck = 1.1f;

        const int SmoothPasses = 10;
        const float Lambda = 0.5f, Mu = -0.53f;

        /// <summary>How strongly each material zone is relaxed.</summary>
        static float Strength(string zone) => zone == "Hair" ? 0.5f : zone == "Skin" ? 0.8f : 0.7f;

        /// <summary>How far (x body height) a zone may sink along its normal: hair and clothes lie on the skin.</summary>
        static float Sink(string zone) => zone == "Hair" ? 0.002f : zone == "Skin" ? float.PositiveInfinity : 0.003f;

        static bool Subdivided(string zone) => zone == "Hair" || zone == "Skin";

        /// <summary>The next joint along each bone, which gives the bone's line.</summary>
        static readonly Dictionary<string, string> Child = new Dictionary<string, string>
        {
            { "pelvis", "spine_01" }, { "spine_01", "spine_02" }, { "spine_02", "spine_03" }, { "spine_03", "neck_01" },
            { "neck_01", "Head" },
            { "upperarm_l", "lowerarm_l" }, { "lowerarm_l", "hand_l" }, { "upperarm_r", "lowerarm_r" }, { "lowerarm_r", "hand_r" },
            { "thigh_l", "calf_l" }, { "calf_l", "foot_l" }, { "thigh_r", "calf_r" }, { "calf_r", "foot_r" },
        };

        /// <summary>(true, k): scale by k about the joint; (false, k): thicken by k across the bone; null: leave.</summary>
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

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith("Assets/Art/") || !Path.GetFileNameWithoutExtension(assetPath).StartsWith("Person_")) return;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = r.sharedMesh;
                if (mesh == null || r.bones == null || r.bones.Length != mesh.bindposes.Length) continue;
                Reshape(mesh, r.bones);
                var zones = new string[mesh.subMeshCount];
                var mats = r.sharedMaterials;
                for (int i = 0; i < zones.Length; i++)
                {
                    string n = i < mats.Length && mats[i] != null ? mats[i].name : "";
                    zones[i] = n.StartsWith("Hair") ? "Hair" : n.StartsWith("Skin") ? "Skin" : n.StartsWith("Main") ? "Main" : "Rest";
                }
                Soften(mesh, zones, subdivide: !r.name.Contains("_LOD") && mesh.blendShapeCount == 0);
            }
        }

        // MARK: - Proportions

        static void Reshape(Mesh mesh, Transform[] bones)
        {
            var bind = mesh.bindposes;
            // Each joint where it stands in the mesh's own space (the bind pose).
            var joint = new Vector3[bones.Length];
            var index = new Dictionary<string, int>();
            for (int i = 0; i < bones.Length; i++)
            {
                joint[i] = bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
                if (bones[i] != null) index[bones[i].name] = i;
            }
            var rules = new (bool uniform, float k)?[bones.Length];
            var axis = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                string name = bones[i].name;
                rules[i] = Rule(name);
                axis[i] = Child.TryGetValue(name, out var c) && index.TryGetValue(c, out int ci)
                    ? (joint[ci] - joint[i]).normalized
                    : Vector3.up;
            }

            var vertices = mesh.vertices;
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            int w = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                var p = vertices[v];
                var move = Vector3.zero;
                int count = perVertex[v];
                for (int k = 0; k < count; k++, w++)
                {
                    var bw = weights[w];
                    var rule = rules[bw.boneIndex];
                    if (rule == null || bw.weight <= 0f) continue;
                    var rel = p - joint[bw.boneIndex];
                    if (!rule.Value.uniform) rel -= axis[bw.boneIndex] * Vector3.Dot(rel, axis[bw.boneIndex]);
                    move += bw.weight * (rule.Value.k - 1f) * rel;
                }
                vertices[v] = p + move;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        // MARK: - Rounder, softer

        /// <summary>Mesh vertex data, editable as lists (new points are added at the end).</summary>
        sealed class Data
        {
            public List<Vector3> Pos = new List<Vector3>(), Nrm = new List<Vector3>();
            public List<Vector2> Uv = new List<Vector2>(), Uv2 = new List<Vector2>();
            public List<Color> Col = new List<Color>();
            public List<BoneWeight> Weights = new List<BoneWeight>();

            public int Add(int a, int b)
            {
                Pos.Add((Pos[a] + Pos[b]) * 0.5f);
                Nrm.Add((Nrm[a] + Nrm[b]).normalized);
                if (Uv.Count > 0) Uv.Add((Uv[a] + Uv[b]) * 0.5f);
                if (Uv2.Count > 0) Uv2.Add((Uv2[a] + Uv2[b]) * 0.5f);
                if (Col.Count > 0) Col.Add((Col[a] + Col[b]) * 0.5f);
                if (Weights.Count > 0) Weights.Add(Blend(Weights[a], Weights[b]));
                return Pos.Count - 1;
            }
        }

        /// <summary>Half of each bone weight set, merged by bone, the four strongest kept.</summary>
        static BoneWeight Blend(BoneWeight a, BoneWeight b)
        {
            var sum = new Dictionary<int, float>();
            void Put(int i, float w) { if (w > 0f) sum[i] = (sum.TryGetValue(i, out var s) ? s : 0f) + w * 0.5f; }
            Put(a.boneIndex0, a.weight0); Put(a.boneIndex1, a.weight1); Put(a.boneIndex2, a.weight2); Put(a.boneIndex3, a.weight3);
            Put(b.boneIndex0, b.weight0); Put(b.boneIndex1, b.weight1); Put(b.boneIndex2, b.weight2); Put(b.boneIndex3, b.weight3);
            var list = new List<KeyValuePair<int, float>>(sum);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            float total = 0f;
            for (int i = 0; i < list.Count && i < 4; i++) total += list[i].Value;
            if (total <= 0f) return a;
            var r = new BoneWeight();
            if (list.Count > 0) { r.boneIndex0 = list[0].Key; r.weight0 = list[0].Value / total; }
            if (list.Count > 1) { r.boneIndex1 = list[1].Key; r.weight1 = list[1].Value / total; }
            if (list.Count > 2) { r.boneIndex2 = list[2].Key; r.weight2 = list[2].Value / total; }
            if (list.Count > 3) { r.boneIndex3 = list[3].Key; r.weight3 = list[3].Value / total; }
            return r;
        }

        /// <summary>Gives points that share a position (the mesh is split at UV seams) one id.</summary>
        sealed class Welder
        {
            readonly Dictionary<Vector3Int, int> ids = new Dictionary<Vector3Int, int>();
            readonly float step;
            public readonly List<int> Of = new List<int>();
            public int Count => ids.Count;
            public Welder(float step) { this.step = step; }

            public void Add(Vector3 p)
            {
                var key = new Vector3Int(Mathf.RoundToInt(p.x / step), Mathf.RoundToInt(p.y / step), Mathf.RoundToInt(p.z / step));
                if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
                Of.Add(id);
            }
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static void Soften(Mesh mesh, string[] zones, bool subdivide)
        {
            var d = new Data();
            mesh.GetVertices(d.Pos);
            mesh.GetNormals(d.Nrm);
            mesh.GetUVs(0, d.Uv);
            mesh.GetUVs(1, d.Uv2);
            mesh.GetColors(d.Col);
            d.Weights.AddRange(mesh.boneWeights);
            if (d.Nrm.Count != d.Pos.Count || d.Weights.Count != d.Pos.Count) return;
            var tris = new List<int>[mesh.subMeshCount];
            for (int s = 0; s < tris.Length; s++) tris[s] = new List<int>(mesh.GetTriangles(s));

            var size = mesh.bounds.size;
            float height = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var weld = new Welder(Mathf.Max(1e-7f, height * 1e-5f));
            foreach (var p in d.Pos) weld.Add(p);

            if (subdivide)
            {
                // Edges (between welded points) of every hair and skin triangle are split; a neighbour
                // with split edges is split along them too, so the surface stays closed.
                var split = new HashSet<long>();
                for (int s = 0; s < tris.Length; s++)
                {
                    if (!Subdivided(zones[s])) continue;
                    var t = tris[s];
                    for (int i = 0; i < t.Count; i += 3)
                        for (int e = 0; e < 3; e++)
                            split.Add(Key(weld.Of[t[i + e]], weld.Of[t[i + (e + 1) % 3]]));
                }
                var mids = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long k = Key(a, b);
                    if (mids.TryGetValue(k, out int m)) return m;
                    m = d.Add(a, b);
                    weld.Add(d.Pos[m]);
                    return mids[k] = m;
                }
                for (int s = 0; s < tris.Length; s++)
                {
                    var t = tris[s];
                    var o = new List<int>(t.Count * 2);
                    for (int i = 0; i < t.Count; i += 3)
                    {
                        int a = t[i], b = t[i + 1], c = t[i + 2];
                        bool ab = split.Contains(Key(weld.Of[a], weld.Of[b]));
                        bool bc = split.Contains(Key(weld.Of[b], weld.Of[c]));
                        bool ca = split.Contains(Key(weld.Of[c], weld.Of[a]));
                        int n = (ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0);
                        if (n == 0) { o.Add(a); o.Add(b); o.Add(c); continue; }
                        if (n == 3)
                        {
                            int mab = Mid(a, b), mbc = Mid(b, c), mca = Mid(c, a);
                            o.AddRange(new[] { a, mab, mca, mab, b, mbc, mca, mbc, c, mab, mbc, mca });
                            continue;
                        }
                        // Turn the triangle (keeping its winding) so that the split edges come first.
                        if (n == 1) { while (!ab) { (a, b, c) = (b, c, a); (ab, bc, ca) = (bc, ca, ab); } }
                        else { while (ca) { (a, b, c) = (b, c, a); (ab, bc, ca) = (bc, ca, ab); } }
                        if (n == 1)
                        {
                            int m = Mid(a, b);
                            o.AddRange(new[] { a, m, c, m, b, c });
                        }
                        else
                        {
                            // ab and bc split, ca whole.
                            int mab = Mid(a, b), mbc = Mid(b, c);
                            o.AddRange(new[] { mab, b, mbc, a, mab, mbc, a, mbc, c });
                        }
                    }
                    tris[s] = o;
                }
            }

            // Welded points: strength, sink limit, neighbours, position and normal before softening.
            int count = weld.Count, verts = d.Pos.Count;
            var strength = new float[count];
            var sink = new float[count];
            for (int i = 0; i < count; i++) sink[i] = float.PositiveInfinity;
            var edges = new HashSet<long>();
            for (int s = 0; s < tris.Length; s++)
            {
                float st = Strength(zones[s]), sk = Sink(zones[s]);
                var t = tris[s];
                for (int i = 0; i < t.Count; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = weld.Of[t[i + e]], b = weld.Of[t[i + (e + 1) % 3]];
                        strength[a] = Mathf.Max(strength[a], st);
                        sink[a] = Mathf.Min(sink[a], sk);
                        if (a != b) edges.Add(Key(a, b));
                    }
            }
            var pos = new Vector3[count];
            var normal0 = new Vector3[count];
            var shared = new int[count];
            for (int v = 0; v < verts; v++)
            {
                int id = weld.Of[v];
                pos[id] += d.Pos[v];
                normal0[id] += d.Nrm[v];
                shared[id]++;
            }
            for (int i = 0; i < count; i++)
            {
                if (shared[i] > 0) pos[i] /= shared[i];
                normal0[i] = normal0[i].normalized;
            }
            var original = (Vector3[])pos.Clone();
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
                    for (int i = 0; i < ea.Length; i++) { avg[ea[i]] += pos[eb[i]]; avg[eb[i]] += pos[ea[i]]; }
                    for (int i = 0; i < count; i++)
                        if (degree[i] > 0) pos[i] += f * strength[i] * (avg[i] / degree[i] - pos[i]);
                }
            // Hair and clothes come back out to where they may sink at most.
            for (int i = 0; i < count; i++)
            {
                float depth = -Vector3.Dot(pos[i] - original[i], normal0[i]);
                float over = depth - sink[i] * height;
                if (over > 0f) pos[i] += normal0[i] * over;
            }

            // One smooth normal per point (area-weighted face normals).
            var normal = new Vector3[count];
            foreach (var t in tris)
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = weld.Of[t[i]], b = weld.Of[t[i + 1]], c = weld.Of[t[i + 2]];
                    var n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                    normal[a] += n; normal[b] += n; normal[c] += n;
                }
            for (int v = 0; v < verts; v++)
            {
                int id = weld.Of[v];
                d.Pos[v] = pos[id];
                var n = normal[id];
                d.Nrm[v] = n.sqrMagnitude > 1e-20f ? n.normalized : d.Nrm[v];
            }

            if (d.Pos.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(d.Pos);
            mesh.SetNormals(d.Nrm);
            if (d.Uv.Count > 0) mesh.SetUVs(0, d.Uv);
            if (d.Uv2.Count > 0) mesh.SetUVs(1, d.Uv2);
            if (d.Col.Count > 0) mesh.SetColors(d.Col);
            mesh.boneWeights = d.Weights.ToArray();
            for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s, false);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }
    }
}
