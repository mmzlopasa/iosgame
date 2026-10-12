using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Founders.UI
{
    /// <summary>
    /// The loading screen shown while the world is generated: a looping montage (any aspect; it fills the
    /// screen, cropped like aspect-fill) behind the title, a tip and the progress bar.
    /// </summary>
    public sealed class LoadingScreen
    {
        static readonly string[] Tips =
        {
            "Stone gatherers must work next to rock. Scout the ridges to find more.",
            "Food spoils in the open. A storage pit keeps part of it safe.",
            "Idle hands help no one: assign every newcomer a job.",
            "Builders speed up construction. Two builders raise a shelter twice as fast.",
            "Every choice you make is written into the Legacy Timeline.",
            "Shelters let the community grow. Without them, newcomers will not stay.",
        };

        readonly VisualElement layer;
        readonly VisualElement fill, card, video, tipRow, progressRow;
        readonly Label version;
        VideoPlayer player;
        GameObject videoHost;
        RenderTexture videoTexture;
        readonly Label percent, status;
        float shown;

        public bool Visible { get; private set; }

        public LoadingScreen(VisualElement layer)
        {
            this.layer = layer;
            layer.pickingMode = PickingMode.Position;
            layer.style.backgroundImage = new StyleBackground(Theme.Image("loading_bg"));
            layer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            layer.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            layer.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            layer.style.backgroundColor = Theme.Hex("#0D0F14");
            layer.Transition(420, "opacity");

            // A montage of the valley plays behind the title (fades in once the first frame is ready).
            video = E.Div().Fill().Ignore();
            video.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            video.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            video.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            video.style.opacity = 0;
            video.Transition(900, "opacity");
            layer.Add(video);
            var topShade = E.Div().Abs(0, 0, 0).BgImage(Theme.TopShade).Ignore();
            topShade.style.height = Length.Percent(45);
            layer.Add(topShade);
            var bottomShade = E.Div().Abs(0, null, 0, 0).BgImage(Theme.BottomShade).Ignore();
            bottomShade.style.height = Length.Percent(45);
            layer.Add(bottomShade);

            // Landscape: the title in the upper half, then a one-line tip, the progress bar and the version
            // along the bottom (kept clear of the home indicator by SetInsets).
            var brand = E.Div().Col(Align.Center).Ignore();
            brand.style.position = Position.Absolute;
            brand.style.left = 0; brand.style.right = 0;
            brand.style.top = Length.Percent(9);
            brand.Add(E.Image(Theme.Image("logo_mark"), 70, 70));
            var word = E.T("FOUNDERS", 38);
            word.style.letterSpacing = 4;
            word.style.marginTop = 6;
            brand.Add(word);
            var sub = E.T("THE LONG RISE", 14, Theme.Gold);
            sub.style.letterSpacing = 3;
            brand.Add(sub);
            brand.Add(E.T("Build a world that outlives you.", 12.5f, Theme.Pale).Margin(6));
            layer.Add(brand);

            tipRow = E.Div().Col(Align.Center).Pad(0, 24).Abs(0, null, 0, 76).Ignore();
            card = E.Panel().Row().Pad(5, 12, 9, 10).Ignore();
            card.style.maxWidth = 560;
            card.Add(E.Icon("knowledge", 26));
            var tipCol = E.Div().Col().Grow().Ignore();
            tipCol.style.marginLeft = 8;
            tipCol.Add(E.T("Founder's tip", 11, Theme.Gold));
            tipCol.Add(E.T(Tips[Random.Range(0, Tips.Length)], 11.5f).Wrap());
            card.Add(tipCol);
            tipRow.Add(card);
            layer.Add(tipRow);

            progressRow = E.Div().Col(Align.Center).Pad(0, 24).Abs(0, null, 0, 34).Ignore();
            var progress = E.Div().Col().Ignore();
            progress.style.width = Length.Percent(100);
            progress.style.maxWidth = 520;
            var labels = E.Div().Row().Between().Ignore();
            status = E.T("Kindling the hearth…", 12, Theme.Pale);
            labels.Add(status);
            percent = E.T("0%", 12, Theme.Gold);
            labels.Add(percent);
            progress.Add(labels);
            var bar = E.Bar(out fill, 12);
            bar.style.marginTop = 4;
            progress.Add(bar);
            progressRow.Add(progress);
            layer.Add(progressRow);

            version = E.T($"v{Application.version} · Era I — First Communities", 10, Theme.Pale)
                .Align(TextAnchor.MiddleCenter).Abs(0, null, 0, 10);
            layer.Add(version);
        }

        /// <summary>Lifts the bottom rows above the home indicator (inset in panel units).</summary>
        public void SetInsets(float bottom)
        {
            tipRow.style.bottom = bottom + 76;
            progressRow.style.bottom = bottom + 34;
            version.style.bottom = bottom + 10;
        }

        /// <summary>Starts the loading montage (Resources/Video/loading) on a VideoPlayer owned by <paramref name="host"/>.</summary>
        public void StartVideo(GameObject host)
        {
            videoHost = host;
            // Played from StreamingAssets by URL: no import step, so every build machine can ship it.
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "loading.mp4");
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[FOUNDERS] loading video: missing " + path); return; }
            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = path;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.errorReceived += (p, message) => Debug.LogWarning("[FOUNDERS] loading video error: " + message);
            player.prepareCompleted += p =>
            {
                Debug.Log($"[FOUNDERS] loading video ready {p.width}x{p.height} visible={Visible}");
                if (!Visible) return;
                videoTexture = new RenderTexture((int)p.width, (int)p.height, 0) { name = "LoadingVideo" };
                p.targetTexture = videoTexture;
                video.style.backgroundImage = Background.FromRenderTexture(videoTexture);
                p.Play();
                video.style.opacity = 0.62f; // dimmed so the title stays readable
            };
            player.Prepare();
        }

        void StopVideo()
        {
            if (player == null) return;
            player.Stop();
            Object.Destroy(player);
            player = null;
            video.style.backgroundImage = StyleKeyword.None;
            if (videoTexture != null) { videoTexture.Release(); Object.Destroy(videoTexture); videoTexture = null; }
        }

        public bool VideoPlaying => player != null && player.isPlaying;

        public void Show()
        {
            Visible = true;
            if (player == null && videoHost != null) StartVideo(videoHost);
            layer.style.opacity = 1;
            layer.Hidden(false);
        }

        public void Set(float value, string text = null)
        {
            shown = Mathf.Clamp01(value);
            E.SetFill(fill, shown);
            percent.text = Mathf.RoundToInt(shown * 100) + "%";
            if (text != null) status.text = text;
        }

        public float Value => shown;

        public void Hide()
        {
            Visible = false;
            layer.style.opacity = 0;
            layer.schedule.Execute(() => { if (!Visible) { layer.Hidden(true); StopVideo(); } }).StartingIn(450);
        }
    }

    /// <summary>Player preferences (device-local) and how they are applied.</summary>
    public static class Prefs
    {
        public static int Quality
        {
            get => PlayerPrefs.GetInt("gfx.quality", 2);
            set { PlayerPrefs.SetInt("gfx.quality", value); PlayerPrefs.Save(); }
        }

        public static bool BatterySaver
        {
            get => PlayerPrefs.GetInt("gfx.battery", 0) == 1;
            set { PlayerPrefs.SetInt("gfx.battery", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool Haptics
        {
            get => PlayerPrefs.GetInt("haptics", 1) == 1;
            set { PlayerPrefs.SetInt("haptics", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static float? baseShadowDistance;

        public static void Apply()
        {
            Application.targetFrameRate = BatterySaver ? 30 : 60;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (!baseShadowDistance.HasValue) baseShadowDistance = urp.shadowDistance;
                switch (Quality)
                {
                    // Retina phones draw ~3 pixels per point: rendering below native and upscaling
                    // (FSR) keeps the picture sharp while holding 60 fps.
                    case 0: urp.renderScale = 0.62f; urp.shadowDistance = baseShadowDistance.Value * 0.6f; break;
                    case 1: urp.renderScale = 0.74f; urp.shadowDistance = baseShadowDistance.Value * 0.8f; break;
                    default: urp.renderScale = 0.85f; urp.shadowDistance = baseShadowDistance.Value; break;
                }
            }
        }
    }
}
