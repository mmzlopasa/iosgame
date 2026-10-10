using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Buildings assembled from photo-scanned CC0 models (Poly Haven) and photo-textured
    /// shapes, one per building "sprite" key in content.json. The origin is the centre of the
    /// footprint on the ground; one grid cell is <see cref="Grid.CellSize"/> metres wide.
    /// With a <c>tint</c> material every part uses it (placement preview).
    /// </summary>
    public static class BuildingModels
    {
        public static GameObject Create(string sprite, Transform parent, Material tint)
        {
            var root = new GameObject("Model_" + sprite);
            root.transform.SetParent(parent, false);
            var b = new Builder(root.transform, tint, tint == null);
            switch (sprite)
            {
                case "campfire": Campfire(b); break;
                case "village_hearth": Hearth(b); break;
                case "shelter": Shelter(b); break;
                case "woven_shelter": WovenShelter(b); break;
                case "foraging_camp": ForagingCamp(b); break;
                case "wood_camp": WoodCamp(b); break;
                case "stone_site": StoneSite(b); break;
                case "storage_pit": StoragePit(b); break;
                case "toolmaker": Toolmaker(b); break;
                case "community_circle": CommunityCircle(b); break;
                default: b.Model("boulder_01", Vector3.zero, 0.5f); break;
            }
            return root;
        }

        sealed class Builder
        {
            public readonly Transform T;
            readonly Material tint;
            public readonly bool Effects;

            public Builder(Transform t, Material tint, bool effects) { T = t; this.tint = tint; Effects = effects; }

            Material Surface(string texture) => tint ?? ModelLibrary.Surface(texture);
            Material Color(string name) => tint ?? Palette.Get(name);

            public GameObject Model(string id, Vector3 pos, float scale, float yaw = 0f, Vector3? axis = null, Vector3? euler = null)
            {
                var go = ModelLibrary.Place(id, T, pos, scale, yaw, axis, tint);
                if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
                return go;
            }

            /// <summary>A wooden pole (scanned log) from a to b.</summary>
            public void Pole(Vector3 a, Vector3 b, float thickness = 0.12f)
            {
                var dir = b - a;
                float len = dir.magnitude;
                var size = ModelLibrary.Size("dead_tree_trunk"); // lies along X: length x, thickness y/z
                float k = len / Mathf.Max(0.01f, size.x);
                float t = thickness / Mathf.Max(0.01f, size.y * k);
                var go = ModelLibrary.Place("dead_tree_trunk", T, Vector3.zero, k, 0f, new Vector3(1f, t, t), tint);
                // Model pivot is bottom centre: lift by half thickness, then orient along a→b.
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
                go.transform.localPosition = (a + b) * 0.5f - go.transform.localRotation * new Vector3(0, size.y * k * t * 0.5f, 0);
            }

            public GameObject Shape(Mesh mesh, Vector3 pos, Vector3 scale, string texture, Vector3 euler = default) =>
                Parts.Shape(mesh, T, pos, scale, Surface(texture), euler);

            public GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, string texture, Vector3 euler = default) =>
                Parts.Prim(type, T, pos, scale, Surface(texture), euler);

            public GameObject PrimColor(PrimitiveType type, Vector3 pos, Vector3 scale, string colorMaterial, Vector3 euler = default) =>
                Parts.Prim(type, T, pos, scale, Color(colorMaterial), euler);

            public void Fire(Vector3 at, float scale)
            {
                if (!Effects) return;
                Founders.Effects.Fire(T, at, scale);
            }
        }

        // MARK: - Buildings

        static void LogsInFire(Builder b, Vector3 at, float r)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var outer = at + new Vector3(Mathf.Cos(a) * r, 0.02f, Mathf.Sin(a) * r);
                b.Pole(outer, at + new Vector3(0, 0.18f, 0), 0.09f);
            }
        }

        static void Campfire(Builder b)
        {
            b.Model("stone_fire_pit", Vector3.zero, 0.85f);
            LogsInFire(b, new Vector3(0, 0.04f, 0), 0.38f);
            b.Fire(new Vector3(0, 0.1f, 0), 1f);
            // A log to sit on and a bowl.
            b.Pole(new Vector3(-0.7f, 0.08f, 0.85f), new Vector3(0.5f, 0.08f, 1.05f), 0.22f);
            b.Model("wooden_bowl_01", new Vector3(0.75f, 0f, -0.55f), 1f, 30f);
        }

        static void Hearth(Builder b)
        {
            b.Model("stone_fire_pit", Vector3.zero, 1.15f);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                b.Model("stone_01", new Vector3(Mathf.Cos(a) * 1.05f, 0f, Mathf.Sin(a) * 1.05f), 3.2f, i * 40f);
            }
            LogsInFire(b, new Vector3(0, 0.05f, 0), 0.45f);
            b.Fire(new Vector3(0, 0.12f, 0), 1.35f);
        }

        /// <summary>A hide tent: A-frame of poles covered with stitched hides.</summary>
        static void HideTent(Builder b, Vector3 at, float width, float height, float depth, float yaw)
        {
            var g = Parts.Group("Tent", b.T, at, yaw).transform;
            var tb = new BuilderAt(b, g);
            tb.Shape(Meshes.Prism(), Vector3.zero, new Vector3(width, height, depth), "Hide");
            float hw = width * 0.5f, hd = depth * 0.5f;
            foreach (float z in new[] { -hd - 0.05f, hd + 0.05f })
            {
                tb.Pole(new Vector3(-hw - 0.1f, 0, z), new Vector3(0.12f, height + 0.25f, z), 0.07f);
                tb.Pole(new Vector3(hw + 0.1f, 0, z), new Vector3(-0.12f, height + 0.25f, z), 0.07f);
            }
            tb.Pole(new Vector3(0, height + 0.02f, -hd - 0.3f), new Vector3(0, height + 0.02f, hd + 0.3f), 0.08f);
        }

        /// <summary>Lets helpers build into a sub-transform with the same tint/effects.</summary>
        sealed class BuilderAt
        {
            readonly Builder parent;
            readonly Transform t;
            public BuilderAt(Builder parent, Transform t) { this.parent = parent; this.t = t; }

            public void Shape(Mesh mesh, Vector3 pos, Vector3 scale, string texture)
            {
                var go = parent.Shape(mesh, pos, scale, texture);
                go.transform.SetParent(t, false);
            }

            public void Pole(Vector3 a, Vector3 b, float thickness)
            {
                int before = parent.T.childCount;
                parent.Pole(a, b, thickness);
                for (int i = parent.T.childCount - 1; i >= before; i--) parent.T.GetChild(i).SetParent(t, false);
            }
        }

        static void Shelter(Builder b)
        {
            HideTent(b, Vector3.zero, 1.6f, 1.35f, 1.9f, 0f);
            b.Model("wicker_basket_01", new Vector3(0.95f, 0f, 0.6f), 1.2f, 20f);
            b.Model("dry_branches_medium_01", new Vector3(-0.95f, 0f, -0.7f), 0.7f, 70f);
        }

        static void WovenShelter(Builder b)
        {
            b.Shape(Meshes.Dome(), Vector3.zero, new Vector3(1.9f, 1.75f, 1.9f), "Thatch");
            b.Shape(Meshes.Cone(), new Vector3(0, 0.78f, 0), new Vector3(0.7f, 0.35f, 0.7f), "Thatch");
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                b.Model("stone_01", new Vector3(Mathf.Cos(a) * 0.98f, 0f, Mathf.Sin(a) * 0.98f), 2.6f, i * 33f);
            }
            // Doorway with a hide flap.
            b.PrimColor(PrimitiveType.Cube, new Vector3(0, 0.4f, -0.86f), new Vector3(0.5f, 0.8f, 0.1f), "Ash");
            b.Prim(PrimitiveType.Cube, new Vector3(0.18f, 0.38f, -0.92f), new Vector3(0.25f, 0.76f, 0.03f), "Hide", new Vector3(0, -20, 0));
            b.Model("ceramic_pot", new Vector3(0.9f, 0f, -0.6f), 0.6f, 15f);
        }

        static void ForagingCamp(Builder b)
        {
            // Drying rack
            b.Pole(new Vector3(-0.7f, 0f, -0.55f), new Vector3(-0.7f, 1.1f, -0.55f), 0.07f);
            b.Pole(new Vector3(0.7f, 0f, -0.55f), new Vector3(0.7f, 1.1f, -0.55f), 0.07f);
            b.Pole(new Vector3(-0.85f, 1.08f, -0.55f), new Vector3(0.85f, 1.08f, -0.55f), 0.06f);
            b.Prim(PrimitiveType.Cube, new Vector3(-0.25f, 0.8f, -0.55f), new Vector3(0.35f, 0.5f, 0.02f), "Hide");
            b.Prim(PrimitiveType.Cube, new Vector3(0.3f, 0.82f, -0.55f), new Vector3(0.3f, 0.45f, 0.02f), "Hide");
            // Gathered food
            b.Model("wicker_basket_01", new Vector3(-0.35f, 0f, 0.35f), 1.5f, 10f);
            b.Model("wicker_basket_01", new Vector3(0.35f, 0f, 0.5f), 1.3f, 75f);
            b.Model("ceramic_pot", new Vector3(0.75f, 0f, 0.05f), 0.55f);
            b.Model("wooden_bowl_01", new Vector3(-0.8f, 0f, 0.1f), 1.1f);
            b.Model("shrub_04", new Vector3(0f, 0f, 0.95f), 1.6f, 40f);
        }

        static void WoodCamp(Builder b)
        {
            // Stacked woodpile
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 4 - row; i++)
                {
                    float z = -0.45f + i * 0.24f + row * 0.12f;
                    float y = 0.07f + row * 0.18f;
                    b.Pole(new Vector3(-0.7f, y, z), new Vector3(0.6f, y, z), 0.2f);
                }
            b.Model("tree_stump_01", new Vector3(0.55f, 0f, 0.55f), 0.55f, 30f);
            b.Model("wooden_axe", new Vector3(0.55f, 0.28f, 0.55f), 1f, 0f, null, new Vector3(0, 40, 20));
            b.Model("dry_branches_medium_01", new Vector3(-0.6f, 0f, 0.6f), 0.8f, 120f);
        }

        static void StoneSite(Builder b)
        {
            b.Model("boulder_01", new Vector3(-0.35f, 0f, 0.2f), 0.75f, 25f);
            b.Model("namaqualand_boulder_02", new Vector3(0.4f, 0f, -0.35f), 0.42f, 110f);
            for (int i = 0; i < 7; i++)
                b.Model("stone_01", new Vector3(0.55f - (i % 3) * 0.3f, 0f, 0.55f - (i / 3) * 0.25f), 2.6f, i * 51f);
            b.Model("wicker_basket_01", new Vector3(-0.8f, 0f, -0.55f), 1.2f, 60f);
        }

        static void StoragePit(Builder b)
        {
            b.Prim(PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(1.7f, 0.06f, 1.7f), "Mud");
            b.PrimColor(PrimitiveType.Cylinder, new Vector3(0, 0.08f, 0), new Vector3(1.15f, 0.04f, 1.15f), "Ash");
            for (int i = 0; i < 5; i++)
            {
                float z = -0.42f + i * 0.21f;
                b.Pole(new Vector3(-0.7f, 0.13f, z), new Vector3(0.7f, 0.13f, z + 0.05f), 0.12f);
            }
            b.Model("ceramic_pot", new Vector3(0.85f, 0f, -0.7f), 0.6f, 40f);
            b.Model("ceramic_pot", new Vector3(-0.85f, 0f, -0.65f), 0.5f, 200f);
            b.Model("wicker_basket_01", new Vector3(0.8f, 0f, 0.75f), 1.3f, 15f);
        }

        static void Toolmaker(Builder b)
        {
            HideTent(b, new Vector3(-0.3f, 0f, 0.3f), 1.2f, 1.05f, 1.3f, 35f);
            b.Model("boulder_01", new Vector3(0.55f, 0f, -0.45f), 0.38f, 80f);
            b.Model("wooden_axe", new Vector3(0.8f, 0f, -0.1f), 0.9f, 0f, null, new Vector3(0, 30, 75));
            b.Model("wooden_bowl_01", new Vector3(0.2f, 0f, -0.8f), 1f);
            for (int i = 0; i < 5; i++)
                b.Model("stone_01", new Vector3(0.75f - i * 0.12f, 0f, -0.85f + (i % 2) * 0.1f), 1.4f, i * 70f);
        }

        static void CommunityCircle(Builder b)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 1.55f, 0f, Mathf.Sin(a) * 1.55f);
                float h = 0.85f + (i % 3) * 0.18f;
                b.Model("boulder_01", p, 1f, -a * Mathf.Rad2Deg, new Vector3(0.24f, h, 0.18f));
            }
            b.Model("stone_fire_pit", Vector3.zero, 0.6f);
            LogsInFire(b, new Vector3(0, 0.03f, 0), 0.26f);
            b.Fire(new Vector3(0, 0.08f, 0), 0.75f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                var c = new Vector3(Mathf.Cos(a) * 0.95f, 0.1f, Mathf.Sin(a) * 0.95f);
                var tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.4f;
                b.Pole(c - tangent, c + tangent, 0.22f);
            }
        }
    }
}
