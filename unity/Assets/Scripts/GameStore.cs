using System;
using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using UnityEngine;

namespace Founders
{
    public enum Panel { None, Build, People, Research, Objectives, Map, Profile, Settings, Weather, Customs }

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
        /// <summary>The running settlement's content (its own map and lands on a generated valley).</summary>
        public GameContent Content => Game?.Content ?? baseContent;
        GameContent baseContent;
        /// <summary>The content as shipped (the hand-drawn valley).</summary>
        public GameContent BaseContent => baseContent;
        public Game Game { get; private set; }
        public FileSaveStore SaveStore { get; private set; }
        /// <summary>The founder (name, portrait, frame, play time); kept when the settlement is reset.</summary>
        public PlayerProfile Profile { get; private set; }
        FileProfileStore profileStore;
        public CloudSave Cloud { get; private set; }
        public WorldView World { get; private set; }
        public CameraRig Rig { get; private set; }

        public Panel ActivePanel = Panel.None;
        public int? SelectedBuildingId;
        /// <summary>A tree stand, rock or ore vein the player tapped.</summary>
        public (int x, int y)? SelectedNode;
        public string PlacingBuildingId { get; private set; }
        public (int x, int y)? GhostCell { get; private set; }
        /// <summary>Quarter turns of the building being placed.</summary>
        public int PlacementRotation { get; private set; }
        /// <summary>Decoration being laid out (tap or drag on the ground), or "remove" to clear decorations.</summary>
        public string DecoratingId { get; private set; }
        public const string RemoveTool = "remove";
        /// <summary>Zone brushes are decoration tools named "zone:&lt;zone id&gt;" ("zone:clear" erases).</summary>
        public const string ZonePrefix = "zone:", ZoneEraser = "zone:clear";
        public static bool IsZoneTool(string id) => id != null && id.StartsWith(ZonePrefix, StringComparison.Ordinal);
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
            baseContent = GameContent.Load(asset.text);
            SaveStore = new FileSaveStore(Application.persistentDataPath);
            profileStore = new FileProfileStore(Application.persistentDataPath);

            string message = null;
            var outcome = ephemeral ? LoadOutcome.NoSave : SaveStore.Load(baseContent);
            switch (outcome.Result)
            {
                case LoadResult.Loaded:
                    Game = new Game(baseContent, outcome.State, clock);
                    break;
                case LoadResult.Corrupt:
                    Debug.LogWarning($"[FOUNDERS] Corrupt save moved aside: {outcome.Reason}");
                    message = "Your previous save could not be read, so a new camp was started. A backup was kept.";
                    Game = NewSettlement();
                    break;
                case LoadResult.UnsupportedVersion:
                    Debug.LogWarning($"[FOUNDERS] Save version {outcome.Version} is newer than this build supports.");
                    message = $"Your save is from a newer version of FOUNDERS (v{outcome.Version}). Started a temporary new camp; the save was not changed.";
                    Game = NewSettlement();
                    break;
                default:
                    Game = NewSettlement();
                    break;
            }

