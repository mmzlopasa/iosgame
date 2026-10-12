using Founders.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Founders
{
    /// <summary>
    /// Day and night plus the look of the seasons, driven by game time: the sun travels and
    /// warms at dawn and dusk, a short blue night lets the hearths glow, grass and leaf trees
    /// turn golden in autumn and pale in winter, and snow falls in winter.
    /// </summary>
    public class Atmosphere : MonoBehaviour
    {
        /// <summary>Seconds of game time per full day: six real hours (one season, so a year is four days).</summary>
        public const float DayLength = 6 * 3600f;

        /// <summary>0 by day, 1 at midnight (read by villagers and audio).</summary>
        public static float Nightness { get; private set; }

        GameStore store;
        Light sun;
        Material sky;
        float skyExposure = 1f;
        WhiteBalance white;
        float baseTemperature;
        ParticleSystem snow;
        Color grassTint = Color.white, leafTint = Color.white;

        static readonly Color Dawn = new Color(1f, 0.62f, 0.38f);
        static readonly Color Moon = new Color(0.55f, 0.66f, 1f);
        static readonly Color NightFog = new Color(0.17f, 0.21f, 0.30f);

        public void Init(GameStore owner)
        {
            store = owner;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure")) skyExposure = sky.GetFloat("_Exposure");
            var volume = FindAnyObjectByType<Volume>();
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out WhiteBalance wb))
            {
                white = wb;
                baseTemperature = wb.temperature.value;
            }
            BuildSnow();
        }

        void BuildSnow()
        {
            var go = new GameObject("Snow");
            go.transform.SetParent(transform, false);
            snow = go.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = snow.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.maxParticles = 1500;
            main.gravityModifier = 0.025f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 1f, 60f);
            var noise = snow.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            var em = snow.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Palette.Get("Mist");
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.maxParticleSize = 0.006f; // flakes right in front of the lens stay small
            snow.Play();
        }

        void Update()
        {
            var game = store?.Game;
            if (game == null || sun == null) return;
            double t = game.State.SimTime;

            // Day: 75 % of the cycle, night: 25 %.
            float p = (float)(t % DayLength / DayLength);
            float elevation, nightness;
            float yaw;
            if (p < 0.75f)
            {
                float u = p / 0.75f;
                elevation = Mathf.Sin(u * Mathf.PI);
                nightness = 0f;
                yaw = Mathf.Lerp(-130f, 50f, u);
            }
            else
            {
                float n = (p - 0.75f) / 0.25f;
                elevation = 0f;
                nightness = Mathf.Sin(n * Mathf.PI);
                yaw = Mathf.Lerp(50f, 230f, n);
            }
            Nightness = nightness;

            var season = game.Season;
            string id = season?.Id ?? "summer";
            Color noon = id switch
            {
                "winter" => new Color(0.86f, 0.92f, 1f),
                "autumn" => new Color(1f, 0.86f, 0.68f),
                "spring" => new Color(1f, 0.95f, 0.86f),
                _ => new Color(1f, 0.92f, 0.78f),
            };
            float seasonLight = id == "winter" ? 0.85f : 1f;

            Color dayColor = Color.Lerp(Dawn, noon, Mathf.SmoothStep(0f, 1f, elevation / 0.4f));
            float dayIntensity = Mathf.Lerp(0.9f, 2.1f, elevation) * seasonLight;
            sun.color = Color.Lerp(dayColor, Moon, nightness);
            // A bright moon: the town stays readable at night, the lights still glow.
            sun.intensity = Mathf.Lerp(dayIntensity, 0.95f, nightness);
            float pitch = p < 0.75f ? Mathf.Lerp(9f, 56f, elevation) : Mathf.Lerp(12f, 48f, nightness);
            sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            RenderSettings.ambientIntensity = Mathf.Lerp(Mathf.Lerp(0.75f, 1f, elevation), 0.82f, nightness);
            RenderSettings.reflectionIntensity = Mathf.Lerp(1f, 0.3f, nightness);
            if (sky != null && sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(skyExposure, skyExposure * 0.55f, nightness));
            var fog = id == "winter" ? new Color(0.78f, 0.82f, 0.88f) : SceneEnvironment.FogColor;
            RenderSettings.fogColor = Color.Lerp(fog, NightFog, nightness);

            if (white != null)
            {
                float temp = id switch { "summer" => 8f, "autumn" => 16f, "winter" => -16f, _ => 2f };
                white.temperature.value = Mathf.Lerp(baseTemperature + temp, -12f, nightness);
            }

            // Vegetation tints follow the season (eased over the first part of each season).
            Color grass = id switch
            {
                "autumn" => new Color(1.15f, 0.88f, 0.52f),
                "winter" => new Color(0.82f, 0.84f, 0.86f),
                "summer" => new Color(1.05f, 1.0f, 0.82f),
                _ => new Color(0.95f, 1.08f, 0.9f),
            };
            Color leaf = id switch
            {
                "autumn" => new Color(1.35f, 0.72f, 0.35f),
                "winter" => new Color(0.62f, 0.56f, 0.5f),
                _ => Color.white,
            };
            float ease = Mathf.Clamp01((float)game.SeasonProgress / 0.15f);
            grassTint = Color.Lerp(grassTint, grass, ease * Time.deltaTime * 2f + 0.002f);
            leafTint = Color.Lerp(leafTint, leaf, ease * Time.deltaTime * 2f + 0.002f);
            Tint("I_GrassTuft_side", grassTint);
            Tint("I_GrassTuft_top", grassTint);
            Tint("I_Wildflowers_side", grassTint);
            Tint("I_Wildflowers_top", grassTint);
            Tint("I_LeafTree_side", leafTint);
            Tint("I_LeafTree_side2", leafTint);
            Tint("I_LeafTree_top", leafTint);

            // Snow follows the camera in winter.
            var em = snow.emission;
            // Snow follows the cold: flurries below freezing, a blizzard in a great frost.
            float outside = game.HasClimate ? (float)game.Temperature : (id == "winter" ? -5f : 10f);
            em.rateOverTime = outside < 0f ? Mathf.Lerp(90f, 360f, Mathf.Clamp01(-outside / 22f)) : 0f;
            var cam = Camera.main;
            if (cam != null)
            {
                var c = cam.transform.position;
                snow.transform.position = new Vector3(c.x, c.y + 6f, c.z) + cam.transform.forward * 14f;
            }
        }

        static void Tint(string material, Color c)
        {
            var m = Palette.Get(material);
            if (m != null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
