using Founders.Core;
using Godot;

namespace Founders.World
{
    /// <summary>
    /// Day and night plus the look of the seasons, driven by game time (as in the Unity version): the
    /// sun travels and warms at dawn and dusk, a short gentle night lets the hearths glow, meadows and
    /// leaves turn golden in autumn and pale in winter, and snow falls when it freezes.
    /// </summary>
    public sealed partial class Atmosphere : Node3D
    {
        /// <summary>Seconds of game time per full day: six real hours (one season, so a year is four days).</summary>
        public const float DayLength = 6 * 3600f;

        /// <summary>0 by day, 1 at midnight (villagers, lamps and audio read it).</summary>
        public static float Nightness { get; private set; }
        public static string SeasonId { get; private set; } = "summer";

        DirectionalLight3D sun, fill;
        WorldEnvironment worldEnv;
        ProceduralSkyMaterial sky;
        GpuParticles3D snow;
        Color grassTint = Colors.White, leafTint = Colors.White;
        float snowCover;

        static readonly Color Dawn = new Color(1f, 0.62f, 0.38f);
        static readonly Color Moon = new Color(0.78f, 0.84f, 1f);

        public Terrain Ground;
        public Nature Plants;
        public ShaderMaterial Water;
        public Camera3D Camera;
        public System.Func<Game> GameOf;

        public override void _Ready()
        {
            Name = "Atmosphere";
            sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("5f8fc9"),
                SkyHorizonColor = new Color("d8e3ea"),
                GroundHorizonColor = new Color("c9c2a8"),
                GroundBottomColor = new Color("5b5340"),
                SunAngleMax = 30f,
            };
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.9f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 6f,
                FogEnabled = true,
                FogLightColor = new Color("c8d4dc"),
                FogDensity = 0.0025f,
                FogSkyAffect = 0f,
                GlowEnabled = true,
                GlowHdrThreshold = 1.4f,
                GlowIntensity = 0.6f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 1.08f,
                AdjustmentContrast = 1.04f,
            };
            worldEnv = new WorldEnvironment { Environment = env };
            AddChild(worldEnv);

