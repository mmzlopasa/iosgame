using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>Lighting and atmosphere. Applied at build time (so fog variants ship) and at runtime.</summary>
    public static class SceneEnvironment
    {
        public static readonly Color FogColor = new Color(0.66f, 0.73f, 0.80f);

        public static void Apply()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 230f;
            RenderSettings.fogColor = FogColor;
        }

        public static Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(38f, -62f, 0f);
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.80f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
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
    /// Draws the settlement from the game state: textured terrain with the river, water,
    /// forests and rocks, buildings with construction
    /// progress, placement preview, selection, trodden paths and villagers.
    /// It never changes game state itself.
    /// </summary>
    public class WorldView : MonoBehaviour
    {
        GameStore store;
        public WorldTerrain Ground { get; private set; }
        Transform objectRoot, overlayRoot, peopleRoot;

        GrassField grassField;
        NatureField nature;
        readonly Dictionary<int, BuildingView> buildings = new Dictionary<int, BuildingView>();
        readonly Dictionary<int, (string key, GameObject go)> decorations = new Dictionary<int, (string, GameObject)>();
        string occupancyKey;
        readonly List<GameObject> selection = new List<GameObject>();
        readonly List<GameObject> ghost = new List<GameObject>();
        readonly List<Villager> villagers = new List<Villager>();
        string ghostKey = "";
        int selectionKey = -1;
        string splatKey;
        string lastStage;

        static readonly string[] Pines = { "PineA", "PineB", "PineC" };

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
            buildings.Clear();
            decorations.Clear();
            occupancyKey = null;
            selection.Clear();
            ghost.Clear();
            villagers.Clear();
            ghostKey = "";
            selectionKey = -1;
            splatKey = null;

            Ground = new WorldTerrain(store.Game.Content.Map, transform);
            objectRoot = Parts.Group("Buildings", transform, Vector3.zero).transform;
            overlayRoot = Parts.Group("Overlay", transform, Vector3.zero).transform;
            peopleRoot = Parts.Group("People", transform, Vector3.zero).transform;
            BuildWater();
            BuildNature();
            grassField = Parts.Group("Grass", transform, Vector3.zero).AddComponent<GrassField>();
            grassField.Init(Ground, store.Game);
            Sync(true);
        }

        // MARK: - Water

        void BuildWater()
        {
            var center = Grid.CellCenter((store.Game.Content.Map.Width - 1) * 0.5f, (store.Game.Content.Map.Height - 1) * 0.5f);
            var water = Parts.Prim(PrimitiveType.Quad, transform, new Vector3(center.x, WorldTerrain.WaterLevel, center.z),
                                   new Vector3(WorldTerrain.Size, WorldTerrain.Size, 1f), Palette.Get("Water"), new Vector3(90, 0, 0), false);
            water.name = "Water";
            water.GetComponent<Renderer>().receiveShadows = true;
        }

        // MARK: - Nature

        /// <summary>Cells of land around the map that still get trees and rocks.</summary>
        const int NatureMargin = 30;

        void BuildNature()
        {
            var map = store.Game.Content.Map;
            nature = Parts.Group("Nature", transform, Vector3.zero).AddComponent<NatureField>();
            for (int cy = -NatureMargin; cy < map.Height + NatureMargin; cy++)
            {
                for (int cx = -NatureMargin; cx < map.Width + NatureMargin; cx++)
                {
                    bool inside = cx >= 0 && cy >= 0 && cx < map.Width && cy < map.Height;
                    var type = Ground.CellType(cx, cy);
                    // Far outside the map only every other open cell gets props.
                    if (!inside && (cx + cy) % 2 != 0 && type != TerrainType.Forest) continue;
                    var rng = new System.Random(cx * 73856093 ^ cy * 19349663 ^ 0x5bd1e995);
                    PopulateCell(type, cx, cy, rng, inside);
                }
            }
            nature.Build();
            Debug.Log($"[FOUNDERS] nature: {nature.Count} objects");
        }

        Vector3 Jitter(Vector3 c, System.Random rng, float amount) =>
            Ground.OnGround(c + new Vector3(((float)rng.NextDouble() - 0.5f) * amount, 0, ((float)rng.NextDouble() - 0.5f) * amount));

        void PopulateCell(TerrainType type, int cx, int cy, System.Random rng, bool inside)
        {
            float yaw() => (float)rng.NextDouble() * 360f;
            var c = Grid.CellCenter(cx, cy);
            void Model(string id, Vector3 p, float scale, bool shadows = true, Material m = null) =>
                nature.AddModel(id, p, scale, yaw(), cx, cy, shadows, m);
            void Tree(string kind, Vector3 p, float h) => nature.AddTree(kind, p, h, yaw(), cx, cy);

            switch (type)
            {
                case TerrainType.Forest:
                {
                    int trees = inside ? 1 + rng.Next(2) : 1 + (rng.Next(3) == 0 ? 1 : 0);
                    for (int i = 0; i < trees; i++)
                    {
                        bool pine = rng.Next(3) != 0;
                        string kind = pine ? Pines[rng.Next(Pines.Length)] : "LeafTree";
                        float h = pine ? 8f + (float)rng.NextDouble() * 5f : 5.5f + (float)rng.NextDouble() * 2.5f;
                        Tree(kind, Jitter(c, rng, 1.6f), h);
                    }
                    if (inside && rng.Next(2) == 0) Model("fern_02", Jitter(c, rng, 1.6f), 0.8f, false);
                    if (inside && rng.Next(4) == 0) Model("dry_branches_medium_01", Jitter(c, rng, 1.6f), 1f, false);
                    if (rng.Next(6) == 0) Model("tree_stump_01", Jitter(c, rng, 1.4f), 0.6f, false);
                    break;
                }
                case TerrainType.Rock:
                {
                    if (rng.Next(2) == 0) Model("rock_moss_set_01", Jitter(c, rng, 0.8f), 0.24f + (float)rng.NextDouble() * 0.08f);
                    else Model("namaqualand_boulder_02", Jitter(c, rng, 0.8f), 0.7f + (float)rng.NextDouble() * 0.4f);
                    if (rng.Next(2) == 0) Model("boulder_01", Jitter(c, rng, 1.6f), 0.45f);
                    if (rng.Next(5) == 0) Model("shrub_03", Jitter(c, rng, 1.4f), 0.9f, false);
                    break;
                }
                case TerrainType.Ore:
                {
                    // Veins of rusty iron and green copper stone at the mountain foot.
                    var ore = rng.Next(4) == 0 ? ModelLibrary.CopperOre : ModelLibrary.IronOre;
                    Model("boulder_01", Jitter(c, rng, 0.9f), 0.5f + (float)rng.NextDouble() * 0.3f, true, ore);
                    for (int i = 0; i < 3; i++) Model("stone_01", Jitter(c, rng, 1.6f), 3f + (float)rng.NextDouble() * 2f, false, ore);
                    if (rng.Next(2) == 0) Model("rock_07", Jitter(c, rng, 1.4f), 0.3f);
                    break;
                }
                case TerrainType.Mountain:
                {
                    var p = Jitter(c, rng, 1.6f);
                    // Pines climb the lower slopes; boulders lie in the scree above.
                    if (p.y < 11f && rng.Next(3) == 0) Tree(Pines[rng.Next(Pines.Length)], p, 6f + (float)rng.NextDouble() * 4f);
                    else if (rng.Next(3) == 0) Model("namaqualand_boulder_02", p, 0.9f + (float)rng.NextDouble() * 1.2f);
                    break;
                }
                case TerrainType.Sand:
                {
                    if (rng.Next(3) == 0) Model("stone_01", Jitter(c, rng, 1.6f), 3.5f, false);
                    if (rng.Next(4) == 0) Model("nettle_plant", Jitter(c, rng, 1.4f), 0.9f, false);
                    break;
                }
                case TerrainType.Grass:
                {
                    int r = rng.Next(10);
                    if (r == 0) Model("shrub_03", Jitter(c, rng, 1.4f), 1f);
                    else if (r == 1) Model("shrub_04", Jitter(c, rng, 1.4f), 1.4f);
                    else if (r == 2 && !inside) Tree("LeafTree", Jitter(c, rng, 1.4f), 5f);
                    else if (r == 3 && inside) Tree("FirSapling", Jitter(c, rng, 1.4f), 1.6f);
                    break;
                }
            }
        }

        // MARK: - Sync

        void Update()
        {
            foreach (var view in buildings.Values)
            {
                if (view.PopUntil <= 0f) continue;
                float k = 1f - Mathf.Clamp01((view.PopUntil - Time.time) / 0.35f);
                view.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, Mathf.SmoothStep(0, 1, k));
                if (k >= 1f) view.PopUntil = 0f;
            }
        }

        /// <summary>Updates objects to match the game state. Cheap enough to call several times per second.</summary>
        public void Sync(bool initial = false)
        {
            if (store == null) return;
            var game = store.Game;

            var seen = new HashSet<int>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                seen.Add(b.Id);
                string key = $"{b.DefinitionId}|{b.Status}|{b.Rotation}";
                if (buildings.TryGetValue(b.Id, out var view) && view.Key == key)
                {
                    SetProgress(view, b);
                    continue;
                }
                bool wasThere = view != null;
                if (view != null) Destroy(view.Root);
                view = CreateBuilding(b, def, key);
                buildings[b.Id] = view;
                if (!initial && (wasThere || b.Status == BuildingStatus.Constructing)) view.PopUntil = Time.time + 0.35f;
            }
            foreach (var id in buildings.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(buildings[id].Root);
                buildings.Remove(id);
            }

            SyncDecorations(game);
            UpdateOccupancy(game);
            RepaintGround(game, initial);
            UpdateSelection();
            UpdateGhost();
            UpdateVillagers(game);
        }

        BuildingView CreateBuilding(PlacedBuilding b, BuildingDefinition def, string key)
        {
            var center = Ground.OnGround(Grid.FootprintCenter(b.X, b.Y, store.Game.FootprintOf(b)));
            var root = Parts.Group($"Building_{b.Id}", objectRoot, center, b.Rotation * 90f);
            var model = BuildingModels.Create(def.Sprite, root.transform, null, Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height));
            var bar = Parts.Group("Progress", root.transform, new Vector3(0, 2.6f, 0));
            bar.transform.rotation = Quaternion.Euler(0, CameraRig.Yaw, 0);
            Parts.Prim(PrimitiveType.Cube, bar.transform, Vector3.zero, new Vector3(1.4f, 0.16f, 0.06f), "BarBack");
            var fill = Parts.Prim(PrimitiveType.Cube, bar.transform, new Vector3(0, 0, -0.04f), new Vector3(1.3f, 0.1f, 0.06f), "BarFill").transform;
            var view = new BuildingView { Key = key, Root = root, Model = model.transform, Bar = bar, BarFill = fill };
            SetProgress(view, b);
            return view;
        }

        static void SetProgress(BuildingView view, PlacedBuilding b)
        {
            bool active = b.Status != BuildingStatus.Complete;
            view.Bar.SetActive(active);
            float p = Mathf.Clamp01((float)b.Progress);
            float h = b.Status == BuildingStatus.Constructing ? Mathf.Lerp(0.2f, 1f, p) : 1f;
            view.Model.localScale = new Vector3(1f, h, 1f);
            if (active)
            {
                float w = Mathf.Max(0.02f, p) * 1.3f;
                view.BarFill.localScale = new Vector3(w, 0.1f, 0.06f);
                view.BarFill.localPosition = new Vector3(-0.65f + w * 0.5f, 0, -0.04f);
            }
        }

        /// <summary>Trodden ground around buildings and, in the village stage, paths to the hearth.</summary>
        void RepaintGround(Game game, bool initial)
        {
            string key = game.State.StageId + "#" + string.Join(",", game.State.Buildings.Select(b => $"{b.X}:{b.Y}:{b.DefinitionId}")) +
                         "#" + string.Join(",", game.State.Decorations.Select(d => $"{d.X}:{d.Y}:{d.DefinitionId}"));
            if (key == splatKey) return;
            bool firstVillage = !initial && lastStage == game.Content.StartStage && game.State.StageId != game.Content.StartStage;
            lastStage = game.State.StageId;
            splatKey = key;
            var around = new List<(Vector3, float)>();
            var paths = new List<(Vector3, Vector3)>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                around.Add((Grid.FootprintCenter(b.X, b.Y, def.Footprint), 0.9f * Mathf.Max(def.Footprint.Width, def.Footprint.Height)));
            }
            if (game.State.StageId != game.Content.StartStage)
            {
                var hearth = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "village_hearth");
                if (hearth != null)
                {
                    var start = Grid.CellCenter(hearth.X, hearth.Y);
                    foreach (var b in game.State.Buildings)
                    {
                        if (b.Id == hearth.Id) continue;
                        var def = game.Content.Building(b.DefinitionId);
                        if (def != null) paths.Add((start, Grid.FootprintCenter(b.X, b.Y, def.Footprint)));
                    }
                    if (firstVillage && store.Rig != null) store.Rig.SetFocus(start, zoom: 22f);
                }
            }
            Ground.PaintSplat(around, paths, RoadSegments(game));
        }

        /// <summary>Road cells joined to their road neighbours (a lone cell becomes a short stub).</summary>
        static List<WorldTerrain.RoadSegment> RoadSegments(Game game)
        {
            var roads = new Dictionary<(int, int), bool>();
            foreach (var d in game.State.Decorations)
            {
                var def = game.Content.Decoration(d.DefinitionId);
                if (def != null && def.IsRoad) roads[(d.X, d.Y)] = d.DefinitionId != "dirt_path";
            }
            var list = new List<WorldTerrain.RoadSegment>();
            foreach (var kv in roads)
            {
                var (x, y) = kv.Key;
                var c = Grid.CellCenter(x, y);
                bool any = false;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                {
                    if (!roads.TryGetValue((x + dx, y + dy), out bool stone)) continue;
                    any = true;
                    // Each joint is drawn once, from the cell with the smaller coordinates.
                    if (dx < 0 || dy < 0) continue;
                    list.Add(new WorldTerrain.RoadSegment(c, Grid.CellCenter(x + dx, y + dy), kv.Value && stone));
                }
                if (!any) list.Add(new WorldTerrain.RoadSegment(c + new Vector3(-0.5f, 0, 0), c + new Vector3(0.5f, 0, 0), kv.Value));
                else list.Add(new WorldTerrain.RoadSegment(c, c, kv.Value));
            }
            return list;
        }

        // MARK: - Decorations

        static readonly (int dx, int dy, int bit)[] Neighbours =
        {
            (0, -1, BuildingModels.North), (1, 0, BuildingModels.East), (0, 1, BuildingModels.South), (-1, 0, BuildingModels.West),
        };

        void SyncDecorations(Game game)
        {
            var at = new Dictionary<(int, int), string>();
            foreach (var d in game.State.Decorations) at[(d.X, d.Y)] = d.DefinitionId;
            var seen = new HashSet<int>();
            foreach (var d in game.State.Decorations)
            {
                var def = game.Content.Decoration(d.DefinitionId);
                if (def == null || def.IsRoad) continue;
                seen.Add(d.Id);
                int connect = 0;
                foreach (var (dx, dy, bit) in Neighbours)
                    if (at.TryGetValue((d.X + dx, d.Y + dy), out var other) && other == d.DefinitionId) connect |= bit;
                string key = $"{d.DefinitionId}|{d.Rotation}|{connect}";
                if (decorations.TryGetValue(d.Id, out var view) && view.key == key) continue;
                if (view.go != null) Destroy(view.go);
                var go = BuildingModels.Decoration(def.Sprite, objectRoot, null, d.Rotation, connect);
                go.transform.position = Ground.OnGround(Grid.CellCenter(d.X, d.Y));
                decorations[d.Id] = (key, go);
            }
            foreach (var id in decorations.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(decorations[id].go);
                decorations.Remove(id);
            }
        }

        public static HashSet<(int, int)> OccupiedCells(Game game)
        {
            var used = new HashSet<(int, int)>();
            foreach (var b in game.State.Buildings)
            {
                var fp = game.FootprintOf(b);
                for (int dy = 0; dy < fp.Height; dy++)
                    for (int dx = 0; dx < fp.Width; dx++) used.Add((b.X + dx, b.Y + dy));
            }
            foreach (var d in game.State.Decorations) used.Add((d.X, d.Y));
            return used;
        }

        /// <summary>Trees, rocks and grass disappear from cells the player builds on.</summary>
        void UpdateOccupancy(Game game)
        {
            string key = string.Join(",", game.State.Buildings.Select(b => $"{b.X}:{b.Y}:{b.Rotation}")) + "#" +
                         string.Join(",", game.State.Decorations.Select(d => $"{d.X}:{d.Y}"));
            if (key == occupancyKey) return;
            bool first = occupancyKey == null;
            occupancyKey = key;
            var used = OccupiedCells(game);
            nature.SetBlocked((x, y) => used.Contains((x, y)));
            if (!first && grassField != null) grassField.Rebuild();
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
            var fp = store.Game.FootprintOf(b);
            for (int dy = 0; dy < fp.Height; dy++)
                for (int dx = 0; dx < fp.Width; dx++)
                    selection.Add(Marker(b.X + dx, b.Y + dy, "Select"));
        }

        GameObject Marker(int x, int y, string material)
        {
            var c = Ground.OnGround(Grid.CellCenter(x, y));
            var frame = Parts.Group("Marker", overlayRoot, c + new Vector3(0, 0.06f, 0)).transform;
            float s = Grid.CellSize;
            var m = Palette.Get(material);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, s * 0.46f), new Vector3(s * 0.96f, 0.06f, 0.1f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, -s * 0.46f), new Vector3(s * 0.96f, 0.06f, 0.1f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(s * 0.46f, 0, 0), new Vector3(0.1f, 0.06f, s * 0.96f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(-s * 0.46f, 0, 0), new Vector3(0.1f, 0.06f, s * 0.96f), m, default, false);
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

            string mat = valid ? "GhostOk" : "GhostBad";
            for (int dy = 0; dy < def.Footprint.Height; dy++)
                for (int dx = 0; dx < def.Footprint.Width; dx++)
                    ghost.Add(Marker(cell.Value.x + dx, cell.Value.y + dy, mat));
            var center = Ground.OnGround(Grid.FootprintCenter(cell.Value.x, cell.Value.y, def.Footprint));
            var preview = Parts.Group("GhostModel", overlayRoot, center);
            BuildingModels.Create(def.Sprite, preview.transform, valid ? null : Palette.Get("GhostBad"), Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height));
            if (!valid) preview.transform.localScale = new Vector3(1f, 0.6f, 1f);
            ghost.Add(preview);
        }

        // MARK: - Villagers

        void UpdateVillagers(Game game)
        {
            int wanted = Mathf.Min(game.State.Population, 24);
            while (villagers.Count < wanted)
            {
                var v = Villager.Spawn(peopleRoot, RandomSpot(game), villagers.Count);
                v.PickSpot = () => RandomSpot(store.Game);
                int index = villagers.Count;
                v.NextTask = () => TaskFor(index);
                v.Ground = Ground;
                villagers.Add(v);
            }
            while (villagers.Count > wanted)
            {
                Destroy(villagers[villagers.Count - 1].gameObject);
                villagers.RemoveAt(villagers.Count - 1);
            }
        }

        // MARK: - Villager jobs (people go where their work is)

        static readonly Dictionary<JobType, string> JobAction = new Dictionary<JobType, string>
        {
            [JobType.Foraging] = "Gather", [JobType.Farming] = "Gather", [JobType.Woodcutting] = "Work",
            [JobType.Stonecutting] = "Work", [JobType.Crafting] = "Work", [JobType.Construction] = "Work",
            [JobType.Research] = "Talk", [JobType.Fishing] = "Work", [JobType.Hunting] = "Gather", [JobType.Mining] = "Work",
        };

        /// <summary>The job of villager #index, from the current assignments (in job order), or null if idle.</summary>
        JobType? JobOf(Game game, int index)
        {
            int k = 0;
            foreach (var job in Enums.Jobs)
            {
                int n = game.State.Assignments[job];
                if (index < k + n) return job;
                k += n;
            }
            return null;
        }

        Vector3 Around(Vector3 center, float radius) =>
            Ground.OnGround(center + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * radius);

        /// <summary>Nearest cell of a terrain type around a building (wood camps send people into the trees).</summary>
        Vector3? NearestTerrain(Vector3 from, TerrainType type, int range = 6)
        {
            var (cx, cy) = Grid.CellAt(from);
            Vector3? best = null;
            float bestD = float.MaxValue;
            for (int y = cy - range; y <= cy + range; y++)
                for (int x = cx - range; x <= cx + range; x++)
                {
                    if (Ground.CellType(x, y) != type) continue;
                    var c = Grid.CellCenter(x, y);
                    float d = (c - from).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        VillagerTask TaskFor(int index)
        {
            var game = store.Game;
            var task = new VillagerTask { Action = "Idle", Duration = Random.Range(3f, 7f) };
            var hearth = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "campfire" || b.DefinitionId == "village_hearth");
            Vector3 hearthPos = hearth != null ? Grid.CellCenter(hearth.X, hearth.Y) : Vector3.zero;

            // Night: everyone gathers around the hearth or rests by the houses.
            if (Atmosphere.Nightness > 0.55f)
            {
                if (index % 3 != 0 || hearth == null)
                {
                    task.Spot = Around(hearthPos, Random.Range(1.6f, 2.4f));
                    task.Action = Random.value < 0.6f ? "Sit" : "Talk";
                }
                else task.Spot = RandomSpot(game);
                task.Duration = Random.Range(6f, 12f);
                return task;
            }

            var job = JobOf(game, index);
            if (job == null)
            {
                task.Spot = Around(hearthPos, Random.Range(1.6f, 2.6f));
                task.Action = Random.value < 0.5f ? "Talk" : "Idle";
                return task;
            }

            task.Action = JobAction.TryGetValue(job.Value, out var action) ? action : "Work";
            task.Duration = Random.Range(5f, 10f);
            PlacedBuilding site = null;
            if (job == JobType.Construction)
                site = game.State.Buildings.FirstOrDefault(b => b.Status != BuildingStatus.Complete);
            else
            {
                var places = game.OperationalBuildings().Where(x => x.def.Slots(job.Value) > 0).Select(x => x.building).ToList();
                if (places.Count > 0) site = places[index % places.Count];
            }
            if (site == null)
            {
                task.Spot = Around(hearthPos, 2f);
                task.Action = "Idle";
                return task;
            }
            var def = game.Content.Building(site.DefinitionId);
            var center = def == null ? Grid.CellCenter(site.X, site.Y) : Grid.FootprintCenter(site.X, site.Y, def.Footprint);
            float reach = Grid.CellSize * 0.5f * Mathf.Max(def?.Footprint.Width ?? 1, def?.Footprint.Height ?? 1);
            // Gatherers walk out to the resource itself.
            if (job == JobType.Woodcutting && NearestTerrain(center, TerrainType.Forest) is Vector3 forest && Random.value < 0.7f)
                task.Spot = Around(forest, Random.Range(0.6f, 1.4f));
            else if (job == JobType.Stonecutting && NearestTerrain(center, TerrainType.Rock) is Vector3 rock && Random.value < 0.7f)
                task.Spot = Around(rock, Random.Range(0.6f, 1.2f));
            else if (job == JobType.Mining && NearestTerrain(center, TerrainType.Ore) is Vector3 ore && Random.value < 0.75f)
                task.Spot = Around(ore, Random.Range(0.5f, 1.0f));
            else if (job == JobType.Fishing && NearestTerrain(center, TerrainType.Water, 3) is Vector3 water)
                task.Spot = Around(Vector3.Lerp(water, center, 0.55f), Random.Range(0.1f, 0.5f));
            else if (job == JobType.Hunting && NearestTerrain(center, TerrainType.Forest, 9) is Vector3 woods && Random.value < 0.8f)
                task.Spot = Around(woods, Random.Range(1f, 4f));
            else if (job == JobType.Farming)
                task.Spot = Ground.OnGround(center + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f)));
            else
                task.Spot = Around(center, reach + Random.Range(0.5f, 1.1f));
            return task;
        }

        Vector3 RandomSpot(Game game)
        {
            var list = game.State.Buildings;
            if (list.Count == 0) return Vector3.zero;
            var b = list[Random.Range(0, list.Count)];
            var def = game.Content.Building(b.DefinitionId);
            var center = def == null ? Grid.CellCenter(b.X, b.Y) : Grid.FootprintCenter(b.X, b.Y, def.Footprint);
            // Stand around the building, never on it (the hearth sits in the middle of its cell).
            float reach = Grid.CellSize * (0.55f + 0.5f * Mathf.Max(def?.Footprint.Width ?? 1, def?.Footprint.Height ?? 1)) + Random.Range(0f, 0.6f);
            var offset = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * reach;
            return Ground.OnGround(center + offset);
        }
    }
}
