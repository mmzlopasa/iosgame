using System;
using System.Collections.Generic;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    public enum Panel { None, Build, People, Research, Objectives, Map, Settings }

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
        public readonly List<Toast> Toasts = new List<Toast>();
        public bool EventMinimized;
        public string Celebration;
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

            Game.Update(); // simulate time spent away since the last save (capped, never duplicated)
            if (message != null) AddToast(new GameNotice(NoticeKind.Warning, message));
            AbsorbNotices();
            World.Init(this);
            Rig.Bounds = MapBounds();
            FocusOnSettlement(true);
            Save();
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
            Handheld.Vibrate();
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
        public void ResolveEvent(string optionId) => Perform(Game.ResolveEvent(optionId).Error);
        public void Explore(string regionId) => Perform(Game.StartExpedition(regionId).Error);
        public void ClaimMilestone(string id) => Perform(Game.ClaimMilestone(id).Error);

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
            return Game.PlacementIssues(PlacingBuildingId, GhostCell.Value.x, GhostCell.Value.y);
        }

        public void BeginPlacement(string buildingId)
        {
            ActivePanel = Panel.None;
            SelectedBuildingId = null;
            PlacingBuildingId = buildingId;
            GhostCell = SuggestedCell(buildingId);
            if (GhostCell != null) Rig.SetFocus(Grid.CellCenter(GhostCell.Value.x, GhostCell.Value.y));
            Refresh();
        }

        public void CancelPlacement()
        {
            PlacingBuildingId = null;
            GhostCell = null;
            Refresh();
        }

        public void ConfirmPlacement()
        {
            if (PlacingBuildingId == null || GhostCell == null) return;
            var result = Game.PlaceBuilding(PlacingBuildingId, GhostCell.Value.x, GhostCell.Value.y);
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
            (int, int)? best = null;
            int bestDistance = int.MaxValue;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int d = Math.Abs(x - cx) + Math.Abs(y - cy);
                    if (d >= bestDistance) continue;
                    bool ok = true;
                    foreach (var issue in Game.PlacementIssues(buildingId, x, y))
                        if (issue.Kind != PlacementIssueKind.Unaffordable) { ok = false; break; }
                    if (ok) { best = (x, y); bestDistance = d; }
                }
            }
            return best ?? (cx + 1, cy + 1);
        }

        /// <summary>A tap on the world (screen pixels, bottom-left origin).</summary>
        public void HandleTap(Vector2 screen)
        {
            if (!Rig.GroundPoint(screen, out var point)) return;
            var (x, y) = Grid.CellAt(point);
            if (PlacingBuildingId != null)
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
