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
        Transform natureRoot, objectRoot, overlayRoot, peopleRoot;

        GrassField grassField;
        readonly Dictionary<(int, int), GameObject> cellProps = new Dictionary<(int, int), GameObject>();
        readonly Dictionary<int, BuildingView> buildings = new Dictionary<int, BuildingView>();
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
            cellProps.Clear();
            buildings.Clear();
            selection.Clear();
            ghost.Clear();
            villagers.Clear();
            ghostKey = "";
            selectionKey = -1;
            splatKey = null;

            Ground = new WorldTerrain(store.Game.Content.Map, transform);
            natureRoot = Parts.Group("Nature", transform, Vector3.zero).transform;
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

        void BuildNature()
        {
            var game = store.Game;
            var map = game.Content.Map;
            int margin = 20;
            for (int cy = -margin; cy < map.Height + margin; cy++)
            {
                for (int cx = -margin; cx < map.Width + margin; cx++)
                {
                    bool inside = cx >= 0 && cy >= 0 && cx < map.Width && cy < map.Height;
                    var type = Ground.CellType(cx, cy);
                    var rng = new System.Random(cx * 73856093 ^ cy * 19349663 ^ 0x5bd1e995);
                    var c = Grid.CellCenter(cx, cy);
                    var props = Parts.Group($"Cell_{cx}_{cy}", natureRoot, Vector3.zero).transform;
                    // Outside the map only every other far cell gets props, to keep the draw count sane.
                    if (!inside && (cx + cy) % 2 != 0 && type != TerrainType.Forest) { Destroy(props.gameObject); continue; }
                    PopulateCell(props, type, c, rng, inside);
                    if (props.childCount == 0) { Destroy(props.gameObject); continue; }
                    if (inside)
                    {
                        cellProps[(cx, cy)] = props.gameObject;
                    }
                }
            }
        }

        Vector3 Jitter(Vector3 c, System.Random rng, float amount) =>
            Ground.OnGround(c + new Vector3(((float)rng.NextDouble() - 0.5f) * amount, 0, ((float)rng.NextDouble() - 0.5f) * amount));

        void PopulateCell(Transform t, TerrainType type, Vector3 c, System.Random rng, bool inside)
        {
            float yaw() => (float)rng.NextDouble() * 360f;
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
                        ModelLibrary.Tree(kind, t, Jitter(c, rng, 1.6f), h, yaw());
                    }
                    if (inside && rng.Next(2) == 0) ModelLibrary.Place("fern_02", t, Jitter(c, rng, 1.6f), 0.8f, yaw());
                    if (inside && rng.Next(4) == 0) ModelLibrary.Place("dry_branches_medium_01", t, Jitter(c, rng, 1.6f), 1f, yaw());
                    if (rng.Next(6) == 0) ModelLibrary.Place("tree_stump_01", t, Jitter(c, rng, 1.4f), 0.6f, yaw());
                    break;
                }
                case TerrainType.Rock:
                {
                    if (rng.Next(2) == 0)
                        ModelLibrary.Place("rock_moss_set_01", t, Jitter(c, rng, 0.8f), 0.24f + (float)rng.NextDouble() * 0.08f, yaw());
                    else
                        ModelLibrary.Place("namaqualand_boulder_02", t, Jitter(c, rng, 0.8f), 0.7f + (float)rng.NextDouble() * 0.4f, yaw());
                    if (rng.Next(2) == 0) ModelLibrary.Place("boulder_01", t, Jitter(c, rng, 1.6f), 0.45f, yaw());
                    break;
                }
                case TerrainType.Sand:
                {
                    if (rng.Next(3) == 0) ModelLibrary.Place("stone_01", t, Jitter(c, rng, 1.6f), 3.5f, yaw());
                    if (rng.Next(4) == 0) ModelLibrary.Place("nettle_plant", t, Jitter(c, rng, 1.4f), 0.9f, yaw());
                    break;
                }
                case TerrainType.Grass:
                {
                    int r = rng.Next(10);
                    if (r == 0) ModelLibrary.Place("shrub_03", t, Jitter(c, rng, 1.4f), 1f, yaw());
                    else if (r == 1) ModelLibrary.Place("shrub_04", t, Jitter(c, rng, 1.4f), 1.4f, yaw());
                    else if (r == 2 && !inside) ModelLibrary.Tree("LeafTree", t, Jitter(c, rng, 1.4f), 5f, yaw());
                    else if (r == 3 && inside) ModelLibrary.Tree("FirSapling", t, Jitter(c, rng, 1.4f), 1.6f, yaw());
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
                if (!initial && (wasThere || b.Status == BuildingStatus.Constructing)) view.PopUntil = Time.time + 0.35f;
            }
            foreach (var id in buildings.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(buildings[id].Root);
                buildings.Remove(id);
            }

            RepaintGround(game, initial);
            UpdateSelection();
            UpdateGhost();
            UpdateVillagers(game);
        }

        BuildingView CreateBuilding(PlacedBuilding b, BuildingDefinition def, string key)
        {
            var center = Ground.OnGround(Grid.FootprintCenter(b.X, b.Y, def.Footprint));
            var root = Parts.Group($"Building_{b.Id}", objectRoot, center, (b.Id * 47) % 360);
            var model = BuildingModels.Create(def.Sprite, root.transform, null);
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
            string key = game.State.StageId + "#" + string.Join(",", game.State.Buildings.Select(b => $"{b.X}:{b.Y}:{b.DefinitionId}"));
            if (key == splatKey) return;
            bool firstVillage = !initial && lastStage == game.Content.StartStage && game.State.StageId != game.Content.StartStage;
            lastStage = game.State.StageId;
            splatKey = key;
            if (!initial && grassField != null) grassField.Rebuild();
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
            Ground.PaintSplat(around, paths);
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
            BuildingModels.Create(def.Sprite, preview.transform, valid ? null : Palette.Get("GhostBad"));
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
                v.Ground = Ground;
                villagers.Add(v);
            }
            while (villagers.Count > wanted)
            {
                Destroy(villagers[villagers.Count - 1].gameObject);
                villagers.RemoveAt(villagers.Count - 1);
            }
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
