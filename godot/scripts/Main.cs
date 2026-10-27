using System;
using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using Godot;

namespace Founders
{
    /// <summary>
    /// First Godot slice: loads the rules (Core/) and the save, ticks the simulation, draws the valley with
    /// placeholder buildings, and shows the stock on a top bar. Drag to pan, pinch or scroll to zoom.
    /// </summary>
    public partial class Main : Node3D
    {
        static readonly Color Ink = new Color("202734");
        static readonly Color Panel = new Color("36415A");

        GameContent baseContent;
        Game game;
        FileSaveStore saves;
        readonly SystemClock clock = new SystemClock();
        double stepTimer, saveTimer;

        Camera3D camera;
        Vector3 target;
        float distance = 30f;
        const float Pitch = 52f, Yaw = 35f, MinDistance = 8f, MaxDistance = 90f;
        readonly Dictionary<int, Vector2> touches = new Dictionary<int, Vector2>();

        Node3D buildingRoot;
        string buildingSignature = "";
        Label stockLabel, statusLabel;

        public override void _Ready()
        {
            baseContent = GameContent.Load(Godot.FileAccess.GetFileAsString("res://data/content.json"));
            saves = new FileSaveStore(ProjectSettings.GlobalizePath("user://"));
            var outcome = saves.Load(baseContent);
            game = outcome.Result == LoadResult.Loaded
                ? new Game(baseContent, outcome.State, clock)
                : Game.NewGame(baseContent.ForNewGame, clock); // the founding step comes back with the new UI
            game.Update();

            BuildEnvironment();
            BuildTerrain(game.Content.Map);
            buildingRoot = new Node3D { Name = "Buildings" };
            AddChild(buildingRoot);
            BuildHud();
            FocusOnSettlement();
            Refresh();
            Save();
        }

        public override void _Process(double delta)
        {
            stepTimer += delta;
            saveTimer += delta;
            if (stepTimer >= 1.0)
            {
                stepTimer = 0;
                game.Update();
                game.DrainNotices();
                Refresh();
            }
            if (saveTimer >= 30.0) Save();
        }

        public override void _Notification(int what)
        {
            if (what == NotificationApplicationPaused || what == NotificationWMCloseRequest || what == NotificationApplicationFocusOut)
                Save();
        }

        void Save()
        {
            saveTimer = 0;
            if (game != null) saves.Save(game.State, game.Content, clock.Now());
        }

        // ---------------------------------------------------------------- world

        void BuildEnvironment()
        {
            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("6f9bd1"),
                SkyHorizonColor = new Color("f2d6a8"),
                GroundHorizonColor = new Color("c9b48c"),
                GroundBottomColor = new Color("5b4a35"),
            };
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.8f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            };
            AddChild(new WorldEnvironment { Environment = env });

            var sun = new DirectionalLight3D
            {
                LightColor = new Color("fff1d6"),
                LightEnergy = 1.2f,
                ShadowEnabled = true,
                RotationDegrees = new Vector3(-50f, 40f, 0f),
            };
            AddChild(sun);

