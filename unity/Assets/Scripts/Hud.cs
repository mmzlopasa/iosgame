using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    public static class Fmt
    {
        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            int t = Math.Max(0, (int)Math.Ceiling(seconds));
            return t >= 60 ? $"{t / 60}m {t % 60}s" : $"{t}s";
        }

        public static string Amount(double value) => ((int)Math.Floor(value)).ToString(CultureInfo.InvariantCulture);

        public static string PerMinute(double perSecond) =>
            (perSecond * 60).ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture) + "/min";
    }

    /// <summary>
    /// The whole UI, drawn with IMGUI so it is created from code (no editor-made assets).
    /// Layout mirrors the SwiftUI prototype: top bar, objective chip, toasts, context card
    /// (placement / selected building), bottom navigation, panels, event and milestone dialogs.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        GameStore store;
        float u = 1f;                    // pixels per design unit
        readonly List<Rect> blockers = new List<Rect>(); // GUI-space rects drawn this frame
        bool modal;                      // a panel/dialog covers the map
        Rect panelRect;                  // GUI space
        Vector2 panelScroll;
        float panelContentHeight;
        Panel lastPanel = Panel.None;
        string chosenOption;
        float confirmResetUntil;
        bool suppressClicks;
        Vector2 touchStart;

        static readonly Color Accent = new Color(0.95f, 0.72f, 0.35f);
        static readonly Color Good = new Color(0.45f, 0.85f, 0.45f);
        static readonly Color Bad = new Color(1.0f, 0.45f, 0.4f);
        static readonly Color Muted = new Color(0.72f, 0.70f, 0.66f);
        static readonly Color Text = new Color(0.96f, 0.94f, 0.90f);

        GUIStyle sTitle, sHead, sBody, sSmall, sTiny, sBold, sButton, sButtonPrimary, sButtonFlat, sPillValue, sPillRate, sCenter;
        Texture2D texBar, texCard, texPill, texButton, texButtonDown, texPrimary, texPrimaryDown, texDim, texAccent, texGood, texBadge,
                  texToastInfo, texToastSuccess, texToastWarning, texToastMilestone, texSelected, texTrack;
        int stylesForSize = -1;

        public void Init(GameStore owner) { store = owner; }

        // MARK: - Pointer blocking (for the camera)

        /// <summary>True when a screen point (pixels, bottom-left origin) is over UI or a modal is open.</summary>
        public bool BlocksPointer(Vector2 screen)
        {
            if (modal) return true;
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            foreach (var r in blockers) if (r.Contains(gui)) return true;
            return false;
        }

        // MARK: - Panel touch scrolling

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) { touchStart = t.position; suppressClicks = false; }
                if (t.phase == TouchPhase.Moved)
                {
                    if ((t.position - touchStart).magnitude > 12f * u) suppressClicks = true;
                    var gui = new Vector2(t.position.x, Screen.height - t.position.y);
                    if (store != null && store.ActivePanel != Panel.None && panelRect.Contains(gui))
                        panelScroll.y = Mathf.Clamp(panelScroll.y + t.deltaPosition.y, 0, Mathf.Max(0, panelContentHeight - panelRect.height));
                }
            }
            else if (Input.touchCount == 0 && Input.GetMouseButtonDown(0))
            {
                suppressClicks = false;
            }
