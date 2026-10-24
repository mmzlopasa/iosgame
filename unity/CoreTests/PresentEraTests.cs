using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>The second wave of the playable eras: planks, tools, bread and buildings with lasting effects.</summary>
    public class PresentEraTests
    {
        /// <summary>A settlement with nobody at work, the given buildings finished and the given stock.</summary>
        static (Game game, ManualClock clock) Town(ResourceBundle stock, params string[] buildings)
        {
            var (game, clock) = Fixtures.NewGame();
            int x = 6;
            foreach (var id in buildings)
            {
                game.TestResearch(game.Content.Building(id).RequiresTech);
                game.AddCompleted(id, x, 12);
                x += 3;
            }
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.State.Population = 20;
            game.State.Resources = stock;
            return (game, clock);
        }

        [Test]
        public void SawyersTurnTwoWoodIntoAPlank()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500, wood: 100), "sawpit");
            Expect.Success(game.SetWorkers(JobType.Sawing, 2));
            Assert.That(game.Processing(JobType.Sawing).State, Is.EqualTo(ProcessingState.Working));
            game.Run(100, clock);
            double planks = 2 * 0.05 * 100;
            Assert.That(game.State.Resources.Planks, Is.EqualTo(planks).Within(1e-6));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(100 - 2 * planks).Within(1e-6));
            Assert.That(game.Made(ResourceType.Planks), Is.EqualTo(planks).Within(1e-6));
        }

        [Test]
        public void ToolsmithsNeedBronzeAndPlanks()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500, bronze: 10), "toolsmith");
            Expect.Success(game.SetWorkers(JobType.Toolmaking, 2));
            var status = game.Processing(JobType.Toolmaking);
            Assert.That(status.State, Is.EqualTo(ProcessingState.MissingInput));
            Assert.That(status.Missing, Is.EqualTo(ResourceType.Planks));
            game.Run(50, clock);
            Assert.That(game.State.Resources.Tools, Is.EqualTo(0));
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(10), "no bronze is spent without planks");

            game.TestGrant(new ResourceBundle(planks: 10));
            game.Run(100, clock);
            double tools = 2 * 0.03 * 100;
            Assert.That(game.State.Resources.Tools, Is.EqualTo(tools).Within(1e-6));
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(10 - tools / 2).Within(1e-6));
            Assert.That(game.State.Resources.Planks, Is.EqualTo(10 - tools / 2).Within(1e-6));
        }

        [Test]
        public void ToolsInStoreMakeGatherersFasterAndWearOut()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500), "wood_camp");
            int cutters = game.JobCapacity(JobType.Woodcutting);
            Assume.That(cutters, Is.GreaterThan(0));
            Expect.Success(game.SetWorkers(JobType.Woodcutting, cutters));
            double bare = game.RatePerWorker(JobType.Woodcutting);
            Assert.That(game.ToolBonus, Is.EqualTo(0));

            game.TestGrant(new ResourceBundle(tools: 1));
            Assert.That(game.ToolBonus, Is.EqualTo(0.15).Within(1e-9));
            Assert.That(game.RatePerWorker(JobType.Woodcutting), Is.EqualTo(bare * 1.15).Within(1e-9));
            Assert.That(game.RatePerWorker(JobType.Research), Is.EqualTo(game.RatePerWorker(JobType.Research)), "only gatherers use tools");

            double wear = cutters * game.Content.Balance.ToolWearPerWorkerPerSecond;
            game.Run(10, clock);
            Assert.That(game.State.Resources.Tools, Is.EqualTo(1 - wear * 10).Within(1e-6));

            // Worn out to the last one: never below zero, and the bonus goes with them.
            game.Run(2 / wear, clock);
            Assert.That(game.State.Resources.Tools, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.State.Resources.IsValid, Is.True);
            Assert.That(game.ToolBonus, Is.EqualTo(0));
        }

        [Test]
        public void BakersBakeFlourIntoFood()
        {
            var (game, clock) = Town(new ResourceBundle(food: 100, wood: 100, flour: 100), "medieval_bakery");
            Assert.That(game.JobCapacity(JobType.Baking), Is.EqualTo(3));
            Expect.Success(game.SetWorkers(JobType.Baking, 3));
            game.Run(100, clock);
            double bread = 3 * 0.3 * 100;
            Assert.That(game.Made(ResourceType.Food), Is.EqualTo(bread).Within(1e-6));
            Assert.That(game.State.Resources.Flour, Is.EqualTo(100 - bread * 0.3).Within(1e-6));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(100 - bread * 0.1).Within(1e-6));
        }

        [Test]
        public void MillersGrindGrainIntoFlourAndTheBreadGoesFurther()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500, wood: 100), "medieval_mill");
            Assume.That(game.JobCapacity(JobType.Milling), Is.EqualTo(2));
            Expect.Success(game.SetWorkers(JobType.Milling, 2));
            double food = game.State.Resources.Food;
            game.Run(100, clock);
            double flour = 2 * 0.12 * 100;
            Assert.That(game.State.Resources.Flour, Is.EqualTo(flour).Within(1e-6));
            Assert.That(game.State.Resources.Food, Is.LessThan(food - flour + 1e-6), "the mill takes its grain from the stores");
            // Each Flour bakes into more than three Food.
            var baking = game.Content.JobConfig(JobType.Baking);
            Assert.That(1 / baking.Inputs.Flour, Is.GreaterThan(3));
        }

        [Test]
        public void BreadAndHoneyCountForFoodVariety()
        {
            var (game, _) = Town(new ResourceBundle(food: 500), "apiary", "medieval_bakery");
            var variety = game.ApprovalFactors().FirstOrDefault(f => f.Label.StartsWith("Food variety"));
            Assert.That(variety, Is.Not.Null);
            Assert.That(variety.Label, Does.Contain("2 kinds"));
        }

        [Test]
        public void FinishedBuildingsGiveTheirLastingEffects()
        {
            var (game, _) = Town(new ResourceBundle(food: 500));
            Assert.That(game.Multiplier(EffectKind.SicknessMultiplier), Is.EqualTo(1));
            game.TestResearch("herbal_healing");
            var garden = game.AddCompleted("herb_garden", 6, 12);
            Assert.That(game.Multiplier(EffectKind.SicknessMultiplier), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(game.Multiplier(EffectKind.RecoveryMultiplier), Is.EqualTo(1.1).Within(1e-9));
            game.AddCompleted("herb_garden", 9, 12);
            Assert.That(game.Multiplier(EffectKind.SicknessMultiplier), Is.EqualTo(0.81).Within(1e-9));

            // A garden still being dug does nothing yet.
            garden.Status = BuildingStatus.Constructing;
            Assert.That(game.Multiplier(EffectKind.SicknessMultiplier), Is.EqualTo(0.9).Within(1e-9));
            game.State.Buildings.Remove(garden);
            Assert.That(game.Multiplier(EffectKind.SicknessMultiplier), Is.EqualTo(0.9).Within(1e-9));
        }

        [Test]
        public void TheLookoutSpeedsUpTheScouts()
        {
            var (game, _) = Town(new ResourceBundle(food: 500));
            var region = game.Content.Regions.First(r => r.Id != game.Content.Regions[0].Id);
            double before = game.ExpeditionSeconds(region);
            game.TestResearch("trail_signs");
            Assert.That(game.ExpeditionSeconds(region), Is.EqualTo(before / 1.1).Within(1e-6));
            game.AddCompleted("lookout_tower", 6, 12);
            Assert.That(game.ExpeditionSeconds(region), Is.EqualTo(before / 1.1 / 1.25).Within(1e-6));
        }

        [Test]
        public void NewGoodsSurviveSaveAndLoad()
        {
            var (game, clock) = Town(new ResourceBundle(food: 500, wood: 100, bronze: 5), "sawpit", "toolsmith");
            Expect.Success(game.SetWorkers(JobType.Sawing, 2));
            Expect.Success(game.SetWorkers(JobType.Toolmaking, 2));
            game.Run(200, clock);
            Assume.That(game.State.Resources.Tools, Is.GreaterThan(0));
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(loaded.State.Resources, Is.EqualTo(game.State.Resources));
            Assert.That(loaded.State.Assignments[JobType.Toolmaking], Is.EqualTo(2));
            Assert.That(Json.Write(loaded.State.ToJson()), Is.EqualTo(Json.Write(game.State.ToJson())));
        }

        [Test]
        public void TheGoalsGoOnAfterTheVillage()
        {
            var c = Fixtures.Content();
            var ids = c.Objectives.Select(o => o.Id).ToList();
            int village = ids.IndexOf("obj_village");
            Assert.That(ids.Skip(village + 1), Is.EqualTo(new[] { "obj_smokehouse", "obj_ancestors", "obj_planks", "obj_tools", "obj_bread", "obj_tavern" }));
            // Every new building can be reached by research.
            foreach (var id in new[] { "lookout_tower", "herb_garden", "smokehouse", "ancestor_shrine", "sawpit", "apiary", "toolsmith",
                                       "medieval_bakery", "medieval_carpenter", "medieval_tavern", "medieval_scriptorium", "medieval_warehouse", "medieval_bathhouse" })
            {
                var def = c.Building(id);
                Assert.That(def, Is.Not.Null, id);
                Assert.That(def.Buildable, Is.True, id);
                Assert.That(c.Technologies.Any(t => t.Effects.Any(e => e.Kind == EffectKind.UnlockBuilding && e.BuildingId == id)), Is.True, $"{id} is unlocked by research");
            }
        }
    }
}
