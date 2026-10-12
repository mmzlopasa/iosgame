using System;
using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// Owns the settlement state and applies the rules to it. Rules are split by topic into
    /// partial files (Economy, Placement, Construction, Research, Events, Progression,
    /// Exploration). The UI only calls these commands and reads State; it never changes
    /// state directly.
    /// </summary>
    public sealed partial class Game
    {
        public readonly GameContent Content;
        public GameState State { get; internal set; }
        readonly IGameClock clock;
        /// <summary>Feedback produced since the UI last called DrainNotices().</summary>
        readonly List<GameNotice> notices = new List<GameNotice>();

        public Game(GameContent content, GameState state, IGameClock clock)
        {
            Content = content;
            State = state;
            this.clock = clock;
            SyncPeople(); // older saves had no named people: create them from the population
        }

        /// <summary>Creates a brand new settlement from content.</summary>
        public static Game NewGame(GameContent content, IGameClock clock, string settlementName = "Riverside Camp")
        {
            var b = content.Balance;
            var assignments = new JobAssignments();
            foreach (var slot in b.StartAssignments) assignments[slot.Job] += slot.Count;

            var state = new GameState
            {
                SettlementId = Guid.NewGuid().ToString().ToUpperInvariant(),
                SettlementName = settlementName,
                MapName = content.Map.Name,
                EpochId = content.StartEpoch,
                StageId = content.StartStage,
                LastUpdateWallTime = clock.Now(),
                SimTime = 0,
                Resources = b.StartResources,
                Population = b.StartPopulation,
                GrowthProgress = 0,
                Assignments = assignments,
                FoodShortage = false,
                NextBuildingId = 1,
                ExploredRegions = content.Regions.Where(r => r.RevealedAtStart).Select(r => r.Id).ToList(),
                NextLegacyId = 1,
            };
            var game = new Game(content, state, clock);
            foreach (var sb in content.Map.StartBuildings)
            {
                var def = content.Building(sb.BuildingId);
                if (def == null) continue;
                game.State.Buildings.Add(new PlacedBuilding
                {
                    Id = game.State.NextBuildingId, DefinitionId = def.Id, X = sb.X, Y = sb.Y,
                    Status = BuildingStatus.Complete, RemainingWork = 0, TotalWork = def.BuildSeconds,
                    History = new List<string> { def.Id }, FoundedAtSimTime = 0,
                });
                game.State.NextBuildingId += 1;
            }
            var traits = string.Join(", ", content.Map.Traits);
            game.AddLegacy("founding", $"{settlementName} is founded",
                $"A small band settles in a clearing ({traits}). This place will shape everything built here.", content.Map.Name);
            game.BeginStory();
            return game;
        }

        /// <summary>Returns and clears pending feedback for the UI.</summary>
        public List<GameNotice> DrainNotices()
        {
            var output = new List<GameNotice>(notices);
            notices.Clear();
            return output;
        }

        void Notice(NoticeKind kind, string message) => notices.Add(new GameNotice(kind, message));

        // MARK: - Time

        /// <summary>
        /// Brings the simulation up to the current wall-clock time. Every wall-clock interval is
        /// simulated exactly once, so calling this on resume, after a restart or repeatedly can
        /// never grant duplicate production.
        /// </summary>
        public void Update()
        {
            double now = clock.Now();
            double elapsed = now - State.LastUpdateWallTime;
            if (!(elapsed > 0))
            {
                // Clock moved backwards (or no time passed): re-anchor without simulating.
                State.LastUpdateWallTime = now;
                return;
            }
            double simulated = Math.Min(elapsed, Content.Balance.MaxOfflineSeconds);
            State.LastUpdateWallTime = now;
            Advance(simulated);
        }

        /// <summary>Simulates game time in fixed steps (deterministic for identical input).</summary>
        internal void Advance(double seconds)
        {
            if (!(seconds > 0)) return;
            double step = Content.Balance.SimulationStepSeconds;
            // Long absences (hours away) are simulated in coarser steps so resuming stays quick.
            if (seconds > 3600) step = Math.Max(step, 5);
            double remaining = seconds;
            while (remaining > 1e-9)
            {
                double dt = Math.Min(step, remaining);
                SimulateStep(dt);
                remaining -= dt;
            }
            EvaluateProgress();
        }

        void SimulateStep(double dt)
        {
            stepEffects = null;
            stepApproval = null;
            stepEffects = ActiveEffects();
            stepApproval = Approval;
            try { Step(dt); }
            finally
            {
                stepEffects = null;
                stepApproval = null;
                gatherCache.Clear();
            }
        }

        void Step(double dt)
        {
            simulating = true;
            var snapshot = Economy();
            simulating = false;

            // Resources
            var caps = StorageCapacity();
            var net = snapshot.Net;
            var res = State.Resources;
            foreach (var t in ResourceTypes.All)
                res[t] = Math.Min(caps[t], Math.Max(0, res[t] + net[t] * dt));
            State.Resources = res;
            AdvanceNodes(dt);

            // Food shortage: warning + productivity penalty, never deaths.
            bool shortage = State.Resources.Food <= 1e-6 && net.Food < 0;
            if (shortage != State.FoodShortage)
            {
                State.FoodShortage = shortage;
                if (shortage) Notice(NoticeKind.Warning, "Food has run out! Work slows down until food is restored.");
                else Notice(NoticeKind.Success, "Food supply restored.");
            }

            AdvanceSkills(dt);
            AdvanceConstruction(dt);
            AdvanceResearch(dt);
            AdvanceExpedition(dt);
            AdvanceGrowth(dt, snapshot.Net.Food);

            State.SimTime += dt;
            TrackSeason();
            double simTime = State.SimTime;
            State.Modifiers.RemoveAll(m => m.ExpiresAtSimTime.HasValue && m.ExpiresAtSimTime.Value <= simTime);
        }

        void AdvanceGrowth(double dt, double netFood)
        {
            var b = Content.Balance;
            // Families grow only while there is room, a reserve and more food coming in than eaten.
            if (State.FoodShortage || State.Population >= HousingCapacity() || State.Resources.Food < b.GrowthMinFood
                || netFood <= 0 || b.GrowthSecondsPerPerson <= 0) return;
            double rate = Multiplier(EffectKind.GrowthMultiplier) * (Season?.Growth ?? 1) * ApprovalGrowth(Approval);
            if (rate <= 0) return;
            State.GrowthProgress += dt / b.GrowthSecondsPerPerson * rate;
            if (State.GrowthProgress >= 1)
            {
                State.GrowthProgress = 0;
                State.Population += 1;
                SyncPeople();
                Notice(NoticeKind.Success, $"A new member joined the community (population {State.Population}).");
            }
        }

        // MARK: - Legacy

        internal void AddLegacy(string kind, string title, string detail, string refId)
        {
            State.Legacy.Add(new LegacyEntry
            {
                Id = State.NextLegacyId, SimTime = State.SimTime, EpochId = State.EpochId,
                Kind = kind, Title = title, Detail = detail, RefId = refId,
            });
            State.NextLegacyId += 1;
        }

        // MARK: - Display helpers

        public string EpochName => Content.Epoch(State.EpochId)?.Name ?? State.EpochId;

        public string StageName =>
            Content.Epoch(State.EpochId)?.Stages.FirstOrDefault(s => s.Id == State.StageId)?.Name ?? State.StageId;

        public int IdleWorkers => Math.Max(0, State.Population - State.Assignments.Total);

        // MARK: - Developer tools

        /// <summary>Simulates game time immediately (as if the app had been away).</summary>
        public void DebugFastForward(double seconds)
        {
            Update();
            Advance(Math.Min(Math.Max(0, seconds), Content.Balance.MaxOfflineSeconds));
        }

        /// <summary>Adds resources (clamped to storage) and re-checks objectives.</summary>
        public void DebugGrant(ResourceBundle resources)
        {
            Update();
            Apply(new Effect(EffectKind.GrantResources) { Resources = resources }, "debug");
            EvaluateProgress();
        }
    }
}