#endif
        }

        // MARK: - Styles

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(4, 4) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        int F(float size) => Mathf.Max(8, Mathf.RoundToInt(size * u));

        void EnsureStyles()
        {
            u = Mathf.Max(1f, Mathf.Min(Screen.width / 400f, Screen.height / 720f));
            int key = Mathf.RoundToInt(u * 100);
            if (key == stylesForSize) return;
            stylesForSize = key;

            if (texBar == null)
            {
                texBar = Solid(new Color(0.09f, 0.08f, 0.07f, 0.88f));
                texCard = Solid(new Color(0.13f, 0.12f, 0.11f, 0.95f));
                texPill = Solid(new Color(0f, 0f, 0f, 0.3f));
                texButton = Solid(new Color(0.24f, 0.22f, 0.20f, 1f));
                texButtonDown = Solid(new Color(0.33f, 0.30f, 0.27f, 1f));
                texPrimary = Solid(Accent);
                texPrimaryDown = Solid(Accent * 0.8f);
                texDim = Solid(new Color(0f, 0f, 0f, 0.6f));
                texAccent = Solid(Accent);
                texGood = Solid(Good);
                texBadge = Solid(Accent);
                texToastInfo = Solid(new Color(0.2f, 0.2f, 0.2f, 0.92f));
                texToastSuccess = Solid(new Color(0.16f, 0.36f, 0.2f, 0.95f));
                texToastWarning = Solid(new Color(0.5f, 0.22f, 0.14f, 0.95f));
                texToastMilestone = Solid(new Color(0.55f, 0.4f, 0.1f, 0.95f));
                texSelected = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.25f));
                texTrack = Solid(new Color(1f, 1f, 1f, 0.12f));
            }

            GUIStyle Label(int size, FontStyle fs, Color c, TextAnchor anchor = TextAnchor.UpperLeft) => new GUIStyle
            {
                fontSize = F(size), fontStyle = fs, wordWrap = true, richText = false, alignment = anchor, clipping = TextClipping.Clip,
                normal = { textColor = c },
            };

            sTitle = Label(18, FontStyle.Bold, Text);
            sHead = Label(15, FontStyle.Bold, Text);
            sBody = Label(13, FontStyle.Normal, Text);
            sSmall = Label(12, FontStyle.Normal, Muted);
            sTiny = Label(10, FontStyle.Bold, Accent);
            sBold = Label(13, FontStyle.Bold, Text);
            sCenter = Label(13, FontStyle.Normal, Text, TextAnchor.MiddleCenter);
            sPillValue = Label(14, FontStyle.Bold, Text, TextAnchor.MiddleCenter);
            sPillValue.wordWrap = false;
            sPillRate = Label(10, FontStyle.Bold, Muted, TextAnchor.MiddleCenter);
            sPillRate.wordWrap = false;

            sButton = new GUIStyle
            {
                fontSize = F(13), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                padding = new RectOffset(F(8), F(8), F(4), F(4)),
                normal = { background = texButton, textColor = Text },
                hover = { background = texButton, textColor = Text },
                active = { background = texButtonDown, textColor = Text },
            };
            sButtonPrimary = new GUIStyle(sButton)
            {
                normal = { background = texPrimary, textColor = Color.black },
                hover = { background = texPrimary, textColor = Color.black },
                active = { background = texPrimaryDown, textColor = Color.black },
            };
            sButtonFlat = new GUIStyle(sButton)
            {
                fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft,
                normal = { background = null, textColor = Text },
                hover = { background = null, textColor = Text },
                active = { background = texTrack, textColor = Text },
            };
        }

        static GUIStyle Tint(GUIStyle s, Color c)
        {
            var copy = new GUIStyle(s);
            copy.normal.textColor = c;
            return copy;
        }

        // MARK: - Small drawing helpers

        void Box(Rect r, Texture2D tex)
        {
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, tex);
        }

        bool Button(Rect r, string text, GUIStyle style, bool enabled = true)
        {
            bool old = GUI.enabled;
            GUI.enabled = old && enabled;
            var color = GUI.color;
            if (!enabled) GUI.color = new Color(1, 1, 1, 0.45f);
            bool clicked = GUI.Button(r, text, style);
            GUI.color = color;
            GUI.enabled = old;
            return clicked && enabled && !suppressClicks;
        }

        void ProgressBar(Rect r, double value, Color color)
        {
            Box(r, texTrack);
            var fill = new Rect(r.x, r.y, r.width * Mathf.Clamp01((float)value), r.height);
            var c = GUI.color;
            GUI.color = color;
            Box(fill, Texture2D.whiteTexture);
            GUI.color = c;
        }

        /// <summary>
        /// Vertical layout helper with a measure pass: when Draw is false it only accumulates
        /// height, so cards and scroll views can be sized before drawing.
        /// </summary>
        sealed class Flow
        {
            public float X, Y, W, Gap;
            public bool Draw;
            readonly Hud hud;
            public Flow(Hud hud, float x, float y, float w, bool draw) { this.hud = hud; X = x; Y = y; W = w; Draw = draw; Gap = 4 * hud.u; }

            public float Label(string text, GUIStyle style, float indent = 0)
            {
                if (string.IsNullOrEmpty(text)) return 0;
                var content = new GUIContent(text);
                float h = style.CalcHeight(content, W - indent);
                if (Draw) GUI.Label(new Rect(X + indent, Y, W - indent, h), content, style);
                Y += h + Gap;
                return h;
            }

            public bool Button(string text, GUIStyle style, float height, bool enabled = true)
            {
                var r = new Rect(X, Y, W, height * hud.u);
                Y += r.height + Gap;
                return Draw && hud.Button(r, text, style, enabled);
            }

            public void Progress(double value, Color color, float height = 6)
            {
                if (Draw) hud.ProgressBar(new Rect(X, Y, W, height * hud.u), value, color);
                Y += height * hud.u + Gap;
            }

            public Rect Reserve(float height)
            {
                var r = new Rect(X, Y, W, height);
                Y += height + Gap;
                return r;
            }

            public void Space(float units) => Y += units * hud.u;
        }

        // MARK: - OnGUI

        void OnGUI()
        {
            if (store == null || store.Game == null) return;
            EnsureStyles();
            if (Event.current.type == EventType.Layout) blockers.Clear();
            GUI.skin.settings.cursorColor = Accent;

            var safe = Screen.safeArea;
            float top = Screen.height - safe.yMax;
            float bottom = safe.yMin;
            float left = safe.xMin;
            float width = safe.width;
            var game = store.Game;

            modal = store.ActivePanel != Panel.None || store.Celebration != null
                    || (game.PendingEvent != null && !store.EventMinimized);

            float y = TopBar(left, top, width);
            y = ObjectiveChip(left, y, width);
            Toasts(left, y, width);

            float navTop = BottomNav(left, bottom, width);
            float above = navTop - 8 * u;
            if (game.PendingEvent != null && store.EventMinimized) above = EventBadge(left, above, width);
            if (store.PlacingBuildingId != null) PlacementBar(left, above, width);
            else if (store.SelectedBuildingId != null) BuildingCard(left, above, width);

            if (store.ActivePanel != Panel.None) PanelSheet(left, top, width, navTop);
            else if (game.PendingEvent != null && !store.EventMinimized) EventDialog(left, top, width, bottom);
            if (store.Celebration != null) MilestoneOverlay(left, width);
        }

        void Block(Rect r)
        {
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) blockers.Add(r);
        }

        // MARK: - Top bar

        float TopBar(float left, float top, float width)
        {
            var game = store.Game;
            float pad = 12 * u;
            float h = top + 98 * u;
            var bar = new Rect(0, 0, Screen.width, h);
            Box(bar, texBar);
            Block(bar);

            int epochIndex = game.Content.Epoch(game.State.EpochId)?.Index ?? 1;
            GUI.Label(new Rect(left + pad, top + 6 * u, width - 70 * u, 16 * u), $"EPOCH {epochIndex} · {game.EpochName.ToUpperInvariant()}", sTiny);
            GUI.Label(new Rect(left + pad, top + 20 * u, width - 70 * u, 26 * u), $"{game.StageName} — {game.State.SettlementName}", sTitle);
            if (Button(new Rect(left + width - 52 * u, top + 6 * u, 44 * u, 40 * u), "Dev", sButton))
                store.ActivePanel = Panel.Settings;

            var economy = game.Economy();
            var caps = game.StorageCapacity();
            float pillsY = top + 52 * u;
            float cellW = (width - pad * 2) / 5f;
            int i = 0;
            foreach (var type in ResourceTypes.All)
            {
                var r = new Rect(left + pad + i * cellW, pillsY, cellW - 4 * u, 40 * u);
                double value = game.State.Resources[type];
                double rate = economy.Net[type];
                bool nearCap = value >= caps[type] - 0.5;
                Pill(r, $"{Fmt.Amount(value)}", $"{type.DisplayName()} {(Math.Abs(rate) < 0.0005 ? "" : Fmt.PerMinute(rate))}",
                     nearCap ? Accent : Text, rate < -0.0005 ? Bad : (rate > 0.0005 ? Good : Muted));
                i++;
            }
            var pr = new Rect(left + pad + 4 * cellW, pillsY, cellW - 4 * u, 40 * u);
            string sub = game.State.FoodShortage ? "HUNGRY" : (game.IdleWorkers > 0 ? $"{game.IdleWorkers} idle" : "all busy");
            Pill(pr, $"{game.State.Population}/{game.HousingCapacity()}", $"People · {sub}", Text,
                 game.State.FoodShortage ? Bad : (game.IdleWorkers > 0 ? Accent : Muted));
            return h + 6 * u;
        }

        void Pill(Rect r, string value, string caption, Color valueColor, Color captionColor)
        {
            Box(r, texPill);
            GUI.Label(new Rect(r.x, r.y + 2 * u, r.width, r.height * 0.55f), value, Tint(sPillValue, valueColor));
            GUI.Label(new Rect(r.x, r.y + r.height * 0.52f, r.width, r.height * 0.45f), caption, Tint(sPillRate, captionColor));
        }

        // MARK: - Objective chip & toasts

        float ObjectiveChip(float left, float y, float width)
        {
            var game = store.Game;
            var objective = game.CurrentObjective;
            if (objective == null) return y;
            string first = objective.Conditions.Count > 0 ? game.Describe(objective.Conditions[0]) : "";
            var r = new Rect(left + 12 * u, y, width - 24 * u, 44 * u);
            Box(r, texCard);
            Block(r);
            GUI.Label(new Rect(r.x + 10 * u, r.y + 4 * u, r.width - 30 * u, 20 * u), "Goal: " + objective.Title, Tint(sBold, Text));
            GUI.Label(new Rect(r.x + 10 * u, r.y + 23 * u, r.width - 30 * u, 18 * u), first, sSmall);
            GUI.Label(new Rect(r.xMax - 22 * u, r.y, 16 * u, r.height), ">", Tint(sCenter, Muted));
            if (Button(r, "", GUIStyle.none)) store.ActivePanel = Panel.Objectives;
            return r.yMax + 6 * u;
        }

        void Toasts(float left, float y, float width)
        {
            foreach (var toast in store.Toasts)
            {
                var style = Tint(sCenter, Text);
                float w = width - 32 * u;
                float h = style.CalcHeight(new GUIContent(toast.Notice.Message), w - 16 * u) + 14 * u;
                var r = new Rect(left + 16 * u, y, w, h);
                Texture2D tex;
                switch (toast.Notice.Kind)
                {
                    case NoticeKind.Success: tex = texToastSuccess; break;
                    case NoticeKind.Warning: tex = texToastWarning; break;
                    case NoticeKind.Milestone: tex = texToastMilestone; break;
                    default: tex = texToastInfo; break;
                }
                Box(r, tex);
                GUI.Label(new Rect(r.x + 8 * u, r.y, r.width - 16 * u, r.height), toast.Notice.Message, style);
                y += h + 4 * u;
            }
        }

        // MARK: - Bottom navigation

        float BottomNav(float left, float bottom, float width)
        {
            var game = store.Game;
            float h = 58 * u;
            var bar = new Rect(0, Screen.height - bottom - h, Screen.width, h + bottom);
            Box(bar, texBar);
            Block(bar);

            bool researchIdle = game.State.ActiveResearch == null && game.Content.Technologies.Any(t =>
                game.GetResearchStatus(t.Id).State == ResearchState.Available && game.State.Resources.Covers(t.Cost));
            bool milestoneReady = game.Content.Milestones.Any(m => game.CanClaimMilestone(m.Id));
            var items = new (string title, Panel panel, bool badge)[]
            {
                ("Build", Panel.Build, false),
                ("People", Panel.People, game.IdleWorkers > 0),
                ("Research", Panel.Research, researchIdle),
                ("Goals", Panel.Objectives, milestoneReady),
                ("Map", Panel.Map, false),
            };
            float w = (width - 8 * u) / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var r = new Rect(left + 4 * u + i * w + 3 * u, bar.y + 6 * u, w - 6 * u, h - 12 * u);
                bool active = store.ActivePanel == items[i].panel;
                if (Button(r, items[i].title, active ? sButtonPrimary : sButton))
                    store.ActivePanel = active ? Panel.None : items[i].panel;
                if (items[i].badge) Box(new Rect(r.xMax - 12 * u, r.y + 4 * u, 8 * u, 8 * u), texBadge);
            }
            return bar.y;
        }

        // MARK: - Context cards

        float EventBadge(float left, float bottomY, float width)
        {
            var ev = store.Game.PendingEvent;
            var r = new Rect(left + 40 * u, bottomY - 40 * u, width - 80 * u, 38 * u);
            Block(r);
            if (Button(r, $"Decision waiting: {ev.Title}", sButtonPrimary)) store.EventMinimized = false;
            return r.y - 8 * u;
        }

        void Card(float left, float bottomY, float width, Action<Flow> content)
        {
            float x = left + 12 * u, w = width - 24 * u, pad = 14 * u;
            var measure = new Flow(this, x + pad, 0, w - pad * 2, false);
            content(measure);
            float h = measure.Y + pad * 2;
            var r = new Rect(x, bottomY - h, w, h);
            Box(r, texCard);
            Block(r);
            content(new Flow(this, x + pad, r.y + pad, w - pad * 2, true));
        }

        void PlacementBar(float left, float bottomY, float width)
        {
            var game = store.Game;
            var def = game.Content.Building(store.PlacingBuildingId);
            if (def == null) return;
            var issues = store.PlacementIssues();
            Card(left, bottomY, width, f =>
            {
                f.Label($"{def.Name}   {def.Cost.ShortDescription} · {Fmt.Duration(game.EstimatedSeconds(def.BuildSeconds))}", sHead);
                if (issues.Count > 0) f.Label(issues[0].Message, Tint(sBold, Bad));
                else f.Label("Valid spot. Tap the ground to move it, then confirm.", Tint(sBold, Good));
                var row = f.Reserve(44 * u);
                if (f.Draw)
                {
                    float half = (row.width - 10 * u) / 2f;
                    if (Button(new Rect(row.x, row.y, half, row.height), "Cancel", sButton)) store.CancelPlacement();
                    if (Button(new Rect(row.x + half + 10 * u, row.y, half, row.height), "Build here", sButtonPrimary, issues.Count == 0))
                        store.ConfirmPlacement();
                }
            });
        }

        void BuildingCard(float left, float bottomY, float width)
        {
            var game = store.Game;
            var b = game.State.Building(store.SelectedBuildingId.Value);
            var def = b == null ? null : game.Content.Building(b.DefinitionId);
            if (def == null) { store.SelectedBuildingId = null; return; }
            Card(left, bottomY, width, f =>
            {
                var head = f.Reserve(26 * u);
                if (f.Draw)
                {
                    GUI.Label(new Rect(head.x, head.y, head.width - 40 * u, head.height), def.Name, sTitle);
                    if (Button(new Rect(head.xMax - 36 * u, head.y - 4 * u, 36 * u, 32 * u), "X", sButton)) store.SelectedBuildingId = null;
                }
                f.Label(def.Description, sSmall);
                if (b.Status != BuildingStatus.Complete)
                {
                    string label = b.Status == BuildingStatus.Upgrading ? "Upgrading" : "Under construction";
                    f.Label($"{label} · {Fmt.Duration(game.EstimatedSeconds(b.RemainingWork))} left", sBody);
                    f.Progress(b.Progress, Accent);
                }
                if (def.HousingValue > 0) f.Label($"Houses {def.HousingValue} people", sSmall);
                if (def.StorageValue > 0) f.Label($"+{(int)def.StorageValue} storage", sSmall);
                if (def.ProtectedFoodValue > 0) f.Label($"Protects {(int)def.ProtectedFoodValue} food from spoiling", sSmall);
                foreach (var slot in def.JobSlots ?? new List<JobSlot>()) JobRow(f, slot.Job, true);

                var target = def.UpgradesTo == null ? null : game.Content.Building(def.UpgradesTo);
                if (target != null && b.Status == BuildingStatus.Complete)
                {
                    bool locked = !game.IsUnlocked(target);
                    string detail = locked
                        ? $"Requires {game.Content.Technology(target.RequiresTech ?? "")?.Name ?? "research"}"
                        : $"{target.Cost.ShortDescription} · {Fmt.Duration(game.EstimatedSeconds(target.BuildSeconds))} · houses {target.HousingValue}";
                    if (f.Button($"Upgrade to {target.Name}\n{detail}", sButtonPrimary, 48, !locked && game.State.Resources.Covers(target.Cost)))
                        store.Upgrade(b.Id);
                }
                var next = def.SuccessorId == null ? null : game.Content.Building(def.SuccessorId);
                if (next != null) f.Label($"Will become the {next.Name} when the settlement grows.", sSmall);
                if (b.History.Count > 1)
                    f.Label("History: " + string.Join(" → ", b.History.Select(h => game.Content.Building(h)?.Name ?? h)), sSmall);
            });
        }

        /// <summary>Worker count for a job with −/+ buttons and the effect on production.</summary>
        void JobRow(Flow f, JobType job, bool compact)
        {
            var game = store.Game;
            var config = game.Content.JobConfig(job);
            int workers = game.State.Assignments[job];
            int capacity = game.JobCapacity(job);
            bool unlimited = config?.UnlimitedSlots ?? false;
            var row = f.Reserve(46 * u);
            if (!f.Draw) return;
            float bw = 40 * u;
            var textRect = new Rect(row.x, row.y, row.width - bw * 2 - 56 * u, row.height);
            GUI.Label(new Rect(textRect.x, textRect.y + 2 * u, textRect.width, 20 * u), config?.Name ?? job.Id(), compact ? sBold : sHead);
            GUI.Label(new Rect(textRect.x, textRect.y + 22 * u, textRect.width, 22 * u), EffectText(job, workers), sSmall);
            float x = row.xMax - bw * 2 - 50 * u;
            if (Button(new Rect(x, row.y + 4 * u, bw, 38 * u), "−", sButton, workers > 0)) store.SetWorkers(job, workers - 1);
            GUI.Label(new Rect(x + bw, row.y, 50 * u, row.height), unlimited ? $"{workers}" : $"{workers}/{capacity}", Tint(sCenter, Text));
            if (Button(new Rect(x + bw + 50 * u, row.y + 4 * u, bw, 38 * u), "+", sButton, game.IdleWorkers > 0 && workers < capacity))
                store.SetWorkers(job, workers + 1);
        }

        string EffectText(JobType job, int workers)
        {
            var game = store.Game;
            var config = game.Content.JobConfig(job);
            if (config == null) return "";
            if (config.Output.HasValue)
            {
                double each = game.RatePerWorker(job) * 60;
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}/min each · total {2:0.0}/min",
                                     config.Output.Value.DisplayName(), each, each * workers);
            }
            switch (job)
            {
                case JobType.Construction:
                    return string.Format(CultureInfo.InvariantCulture, "Build speed ×{0:0.0}", game.ConstructionSpeed());
                case JobType.Crafting:
                    var status = game.Economy();
                    if (workers == 0) return "Tools boost all gathering";
                    return status.CraftingActive
                        ? string.Format(CultureInfo.InvariantCulture, "Gathering +{0:0}% · uses wood & stone", status.CraftingBonus * 100)
                        : "Needs wood and stone to work";
                default:
                    return config.Description;
            }
        }

        // MARK: - Panels

        void PanelSheet(float left, float top, float width, float navTop)
        {
            if (store.ActivePanel != lastPanel) { panelScroll = Vector2.zero; lastPanel = store.ActivePanel; }
            float y0 = top + 104 * u;
            var sheet = new Rect(0, y0, Screen.width, navTop - y0);
            Box(sheet, texCard);
            Block(sheet);

            string title;
            switch (store.ActivePanel)
            {
                case Panel.Build: title = "Build"; break;
                case Panel.People: title = "People & Jobs"; break;
                case Panel.Research: title = "Research"; break;
                case Panel.Objectives: title = "Objectives"; break;
                case Panel.Map: title = "Map & Legacy"; break;
                default: title = "Developer"; break;
            }
            float pad = 14 * u;
            GUI.Label(new Rect(left + pad, y0 + 10 * u, width - 120 * u, 28 * u), title, sTitle);
            if (Button(new Rect(left + width - 86 * u, y0 + 8 * u, 74 * u, 34 * u), "Done", sButtonPrimary)) store.ActivePanel = Panel.None;

            panelRect = new Rect(left, y0 + 48 * u, width, sheet.yMax - (y0 + 48 * u));
            float innerW = width - pad * 2;
            Action<Flow> content;
            switch (store.ActivePanel)
            {
                case Panel.Build: content = BuildPanel; break;
                case Panel.People: content = PeoplePanel; break;
                case Panel.Research: content = ResearchPanel; break;
                case Panel.Objectives: content = ObjectivesPanel; break;
                case Panel.Map: content = MapPanel; break;
                default: content = DevPanel; break;
            }
            var measure = new Flow(this, pad, 0, innerW, false);
            content(measure);
            panelContentHeight = measure.Y + 24 * u;
            panelScroll.y = Mathf.Clamp(panelScroll.y, 0, Mathf.Max(0, panelContentHeight - panelRect.height));

            panelScroll = GUI.BeginScrollView(panelRect, panelScroll, new Rect(0, 0, width - 1, panelContentHeight), false, false,
                                              GUIStyle.none, GUIStyle.none);
            content(new Flow(this, pad, 0, innerW, true));
            GUI.EndScrollView();
        }

        void Section(Flow f, string title)
        {
            f.Space(8);
            f.Label(title.ToUpperInvariant(), sTiny);
        }

        void BuildPanel(Flow f)
        {
            var game = store.Game;
            foreach (var def in game.Content.Buildings.Where(b => b.Buildable))
            {
                bool affordable = game.State.Resources.Covers(def.Cost);
                bool unlocked = game.IsUnlocked(def);
                bool limitReached = def.MaxCount.HasValue && game.Count(def.Id, true) >= def.MaxCount.Value;
                string size = def.Footprint.Width > 1 ? $" · {def.Footprint.Width}×{def.Footprint.Height}" : "";
                string status = !unlocked
                    ? $"\nRequires {game.Content.Technology(def.RequiresTech ?? "")?.Name ?? "research"}"
                    : limitReached ? "\nLimit reached" : "";
                string text = $"{def.Name}\n{def.Description}\n{def.Cost.ShortDescription} · {Fmt.Duration(game.EstimatedSeconds(def.BuildSeconds))}{size}"
                              + (affordable ? "" : "  (not enough yet)") + status;
                float h = sButtonFlat.CalcHeight(new GUIContent(text), f.W) / u + 8;
                var r = new Rect(f.X, f.Y, f.W, h * u);
                if (f.Draw) Box(r, texPill);
                if (f.Button(text, Tint(sButtonFlat, affordable ? Text : Muted), h, unlocked && !limitReached))
                    store.BeginPlacement(def.Id);
            }
            f.Label("Tap a building, then tap the ground to place it. Resources are only spent when you confirm.", sSmall);
        }

        void PeoplePanel(Flow f)
        {
            var game = store.Game;
            var economy = game.Economy();
            bool growthBlocked = game.State.Population >= game.HousingCapacity();
            f.Label($"Population {game.State.Population} / {game.HousingCapacity()} housed · Idle {game.IdleWorkers}", sHead);
            f.Label($"Food balance {Fmt.PerMinute(economy.Net.Food)}", sBody);
            if (game.State.FoodShortage) f.Label("Food has run out. All work is slower until food is restored.", Tint(sBold, Bad));
            else if (growthBlocked) f.Label("Build or upgrade shelters so the community can grow.", Tint(sBold, Accent));
            else
            {
                f.Label("Next newcomer", sSmall);
                f.Progress(game.State.GrowthProgress, Good);
            }
            Section(f, "Jobs");
            foreach (var job in Enums.Jobs) JobRow(f, job, false);
            f.Label("Workplaces set how many people a job can take. Changes apply immediately.", sSmall);
            Section(f, "Consumption");
            f.Label($"Eating {Fmt.PerMinute(-game.State.Population * game.Content.Balance.FoodPerPersonPerSecond)}", sBody);
            f.Label($"Food spoiling {Fmt.PerMinute(-economy.Spoilage)}", sBody);
        }

        void ResearchPanel(Flow f)
        {
            var game = store.Game;
            var economy = game.Economy();
            f.Label($"Knowledge {Fmt.Amount(game.State.Resources.Knowledge)} ({Fmt.PerMinute(economy.Net.Knowledge)})", sHead);
            if (economy.Production.Knowledge <= 0)
                f.Label("Assign people to Research (People tab) to produce knowledge.", Tint(sSmall, Accent));
            Section(f, "Technologies");
            foreach (var tech in game.Content.Technologies)
            {
                var status = game.GetResearchStatus(tech.Id);
                string head = tech.Name + (status.State == ResearchState.Researched ? "   (done)" : status.State == ResearchState.InProgress ? "   (researching…)" : "");
                f.Label(head, Tint(sHead, status.State == ResearchState.Researched ? Good : Text));
                f.Label(tech.Description, sSmall);
                switch (status.State)
                {
                    case ResearchState.InProgress:
                        f.Label($"{Fmt.Duration(game.State.ActiveResearch?.RemainingSeconds ?? 0)} left", sSmall);
                        f.Progress(status.Progress, Accent);
                        break;
                    case ResearchState.Blocked:
                        f.Label("Requires: " + string.Join(", ", status.Missing), Tint(sBold, Bad));
                        break;
                    case ResearchState.Available:
                        bool affordable = game.State.Resources.Covers(tech.Cost);
                        bool busy = game.State.ActiveResearch != null;
                        if (f.Button($"Research · {tech.Cost.ShortDescription} · {Fmt.Duration(tech.ResearchSeconds)}", sButtonPrimary, 40, affordable && !busy))
                            store.StartResearch(tech.Id);
                        if (busy) f.Label("One project at a time.", sSmall);
                        break;
                }
                f.Space(6);
            }
        }

        void ObjectivesPanel(Flow f)
        {
            var game = store.Game;
            foreach (var ms in game.Content.Milestones)
            {
                Section(f, "Milestone");
                f.Label(ms.Name, sHead);
                if (game.State.ReachedMilestones.Contains(ms.Id))
                {
                    f.Label("Reached — your camp is now a permanent village.", Tint(sBold, Accent));
                    continue;
                }
                foreach (var req in game.MilestoneRequirements(ms.Id))
                    f.Label((req.Met ? "+  " : "·  ") + req.Text, Tint(sBody, req.Met ? Good : Muted));
                if (f.Button("Found the village", sButtonPrimary, 46, game.CanClaimMilestone(ms.Id)))
                {
                    store.ClaimMilestone(ms.Id);
                    store.ActivePanel = Panel.None;
                }
            }
            Section(f, "First steps");
            var current = game.CurrentObjective;
            foreach (var obj in game.Content.Objectives)
            {
                bool done = game.State.CompletedObjectives.Contains(obj.Id);
                bool isCurrent = current != null && current.Id == obj.Id;
                string mark = done ? "+  " : isCurrent ? ">  " : "·  ";
                f.Label(mark + obj.Title, Tint(isCurrent ? sHead : sBody, done ? Good : isCurrent ? Accent : Text));
                if (isCurrent)
                {
                    f.Label(obj.Hint, sSmall, 18 * u);
                    foreach (var req in game.Status(obj.Conditions))
                        f.Label((req.Met ? "+ " : "· ") + req.Text, Tint(sSmall, req.Met ? Good : Text), 18 * u);
                }
                if (obj.Reward.HasValue && !done) f.Label("Reward: " + obj.Reward.Value.ShortDescription, sSmall, 18 * u);
            }
        }

        void MapPanel(Flow f)
        {
            var game = store.Game;
            f.Label(game.State.SettlementName, sHead);
            f.Label("Geography: " + string.Join(", ", game.Content.Map.Traits), sSmall);
            Section(f, "Explore");
            var active = game.State.ActiveExpedition;
            foreach (var region in game.Content.Regions.Where(r => !r.RevealedAtStart))
            {
                bool explored = game.State.ExploredRegions.Contains(region.Id);
                f.Label(region.Name + (explored ? "   (explored)" : ""), Tint(sHead, explored ? Good : Text));
                f.Label(region.Description, sSmall);
                if (active != null && active.RegionId == region.Id)
                {
                    f.Label($"Scouts away · {Fmt.Duration(active.RemainingSeconds)}", sSmall);
                    f.Progress(1 - active.RemainingSeconds / Math.Max(1, active.TotalSeconds), Accent);
                }
                else if (!explored)
                {
                    if (f.Button($"Send scouts · {region.Cost.ShortDescription} · {Fmt.Duration(region.ExpeditionSeconds)}", sButton, 40,
                                 active == null && game.State.Resources.Covers(region.Cost)))
                        store.Explore(region.Id);
                }
                f.Space(4);
            }
            f.Label("Scouts reveal new land for building and may discover opportunities.", sSmall);
            Section(f, "Legacy Timeline");
            foreach (var entry in Enumerable.Reverse(game.State.Legacy))
            {
                f.Label($"{entry.Title}   ({Fmt.Duration(entry.SimTime)})", sBold);
                if (!string.IsNullOrEmpty(entry.Detail)) f.Label(entry.Detail, sSmall);
                f.Space(4);
            }
        }

        void DevPanel(Flow f)
        {
            var game = store.Game;
            Section(f, "Diagnostics");
            f.Label($"App version {Application.version} · Unity {Application.unityVersion}", sBody);
            f.Label($"Content version {game.Content.ContentVersion} · Save version {SaveCodec.CurrentVersion}", sBody);
            f.Label($"Game time {Fmt.Duration(game.State.SimTime)} · Buildings {game.State.Buildings.Count}", sBody);
            f.Label(store.SaveStatus, sBody);
            f.Label(store.SaveStore.FilePath, sSmall);
            f.Label($"Settlement {game.State.SettlementId}", sSmall);
            Section(f, "Testing tools");
            if (f.Button("Save now", sButton, 40)) store.Save();
            if (f.Button("Fast-forward 1 minute", sButton, 40)) store.DebugFastForward(1);
            if (f.Button("Fast-forward 10 minutes", sButton, 40)) store.DebugFastForward(10);
            if (f.Button("Grant +50 of every resource", sButton, 40)) store.DebugGrant();
            f.Space(8);
            bool armed = Time.unscaledTime < confirmResetUntil;
            if (f.Button(armed ? "Tap again to erase this settlement" : "Reset prototype", armed ? sButtonPrimary : sButton, 44))
            {
                if (armed) { confirmResetUntil = 0; store.ResetPrototype(); }
                else confirmResetUntil = Time.unscaledTime + 4f;
            }
            f.Label($"{game.Content.Branding.Title} — {game.Content.Branding.Subtitle}. Prototype build with placeholder art.", sSmall);
        }

        // MARK: - Event dialog

        void EventDialog(float left, float top, float width, float bottom)
        {
            var game = store.Game;
            var ev = game.PendingEvent;
            Box(new Rect(0, 0, Screen.width, Screen.height), texDim);
            Card(left, Screen.height - bottom - 70 * u, width, f =>
            {
                f.Label("LOCAL EVENT", sTiny);
                f.Label(ev.Title, sTitle);
                f.Label(ev.Text, sSmall);
                foreach (var option in ev.Options)
                {
                    var blocker = game.OptionBlocker(option);
                    string cost = option.Cost.HasValue && !option.Cost.Value.IsZero ? $"   ({option.Cost.Value.ShortDescription})" : "";
                    string text = $"{option.Label}{cost}\n{option.TradeOff}" + (blocker != null ? $"\n{blocker}" : "");
                    float h = sButtonFlat.CalcHeight(new GUIContent(text), f.W) / u + 8;
                    var r = new Rect(f.X, f.Y, f.W, h * u);
                    if (f.Draw) Box(r, chosenOption == option.Id ? texSelected : texPill);
                    if (f.Button(text, Tint(sButtonFlat, blocker != null ? Muted : Text), h, blocker == null)) chosenOption = option.Id;
                }
                var row = f.Reserve(44 * u);
                if (f.Draw)
                {
                    float half = (row.width - 10 * u) / 2f;
                    if (Button(new Rect(row.x, row.y, half, row.height), "Decide later", sButton)) store.EventMinimized = true;
                    if (Button(new Rect(row.x + half + 10 * u, row.y, half, row.height), "Confirm choice", sButtonPrimary, chosenOption != null))
                    {
                        store.ResolveEvent(chosenOption);
                        chosenOption = null;
                    }
                }
            });
        }

        // MARK: - Milestone

        void MilestoneOverlay(float left, float width)
        {
            Box(new Rect(0, 0, Screen.width, Screen.height), texDim);
            Card(left, Screen.height * 0.7f, width, f =>
            {
                f.Label("MILESTONE", Tint(sTiny, Accent));
                f.Label("Temporary Camp → Stable Village", sTitle);
                f.Label(store.Celebration, sBody);
                f.Label("Recorded in your Legacy Timeline.", sSmall);
                if (f.Button("Continue", sButtonPrimary, 44)) store.Celebration = null;
            });
        }
    }
}
