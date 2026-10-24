using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FoundersEditor
{
    /// <summary>
    /// Gives the villagers (Assets/Art/Resources/Models/Person_*.fbx) the chunky painted-figure
    /// proportions of the game's icons: a big head, big hands and feet, thick arms, legs and body.
    /// Only the skin moves, around the joints it is bound to, so the skeleton and every clip stay
    /// exactly as they are: a vertex of the head is scaled about the neck joint, a vertex of an arm
    /// is pushed out from the bone's line, and blended vertices move by their weights.
    /// The same rules are in tools/assets/person_style.py (Blender preview).
    /// </summary>
    public class PersonStylePostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        // Uniform scale about the bone's joint.
        public const float Head = 1.55f, Hand = 1.4f, Foot = 1.18f;
        // Thickness across the bone (its length stays).
        public const float Arm = 1.4f, Leg = 1.3f, Torso = 1.15f, Neck = 1.15f;

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
                if (r.sharedMesh != null && r.bones != null && r.bones.Length == r.sharedMesh.bindposes.Length)
                    Reshape(r.sharedMesh, r.bones);
        }

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
    }
}
