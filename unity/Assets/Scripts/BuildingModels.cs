using UnityEngine;

namespace Founders
{
    /// <summary>
    /// PLACEHOLDER ART: low-poly models assembled from primitives, one per building "sprite"
    /// key in content.json. The origin is the centre of the footprint on the ground;
    /// one grid cell is <see cref="Grid.CellSize"/> metres wide.
    /// </summary>
    public static class BuildingModels
    {
        public static GameObject Create(string sprite, Transform parent, int footprint)
        {
            var root = new GameObject("Model_" + sprite);
            root.transform.SetParent(parent, false);
            var t = root.transform;
            switch (sprite)
            {
                case "campfire": Campfire(t); break;
                case "village_hearth": Hearth(t); break;
                case "shelter": Shelter(t); break;
                case "woven_shelter": WovenShelter(t); break;
                case "foraging_camp": ForagingCamp(t); break;
                case "wood_camp": WoodCamp(t); break;
                case "stone_site": StoneSite(t); break;
                case "storage_pit": StoragePit(t); break;
                case "toolmaker": Toolmaker(t); break;
                case "community_circle": CommunityCircle(t); break;
                default:
                    Parts.Shape(Meshes.Prism(), t, Vector3.zero, new Vector3(1.2f, 1.2f, 1.2f), "GhostBad");
                    break;
            }
            return root;
        }