            // A settlement still waiting for its campfire first chooses its valley (Foundation's map generator).
            if (!ephemeral && Founding) OpenMapChoice();
            Profile = (ephemeral ? null : profileStore.Load()) ?? PlayerProfile.New(Game.State.SettlementId, CloudSave.Now);
            Profile.Sessions++;
            Cloud = new CloudSave(this, ephemeral ? new Founders.Cloud.NoCloud() : Founders.Cloud.CloudBackends.Create());

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
            if (!ephemeral) Cloud.Start();
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
            if (Founding)
            {
                // The band looks over the valley: a wide view around where the map suggests to settle.
                BeginFounding();
                var (sx, sy) = FoundingCell ?? Game.CampCell();
                Rig.SetFocus(Grid.CellCenter(sx, sy), instant, 70f);
                return;
            }
            var first = Game.State.Buildings.Count > 0 ? Game.State.Buildings[0] : null;
            var center = first != null
                ? Grid.CellCenter(first.X, first.Y)
                : Grid.CellCenter(Content.Map.Width / 2f, Content.Map.Height / 2f);
            Rig.SetFocus(center, instant, 30f);
        }

        // MARK: - Founding (the player chooses where the campfire and the first shelters go)

        /// <summary>A new settlement: in capture mode it stands where the map suggests, otherwise the player chooses.</summary>
        Game NewSettlement() => Ephemeral ? Game.NewGame(baseContent, clock) : Game.NewUnfoundedGame(baseContent, clock);

        /// <summary>True until the campfire is lit and the first shelters stand.</summary>
        public bool Founding => Game != null && !Game.State.Founded;
        /// <summary>Where the player decided the campfire burns (second step: the shelters), or null (first step).</summary>
        public (int x, int y)? FoundingFire { get; private set; }
        /// <summary>The cell the player is pointing at in the current step.</summary>
        public (int x, int y)? FoundingCell { get; private set; }

        /// <summary>Why the current choice does not work (null = it does).</summary>
        public string FoundingIssue()
        {
            if (!Founding || FoundingCell == null) return "Tap the land where your people should settle.";
            var (x, y) = FoundingCell.Value;
            return FoundingFire is (int fx, int fy) ? Game.SettlementIssue(fx, fy, x, y) : Game.CampfireIssue(x, y);
        }

        void BeginFounding()
        {
            ActivePanel = Panel.None;
            PlacingBuildingId = null;
            GhostCell = null;
            SelectedBuildingId = null;
            SelectedNode = null;
            DecoratingId = null;
            if (FoundingCell == null) FoundingCell = FoundingFire ?? Game.CampCell();
        }

        public void ConfirmFounding()
        {
            if (!Founding || FoundingCell == null || FoundingIssue() != null) return;
            var (x, y) = FoundingCell.Value;
            if (FoundingFire == null)
            {
                FoundingFire = (x, y);
                FoundingCell = SuggestedSettlement(x, y);
                AudioDirector.Ui("ui_tap");
                Rig.SetFocus(Grid.CellCenter(x, y), false, 34f);
                Refresh();
                return;
            }
            var (fx, fy) = FoundingFire.Value;
            if (!Perform(Game.Found(fx, fy, x, y).Error)) return;
            FoundingFire = null;
            FoundingCell = null;
            Celebration = null;
            World.RebuildAll();
            Rig.SetFocus(Grid.CellCenter(fx, fy), false, 30f);
            Save();
            Refresh();
        }

        /// <summary>Back to choosing the campfire's place.</summary>
        public void BackFounding()
        {
            if (FoundingFire == null) return;
            FoundingCell = FoundingFire;
            FoundingFire = null;
            Refresh();
        }

        /// <summary>The nearest spot around the fire where the first shelters fit.</summary>
        (int x, int y) SuggestedSettlement(int fx, int fy)
        {
            for (int r = 1; r <= Game.SettlementReach; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        if (Game.SettlementIssue(fx, fy, fx + dx, fy + dy) == null) return (fx + dx, fy + dy);
                    }
            return (fx + 1, fy + 1);
        }

        // MARK: - Choosing the valley (before the campfire is lit)

        /// <summary>True while the map generator is on screen.</summary>
        public bool ChoosingMap { get; private set; }
        /// <summary>The valley being shaped: a preset's id (<see cref="MapPreset"/>), or null for the player's own settings.</summary>
        public string MapPresetId { get; private set; }
        /// <summary>The generator's settings (also shown for a preset, so the sliders start from it).</summary>
        public MapSettings MapDraft { get; private set; }

        /// <summary>The map the draft would give: the hand-drawn valley or a generated one.</summary>
        public string DraftMapName => MapPresetId == MapPreset.ClassicId ? baseContent.Map.Name : MapDraft.MapName;

        /// <summary>Settings that look like the hand-drawn valley, for sliders moved away from it.</summary>
        static MapSettings ClassicLike(int seed) => MapSettings.Of(seed, 3, 2, 0, 3, 3, 3, 2);

        /// <summary>Opens the map generator on the valley the settlement has now.</summary>
        public void OpenMapChoice()
        {
            if (!Founding || Ephemeral) return;
            string current = Game.Content.Map.Name;
            if (MapSettings.IsGeneratedName(current) && MapSettings.TryParse(current, out var s))
            {
                MapDraft = s;
                MapPresetId = MapPreset.All.FirstOrDefault(p => p.Settings != null && p.Settings.Code == s.Code)?.Id;
            }
            else
            {
                MapDraft = ClassicLike(NewSeed());
                MapPresetId = MapPreset.ClassicId;
            }
            FoundingFire = null;
            ChoosingMap = true;
        }

        static int NewSeed() => new System.Random().Next(0, MapSettings.SeedRange);

        public void ChooseMapPreset(string id)
        {
            var p = MapPreset.Get(id);
            if (p == null) return;
            MapPresetId = p.Id;
            MapDraft = p.Settings?.Clone() ?? ClassicLike(MapDraft?.Seed ?? NewSeed());
        }

        /// <summary>One step more or less of a feature; the valley becomes the player's own.</summary>
        public void StepMapFeature(MapSettings.Feature f, int delta)
        {
            var d = MapDraft.Clone();
            d[f] = d[f] + delta;
            if (d.Code == MapDraft.Code && MapPresetId != MapPreset.ClassicId) return;
            MapDraft = d;
            MapPresetId = null;
        }

        /// <summary>The same settings, another valley.</summary>
        public void RerollMap()
        {
            var d = MapDraft.Clone();
            d.Seed = NewSeed();
            MapDraft = d;
            MapPresetId = null;
        }

        /// <summary>Uses a map code (shared by another player); returns false when it is not one.</summary>
        public bool UseMapCode(string code)
        {
            if (!MapSettings.TryParse(code, out var s)) return false;
            MapDraft = s;
            MapPresetId = MapPreset.All.FirstOrDefault(p => p.Settings != null && p.Settings.Code == s.Code)?.Id;
            return true;
        }

        /// <summary>Closes the generator without changing the valley.</summary>
        public void CancelMapChoice() => ChoosingMap = false;

        /// <summary>
        /// Settles on the drafted valley: a new (still unfounded) settlement on it, the terrain, water and nature
        /// built again from its map, and the player chooses where the campfire burns.
        /// </summary>
        public void ConfirmMapChoice()
        {
            if (!ChoosingMap) return;
            ChoosingMap = false;
            string name = DraftMapName;
            if (!Founding || name == Game.Content.Map.Name) { FocusOnSettlement(false); Refresh(); return; }
            var old = Game.State;
            var game = Game.NewUnfoundedGame(baseContent.ForMap(name), clock, old.SettlementName);
            game.State.SettlementId = old.SettlementId;
            Game = game;
            FoundingFire = null;
            FoundingCell = null;
            World.RebuildAll();
            Rig.Bounds = MapBounds();
            FocusOnSettlement(true);
            Save();
            Refresh();
        }

        // MARK: - Clock

        void Update()
        {
            if (Game == null || Time.unscaledTime < nextStep) return;
            nextStep = Time.unscaledTime + 0.25f;
            Game.Update();
            Refresh();
            if (Application.isFocused) Profile.PlaySeconds += 0.25;
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

        /// <summary>A short message at the top of the screen.</summary>
        public void Notify(string message) => AddToast(new GameNotice(NoticeKind.Info, message));

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
                // A new custom is sealed in the Book of Customs itself (the milestone dialog would name the wrong thing).
                if (n.Kind == NoticeKind.Milestone && !n.Message.StartsWith("New custom:", StringComparison.Ordinal))
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
            if (paused) { Save(); Cloud?.Tick(true); }
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
                profileStore.Save(Profile);
                lastSaveWallTime = clock.Now();
                SaveStatus = $"Saved {DateTime.Now:HH:mm:ss}";
                Cloud?.Tick();
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
            Game = NewSettlement();
            FoundingFire = null;
            FoundingCell = null;
            PlacingBuildingId = null;
            GhostCell = null;
            SelectedBuildingId = null;
            Celebration = null;
            Offline = null;
            Toasts.Clear();
            ActivePanel = Panel.None;
            World.RebuildAll();
            Rig.Bounds = MapBounds();
            FocusOnSettlement(false);
            OpenMapChoice();
            Save();
            Refresh();
        }

        /// <summary>Replaces the settlement (and the profile, when given) with progress taken from the cloud.</summary>
        public void Restore(GameState state, PlayerProfile profile)
        {
            if (profile != null)
            {
                profile.Sessions = Math.Max(profile.Sessions, Profile.Sessions);
                Profile = profile;
            }
            Game = new Game(baseContent, state, clock);
            Game.Update();
            ChoosingMap = false;
            FoundingFire = null;
            FoundingCell = null;
            PlacingBuildingId = null;
            GhostCell = null;
            SelectedBuildingId = null;
            SelectedNode = null;
            DecoratingId = null;
            Celebration = null;
            Offline = null;
            Toasts.Clear();
            ActivePanel = Panel.None;
            World.RebuildAll();
            Rig.Bounds = MapBounds();
            FocusOnSettlement(false);
            Save();
            AddToast(new GameNotice(NoticeKind.Info, $"Loaded {state.SettlementName} from the cloud."));
            Refresh();
        }

        // MARK: - Profile

        /// <summary>Renames the founder; returns the problem with the name, or null.</summary>
        public string RenameFounder(string name)
        {
            var problem = Profile.Rename(name);
            if (problem == null) Save();
            return problem;
        }

        public void SetAvatar(string id)
        {
            if (Profile.SetAvatar(id)) Save();
        }

        public void SetFrame(string id)
        {
            if (Profile.SetFrame(id, Game.State.ReachedMilestones.Count)) Save();
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
        public void UseSpeedup(SpeedupTask task, SpeedupKind kind, int minutes) => Perform(Game.UseSpeedup(task, kind, minutes).Error);
        public void FinishWithSpeedups(SpeedupTask task) => Perform(Game.UseSpeedupsToFinish(task).Error);
        public void ClaimMilestone(string id) => Perform(Game.ClaimMilestone(id).Error);
        public void ToggleHarvest(int x, int y) => Perform(Game.ToggleHarvestOrder(x, y).Error);

        /// <summary>Burns the heart hot for a while (more warmth, wider reach, much more firewood).</summary>
        public void StokeHeart() => Perform(Game.StokeHeart().Error);

        /// <summary>Signs a custom from the Book of Customs (for good). Returns true when it was signed.</summary>
        public bool SignLaw(string id) => Perform(Game.SignLaw(id).Error);

        /// <summary>Answers the people's petition: promise what they ask, or refuse.</summary>
        public void AnswerPetition(bool promise) => Perform(Game.AnswerPetition(promise).Error);

        /// <summary>Sends someone to gather this tree, rock, bush or water now.</summary>
        public void SendGatherer(int x, int y)
        {
            var node = Game.NodeAt(x, y);
            if (Perform(Game.SendGatherer(x, y).Error) && node != null) World.Dispatch(x, y, node.Job);
        }

        /// <summary>
        /// The tree, rock, ore or bush under a screen point. The view ray is followed through the
        /// air, so tapping a tree's crown picks that tree (not the ground behind it).
        /// </summary>
        public (int x, int y)? PickNode(Vector2 screen)
        {
            var ray = Rig.Cam.ScreenPointToRay(screen);
            var ground = World.Ground;
            for (float t = 0.5f; t < 420f; t += 0.35f)
            {
                var p = ray.GetPoint(t);
                float h = ground != null ? ground.HeightAt(p) : 0f;
                if (p.y < Mathf.Max(h, WorldTerrain.WaterLevel) - 0.05f) return null;
                var (cx, cy) = Grid.CellAt(p);
                var terrain = Game.TerrainNow(cx, cy);
                if (terrain == null || terrain == TerrainType.Water) continue;
                float tall = terrain == TerrainType.Forest ? 7.5f : terrain == TerrainType.Grass ? 1.2f : 1.8f;
                if (p.y > h + tall) continue;
                var node = Game.NodeAt(cx, cy);
                if (node == null || node.Depleted || !Game.IsRevealed(cx, cy)) continue;
                // Close enough to the middle of the cell, where the tree or rock stands.
                var c = Grid.CellCenter(cx, cy);
                if (new Vector2(p.x - c.x, p.z - c.z).magnitude > Grid.CellSize * 0.62f) continue;
                return (cx, cy);
            }
            return null;
        }

        /// <summary>The narrator's story box is on screen: only when the player taps for it, never by itself.</summary>
        public bool StoryOpen;

        /// <summary>Opens the narrator's story box for the waiting chapter (or the given one).</summary>
        public void OpenStory(string chapterId = null)
        {
            if (chapterId != null) Game.State.PendingStory = chapterId;
            if (Game.State.PendingStory == null) return;
            StoryOpen = true;
            Refresh();
        }

        public void DismissStory()
        {
            StoryOpen = false;
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
            PaintMode = IsZoneTool(decorationId) || def != null && (def.IsRoad || def.Category == "Fences");
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
            if (IsZoneTool(DecoratingId))
            {
                string zone = DecoratingId == ZoneEraser ? null : DecoratingId.Substring(ZonePrefix.Length);
                if (Game.ZoneAt(x, y) == zone) return;
                error = Game.SetZone(x, y, zone).Error;
            }
            else if (DecoratingId == RemoveTool)
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
            if (Founding)
            {
                Notify("First choose where your people settle.");
                return;
            }
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

        /// <summary>True when the press is on (or right next to) the building being placed.</summary>
        public bool GrabsGhost(Vector2 screen)
        {
            if (PlacingBuildingId == null || GhostCell == null || !Rig.GroundPoint(screen, out var p)) return false;
            var def = Content.Building(PlacingBuildingId);
            var fp = Game.Rotated(def.Footprint, PlacementRotation);
            var (x, y) = Grid.CellAt(p);
            var g = GhostCell.Value;
            return x >= g.x - 1 && x <= g.x + fp.Width && y >= g.y - 1 && y <= g.y + fp.Height;
        }

        /// <summary>Drags the building being placed so its middle follows the finger.</summary>
        public void DragGhost(Vector2 screen)
        {
            if (PlacingBuildingId == null || !Rig.GroundPoint(screen, out var p)) return;
            var fp = Game.Rotated(Content.Building(PlacingBuildingId).Footprint, PlacementRotation);
            var (x, y) = Grid.CellAt(p - new Vector3((fp.Width - 1) * 0.5f * Grid.CellSize, 0, -(fp.Height - 1) * 0.5f * Grid.CellSize));
            if (GhostCell == (x, y)) return;
            GhostCell = (x, y);
            Refresh();
        }

        /// <summary>Nearest valid cell to the camp centre, so the ghost starts somewhere sensible.</summary>
        (int x, int y)? SuggestedCell(string buildingId)
        {
            var map = Content.Map;
            var first = Game.Heart ?? (Game.State.Buildings.Count > 0 ? Game.State.Buildings[0] : null);
            int cx = first?.X ?? map.Width / 2, cy = first?.Y ?? map.Height / 2;
            // Search outwards ring by ring (Manhattan distance) and stop at the first good cell.
            // Only the land matters here: costs and the building queue are shown on the placement card.
            int maxR = 20;
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
                            if (issue.Kind != PlacementIssueKind.Unaffordable && issue.Kind != PlacementIssueKind.ConstructionQueueFull
                                && issue.Kind != PlacementIssueKind.LimitReached) { ok = false; break; }
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
            if (Founding)
            {
                if (x >= 0 && y >= 0 && x < Content.Map.Width && y < Content.Map.Height) FoundingCell = (x, y);
            }
            else if (DecoratingId != null)
            {
                lastPainted = null;
                DecorateCell(x, y, false);
                lastPainted = null;
            }
            else if (PlacingBuildingId != null)
            {
                // The building's middle goes where the ground was tapped (as when it is dragged).
                DragGhost(screen);
            }
            else
            {
                // Trees, rocks, ore and berry bushes can be tapped: the player sends gatherers to them.
                var picked = PickNode(screen);
                var hit = picked == null ? Game.BuildingIdAt(x, y) : null;
                SelectedBuildingId = hit == SelectedBuildingId ? null : hit;
                if (picked == null && hit == null && Game.IsRevealed(x, y) && Game.NodeAt(x, y) != null) picked = (x, y);
                SelectedNode = picked != null && SelectedNode != picked ? picked : null;
                if (SelectedNode != null) AudioDirector.Ui("ui_tap");
            }
            Refresh();
        }
    }
}
