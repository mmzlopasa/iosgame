using System;
using System.IO;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    // Port of Core/Tests/FoundersCoreTests (Swift). Same scenarios, same numbers.

    public class ContentTests
    {
        [Test]
        public void BundledContentIsValid()
        {
            var c = Fixtures.Content();
            Assert.That(ContentValidator.Validate(c), Is.Empty);
            Assert.That(c.Buildings.Count(b => b.Buildable), Is.EqualTo(44));
            Assert.That(c.Technologies.Count, Is.EqualTo(29));
        }

        [Test]
        public void DetectsPrerequisiteCycle()
        {
            var c = Fixtures.Content();
            c.Technology("improved_stone_tools").Prerequisites = new() { "permanent_settlement" };
            var problems = ContentValidator.Validate(c);
            Assert.That(problems.Any(p => p.Contains("cycle")), Is.True, string.Join("; ", problems));
        }

        [Test]
        public void DetectsUnknownReferencesAndBadMap()
        {
            var c = Fixtures.Content();
            c.Technologies[0].Prerequisites = new() { "does_not_exist" };
            c.Map.Rows[0] = "TTT";
            var problems = ContentValidator.Validate(c);
            Assert.That(problems.Any(p => p.Contains("does_not_exist")), Is.True);
            Assert.That(problems.Any(p => p.Contains("map row 0")), Is.True);
        }

        [Test]
        public void LoadRejectsInvalidContent()
        {
            // Duplicate building id.
            var json = Fixtures.ContentJson.Replace("\"id\": \"woven_shelter\"", "\"id\": \"shelter\"");
            Assert.Throws<ContentException>(() => GameContent.Load(json));
            Assert.Throws<FormatException>(() => GameContent.Load("{ not json"));
        }
    }

    public class EconomyTests
    {
        [Test]
        public void StartingState()
        {
            var (game, _) = Fixtures.NewGame();
            Assert.That(game.State.Population, Is.EqualTo(6));
            Assert.That(game.HousingCapacity(), Is.EqualTo(6));
            Assert.That(game.State.Buildings.Count, Is.EqualTo(4));
            Assert.That(game.IdleWorkers, Is.EqualTo(3));
            Assert.That(game.State.Legacy.First().Kind, Is.EqualTo("founding"));
        }

        [Test]
        public void WoodProductionIsExactOverControlledDuration()
        {
            var (game, clock) = Fixtures.NewGame();
            // 1 wood gatherer at 0.10 wood/s, wood has no consumption.
            game.Run(60, clock);
            Assert.That(game.State.Resources.Wood, Is.EqualTo(25 + 6).Within(1e-6));
        }

        [Test]
        public void KnowledgeFromResearchers()
        {
            var (game, clock) = Fixtures.NewGame();
            Expect.Success(game.SetWorkers(JobType.Research, 1));
            // Using a third job completes "obj_jobs" (+4 knowledge reward).
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(4).Within(1e-6));
            game.Run(100, clock);
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(4 + 6).Within(1e-6));
        }

        [Test]
        public void JobReassignmentChangesRatesImmediately()
        {
            var (game, _) = Fixtures.NewGame();
            double before = game.Economy().Production.Wood;
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 0));
            Assert.That(game.Economy().Production.Wood, Is.EqualTo(0));
            Assert.That(before, Is.EqualTo(0.1).Within(1e-9));
            Expect.Success(game.SetWorkers(JobType.Foraging, 2));
            Assert.That(game.Economy().Production.Food, Is.EqualTo(0.24).Within(1e-9));
        }

        [Test]
        public void JobCapacityAndIdleLimits()
        {
            var (game, _) = Fixtures.NewGame();
            // Campfire offers only 2 foraging slots.
            Expect.Failure(game.SetWorkers(JobType.Foraging, 3), GameError.NoJobSlots(2));
            // Construction has unlimited slots but only 3 people are idle.
            Expect.Failure(game.SetWorkers(JobType.Construction, 4), GameError.TooManyWorkers(3));
            Expect.Success(game.SetWorkers(JobType.Construction, 3));
            Assert.That(game.IdleWorkers, Is.EqualTo(0));
        }

        [Test]
        public void StorageCapsProduction()
        {
            var (game, clock) = Fixtures.NewGame();
            game.Run(10_000, clock);
            Assert.That(game.State.Resources.Wood, Is.LessThanOrEqualTo(game.StorageCapacity().Wood + 1e-9));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(game.StorageCapacity().Wood).Within(1e-6));
        }

        [Test]
        public void FoodShortageWarnsButNobodyDies()
        {
            var (game, clock) = Fixtures.NewGame();
            Expect.Success(game.SetWorkers(JobType.Foraging, 0));
            game.Run(4 * 3600, clock);
            Assert.That(game.State.Resources.Food, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.State.FoodShortage, Is.True);
            Assert.That(game.State.Population, Is.EqualTo(6));
            Assert.That(game.DrainNotices().Any(n => n.Kind == NoticeKind.Warning), Is.True);
            // Shortage slows work.
            Assert.That(game.RatePerWorker(JobType.Woodcutting), Is.EqualTo(0.1 * 0.6).Within(1e-9));
        }

        [Test]
        public void PopulationGrowsWhenHousedAndFed()
        {
            // Families grow only while more food comes in than is eaten.
            var (game, clock) = Fixtures.NewGame(c => c.Balance.FoodPerPersonPerSecond = 0.02);
            game.TestGrant(new ResourceBundle(food: 80, wood: 20));
            Expect.Success(game.PlaceBuilding("shelter", 4, 5));
            game.Run(200, clock);
            Assert.That(game.HousingCapacity(), Is.EqualTo(8));
            Assert.That(game.State.Population, Is.GreaterThan(6));
            Assert.That(game.State.Population, Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void CraftingBoostsGatheringAndConsumesMaterials()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(wood: 50, stone: 50));
            game.AddCompleted("toolmaker", 4, 4, id: 99);
            Expect.Success(game.SetWorkers(JobType.Crafting, 2));
            var snap = game.Economy();
            Assert.That(snap.CraftingActive, Is.True);
            Assert.That(snap.CraftingBonus, Is.EqualTo(0.3).Within(1e-9));
            Assert.That(snap.Production.Wood, Is.EqualTo(0.1 * 1.3).Within(1e-9));
            Assert.That(snap.Consumption.Stone, Is.EqualTo(0.04).Within(1e-9));
        }
    }

    public class PlacementTests
    {
        [Test]
        public void ValidPlacementDeductsCostExactlyOnce()
        {
            var (game, _) = Fixtures.NewGame();
            double woodBefore = game.State.Resources.Wood;
            Expect.Issues(game.PlacementIssues("foraging_camp", 4, 5));
            Expect.Success(game.PlaceBuilding("foraging_camp", 4, 5));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(woodBefore - 10).Within(1e-9));
            // Same spot again: rejected and nothing is charged.
            Expect.Failure(game.PlaceBuilding("foraging_camp", 4, 5), GameError.Placement(PlacementIssue.Overlaps));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(woodBefore - 10).Within(1e-9));
            Assert.That(game.Count("foraging_camp", includeUnfinished: true), Is.EqualTo(1));
        }

        [Test]
        public void OverlapWithStartingBuildingRejected()
        {
            var (game, _) = Fixtures.NewGame();
            Assert.That(game.PlacementIssues("shelter", 6, 7), Does.Contain(PlacementIssue.Overlaps));
        }

        [Test]
        public void MultiCellOverlapRejected()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(wood: 50, stone: 50));
            // 2x2 footprint whose corner touches the campfire at (6,7).
            Assert.That(game.PlacementIssues("community_circle", 5, 7), Does.Contain(PlacementIssue.Overlaps));
        }

        [Test]
        public void InvalidTerrainRejected()
        {
            var (game, _) = Fixtures.NewGame();
            // (10,5) is river water. (5,7) is rock and (1,2) forest: anything that stands on grass may
            // stand there too (the stones are cleared, the trees felled).
            Expect.Issues(game.PlacementIssues("shelter", 10, 5), PlacementIssue.InvalidTerrain(TerrainType.Water));
            Expect.Issues(game.PlacementIssues("shelter", 5, 7));
            Assert.That(game.Content.Map.Terrain(1, 2), Is.EqualTo(TerrainType.Forest));
            Expect.Issues(game.PlacementIssues("shelter", 1, 2));
            // Mountains stay unbuildable.
            var peak = Enumerable.Range(0, game.Content.Map.Width * game.Content.Map.Height)
                .Select(i => (x: i % game.Content.Map.Width, y: i / game.Content.Map.Width))
                .First(c => game.Content.Map.Terrain(c.x, c.y) == TerrainType.Mountain);
            Assert.That(game.PlacementIssues("shelter", peak.x, peak.y), Does.Contain(PlacementIssue.InvalidTerrain(TerrainType.Mountain)));
        }

        [Test]
        public void UnexploredAndOutOfBoundsRejected()
        {
            var (game, _) = Fixtures.NewGame();
            Assert.That(game.PlacementIssues("shelter", 20, 8), Does.Contain(PlacementIssue.Unexplored));
            Assert.That(game.PlacementIssues("shelter", -1, 0), Does.Contain(PlacementIssue.OutOfBounds));
            Assert.That(game.PlacementIssues("shelter", game.Content.Map.Width, 3), Does.Contain(PlacementIssue.OutOfBounds));
        }

        [Test]
        public void AdjacencyRules()
        {
            var (game, _) = Fixtures.NewGame();
            // (6,8) touches rock at (5,8); (8,10) has no rock nearby.
            Expect.Issues(game.PlacementIssues("stone_site", 6, 8));
            Expect.Issues(game.PlacementIssues("stone_site", 8, 10), PlacementIssue.NeedsAdjacent(TerrainType.Rock));
            // (6,5) touches forest at (6,4).
            Expect.Issues(game.PlacementIssues("wood_camp", 6, 5));
        }

        [Test]
        public void UnaffordableRejectedWithoutCharge()
        {
            var (game, _) = Fixtures.NewGame();
            var before = game.State.Resources;
            var result = game.PlaceBuilding("community_circle", 3, 9);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(GameErrorKind.Placement));
            Assert.That(result.Error.Issue.Kind, Is.EqualTo(PlacementIssueKind.Unaffordable));
            Assert.That(result.Error.Issue.Missing.Wood, Is.EqualTo(5).Within(1e-9));
            Assert.That(result.Error.Issue.Missing.Stone, Is.EqualTo(15).Within(1e-9));
            Assert.That(game.State.Resources, Is.EqualTo(before));
        }

        [Test]
        public void LockedAndNonBuildable()
        {
            var (game, _) = Fixtures.NewGame();
            Assert.That(game.PlacementIssues("campfire", 4, 4), Does.Contain(PlacementIssue.NotBuildable));
            Assert.That(game.PlacementIssues("woven_shelter", 4, 4), Does.Contain(PlacementIssue.LockedByTech("Structural Weaving")));
        }

        [Test]
        public void ConstructionQueueLimit()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(wood: 100));
            Expect.Success(game.PlaceBuilding("shelter", 4, 4));
            Expect.Success(game.PlaceBuilding("shelter", 4, 5));
            double wood = game.State.Resources.Wood;
            Expect.Failure(game.PlaceBuilding("shelter", 4, 6), GameError.Placement(PlacementIssue.ConstructionQueueFull(2)));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(wood));
        }

        [Test]
        public void MaxCountLimit()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(wood: 100, stone: 100));
            Expect.Success(game.PlaceBuilding("community_circle", 3, 9));
            Assert.That(game.PlacementIssues("community_circle", 3, 3), Does.Contain(PlacementIssue.LimitReached(1)));
        }
    }

    public class ConstructionTests
    {
        [Test]
        public void ConstructionCompletesOnceWithFeedback()
        {
            var (game, clock) = Fixtures.NewGame();
            var placed = game.PlaceBuilding("foraging_camp", 4, 5);
            Expect.Success(placed);
            game.DrainNotices();
            game.Run(14, clock);
            Assert.That(game.State.Building(placed.Value).Status, Is.EqualTo(BuildingStatus.Constructing));
            game.Run(1, clock);
            Assert.That(game.State.Building(placed.Value).Status, Is.EqualTo(BuildingStatus.Complete));
            Assert.That(game.DrainNotices().Count(n => n.Message.Contains("Construction complete")), Is.EqualTo(1));
            game.Run(100, clock);
            Assert.That(game.DrainNotices().Any(n => n.Message.Contains("Construction complete")), Is.False);
            Assert.That(game.JobCapacity(JobType.Foraging), Is.EqualTo(5));
        }

        [Test]
        public void BuildersSpeedUpConstruction()
        {
            var (game, clock) = Fixtures.NewGame();
            Expect.Success(game.SetWorkers(JobType.Construction, 2)); // speed 1 + 2*0.5 = 2
            var placed = game.PlaceBuilding("shelter", 4, 5);
            Expect.Success(placed);
            game.Run(10, clock);
            Assert.That(game.State.Building(placed.Value).Status, Is.EqualTo(BuildingStatus.Complete));
        }

        [Test]
        public void UpgradeKeepsIdentity()
        {
            var (game, clock) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(wood: 50, stone: 50));
            var shelter = game.State.Buildings.First(b => b.DefinitionId == "shelter");
            int x = shelter.X;
            Expect.Failure(game.UpgradeBuilding(shelter.Id), GameError.LockedByTech("Structural Weaving"));
            game.TestResearch("improved_stone_tools", "structural_weaving");
            Expect.Success(game.UpgradeBuilding(shelter.Id));
            game.Run(30, clock);
            var upgraded = game.State.Building(shelter.Id);
            Assert.That(upgraded.DefinitionId, Is.EqualTo("woven_shelter"));
            Assert.That(upgraded.X, Is.EqualTo(x));
            Assert.That(upgraded.History, Is.EqualTo(new[] { "shelter", "woven_shelter" }));
            Assert.That(game.HousingCapacity(), Is.EqualTo(8));
        }
    }

    public class ResumeTests
    {
        [Test]
        public void RepeatedUpdatesNeverDuplicateProduction()
        {
            var (game, clock) = Fixtures.NewGame();
            clock.Advance(60);
            game.Update();
            double wood = game.State.Resources.Wood;
            // Simulated foreground/background cycles without time passing.
            game.Update(); game.Update(); game.Update();
            Assert.That(game.State.Resources.Wood, Is.EqualTo(wood));
        }

        [Test]
        public void ClockGoingBackwardsGrantsNothing()
        {
            var (game, clock) = Fixtures.NewGame();
            clock.Advance(-3600);
            game.Update();
            Assert.That(game.State.Resources.Wood, Is.EqualTo(25));
            clock.Advance(3600); // back to the original time: only 1h of real progress since re-anchoring
            game.Update();
            // At most one hour of woodcutting (one cutter, 0.1/s), not two.
            Assert.That(game.State.Resources.Wood, Is.GreaterThan(25).And.LessThanOrEqualTo(25 + 360 + 1e-6));
        }

        [Test]
        public void OfflineProgressIsCapped()
        {
            var (game, clock) = Fixtures.NewGame();
            clock.Advance(10 * 24 * 3600);
            game.Update();
            Assert.That(game.State.SimTime, Is.EqualTo(game.Content.Balance.MaxOfflineSeconds).Within(1e-6));
        }
    }

    public class ResearchTests
    {
        [Test]
        public void PrerequisitesEnforced()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(knowledge: 100));
            Expect.Failure(game.StartResearch("structural_weaving"), GameError.MissingPrerequisites(new() { "Improved Stone Tools" }));
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(100));
        }

        [Test]
        public void ResearchCostAndCannotBuyTwice()
        {
            var (game, clock) = Fixtures.NewGame();
            var first = game.StartResearch("improved_stone_tools");
            Assert.That(first.Ok, Is.False);
            Assert.That(first.Error.Kind, Is.EqualTo(GameErrorKind.Unaffordable));
            game.TestGrant(new ResourceBundle(knowledge: 50));
            Expect.Success(game.StartResearch("improved_stone_tools"));
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(50 - game.Content.Technology("improved_stone_tools").Cost.Knowledge).Within(1e-9));
            Expect.Failure(game.StartResearch("improved_stone_tools"), GameError.ResearchInProgress);
            Expect.Failure(game.StartResearch("food_preservation"), GameError.ResearchInProgress);
            game.Run(game.Content.Technology("improved_stone_tools").ResearchSeconds, clock);
            Assert.That(game.GetResearchStatus("improved_stone_tools").State, Is.EqualTo(ResearchState.Researched));
            Expect.Failure(game.StartResearch("improved_stone_tools"), GameError.ResearchAlreadyDone);
            Assert.That(game.RatePerWorker(JobType.Woodcutting), Is.EqualTo(0.125).Within(1e-9));
            // Second tech now possible.
            Expect.Success(game.StartResearch("structural_weaving"));
        }
    }

    public class EventAndMilestoneTests
    {
        /// <summary>Completes the first research objective so the poor season event opens.</summary>
        static (Game, ManualClock) GameWithEvent()
        {
            var (game, clock) = Fixtures.NewGame();
            var tech = game.Content.Technology("food_preservation");
            game.TestGrant(tech.Cost);
            Expect.Success(game.StartResearch("food_preservation"));
            game.Run(tech.ResearchSeconds, clock);
            return (game, clock);
        }

        [Test]
        public void EventTriggersAfterFirstResearch()
        {
            var (game, _) = GameWithEvent();
            Assert.That(game.PendingEvent?.Id, Is.EqualTo("poor_foraging_season"));
        }

        [Test]
        public void StorageOptionRequiresPreparation()
        {
            var (game, _) = GameWithEvent();
            var option = game.PendingEvent.Options.First(o => o.Id == "open_stores");
            Assert.That(game.OptionBlocker(option), Is.Not.Null);
            var r = game.ResolveEvent("open_stores");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error.Kind, Is.EqualTo(GameErrorKind.OptionUnavailable));
            Assert.That(game.PendingEvent, Is.Not.Null);
        }

        [Test]
        public void ForageHarderHasRealConsequences()
        {
            var (game, clock) = GameWithEvent();
            double woodRate = game.RatePerWorker(JobType.Woodcutting);
            Expect.Success(game.ResolveEvent("forage_harder"));
            Assert.That(game.PendingEvent, Is.Null);
            Assert.That(game.RatePerWorker(JobType.Woodcutting), Is.EqualTo(woodRate * 0.5).Within(1e-9));
            Assert.That(game.RatePerWorker(JobType.Foraging), Is.EqualTo(0.12 * 0.7 * 1.6).Within(1e-9));
            Assert.That(game.State.ResolvedEvents["poor_foraging_season"], Is.EqualTo("forage_harder"));
            // Cannot be resolved twice.
            Expect.Failure(game.ResolveEvent("ration"), GameError.NoPendingEvent);
            // Temporary effects expire.
            game.Run(241, clock);
            Assert.That(game.RatePerWorker(JobType.Woodcutting), Is.EqualTo(woodRate).Within(1e-9));
            Assert.That(game.State.Legacy.Any(l => l.Kind == "decision"), Is.True);
        }

        [Test]
        public void RationStopsGrowth()
        {
            var (game, _) = GameWithEvent();
            Expect.Success(game.ResolveEvent("ration"));
            Assert.That(game.Multiplier(EffectKind.GrowthMultiplier), Is.EqualTo(0));
            Assert.That(game.Multiplier(EffectKind.ConsumptionMultiplier), Is.EqualTo(0.75).Within(1e-9));
        }

        [Test]
        public void MilestoneGatingAndTransform()
        {
            var (game, _) = GameWithEvent();
            Assert.That(game.CanClaimMilestone("stable_village"), Is.False);
            var early = game.ClaimMilestone("stable_village");
            Assert.That(early.Ok, Is.False);
            Assert.That(early.Error.Kind, Is.EqualTo(GameErrorKind.RequirementsNotMet));
            Assert.That(game.State.StageId, Is.EqualTo("camp"));

            Expect.Success(game.ResolveEvent("ration"));
            game.TestResearch("improved_stone_tools", "structural_weaving", "permanent_settlement");
            game.AddCompleted("community_circle", 3, 9);
            game.AddCompleted("storage_pit", 4, 4);
            game.AddCompleted("shelter", 4, 5);
            Assert.That(game.CanClaimMilestone("stable_village"), Is.False, "population is still 6");
            game.State.Population = 8;
            Assert.That(game.CanClaimMilestone("stable_village"), Is.False, "the campfire is still at level 1");
            game.State.Buildings.First(b => b.DefinitionId == "campfire").Level = 10;
            Assert.That(game.CanClaimMilestone("stable_village"), Is.True,
                string.Join(", ", game.MilestoneRequirements("stable_village").Where(r => !r.Met).Select(r => r.Text)));

            var campfire = game.State.Buildings.First(b => b.DefinitionId == "campfire");
            Expect.Success(game.ClaimMilestone("stable_village"));
            Assert.That(game.State.StageId, Is.EqualTo("village"));
            var hearth = game.State.Building(campfire.Id);
            Assert.That(hearth.DefinitionId, Is.EqualTo("village_hearth"));
            Assert.That(hearth.History, Is.EqualTo(new[] { "campfire", "village_hearth" }));
            Assert.That(game.State.CompletedObjectives, Does.Contain("obj_village"));
            Expect.Failure(game.ClaimMilestone("stable_village"), GameError.MilestoneAlreadyReached);
        }

        [Test]
        public void ObjectiveRewardGrantedOnce()
        {
            var (game, clock) = Fixtures.NewGame();
            Expect.Success(game.SetWorkers(JobType.Research, 1));
            Assert.That(game.State.CompletedObjectives, Does.Contain("obj_jobs"));
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(4).Within(1e-9));
            game.Run(1, clock);
            Assert.That(game.State.CompletedObjectives.Count(o => o == "obj_jobs"), Is.EqualTo(1));
            Assert.That(game.State.Resources.Knowledge, Is.EqualTo(4.06).Within(1e-9));
        }

        [Test]
        public void ExplorationRevealsLandAndTriggersEvent()
        {
            var (game, clock) = Fixtures.NewGame();
            Assert.That(game.IsRevealed(20, 8), Is.False);
            // Scouts go outward, land by land: the far valley waits until its neighbours are known.
            Expect.Failure(game.StartExpedition("land_3_0"), GameError.RegionNotAdjacent);
            Expect.Success(game.StartExpedition("land_1_0"));
            Expect.Failure(game.StartExpedition("land_0_1"), GameError.ExpeditionInProgress);
            game.Run(game.ExpeditionSeconds(game.Content.Region("land_1_0")), clock);
            Assert.That(game.IsRevealed(20, 8), Is.True);
            Assert.That(game.PendingEvent?.Id, Is.EqualTo("ridge_deposit"));
            Expect.Failure(game.StartExpedition("land_1_0"), GameError.RegionAlreadyExplored);
            // Lands further away cost more and take longer.
            var near = game.Content.Region("land_2_0");
            var far = game.Content.Region("land_7_7");
            Assert.That(game.ExpeditionSeconds(far), Is.GreaterThan(game.ExpeditionSeconds(near)));
        }
    }

    public class SaveTests
    {
        [Test]
        public void RoundTripPreservesState()
        {
            var (game, clock) = Fixtures.NewGame();
            Expect.Success(game.SetWorkers(JobType.Research, 1));
            Expect.Success(game.PlaceBuilding("foraging_camp", 4, 5));
            game.Run(5, clock);
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(Json.Write(loaded.State.ToJson()), Is.EqualTo(Json.Write(game.State.ToJson())));
        }

        [Test]
        public void RestartSimulatesOfflineTimeOnce()
        {
            var (game, clock) = Fixtures.NewGame();
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());

            // App is killed; 60 s later it is relaunched from the save.
            clock.Advance(60);
            var outcome = store.Load(game.Content);
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Loaded));
            var relaunched = new Game(game.Content, outcome.State, clock);
            relaunched.Update();
            Assert.That(relaunched.State.Resources.Wood, Is.EqualTo(31).Within(1e-6));
            relaunched.Update();
            Assert.That(relaunched.State.Resources.Wood, Is.EqualTo(31).Within(1e-6));
        }

        [Test]
        public void FileStoreWritesAndReadsAndRecoversFromCorruption()
        {
            var dir = Path.Combine(Path.GetTempPath(), "founders-tests-" + Guid.NewGuid());
            try
            {
                var store = new FileSaveStore(dir);
                var (game, clock) = Fixtures.NewGame();
                Assert.That(store.Load(game.Content).Result, Is.EqualTo(LoadResult.NoSave));

                store.Save(game.State, game.Content, clock.Now());
                Assert.That(store.Load(game.Content).Result, Is.EqualTo(LoadResult.Loaded));

                File.WriteAllText(store.FilePath, "{ not json");
                Assert.That(store.Load(game.Content).Result, Is.EqualTo(LoadResult.Corrupt));
                // Corrupt file was moved aside; next load starts clean.
                Assert.That(store.Load(game.Content).Result, Is.EqualTo(LoadResult.NoSave));
                Assert.That(File.Exists(store.FilePath + ".corrupt"), Is.True);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void NewerSaveVersionIsRefused()
        {
            var (game, clock) = Fixtures.NewGame();
            var data = SaveCodec.Encode(game.State, game.Content, clock.Now()).Replace("\"saveVersion\":1", "\"saveVersion\":99");
            var outcome = SaveCodec.Decode(data, game.Content);
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.UnsupportedVersion));
            Assert.That(outcome.Version, Is.EqualTo(99));
        }

        [Test]
        public void TamperedSaveIsRejected()
        {
            var (game, clock) = Fixtures.NewGame();
            var bad = game.State.Clone();
            bad.Resources.Wood = -50;
            bad.Assignments[JobType.Foraging] = 40;
            var outcome = SaveCodec.Decode(SaveCodec.Encode(bad, game.Content, clock.Now()), game.Content);
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Corrupt));
            Assert.That(outcome.Reason, Does.Contain("resources"));

            var unknown = game.State.Clone();
            unknown.Buildings[0].DefinitionId = "laser_tower";
            var outcome2 = SaveCodec.Decode(SaveCodec.Encode(unknown, game.Content, clock.Now()), game.Content);
            Assert.That(outcome2.Result, Is.EqualTo(LoadResult.Corrupt));
        }

        [Test]
        public void MissingFieldsAreCorruptNotCrash()
        {
            var outcome = SaveCodec.Decode("{\"saveVersion\":1,\"state\":{}}", Fixtures.Content());
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Corrupt));
        }
    }
}
