using Godot;

namespace Founders
{
    /// <summary>
    /// Small settings kept on this device (Unity's PlayerPrefs): graphics, haptics, sound, and the cloud's
    /// device id and sign-in. Stored in user://prefs.cfg.
    /// </summary>
    public static class Prefs
    {
        const string File = "user://prefs.cfg";
        const string Section = "prefs";
        static ConfigFile cfg;

        static ConfigFile Cfg
        {
            get
            {
                if (cfg != null) return cfg;
                cfg = new ConfigFile();
                cfg.Load(File);
                return cfg;
            }
        }

        public static int GetInt(string key, int fallback) => (int)Cfg.GetValue(Section, key, fallback);
        public static float GetFloat(string key, float fallback) => (float)(double)Cfg.GetValue(Section, key, (double)fallback);
        public static string GetString(string key, string fallback) => (string)Cfg.GetValue(Section, key, fallback);

        public static void SetInt(string key, int value) { Cfg.SetValue(Section, key, value); Save(); }
        public static void SetFloat(string key, float value) { Cfg.SetValue(Section, key, (double)value); Save(); }
        public static void SetString(string key, string value) { Cfg.SetValue(Section, key, value ?? ""); Save(); }

        public static void Save() => Cfg.Save(File);

        // ------------------------------------------------------------------ graphics and feel

        /// <summary>0 battery-friendly, 1 balanced, 2 sharp.</summary>
        public static int Quality
        {
            get => GetInt("gfx.quality", 2);
            set { SetInt("gfx.quality", value); Apply(); }
        }

        public static bool BatterySaver
        {
            get => GetInt("gfx.battery", 0) == 1;
            set { SetInt("gfx.battery", value ? 1 : 0); Apply(); }
        }

        public static bool Haptics
        {
            get => GetInt("haptics", 1) == 1;
            set => SetInt("haptics", value ? 1 : 0);
        }

        public static float MusicVolume
        {
            get => GetFloat("audio.music", 0.7f);
            set => SetFloat("audio.music", value);
        }

        public static float SoundVolume
        {
            get => GetFloat("audio.sound", 0.9f);
            set => SetFloat("audio.sound", value);
        }

        /// <summary>
        /// As fast as the screen refreshes (120 on ProMotion iPhones and iPads), or 30 to save the battery;
        /// below native resolution on the lower settings (retina phones draw ~3 pixels per point).
        /// </summary>
        public static void Apply()
        {
            float hz = DisplayServer.ScreenGetRefreshRate();
            Engine.MaxFps = BatterySaver ? 30 : Mathf.Max(60, Mathf.RoundToInt(hz > 0 ? hz : 60));
            if (Engine.GetMainLoop() is SceneTree tree)
            {
                var vp = tree.Root;
                vp.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
                vp.Scaling3DScale = Quality switch { 0 => 0.62f, 1 => 0.74f, _ => 0.85f };
            }
        }

        /// <summary>A short buzz (milestones), when the player has not switched it off.</summary>
        public static void Vibrate(int ms = 40)
        {
            if (Haptics) Input.VibrateHandheld(ms);
        }
    }
}

namespace Founders
{
    /// <summary>Facts about the build.</summary>
    public static class Platform
    {
        /// <summary>The app's version (project.godot application/config/version).</summary>
        public static string Version
        {
            get
            {
                var v = ProjectSettings.GetSetting("application/config/version", "1.0").AsString();
                return string.IsNullOrEmpty(v) ? "1.0" : v;
            }
        }
    }
}
