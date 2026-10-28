using System.Collections.Generic;
using Founders.Core;
using Founders.World;
using Godot;

namespace Founders
{
    /// <summary>
    /// Ambience and music (all CC0, art/audio). Loops are mixed by what the camera sees: the crackle of
    /// the nearest hearth, the river when it is close, birds by day (not in winter), wind in winter and
    /// from high up. Two calm music tracks alternate with a pause between them.
    /// </summary>
    public sealed partial class AudioDirector : Node
    {
        static AudioDirector instance;
        AudioStreamPlayer fire, river, birds, wind, musicA, musicB, ui;
        readonly List<AudioStream> tracks = new List<AudioStream>();
        int track;
        bool musicOnA = true;
        double now, nextTrackAt = 4;
        readonly Dictionary<string, AudioStream> uiClips = new Dictionary<string, AudioStream>();
        public Session Store;

        public static bool MusicOn
        {
            get => Prefs.GetInt("audio.music.on", 1) == 1;
            set => Prefs.SetInt("audio.music.on", value ? 1 : 0);
        }

        public static bool SoundOn
        {
            get => Prefs.GetInt("audio.sound.on", 1) == 1;
            set => Prefs.SetInt("audio.sound.on", value ? 1 : 0);
        }

        /// <summary>Short UI sound: "ui_tap", "ui_open", "ui_confirm".</summary>
        public static void Ui(string name)
        {
            if (instance == null || !SoundOn) return;
            if (!instance.uiClips.TryGetValue(name, out var clip)) instance.uiClips[name] = clip = Load(name);
            if (clip == null) return;
            instance.ui.Stream = clip;
            instance.ui.VolumeDb = Mathf.LinearToDb(0.5f);
            instance.ui.Play();
        }

        static AudioStream Load(string name)
        {
            string path = $"res://art/audio/{name}.ogg";
            return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        }

        public override void _Ready()
        {
            Name = "Audio";
            instance = this;
            ProcessMode = ProcessModeEnum.Always;
            fire = Loop("fire");
            river = Loop("river");
            birds = Loop("birds");
            wind = Loop("wind");
            ui = new AudioStreamPlayer { Name = "Ui" };
            AddChild(ui);
            musicA = new AudioStreamPlayer { Name = "MusicA", VolumeDb = -80f };
            musicB = new AudioStreamPlayer { Name = "MusicB", VolumeDb = -80f };
            AddChild(musicA);
            AddChild(musicB);
            foreach (var n in new[] { "music_first_light", "music_forest" })
            {
                var s = Load(n);
                if (s is AudioStreamOggVorbis ogg) ogg.Loop = false;
                if (s != null) tracks.Add(s);
            }
        }

        AudioStreamPlayer Loop(string name)
        {
            var p = new AudioStreamPlayer { Name = name, VolumeDb = -80f };
            AddChild(p);
            var s = Load(name);
            if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
            p.Stream = s;
            if (s != null) p.Play((float)GD.RandRange(0, s.GetLength() * 0.9));
            return p;
        }

        static float Vol(AudioStreamPlayer p) => p.VolumeDb <= -79f ? 0f : Mathf.DbToLinear(p.VolumeDb);
        static void SetVol(AudioStreamPlayer p, float v) => p.VolumeDb = v <= 0.001f ? -80f : Mathf.LinearToDb(v);

        public override void _Process(double delta)
        {
            now += delta;
            var game = Store?.Game;
            var cam = GetViewport()?.GetCamera3D();
            if (game == null || cam == null) return;
            float k = (float)delta * 1.5f;
            bool sound = SoundOn;

            // Where the camera looks (on the ground) and how high it is.
            var c = cam.GlobalPosition;
            var fwd = -cam.GlobalBasis.Z;
            var focus = c + fwd * (c.Y / Mathf.Max(0.2f, -fwd.Y));
            float height = c.Y;

            float fireDist = float.MaxValue;
            foreach (var b in game.State.Buildings)
            {
                if (b.DefinitionId != "campfire" && b.DefinitionId != "village_hearth" && b.DefinitionId != "community_circle") continue;
                fireDist = Mathf.Min(fireDist, focus.DistanceTo(Grid.CellCenter(b.X, b.Y)));
            }
            float riverDist = float.MaxValue;
            var map = game.Content.Map;
            var (fx, fy) = Grid.CellAt(focus);
            for (int y = fy - 8; y <= fy + 8; y++)
                for (int x = fx - 8; x <= fx + 8; x++)
                    if (map.Terrain(Mathf.Clamp(x, 0, map.Width - 1), Mathf.Clamp(y, 0, map.Height - 1)) == TerrainType.Water)
                        riverDist = Mathf.Min(riverDist, new Vector2(x, y).DistanceTo(new Vector2(fx, fy)) * Grid.CellSize);

            string season = game.Season?.Id ?? "summer";
            float night = Atmosphere.Nightness;
            float zoom = Mathf.Clamp(Mathf.InverseLerp(8f, 40f, height), 0f, 1f);
            float sv = Prefs.SoundVolume;

            Set(fire, sound ? sv * Mathf.Clamp(1f - fireDist / 14f, 0f, 1f) * Mathf.Lerp(0.55f, 0.25f, zoom) * (1f + night * 0.4f) : 0f, k);
            Set(river, sound ? sv * Mathf.Clamp(1f - riverDist / 16f, 0f, 1f) * 0.45f : 0f, k);
            Set(birds, sound && season != "winter" ? sv * 0.32f * (1f - night) * (season == "autumn" ? 0.5f : 1f) : 0f, k);
            Set(wind, sound ? sv * ((season == "winter" ? 0.45f : 0.08f) + zoom * 0.15f) : 0f, k);

            // Music: alternate tracks with gentle fades and a pause between them.
            var playing = musicOnA ? musicA : musicB;
            float target = MusicOn ? 0.32f * Prefs.MusicVolume / 0.7f : 0f;
            SetVol(playing, Mathf.MoveToward(Vol(playing), target, (float)delta * 0.15f));
            var other = musicOnA ? musicB : musicA;
            SetVol(other, Mathf.MoveToward(Vol(other), 0f, (float)delta * 0.15f));
            if (tracks.Count > 0 && now >= nextTrackAt && !playing.Playing)
            {
                musicOnA = !musicOnA;
                var next = musicOnA ? musicA : musicB;
                next.Stream = tracks[track++ % tracks.Count];
                SetVol(next, 0f);
                next.Play();
                nextTrackAt = now + next.Stream.GetLength() + GD.RandRange(25.0, 60.0);
            }
        }

        static void Set(AudioStreamPlayer s, float volume, float k)
        {
            if (s?.Stream == null) return;
            SetVol(s, Mathf.Lerp(Vol(s), volume, Mathf.Clamp(k, 0f, 1f)));
        }
    }
}
