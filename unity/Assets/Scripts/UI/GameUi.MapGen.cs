using System.Collections;
using System.Linq;
using Founders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Founders.UI
{
    /// <summary>
    /// The map generator of a new game (Foundation's "Select map"): ready-made valleys on the left, the valley
    /// seen from above in the middle (the campfire marks where the game suggests to settle), a slider for each
    /// feature on the right (lakes, rivers, islands, mountains, hills, forest, ore) and the valley's code below,
    /// to copy and share or to paste one from a friend. Confirm builds the valley and the player then chooses
    /// where the campfire burns.
    /// </summary>
    public partial class GameUi
    {
        Texture2D mapPreview;
        string mapPreviewName;
        MapDefinition mapPreviewMap;
        string mapCodeProblem;
        string lastModal = "";

        static readonly string[] FeatureNames = { "Lakes", "Rivers", "Islands", "Mountains", "Hills", "Forest", "Ore" };

        string MapKey() => store.ChoosingMap ? $"map|{store.MapPresetId}|{store.MapDraft.Code}|{mapCodeProblem}" : "";

        /// <summary>The valley the draft gives (generated once per code, shared with Confirm).</summary>
        MapDefinition DraftMap()
        {
            string name = store.DraftMapName;
            if (name != mapPreviewName || mapPreviewMap == null)
            {
                mapPreviewName = name;
                mapPreviewMap = store.BaseContent.ForMap(name).Map;
                mapPreview = MapPreview.Render(mapPreviewMap, mapPreview);
            }
            return mapPreviewMap;
        }

        VisualElement MapCard()
        {
            float width = Mathf.Min(900, SafeWidth - 24);
            var c = DialogShell("Choose Your Valley", width, out var body, out var foot);
            float inner = width - 24;
            float room = PanelH - PopupTop - BottomPad - 10 - Theme.PlankH - Theme.ButtonH - 44;
            const float leftW = 172, rightMin = 290, gap = 12;
            float side = Mathf.Floor(Mathf.Clamp(Mathf.Min(inner - leftW - rightMin - gap * 2, room), 160, 300));
            float rightW = inner - leftW - side - gap * 2;
            var map = DraftMap();

            var row = E.Div().Row(0, Align.FlexStart).Ignore();
            body.Add(row);

            // Left: the ready-made valleys.
            var left = E.Scroll(ScrollViewMode.Vertical);
            left.style.width = leftW;
            left.style.height = side + 8;
            left.style.flexShrink = 0;
            left.contentContainer.style.paddingRight = 4;
            foreach (var p in MapPreset.All)
                left.Add(PresetRow(p.Name, store.MapPresetId == p.Id, p.Id));
            if (store.MapPresetId == null) left.Add(PresetRow("Your own valley", true, null));
            row.Add(left);

            // Middle: the valley from above, the suggested campfire on it, and a new roll of the dice.
            var frame = E.Div().Skin("section").Pad(4).Margin(0, 0, 0, gap);
            frame.style.flexShrink = 0;
            var pic = E.Image(mapPreview, side, side);
            frame.Add(pic);
            var fire = map.StartBuildings.FirstOrDefault(b => b.BuildingId == "campfire");
            if (fire != null)
            {
                float k = side / map.Width;
                pic.Add(E.Icon("campfire", 24).Abs(Mathf.Round((fire.X + 0.5f) * k - 12), Mathf.Round((fire.Y + 0.5f) * k - 18)));
            }
            pic.Add(E.Square("rotate", Act(store.RerollMap), E.Kind.Blue, 36).Abs(null, null, 4, 4));
            row.Add(frame);

            // Right: the valley's name and its features.
            var right = E.Div().Col().Ignore().Margin(0, 0, 0, gap);
            right.style.width = rightW;
            right.Add(E.T(map.Title ?? "Long Valley", Theme.H2, Theme.Gold).Fit(rightW));
            right.Add(E.Ink(string.Join(" · ", map.Traits), Theme.Small, Theme.InkSoft).Wrap().Margin(0, 0, 4));
            bool classic = store.MapPresetId == MapPreset.ClassicId;
            float rowH = Mathf.Clamp(Mathf.Floor((side - 40) / MapSettings.Features.Length), 26, 34);
            foreach (var f in MapSettings.Features) right.Add(FeatureRow(f, classic, rightW, rowH));
            row.Add(right);

            if (mapCodeProblem != null) body.Add(E.T(mapCodeProblem, Theme.Small, Theme.Bad).Margin(4));

            // Foot: the code (copy / paste / type), then Cancel and Confirm.
            foot.style.justifyContent = Justify.SpaceBetween;
            var code = E.Div().Row().Ignore();
            string shown = classic ? "" : store.MapDraft.Code;
            code.Add(Field(shown, OnMapCodeTyped, false, 168, 20, "map-code"));
            code.Add(E.Square("copy", classic ? null : Act(CopyMapCode), classic ? E.Kind.Grey : E.Kind.Blue, 40).Margin(0, 0, 0, 6));
            code.Add(E.Button("Paste", Act(PasteMapCode), E.Kind.Blue, 40, null, 14, null, 92).Margin(0, 0, 0, 6));
            foot.Add(code);
            var buttons = E.Div().Row().Ignore();
            buttons.Add(E.Button("Cancel", Act(() => { mapCodeProblem = null; store.CancelMapChoice(); }), E.Kind.Blue, Theme.ButtonH, null, 16, null, 140));
            buttons.Add(E.Button("Confirm", Act(() => StartCoroutine(ConfirmMap())), E.Kind.Green, Theme.ButtonH, null, 17, null, 170).Margin(0, 0, 0, 10));
            foot.Add(buttons);
            return c;
        }

        VisualElement PresetRow(string name, bool on, string id)
        {
            var o = E.Div().Row().Skin(on ? "slot" : "section").Pad(0, 8, on ? 3 : 0, 10).Margin(0, 0, 5);
            o.style.height = 32;
            o.style.flexShrink = 0;
            o.Add(E.T(name, Theme.Body).Flex());
            if (on) o.Add(E.Icon("check", 20).NoShrink());
            if (id != null && !on) Tap(o, () => { mapCodeProblem = null; store.ChooseMapPreset(id); });
            return o;
        }

        VisualElement FeatureRow(MapSettings.Feature f, bool classic, float width, float height)
        {
            var r = E.Div().Row().Ignore();
            r.style.height = height;
            r.style.flexShrink = 0;
            string custom = "map_" + f.ToString().ToLowerInvariant();
            float sw = height - 4;
            // The player's own swatch (Custom/UI/Icons/map_lake.png…) or the painted one.
            r.Add((Theme.HasIcon(custom) ? E.Icon(custom, sw) : E.Image(MapPreview.Swatch(f), sw, sw)).NoShrink());
            r.Add(E.T(FeatureNames[(int)f], Theme.Body).Fit(84).Also(l => { l.style.width = 84; l.style.marginLeft = 6; }));
            int level = store.MapDraft[f];
            float step = height - 4;
            r.Add(E.Square("minus", level > MapSettings.Min || classic ? Act(() => { mapCodeProblem = null; store.StepMapFeature(f, -1); }) : null,
                           level > MapSettings.Min || classic ? E.Kind.Brown : E.Kind.Grey, step));
            var bar = E.Bar(out var fill, 14).Grow().Margin(0, 6, 0, 6);
            E.SetFill(fill, classic ? 0.5 : level / (double)MapSettings.Max);
            if (classic) bar.style.opacity = 0.6f;
            r.Add(bar);
            r.Add(E.Square("plus", level < MapSettings.Max || classic ? Act(() => { mapCodeProblem = null; store.StepMapFeature(f, +1); }) : null,
                           level < MapSettings.Max || classic ? E.Kind.Brown : E.Kind.Grey, step));
            return r;
        }

        void OnMapCodeTyped(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length < MapSettings.Features.Length + MapSettings.SeedDigits) return;
            if (store.UseMapCode(t)) mapCodeProblem = null;
            else mapCodeProblem = "That is not a valley code.";
            dirty = true;
        }

        void CopyMapCode()
        {
            GUIUtility.systemCopyBuffer = store.MapDraft.Code;
            store.Notify("Valley code copied.");
        }

        void PasteMapCode()
        {
            string t = GUIUtility.systemCopyBuffer;
            mapCodeProblem = store.UseMapCode(t) ? null : "The clipboard holds no valley code.";
        }

        /// <summary>Builds the chosen valley under the loading screen (terrain, water and nature take a moment).</summary>
        IEnumerator ConfirmMap()
        {
            mapCodeProblem = null;
            bool changes = store.DraftMapName != store.Game.Content.Map.Name;
            if (changes)
            {
                Loading.Show();
                Loading.Set(0.3f, "Shaping the valley…");
                yield return null;
                yield return null;
            }
            store.ConfirmMapChoice();
            dirty = true;
            if (!changes) yield break;
            Loading.Set(0.85f, "Growing the forests…");
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime) yield return null;
            Loading.Set(1f, "Choose where to light the campfire");
            yield return new WaitForSecondsRealtime(0.2f);
            Loading.Hide();
        }
    }
}
