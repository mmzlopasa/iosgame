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
                case "fishing_weir": FishingWeir(b); break;
                case "drying_rack": DryingRack(b); break;
                case "elders_lodge": EldersLodge(b); break;
                case "longhouse": Longhouse(b); break;
                case "daub_house": DaubHouse(b); break;
                case "farm_field": FarmField(b); break;
                case "animal_pen": AnimalPen(b); break;
                case "granary": Granary(b); break;
                case "pottery_kiln": PotteryKiln(b); break;
                case "copper_smelter": CopperSmelter(b); break;
                case "bronze_workshop": BronzeWorkshop(b); break;
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

        /// <summary>Conical hide tent: a ring of poles crossing above a smoke hole, hides sagging between them.</summary>
        static void Tipi(Builder b, Vector3 at, float diameter, float height, float yaw)
        {
            var g = Parts.Group("Tipi", b.T, at, yaw).transform;
            var go = b.Shape(Meshes.Tipi(), Vector3.zero, new Vector3(diameter, height, diameter), "Hide");
            go.transform.SetParent(g, false);
            const int poles = 9;
            var apex = new Vector3(0, height, 0);
            int before = b.T.childCount;
            for (int i = 0; i < poles; i++)
            {
                float a = -Mathf.PI * 0.5f + (i + 0.5f) * Mathf.PI * 2f / poles;
                var foot = new Vector3(Mathf.Cos(a) * diameter * 0.5f, 0f, Mathf.Sin(a) * diameter * 0.5f);
                var tip = apex + (apex - foot).normalized * (0.3f + 0.08f * (i % 3));
                b.Pole(foot, tip, 0.045f);
            }
            for (int i = b.T.childCount - 1; i >= before; i--) b.T.GetChild(i).SetParent(g, false);
            // Rolled-back door flap and a dark interior floor.
            var flap = b.Prim(PrimitiveType.Cube, new Vector3(diameter * 0.12f, height * 0.22f, -diameter * 0.42f),
                              new Vector3(diameter * 0.16f, height * 0.42f, 0.02f), "Hide", new Vector3(8f, 30f, 0));
            flap.transform.SetParent(g, false);
            var floor = b.PrimColor(PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0), new Vector3(diameter * 0.92f, 0.01f, diameter * 0.92f), "Ash");
            floor.transform.SetParent(g, false);
        }

        static void Shelter(Builder b)
        {
            Tipi(b, new Vector3(0, 0, 0.1f), 1.75f, 2.35f, 0f);
            b.Model("wicker_basket_01", new Vector3(0.85f, 0f, -0.65f), 1.2f, 20f);
            b.Model("dry_branches_medium_01", new Vector3(-0.85f, 0f, -0.75f), 0.7f, 70f);
            b.Pole(new Vector3(-0.45f, 0.06f, -0.9f), new Vector3(0.15f, 0.06f, -0.95f), 0.11f);
        }

        /// <summary>Roundhouse: wattle wall on a stone footing under a thick conical thatch roof.</summary>
        static void Roundhouse(Builder b, float diameter, float wallHeight, float roofHeight, string wall = "Bark")
        {
            b.Shape(Meshes.Wall(), Vector3.zero, new Vector3(diameter, wallHeight, diameter), wall);
            b.Shape(Meshes.Roof(), new Vector3(0, wallHeight - 0.05f, 0), new Vector3(diameter * 1.28f, roofHeight, diameter * 1.28f), "Thatch");
            b.PrimColor(PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0), new Vector3(diameter * 0.97f, 0.01f, diameter * 0.97f), "Ash");
            int stones = Mathf.RoundToInt(diameter * 8f);
            for (int i = 0; i < stones; i++)
            {
                float a = i / (float)stones * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, -90f)) < 18f) continue; // doorway
                b.Model("stone_01", new Vector3(Mathf.Cos(a) * diameter * 0.52f, 0f, Mathf.Sin(a) * diameter * 0.52f), 2.4f, i * 33f);
            }
            // Door posts and lintel.
            float dz = -diameter * 0.5f;
            b.Pole(new Vector3(-0.22f, 0f, dz), new Vector3(-0.22f, wallHeight + 0.05f, dz), 0.07f);
            b.Pole(new Vector3(0.22f, 0f, dz), new Vector3(0.22f, wallHeight + 0.05f, dz), 0.07f);
            b.Pole(new Vector3(-0.3f, wallHeight, dz), new Vector3(0.3f, wallHeight, dz), 0.07f);
        }

        static void WovenShelter(Builder b)
        {
            Roundhouse(b, 1.75f, 0.95f, 1.35f);
            b.Model("ceramic_pot", new Vector3(0.8f, 0f, -0.75f), 0.6f, 15f);
            b.Model("wicker_basket_01", new Vector3(-0.8f, 0f, -0.7f), 1.1f, 60f);
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
            Tipi(b, new Vector3(-0.35f, 0f, 0.35f), 1.25f, 1.7f, 35f);
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
    
        static void FishingWeir(Builder b)
        {
            // Fish drying frame
            b.Pole(new Vector3(-0.75f, 0f, 0.2f), new Vector3(-0.75f, 1.25f, 0.2f), 0.06f);
            b.Pole(new Vector3(0.75f, 0f, 0.2f), new Vector3(0.75f, 1.25f, 0.2f), 0.06f);
            b.Pole(new Vector3(-0.9f, 1.2f, 0.2f), new Vector3(0.9f, 1.2f, 0.2f), 0.05f);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.55f + i * 0.22f;
                b.PrimColor(PrimitiveType.Capsule, new Vector3(x, 0.98f - (i % 2) * 0.05f, 0.2f), new Vector3(0.07f, 0.17f, 0.04f), "Fish");
            }
            // Fish traps and baskets
            b.Model("wicker_basket_01", new Vector3(-0.45f, 0f, -0.45f), 1.4f, 30f);
            b.Model("wicker_basket_01", new Vector3(0.2f, 0f, -0.6f), 1.1f, 100f, null, new Vector3(80, 100, 0));
            // A dugout canoe pulled up on the bank
            b.Pole(new Vector3(0.15f, 0.12f, 0.85f), new Vector3(1.0f, 0.12f, -0.75f), 0.3f);
            // Stakes of the weir
            for (int i = 0; i < 5; i++)
            {
                float x = -0.9f + i * 0.18f;
                b.Pole(new Vector3(x, 0f, -0.95f), new Vector3(x + 0.05f, 0.75f + (i % 2) * 0.15f, -0.98f), 0.04f);
            }
        }

        static void DryingRack(Builder b)
        {
            foreach (float x in new[] { -0.8f, 0.8f })
            {
                b.Pole(new Vector3(x, 0f, -0.45f), new Vector3(x, 1.35f, 0f), 0.06f);
                b.Pole(new Vector3(x, 0f, 0.45f), new Vector3(x, 1.35f, 0f), 0.06f);
            }
            b.Pole(new Vector3(-0.95f, 1.3f, 0f), new Vector3(0.95f, 1.3f, 0f), 0.06f);
            b.Pole(new Vector3(-0.85f, 0.85f, -0.22f), new Vector3(0.85f, 0.85f, -0.22f), 0.04f);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.6f + i * 0.2f;
                b.Prim(PrimitiveType.Cube, new Vector3(x, 1.05f, 0f), new Vector3(0.1f, 0.42f, 0.015f), "Hide", new Vector3(0, i * 9f, 0));
            }
            b.Prim(PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.22f), new Vector3(1.1f, 0.42f, 0.02f), "Hide", new Vector3(-8f, 0, 0));
            b.Model("stone_fire_pit", new Vector3(0f, 0f, 0.6f), 0.35f);
            b.Fire(new Vector3(0f, 0.05f, 0.6f), 0.4f);
        }

        static void EldersLodge(Builder b)
        {
            Roundhouse(b, 1.9f, 1.05f, 1.6f);
            // Carved post at the door
            b.Pole(new Vector3(0.7f, 0f, -0.85f), new Vector3(0.7f, 2.4f, -0.85f), 0.16f);
            b.Model("boulder_01", new Vector3(0.7f, 2.32f, -0.85f), 0.14f, 20f);
            b.Pole(new Vector3(-0.85f, 0.12f, -0.8f), new Vector3(-0.2f, 0.12f, -1.0f), 0.2f);
        }

        static void Longhouse(Builder b)
        {
            // Footprint 2×2 cells (4 m). Long axis along z.
            const float len = 3.5f, wid = 2.3f, wallH = 0.85f;
            b.Prim(PrimitiveType.Cube, new Vector3(-wid * 0.5f, wallH * 0.5f, 0), new Vector3(0.12f, wallH, len), "Bark");
            b.Prim(PrimitiveType.Cube, new Vector3(wid * 0.5f, wallH * 0.5f, 0), new Vector3(0.12f, wallH, len), "Bark");
            b.Prim(PrimitiveType.Cube, new Vector3(0, wallH * 0.5f, len * 0.5f), new Vector3(wid, wallH, 0.12f), "Bark");
            b.Prim(PrimitiveType.Cube, new Vector3(-wid * 0.32f, wallH * 0.5f, -len * 0.5f), new Vector3(wid * 0.36f, wallH, 0.12f), "Bark");
            b.Prim(PrimitiveType.Cube, new Vector3(wid * 0.32f, wallH * 0.5f, -len * 0.5f), new Vector3(wid * 0.36f, wallH, 0.12f), "Bark");
            b.Shape(Meshes.LongRoof(), new Vector3(0, wallH - 0.05f, 0), new Vector3(wid * 1.3f, 1.9f, len * 1.15f), "Thatch");
            // Ridge pole and gable posts
            b.Pole(new Vector3(0, wallH + 1.85f, -len * 0.42f), new Vector3(0, wallH + 1.85f, len * 0.42f), 0.08f);
            foreach (float z in new[] { -len * 0.5f - 0.05f, len * 0.5f + 0.05f })
            {
                b.Pole(new Vector3(-0.3f, 0f, z), new Vector3(-0.3f, wallH + 0.25f, z), 0.08f);
                b.Pole(new Vector3(0.3f, 0f, z), new Vector3(0.3f, wallH + 0.25f, z), 0.08f);
            }
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 3 - row; i++)
                {
                    float x = -1.55f, z = -0.5f + i * 0.24f + row * 0.12f;
                    b.Pole(new Vector3(x, 0.07f + row * 0.18f, z - 0.6f), new Vector3(x, 0.07f + row * 0.18f, z + 0.6f), 0.18f);
                }
            b.Model("ceramic_pot", new Vector3(1.5f, 0f, -1.4f), 0.6f, 15f);
            b.Model("wicker_basket_01", new Vector3(1.45f, 0f, 1.3f), 1.3f, 40f);
        }

        // MARK: - Era II: First Civilizations

        static void DaubHouse(Builder b)
        {
            Roundhouse(b, 1.85f, 1.1f, 1.55f, "Mud");
            b.Model("ceramic_pot", new Vector3(0.85f, 0f, -0.8f), 0.7f, 15f);
            b.Model("ceramic_pot", new Vector3(0.65f, 0f, -0.95f), 0.5f, 75f);
            b.Pole(new Vector3(-0.95f, 0.08f, -0.6f), new Vector3(-0.3f, 0.08f, -1.0f), 0.18f);
        }

        static void FarmField(Builder b)
        {
            // 2×2 cells: tilled earth, furrows and rows of grain.
            b.Prim(PrimitiveType.Cube, new Vector3(0, 0.03f, 0), new Vector3(3.6f, 0.06f, 3.6f), "Mud");
            for (int i = 0; i < 7; i++)
            {
                float x = -1.5f + i * 0.5f;
                b.Prim(PrimitiveType.Cube, new Vector3(x, 0.08f, 0), new Vector3(0.18f, 0.06f, 3.4f), "Mud", new Vector3(0, 0, 0));
                for (int k = 0; k < 6; k++)
                {
                    float z = -1.45f + k * 0.58f + (i % 2) * 0.2f;
                    if (!b.Effects) continue; // the placement preview skips the crop cards
                    var tuft = ModelLibrary.Tree("GrassTuft", b.T, new Vector3(x, 0.08f, z), 0.5f + (k % 3) * 0.06f, i * 37f + k * 53f);
                    tuft.name = "Crop";
                }
            }
            // Corner posts and a low wattle fence on two sides.
            foreach (var c in new[] { new Vector3(-1.85f, 0, -1.85f), new Vector3(1.85f, 0, -1.85f), new Vector3(-1.85f, 0, 1.85f), new Vector3(1.85f, 0, 1.85f) })
                b.Pole(c, c + new Vector3(0, 0.7f, 0), 0.07f);
            b.Pole(new Vector3(-1.85f, 0.5f, 1.85f), new Vector3(1.85f, 0.5f, 1.85f), 0.05f);
            b.Pole(new Vector3(-1.85f, 0.5f, -1.85f), new Vector3(-1.85f, 0.5f, 1.85f), 0.05f);
            b.Model("wicker_basket_01", new Vector3(1.6f, 0f, -1.5f), 1.3f, 30f);
        }

        static void AnimalPen(Builder b)
        {
            const int posts = 10;
            for (int i = 0; i < posts; i++)
            {
                float a0 = i / (float)posts * Mathf.PI * 2f, a1 = (i + 1) / (float)posts * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.9f, 0f, Mathf.Sin(a0) * 0.9f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.9f, 0f, Mathf.Sin(a1) * 0.9f);
                b.Pole(p0, p0 + new Vector3(0, 0.75f, 0), 0.06f);
                if (i == 7) continue; // gate gap
                b.Pole(p0 + new Vector3(0, 0.55f, 0), p1 + new Vector3(0, 0.55f, 0), 0.04f);
                b.Pole(p0 + new Vector3(0, 0.3f, 0), p1 + new Vector3(0, 0.3f, 0), 0.04f);
            }
            b.Prim(PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0), new Vector3(1.7f, 0.01f, 1.7f), "Mud");
            b.Shape(Meshes.Dome(), new Vector3(-0.35f, 0f, 0.3f), new Vector3(0.6f, 0.45f, 0.6f), "Thatch");
            b.Pole(new Vector3(0.1f, 0.1f, -0.45f), new Vector3(0.6f, 0.1f, -0.2f), 0.16f);
            // Goats: small bodies with heads (simple shapes read well from the strategy camera).
            for (int i = 0; i < 3; i++)
            {
                var g = Parts.Group("Goat", b.T, new Vector3(0.35f - i * 0.3f, 0f, -0.1f + (i % 2) * 0.35f), i * 70f).transform;
                var body = b.PrimColor(PrimitiveType.Capsule, new Vector3(0, 0.32f, 0), new Vector3(0.18f, 0.2f, 0.18f), i == 1 ? "Tunic2" : "Tunic", new Vector3(90, 0, 0));
                body.transform.SetParent(g, false);
                var head = b.PrimColor(PrimitiveType.Sphere, new Vector3(0, 0.42f, 0.2f), new Vector3(0.12f, 0.12f, 0.14f), i == 1 ? "Tunic2" : "Tunic");
                head.transform.SetParent(g, false);
                foreach (var lx in new[] { -0.06f, 0.06f })
                    foreach (var lz in new[] { -0.1f, 0.1f })
                    {
                        var leg = b.PrimColor(PrimitiveType.Cylinder, new Vector3(lx, 0.12f, lz), new Vector3(0.035f, 0.12f, 0.035f), "Hair");
                        leg.transform.SetParent(g, false);
                    }
            }
        }

        static void Granary(Builder b)
        {
            const float lift = 0.65f;
            foreach (var c in new[] { new Vector3(-0.6f, 0, -0.6f), new Vector3(0.6f, 0, -0.6f), new Vector3(-0.6f, 0, 0.6f), new Vector3(0.6f, 0, 0.6f) })
            {
                b.Pole(c, c + new Vector3(0, lift, 0), 0.12f);
                b.Model("stone_01", c + new Vector3(0, lift - 0.02f, 0), 2.2f);
            }
            b.Prim(PrimitiveType.Cube, new Vector3(0, lift + 0.05f, 0), new Vector3(1.6f, 0.1f, 1.6f), "RoughWood");
            var g = Parts.Group("Store", b.T, new Vector3(0, lift + 0.1f, 0)).transform;
            int before = b.T.childCount;
            Roundhouse(b, 1.35f, 0.75f, 1.05f, "Mud");
            for (int i = b.T.childCount - 1; i >= before; i--) b.T.GetChild(i).SetParent(g, false);
            // Ladder
            b.Pole(new Vector3(-0.2f, 0f, -1.15f), new Vector3(-0.2f, lift + 0.15f, -0.75f), 0.04f);
            b.Pole(new Vector3(0.2f, 0f, -1.15f), new Vector3(0.2f, lift + 0.15f, -0.75f), 0.04f);
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                var y = t * (lift + 0.15f);
                var z = Mathf.Lerp(-1.15f, -0.75f, t);
                b.Pole(new Vector3(-0.22f, y, z), new Vector3(0.22f, y, z), 0.03f);
            }
            b.Model("ceramic_pot", new Vector3(0.85f, 0f, -0.75f), 0.65f, 20f);
            b.Model("ceramic_pot", new Vector3(-0.85f, 0f, -0.7f), 0.55f, 120f);
        }

        static void PotteryKiln(Builder b)
        {
            b.Shape(Meshes.Dome(), new Vector3(0.1f, 0f, 0.25f), new Vector3(1.3f, 1.2f, 1.3f), "Mud");
            b.Shape(Meshes.Cone(), new Vector3(0.1f, 0.55f, 0.25f), new Vector3(0.35f, 0.35f, 0.35f), "Mud");
            b.PrimColor(PrimitiveType.Cube, new Vector3(0.1f, 0.22f, -0.42f), new Vector3(0.4f, 0.4f, 0.08f), "Ash");
            b.Fire(new Vector3(0.1f, 0.05f, -0.5f), 0.45f);
            for (int i = 0; i < 6; i++)
                b.Model("ceramic_pot", new Vector3(-0.85f + (i % 3) * 0.3f, 0f, -0.6f + (i / 3) * 0.35f), 0.45f + (i % 2) * 0.1f, i * 47f);
            b.Pole(new Vector3(0.75f, 0.08f, -0.8f), new Vector3(0.95f, 0.08f, 0.2f), 0.16f);
        }

        static void CopperSmelter(Builder b)
        {
            for (int row = 0; row < 3; row++)
            {
                int n = 9 - row * 2;
                float r = 0.55f - row * 0.1f;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + row * 0.5f) / n * Mathf.PI * 2f;
                    b.Model("stone_01", new Vector3(Mathf.Cos(a) * r, row * 0.18f, Mathf.Sin(a) * r), 3.4f - row * 0.5f, i * 41f);
                }
            }
            b.Fire(new Vector3(0, 0.45f, 0), 0.6f);
            b.Model("rock_07", new Vector3(0.7f, 0f, 0.55f), 0.25f, 30f);
            b.Model("rock_07", new Vector3(0.85f, 0f, 0.25f), 0.18f, 100f);
            // Bellows of hide and a crucible pot
            b.Prim(PrimitiveType.Cube, new Vector3(-0.75f, 0.15f, -0.3f), new Vector3(0.4f, 0.22f, 0.3f), "Hide", new Vector3(0, 25, 0));
            b.Pole(new Vector3(-0.55f, 0.12f, -0.25f), new Vector3(-0.3f, 0.2f, -0.1f), 0.05f);
            b.Model("ceramic_pot", new Vector3(0.3f, 0f, -0.75f), 0.45f, 0f);
        }

        static void BronzeWorkshop(Builder b)
        {
            // Open shed: four posts under a thatch roof.
            foreach (var c in new[] { new Vector3(-0.75f, 0, -0.75f), new Vector3(0.75f, 0, -0.75f), new Vector3(-0.75f, 0, 0.75f), new Vector3(0.75f, 0, 0.75f) })
                b.Pole(c, c + new Vector3(0, 1.35f, 0), 0.09f);
            b.Shape(Meshes.Roof(), new Vector3(0, 1.3f, 0), new Vector3(2.4f, 1.1f, 2.4f), "Thatch");
            b.Model("stone_fire_pit", new Vector3(-0.3f, 0f, 0.25f), 0.45f);
            b.Fire(new Vector3(-0.3f, 0.05f, 0.25f), 0.45f);
            b.Model("boulder_01", new Vector3(0.35f, 0f, -0.2f), 0.22f, 40f);
            b.Model("wooden_axe", new Vector3(0.4f, 0.32f, -0.2f), 0.8f, 0f, null, new Vector3(0, 60, 80));
            b.Model("ceramic_pot", new Vector3(0.55f, 0f, 0.5f), 0.5f);
            b.Model("wooden_bowl_01", new Vector3(-0.6f, 0f, -0.55f), 1.1f);
        }
}
}
