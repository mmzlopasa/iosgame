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
        /// <summary>Tall decorations (trees, totems, banners) that step aside when they block the view.</summary>
        readonly Dictionary<int, (Transform t, float height, Renderer[] renderers, bool hidden)> tallDecor =
            new Dictionary<int, (Transform, float, Renderer[], bool)>();
        float nextDecorCheck;
        static readonly Dictionary<string, float> TallDecorations = new Dictionary<string, float>
        {
            ["young_tree"] = 4.2f, ["totem"] = 3.2f, ["banner"] = 3.4f, ["oil_lantern"] = 2.1f, ["torch_post"] = 1.8f,
        };
        string occupancyKey;
        float nextGroundPaint, nextGrassRebuild;
        bool grassDirty;
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
            tallDecor.Clear();
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
                    // Outcrops here and there; the hills show their stony ground between them.
                    int r = rng.Next(10);
                    if (r < 3) Model("rock_moss_set_01", Jitter(c, rng, 0.8f), 0.24f + (float)rng.NextDouble() * 0.08f);
                    else if (r < 5) Model("namaqualand_boulder_02", Jitter(c, rng, 0.8f), 0.7f + (float)rng.NextDouble() * 0.5f);
                    if (rng.Next(4) == 0) Model("boulder_01", Jitter(c, rng, 1.6f), 0.45f);
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
                    if (p.y < 7f && rng.Next(3) == 0) Tree(Pines[rng.Next(Pines.Length)], p, 6f + (float)rng.NextDouble() * 4f);
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

        void UpdateTallDecor()
        {
            if (Time.unscaledTime < nextDecorCheck || tallDecor.Count == 0) return;
            nextDecorCheck = Time.unscaledTime + 0.15f;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var id in tallDecor.Keys.ToList())
            {
                var v = tallDecor[id];
                if (v.t == null) continue;
                bool hide = NatureField.Occludes(cam.transform.position, cam.transform.forward, v.t.position, v.height);
                if (hide == v.hidden) continue;
                foreach (var r in v.renderers)
                    if (r != null && !(r is ParticleSystemRenderer)) r.enabled = !hide;
                tallDecor[id] = (v.t, v.height, v.renderers, hide);
            }
        }

        void Update()
        {
            UpdateTallDecor();
            if (grassDirty && grassField != null && Time.unscaledTime >= nextGrassRebuild)
            {
                grassDirty = false;
                nextGrassRebuild = Time.unscaledTime + 0.6f;
                grassField.Rebuild();
            }
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
            // Repainting the ground is heavy: while a road is being drawn, do it a few times a second at most.
            if (!initial && Time.unscaledTime < nextGroundPaint) return;
            nextGroundPaint = Time.unscaledTime + 0.35f;
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
                if (TallDecorations.TryGetValue(def.Sprite, out float h))
                    tallDecor[d.Id] = (go.transform, h, go.GetComponentsInChildren<Renderer>(), false);
            }
            foreach (var id in decorations.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(decorations[id].go);
                decorations.Remove(id);
                tallDecor.Remove(id);
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
            if (!first) grassDirty = true;
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
            int rot = store.PlacementRotation;
            string key = $"{id}|{cell.Value.x},{cell.Value.y}|{valid}|{rot}";
            if (key == ghostKey) return;
            ghostKey = key;
            foreach (var go in ghost) Destroy(go);
            ghost.Clear();

            string mat = valid ? "GhostOk" : "GhostBad";
            var fp = Game.Rotated(def.Footprint, rot);
            for (int dy = 0; dy < fp.Height; dy++)
                for (int dx = 0; dx < fp.Width; dx++)
                    ghost.Add(Marker(cell.Value.x + dx, cell.Value.y + dy, mat));
            var center = Ground.OnGround(Grid.FootprintCenter(cell.Value.x, cell.Value.y, fp));
            var preview = Parts.Group("GhostModel", overlayRoot, center, rot * 90f);
            BuildingModels.Create(def.Sprite, preview.transform, valid ? null : Palette.Get("GhostBad"), Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height));
            if (!valid) preview.transform.localScale = new Vector3(1f, 0.6f, 1f);
            ghost.Add(preview);
        }

        // MARK: - Villagers

        const int MaxVillagers = 30;

        /// <summary>A villager currently playing the given clip (for close-up views), or null.</summary>
        public Villager FindVillager(string clip, string tool = null) =>
            villagers.FirstOrDefault(v => v != null && v.Clip == clip && (tool == null || v.Tool == tool));

        void UpdateVillagers(Game game)
        {
            int wanted = Mathf.Min(game.State.Population, MaxVillagers);
            while (villagers.Count < wanted)
            {
                int index = villagers.Count;
                var person = index < game.State.People.Count ? game.State.People[index] : null;
                var v = Villager.Spawn(peopleRoot, RandomSpot(game), person?.Id ?? index, person?.Female ?? index % 2 == 1);
                v.NextTask = from => TaskFor(index, from);
                v.Ground = Ground;
                villagers.Add(v);
            }
            while (villagers.Count > wanted)
            {
                Destroy(villagers[villagers.Count - 1].gameObject);
                villagers.RemoveAt(villagers.Count - 1);
            }
        }

        // MARK: - Villager jobs (people go where their work is, and do it)

        readonly Dictionary<int, int> trips = new Dictionary<int, int>();

        /// <summary>The job of villager #index (the person with that index), or null if idle.</summary>
        static JobType? JobOf(Game game, int index)
        {
            if (index >= game.State.People.Count) return null;
            var id = game.State.People[index].Job;
            if (id == null) return null;
            foreach (var job in Enums.Jobs)
                if (job.Id() == id) return job;
            return null;
        }

        Vector3 Around(Vector3 center, float radius) =>
            Ground.OnGround(center + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * radius);

        /// <summary>A random cell of a terrain type around a point (so workers spread out), nearest first.</summary>
        Vector3? NearTerrain(Vector3 from, TerrainType type, int range = 6)
        {
            var (cx, cy) = Grid.CellAt(from);
            var found = new List<(float d, Vector3 c)>();
            for (int y = cy - range; y <= cy + range; y++)
                for (int x = cx - range; x <= cx + range; x++)
                {
                    if (Ground.CellType(x, y) != type) continue;
                    var c = Grid.CellCenter(x, y);
                    found.Add(((c - from).sqrMagnitude, c));
                }
            if (found.Count == 0) return null;
            found.Sort((a, b) => a.d.CompareTo(b.d));
            return found[Random.Range(0, Mathf.Min(4, found.Count))].c;
        }

        VillagerTask TaskFor(int index, Vector3 from)
        {
            var task = BuildTask(index);
            if (task.WalkClip == null) task.WalkClip = "Walk";
            task.Route = RouteVia(from, task.Spot);
            return task;
        }

        VillagerTask BuildTask(int index)
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
                else
                {
                    task.Spot = RandomSpot(game);
                    task.Action = "Torch";
                }
                task.Duration = Random.Range(6f, 12f);
                return task;
            }

            var sites = game.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).ToList();
            var job = JobOf(game, index);
            // Builders, and idle people while something is being built (Manor Lords: free families build).
            if (sites.Count > 0 && (job == JobType.Construction || (job == null && index % 3 != 2)))
                return BuildAt(sites[index % sites.Count], task);
            if (job == null)
            {
                task.Spot = Around(hearthPos, Random.Range(1.6f, 2.6f));
                task.Action = Random.value < 0.5f ? "Talk" : "Watch";
                return task;
            }

            PlacedBuilding site = null;
            var places = game.OperationalBuildings().Where(x => x.def.Slots(job.Value) > 0).Select(x => x.building).ToList();
            if (places.Count > 0) site = places[index % places.Count];
            if (site == null)
            {
                task.Spot = Around(hearthPos, 2f);
                task.Action = "Watch";
                return task;
            }
            var fp = game.FootprintOf(site);
            var center = Grid.FootprintCenter(site.X, site.Y, fp);
            float reach = Grid.CellSize * 0.5f * Mathf.Max(fp.Width, fp.Height);
            int trip = trips.TryGetValue(index, out var t) ? t : 0;
            trips[index] = trip + 1;
            // Every third trip a gatherer brings the goods back to the workplace.
            bool haul = trip % 3 == 2;
            task.Duration = Random.Range(6f, 11f);

            switch (job.Value)
            {
                case JobType.Woodcutting:
                case JobType.Stonecutting:
                case JobType.Mining:
                {
                    if (haul)
                    {
                        task.Spot = Around(center, reach + 0.6f);
                        task.WalkClip = "Carry";
                        task.Action = "Idle";
                        task.Duration = 2.5f;
                        return task;
                    }
                    var terrain = job == JobType.Woodcutting ? TerrainType.Forest : job == JobType.Mining ? TerrainType.Ore : TerrainType.Rock;
                    task.Spot = NearTerrain(center, terrain) is Vector3 res ? Around(res, Random.Range(0.6f, 1.2f)) : Around(center, reach + 0.8f);
                    task.Action = "Chop";
                    task.Tool = job == JobType.Woodcutting ? "Prop_Axe" : "Prop_Pickaxe";
                    return task;
                }
                case JobType.Farming:
                {
                    task.Spot = Ground.OnGround(center + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f)));
                    string season = game.Season?.Id ?? "summer";
                    task.Action = season == "spring" ? "Plant" : season == "summer" ? (Random.value < 0.5f ? "Water" : "Plant")
                        : season == "autumn" ? "Harvest" : "Gather";
                    return task;
                }
                case JobType.Foraging:
                {
                    task.Spot = NearTerrain(center, TerrainType.Forest, 4) is Vector3 woods && Random.value < 0.6f
                        ? Around(woods, Random.Range(1.2f, 2f)) : Around(center, reach + Random.Range(0.6f, 1.6f));
                    task.Action = Random.value < 0.5f ? "Harvest" : "Gather";
                    return task;
                }
                case JobType.Fishing:
                {
                    task.Spot = NearTerrain(center, TerrainType.Water, 3) is Vector3 water
                        ? Around(Vector3.Lerp(water, center, 0.55f), Random.Range(0.1f, 0.4f)) : Around(center, reach + 0.6f);
                    task.Action = Random.value < 0.6f ? "Throw" : "Idle";
                    task.Tool = "Prop_Spear";
                    return task;
                }
                case JobType.Hunting:
                {
                    if (haul)
                    {
                        task.Spot = Around(center, reach + 0.6f);
                        task.WalkClip = "Carry";
                        task.Action = "Idle";
                        task.Duration = 2.5f;
                        return task;
                    }
                    task.Spot = NearTerrain(center, TerrainType.Forest, 9) is Vector3 woods ? Around(woods, Random.Range(1f, 3f)) : Around(center, reach + 2f);
                    task.Action = Random.value < 0.5f ? "Throw" : "Watch";
                    task.Tool = "Prop_Spear";
                    task.WalkClip = Random.value < 0.4f ? "Jog" : "Walk";
                    return task;
                }
                case JobType.Crafting:
                    task.Spot = Around(center, reach + Random.Range(0.4f, 0.9f));
                    task.Action = "Work";
                    task.Tool = "Prop_Hammer";
                    return task;
                default:
                    task.Spot = Around(center, reach + Random.Range(0.5f, 1.1f));
                    task.Action = "Talk";
                    return task;
            }
        }

        /// <summary>One or two people stand at each building site and hammer, kneel and fit parts.</summary>
        VillagerTask BuildAt(PlacedBuilding site, VillagerTask task)
        {
            var fp = store.Game.FootprintOf(site);
            var center = Grid.FootprintCenter(site.X, site.Y, fp);
            float reach = Grid.CellSize * 0.5f * Mathf.Max(fp.Width, fp.Height) + Random.Range(0.35f, 0.7f);
            task.Spot = Around(center, reach);
            task.Action = Random.value < 0.55f ? "Chop" : "Work";
            task.Tool = "Prop_Hammer";
            task.Duration = Random.Range(6f, 10f);
            return task;
        }

        // MARK: - Walking on roads

        HashSet<(int, int)> roadCells = new HashSet<(int, int)>();
        string roadKey;

        HashSet<(int, int)> Roads()
        {
            var game = store.Game;
            string key = game.State.NextDecorationId + "|" + game.State.Decorations.Count;
            if (key == roadKey) return roadCells;
            roadKey = key;
            roadCells = new HashSet<(int, int)>(game.State.Decorations
                .Where(d => game.Content.Decoration(d.DefinitionId)?.IsRoad ?? false).Select(d => (d.X, d.Y)));
            return roadCells;
        }

        /// <summary>
        /// Waypoints along the roads from near <paramref name="from"/> to near <paramref name="to"/>,
        /// or null when no road helps (people simply walk straight there).
        /// </summary>
        List<Vector3> RouteVia(Vector3 from, Vector3 to)
        {
            var roads = Roads();
            if (roads.Count < 2) return null;
            (int, int)? Nearest(Vector3 p)
            {
                var (cx, cy) = Grid.CellAt(p);
                (int, int)? best = null;
                int bestD = 5; // only roads within a few cells are worth a detour
                foreach (var c in roads)
                {
                    int d = Mathf.Abs(c.Item1 - cx) + Mathf.Abs(c.Item2 - cy);
                    if (d < bestD) { bestD = d; best = c; }
                }
                return best;
            }
            var a = Nearest(from);
            var b = Nearest(to);
            if (a == null || b == null || a == b) return null;
            // Breadth-first search over connected road cells.
            var prev = new Dictionary<(int, int), (int, int)> { [a.Value] = a.Value };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(a.Value);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == b.Value) break;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (c.Item1 + dx, c.Item2 + dy);
                    if (!roads.Contains(n) || prev.ContainsKey(n)) continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(b.Value)) return null;
            var cells = new List<(int, int)>();
            for (var c = b.Value; c != a.Value; c = prev[c]) cells.Add(c);
            cells.Add(a.Value);
            cells.Reverse();
            // Only worth it when the road is not a long detour.
            float direct = Vector3.Distance(from, to);
            if (cells.Count * Grid.CellSize > direct * 1.8f + 6f) return null;
            return cells.Select(c => Ground.OnGround(Grid.CellCenter(c.Item1, c.Item2) +
                                                     new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f)))).ToList();
        }

        Vector3 RandomSpot(Game game)
        {
            var list = game.State.Buildings;
            if (list.Count == 0) return Vector3.zero;
            var b = list[Random.Range(0, list.Count)];
            var fp = game.FootprintOf(b);
            var center = Grid.FootprintCenter(b.X, b.Y, fp);
            // Stand around the building, never on it (the hearth sits in the middle of its cell).
            float reach = Grid.CellSize * (0.55f + 0.5f * Mathf.Max(fp.Width, fp.Height)) + Random.Range(0f, 0.6f);
            var offset = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * reach;
            return Ground.OnGround(center + offset);
        }
    }
}