            sun = new DirectionalLight3D
            {
                Name = "Sun",
                ShadowEnabled = true,
                DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
                DirectionalShadowMaxDistance = 140f,
                ShadowBlur = 1.5f,
            };
            AddChild(sun);
            fill = new DirectionalLight3D { Name = "Fill", ShadowEnabled = false, LightColor = new Color(0.86f, 0.9f, 1f), LightEnergy = 0.35f };
            AddChild(fill);
            BuildSnow();
        }

        void BuildSnow()
        {
            var mat = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(30f, 0.5f, 30f),
                Gravity = new Vector3(0, -0.9f, 0),
                InitialVelocityMin = 0.1f,
                InitialVelocityMax = 0.4f,
                Direction = new Vector3(0.3f, -1f, 0.1f),
                Spread = 25f,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 0.6f,
                TurbulenceNoiseScale = 4f,
            };
            var flake = new QuadMesh { Size = new Vector2(0.09f, 0.09f) };
            flake.Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoColor = new Color(1, 1, 1, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            snow = new GpuParticles3D
            {
                Name = "Snow",
                Amount = 1400,
                Lifetime = 9f,
                ProcessMaterial = mat,
                DrawPass1 = flake,
                Emitting = false,
                VisibilityAabb = new Aabb(new Vector3(-40, -40, -40), new Vector3(80, 60, 80)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(snow);
        }

        public override void _Process(double delta)
        {
            var game = GameOf?.Invoke();
            if (game == null) return;
            double t = game.State.SimTime;

            // Day: 75 % of the cycle, night: 25 %.
            float p = (float)(t % DayLength / DayLength);
            float elevation, nightness, yaw;
            if (p < 0.75f)
            {
                float u = p / 0.75f;
                elevation = Mathf.Sin(u * Mathf.Pi);
                nightness = 0f;
                yaw = Mathf.Lerp(-130f, 50f, u);
            }
            else
            {
                float n = (p - 0.75f) / 0.25f;
                elevation = 0f;
                nightness = Mathf.Sin(n * Mathf.Pi);
                yaw = Mathf.Lerp(50f, 230f, n);
            }
            string id = game.Season?.Id ?? "summer";
            SeasonId = id;
            Color noon = id switch
            {
                "winter" => new Color(0.86f, 0.92f, 1f),
                "autumn" => new Color(1f, 0.86f, 0.68f),
                "spring" => new Color(1f, 0.95f, 0.86f),
                _ => new Color(1f, 0.92f, 0.78f),
            };
            float seasonLight = id == "winter" ? 0.92f : 1f;
            // A gentle night (as in Foundation): the valley stays readable, only bluer and dimmer.
            nightness *= 0.55f;
            Nightness = nightness;

            var dayColor = Dawn.Lerp(noon, Mathf.SmoothStep(0f, 1f, elevation / 0.4f));
            float dayEnergy = Mathf.Lerp(0.75f, 1.45f, elevation) * seasonLight;
            sun.LightColor = dayColor.Lerp(Moon, nightness);
            sun.LightEnergy = Mathf.Lerp(dayEnergy, 0.6f, nightness);
            float pitch = p < 0.75f ? Mathf.Lerp(32f, 58f, elevation) : Mathf.Lerp(34f, 50f, nightness);
            // Godot's directional lights shine along −Z of their basis.
            sun.RotationDegrees = new Vector3(-pitch, -yaw, 0f);
            fill.RotationDegrees = new Vector3(-40f, -yaw + 180f, 0f);
            fill.LightEnergy = sun.LightEnergy * 0.3f;

            var env = worldEnv.Environment;
            env.AmbientLightEnergy = Mathf.Lerp(Mathf.Lerp(0.75f, 1f, elevation), 0.8f, nightness);
            sky.SkyEnergyMultiplier = Mathf.Lerp(1f, 0.45f, nightness);
            var fog = id == "winter" ? new Color(0.78f, 0.82f, 0.88f) : new Color("c8d4dc");
            env.FogLightColor = fog.Lerp(new Color(0.36f, 0.42f, 0.52f), nightness);

            Color grass = id switch
            {
                "autumn" => new Color(1.15f, 0.88f, 0.52f),
                "winter" => new Color(0.82f, 0.84f, 0.86f),
                "summer" => new Color(1.05f, 1.0f, 0.86f),
                _ => new Color(0.95f, 1.08f, 0.9f),
            };
            Color leaf = id switch
            {
                "autumn" => new Color(1.35f, 0.72f, 0.35f),
                "winter" => new Color(0.62f, 0.56f, 0.5f),
                _ => Colors.White,
            };
            float ease = Mathf.Clamp((float)game.SeasonProgress / 0.15f, 0f, 1f);
            float k = ease * (float)delta * 2f + 0.002f;
            grassTint = grassTint.Lerp(grass, k);
            leafTint = leafTint.Lerp(leaf, k);

            float outside = game.HasClimate ? (float)game.Temperature : (id == "winter" ? -5f : 10f);
            float wantSnow = outside < 0f ? Mathf.Clamp(-outside / 8f, 0.25f, 0.85f) : 0f;
            snowCover = Mathf.MoveToward(snowCover, wantSnow, (float)delta * 0.02f);

            if (Ground != null)
            {
                Ground.Material.SetShaderParameter("season_tint", new Vector3(grassTint.R, grassTint.G, grassTint.B));
                Ground.Material.SetShaderParameter("snow", snowCover);
            }
            Plants?.SetSeason(leafTint, snowCover, id == "winter" ? 1.4f : 1f);
            if (Water != null)
            {
                Water.SetShaderParameter("night", nightness);
                Water.SetShaderParameter("frozen", outside < -8f ? Mathf.Clamp((-outside - 8f) / 10f, 0f, 0.9f) : 0f);
            }

            bool falling = outside < 0f;
            snow.Emitting = falling;
            snow.AmountRatio = falling ? Mathf.Lerp(0.25f, 1f, Mathf.Clamp(-outside / 22f, 0f, 1f)) : 0f;
            if (Camera != null)
            {
                var c = Camera.GlobalPosition;
                snow.GlobalPosition = new Vector3(c.X, c.Y + 6f, c.Z) - Camera.GlobalBasis.Z * 14f;
            }
        }
    }
}
