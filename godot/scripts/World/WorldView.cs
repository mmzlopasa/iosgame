using System.Collections.Generic;
using System.Linq;
using Founders.Art;
using Founders.Core;
using Founders.People;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Draws the settlement from the game state: the painted terrain with the river and water, the valley's
    /// nature, buildings with construction progress, the placement preview, selection, trodden paths and
    /// roads, animals, caravans and villagers going about their work. It never changes game state itself.
    /// </summary>
    public sealed partial class WorldView : Node3D
    {
        Session store;
        public Terrain Ground { get; private set; }
        public Nature Plants => nature;
        public ShaderMaterial WaterMaterial { get; private set; }
        /// <summary>Called after the world was rebuilt (a new settlement, a cloud restore): the sky re-binds the ground.</summary>
        public System.Action Rebuilt;
        Node3D objectRoot, overlayRoot, peopleRoot;

        Nature nature;
        readonly Dictionary<int, BuildingView> buildings = new Dictionary<int, BuildingView>();
        readonly Dictionary<int, (string key, Node3D node)> decorations = new Dictionary<int, (string, Node3D)>();
        /// <summary>Tall decorations (trees, totems, banners) that step aside when they block the view.</summary>
        readonly Dictionary<int, (Node3D node, float height, bool hidden)> tallDecor = new Dictionary<int, (Node3D, float, bool)>();
        double nextDecorCheck;
        static readonly Dictionary<string, float> TallDecorations = new Dictionary<string, float>
        {
            ["young_tree"] = 4.2f, ["totem"] = 3.2f, ["banner"] = 3.4f, ["oil_lantern"] = 2.1f, ["torch_post"] = 1.8f,
        };
        string occupancyKey;
        double nextGroundPaint;
        readonly List<Node3D> selection = new List<Node3D>();
        readonly List<Node3D> ghost = new List<Node3D>();
        readonly List<Villager> villagers = new List<Villager>();
        string ghostKey = "";
        int selectionKey = -1;
        string splatKey;
        string lastStage;
        double now;

        sealed class BuildingView
        {
            public string Key;
            public Node3D Root;
            public Node3D Model;
            public Node3D Bar;
            public Node3D BarFill;
            public double PopUntil;
        }

        static float R(float a, float b) => (float)GD.RandRange(a, b);
        static float Rv() => GD.Randf();

        /// <summary>A group at a world position, turned by a Unity-style yaw (its parts are built in the Unity version's local space).</summary>
        static Node3D Group(string name, Node3D parent, Vector3 world, float yaw = 0f)
        {
            var g = new Node3D { Name = name };
            parent.AddChild(g);
            g.Transform = new Transform3D(U.Yaw(yaw), world);
            return g;
        }

        public void Init(Session owner)
        {
            store = owner;
            RebuildAll();
        }

        public void RebuildAll()
        {
            foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
            buildings.Clear();
            decorations.Clear();
            tallDecor.Clear();
            harvestMarkers.Clear();
            herds.Clear();
            caravans.Clear();
            occupancyKey = null;
            selection.Clear();
            ghost.Clear();
            villagers.Clear();
            children.Clear();
            spots.Clear();
            workTargets.Clear();
            dispatched.Clear();
            carrying.Clear();
            haulLeg.Clear();
            lying.Clear();
            ghostKey = "";
            foundingGhost.Clear();
            foundingKey = "";
            selectionKey = -1;
            splatKey = null;
            zoneOverlays.Clear();
            zoneOverlayKey = null;
            heatRoot = null;
            paths = null;

            Ground = new Terrain(store.Game.Content.Map, this);
            objectRoot = Group("Buildings", this, Vector3.Zero);
            overlayRoot = Group("Overlay", this, Vector3.Zero);
            peopleRoot = Group("People", this, Vector3.Zero);
            BuildWater();
            BuildNature();
            Sync(true);
            Rebuilt?.Invoke();
        }

        // ------------------------------------------------------------------ water

        void BuildWater()
        {
            var map = store.Game.Content.Map;
            var center = Grid.CellCenter((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f);
            WaterMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
            var water = new MeshInstance3D
            {
                Name = "Water",
                Mesh = new PlaneMesh { Size = new Vector2(Ground.Size, Ground.Size), SubdivideWidth = 0, SubdivideDepth = 0 },
                MaterialOverride = WaterMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(center.X, Terrain.WaterLevel, center.Z),
            };
            AddChild(water);
        }

        // ------------------------------------------------------------------ nature

        void BuildNature()
        {
            var berries = store.Game.NodeKinds.FirstOrDefault(k => k.Id == "berries" && k.Capacity > 0);
            nature = new Nature();
            AddChild(nature);
            nature.Build(Ground, (x, y) => berries != null && Game.HasFeature(berries, x, y));
        }

        // ------------------------------------------------------------------ tall decorations step aside

        /// <summary>True when a thing of <paramref name="height"/> at <paramref name="p"/> stands between the camera and what it looks at.</summary>
        static bool Occludes(Vector3 cam, Vector3 forward, Vector3 p, float height)
        {
            var focusDist = cam.Y / Mathf.Max(0.2f, -forward.Y);
            var focus = cam + forward * focusDist;
            var toThing = p - cam;
            float along = toThing.Dot(forward);
            if (along <= 0f || along >= focusDist) return false;
            var closest = cam + forward * along;
            var off = new Vector2(p.X - closest.X, p.Z - closest.Z).Length();
            bool tallEnough = p.Y + height > closest.Y - 0.5f;
            return off < 2.2f && tallEnough && (focus - p).Length() > 2.5f;
        }

        void UpdateTallDecor()
        {
            if (now < nextDecorCheck || tallDecor.Count == 0) return;
            nextDecorCheck = now + 0.15;
            var cam = GetViewport()?.GetCamera3D();
            if (cam == null) return;
            foreach (var id in tallDecor.Keys.ToList())
            {
                var v = tallDecor[id];
                if (!IsInstanceValid(v.node)) continue;
                bool hide = Occludes(cam.GlobalPosition, -cam.GlobalBasis.Z, v.node.GlobalPosition, v.height);
                if (hide == v.hidden) continue;
                v.node.Visible = !hide;
                tallDecor[id] = (v.node, v.height, hide);
            }
        }

        // ------------------------------------------------------------------ heat overlay (Frostpunk's heat map)

        /// <summary>Shows the reach of every fire and how warm each home is (the Weather panel turns it on).</summary>
        public bool ShowHeat { get; set; }
        Node3D heatRoot;
        double nextHeatRefresh;
        Material heatMaterial;
        readonly Dictionary<Exposure, Material> homeMaterials = new Dictionary<Exposure, Material>();
        static Texture2D heatTexture;

        static Texture2D HeatTexture()
        {
            if (heatTexture != null) return heatTexture;
            const int n = 128;
            var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    // Warm in the middle, fading to half at the edge (as the warmth does), with a bright rim.
                    float a = d > 1f ? 0f : Mathf.Lerp(0.16f, 0.05f, d) + (d > 0.95f ? 0.3f : 0f);
                    img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            return heatTexture = ImageTexture.CreateFromImage(img);
        }

        static Material OverlayMaterial(Color color, Texture2D tex) => new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = tex,
            AlbedoColor = color,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 2,
        };

        static Color ExposureColor(Exposure e) =>
            e == Exposure.Comfortable ? new Color(1f, 0.62f, 0.22f) : e == Exposure.Chilly ? new Color(0.95f, 0.9f, 0.55f)
            : e == Exposure.Cold ? new Color(0.45f, 0.75f, 1f) : new Color(0.25f, 0.45f, 1f);

        void UpdateHeatOverlay()
        {
            if (!ShowHeat)
            {
                if (heatRoot != null && heatRoot.Visible) heatRoot.Visible = false;
                return;
            }
            if (heatRoot == null) heatRoot = Group("HeatOverlay", this, Vector3.Zero);
            if (!heatRoot.Visible) { heatRoot.Visible = true; nextHeatRefresh = 0; }
            if (now < nextHeatRefresh || store?.Game == null) return;
            nextHeatRefresh = now + 1;
            var game = store.Game;
            foreach (var c in heatRoot.GetChildren()) c.QueueFree();
            heatMaterial ??= OverlayMaterial(new Color(1f, 0.55f, 0.15f, 1f), HeatTexture());
            foreach (var spot in game.HeatSpots())
            {
                var at = Grid.CellCenter((float)spot.X - 0.5f, (float)spot.Y - 0.5f);
                float size = (float)spot.Radius * 2f * Grid.CellSize;
                FlatCard(heatRoot, Ground.OnGround(at) + Vector3.Up * 0.12f, size, heatMaterial);
            }
            // A coloured mark under every home: orange warm, pale chilly, blue cold or freezing.
            foreach (var h in game.Warmth().Homes)
            {
                if (!homeMaterials.TryGetValue(h.Exposure, out var m))
                {
                    var c = ExposureColor(h.Exposure);
                    homeMaterials[h.Exposure] = m = OverlayMaterial(new Color(c.R, c.G, c.B, 1f), HeatTexture());
                    ((StandardMaterial3D)m).AlbedoColor = new Color(c.R, c.G, c.B, 3.5f);
                }
                var fp = game.FootprintOf(h.Building);
                var at = Grid.FootprintCenter(h.Building.X, h.Building.Y, fp);
                FlatCard(heatRoot, Ground.OnGround(at) + Vector3.Up * 0.16f, Mathf.Max(fp.Width, fp.Height) * Grid.CellSize * 1.35f, m);
            }
        }

        static void FlatCard(Node3D parent, Vector3 at, float size, Material m)
        {
            var mi = new MeshInstance3D
            {
                Name = "HeatCard",
                Mesh = new PlaneMesh { Size = Vector2.One },
                MaterialOverride = m,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = at,
                Scale = new Vector3(size, 1f, size),
            };
            parent.AddChild(mi);
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
            if (stoked == stokedShown || heart == null || !buildings.TryGetValue(heart.Id, out var view) || !IsInstanceValid(view.Root)) return;
            stokedShown = stoked;
            float k = stoked ? 1.7f : 1f / 1.7f;
            foreach (var p in FindAll<GpuParticles3D>(view.Root))
            {
                if (p.Name != "Flames") continue;
                if (p.DrawPass1 is QuadMesh q) q.Size *= k;
                if (p.ProcessMaterial is ParticleProcessMaterial pm)
                {
                    pm.InitialVelocityMin *= k;
                    pm.InitialVelocityMax *= k;
                }
                p.AmountRatio = stoked ? 1f : 0.6f;
            }
        }

        static IEnumerable<T> FindAll<T>(Node n) where T : Node
        {
            if (n is T t) yield return t;
            foreach (var c in n.GetChildren())
                foreach (var x in FindAll<T>(c)) yield return x;
        }

        public override void _Process(double delta)
        {
            now += delta;
            if (store == null || Ground == null) return;
            UpdateCaravans();
            UpdateTallDecor();
            UpdateHeatOverlay();
            UpdateStokedFire();
            var face = new Basis(Vector3.Up, Mathf.DegToRad(CameraRig.Yaw));
            foreach (var view in buildings.Values)
            {
                // Progress bars face the camera whichever way it is turned.
                if (view.Bar != null && view.Bar.Visible) view.Bar.GlobalBasis = face;
                if (view.PopUntil <= 0) continue;
                float k = 1f - Mathf.Clamp((float)(view.PopUntil - now) / 0.35f, 0f, 1f);
                view.Root.Scale = Vector3.One * Mathf.Lerp(0.7f, 1f, U.SmoothStep(0, 1, k));
                if (k >= 1f) view.PopUntil = 0;
            }
        }

        // ------------------------------------------------------------------ sync

        /// <summary>Updates objects to match the game state. Cheap enough to call several times per second.</summary>
        public void Sync(bool initial = false)
        {
            if (store == null || Ground == null) return;
            var game = store.Game;

            var seen = new HashSet<int>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                seen.Add(b.Id);
                int tier = def.Looks.Count > 0 ? 0 : b.Level / 5;
                string key = $"{b.DefinitionId}|{b.Status}|{b.Rotation}|{game.SpriteOf(b)}|{tier}|{b.X},{b.Y}";
                if (buildings.TryGetValue(b.Id, out var view) && view.Key == key)
                {
                    SetProgress(view, b);
                    continue;
                }
                bool wasThere = view != null;
                if (view != null) view.Root.QueueFree();
                view = CreateBuilding(b, def, key);
                buildings[b.Id] = view;
                if (b.Id == stokedHeartId) stokedShown = false; // a new model starts with the normal flames
                if (!initial && (wasThere || b.Status == BuildingStatus.Constructing)) view.PopUntil = now + 0.35;
            }
            foreach (var id in buildings.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                buildings[id].Root.QueueFree();
                buildings.Remove(id);
            }

            SyncDecorations(game);
            SyncHerds(game);
            UpdateOccupancy(game);
            RepaintGround(game, initial);
            UpdateSelection();
            UpdateGhost();
            UpdateZoneOverlay(game);
            UpdateFoundingGhost();
            UpdateVillagers(game);
            SyncCaravans(game);
        }

        BuildingView CreateBuilding(PlacedBuilding b, BuildingDefinition def, string key)
        {
            var fpWorld = store.Game.FootprintOf(b);
            var center = Ground.OnGround(Grid.FootprintCenter(b.X, b.Y, fpWorld));
            float low = Ground.LowestGround(center, fpWorld);
            // Never below the water line (beach plots by the river).
            center.Y = Mathf.Max(center.Y, Terrain.WaterLevel + 0.12f);
            var root = Group($"Building_{b.Id}", objectRoot, center, b.Rotation * 90f);
            float size = Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height);
            // Where the ground falls away under the plot (a sandy bank, a slope) a packed-earth
            // footing carries the building, so nothing floats over the edge.
            if (center.Y - low > 0.12f)
            {
                float depth = center.Y - low + 0.15f;
                Parts.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.02f - depth * 0.5f, 0),
                           new Vector3(def.Footprint.Width * Grid.CellSize * 0.9f, depth, def.Footprint.Height * Grid.CellSize * 0.9f), "Earth");
            }
            var model = BuildingModels.Create(store.Game.SpriteOf(b), root, null, size, b.Level,
                                              new Vector2(def.Footprint.Width, def.Footprint.Height) * Grid.CellSize);
            // Buildings without level models of their own show their level with crates, torches and banners.
            if (def.Looks.Count == 0 && b.Status == BuildingStatus.Complete) BuildingModels.Dress(model, b.Level, size);
            var bar = Group("Progress", root, new Vector3(0, 2.6f, 0));
            Parts.Prim(PrimitiveType.Cube, bar, Vector3.Zero, new Vector3(1.4f, 0.16f, 0.06f), "BarBack");
            var fill = Parts.Prim(PrimitiveType.Cube, bar, new Vector3(0, 0, -0.04f), new Vector3(1.3f, 0.1f, 0.06f), "BarFill");
            var view = new BuildingView { Key = key, Root = root, Model = model, Bar = bar, BarFill = fill };
            SetProgress(view, b);
            return view;
        }

        static void SetProgress(BuildingView view, PlacedBuilding b)
        {
            bool active = b.Status != BuildingStatus.Complete;
            view.Bar.Visible = active;
            float p = Mathf.Clamp((float)b.Progress, 0f, 1f);
            float h = b.Status == BuildingStatus.Constructing ? Mathf.Lerp(0.2f, 1f, p) : 1f;
            view.Model.Scale = new Vector3(1f, h, 1f);
            if (active)
            {
                float w = Mathf.Max(0.02f, p) * 1.3f;
                view.BarFill.Transform = new Transform3D(Basis.FromScale(new Vector3(w, 0.1f, 0.06f)), U.ToGodot(new Vector3(-0.65f + w * 0.5f, 0, -0.04f)));
            }
        }

        /// <summary>Trodden ground around buildings and, in the village stage, paths to the hearth.</summary>
        void RepaintGround(Game game, bool initial)
        {
            var st = game.State;
            string key = $"{st.StageId}|{st.NextBuildingId}|{st.Buildings.Count}|{st.NextDecorationId}|{st.Decorations.Count}|{game.PathsVersion}|{st.Wear.Count > 0}";
            if (key == splatKey) return;
            // Repainting the ground is heavy: while a road is being drawn, do it a few times a second at most.
            if (!initial && now < nextGroundPaint) return;
            nextGroundPaint = now + 0.35;
            bool firstVillage = !initial && lastStage == game.Content.StartStage && game.State.StageId != game.Content.StartStage;
            lastStage = game.State.StageId;
            splatKey = key;
            var around = new List<(Vector3, float)>();
            var trails = new List<(Vector3, Vector3)>();
            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                around.Add((Grid.FootprintCenter(b.X, b.Y, def.Footprint), 0.9f * Mathf.Max(def.Footprint.Width, def.Footprint.Height)));
            }
            if (game.HasPaths)
            {
                // Paths worn by feet: each trodden cell joined to its trodden neighbours, so the lanes wind as people walk.
                var trodden = new HashSet<(int, int)>(game.TroddenCells());
                foreach (var (x, y) in trodden)
                {
                    var c = Grid.CellCenter(x, y);
                    bool any = false;
                    foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                        if (trodden.Contains((x + dx, y + dy))) { trails.Add((c, Grid.CellCenter(x + dx, y + dy))); any = true; }
                    if (!any && !trodden.Contains((x - 1, y)) && !trodden.Contains((x, y - 1)) && !trodden.Contains((x - 1, y - 1)) && !trodden.Contains((x - 1, y + 1)))
                        trails.Add((c + new Vector3(-0.3f, 0, 0), c + new Vector3(0.3f, 0, 0)));
                }
            }
            else if (game.State.StageId != game.Content.StartStage)
            {
                var hearth = game.State.Buildings.FirstOrDefault(b => b.DefinitionId == "village_hearth");
                if (hearth != null)
                {
                    var start = Grid.CellCenter(hearth.X, hearth.Y);
                    foreach (var b in game.State.Buildings)
                    {
                        if (b.Id == hearth.Id) continue;
                        var def = game.Content.Building(b.DefinitionId);
                        if (def != null) trails.Add((start, Grid.FootprintCenter(b.X, b.Y, def.Footprint)));
                    }
                    if (firstVillage && store.Rig != null) store.Rig.SetFocus(start, zoom: 22f);
                }
            }
            Ground.Paint(around, trails, RoadSegments(game));
        }

        /// <summary>Road cells joined to their road neighbours (a lone cell becomes a short stub).</summary>
        static List<Terrain.RoadSegment> RoadSegments(Game game)
        {
            var roads = new Dictionary<(int, int), bool>();
            foreach (var d in game.State.Decorations)
            {
                var def = game.Content.Decoration(d.DefinitionId);
                if (def != null && def.IsRoad && !def.IsBridge) roads[(d.X, d.Y)] = d.DefinitionId != "dirt_path";
            }
            var list = new List<Terrain.RoadSegment>();
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
                    list.Add(new Terrain.RoadSegment(c, Grid.CellCenter(x + dx, y + dy), kv.Value && stone));
                }
                if (!any) list.Add(new Terrain.RoadSegment(c + new Vector3(-0.5f, 0, 0), c + new Vector3(0.5f, 0, 0), kv.Value));
                else list.Add(new Terrain.RoadSegment(c, c, kv.Value));
            }
            return list;
        }

        // ------------------------------------------------------------------ herds

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
                if (herd.animals != null) foreach (var a in herd.animals) if (IsInstanceValid(a)) a.QueueFree();
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
                foreach (var a in herds[id].animals) if (IsInstanceValid(a)) a.QueueFree();
                herds.Remove(id);
            }
        }

        // ------------------------------------------------------------------ decorations

        static readonly (int dx, int dy, int bit)[] Neighbours =
        {
            (0, -1, BuildingModels.North), (1, 0, BuildingModels.East), (0, 1, BuildingModels.South), (-1, 0, BuildingModels.West),
        };

        /// <summary>The deck height of the crossing a bridge piece belongs to: the highest ground (or the
        /// water) under any piece joined to it, so a crossing is level from bank to bank.</summary>
        float CrossingHeight(Dictionary<(int, int), string> at, int x, int y, string id)
        {
            float top = Terrain.WaterLevel;
            var seen = new HashSet<(int, int)> { (x, y) };
            var todo = new Stack<(int, int)>();
            todo.Push((x, y));
            while (todo.Count > 0 && seen.Count < 64)
            {
                var (cx, cy) = todo.Pop();
                top = Mathf.Max(top, Ground.HeightAt(Grid.CellCenter(cx, cy)));
                foreach (var (dx, dy, _) in Neighbours)
                {
                    var n = (cx + dx, cy + dy);
                    if (!seen.Contains(n) && at.TryGetValue(n, out var other) && other == id) { seen.Add(n); todo.Push(n); }
                }
            }
            return top;
        }

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
                {
                    // Over the water it runs from bank to bank; on the sandy bank it runs out over the water.
                    bool onBank = game.TerrainNow(d.X, d.Y) != TerrainType.Water;
                    foreach (var (dx, dy, bit) in Neighbours)
                        if (game.TerrainNow(d.X + dx, d.Y + dy) is TerrainType t && (t == TerrainType.Water) == onBank) connect |= bit;
                }
                var pos = Ground.OnGround(Grid.CellCenter(d.X, d.Y));
                float drop = 0f;
                if (def.IsBridge)
                {
                    // One deck height for the whole crossing (the highest bank or the water).
                    float ground = Mathf.Max(pos.Y, Terrain.WaterLevel);
                    pos.Y = CrossingHeight(at, d.X, d.Y, d.DefinitionId);
                    drop = pos.Y - ground;
                }
                string key = $"{d.DefinitionId}|{d.Rotation}|{connect}|{Mathf.RoundToInt(pos.Y * 100)}|{d.X},{d.Y}";
                if (decorations.TryGetValue(d.Id, out var view) && view.key == key) continue;
                if (view.node != null && IsInstanceValid(view.node)) view.node.QueueFree();
                var node = BuildingModels.Decoration(def.Sprite, objectRoot, null, d.Rotation, connect, drop);
                node.Position = pos;
                decorations[d.Id] = (key, node);
                if (TallDecorations.TryGetValue(def.Sprite, out float h)) tallDecor[d.Id] = (node, h, false);
            }
            foreach (var id in decorations.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                if (IsInstanceValid(decorations[id].node)) decorations[id].node.QueueFree();
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

        readonly Dictionary<int, Node3D> harvestMarkers = new Dictionary<int, Node3D>();

        /// <summary>Gold frames on the trees, rocks and veins the player marked to gather first.</summary>
        void UpdateHarvestMarkers(Game game)
        {
            var orders = new HashSet<int>(game.State.HarvestOrders);
            foreach (var key in harvestMarkers.Keys.Where(k => !orders.Contains(k)).ToList())
            {
                harvestMarkers[key].QueueFree();
                harvestMarkers.Remove(key);
            }
            int w = game.Content.Map.Width;
            foreach (int key in orders)
                if (!harvestMarkers.ContainsKey(key)) harvestMarkers[key] = Marker(key % w, key / w, "Select");
        }

        HashSet<(int, int)> usedCells = new HashSet<(int, int)>();
        double nextNodeRefresh;

        float Standing(int x, int y)
        {
            if (usedCells.Contains((x, y))) return 0f;
            var node = store.Game.NodeAt(x, y);
            return node == null || double.IsInfinity(node.Remaining) ? 1f : (float)node.Fraction;
        }

        /// <summary>Metres of free ground kept around a building, a road and the settlement's heart.</summary>
        const float BuildingMargin = 0.6f, RoadMargin = 0.2f, DecorationMargin = 0.35f;
        /// <summary>The trodden ground around the campfire (or hearth): radius in metres and how ragged its edge is.</summary>
        const float HeartClearing = 6f, HeartClearingRagged = 1.6f;

        /// <summary>
        /// Ground nature keeps away from: every building's footprint and a little around it (so no crown
        /// grows through a roof), roads and decorations, and a round clearing about the fire.
        /// </summary>
        public static List<Nature.Clearing> Clearings(Game game)
        {
            var list = new List<Nature.Clearing>();
            void Rect(int x, int y, int w, int h, float margin, float ragged = 0f)
            {
                var a = Grid.CellCenter(x - 0.5f, y - 0.5f);
                var b = Grid.CellCenter(x + w - 0.5f, y + h - 0.5f);
                list.Add(new Nature.Clearing
                {
                    Min = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Z, b.Z)),
                    Max = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Z, b.Z)),
                    Margin = margin, Ragged = ragged,
                });
            }
            foreach (var b in game.State.Buildings)
            {
                var fp = game.FootprintOf(b);
                Rect(b.X, b.Y, fp.Width, fp.Height, BuildingMargin);
                if (game.Content.Building(b.DefinitionId)?.Heart ?? false)
                {
                    var c = Grid.FootprintCenter(b.X, b.Y, fp);
                    list.Add(new Nature.Clearing
                    {
                        Min = new Vector2(c.X, c.Z), Max = new Vector2(c.X, c.Z),
                        Margin = HeartClearing + 0.5f * Mathf.Max(fp.Width, fp.Height) * Grid.CellSize, Ragged = HeartClearingRagged,
                    });
                }
            }
            foreach (var d in game.State.Decorations)
                Rect(d.X, d.Y, 1, 1, (game.Content.Decoration(d.DefinitionId)?.IsRoad ?? false) ? RoadMargin : DecorationMargin);
            return list;
        }

        /// <summary>Trees, rocks and plants disappear from cells the player builds on.</summary>
        void UpdateOccupancy(Game game)
        {
            // Standing nature changes slowly: refresh when something is built, cut down or grows back,
            // and every few seconds while gatherers are at work.
            var s = game.State;
            string key = $"{s.NextBuildingId}|{s.Buildings.Count}|{s.Buildings.Sum(b => b.Rotation)}|{s.NextDecorationId}|{s.Decorations.Count}|{s.NodeCleared.Count}";
            if (key != occupancyKey)
            {
                occupancyKey = key;
                usedCells = OccupiedCells(game);
                nature.SetStanding(Standing);
                nature.SetClearings(Clearings(game));
                UpdateHarvestMarkers(game);
                return;
            }
            // While people gather, only the cells they work on change: refresh just those, now and then.
            if (now < nextNodeRefresh) return;
            nextNodeRefresh = now + 2;
            foreach (var job in Enums.Jobs)
            {
                if (game.State.Assignments[job] <= 0 || !GathersFromLand(job)) continue;
                var cells = game.GatherCells(job);
                for (int i = 0; i < cells.Count && i < 12; i++) nature.SetStandingCell(cells[i].x, cells[i].y, Standing(cells[i].x, cells[i].y));
            }
            UpdateHarvestMarkers(game);
        }

        // ------------------------------------------------------------------ selection and placement ghost

        void UpdateSelection()
        {
            int id = store.PlacingBuildingId == null && store.SelectedBuildingId.HasValue ? store.SelectedBuildingId.Value : -1;
            var b = id >= 0 ? store.Game.State.Building(id) : null;
            var def = b == null ? null : store.Game.Content.Building(b.DefinitionId);
            int key = def == null ? -1 : id * 1000 + def.Footprint.Width;
            if (key == selectionKey) return;
            selectionKey = key;
            foreach (var n in selection) n.QueueFree();
            selection.Clear();
            if (def == null) return;
            var fp = store.Game.FootprintOf(b);
            for (int dy = 0; dy < fp.Height; dy++)
                for (int dx = 0; dx < fp.Width; dx++)
                    selection.Add(Marker(b.X + dx, b.Y + dy, "Select"));
        }

        Node3D Marker(int x, int y, string material)
        {
            var c = Ground.OnGround(Grid.CellCenter(x, y));
            var frame = Group("Marker", overlayRoot, c + new Vector3(0, 0.06f, 0));
            float s = Grid.CellSize;
            var m = Palette.Get(material);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, s * 0.46f), new Vector3(s * 0.96f, 0.06f, 0.1f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(0, 0, -s * 0.46f), new Vector3(s * 0.96f, 0.06f, 0.1f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(s * 0.46f, 0, 0), new Vector3(0.1f, 0.06f, s * 0.96f), m, default, false);
            Parts.Prim(PrimitiveType.Cube, frame, new Vector3(-s * 0.46f, 0, 0), new Vector3(0.1f, 0.06f, s * 0.96f), m, default, false);
            return frame;
        }

        // ------------------------------------------------------------------ zones

        readonly Dictionary<string, Node3D> zoneOverlays = new Dictionary<string, Node3D>();
        string zoneOverlayKey;
        static readonly Dictionary<string, Material> zoneMaterials = new Dictionary<string, Material>();

        /// <summary>The painted zones as coloured tiles, one mesh per kind, shown while building or painting.</summary>
        void UpdateZoneOverlay(Game game)
        {
            bool show = game.HasZones && (store.ActivePanel == Panel.Build || Session.IsZoneTool(store.DecoratingId));
            string key = show ? $"{game.ZonesVersion}|{game.State.Zones.Count}|{game.State.NextBuildingId}" : "";
            if (key == zoneOverlayKey) return;
            zoneOverlayKey = key;
            foreach (var n in zoneOverlays.Values) n.QueueFree();
            zoneOverlays.Clear();
            if (!show) return;
            int w = game.Content.Map.Width;
            float h = Grid.CellSize * 0.42f;
            foreach (var zone in Game.ZoneIds)
            {
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                int quads = 0;
                foreach (var kv in game.State.Zones)
                {
                    if (kv.Value != zone) continue;
                    int x = kv.Key % w, y = kv.Key / w;
                    if (game.BuildingIdAt(x, y) != null) continue; // built on: the zone did its work
                    var c = Ground.OnGround(Grid.CellCenter(x, y)) + new Vector3(0, 0.05f, 0);
                    var a = c + new Vector3(-h, 0, -h);
                    var b = c + new Vector3(h, 0, -h);
                    var cc = c + new Vector3(h, 0, h);
                    var d = c + new Vector3(-h, 0, h);
                    st.SetNormal(Vector3.Up);
                    st.AddVertex(a); st.AddVertex(b); st.AddVertex(cc);
                    st.AddVertex(a); st.AddVertex(cc); st.AddVertex(d);
                    quads++;
                }
                if (quads == 0) continue;
                if (!zoneMaterials.TryGetValue(zone, out var mat))
                {
                    var src = (StandardMaterial3D)Palette.Get("Zone_" + zone).Duplicate();
                    src.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    zoneMaterials[zone] = mat = src;
                }
                var mi = new MeshInstance3D
                {
                    Name = "Zone_" + zone,
                    Mesh = st.Commit(),
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                overlayRoot.AddChild(mi);
                zoneOverlays[zone] = mi;
            }
        }

        void UpdateGhost()
        {
            var id = store.PlacingBuildingId;
            var cell = store.GhostCell;
            var def = id == null ? null : store.Game.Content.Building(id);
            if (def == null || cell == null)
            {
                if (ghost.Count > 0) { foreach (var n in ghost) n.QueueFree(); ghost.Clear(); }
                ghostKey = "";
                return;
            }
            bool valid = store.PlacementIssues().Count == 0;
            int rot = store.PlacementRotation;
            string key = $"{id}|{cell.Value.x},{cell.Value.y}|{valid}|{rot}";
            if (key == ghostKey) return;
            ghostKey = key;
            foreach (var n in ghost) n.QueueFree();
            ghost.Clear();

            string mat = valid ? "GhostOk" : "GhostBad";
            var fp = Game.Rotated(def.Footprint, rot);
            for (int dy = 0; dy < fp.Height; dy++)
                for (int dx = 0; dx < fp.Width; dx++)
                    ghost.Add(Marker(cell.Value.x + dx, cell.Value.y + dy, mat));
            var center = Ground.OnGround(Grid.FootprintCenter(cell.Value.x, cell.Value.y, fp));
            center.Y = Mathf.Max(center.Y, Terrain.WaterLevel + 0.12f); // as the building will stand (beach plots)
            var preview = Group("GhostModel", overlayRoot, center, rot * 90f);
            BuildingModels.Create(def.Sprite, preview, valid ? null : Palette.Get("GhostBad"), Grid.CellSize * Mathf.Min(def.Footprint.Width, def.Footprint.Height),
                                  1, new Vector2(def.Footprint.Width, def.Footprint.Height) * Grid.CellSize);
            if (!valid) preview.Scale = new Vector3(1f, 0.6f, 1f);
            ghost.Add(preview);
        }

        // ------------------------------------------------------------------ founding ghost

        readonly List<Node3D> foundingGhost = new List<Node3D>();
        string foundingKey = "";

        void UpdateFoundingGhost()
        {
            var cell = store.Founding ? store.FoundingCell : null;
            var fire = store.FoundingFire;
            string issue = cell == null ? null : store.FoundingIssue();
            string key = cell == null ? "" : $"{cell}|{fire}|{issue == null}";
            if (key == foundingKey) return;
            foundingKey = key;
            foreach (var n in foundingGhost) n.QueueFree();
            foundingGhost.Clear();
            if (cell == null) return;
            var game = store.Game;
            var campfire = game.Content.Building("campfire");
            var shelter = game.Content.Building("shelter");
            void Ghost(BuildingDefinition def, int x, int y, bool valid)
            {
                if (def == null) return;
                foundingGhost.Add(Marker(x, y, valid ? "GhostOk" : "GhostBad"));
                var center = Ground.OnGround(Grid.CellCenter(x, y));
                center.Y = Mathf.Max(center.Y, Terrain.WaterLevel + 0.12f);
                var preview = Group("FoundingGhost", overlayRoot, center, 0f);
                BuildingModels.Create(def.Sprite, preview, valid ? null : Palette.Get("GhostBad"), Grid.CellSize, 1,
                                      new Vector2(def.Footprint.Width, def.Footprint.Height) * Grid.CellSize);
                if (!valid) preview.Scale = new Vector3(1f, 0.6f, 1f);
                foundingGhost.Add(preview);
            }
            bool ok = issue == null;
            if (fire is (int fx, int fy))
            {
                Ghost(campfire, fx, fy, true);
                foreach (var (x, y) in game.SettlementCells(cell.Value.x, cell.Value.y)) Ghost(shelter, x, y, ok);
                // The warmth of the fire: the shelters must stand inside this square.
                int r = Game.SettlementReach;
                for (int d = -r; d <= r; d++)
                    foreach (var (x, y) in new[] { (fx + d, fy - r), (fx + d, fy + r), (fx - r, fy + d), (fx + r, fy + d) })
                        if (game.IsSettleable(x, y)) foundingGhost.Add(Marker(x, y, "Select"));
            }
            else Ghost(campfire, cell.Value.x, cell.Value.y, ok);
        }
    }
}