            camera = new Camera3D { Fov = 40f, Far = 400f };
            AddChild(camera);
        }

        static float TileHeight(TerrainType? t)
        {
            switch (t)
            {
                case TerrainType.Water: return -0.6f;
                case TerrainType.Sand: return 0.0f;
                case TerrainType.Forest: return 0.25f;
                case TerrainType.Rock: return 0.7f;
                case TerrainType.Ore: return 0.9f;
                case TerrainType.Mountain: return 3.2f;
                default: return 0.2f;
            }
        }

        static Color TileColor(TerrainType? t)
        {
            switch (t)
            {
                case TerrainType.Water: return new Color("4d7f8f");
                case TerrainType.Sand: return new Color("d8c08a");
                case TerrainType.Forest: return new Color("4f7a3a");
                case TerrainType.Rock: return new Color("8f8a80");
                case TerrainType.Ore: return new Color("8a6f5a");
                case TerrainType.Mountain: return new Color("9a948a");
                default: return new Color("7da454");
            }
        }

        void BuildTerrain(MapDefinition map)
        {
            int w = map.Width, h = map.Height;
            var noise = new FastNoiseLite { Seed = 7, Frequency = 0.08f };

            // Each corner sits at the average height of the tiles around it, so the valley rolls instead of stepping.
            var corner = new float[w + 1, h + 1];
            for (int cy = 0; cy <= h; cy++)
                for (int cx = 0; cx <= w; cx++)
                {
                    float sum = 0; int n = 0;
                    for (int dy = -1; dy <= 0; dy++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            var t = map.Terrain(cx + dx, cy + dy);
                            if (t == null) continue;
                            sum += TileHeight(t); n++;
                        }
                    corner[cx, cy] = (n > 0 ? sum / n : 0f) + noise.GetNoise2D(cx, cy) * 0.15f;
                }

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = TileColor(map.Terrain(x, y));
                    float shade = 1f + noise.GetNoise2D(x * 3.1f, y * 3.1f) * 0.08f;
                    st.SetColor(new Color(c.R * shade, c.G * shade, c.B * shade));
                    var a = new Vector3(x, corner[x, y], y);
                    var b = new Vector3(x + 1, corner[x + 1, y], y);
                    var d = new Vector3(x, corner[x, y + 1], y + 1);
                    var e = new Vector3(x + 1, corner[x + 1, y + 1], y + 1);
                    st.AddVertex(a); st.AddVertex(b); st.AddVertex(e);
                    st.AddVertex(a); st.AddVertex(e); st.AddVertex(d);
                }
            st.GenerateNormals();
            var ground = new MeshInstance3D
            {
                Name = "Ground",
                Mesh = st.Commit(),
                MaterialOverride = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 1f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            AddChild(ground);

            var water = new MeshInstance3D
            {
                Name = "Water",
                Mesh = new PlaneMesh { Size = new Vector2(w + 40, h + 40) },
                Position = new Vector3(w / 2f, -0.15f, h / 2f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.30f, 0.55f, 0.65f, 0.85f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.35f,
                },
            };
            AddChild(water);

            AddScatter(map, corner, TerrainType.Forest, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.45f, Height = 1.4f }, new Color("3d6b30"), 0.7f);
            AddScatter(map, corner, TerrainType.Rock, new SphereMesh { Radius = 0.35f, Height = 0.4f }, new Color("8c877d"), 0.2f);
        }

        void AddScatter(MapDefinition map, float[,] corner, TerrainType kind, Mesh mesh, Color color, float lift)
        {
            var spots = new List<Vector3>();
            var rng = new RandomNumberGenerator { Seed = (ulong)(int)kind + 11 };
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    if (map.Terrain(x, y) == kind)
                    {
                        float fx = rng.RandfRange(0.25f, 0.75f), fy = rng.RandfRange(0.25f, 0.75f);
                        float ground = Mathf.Lerp(Mathf.Lerp(corner[x, y], corner[x + 1, y], fx), Mathf.Lerp(corner[x, y + 1], corner[x + 1, y + 1], fx), fy);
                        spots.Add(new Vector3(x + fx, ground + lift, y + fy));
                    }
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = spots.Count };
            for (int i = 0; i < spots.Count; i++)
            {
                float s = rng.RandfRange(0.8f, 1.25f);
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), spots[i]));
            }
            AddChild(new MultiMeshInstance3D
            {
                Name = kind + "Scatter",
                Multimesh = mm,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
            });
        }

        void RefreshBuildings()
        {
            var sig = string.Join(";", game.State.Buildings.Select(b => $"{b.Id}:{b.DefinitionId}:{b.Status}"));
            if (sig == buildingSignature) return;
            buildingSignature = sig;
            foreach (var child in buildingRoot.GetChildren()) child.QueueFree();

            foreach (var b in game.State.Buildings)
            {
                var def = game.Content.Building(b.DefinitionId);
                if (def == null) continue;
                var size = new Vector2(Math.Max(1, def.Footprint.Width), Math.Max(1, def.Footprint.Height));
                bool built = b.Status != BuildingStatus.Constructing;
                var box = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(size.X * 0.85f, 0.9f, size.Y * 0.85f) },
                    Position = new Vector3(b.X + size.X / 2f, 0.65f, b.Y + size.Y / 2f),
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = built ? new Color("b07a4a") : new Color("d9c7a3"),
                        Roughness = 1f,
                    },
                };
                buildingRoot.AddChild(box);
                buildingRoot.AddChild(new Label3D
                {
                    Text = def.Name,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    FontSize = 48,
                    PixelSize = 0.006f,
                    OutlineSize = 12,
                    Modulate = Colors.White,
                    OutlineModulate = Ink,
                    Position = box.Position + new Vector3(0, 1.0f, 0),
                    NoDepthTest = true,
                });
            }
        }

        // ---------------------------------------------------------------- camera

        void FocusOnSettlement()
        {
            var list = game.State.Buildings;
            target = list.Count > 0
                ? new Vector3((float)list.Average(b => b.X) + 0.5f, 0f, (float)list.Average(b => b.Y) + 0.5f)
                : new Vector3(game.Content.Map.Width / 2f, 0f, game.Content.Map.Height / 2f);
            UpdateCamera();
        }

        void UpdateCamera()
        {
            var map = game.Content.Map;
            target.X = Mathf.Clamp(target.X, 0, map.Width);
            target.Z = Mathf.Clamp(target.Z, 0, map.Height);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            var dir = Basis.FromEuler(new Vector3(Mathf.DegToRad(-Pitch), Mathf.DegToRad(Yaw), 0)) * Vector3.Back;
            camera.Position = target + dir * distance;
            camera.LookAt(target, Vector3.Up);
        }

        void Pan(Vector2 screenDelta)
        {
            var right = camera.GlobalBasis.X; right.Y = 0;
            var forward = -camera.GlobalBasis.Z; forward.Y = 0;
            float k = distance / GetViewport().GetVisibleRect().Size.Y * 1.1f;
            target -= right.Normalized() * screenDelta.X * k;
            target += forward.Normalized() * screenDelta.Y * k;
            UpdateCamera();
        }

        void Zoom(float factor)
        {
            distance *= factor;
            UpdateCamera();
        }

        public override void _UnhandledInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventScreenTouch t:
                    if (t.Pressed) touches[t.Index] = t.Position; else touches.Remove(t.Index);
                    break;
                case InputEventScreenDrag d:
                    if (touches.Count >= 2 && touches.ContainsKey(d.Index))
                    {
                        var other = touches.First(p => p.Key != d.Index).Value;
                        float before = touches[d.Index].DistanceTo(other), after = d.Position.DistanceTo(other);
                        if (after > 1f) Zoom(before / after);
                    }
                    else if (touches.Count <= 1) Pan(d.Relative);
                    touches[d.Index] = d.Position;
                    break;
                case InputEventMagnifyGesture g:
                    if (g.Factor > 0.01f) Zoom(1f / g.Factor);
                    break;
                case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Left) != 0:
                    Pan(m.Relative);
                    break;
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp:
                    Zoom(0.9f);
                    break;
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown:
                    Zoom(1.1f);
                    break;
            }
        }

        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            var layer = new CanvasLayer();
            AddChild(layer);

            var bar = new PanelContainer();
            bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(Ink, 0.92f),
                BorderColor = Panel,
                BorderWidthBottom = 3,
                CornerRadiusBottomLeft = 18,
                CornerRadiusBottomRight = 18,
                ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 12, ContentMarginBottom = 12,
            });
            bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
            bar.GrowHorizontal = Control.GrowDirection.Both;
            layer.AddChild(bar);

            stockLabel = new Label();
            stockLabel.AddThemeFontSizeOverride("font_size", 34);
            stockLabel.AddThemeColorOverride("font_color", new Color("f3e6c8"));
            bar.AddChild(stockLabel);

            statusLabel = new Label { Text = "" };
            statusLabel.AddThemeFontSizeOverride("font_size", 24);
            statusLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
            statusLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
            statusLabel.Position += new Vector2(40, -60);
            layer.AddChild(statusLabel);
        }

        void Refresh()
        {
            var s = game.State;
            var parts = new[] { ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Knowledge }
                .Select(t => $"{t.DisplayName()} {Math.Floor(s.Resources[t])}");
            stockLabel.Text = string.Join("     ", parts) + $"     People {s.Population}";
            statusLabel.Text = $"{s.SettlementName} · Godot preview";
            RefreshBuildings();
        }
    }
}
