using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Free layout: rotation, roads, decorations, demolition, levels and skilled people.</summary>
    public class TownTests
    {
        [Test]
        public void RotationSwapsTheFootprint()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestGrant(new ResourceBundle(200, 200, 200, 0));
            game.TestResearch("permanent_settlement", "longhouses");
            var fp = Game.Rotated(new Footprint(2, 1), 1);
            Assert.That((fp.Width, fp.Height), Is.EqualTo((1, 2)));
            Assert.That(game.PlacementIssues("longhouse", 3, 3, 1).Any(i => i.Kind == PlacementIssueKind.Overlaps), Is.False);
        }

        [Test]
        public void RoadsArePlacedInstantlyAndBlockBuildings()
        {
            var (game, _) = Fixtures.NewGame();
            Expect.Success(game.PlaceDecoration("dirt_path", 4, 4));
            Assert.That(game.IsRoad(4, 4), Is.True);
            Assert.That(game.RoadCount(), Is.EqualTo(1));
            Assert.That(game.PlacementIssues("shelter", 4, 4), Does.Contain(PlacementIssue.Overlaps));
            Assert.That(game.State.Legacy.Any(l => l.Title == "The first road"), Is.True);
            var id = game.DecorationAt(4, 4).Id;
            Expect.Success(game.RemoveDecoration(id));
            Assert.That(game.IsRoad(4, 4), Is.False);
        }

        [Test]
        public void RoadAccessMakesWorkplacesFaster()
        {
            var (game, _) = Fixtures.NewGame(c => c.Balance.RoadWorkBonus = 0.1);
            var camp = game.AddCompleted("wood_camp", 4, 5);
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 1));
            double before = game.RatePerWorker(JobType.Woodcutting);
            Expect.Success(game.PlaceDecoration("dirt_path", 4, 6));
            Assert.That(game.HasRoadAccess(camp), Is.True);
            double after = game.RatePerWorker(JobType.Woodcutting);
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(after, Is.LessThanOrEqualTo(before * 1.1 + 1e-9));
        }

        [Test]
        public void DecorationsAddBeautyToApproval()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            int before = game.Approval;
            game.TestGrant(new ResourceBundle(50, 50, 50, 0));
            Expect.Success(game.PlaceDecoration("totem", 4, 4));
            Expect.Success(game.PlaceDecoration("torch_post", 4, 5));
            Assert.That(game.Beauty(), Is.EqualTo(7));
            Assert.That(game.Approval, Is.GreaterThan(before));
        }

        [Test]
        public void LevelsRaiseCapacity()
        {
            var (game, clock) = Fixtures.NewGame();
            var camp = game.AddCompleted("wood_camp", 4, 5);
            int slots = game.JobCapacity(JobType.Woodcutting);
            game.TestGrant(new ResourceBundle(50, 50, 50, 0));
            Expect.Success(game.UpgradeLevel(camp.Id));
            Assert.That(camp.Status, Is.EqualTo(BuildingStatus.Upgrading));
            game.Run(120, clock);
            Assert.That(camp.Level, Is.EqualTo(2));
            Assert.That(game.JobCapacity(JobType.Woodcutting), Is.GreaterThan(slots));
        }

        [Test]
        public void DemolishRefundsHalf()
        {
            var (game, _) = Fixtures.NewGame();
            var camp = game.AddCompleted("foraging_camp", 4, 5);
            double wood = game.State.Resources.Wood;
            Expect.Success(game.DemolishBuilding(camp.Id));
            Assert.That(game.State.Buildings.Contains(camp), Is.False);
            Assert.That(game.State.Resources.Wood, Is.EqualTo(wood + 5).Within(1e-9));
        }

        [Test]
        public void PeopleHaveNamesJobsAndGrowingSkills()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            Assert.That(game.State.People.Count, Is.EqualTo(game.State.Population));
            Assert.That(game.State.People.All(p => !string.IsNullOrEmpty(p.Name)), Is.True);
            int working = game.State.People.Count(p => p.Job != null);
            Assert.That(working, Is.EqualTo(game.State.Assignments.Total));
            var forager = game.State.People.First(p => p.Job == "foraging");
            int level = game.SkillLevel(forager, JobType.Foraging);
            game.Run(600, clock);
            Assert.That(game.SkillLevel(forager, JobType.Foraging), Is.GreaterThan(level));
        }

        [Test]
        public void SkilledWorkersProduceMore()
        {
            var (game, _) = Fixtures.NewGame(c => c.Balance.SkillBonusPerLevel = 0.1);
            foreach (var job in Enums.Jobs) game.SetWorkers(job, 0);
            game.AddCompleted("wood_camp", 4, 5);
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 1));
            double novice = game.Economy().Production.Wood;
            var cutter = game.State.People.First(p => p.Job == "woodcutting");
            cutter.Experience["woodcutting"] = game.Content.Balance.SkillSecondsPerLevel * 9; // level 3
            Assert.That(game.Economy().Production.Wood, Is.GreaterThan(novice));
        }

        [Test]
        public void OldSavesWithoutPeopleGetThem()
        {
            var (game, clock) = Fixtures.NewGame();
            var json = Json.Write(game.State.ToJson());
            var o = new JObj(Json.Parse(json));
            var state = GameState.FromJson(o);
            state.People.Clear();
            var loaded = new Game(game.Content, state, clock);
            Assert.That(loaded.State.People.Count, Is.EqualTo(loaded.State.Population));
        }

        [Test]
        public void HuntersBringHidesAndMinersOre()
        {
            var (game, _) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) game.SetWorkers(job, 0);
            game.TestResearch("improved_stone_tools");
            game.AddCompleted("hunters_lodge", 4, 5);
            game.AddCompleted("mine", 5, 5);
            Expect.Success(game.SetWorkers(JobType.Hunting, 1));
            Expect.Success(game.SetWorkers(JobType.Mining, 1));
            var p = game.Economy().Production;
            Assert.That(p.Hides, Is.GreaterThan(0));
            Assert.That(p.Ore, Is.GreaterThan(0));
        }

        [Test]
        public void YearConditionWaitsForTime()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var c = new Condition(ConditionKind.YearAtLeast) { Amount = 2 };
            Assert.That(game.IsMet(c), Is.False);
            game.State.SimTime = game.Content.Balance.SeasonSeconds * 4 + 1;
            Assert.That(game.IsMet(c), Is.True);
        }
    }
}
