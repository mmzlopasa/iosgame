using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>The cairn-and-sunrise loading screen shown while the world is generated.</summary>
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
        readonly VisualElement fill, card;
        readonly Label percent, status;
        float shown;

        public bool Visible { get; private set; }

        public LoadingScreen(VisualElement layer)
        {
            this.layer = layer;
            layer.pickingMode = PickingMode.Position;
            layer.Cover(Theme.Image("loading_bg"));
            layer.style.backgroundColor = Theme.Bg;
            layer.Transition(420, "opacity");

            var brand = E.Div().Col(Align.Center).Ignore();
            brand.style.position = Position.Absolute;
            brand.style.left = 0; brand.style.right = 0;
            brand.style.top = Length.Percent(23);
            brand.Add(E.Image(Theme.Image("logo_mark"), 132, 132));
            var word = E.Text("FOUNDERS", 38, Theme.Ink, Theme.F.DisplayBold, 6.5f).Margin(20);
            brand.Add(word);
            var sub = E.Div().Row().Margin(10).Ignore();
            sub.Add(E.Div().Size(28, 1).Bg(Theme.Alpha(Theme.Gold, 0.6f)).Ignore());
            var subText = E.Text("THE LONG RISE", 12, Theme.Gold, Theme.F.TextBold, 3.6f);
            subText.style.marginLeft = 10; subText.style.marginRight = 10;
            sub.Add(subText);
            sub.Add(E.Div().Size(28, 1).Bg(Theme.Alpha(Theme.Gold, 0.6f)).Ignore());
            brand.Add(sub);
            var tagline = E.Text("Build a world that outlives you.", 14, Theme.Body).Margin(14);
            tagline.style.unityFontStyleAndWeight = FontStyle.Italic;
            brand.Add(tagline);
            layer.Add(brand);

            card = E.Div().Row(0, Align.FlexStart).Glass(20).Pad(14, 16).Abs(24, null, 24, 150).Ignore();
            var tile = E.Div().Size(30, 30).Radius(10).Center().Bg(Theme.Alpha(Theme.Gold, 0.16f)).Ignore();
            tile.Add(E.Icon("knowledge", 16, Theme.Gold));
            card.Add(tile);
            var tipCol = E.Div().Col().Grow().Ignore();
            tipCol.style.marginLeft = 12;
            tipCol.Add(E.Eyebrow("Founder’s tip", Theme.Gold, 10));
            tipCol.Add(E.Text(Tips[Random.Range(0, Tips.Length)], 13, Theme.Ink).Margin(4));
            card.Add(tipCol);
            layer.Add(card);

            var progress = E.Div().Col().Abs(24, null, 24, 74).Ignore();
            var labels = E.Div().Row().Between().Ignore();
            status = E.Text("Kindling the hearth…", 12, Theme.Body);
            labels.Add(status);
            percent = E.Mono("0%", 12, Theme.GoldLight);
            labels.Add(percent);
            progress.Add(labels);
            var bar = E.Bar(out fill, 6, Theme.GoldFill);
            bar.style.marginTop = 10;
            progress.Add(bar);
            layer.Add(progress);

            var version = E.Text($"v{Application.version} · ERA I — FIRST COMMUNITIES", 10.5f, Theme.Muted, Theme.F.Mono, 0.6f)
                .Align(TextAnchor.MiddleCenter).Abs(0, null, 0, 30);
            layer.Add(version);
        }

        public void Show()
        {
            Visible = true;
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
            layer.schedule.Execute(() => { if (!Visible) layer.Hidden(true); }).StartingIn(450);
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
                    case 0: urp.renderScale = 0.7f; urp.shadowDistance = baseShadowDistance.Value * 0.6f; break;
                    case 1: urp.renderScale = 0.85f; urp.shadowDistance = baseShadowDistance.Value * 0.8f; break;
                    default: urp.renderScale = 1f; urp.shadowDistance = baseShadowDistance.Value; break;
                }
            }
        }
    }
}
