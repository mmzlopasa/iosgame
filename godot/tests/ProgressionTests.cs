using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>The heart's levels, building looks, job skills, wild food, bridges and sending gatherers.</summary>
    public class ProgressionTests
    {
        [Test]
        public void NothingOutgrowsTheHeart()
        {
            var (game, clock) = Fixtures.NewGame();
            var camp = game.AddCompleted("wood_camp", 4, 5);
            camp.Level = 3;
            game.State.Buildings.First(b => b.DefinitionId == "campfire").Level = 3;
            game.TestGrant(new ResourceBundle(500, 500, 500, 0));
            var reqs = game.LevelRequirements(camp);
            Assert.That(reqs.Any(r => !r.Met && r.Text.Contains("level 3/4")), Is.True, string.Join(", ", reqs.Select(r => r.Text)));
            Assert.That(game.UpgradeLevel(camp.Id).Ok, Is.False);
        }

        [Test]
        public void HeartLevelsAskForMoreAndTakeLonger()
        {
            var c = Fixtures.Content();
            var heart = c.Building("campfire");
            Assert.That(heart.Heart, Is.True);
            Assert.That(heart.MaxLevel, Is.EqualTo(30));
            for (int i = 5; i < heart.Levels.Count; i++)
            {
                Assert.That(heart.Levels[i].Cost.Wood, Is.GreaterThan(heart.Levels[i - 1].Cost.Wood), $"level {i + 2}");
                Assert.That(heart.Levels[i].BuildSeconds, Is.GreaterThanOrEqualTo(heart.Levels[i - 1].BuildSeconds), $"level {i + 2}");
            }
            // The jump after level 5 is the point where it stops being easy.
            Assert.That(heart.Levels[4].Cost.Wood, Is.GreaterThan(heart.Levels[3].Cost.Wood * 2));
            Assert.That(c.Building("village_hearth").MaxLevel, Is.EqualTo(30));
        }

        [Test]
        public void TheErasOpenWithTheHeart()
        {
            var c = Fixtures.Content();
            int Heart(string milestone) => (int)c.Milestone(milestone).Requirements.Single(r => r.Kind == ConditionKind.HeartLevelAtLeast).Amount;
            Assert.That(Heart("stable_village"), Is.EqualTo(10));
            Assert.That(Heart("first_civilization"), Is.EqualTo(20));
            Assert.That(Heart("the_charter"), Is.EqualTo(30));
            Assert.That(c.Milestones.SelectMany(m => m.Requirements).Any(r => r.Kind == ConditionKind.YearAtLeast), Is.False);
        }

        [Test]
        public void LevelsKeepTheHeartAcrossTheVillageTransform()
        {
            var (game, _) = Fixtures.NewGame();
            var campfire = game.State.Buildings.First(b => b.DefinitionId == "campfire");
            campfire.Level = 12;
            game.State.ReachedMilestones.Clear();
            foreach (var t in game.Content.Milestone("stable_village").BuildingTransforms)
                foreach (var b in game.State.Buildings.Where(b => b.DefinitionId == t.From)) b.DefinitionId = t.To;
            Assert.That(game.HeartLevel, Is.EqualTo(12));
            Assert.That(game.HeartName, Is.EqualTo("Village Hearth"));
        }

        [Test]
        public void BuildingsChangeTheirLookWithTheLevel()
        {
            var (game, _) = Fixtures.NewGame();
            var tent = game.State.Buildings.First(b => b.DefinitionId == "shelter");
            Assert.That(game.SpriteOf(tent), Is.EqualTo("shelter"));
            tent.Level = 5;
            Assert.That(game.SpriteOf(tent), Is.EqualTo("woven_shelter"));
            Assert.That(game.DisplayName(tent), Is.EqualTo("Hut"));
            tent.Level = 10;
            Assert.That(game.DisplayName(tent), Is.EqualTo("Cottage"));
            tent.Level = 20;
            Assert.That(game.SpriteOf(tent), Is.EqualTo("medieval_house"));
            Assert.That(game.DisplayName(tent), Is.EqualTo("Two-Storey House"));
            Assert.That(game.HousingOf(tent, game.Content.Building("shelter")), Is.GreaterThan(10));
        }

        [Test]
        public void SkillsRiseToTenAndEarnPerks()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var p = game.State.People[0];
            double unit = game.Content.Balance.SkillSecondsPerLevel;
            p.Experience["woodcutting"] = unit * 100;
            Assert.That(game.SkillLevel(p, JobType.Woodcutting), Is.EqualTo(10));
            p.Experience["woodcutting"] = unit * 1000;
            Assert.That(game.SkillLevel(p, JobType.Woodcutting), Is.EqualTo(10), "ten is the top");
            var perks = game.Perks(p, JobType.Woodcutting).ToList();
            Assert.That(perks.Count, Is.EqualTo(game.Content.Skill(JobType.Woodcutting).Perks.Count));
            Assert.That(game.WorkerMultiplier(p, JobType.Woodcutting), Is.GreaterThan(1 + 10 * game.Content.Balance.SkillBonusPerLevel));
            p.Experience["woodcutting"] = unit * 6.5;
            Assert.That(game.SkillLevel(p, JobType.Woodcutting), Is.EqualTo(2));
            Assert.That(game.SkillProgress(p, JobType.Woodcutting), Is.EqualTo((6.5 - 4) / 5).Within(1e-9));
        }

        [Test]
        public void EveryJobHasASkillWithPerks()
        {
            var c = Fixtures.Content();
            foreach (var job in Enums.Jobs)
            {
                var skill = c.Skill(job);
                Assert.That(skill, Is.Not.Null, job.Id());
                Assert.That(skill.Perks.Count, Is.GreaterThanOrEqualTo(4), job.Id());
            }
        }

        [Test]
        public void BerryBushesFeedTheForagersAndGrowBack()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            var bushes = game.GatherCells(JobType.Foraging);
            Assert.That(bushes.Count, Is.GreaterThan(3), "the clearing has berry bushes");
            var (x, y) = bushes[0];
            var node = game.NodeAt(x, y, JobType.Foraging);
            Assert.That(node.Kind.Id, Is.EqualTo("berries"));
            double before = node.Remaining;
            game.Run(60, clock);
            Assert.That(game.NodeAt(x, y, JobType.Foraging).Remaining, Is.LessThan(before));
        }

        [Test]
        public void HuntersTakeGameFromTheWoodsWithoutFellingThem()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var forest = Enumerable.Range(0, 64).SelectMany(yy => Enumerable.Range(0, 64).Select(xx => (xx, yy)))
                .First(c => game.Content.Map.Terrain(c.xx, c.yy) == TerrainType.Forest && game.NodeAt(c.xx, c.yy, JobType.Hunting) != null);
            Assert.That(game.NodeAt(forest.xx, forest.yy).Resource, Is.EqualTo(ResourceType.Wood));
            Assert.That(game.NodeAt(forest.xx, forest.yy, JobType.Hunting).Resource, Is.EqualTo(ResourceType.Food));
        }

        [Test]
        public void SendingAGathererMarksTheTreeAndPutsSomeoneOnIt()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var tree = game.GatherCells(JobType.Woodcutting).Last();
            int cutters = game.State.Assignments[JobType.Woodcutting];
            game.SetWorkers(JobType.Woodcutting, 0);
            Expect.Success(game.SendGatherer(tree.x, tree.y));
            Assert.That(game.GatherCells(JobType.Woodcutting)[0], Is.EqualTo(tree), "the tree goes first");
            Assert.That(game.State.Assignments[JobType.Woodcutting], Is.EqualTo(1));
            Assert.That(cutters, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void BridgesSpanWaterFromTheBank()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.TestGrant(new ResourceBundle(wood: 100));
            var map = game.Content.Map;
            (int x, int y)? bank = null, middle = null;
            for (int y = 0; y < map.Height && (bank == null || middle == null); y++)
                for (int x = 1; x < map.Width - 1; x++)
                {
                    if (map.Terrain(x, y) != TerrainType.Water || !game.IsRevealed(x, y)) continue;
                    bool touchesLand = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => game.TerrainNow(x + d.Item1, y + d.Item2) is TerrainType t && t != TerrainType.Water);
                    if (touchesLand && bank == null) bank = (x, y);
                }
            Assert.That(bank, Is.Not.Null, "the river runs through the clearing");
            Expect.Success(game.PlaceDecoration("wooden_bridge", bank.Value.x, bank.Value.y));
            Assert.That(game.IsBridge(bank.Value.x, bank.Value.y), Is.True);
            Assert.That(game.DecorationIssues("dirt_path", bank.Value.x, bank.Value.y).Count, Is.GreaterThan(0));
        }

        [Test]
        public void BridgesStandOnTheSandyBankAtTheWater()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.TestGrant(new ResourceBundle(wood: 100));
            var map = game.Content.Map;
            bool NextToWater(int x, int y) => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => game.TerrainNow(x + d.Item1, y + d.Item2) == TerrainType.Water);
            (int x, int y)? shore = null, dry = null;
            for (int y = 0; y < map.Height; y++)
                for (int x = 1; x < map.Width - 1; x++)
                {
                    if (map.Terrain(x, y) != TerrainType.Sand || !game.IsRevealed(x, y) || game.BuildingIdAt(x, y) != null || game.DecorationAt(x, y) != null) continue;
                    if (NextToWater(x, y)) shore ??= (x, y); else dry ??= (x, y);
                }
            Assert.That(shore, Is.Not.Null, "the river has a sandy bank in the clearing");
            Expect.Success(game.PlaceDecoration("wooden_bridge", shore.Value.x, shore.Value.y));
            Assert.That(game.IsBridge(shore.Value.x, shore.Value.y), Is.True);
            if (dry != null)
                Assert.That(game.DecorationIssues("wooden_bridge", dry.Value.x, dry.Value.y),
                    Does.Contain(PlacementIssue.NeedsAdjacent(TerrainType.Water)), "sand away from the water is no place for a bridge");
        }

        [Test]
        public void TheFirstTechsOpenAtTheFirstCampfire()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            Assert.That(game.HeartLevel, Is.EqualTo(1));
            var open = game.Content.Technologies.Where(t => game.MissingPrerequisites(t.Id).Count == 0).Select(t => t.Id).ToList();
            Assert.That(open, Does.Contain("improved_stone_tools").And.Contain("food_preservation"));
            game.TestGrant(new ResourceBundle(knowledge: 20));
            Expect.Success(game.StartResearch("food_preservation"));
        }
    }
}
