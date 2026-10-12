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
            harvestMarkers.Clear();
            herds.Clear();
            occupancyKey = null;
            selection.Clear();
            ghost.Clear();
            villagers.Clear();
            children.Clear();
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
        const int NatureMargin = 16;

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
                    // Outside the map only some cells get props (far away nobody counts them).
                    if (!inside && (cx + cy) % 2 != 0) continue;
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

            float r01() => (float)rng.NextDouble();
            string Rock() => "sn_rock_" + (1 + rng.Next(3));
            switch (type)
            {
                case TerrainType.Forest:
                {
                    // One tree per cell (two now and then): the forest reads as woods, not a wall.
                    int trees = inside && rng.Next(4) == 0 ? 2 : 1;
                    for (int i = 0; i < trees; i++)
                    {
                        bool pine = rng.Next(3) != 0;
                        string kind = pine ? Pines[rng.Next(Pines.Length)] : "LeafTree";
                        float h = pine ? 7f + r01() * 4f : 5.5f + r01() * 2.5f;
                        Tree(kind, Jitter(c, rng, 1.6f), h);
                    }
                    if (inside && rng.Next(2) == 0) Model("sn_fern", Jitter(c, rng, 1.6f), 0.35f + r01() * 0.15f, false);
                    if (inside && rng.Next(5) == 0) Model("sn_mushroom", Jitter(c, rng, 1.6f), 0.6f, false);
                    if (rng.Next(7) == 0) Model("k_trunk", Jitter(c, rng, 1.4f), 2.6f, false);
                    break;
                }
                case TerrainType.Rock:
                {
                    // Outcrops here and there; the hills show their stony ground between them.
                    int r = rng.Next(10);
                    if (r < 4) Model(Rock(), Jitter(c, rng, 0.8f), 0.32f + r01() * 0.25f);
                    if (rng.Next(3) == 0) Model("sn_pebble", Jitter(c, rng, 1.6f), 1.2f + r01(), false);
                    if (rng.Next(6) == 0) Model("sn_plant", Jitter(c, rng, 1.4f), 0.6f, false);
                    break;
                }
                case TerrainType.Ore:
                {
                    // Veins of rusty iron and green copper stone at the mountain foot.
                    var ore = rng.Next(4) == 0 ? ModelLibrary.CopperOre : ModelLibrary.IronOre;
                    Model(Rock(), Jitter(c, rng, 0.9f), 0.28f + r01() * 0.15f, true, ore);
                    for (int i = 0; i < 2; i++) Model("sn_pebble", Jitter(c, rng, 1.6f), 1.4f + r01(), false, ore);
                    break;
                }
                case TerrainType.Mountain:
                {
                    var p = Jitter(c, rng, 1.6f);
                    // Pines climb the lower slopes; boulders lie in the scree above.
                    if (p.y < 7f && rng.Next(3) == 0) Tree(Pines[rng.Next(Pines.Length)], p, 6f + r01() * 3f);
                    else if (rng.Next(3) == 0) Model(Rock(), p, 0.5f + r01() * 0.6f);
                    break;
                }
                case TerrainType.Sand:
                {
                    if (rng.Next(3) == 0) Model("sn_pebble", Jitter(c, rng, 1.6f), 1.5f, false);
                    if (rng.Next(4) == 0) Model("sn_plant_big", Jitter(c, rng, 1.4f), 0.8f, false);
                    break;
                }
                case TerrainType.Grass:
                {
                    // Berry bushes the foragers pick (the same cells the game counts as food).
                    var berries = store.Game.NodeKinds.FirstOrDefault(k => k.Id == "berries" && k.Capacity > 0);
                    if (inside && berries != null && Game.HasFeature(berries, cx, cy))
                    {
                        var p = Jitter(c, rng, 0.9f);
                        Model("sn_bush", p, 0.6f + r01() * 0.15f);
                        for (int i = 0; i < 9; i++)
                            Model("sn_rock_1", p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, 0.4f + r01() * 0.4f, ((float)rng.NextDouble() - 0.5f) * 0.9f),
                                  0.065f, false, ModelLibrary.Berry);
                        break;
                    }
                    // Open meadow: a few shrubs and flowers, no trees, so the player decides what goes where.
                    int r = rng.Next(14);
                    if (r == 0) Model("sn_bush", Jitter(c, rng, 1.4f), 0.5f + r01() * 0.2f);
                    else if (r == 1) Model("sn_bush_flowers", Jitter(c, rng, 1.4f), 0.5f + r01() * 0.2f);
                    else if (r == 2 && !inside) Tree("LeafTree", Jitter(c, rng, 1.4f), 5f);
                    else if (r == 4) Model("sn_flowers", Jitter(c, rng, 1.4f), 0.28f, false);
                    else if (r == 5) Model("sn_clover", Jitter(c, rng, 1.4f), 0.5f, false);
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

        // MARK: - Heat overlay (Frostpunk's heat map)

        /// <summary>Shows the reach of every fire and how warm each home is (the Weather panel turns it on).</summary>
        public bool ShowHeat { get; set; }
        Transform heatRoot;
        float nextHeatRefresh;
        Material heatMaterial;
        readonly Dictionary<Exposure, Material> homeMaterials = new Dictionary<Exposure, Material>();
        static Texture2D heatTexture;

        static Texture2D HeatTexture()
        {
            if (heatTexture != null) return heatTexture;
            const int n = 128;
            heatTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HeatDisc" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    // Warm in the middle, fading to half at the edge (as the warmth does), with a bright rim.
                    float a = d > 1f ? 0f : Mathf.Lerp(0.42f, 0.2f, d) + (d > 0.94f ? 0.35f : 0f);
                    heatTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            heatTexture.Apply();
            return heatTexture;
        }

        Material OverlayMaterial(string name, Color color, Texture tex)
        {
            var m = new Material(Palette.Get("Mist")) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.renderQueue = 3050;
            return m;
        }

        static Color ExposureColor(Exposure e) =>
            e == Exposure.Comfortable ? new Color(1f, 0.62f, 0.22f) : e == Exposure.Chilly ? new Color(0.95f, 0.9f, 0.55f)
            : e == Exposure.Cold ? new Color(0.45f, 0.75f, 1f) : new Color(0.25f, 0.45f, 1f);

        void UpdateHeatOverlay()
        {
            if (!ShowHeat)
            {
                if (heatRoot != null && heatRoot.gameObject.activeSelf) heatRoot.gameObject.SetActive(false);
                return;
            }
            if (heatRoot == null) heatRoot = Parts.Group("HeatOverlay", transform, Vector3.zero).transform;
            if (!heatRoot.gameObject.activeSelf) { heatRoot.gameObject.SetActive(true); nextHeatRefresh = 0f; }
            if (Time.unscaledTime < nextHeatRefresh || store?.Game == null) return;
            nextHeatRefresh = Time.unscaledTime + 1f;
            var game = store.Game;
            for (int i = heatRoot.childCount - 1; i >= 0; i--) Destroy(heatRoot.GetChild(i).gameObject);
            if (heatMaterial == null) heatMaterial = OverlayMaterial("HeatReach", new Color(1f, 0.55f, 0.15f, 1f), HeatTexture());
            foreach (var spot in game.HeatSpots())
            {
                var at = Grid.CellCenter((float)spot.X - 0.5f, (float)spot.Y - 0.5f);
                float size = (float)spot.Radius * 2f * Grid.CellSize;
                FlatCard(heatRoot, Ground.OnGround(at) + Vector3.up * 0.12f, size, heatMaterial);
            }
            // A coloured mark under every home: orange warm, pale chilly, blue cold or freezing.
            foreach (var h in game.Warmth().Homes)
            {
                if (!homeMaterials.TryGetValue(h.Exposure, out var m) || m == null)
                    homeMaterials[h.Exposure] = m = OverlayMaterial("Home_" + h.Exposure, ExposureColor(h.Exposure) * new Color(1, 1, 1, 2.2f), HeatTexture());
                var fp = game.FootprintOf(h.Building);
                var at = Grid.FootprintCenter(h.Building.X, h.Building.Y, fp);
                FlatCard(heatRoot, Ground.OnGround(at) + Vector3.up * 0.16f, Mathf.Max(fp.Width, fp.Height) * Grid.CellSize * 1.35f, m);
            }
        }

        static void FlatCard(Transform parent, Vector3 at, float size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = "HeatCard";
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // The stoked heart burns visibly higher.
        bool stokedShown;
        int stokedHeartId = -1;

        void UpdateStokedFire()
        {
            var game = store?.Game;
            var heart = game?.Heart;
            bool stoked = heart != null && game.IsStoked;
            if (heart != null && heart.Id != stokedHeartId) { stokedHeartId = heart.Id; stokedShown = false; }
            if (stoked == stokedShown || heart == null || !buildings.TryGetValue(heart.Id, out var view) || view.Root == null) return;
            stokedShown = stoked;
            float k = stoked ? 1.7f : 1f / 1.7f;
            foreach (var ps in view.Root.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.name != "Flames") continue;
                var main = ps.main;
                main.startSizeMultiplier *= k;
                main.startSpeedMultiplier *= k;
                var em = ps.emission;
                em.rateOverTimeMultiplier *= k;
            }
        }

        void Update()
        {
            UpdateTallDecor();
            UpdateHeatOverlay();
            UpdateStokedFire();
            if (grassDirty && grassField != null && Time.unscaledTime >= nextGrassRebuild)
            {
                grassDirty = false;
                nextGrassRebuild = Time.unscaledTime + 0.6f;
                grassField.Rebuild();
            }
            foreach (var view in buildings.Values)
            {
                // Progress bars face the camera whichever way it is turned.
                if (view.Bar != null && view.Bar.activeSelf) view.Bar.transform.rotation = Quaternion.Euler(0, CameraRig.Yaw, 0);
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
                int tier = def.Looks.Count > 0 ? 0 : b.Level / 5;
                string key = $"{b.DefinitionId}|{b.Status}|{b.Rotation}|{game.SpriteOf(b)}|{tier}";
                if (buildings.TryGetValue(b.Id, out var view) && view.Key == key)
                {
                    SetProgress(view, b);
                    continue;
                }
                bool wasThere = view != null;
                if (view != null) Destroy(view.Root);
                view = CreateBuilding(b, def, key);
                buildings[b.Id] = view;
                if (b.Id == stokedHeartId) stokedShown = false; // a new model starts with the normal flames
                if (!initial && (wasThere || b.Status == BuildingStatus.Constructing)) view.PopUntil = Time.time + 0.35f;
            }
            foreach (var id in buildings.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Destroy(buildings[id].Root);
                buildings.Remove(id);
            }

            SyncDecorations(game);
            SyncHerds(game);
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
            float size = Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height);
            var model = BuildingModels.Create(store.Game.SpriteOf(b), root.transform, null, size);
            // Buildings without level models of their own show their level with crates, torches and banners.
            if (def.Looks.Count == 0 && b.Status == BuildingStatus.Complete) BuildingModels.Dress(model.transform, b.Level, size);
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
            var st = game.State;
            string key = $"{st.StageId}|{st.NextBuildingId}|{st.Buildings.Count}|{st.NextDecorationId}|{st.Decorations.Count}";
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
                if (def != null && def.IsRoad && !def.IsBridge) roads[(d.X, d.Y)] = d.DefinitionId != "dirt_path";
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

        // MARK: - Herds

        readonly Dictionary<int, (string key, List<Animal> animals)> herds = new Dictionary<int, (string, List<Animal>)>();

        /// <summary>Animals graze around every finished pen, pasture and barn (one more per level).</summary>
        void SyncHerds(Game game)
        {
            var seen = new HashSet<int>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null || def.Animals.Count == 0 || b.Status == BuildingStatus.Constructing) continue;
                seen.Add(b.Id);
                string key = $"{b.DefinitionId}|{b.Level}|{b.X},{b.Y}|{b.Rotation}";
                if (herds.TryGetValue(b.Id, out var herd) && herd.key == key) continue;
                if (herd.animals != null) foreach (var a in herd.animals) if (a != null) Destroy(a.gameObject);
                var fp = game.FootprintOf(b);
                var center = Grid.FootprintCenter(b.X, b.Y, fp);
                float radius = Grid.CellSize * 0.42f * Mathf.Max(fp.Width, fp.Height);
                var list = new List<Animal>();
                int count = def.Animals.Count + (b.Level - 1);
                for (int i = 0; i < count; i++)
                {
                    var a = Animal.Spawn(def.Animals[i % def.Animals.Count], peopleRoot, center, radius, b.Id * 31 + i, Ground);
                    if (a != null) list.Add(a);
                }
                herds[b.Id] = (key, list);
            }
            foreach (var id in herds.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                foreach (var a in herds[id].animals) if (a != null) Destroy(a.gameObject);
                herds.Remove(id);
            }
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
                if (def == null || (def.IsRoad && !def.IsBridge)) continue;
                seen.Add(d.Id);
                int connect = 0;
                foreach (var (dx, dy, bit) in Neighbours)
                    if (at.TryGetValue((d.X + dx, d.Y + dy), out var other) && other == d.DefinitionId) connect |= bit;
                if (def.IsBridge && connect == 0)
                    foreach (var (dx, dy, bit) in Neighbours)
                        if (game.TerrainNow(d.X + dx, d.Y + dy) is TerrainType t && t != TerrainType.Water) connect |= bit;
                string key = $"{d.DefinitionId}|{d.Rotation}|{connect}";
                if (decorations.TryGetValue(d.Id, out var view) && view.key == key) continue;
                if (view.go != null) Destroy(view.go);
                var go = BuildingModels.Decoration(def.Sprite, objectRoot, null, d.Rotation, connect);
                var pos = Ground.OnGround(Grid.CellCenter(d.X, d.Y));
                if (def.IsBridge) pos.y = Mathf.Max(pos.y, WorldTerrain.WaterLevel);
                go.transform.position = pos;
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

        readonly Dictionary<int, GameObject> harvestMarkers = new Dictionary<int, GameObject>();

        /// <summary>Gold frames on the trees, rocks and veins the player marked to gather first.</summary>
        void UpdateHarvestMarkers(Game game)
        {
            var orders = new HashSet<int>(game.State.HarvestOrders);
            foreach (var key in harvestMarkers.Keys.Where(k => !orders.Contains(k)).ToList())
            {
                Destroy(harvestMarkers[key]);
                harvestMarkers.Remove(key);
            }
            int w = game.Content.Map.Width;
            foreach (int key in orders)
                if (!harvestMarkers.ContainsKey(key)) harvestMarkers[key] = Marker(key % w, key / w, "Select");
        }

        HashSet<(int, int)> usedCells = new HashSet<(int, int)>();
        float nextNodeRefresh;

        float Standing(int x, int y)
        {
            if (usedCells.Contains((x, y))) return 0f;
            var node = store.Game.NodeAt(x, y);
            return node == null || double.IsInfinity(node.Remaining) ? 1f : (float)node.Fraction;
        }

        /// <summary>Trees, rocks and grass disappear from cells the player builds on.</summary>
        void UpdateOccupancy(Game game)
        {
            // Standing nature changes slowly: refresh when something is built, cut down or grows back,
            // and every few seconds while gatherers are at work.
            var s = game.State;
            string key = $"{s.NextBuildingId}|{s.Buildings.Count}|{s.Buildings.Sum(b => b.Rotation)}|{s.NextDecorationId}|{s.Decorations.Count}|{s.NodeCleared.Count}";
            if (key != occupancyKey)
            {
                bool first = occupancyKey == null;
                occupancyKey = key;
                usedCells = OccupiedCells(game);
                nature.SetStanding(Standing);
                UpdateHarvestMarkers(game);
                if (!first) grassDirty = true;
                return;
            }
            // While people gather, only the cells they work on change: refresh just those, now and then.
            if (Time.unscaledTime < nextNodeRefresh) return;
            nextNodeRefresh = Time.unscaledTime + 2f;
            foreach (var job in Enums.Jobs)
            {
                if (game.State.Assignments[job] <= 0 || !GathersFromLand(job)) continue;
                var cells = game.GatherCells(job);
                for (int i = 0; i < cells.Count && i < 12; i++) nature.SetStandingCell(cells[i].x, cells[i].y, Standing(cells[i].x, cells[i].y));
            }
            UpdateHarvestMarkers(game);
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
        Pathing paths;
        /// <summary>Per villager: false = on the way to work, true = carrying the goods home.</summary>
        readonly Dictionary<int, bool> carrying = new Dictionary<int, bool>();

        /// <summary>A villager currently playing the given clip (for close-up views), or null.</summary>
        public Villager FindVillager(string clip, string tool = null) =>
            villagers.FirstOrDefault(v => v != null && v.Clip == clip && (tool == null || v.Tool == tool));

        Pathing Paths
        {
            get
            {
                var map = store.Game.Content.Map;
                if (paths == null) paths = new Pathing(map.Width, map.Height);
                paths.Refresh(store.Game);
                return paths;
            }
        }

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
            // Families: children play around the hearth (one for every six people, up to six).
            int kids = Mathf.Min(6, game.State.Population / 6);
            while (children.Count < kids)
            {
                int n = children.Count;
                var c = Villager.Spawn(peopleRoot, RandomSpot(game), 9000 + n, n % 2 == 0, child: true);
                c.NextTask = from => WithRoute(ChildTask(n), from);
                c.Ground = Ground;
                children.Add(c);
            }
            while (children.Count > kids)
            {
                Destroy(children[children.Count - 1].gameObject);
                children.RemoveAt(children.Count - 1);
            }
            // Survival needs over the heads: hunger when food runs out, cold without firewood.
            string needIcon = game.State.FoodShortage ? "food" : game.Freezing ? "winter" : null;
            for (int i = 0; i < villagers.Count; i++)
            {
                bool sick = i < game.State.People.Count && game.State.People[i].Sick;
                villagers[i].SetNeed(sick ? "sick" : needIcon != null && i % 2 == 0 ? needIcon : null);
            }
        }

        /// <summary>
        /// The player sent someone to a tree, rock, bush or water: the nearest villager of that job
        /// drops what they are doing and walks there now.
        /// </summary>
        public void Dispatch(int x, int y, JobType job)
        {
            var game = store.Game;
            var at = Grid.CellCenter(x, y);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < villagers.Count; i++)
            {
                if (villagers[i] == null || JobOf(game, i) != job) continue;
                float d = (villagers[i].transform.position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            carrying[best] = false;
            villagers[best].Interrupt();
        }

        readonly List<Villager> children = new List<Villager>();

        /// <summary>Children run between the houses, play and sit by the fire at night.</summary>
        VillagerTask ChildTask(int n)
        {
            var game = store.Game;
            var hearth = game.Heart;
            var center = hearth != null ? Grid.CellCenter(hearth.X, hearth.Y) : Vector3.zero;
            var task = new VillagerTask { Duration = Random.Range(3f, 7f), WalkClip = "Jog" };
            if (Atmosphere.Nightness > 0.55f)
            {
                task.Spot = Free(Around(center, Random.Range(1.4f, 2f)));
                task.Face = center;
                task.Action = "Sit";
                task.WalkClip = "Walk";
                task.Duration = Random.Range(8f, 14f);
                return task;
            }
            task.Spot = Free(Random.value < 0.5f ? RandomSpot(game) : Around(center, Random.Range(2.5f, 6f)));
            task.Action = Random.value < 0.4f ? "Talk" : Random.value < 0.5f ? "Gather" : "Idle";
            return task;
        }

        // MARK: - Villager jobs (people go where their work is, and do it)

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

        /// <summary>How many people before this one share their job (spreads a crew over the nodes).</summary>
        static int RankInJob(Game game, int index)
        {
            var id = index < game.State.People.Count ? game.State.People[index].Job : null;
            int rank = 0;
            for (int i = 0; i < index && i < game.State.People.Count; i++)
                if (game.State.People[i].Job == id) rank++;
            return rank;
        }

        Vector3 Around(Vector3 center, float radius) =>
            Ground.OnGround(center + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * radius);

        /// <summary>The point itself if people can stand there, otherwise a spot in the nearest open cell.</summary>
        Vector3 Free(Vector3 p)
        {
            var (x, y) = Grid.CellAt(p);
            if (Paths.Walkable(x, y)) return p;
            var c = Paths.NearestWalkable(x, y);
            if (c == null) return p;
            return Ground.OnGround(Grid.CellCenter(c.Value.x, c.Value.y) + new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f)));
        }

        /// <summary>
        /// Where to stand to work at a blocked cell (a tree, a rock, the water's edge): in the open
        /// cell beside it nearest to <paramref name="from"/>, close to the shared edge.
        /// </summary>
        Vector3 BesideCell(int x, int y, Vector3 from)
        {
            var target = Grid.CellCenter(x, y);
            (int x, int y)? best = null;
            float bestD = float.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || !Paths.Walkable(x + dx, y + dy)) continue;
                    float d = (Grid.CellCenter(x + dx, y + dy) - from).sqrMagnitude * (dx != 0 && dy != 0 ? 1.4f : 1f) + Random.Range(0f, 6f);
                    if (d < bestD) { bestD = d; best = (x + dx, y + dy); }
                }
            if (best == null) return Free(target);
            var stand = Grid.CellCenter(best.Value.x, best.Value.y);
            var side = new Vector3(Random.Range(-0.35f, 0.35f), 0, Random.Range(-0.35f, 0.35f));
            return Ground.OnGround(Vector3.Lerp(stand, target, 0.38f) + side);
        }

        /// <summary>A spot just outside a building, on the side nearest to <paramref name="from"/>.</summary>
        Vector3 BesideBuilding(PlacedBuilding b, Vector3 from)
        {
            var fp = store.Game.FootprintOf(b);
            var options = new List<(float d, int x, int y, int ix, int iy)>();
            for (int y = b.Y - 1; y <= b.Y + fp.Height; y++)
                for (int x = b.X - 1; x <= b.X + fp.Width; x++)
                {
                    bool inside = x >= b.X && x < b.X + fp.Width && y >= b.Y && y < b.Y + fp.Height;
                    bool corner = (x == b.X - 1 || x == b.X + fp.Width) && (y == b.Y - 1 || y == b.Y + fp.Height);
                    if (inside || corner || !Paths.Walkable(x, y)) continue;
                    int ix = Mathf.Clamp(x, b.X, b.X + fp.Width - 1), iy = Mathf.Clamp(y, b.Y, b.Y + fp.Height - 1);
                    options.Add(((Grid.CellCenter(x, y) - from).sqrMagnitude, x, y, ix, iy));
                }
            if (options.Count == 0) return Free(Grid.FootprintCenter(b.X, b.Y, fp));
            options.Sort((a, c) => a.d.CompareTo(c.d));
            var o = options[Random.Range(0, Mathf.Min(3, options.Count))];
            var stand = Grid.CellCenter(o.x, o.y);
            var edge = Grid.CellCenter(o.ix, o.iy);
            var side = new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
            return Ground.OnGround(Vector3.Lerp(stand, edge, 0.3f) + side);
        }

        VillagerTask TaskFor(int index, Vector3 from) => WithRoute(BuildTask(index, from), from);

        VillagerTask WithRoute(VillagerTask task, Vector3 from)
        {
            if (task.WalkClip == null) task.WalkClip = "Walk";
            task.Route = Route(from, task.Spot);
            return task;
        }

        /// <summary>Where finished goods go: the nearest store (pit, granary, barn, market, the hearth) or the workplace.</summary>
        PlacedBuilding DropPoint(Game game, Vector3 from, PlacedBuilding workplace)
        {
            PlacedBuilding best = workplace;
            float bestD = workplace != null ? (Grid.CellCenter(workplace.X, workplace.Y) - from).sqrMagnitude : float.MaxValue;
            foreach (var (b, def) in game.OperationalBuildings())
            {
                if (def.StorageValue <= 0 && !def.Heart) continue;
                float d = (Grid.CellCenter(b.X, b.Y) - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        static bool GathersFromLand(JobType job) =>
            job == JobType.Woodcutting || job == JobType.Stonecutting || job == JobType.Mining || job == JobType.Foraging
            || job == JobType.Hunting || job == JobType.Fishing;

        VillagerTask BuildTask(int index, Vector3 from)
        {
            var game = store.Game;
            var task = new VillagerTask { Action = "Idle", Duration = Random.Range(3f, 7f) };
            var hearth = game.Heart;
            Vector3 hearthPos = hearth != null ? Grid.CellCenter(hearth.X, hearth.Y) : Vector3.zero;

            // The sick rest: under a healer's care beside the hut, otherwise by a home.
            var person = index < game.State.People.Count ? game.State.People[index] : null;
            if (person != null && person.Sick)
            {
                PlacedBuilding bed = null;
                if (game.TreatedPatients().Contains(person.Id))
                    bed = game.OperationalBuildings().Where(x => x.def.Slots(JobType.Healing) > 0).Select(x => x.building).FirstOrDefault();
                if (bed == null)
                {
                    var homes = game.OperationalBuildings().Where(x => game.HousingOf(x.building, x.def) > 0).Select(x => x.building).ToList();
                    if (homes.Count > 0) bed = homes[person.Id % homes.Count];
                }
                if (bed != null)
                {
                    task.Spot = BesideBuilding(bed, from);
                    task.Face = Grid.FootprintCenter(bed.X, bed.Y, game.FootprintOf(bed));
                }
                else task.Spot = Free(Around(hearthPos, 2f));
                task.Action = "Sit";
                task.Duration = Random.Range(12f, 20f);
                return task;
            }

            // Night: everyone gathers around the hearth or rests by the houses.
            if (Atmosphere.Nightness > 0.55f)
            {
                if (index % 3 != 0 || hearth == null)
                {
                    task.Spot = Free(Around(hearthPos, Random.Range(1.6f, 2.4f)));
                    task.Face = hearthPos;
                    task.Action = Random.value < 0.6f ? "Sit" : "Talk";
                }
                else
                {
                    task.Spot = Free(RandomSpot(game));
                    task.Action = "Torch";
                }
                task.Duration = Random.Range(6f, 12f);
                return task;
            }

            var sites = game.State.Buildings.Where(b => b.Status != BuildingStatus.Complete).ToList();
            var job = JobOf(game, index);
            // Builders, and idle people while something is being built (Manor Lords: free families build).
            if (sites.Count > 0 && (job == JobType.Construction || (job == null && index % 3 != 2)))
                return BuildAt(sites[index % sites.Count], task, from);
            if (job == null)
            {
                task.Spot = Free(Around(hearthPos, Random.Range(1.6f, 2.6f)));
                task.Face = hearthPos;
                task.Action = Random.value < 0.5f ? "Talk" : "Watch";
                return task;
            }

            PlacedBuilding site = null;
            var places = game.OperationalBuildings().Where(x => game.SlotsOf(x.building, x.def, job.Value) > 0).Select(x => x.building).ToList();
            if (places.Count > 0) site = places[index % places.Count];
            if (site == null)
            {
                task.Spot = Free(Around(hearthPos, 2f));
                task.Action = "Watch";
                return task;
            }
            var fp = game.FootprintOf(site);
            var center = Grid.FootprintCenter(site.X, site.Y, fp);
            task.Duration = Random.Range(7f, 11f);

            if (GathersFromLand(job.Value))
            {
                // Gatherers walk to the land they take from, work there, then carry the goods home.
                carrying.TryGetValue(index, out bool home);
                if (home)
                {
                    carrying[index] = false;
                    var drop = DropPoint(game, from, site);
                    task.Spot = BesideBuilding(drop, from);
                    task.Face = Grid.CellCenter(drop.X, drop.Y);
                    task.WalkClip = job == JobType.Hunting || job == JobType.Fishing || job == JobType.Foraging ? "Walk" : "Carry";
                    task.Carry = job == JobType.Woodcutting ? "k_wood" : job == JobType.Stonecutting || job == JobType.Mining ? "k_stone"
                        : job == JobType.Foraging ? "wicker_basket_01" : null;
                    task.Action = "Gather";
                    task.Duration = 1.6f;
                    return task;
                }
                var cells = game.GatherCells(job.Value);
                if (cells.Count == 0)
                {
                    task.Spot = BesideBuilding(site, from);
                    task.Face = center;
                    task.Action = "Watch";
                    return task;
                }
                int rank = RankInJob(game, index);
                var pick = cells[rank % Mathf.Min(cells.Count, Mathf.Max(2, game.State.Assignments[job.Value]))];
                var cellCenter = Grid.CellCenter(pick.x, pick.y);
                task.Spot = BesideCell(pick.x, pick.y, from);
                task.Face = cellCenter;
                carrying[index] = true;
                switch (job.Value)
                {
                    case JobType.Woodcutting:
                        task.Action = "Chop";
                        task.Tool = "Prop_Axe";
                        break;
                    case JobType.Stonecutting:
                    case JobType.Mining:
                        task.Action = "Chop";
                        task.Tool = "Prop_Pickaxe";
                        break;
                    case JobType.Foraging:
                        task.Action = Random.value < 0.5f ? "Harvest" : "Gather";
                        break;
                    case JobType.Hunting:
                        task.Action = Random.value < 0.6f ? "Throw" : "Watch";
                        task.Tool = "Prop_Spear";
                        task.WalkClip = Random.value < 0.4f ? "Jog" : "Walk";
                        break;
                    default: // fishing at the water's edge
                        task.Action = Random.value < 0.7f ? "Throw" : "Idle";
                        task.Tool = "Prop_Spear";
                        break;
                }
                return task;
            }

            switch (job.Value)
            {
                case JobType.Farming:
                {
                    task.Spot = Ground.OnGround(center + new Vector3(Random.Range(-0.9f, 0.9f) * fp.Width, 0, Random.Range(-0.9f, 0.9f) * fp.Height));
                    string season = game.Season?.Id ?? "summer";
                    task.Action = season == "spring" ? "Plant" : season == "summer" ? (Random.value < 0.5f ? "Water" : "Plant")
                        : season == "autumn" ? "Harvest" : "Gather";
                    return task;
                }
                case JobType.Herding:
                    // Herders walk among the animals with their staff, watch them and lend a hand.
                    task.Spot = Ground.OnGround(center + new Vector3(Random.Range(-0.8f, 0.8f) * fp.Width, 0, Random.Range(-0.8f, 0.8f) * fp.Height));
                    task.Action = Random.value < 0.5f ? "Watch" : Random.value < 0.5f ? "Gather" : "Idle";
                    task.Tool = "Prop_Spear";
                    return task;
                case JobType.Healing:
                {
                    // Healers tend the patients beside the hut, or brew at the door.
                    task.Spot = BesideBuilding(site, from);
                    task.Face = center;
                    task.Action = game.SickCount > 0 ? (Random.value < 0.6f ? "Gather" : "Talk") : (Random.value < 0.5f ? "Work" : "Idle");
                    return task;
                }
                case JobType.Crafting:
                    task.Spot = BesideBuilding(site, from);
                    task.Face = center;
                    task.Action = "Work";
                    task.Tool = "Prop_Hammer";
                    return task;
                default:
                    task.Spot = BesideBuilding(site, from);
                    task.Face = center;
                    task.Action = "Talk";
                    return task;
            }
        }

        /// <summary>One or two people stand at each building site and hammer, kneel and fit parts.</summary>
        VillagerTask BuildAt(PlacedBuilding site, VillagerTask task, Vector3 from)
        {
            var fp = store.Game.FootprintOf(site);
            task.Spot = BesideBuilding(site, from);
            task.Face = Grid.FootprintCenter(site.X, site.Y, fp);
            task.Action = Random.value < 0.55f ? "Chop" : "Work";
            task.Tool = "Prop_Hammer";
            task.Duration = Random.Range(6f, 10f);
            return task;
        }

        // MARK: - Walking around things

        /// <summary>
        /// Waypoints from <paramref name="from"/> to <paramref name="to"/> around buildings, trees,
        /// rocks and water (over bridges), preferring roads; null when the way is straight and clear.
        /// </summary>
        List<Vector3> Route(Vector3 from, Vector3 to)
        {
            var p = Paths;
            var a = Grid.CellAt(from);
            var b = Grid.CellAt(to);
            if (a == b) return null;
            var cells = p.FindPath(a, b);
            if (cells == null) return null;
            cells = p.Straighten(cells);
            if (cells.Count <= 2 && p.ClearLine(cells[0], cells[cells.Count - 1])) return null;
            var points = new List<Vector3>(cells.Count);
            for (int i = 1; i < cells.Count - 1; i++)
            {
                var (x, y) = cells[i];
                float j = p.IsRoad(x, y) ? 0.15f : 0.35f;
                points.Add(Ground.OnGround(Grid.CellCenter(x, y) + new Vector3(Random.Range(-j, j), 0, Random.Range(-j, j))));
            }
            // The last open cell before the spot (the spot itself may stand at a tree or inside a field).
            var last = cells[cells.Count - 1];
            if (last != b) points.Add(Ground.OnGround(Grid.CellCenter(last.x, last.y)));
            return points.Count > 0 ? points : null;
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
            return Free(Ground.OnGround(center + offset));
        }
    }
}
