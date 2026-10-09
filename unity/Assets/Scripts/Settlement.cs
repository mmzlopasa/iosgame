using System.Collections.Generic;
using UnityEngine;

namespace Eonward
{
    /// <summary>Lighting, fog and sky for the frozen world. Applied at build time (so fog shader
    /// variants are kept) and again at runtime.</summary>
    public static class SceneEnvironment
    {
        public static readonly Color FogColor = new Color(0.76f, 0.83f, 0.92f);

        public static void Apply()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.62f, 0.74f);
        }

        public static Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            return sun;
        }
    }

    /// <summary>
    /// Builds the starting frozen settlement around the Furnace from code.
    /// Positions are in metres; the furnace is the world origin.
    /// </summary>
    public class Settlement
    {
        public readonly Transform Root;
        public readonly List<Vector3> WorkSpots = new List<Vector3>();
        public Transform FurnaceTop { get; private set; }

        public Settlement(Transform parent)
        {
            Root = new GameObject("Settlement").transform;
            Root.SetParent(parent, false);
        }

        public void BuildAll()
        {
            Ground();
            Furnace(Vector3.zero);

            // Ring of survivor tents around the furnace's warmth.
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) / 8f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 7.5f;
                Tent(p, -a * Mathf.Rad2Deg + 90f);
            }

            Sawmill(new Vector3(-13f, 0, 6f), 30f);
            HuntersHut(new Vector3(12f, 0, 9f), -40f);
            CoalMine(new Vector3(14f, 0, -9f), -120f);
            Cookhouse(new Vector3(-11f, 0, -10f), 140f);
            Infirmary(new Vector3(0f, 0, -15f), 180f);
            Palisade(21f);
            Forest();
            Drifts();
        }

        void Ground()
        {
            var ground = Parts.Prim(PrimitiveType.Plane, Root, Vector3.zero, new Vector3(30, 1, 30), "Snow", collider: true);
            ground.name = "Ground";
            // Trodden snow around the furnace and paths out to the work sites.
            Parts.Prim(PrimitiveType.Cylinder, Root, new Vector3(0, 0.005f, 0), new Vector3(13f, 0.01f, 13f), "SnowTrodden");
            foreach (var target in new[] { new Vector3(-13f, 0, 6f), new Vector3(12f, 0, 9f), new Vector3(14f, 0, -9f),
                                           new Vector3(-11f, 0, -10f), new Vector3(0f, 0, -15f) })
            {
                var mid = target * 0.5f;
                float yaw = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg;
                Parts.Prim(PrimitiveType.Cube, Root, new Vector3(mid.x, 0.006f, mid.z),
                           new Vector3(1.8f, 0.01f, target.magnitude), "SnowTrodden", new Vector3(0, yaw, 0));
            }
        }

        void Furnace(Vector3 at)
        {
            var g = Parts.Group("Furnace", Root, at).transform;
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 0.4f, 0), new Vector3(5.2f, 0.4f, 5.2f), "DarkStone");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 1.6f, 0), new Vector3(3.6f, 1.0f, 3.6f), "Metal");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 2.7f, 0), new Vector3(3.9f, 0.12f, 3.9f), "Rust");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 3.1f, 0), new Vector3(2.6f, 0.4f, 2.6f), "Metal");
            // Glowing fire mouths on four sides.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Parts.Prim(PrimitiveType.Cube, g, dir * 1.78f + new Vector3(0, 1.3f, 0), new Vector3(1.0f, 0.8f, 0.15f), "Ember",
                           new Vector3(0, a, 0));
                // Rivet bands
                Parts.Prim(PrimitiveType.Cube, g, dir * 1.83f + new Vector3(0, 2.1f, 0), new Vector3(1.4f, 0.12f, 0.08f), "Rust",
                           new Vector3(0, a, 0));
            }
            // Chimney
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 5.2f, 0), new Vector3(1.1f, 2.0f, 1.1f), "Metal");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 7.25f, 0), new Vector3(1.4f, 0.12f, 1.4f), "Rust");
            FurnaceTop = Parts.Group("ChimneyTop", g, new Vector3(0, 7.4f, 0)).transform;
            // Coal heap
            for (int i = 0; i < 5; i++)
            {
                Parts.Shape(Meshes.Rock(), g, new Vector3(3.1f + i * 0.25f, 0.25f, -1.2f + i * 0.55f),
                            Vector3.one * (0.7f + (i % 2) * 0.25f), "Coal", new Vector3(0, i * 40f, 0));
            }

            var lightGo = Parts.Group("FurnaceLight", g, new Vector3(0, 2.2f, 0));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 16f;
            light.intensity = 2.2f;
            lightGo.AddComponent<Flicker>();

            WorkSpots.Add(at + new Vector3(3.5f, 0, 0.5f));
        }

        void Tent(Vector3 at, float yaw)
        {
            var g = Parts.Group("Tent", Root, at, yaw).transform;
            Parts.Shape(Meshes.Prism(), g, Vector3.zero, new Vector3(2.4f, 1.9f, 3.0f), "Canvas");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.45f, -1.51f), new Vector3(0.7f, 0.9f, 0.04f), "DarkWood");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 1.95f, 0), new Vector3(0.08f, 1.65f, 0.08f), "DarkWood",
                       new Vector3(90, 0, 0));
            Parts.Shape(Meshes.Rock(), g, new Vector3(0.9f, 0.1f, 1.2f), new Vector3(1.4f, 0.5f, 1.2f), "Snow");
        }

        void Sawmill(Vector3 at, float yaw)
        {
            var g = Parts.Group("Sawmill", Root, at, yaw).transform;
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 1.1f, 0), new Vector3(4f, 2.2f, 3f), "Wood");
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 2.2f, 0), new Vector3(4.6f, 1.6f, 3.4f), "Roof", new Vector3(0, 90, 0));
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 3.55f, 0), new Vector3(4.0f, 0.3f, 3.6f), "Snow", new Vector3(0, 90, 0));
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0.8f, 1.2f, -1.51f), new Vector3(0.8f, 0.6f, 0.04f), "Window");
            // Log pile
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < 4 - row; i++)
                {
                    Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(3.2f, 0.25f + row * 0.42f, -1f + i * 0.45f + row * 0.22f),
                               new Vector3(0.42f, 1.1f, 0.42f), "DarkWood", new Vector3(0, 0, 90));
                }
            }
            WorkSpots.Add(g.TransformPoint(new Vector3(2.5f, 0, -2f)));
        }

        void HuntersHut(Vector3 at, float yaw)
        {
            var g = Parts.Group("HuntersHut", Root, at, yaw).transform;
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.9f, 0), new Vector3(3f, 1.8f, 2.6f), "DarkWood");
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 1.8f, 0), new Vector3(3.6f, 1.4f, 3.0f), "Snow");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.6f, -1.31f), new Vector3(0.7f, 1.2f, 0.04f), "Wood");
            // Drying rack with meat
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(2.4f, 0.9f, -0.8f), new Vector3(0.08f, 0.9f, 0.08f), "Wood");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(2.4f, 0.9f, 0.8f), new Vector3(0.08f, 0.9f, 0.08f), "Wood");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(2.4f, 1.75f, 0f), new Vector3(0.06f, 0.85f, 0.06f), "Wood",
                       new Vector3(90, 0, 0));
            for (int i = 0; i < 3; i++)
            {
                Parts.Prim(PrimitiveType.Capsule, g, new Vector3(2.4f, 1.35f, -0.5f + i * 0.5f), new Vector3(0.22f, 0.3f, 0.22f), "Meat");
            }
            WorkSpots.Add(g.TransformPoint(new Vector3(2f, 0, -1.8f)));
        }

        void CoalMine(Vector3 at, float yaw)
        {
            var g = Parts.Group("CoalMine", Root, at, yaw).transform;
            Parts.Shape(Meshes.Rock(), g, new Vector3(0, 0.6f, 1f), new Vector3(6f, 3.2f, 4.5f), "Stone");
            Parts.Shape(Meshes.Rock(), g, new Vector3(0.3f, 2.0f, 1.2f), new Vector3(4f, 1.2f, 3f), "Snow");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.9f, -1.2f), new Vector3(1.6f, 1.8f, 0.3f), "Coal");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(-0.9f, 1f, -1.3f), new Vector3(0.2f, 2f, 0.2f), "Wood");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0.9f, 1f, -1.3f), new Vector3(0.2f, 2f, 0.2f), "Wood");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 2f, -1.3f), new Vector3(2.1f, 0.22f, 0.25f), "Wood");
            // Mine cart on rails
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.02f, -3f), new Vector3(0.9f, 0.04f, 3f), "Metal");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.45f, -3.4f), new Vector3(0.9f, 0.55f, 1.1f), "Rust");
            Parts.Shape(Meshes.Rock(), g, new Vector3(0, 0.8f, -3.4f), new Vector3(0.8f, 0.4f, 1f), "Coal");
            WorkSpots.Add(g.TransformPoint(new Vector3(1.3f, 0, -2.5f)));
        }

        void Cookhouse(Vector3 at, float yaw)
        {
            var g = Parts.Group("Cookhouse", Root, at, yaw).transform;
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 1f, 0), new Vector3(3.6f, 2f, 3f), "Stone");
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 2f, 0), new Vector3(4.2f, 1.5f, 3.4f), "Roof");
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 3.25f, 0), new Vector3(3.6f, 0.3f, 3.5f), "Snow");
            Parts.Prim(PrimitiveType.Cylinder, g, new Vector3(1.1f, 3.2f, 0.6f), new Vector3(0.5f, 0.8f, 0.5f), "DarkStone");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(-0.8f, 1.1f, -1.51f), new Vector3(0.8f, 0.7f, 0.04f), "Window");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0.7f, 0.65f, -1.51f), new Vector3(0.8f, 1.3f, 0.04f), "DarkWood");
            WorkSpots.Add(g.TransformPoint(new Vector3(0f, 0, -2.2f)));
        }

        void Infirmary(Vector3 at, float yaw)
        {
            var g = Parts.Group("Infirmary", Root, at, yaw).transform;
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 1f, 0), new Vector3(4.4f, 2f, 3f), "Canvas");
            Parts.Shape(Meshes.Prism(), g, new Vector3(0, 2f, 0), new Vector3(4.8f, 1.2f, 3.4f), "Canvas", new Vector3(0, 90, 0));
            // Red cross
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 1.3f, -1.52f), new Vector3(0.9f, 0.25f, 0.04f), "Meat");
            Parts.Prim(PrimitiveType.Cube, g, new Vector3(0, 1.3f, -1.52f), new Vector3(0.25f, 0.9f, 0.04f), "Meat");
            WorkSpots.Add(g.TransformPoint(new Vector3(-1.5f, 0, -2.2f)));
        }

        void Palisade(float radius)
        {
            var g = Parts.Group("Palisade", Root, Vector3.zero).transform;
            int count = 120;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                // Gate gap facing the camera side.
                float deg = a * Mathf.Rad2Deg;
                if (deg > 222f && deg < 238f) continue;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                float h = 1.6f + Mathf.PerlinNoise(i * 0.7f, 0.3f) * 0.6f;
                Parts.Prim(PrimitiveType.Cylinder, g, p + new Vector3(0, h * 0.5f, 0), new Vector3(0.45f, h * 0.5f, 0.45f), "Wood");
                Parts.Shape(Meshes.Cone(), g, p + new Vector3(0, h, 0), new Vector3(0.45f, 0.4f, 0.45f), "Snow");
            }
        }

        void Forest()
        {
            var g = Parts.Group("Forest", Root, Vector3.zero).transform;
            var rng = new System.Random(7);
            for (int i = 0; i < 160; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 24f + (float)rng.NextDouble() * 34f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float s = 0.8f + (float)rng.NextDouble() * 0.8f;
                PineTree(g, p, s);
            }
            // A few frozen trees inside the walls.
            PineTree(g, new Vector3(-5f, 0, 15f), 0.9f);
            PineTree(g, new Vector3(16f, 0, 2f), 1.1f);
            PineTree(g, new Vector3(-17f, 0, -3f), 1.0f);
        }

        static void PineTree(Transform parent, Vector3 p, float s)
        {
            var t = Parts.Group("Pine", parent, p, p.x * 37f).transform;
            t.localScale = Vector3.one * s;
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.6f, 0.3f), "DarkWood");
            Parts.Shape(Meshes.Cone(), t, new Vector3(0, 1.0f, 0), new Vector3(2.6f, 2.2f, 2.6f), "Pine");
            Parts.Shape(Meshes.Cone(), t, new Vector3(0, 2.2f, 0), new Vector3(2.0f, 1.9f, 2.0f), "Pine");
            Parts.Shape(Meshes.Cone(), t, new Vector3(0, 3.3f, 0), new Vector3(1.3f, 1.6f, 1.3f), "Snow");
        }

        void Drifts()
        {
            var g = Parts.Group("Drifts", Root, Vector3.zero).transform;
            var rng = new System.Random(11);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 9f + (float)rng.NextDouble() * 40f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                bool ice = i % 5 == 0;
                float s = 0.8f + (float)rng.NextDouble() * 1.6f;
                Parts.Shape(Meshes.Rock(), g, p, new Vector3(s * 1.6f, s * (ice ? 1.1f : 0.45f), s), ice ? "Ice" : "Snow",
                            new Vector3(0, (float)rng.NextDouble() * 360f, 0));
            }
        }
    }
}
