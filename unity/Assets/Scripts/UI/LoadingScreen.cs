using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The loading screen shown while the world is generated: five paintings of Green Hollow (loading_1..5, from
    /// tools/loading) drift slowly in a Ken Burns move and dissolve into one another, each with its own chapter
    /// caption, while embers rise from the bottom of the frame. The title sits at the top; the progress bar, a
    /// tip and the version along the bottom. Any shape of screen: the paintings fill it, cropped like aspect-fill.
    /// </summary>
    public sealed class LoadingScreen
    {
        const float SceneSeconds = 7f;
        const float FadeSeconds = 1.6f;
        const int EmberCount = 18;

        struct Scene
        {
            public string Title, Line;
            public float ScaleFrom, ScaleTo;
            public Vector2 From, To; // offset in percent of the screen
            public Scene(string title, string line, float s0, float s1, Vector2 from, Vector2 to)
            {
                Title = title; Line = line; ScaleFrom = s0; ScaleTo = s1; From = from; To = to;
            }
        }

        static readonly Scene[] Scenes =
        {
            new Scene("Green Hollow", "A sheltered valley of meadows, deep woods and clear water.", 1.06f, 1.16f, new Vector2(0, 1), new Vector2(-2, -1)),
            new Scene("The First Village", "Every house raised by hand, every hearth shared.", 1.16f, 1.07f, new Vector2(2, 0), new Vector2(-1.5f, 0.5f)),
            new Scene("The Trade Road", "Caravans bring salt, copper and stories from far away.", 1.07f, 1.17f, new Vector2(-2, 0), new Vector2(1, -1)),
            new Scene("The Market", "Where neighbours meet, barter and plan the seasons ahead.", 1.15f, 1.07f, new Vector2(-1.5f, -1), new Vector2(1.5f, 0.5f)),
            new Scene("When Night Falls", "The fires keep burning. The story goes on.", 1.07f, 1.15f, new Vector2(1, 0.5f), new Vector2(0, -1)),
        };

        static readonly string[] Numerals = { "I", "II", "III", "IV", "V" };

        static readonly string[] Tips =
        {
            "Stone gatherers must work next to rock. Scout the ridges to find more.",
            "Food spoils in the open. A storage pit keeps part of it safe.",
            "Idle hands help no one: assign every newcomer a job.",
            "Builders speed up construction. Two builders raise a shelter twice as fast.",
            "Every choice you make is written into the Legacy Timeline.",
            "Shelters let the community grow. Without them, newcomers will not stay.",
            "A Trading Post opens the road to your neighbours. Caravans bring back what you lack.",
            "The market gathers the village. More stalls mean better prices.",
        };

        readonly VisualElement layer;
        readonly VisualElement[] slides = new VisualElement[2];
        readonly int[] slideScene = { -1, -1 };
        readonly Texture2D[] paintings = new Texture2D[Scenes.Length];
        readonly VisualElement brand, captionBox, progressRow, tipRow, embersLayer, fill, head, intro;
        /// <summary>The SCYDE logo is shown once, when the game starts (it continues the launch screen).</summary>
        static bool introShown;
        bool introNow;
        readonly VisualElement[] pips = new VisualElement[Scenes.Length];
        readonly Label chapter, captionTitle, captionLine, percent, status, tip, version;
        readonly VisualElement[] embers = new VisualElement[EmberCount];
        readonly float[] emberSeed = new float[EmberCount];
        readonly IVisualElementScheduledItem ticker;
        float startTime, lastTick;
        int scene = -1;
        int tipStart;
        float target, shown;
        bool loaded;

        public bool Visible { get; private set; }

        public LoadingScreen(VisualElement layer)
        {
            this.layer = layer;
            layer.pickingMode = PickingMode.Position;
            layer.style.backgroundColor = Theme.Hex("#0B0C10");
            layer.style.overflow = Overflow.Hidden;
            layer.Transition(520, "opacity");

            // The paintings: two layers that take turns, the incoming one dissolving in over the other.
            var stage = E.Div().Fill().Ignore();
            layer.Add(stage);
            for (int i = 0; i < 2; i++)
            {
                var s = E.Div().Fill().Ignore();
                s.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                s.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                s.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
                s.style.opacity = 0;
                slides[i] = s;
                stage.Add(s);
            }

            // Shade at the top for the title and a deeper one at the bottom for the caption and the bar.
            var topShade = E.Div().Abs(0, 0, 0).BgImage(Theme.Gradient("load_top", true,
                new Color(0.04f, 0.03f, 0.02f, 0.62f), new Color(0.04f, 0.03f, 0.02f, 0.18f), new Color(0.04f, 0.03f, 0.02f, 0f))).Ignore();
            topShade.style.height = Length.Percent(38);
            layer.Add(topShade);
            var bottomShade = E.Div().Abs(0, null, 0, 0).BgImage(Theme.Gradient("load_bottom", true,
                new Color(0.04f, 0.03f, 0.02f, 0f), new Color(0.04f, 0.03f, 0.02f, 0.45f), new Color(0.03f, 0.02f, 0.02f, 0.86f))).Ignore();
            bottomShade.style.height = Length.Percent(48);
            layer.Add(bottomShade);

            // Embers drifting up from the hearth below the frame.
            embersLayer = E.Div().Fill().Ignore();
            var dot = EmberTexture();
            for (int i = 0; i < EmberCount; i++)
            {
                var e = E.Div().Ignore();
                e.style.position = Position.Absolute;
                e.style.backgroundImage = new StyleBackground(dot);
                e.style.opacity = 0;
                embers[i] = e;
                emberSeed[i] = Random.value * 100f;
                embersLayer.Add(e);
            }
            layer.Add(embersLayer);

            // The title.
            brand = E.Div().Col(Align.Center).Ignore();
            brand.style.position = Position.Absolute;
            brand.style.left = 0; brand.style.right = 0;
            brand.style.top = Length.Percent(7);
            brand.Add(E.Image(Theme.Image("logo_mark"), 64, 64));
            var word = E.Title("FOUNDERS", 40);
            word.style.letterSpacing = 5;
            word.style.marginTop = 4;
            brand.Add(word);
            var sub = E.T("THE LONG RISE", 14, Theme.Gold);
            sub.style.letterSpacing = 4;
            brand.Add(sub);
            layer.Add(brand);

            // The chapter caption of the painting on screen: numeral between two rules, name, one line, the pips.
            captionBox = E.Div().Col(Align.Center).Pad(0, 24).Abs(0, null, 0, 112).Ignore();
            var chapterRow = E.Div().Row().Ignore();
            chapterRow.Add(Rule(false));
            chapter = E.T("I", Theme.Small, Theme.Gold);
            chapter.style.letterSpacing = 3;
            chapter.style.marginLeft = 10; chapter.style.marginRight = 10;
            chapterRow.Add(chapter);
            chapterRow.Add(Rule(true));
            captionBox.Add(chapterRow);
            captionTitle = E.T("", 26, Theme.Wood1);
            captionTitle.style.letterSpacing = 1.5f;
            captionTitle.style.marginTop = 2;
            captionBox.Add(captionTitle);
            captionLine = E.T("", Theme.Body, Theme.Pale).Align(TextAnchor.MiddleCenter).Wrap();
            captionLine.style.maxWidth = 560;
            captionLine.style.marginTop = 2;
            captionBox.Add(captionLine);
            var pipRow = E.Div().Row().Ignore();
            pipRow.style.marginTop = 8;
            for (int i = 0; i < pips.Length; i++)
            {
                var p = E.Div().Ignore().Radius(3);
                p.style.height = 5;
                p.style.marginLeft = 3; p.style.marginRight = 3;
                p.Transition(500, "width", "background-color");
                pips[i] = p;
                pipRow.Add(p);
            }
            captionBox.Add(pipRow);
            layer.Add(captionBox);

            // The progress: what is happening and how far, over a slim ember-gold bar with a glowing head.
            progressRow = E.Div().Col(Align.Center).Pad(0, 24).Abs(0, null, 0, 46).Ignore();
            var progress = E.Div().Col().Ignore();
            progress.style.width = Length.Percent(100);
            progress.style.maxWidth = 520;
            var labels = E.Div().Row().Between().Ignore();
            status = E.T("Kindling the hearth…", Theme.Small, Theme.Pale);
            labels.Add(status);
            percent = E.T("0%", Theme.Small, Theme.Gold);
            labels.Add(percent);
            progress.Add(labels);
            var track = E.Div().Ignore().Radius(4).Bg(new Color(0.02f, 0.02f, 0.03f, 0.55f)).Border(new Color(1f, 0.88f, 0.48f, 0.28f));
            track.style.height = 8;
            track.style.marginTop = 5;
            fill = E.Div().Ignore().Radius(3).BgImage(Theme.Gradient("load_fill", false,
                Theme.Hex("#9C4A16"), Theme.Hex("#E98A3A"), Theme.Hex("#FFD27A")));
            fill.style.position = Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.width = Length.Percent(0);
            track.Add(fill);
            head = E.Div().Ignore();
            head.style.position = Position.Absolute;
            head.style.width = 26; head.style.height = 26;
            head.style.top = -10;
            head.style.marginLeft = -13;
            head.style.backgroundImage = new StyleBackground(dot);
            track.Add(head);
            progress.Add(track);
            progressRow.Add(progress);
            layer.Add(progressRow);

            tipRow = E.Div().Col(Align.Center).Pad(0, 24).Abs(0, null, 0, 24).Ignore();
            tip = E.T("", Theme.Small, Theme.ProseSoft).Align(TextAnchor.MiddleCenter).Wrap();
            tip.style.maxWidth = 600;
            tipRow.Add(tip);
            layer.Add(tipRow);

            version = E.T($"v{Application.version} · Era I — First Communities", 10, new Color(1, 1, 1, 0.45f))
                .Align(TextAnchor.MiddleCenter).Abs(0, null, 0, 6);
            layer.Add(version);

            // The studio's logo, the same picture as the launch screen, so the game opens on it without a
            // jump; it gives way to the paintings after a moment.
            intro = E.Div().Fill().Ignore();
            intro.style.backgroundColor = Theme.Hex("#202734");
            var splash = Theme.Image("scyde_splash");
            if (splash != null)
            {
                intro.style.backgroundImage = new StyleBackground(splash);
                intro.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                intro.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                intro.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            }
            intro.Hidden(true);
            layer.Add(intro);

            tipStart = Random.Range(0, Tips.Length);
            ticker = layer.schedule.Execute(Tick).Every(16);
            ticker.Pause();
        }

        static VisualElement Rule(bool right)
        {
            var r = E.Div().Ignore().BgImage(right
                ? Theme.Gradient("load_rule_r", false, new Color(1f, 0.88f, 0.48f, 0.8f), new Color(1f, 0.88f, 0.48f, 0f))
                : Theme.Gradient("load_rule_l", false, new Color(1f, 0.88f, 0.48f, 0f), new Color(1f, 0.88f, 0.48f, 0.8f)));
            r.style.width = 54;
            r.style.height = 1;
            return r;
        }

        static Texture2D emberTexture;

        /// <summary>A soft round glow: white-gold core fading through orange to nothing.</summary>
        static Texture2D EmberTexture()
        {
            if (emberTexture != null) return emberTexture;
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f));
                    float a = Mathf.Pow(1 - d, 2.2f);
                    var c = Color.Lerp(new Color(1f, 0.93f, 0.7f), new Color(1f, 0.45f, 0.12f), Mathf.Clamp01(d * 1.6f));
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                }
            t.Apply(false, true);
            return emberTexture = t;
        }

        /// <summary>
        /// Keeps the title under the notch and the bottom rows above the home indicator (insets in panel units). In
        /// portrait the title sits lower and the caption, the bar and the tip get the narrow screen's full width.
        /// </summary>
        public void SetInsets(float top, float bottom, bool portrait)
        {
            brand.style.top = portrait ? new Length(top + 56) : Length.Percent(7);
            captionBox.style.bottom = bottom + (portrait ? 128 : 112);
            progressRow.style.bottom = bottom + (portrait ? 58 : 46);
            tipRow.style.bottom = bottom + 22;
            version.style.bottom = bottom + 6;
            float side = portrait ? 16 : 24;
            foreach (var row in new[] { captionBox, progressRow, tipRow })
            {
                row.style.paddingLeft = side;
                row.style.paddingRight = side;
            }
        }

        void LoadPaintings()
        {
            if (loaded) return;
            loaded = true;
            for (int i = 0; i < paintings.Length; i++)
            {
                paintings[i] = Theme.Image("loading_" + (i + 1));
                if (paintings[i] == null) Debug.LogWarning($"[FOUNDERS] loading painting loading_{i + 1} missing.");
            }
        }

        public void Show()
        {
            LoadPaintings();
            Visible = true;
            startTime = lastTick = Time.realtimeSinceStartup;
            scene = -1;
            slideScene[0] = slideScene[1] = -1;
            for (int i = 0; i < EmberCount; i++) emberSeed[i] = Random.value * 100f;
            introNow = !introShown && Theme.Image("scyde_splash") != null;
            introShown = true;
            intro.Hidden(!introNow);
            intro.style.opacity = 1;
            if (introNow) startTime += IntroSeconds; // the paintings start once the logo begins to fade
            Tick();
            ticker.Resume();
            layer.style.opacity = 1;
            layer.Hidden(false);
        }

        public void Set(float value, string text = null)
        {
            target = Mathf.Clamp01(value);
            if (!Visible) shown = target;
            percent.text = Mathf.RoundToInt(target * 100) + "%";
            if (text != null) status.text = text;
        }

        public float Value => target;

        public void Hide()
        {
            Visible = false;
            layer.style.opacity = 0;
            layer.schedule.Execute(() =>
            {
                if (Visible) return;
                layer.Hidden(true);
                ticker.Pause();
            }).StartingIn(560);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3 - 2 * x);
        }

        const float IntroSeconds = 1.8f, IntroFade = 0.7f;

        void Tick()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Min(0.1f, now - lastTick);
            lastTick = now;
            float t = Mathf.Max(0f, now - startTime);
            if (introNow)
            {
                float since = now - (startTime - IntroSeconds);
                float o = 1f - Smooth((since - IntroSeconds + IntroFade * 0.3f) / IntroFade);
                intro.style.opacity = o;
                if (o <= 0f) { introNow = false; intro.Hidden(true); }
            }
            int n = Scenes.Length;
            int k = Mathf.FloorToInt(t / SceneSeconds);
            float local = t - k * SceneSeconds;

            // The painting on screen, and from FadeSeconds before its end the next one dissolving in over it.
            var cur = slides[k % 2];
            Frame(cur, k % 2, k % n, (local + FadeSeconds) / (SceneSeconds + FadeSeconds), k == 0 ? Smooth(t / 1.4f) : 1f);
            var next = slides[(k + 1) % 2];
            float into = local - (SceneSeconds - FadeSeconds);
            if (into > 0)
            {
                next.BringToFront();
                Frame(next, (k + 1) % 2, (k + 1) % n, into / (SceneSeconds + FadeSeconds), Smooth(into / FadeSeconds));
            }
            else next.style.opacity = 0;

            // The caption follows the painting: in after the dissolve, out just before the next.
            if (k != scene)
            {
                scene = k;
                int s = k % n;
                chapter.text = "CHAPTER " + Numerals[s];
                captionTitle.text = Scenes[s].Title;
                captionLine.text = Scenes[s].Line;
                tip.text = "Tip · " + Tips[(tipStart + k) % Tips.Length];
                for (int i = 0; i < pips.Length; i++)
                {
                    pips[i].style.width = i == s ? 18 : 5;
                    pips[i].style.backgroundColor = i == s ? Theme.Gold : new Color(1, 1, 1, 0.35f);
                }
            }
            float appear = Smooth((local - 0.25f) / 0.9f) * (1 - Smooth((local - (SceneSeconds - FadeSeconds * 0.9f)) / 0.6f));
            captionBox.style.opacity = appear;
            captionBox.style.translate = new Translate(0, (1 - appear) * 10);
            tip.style.opacity = 0.35f + 0.65f * appear;

            // The bar eases towards the real progress so each step glides instead of jumping.
            shown = Mathf.MoveTowards(shown + (target - shown) * Mathf.Min(1, dt * 5), target, dt * 0.05f);
            fill.style.width = Length.Percent(shown * 100);
            head.style.left = Length.Percent(shown * 100);
            head.style.opacity = 0.75f + 0.25f * Mathf.Sin(now * 5.3f);

            // Embers: rise, sway and flicker out, each on its own loop.
            for (int i = 0; i < EmberCount; i++)
            {
                float seed = emberSeed[i];
                float life = 5.5f + (seed % 3.7f);
                float age = (t + seed) % life / life;
                float x = (seed * 7.31f % 100f) + Mathf.Sin((t + seed) * 0.8f) * 2.2f + age * ((seed % 2f) - 1f) * 8f;
                float size = 3f + (seed * 3.17f % 5f);
                var e = embers[i];
                e.style.left = Length.Percent(x);
                e.style.bottom = Length.Percent(-2 + age * 62);
                e.style.width = size; e.style.height = size;
                e.style.opacity = Mathf.Sin(age * Mathf.PI) * (0.55f + 0.45f * Mathf.PerlinNoise(seed, t * 3f));
            }
        }

        /// <summary>Puts painting <paramref name="index"/> on a layer at a point of its slow drift.</summary>
        void Frame(VisualElement slide, int slot, int index, float progress, float opacity)
        {
            if (slideScene[slot] != index)
            {
                slideScene[slot] = index;
                var tex = paintings[index];
                slide.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            }
            var sc = Scenes[index];
            float p = Mathf.Clamp01(progress);
            float s = Mathf.Lerp(sc.ScaleFrom, sc.ScaleTo, p);
            var o = Vector2.Lerp(sc.From, sc.To, p);
            slide.style.scale = new Scale(new Vector3(s, s, 1));
            slide.style.translate = new Translate(Length.Percent(o.x), Length.Percent(o.y));
            slide.style.opacity = opacity;
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

        /// <summary>The sharpest the 3D view is drawn at the chosen quality (see <see cref="FrameGovernor"/>).</summary>
        public static float RenderScaleCap => Quality == 0 ? 0.62f : Quality == 1 ? 0.74f : 0.85f;

        public static void Apply()
        {
            // As fast as the screen refreshes (120 on ProMotion iPhones and iPads), or 30 to save the battery.
            int screenHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = BatterySaver ? 30 : Mathf.Max(60, screenHz);
            QualitySettings.vSyncCount = 0;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (!baseShadowDistance.HasValue) baseShadowDistance = urp.shadowDistance;
                // Retina phones draw ~3 pixels per point: rendering below native and upscaling
                // (FSR) keeps the picture sharp while holding the frame rate.
                urp.renderScale = RenderScaleCap;
                urp.shadowDistance = baseShadowDistance.Value * (Quality == 0 ? 0.55f : Quality == 1 ? 0.75f : 1f);
            }
        }
    }
}
