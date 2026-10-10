using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>Lighting and sky. Applied at build time (so fog shader variants ship) and at runtime.</summary>
    public static class SceneEnvironment
    {
        public static readonly Color SkyColor = new Color(0.62f, 0.74f, 0.80f);

        public static void Apply()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 110f;
            RenderSettings.fogColor = SkyColor;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.60f);
        }

        public static Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(48f, -20f, 0f);
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            return sun;
        }
    }

    /// <summary>Grid ↔ world mapping. Cell (x, y) of content.json sits at world (x·S, 0, −y·S).</summary>
    public static class Grid
    {
        public const float CellSize = 2f;

        public static Vector3 CellCenter(float x, float y) => new Vector3(x * CellSize, 0f, -y * CellSize);

        public static Vector3 FootprintCenter(int x, int y, Footprint fp) =>
            CellCenter(x + (fp.Width - 1) * 0.5f, y + (fp.Height - 1) * 0.5f);

        public static (int x, int y) CellAt(Vector3 world) =>
            (Mathf.RoundToInt(world.x / CellSize), Mathf.RoundToInt(-world.z / CellSize));
    }

    /// <summary>
    /// Draws the settlement from the game state: terrain from the content map, fog over
    /// unexplored regions, buildings with construction progress, placement ghost, selection,
    /// village paths and villagers. It never changes game state itself.
    /// </summary>
    public class WorldView : MonoBehaviour
    {
        GameStore store;
        Transform terrainRoot, objectRoot, overlayRoot, pathRoot, peopleRoot;

        readonly Dictionary<(int, int), GameObject> fog = new Dictionary<(int, int), GameObject>();
        readonly Dictionary<(int, int), GameObject> cellProps = new Dictionary<(int, int), GameObject>();
        readonly List<(Transform t, float started)> fading = new List<(Transform, float)>();
        readonly Dictionary<int, BuildingView> buildings = new Dictionary<int, BuildingView>();
        readonly List<GameObject> selection = new List<GameObject>();
        readonly List<GameObject> ghost = new List<GameObject>();
        readonly List<Villager> villagers = new List<Villager>();
        string ghostKey = "";
        int selectionKey = -1;
        string pathsKey;

        class BuildingView
        {
            public string Key;
            public GameObject Root;
            public Transform Model;
            public GameObject Bar;
            public Transform BarFill;
            public float PopUntil;
        }

        public void Init(GameStore owner)
        {
            store = owner;
            RebuildAll();
        }

        public void RebuildAll()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            fog.Clear();
            cellProps.Clear();
            fading.Clear();
            buildings.Clear();
            selection.Clear();
            ghost.Clear();
            villagers.Clear();
            ghostKey = "";
            selectionKey = -1;
            pathsKey = null;

            terrainRoot = Parts.Group("Terrain", transform, Vector3.zero).transform;
            objectRoot = Parts.Group("Buildings", transform, Vector3.zero).transform;
            overlayRoot = Parts.Group("Overlay", transform, Vector3.zero).transform;
            pathRoot = Parts.Group("Paths", transform, Vector3.zero).transform;
            peopleRoot = Parts.Group("People", transform, Vector3.zero).transform;
            BuildTerrain();
            Sync(true);
        }

        // MARK: - Terrain

        void BuildTerrain()
        {
            var game = store.Game;
            var map = game.Content.Map;
            float s = Grid.CellSize;
            var staticRoot = Parts.Group("Static", terrainRoot, Vector3.zero);

            // Land beyond the map edge so the world does not end in a void.
            Parts.Prim(PrimitiveType.Cube, staticRoot.transform,
                       Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f) + new Vector3(0, -0.45f, 0),
                       new Vector3(map.Width * s + 120f, 0.5f, map.Height * s + 120f), "FarGround");

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var terrain = map.Terrain(x, y);
                    if (terrain == null) continue;
                    var c = Grid.CellCenter(x, y);
                    int variant = (x * 7 + y * 13) % 3;
                    string mat;
                    float top = 0f;
                    switch (terrain.Value)
                    {
                        case TerrainType.Water: mat = "Water"; top = -0.28f; break;
                        case TerrainType.Sand: mat = "Sand"; top = -0.04f; break;
                        case TerrainType.Forest: mat = "ForestFloor"; break;
                        case TerrainType.Rock: mat = "RockGround"; break;
                        default: mat = variant == 0 ? "Grass" : variant == 1 ? "Grass2" : "Grass3"; break;
                    }
                    Parts.Prim(PrimitiveType.Cube, staticRoot.transform, new Vector3(c.x, top - 0.3f, c.z), new Vector3(s, 0.6f, s), mat);

                    // Trees and boulders live in their own group so they can stay hidden under fog.
                    var props = Parts.Group($"Props_{x}_{y}", terrainRoot, c);
                    var rng = new System.Random(x * 73856093 ^ y * 19349663);
                    if (terrain == TerrainType.Forest)
                    {
                        int trees = 2 + rng.Next(2);
                        for (int i = 0; i < trees; i++)
                        {
                            var p = new Vector3((float)rng.NextDouble() * 1.3f - 0.65f, 0, (float)rng.NextDouble() * 1.3f - 0.65f);
                            Tree(props.transform, p, 0.75f + (float)rng.NextDouble() * 0.5f, rng.Next(3));
                        }
                    }
                    else if (terrain == TerrainType.Rock)
                    {
                        Parts.Shape(Meshes.Rock(), props.transform, new Vector3(-0.2f, 0.3f, 0.1f), new Vector3(1.3f, 0.9f, 1.1f), "Boulder",
                                    new Vector3(0, rng.Next(360), 0));
                        Parts.Shape(Meshes.Rock(), props.transform, new Vector3(0.45f, 0.15f, -0.45f), new Vector3(0.6f, 0.4f, 0.6f), "DarkRock",
                                    new Vector3(0, rng.Next(360), 0));
                    }
                    else if (terrain == TerrainType.Grass && rng.Next(5) == 0)
                    {
                        Parts.Prim(PrimitiveType.Sphere, props.transform, new Vector3((float)rng.NextDouble() - 0.5f, 0.05f, (float)rng.NextDouble() - 0.5f),
                                   new Vector3(0.35f, 0.15f, 0.35f), "Leaves2");
                    }
                    if (props.transform.childCount == 0) Destroy(props);
                    else cellProps[(x, y)] = props;

                    if (!store.Game.IsRevealed(x, y))
                    {
                        var f = Parts.Prim(PrimitiveType.Cube, overlayRoot, c + new Vector3(0, 0.9f, 0),
                                           new Vector3(s * 1.01f, 1.8f + variant * 0.15f, s * 1.01f), "Fog");
                        fog[(x, y)] = f;
                        if (cellProps.TryGetValue((x, y), out var hidden)) hidden.SetActive(false);
                    }
                }
            }

            // A distant tree line around the map edge.
            var rngEdge = new System.Random(5);
            var edge = Parts.Group("TreeLine", staticRoot.transform, Vector3.zero).transform;
            for (int i = 0; i < 140; i++)
            {
                float gx = (float)rngEdge.NextDouble() * (map.Width + 16) - 8;
                float gy = (float)rngEdge.NextDouble() * (map.Height + 16) - 8;
                if (gx > -1.5f && gx < map.Width + 0.5f && gy > -1.5f && gy < map.Height + 0.5f) continue;
                Tree(edge, Grid.CellCenter(gx, gy) + new Vector3(0, -0.2f, 0), 0.9f + (float)rngEdge.NextDouble() * 0.6f, rngEdge.Next(3));
            }

            StaticBatchingUtility.Combine(staticRoot);
        }

        static void Tree(Transform parent, Vector3 p, float scale, int kind)
        {
            var t = Parts.Group("Tree", parent, p, p.x * 53f + p.z * 17f).transform;
            t.localScale = Vector3.one * scale;
            Parts.Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.5f, 0), new Vector3(0.22f, 0.5f, 0.22f), "Bark");
            if (kind == 0)
            {
                Parts.Shape(Meshes.Cone(), t, new Vector3(0, 0.7f, 0), new Vector3(1.3f, 1.5f, 1.3f), "Pine");
                Parts.Shape(Meshes.Cone(), t, new Vector3(0, 1.5f, 0), new Vector3(1.0f, 1.3f, 1.0f), "Pine");
            }
            else
            {
                string leaves = kind == 1 ? "Leaves" : "Leaves2";
                Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.45f, 0), new Vector3(1.3f, 1.1f, 1.3f), leaves);
                Parts.Prim(PrimitiveType.Sphere, t, new Vector3(0.3f, 1.9f, 0.1f), new Vector3(0.8f, 0.7f, 0.8f), leaves);
            }
        }

        // MARK: - Sync

        void Update()
        {
            // Fog fade-out and building pop-in animations.
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                var (t, started) = fading[i];
                float k = (Time.time - started) / 0.8f;
                if (t == null || k >= 1f)
                {
                    if (t != null) Destroy(t.gameObject);
                    fading.RemoveAt(i);
                    continue;
                }
                var sc = t.localScale;
                t.localScale = new Vector3(sc.x, Mathf.Lerp(t.localScale.y, 0.01f, k), sc.z);
            }
            foreach (var view in buildings.Values)
            {
                if (view.PopUntil <= 0f) continue;
                float k = 1f - Mathf.Clamp01((view.PopUntil - Time.time) / 0.3f);
                view.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0, 1, k));
                if (k >= 1f) view.PopUntil = 0f;
            }
        }

        /// <summary>Updates objects to match the game state. Cheap enough to call several times per second.</summary>
        public void Sync(bool initial = false)
        {
            if (store == null) return;
            var game = store.Game;

            // Fog of war
            foreach (var key in fog.Keys.ToList())
            {
                if (!game.IsRevealed(key.Item1, key.Item2)) continue;
                fading.Add((fog[key].transform, Time.time));
                fog.Remove(key);
                if (cellProps.TryGetValue(key, out var props)) props.SetActive(true);
            }

            // Buildings
            var seen = new HashSet<int>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                seen.Add(b.Id);
                string key = $"{b.DefinitionId}|{b.Status}";
                if (buildings.TryGetValue(b.Id, out var view) && view.Key == key)
                {
                    SetProgress(view, b);
                    continue;
                }
                bool wasThere = view != null;
                if (view != null) Destroy(view.Root);
                view = CreateBuilding(b, def, key);
                buildings[b.Id] = view;
                if (!initial && (wasThere || b.Status == BuildingStatus.Constructing)) view.PopUntil = Time.time + 0.3f;
            }
            foreach (var id in buildings.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(buildings[id].Root);
                buildings.Remove(id);
            }

            DrawVillagePaths(game, initial);
            UpdateSelection();
            UpdateGhost();
            UpdateVillagers(game);
        }

        BuildingView CreateBuilding(PlacedBuilding b, BuildingDefinition def, string key)
        {
            var root = Parts.Group($"Building_{b.Id}", objectRoot, Grid.FootprintCenter(b.X, b.Y, def.Footprint));
            var model = BuildingModels.Create(def.Sprite, root.transform, Mathf.Max(def.Footprint.Width, def.Footprint.Height));
            var bar = Parts.Group("Progress", root.transform, new Vector3(0, 2.2f, 0), CameraRig.Yaw);
            Parts.Prim(PrimitiveType.Cube, bar.transform, Vector3.zero, new Vector3(1.3f, 0.16f, 0.06f), "BarBack");
            var fill = Parts.Prim(PrimitiveType.Cube, bar.transform, new Vector3(0, 0, -0.04f), new Vector3(1.2f, 0.1f, 0.06f), "BarFill").transform;
            var view = new BuildingView { Key = key, Root = root, Model = model.transform, Bar = bar, BarFill = fill };
            SetProgress(view, b);
            return view;
        }

        static void SetProgress(BuildingView view, PlacedBuilding b)
        {
            bool active = b.Status != BuildingStatus.Complete;
            view.Bar.SetActive(active);
            float p = Mathf.Clamp01((float)b.Progress);
            // Under construction the model rises from the ground; upgrades keep the full model.
            float h = b.Status == BuildingStatus.Constructing ? Mathf.Lerp(0.25f, 1f, p) : 1f;
            view.Model.localScale = new Vector3(1f, h, 1f);
            if (active)
            {
                float w = Mathf.Max(0.02f, p) * 1.2f;
                view.BarFill.localScale = new Vector3(w, 0.1f, 0.06f);
                view.BarFill.localPosition = new Vector3(-0.6f + w * 0.5f, 0, -0.04f);
            }
        }

        void DrawVillagePaths(Game game, bool initial)
        {
            string stage = game.State.StageId;
            string key = $"{stage}#{game.State.Buildings.Count}";
            if (stage == game.Content.StartStage || pathsKey == key) return;
            bool firstTime = pathsKey == null && !initial;
            pathsKey = key;
            foreach (Transform child in pathRoot) Destroy(child.gameObject);
            var hearth = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "village_hearth");
            if (hearth == null) return;
            var start = Grid.CellCenter(hearth.X, hearth.Y);
            foreach (var b in game.State.Buildings)
            {
                if (b.Id == hearth.Id) continue;
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                var end = Grid.FootprintCenter(b.X, b.Y, def.Footprint);
                var mid = (start + end) * 0.5f;
                var dir = end - start;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                Parts.Prim(PrimitiveType.Cube, pathRoot, new Vector3(mid.x, 0.015f, mid.z), new Vector3(0.55f, 0.02f, dir.magnitude), "Path",
                           new Vector3(0, yaw, 0));
            }
            if (firstTime && store.Rig != null) store.Rig.SetFocus(start, zoom: 22f);
        }

        // MARK: - Selection & placement ghost

        void UpdateSelection()
        {
            int id = store.PlacingBuildingId == null && store.SelectedBuildingId.HasValue ? store.SelectedBuildingId.Value : -1;
            var b = id >= 0 ? store.Game.State.Building(id) : null;
            var def = b == null ? null : store.Game.Content.Building(b.DefinitionId);
            int key = def == null ? -1 : id * 1000 + def.Footprint.Width;
            if (key == selectionKey) return;
            selectionKey = key;
            foreach (var go in selection) Destroy(go);
            selection.Clear();
            if (def == null) return;
            for (int dy = 0; dy < def.Footprint.Height; dy++)
                for (int dx = 0; dx < def.Footprint.Width; dx++)
                    selection.Add(Marker(b.X + dx, b.Y + dy, "Select", 0.02f));
        }

        GameObject Marker(int x, int y, string material, float height)
        {
            var c = Grid.CellCenter(x, y);
            var frame = Parts.Group("Marker", overlayRoot, new Vector3(c.x, height, c.z)).transform;
            float s = Grid.CellSize;
            // Four edge bars make a frame; the ground stays visible inside it.
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, s * 0.46f), new Vector3(s * 0.96f, 0.05f, 0.1f), material);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, -s * 0.46f), new Vector3(s * 0.96f, 0.05f, 0.1f), material);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(s * 0.46f, 0, 0), new Vector3(0.1f, 0.05f, s * 0.96f), material);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(-s * 0.46f, 0, 0), new Vector3(0.1f, 0.05f, s * 0.96f), material);
            return frame.gameObject;
        }

        void UpdateGhost()
        {
            var id = store.PlacingBuildingId;
            var cell = store.GhostCell;
            var def = id == null ? null : store.Game.Content.Building(id);
            if (def == null || cell == null)
            {
                if (ghost.Count > 0) { foreach (var go in ghost) Destroy(go); ghost.Clear(); }
                ghostKey = "";
                return;
            }
            bool valid = store.PlacementIssues().Count == 0;
            string key = $"{id}|{cell.Value.x},{cell.Value.y}|{valid}";
            if (key == ghostKey) return;
            ghostKey = key;
            foreach (var go in ghost) Destroy(go);
            ghost.Clear();

            for (int dy = 0; dy < def.Footprint.Height; dy++)
                for (int dx = 0; dx < def.Footprint.Width; dx++)
                    ghost.Add(Marker(cell.Value.x + dx, cell.Value.y + dy, valid ? "GhostOk" : "GhostBad", 0.06f));
            var preview = Parts.Group("GhostModel", overlayRoot, Grid.FootprintCenter(cell.Value.x, cell.Value.y, def.Footprint));
            BuildingModels.Create(def.Sprite, preview.transform, Mathf.Max(def.Footprint.Width, def.Footprint.Height));
            if (!valid)
            {
                // Sink the invalid preview a little and tint it with a red marker pillar.
                preview.transform.localScale = new Vector3(1f, 0.6f, 1f);
            }
            ghost.Add(preview);
        }

        // MARK: - Villagers

        void UpdateVillagers(Game game)
        {
            int wanted = Mathf.Min(game.State.Population, 24);
            while (villagers.Count < wanted)
            {
                var spot = RandomSpot(game);
                var v = Villager.Spawn(peopleRoot, spot, villagers.Count);
                v.PickSpot = () => RandomSpot(store.Game);
                villagers.Add(v);
            }
            while (villagers.Count > wanted)
            {
                Destroy(villagers[villagers.Count - 1].gameObject);
                villagers.RemoveAt(villagers.Count - 1);
            }
        }

        static Vector3 RandomSpot(Game game)
        {
            var list = game.State.Buildings;
            if (list.Count == 0) return Vector3.zero;
            var b = list[Random.Range(0, list.Count)];
            var def = game.Content.Building(b.DefinitionId);
            var center = def == null ? Grid.CellCenter(b.X, b.Y) : Grid.FootprintCenter(b.X, b.Y, def.Footprint);
            // Stand next to the building, not inside it.
            var offset = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * (Grid.CellSize * 0.75f);
            return center + offset;
        }
    }
}
