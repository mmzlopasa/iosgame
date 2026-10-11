using System;
using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    public enum Panel { None, Build, People, Research, Objectives, Map, Profile, Settings }

    /// <summary>What happened while the player was away (shown once on launch).</summary>
    public sealed class OfflineReport
    {
        public double Seconds;
        public ResourceBundle Gained;
        public readonly List<string> Events = new List<string>();
    }

    public sealed class Toast
    {
        public GameNotice Notice;
        public float CreatedAt;
    }

    /// <summary>
    /// Presentation-side owner of the running game: drives the clock, saves, turns notices
    /// into toasts and forwards UI commands to <see cref="Game"/>. It holds no game rules.
    /// </summary>
    public class GameStore : MonoBehaviour
    {
        public GameContent Content { get; private set; }
        public Game Game { get; private set; }
        public FileSaveStore SaveStore { get; private set; }
        public WorldView World { get; private set; }
        public CameraRig Rig { get; private set; }

        public Panel ActivePanel = Panel.None;
        public int? SelectedBuildingId;
        public string PlacingBuildingId { get; private set; }
        public (int x, int y)? GhostCell { get; private set; }
        /// <summary>Quarter turns of the building being placed.</summary>
        public int PlacementRotation { get; private set; }
        /// <summary>Decoration being laid out (tap or drag on the ground), or "remove" to clear decorations.</summary>
        public string DecoratingId { get; private set; }
        public const string RemoveTool = "remove";
        /// <summary>When true a drag paints decorations (roads, fences) instead of moving the camera.</summary>
        public bool PaintMode { get; private set; }
        public int DecorationRotation { get; private set; }
        /// <summary>The player hid the HUD to look at the settlement.</summary>
        public bool HudHidden;
        (int, int)? lastPainted;
        public readonly List<Toast> Toasts = new List<Toast>();
        public bool EventMinimized;
        public string Celebration;
        public OfflineReport Offline;
        public string SaveStatus { get; private set; } = "Not saved yet";

        readonly SystemClock clock = new SystemClock();
        float nextStep;
        double lastSaveWallTime;

        /// <summary>True in capture mode: a throwaway game that never reads or writes the player's save.</summary>
        public bool Ephemeral { get; private set; }

        public void Init(CameraRig rig, WorldView world, bool ephemeral = false)
        {
            Ephemeral = ephemeral;
            Rig = rig;
            World = world;

            var asset = Resources.Load<TextAsset>("content");
            if (asset == null) throw new InvalidOperationException("FOUNDERS content.json is missing from Resources.");
            Content = GameContent.Load(asset.text);
            SaveStore = new FileSaveStore(Application.persistentDataPath);

            string message = null;
            var outcome = ephemeral ? LoadOutcome.NoSave : SaveStore.Load(Content);
            switch (outcome.Result)
            {
                case LoadResult.Loaded:
                    Game = new Game(Content, outcome.State, clock);
                    break;
                case LoadResult.Corrupt:
                    Debug.LogWarning($"[FOUNDERS] Corrupt save moved aside: {outcome.Reason}");
                    message = "Your previous save could not be read, so a new camp was started. A backup was kept.";
                    Game = Game.NewGame(Content, clock);
                    break;
                case LoadResult.UnsupportedVersion:
                    Debug.LogWarning($"[FOUNDERS] Save version {outcome.Version} is newer than this build supports.");
                    message = $"Your save is from a newer version of FOUNDERS (v{outcome.Version}). Started a temporary new camp; the save was not changed.";
                    Game = Game.NewGame(Content, clock);
                    break;
                default:
                    Game = Game.NewGame(Content, clock);
                    break;
            }

            // Simulate the time spent away since the last save (capped, never duplicated).
            var before = Game.State.Clone();
            Game.Update();
            if (!ephemeral && outcome.Result == LoadResult.Loaded) Offline = Report(before, Game.State);
            if (message != null) AddToast(new GameNotice(NoticeKind.Warning, message));
            AbsorbNotices();
            World.Init(this);
            Rig.Bounds = MapBounds();
            // Through the world, not a captured terrain: RebuildAll replaces the terrain.
            Rig.HeightAt = p => World.Ground != null && World.Ground.Terrain != null ? World.Ground.HeightAt(p) : 0f;
            FocusOnSettlement(true);
            Save();
        }

        OfflineReport Report(GameState before, GameState after)
        {
            double seconds = after.SimTime - before.SimTime;
            if (seconds < 90) return null;
            var r = new OfflineReport { Seconds = seconds, Gained = after.Resources - before.Resources };
            foreach (var b in after.Buildings)
            {
                var old = before.Building(b.Id);
                if (b.Status == BuildingStatus.Complete && (old == null || old.Status != BuildingStatus.Complete))
                    r.Events.Add($"{Content.Building(b.DefinitionId)?.Name ?? b.DefinitionId} finished");
            }
            foreach (var t in after.Researched.Except(before.Researched))
                r.Events.Add($"Research complete: {Content.Technology(t)?.Name ?? t}");
            int born = after.Population - before.Population;
            if (born > 0) r.Events.Add(born == 1 ? "A newcomer joined the camp" : $"{born} newcomers joined the camp");
            foreach (var e in after.ExploredRegions.Except(before.ExploredRegions))
                r.Events.Add($"Scouts returned from {Content.Regions.FirstOrDefault(x => x.Id == e)?.Name ?? e}");
            return r;
        }

        /// <summary>Selects a building and moves the camera to it.</summary>
        public void FocusBuilding(int id)
        {
            var b = Game.State.Building(id);
            if (b == null) return;
            ActivePanel = Panel.None;
            PlacingBuildingId = null;
            GhostCell = null;
            SelectedBuildingId = id;
            Rig.SetFocus(Grid.CellCenter(b.X, b.Y));
            Refresh();
        }

        Rect MapBounds()
        {
            var map = Content.Map;
            float s = Grid.CellSize;
            return new Rect(-s, -(map.Height) * s, (map.Width + 1) * s, (map.Height + 1) * s);
        }

        void FocusOnSettlement(bool instant)
        {
            var first = Game.State.Buildings.Count > 0 ? Game.State.Buildings[0] : null;
            var center = first != null
                ? Grid.CellCenter(first.X, first.Y)
                : Grid.CellCenter(Content.Map.Width / 2f, Content.Map.Height / 2f);
            Rig.SetFocus(center, instant, 30f);
        }

        // MARK: - Clock

        void Update()
        {
            if (Game == null || Time.unscaledTime < nextStep) return;
            nextStep = Time.unscaledTime + 0.25f;
            Game.Update();
            Refresh();
            if (!Ephemeral && clock.Now() - lastSaveWallTime > 15) Save();
        }

        /// <summary>Pulls notices, expires toasts, redraws.</summary>
        public void Refresh()
        {
            AbsorbNotices();
            float now = Time.unscaledTime;
            Toasts.RemoveAll(t => now - t.CreatedAt > 4f);
            World.Sync();
        }

        void AddToast(GameNotice notice)
        {
            Toasts.Add(new Toast { Notice = notice, CreatedAt = Time.unscaledTime });
            if (Toasts.Count > 3) Toasts.RemoveRange(0, Toasts.Count - 3);
        }

        void AbsorbNotices()
        {
            foreach (var n in Game.DrainNotices())
            {
                AddToast(n);
                if (n.Kind == NoticeKind.Milestone)
                {
                    Celebration = n.Message;
                    Vibrate();
                }
                if (n.Message.StartsWith("A decision awaits", StringComparison.Ordinal)) EventMinimized = false;
            }
        }

        static void Vibrate()
        {
#if UNITY_IOS || UNITY_ANDROID
            if (UI.Prefs.Haptics) Handheld.Vibrate();
#endif
        }

        void OnApplicationPause(bool paused)
        {
            if (Game == null) return;
            Game.Update();
            if (paused) Save();
            else Refresh();
        }

        void OnApplicationQuit()
        {
            if (Game == null) return;
            Game.Update();
            Save();
        }

        // MARK: - Saving

        public void Save()
        {
            if (Ephemeral) return;
            try
            {
                SaveStore.Save(Game.State, Content, clock.Now());
                lastSaveWallTime = clock.Now();
                SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception e)
            {
                SaveStatus = $"Save failed: {e.Message}";
                Debug.LogError($"[FOUNDERS] Save failed: {e}");
            }
        }

        public void ResetPrototype()
        {
            try { SaveStore.Reset(); }
            catch (Exception e) { Debug.LogError($"[FOUNDERS] Reset failed: {e}"); }
            Game = Game.NewGame(Content, clock);
            PlacingBuildingId = null;
            GhostCell = null;
            SelectedBuildingId = null;
            Celebration = null;
            Offline = null;
            Toasts.Clear();
            ActivePanel = Panel.None;
            World.RebuildAll();
            FocusOnSettlement(false);
            Save();
            Refresh();
        }

        // MARK: - Commands

        /// <summary>Runs a game command; a failure becomes a warning toast. Returns true on success.</summary>
        bool Perform(GameError error)
        {
            if (error != null) AddToast(new GameNotice(NoticeKind.Warning, error.Message));
            Refresh();
            Save();
            return error == null;
        }

        public void SetWorkers(JobType job, int count) => Perform(Game.SetWorkers(job, count).Error);
        public void StartResearch(string id) => Perform(Game.StartResearch(id).Error);
        public void Upgrade(int buildingId) => Perform(Game.UpgradeBuilding(buildingId).Error);
        public void UpgradeLevel(int buildingId) => Perform(Game.UpgradeLevel(buildingId).Error);

        public void Demolish(int buildingId)
        {
            if (Perform(Game.DemolishBuilding(buildingId).Error)) SelectedBuildingId = null;
            Refresh();
        }
        public void ResolveEvent(string optionId) => Perform(Game.ResolveEvent(optionId).Error);
        public void Explore(string regionId) => Perform(Game.StartExpedition(regionId).Error);
        public void ClaimMilestone(string id) => Perform(Game.ClaimMilestone(id).Error);

        public void DismissStory()
        {
            Game.DismissStory();
            Save();
            Refresh();
        }

        public void DebugFastForward(double minutes)
        {
            Game.DebugFastForward(minutes * 60);
            Refresh();
            Save();
        }

        public void DebugGrant()
        {
            Game.DebugGrant(new ResourceBundle(50, 50, 50, 50));
            Refresh();
            Save();
        }

        // MARK: - Placement

        public List<PlacementIssue> PlacementIssues()
        {
            if (PlacingBuildingId == null || GhostCell == null) return new List<PlacementIssue>();
            return Game.PlacementIssues(PlacingBuildingId, GhostCell.Value.x, GhostCell.Value.y, PlacementRotation);
        }

        public void RotatePlacement()
        {
            if (PlacingBuildingId != null) PlacementRotation = (PlacementRotation + 1) % 4;
            else DecorationRotation = (DecorationRotation + 1) % 4;
            Refresh();
        }

        // MARK: - Decorations

        public void BeginDecorating(string decorationId)
        {
            ActivePanel = Panel.None;
            SelectedBuildingId = null;
            PlacingBuildingId = null;
            GhostCell = null;
            DecoratingId = decorationId;
            var def = Content.Decoration(decorationId);
            // Roads and fences are drawn by dragging; single objects are placed by tapping.
            PaintMode = def != null && (def.IsRoad || def.Category == "Fences");
            Refresh();
        }

        public void EndDecorating()
        {
            DecoratingId = null;
            PaintMode = false;
            lastPainted = null;
            Refresh();
        }

        public void TogglePaintMode()
        {
            PaintMode = !PaintMode;
            Refresh();
        }

        /// <summary>Applies the decoration tool to one cell (placing, or removing with the remove tool).</summary>
        void DecorateCell(int x, int y, bool quiet)
        {
            if (DecoratingId == null) return;
            if (lastPainted == (x, y) && quiet) return;
            lastPainted = (x, y);
            GameError error;
            if (DecoratingId == RemoveTool)
            {
                var d = Game.DecorationAt(x, y);
                if (d == null) return;
                error = Game.RemoveDecoration(d.Id).Error;
            }
            else
            {
                var existing = Game.DecorationAt(x, y);
                if (existing != null && existing.DefinitionId == DecoratingId) return;
                error = Game.PlaceDecoration(DecoratingId, x, y, DecorationRotation).Error;
            }
            // While dragging, cells that cannot take the decoration are skipped silently.
            if (error != null && quiet) return;
            if (error == null) AudioDirector.Ui("ui_tap");
            Perform(error);
        }

        /// <summary>A drag over the ground while painting (screen pixels, bottom-left origin).</summary>
        public void HandlePaint(Vector2 screen)
        {
            if (!Rig.GroundPoint(screen, out var point)) return;
            var (x, y) = Grid.CellAt(point);
            // Fill the gap from the previous cell so fast drags leave no holes.
            if (lastPainted.HasValue && Math.Abs(lastPainted.Value.Item1 - x) + Math.Abs(lastPainted.Value.Item2 - y) > 1)
            {
                int px = lastPainted.Value.Item1, py = lastPainted.Value.Item2;
                while (px != x || py != y)
                {
                    if (Math.Abs(x - px) >= Math.Abs(y - py)) px += Math.Sign(x - px); else py += Math.Sign(y - py);
                    DecorateCell(px, py, true);
                }
                return;
            }
            DecorateCell(x, y, true);
        }

        public void EndPaintStroke() => lastPainted = null;

        public void BeginPlacement(string buildingId)
        {
            ActivePanel = Panel.None;
            SelectedBuildingId = null;
            DecoratingId = null;
            PaintMode = false;
            PlacingBuildingId = buildingId;
            PlacementRotation = 0;
            GhostCell = SuggestedCell(buildingId);
            if (GhostCell != null) Rig.SetFocus(Grid.CellCenter(GhostCell.Value.x, GhostCell.Value.y));
            Refresh();
        }

        public void CancelPlacement()
        {
            DecoratingId = null;
            PaintMode = false;
            PlacingBuildingId = null;
            GhostCell = null;
            Refresh();
        }

        public void ConfirmPlacement()
        {
            if (PlacingBuildingId == null || GhostCell == null) return;
            var result = Game.PlaceBuilding(PlacingBuildingId, GhostCell.Value.x, GhostCell.Value.y, PlacementRotation);
            if (Perform(result.Error))
            {
                PlacingBuildingId = null;
                GhostCell = null;
                Refresh();
            }
        }

        /// <summary>Nearest valid cell to the camp centre, so the ghost starts somewhere sensible.</summary>
        (int x, int y)? SuggestedCell(string buildingId)
        {
            var map = Content.Map;
            var first = Game.State.Buildings.Count > 0 ? Game.State.Buildings[0] : null;
            int cx = first?.X ?? map.Width / 2, cy = first?.Y ?? map.Height / 2;
            // Search outwards ring by ring (Manhattan distance) and stop at the first good cell.
            int maxR = map.Width + map.Height;
            for (int r = 0; r <= maxR; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int rest = r - Math.Abs(dx);
                    foreach (int dy in rest == 0 ? new[] { 0 } : new[] { -rest, rest })
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) continue;
                        bool ok = true;
                        foreach (var issue in Game.PlacementIssues(buildingId, x, y))
                            if (issue.Kind != PlacementIssueKind.Unaffordable) { ok = false; break; }
                        if (ok) return (x, y);
                    }
                }
            }
            return (cx + 1, cy + 1);
        }

        /// <summary>A tap on the world (screen pixels, bottom-left origin).</summary>
        public void HandleTap(Vector2 screen)
        {
            if (!Rig.GroundPoint(screen, out var point)) return;
            var (x, y) = Grid.CellAt(point);
            if (DecoratingId != null)
            {
                lastPainted = null;
                DecorateCell(x, y, false);
                lastPainted = null;
            }
            else if (PlacingBuildingId != null)
            {
                GhostCell = (x, y);
            }
            else
            {
                var hit = Game.BuildingIdAt(x, y);
                SelectedBuildingId = hit == SelectedBuildingId ? null : hit;
            }
            Refresh();
        }
    }
}
