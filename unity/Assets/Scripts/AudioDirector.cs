using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    /// <summary>
    /// Ambience and music (all CC0, Resources/Audio). Loops are mixed by what the camera sees:
    /// the crackle of the nearest hearth, the river when it is close, birds by day (not in
    /// winter), wind in winter and from high up. Two calm music tracks alternate.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        static AudioDirector instance;
        AudioSource fire, river, birds, wind, musicA, musicB, ui;
        AudioClip[] tracks;
        int track;
        bool musicOnA = true;
        float nextTrackAt;
        GameStore store;

        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt("audio.music", 1) == 1;
            set { PlayerPrefs.SetInt("audio.music", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt("audio.sound", 1) == 1;
            set { PlayerPrefs.SetInt("audio.sound", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Short UI sound: "ui_tap", "ui_open", "ui_confirm".</summary>
        public static void Ui(string name)
        {
            if (instance == null || !SoundOn) return;
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip != null) instance.ui.PlayOneShot(clip, 0.5f);
        }

        public void Init(GameStore owner)
        {
            instance = this;
            store = owner;
            fire = Loop("fire");
            river = Loop("river");
            birds = Loop("birds");
            wind = Loop("wind");
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            musicA = gameObject.AddComponent<AudioSource>();
            musicB = gameObject.AddComponent<AudioSource>();
            foreach (var m in new[] { musicA, musicB }) { m.playOnAwake = false; m.loop = false; m.volume = 0f; }
            tracks = new[] { Resources.Load<AudioClip>("Audio/music_first_light"), Resources.Load<AudioClip>("Audio/music_forest") }
                .Where(c => c != null).ToArray();
            nextTrackAt = Time.unscaledTime + 4f;
        }

        AudioSource Loop(string name)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Resources.Load<AudioClip>("Audio/" + name);
            s.loop = true;
            s.volume = 0f;
            s.playOnAwake = false;
            if (s.clip != null)
            {
                s.Play();
                s.time = Random.Range(0f, s.clip.length * 0.9f);
            }
            return s;
        }

        void Update()
        {
            var game = store?.Game;
            var cam = Camera.main;
            if (game == null || cam == null) return;
            float k = Time.unscaledDeltaTime * 1.5f;
            bool sound = SoundOn;

            // Where the camera looks (on the ground) and how high it is.
            var c = cam.transform.position;
            var focus = c + cam.transform.forward * (c.y / Mathf.Max(0.2f, -cam.transform.forward.y));
            float height = c.y;

            float fireDist = float.MaxValue;
            foreach (var b in game.State.Buildings)
            {
                if (b.DefinitionId != "campfire" && b.DefinitionId != "village_hearth" && b.DefinitionId != "community_circle") continue;
                fireDist = Mathf.Min(fireDist, Vector3.Distance(focus, Grid.CellCenter(b.X, b.Y)));
            }
            float riverDist = float.MaxValue;
            var map = game.Content.Map;
            var (fx, fy) = Grid.CellAt(focus);
            for (int y = fy - 8; y <= fy + 8; y++)
                for (int x = fx - 8; x <= fx + 8; x++)
                    if (map.Terrain(Mathf.Clamp(x, 0, map.Width - 1), Mathf.Clamp(y, 0, map.Height - 1)) == TerrainType.Water)
                        riverDist = Mathf.Min(riverDist, Vector2.Distance(new Vector2(x, y), new Vector2(fx, fy)) * Grid.CellSize);

            string season = game.Season?.Id ?? "summer";
            float night = Atmosphere.Nightness;
            float zoom = Mathf.InverseLerp(8f, 40f, height);

            Set(fire, sound ? Mathf.Clamp01(1f - fireDist / 14f) * Mathf.Lerp(0.55f, 0.25f, zoom) * (1f + night * 0.4f) : 0f, k);
            Set(river, sound ? Mathf.Clamp01(1f - riverDist / 16f) * 0.45f : 0f, k);
            Set(birds, sound && season != "winter" ? 0.32f * (1f - night) * (season == "autumn" ? 0.5f : 1f) : 0f, k);
            Set(wind, sound ? (season == "winter" ? 0.45f : 0.08f) + zoom * 0.15f : 0f, k);

            // Music: alternate tracks with gentle fades and a pause between them.
            var playing = musicOnA ? musicA : musicB;
            float target = MusicOn ? 0.32f : 0f;
            playing.volume = Mathf.MoveTowards(playing.volume, target, Time.unscaledDeltaTime * 0.15f);
            var other = musicOnA ? musicB : musicA;
            other.volume = Mathf.MoveTowards(other.volume, 0f, Time.unscaledDeltaTime * 0.15f);
            if (tracks.Length > 0 && Time.unscaledTime >= nextTrackAt && !playing.isPlaying)
            {
                musicOnA = !musicOnA;
                var next = musicOnA ? musicA : musicB;
                next.clip = tracks[track++ % tracks.Length];
                next.volume = 0f;
                next.Play();
                nextTrackAt = Time.unscaledTime + next.clip.length + Random.Range(25f, 60f);
            }
        }

        static void Set(AudioSource s, float volume, float k)
        {
            if (s == null || s.clip == null) return;
            s.volume = Mathf.Lerp(s.volume, volume, Mathf.Clamp01(k));
        }
    }
}