        static void StoneRing(Transform t, float radius, int count, float size, string material = "Stone")
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                Parts.Shape(Meshes.Rock(), t, new Vector3(Mathf.Cos(a) * radius, size * 0.3f, Mathf.Sin(a) * radius),
                            new Vector3(size, size * 0.7f, size), material, new Vector3(0, i * 47f, 0));
            }
        }

        /// <summary>Fire with flickering light, smoke and sparks.</summary>
        static void Fire(Transform t, Vector3 at, float scale)
        {
            Parts.Shape(Meshes.Cone(), t, at, new Vector3(0.45f, 0.75f, 0.45f) * scale, "Ember");
            Parts.Shape(Meshes.Cone(), t, at + new Vector3(0.05f, 0, 0.04f), new Vector3(0.25f, 0.95f, 0.25f) * scale, "Flame");
            var lightGo = Parts.Group("FireLight", t, at + new Vector3(0, 0.8f, 0));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 6f * scale;
            light.intensity = 1.6f;
            lightGo.AddComponent<Flicker>();
            var top = Parts.Group("FireTop", t, at + new Vector3(0, 0.7f * scale, 0)).transform;
            Effects.Smoke(top, scale);
            Effects.Sparks(top);
        }

        static void Campfire(Transform t)
        {
            StoneRing(t, 0.55f, 9, 0.28f);
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.02f, 0.9f), "Ash");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.45f, 0.12f), "DarkWood", new Vector3(0, 30, 80));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.45f, 0.12f), "DarkWood", new Vector3(0, 150, 80));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0), new Vector3(0.12f, 0.45f, 0.12f), "DarkWood", new Vector3(0, 270, 80));
            Fire(t, new Vector3(0, 0.08f, 0), 1f);
            // Log seats
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.2f, 0.12f, 0.85f), new Vector3(0.22f, 0.4f, 0.22f), "Wood", new Vector3(0, 0, 90));
        }

        static void Hearth(Transform t)
        {
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0), new Vector3(1.6f, 0.12f, 1.6f), "Stone");
            StoneRing(t, 0.7f, 12, 0.3f, "DarkRock");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.26f, 0), new Vector3(1.0f, 0.03f, 1.0f), "Ash");
            Fire(t, new Vector3(0, 0.28f, 0), 1.35f);
            // Paved ring around the hearth
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Parts.Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * 0.95f, 0.02f, Mathf.Sin(a) * 0.95f),
                           new Vector3(0.35f, 0.05f, 0.25f), "Stone", new Vector3(0, -a * Mathf.Rad2Deg, 0));
            }
        }

        static void Shelter(Transform t)
        {
            // Lean-to: a sloped hide roof on a ridge pole.
            Parts.Shape(Meshes.Prism(), t, new Vector3(0, 0, 0), new Vector3(1.5f, 1.25f, 1.5f), "Hide", new Vector3(0, 45, 0));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 1.25f, 0), new Vector3(0.06f, 1.0f, 0.06f), "DarkWood", new Vector3(90, 45, 0));
            Parts.Prim(PrimitiveType.Cube, t, new Vector3(0.36f, 0.32f, -0.36f), new Vector3(0.4f, 0.64f, 0.03f), "Ash", new Vector3(0, 45, 0));
        }

        static void WovenShelter(Transform t)
        {
            StoneRing(t, 0.72f, 10, 0.22f);
            Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0, 0.15f, 0), new Vector3(1.4f, 1.6f, 1.4f), "Weave");
            Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0, 0.85f, 0), new Vector3(0.6f, 0.35f, 0.6f), "Thatch");
            for (int i = 1; i <= 3; i++)
                Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.25f * i, 0), new Vector3(1.42f - i * 0.12f, 0.015f, 1.42f - i * 0.12f), "Basket");
            Parts.Prim(PrimitiveType.Cube, t, new Vector3(0.43f, 0.3f, -0.43f), new Vector3(0.4f, 0.6f, 0.05f), "Ash", new Vector3(0, 45, 0));
        }

        static void Bush(Transform t, Vector3 at, float s)
        {
            Parts.Prim(PrimitiveType.Sphere, t, at + new Vector3(0, 0.3f * s, 0), new Vector3(0.8f, 0.6f, 0.8f) * s, "Leaves2");
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f;
                Parts.Prim(PrimitiveType.Sphere, t, at + new Vector3(Mathf.Cos(a) * 0.32f * s, (0.35f + (i % 2) * 0.12f) * s, Mathf.Sin(a) * 0.32f * s),
                           Vector3.one * 0.11f * s, "Berry");
            }
        }

        static void ForagingCamp(Transform t)
        {
            Bush(t, new Vector3(-0.45f, 0, -0.35f), 1f);
            Bush(t, new Vector3(0.45f, 0, 0.4f), 0.85f);
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0.25f, 0.18f, -0.3f), new Vector3(0.4f, 0.18f, 0.4f), "Basket");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.3f, 0.14f, 0.45f), new Vector3(0.32f, 0.14f, 0.32f), "Basket");
            Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0.25f, 0.34f, -0.3f), new Vector3(0.32f, 0.12f, 0.32f), "Berry");
            // Drying rack
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0.7f, 0.4f, -0.6f), new Vector3(0.05f, 0.4f, 0.05f), "DarkWood");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.1f, 0.4f, -0.85f), new Vector3(0.05f, 0.4f, 0.05f), "DarkWood");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0.3f, 0.78f, -0.72f), new Vector3(0.04f, 0.45f, 0.04f), "DarkWood", new Vector3(0, 72, 90));
        }

        static void WoodCamp(Transform t)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < 4 - row; i++)
                {
                    Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.2f + i * 0.28f + row * 0.14f, 0.13f + row * 0.24f, -0.2f),
                               new Vector3(0.26f, 0.6f, 0.26f), "Wood", new Vector3(90, 0, 0));
                }
            }
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.6f, 0.18f, 0.5f), new Vector3(0.5f, 0.18f, 0.5f), "Bark");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.6f, 0.37f, 0.5f), new Vector3(0.45f, 0.01f, 0.45f), "Weave");
            // Axe in the stump
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.5f, 0.6f, 0.5f), new Vector3(0.04f, 0.25f, 0.04f), "DarkWood", new Vector3(0, 0, -25));
            Parts.Shape(Meshes.Rock(), t, new Vector3(-0.6f, 0.42f, 0.5f), new Vector3(0.18f, 0.1f, 0.06f), "DarkRock");
            Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0.6f, 0.05f, 0.55f), new Vector3(0.5f, 0.08f, 0.4f), "Thatch");
        }

        static void StoneSite(Transform t)
        {
            Parts.Shape(Meshes.Rock(), t, new Vector3(-0.35f, 0.25f, 0.2f), new Vector3(0.8f, 0.6f, 0.7f), "Boulder", new Vector3(0, 20, 0));
            Parts.Shape(Meshes.Rock(), t, new Vector3(0.35f, 0.18f, -0.3f), new Vector3(0.6f, 0.4f, 0.55f), "DarkRock", new Vector3(0, 80, 0));
            for (int i = 0; i < 3; i++)
                Parts.Prim(PrimitiveType.Cube, t, new Vector3(0.45f, 0.1f + i * 0.17f, 0.45f), new Vector3(0.45f, 0.16f, 0.3f), "Stone",
                           new Vector3(0, i * 12f, 0));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(-0.6f, 0.35f, -0.5f), new Vector3(0.04f, 0.35f, 0.04f), "DarkWood", new Vector3(0, 0, 30));
        }

        static void StoragePit(Transform t)
        {
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.08f, 0), new Vector3(1.5f, 0.08f, 1.5f), "Dirt");
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.12f, 0), new Vector3(1.1f, 0.06f, 1.1f), "Ash");
            for (int i = 0; i < 4; i++)
                Parts.Prim(PrimitiveType.Cube, t, new Vector3(-0.3f + i * 0.2f, 0.18f, 0.05f), new Vector3(0.18f, 0.05f, 1.15f), "Wood",
                           new Vector3(0, 8f, 0));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0.6f, 0.2f, -0.55f), new Vector3(0.36f, 0.2f, 0.36f), "Basket");
            Parts.Prim(PrimitiveType.Sphere, t, new Vector3(-0.65f, 0.15f, -0.5f), new Vector3(0.4f, 0.3f, 0.4f), "Hide");
        }

        static void Toolmaker(Transform t)
        {
            Parts.Shape(Meshes.Prism(), t, new Vector3(-0.25f, 0, 0.25f), new Vector3(1.3f, 1.1f, 1.2f), "Hide", new Vector3(0, 45, 0));
            Parts.Shape(Meshes.Rock(), t, new Vector3(0.45f, 0.2f, -0.35f), new Vector3(0.6f, 0.4f, 0.5f), "Boulder");
            Parts.Shape(Meshes.Rock(), t, new Vector3(0.45f, 0.43f, -0.35f), new Vector3(0.25f, 0.1f, 0.18f), "DarkRock", new Vector3(0, 30, 0));
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0.75f, 0.42f, -0.2f), new Vector3(0.04f, 0.18f, 0.04f), "DarkWood", new Vector3(0, 0, 70));
            for (int i = 0; i < 4; i++)
                Parts.Shape(Meshes.Rock(), t, new Vector3(0.1f + i * 0.12f, 0.03f, -0.7f + (i % 2) * 0.1f), Vector3.one * 0.1f, "Stone");
        }

        static void CommunityCircle(Transform t)
        {
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.01f, 0), new Vector3(3.4f, 0.01f, 3.4f), "Path");
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                float h = 0.9f + (i % 3) * 0.2f;
                Parts.Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * 1.5f, h * 0.5f, Mathf.Sin(a) * 1.5f),
                           new Vector3(0.28f, h, 0.2f), i % 2 == 0 ? "Stone" : "Boulder", new Vector3(0, -a * Mathf.Rad2Deg, 0));
            }
            StoneRing(t, 0.35f, 7, 0.18f);
            Fire(t, new Vector3(0, 0.05f, 0), 0.7f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(Mathf.Cos(a) * 0.95f, 0.12f, Mathf.Sin(a) * 0.95f),
                           new Vector3(0.2f, 0.35f, 0.2f), "Wood", new Vector3(0, -a * Mathf.Rad2Deg, 90));
            }
        }
    }
}
