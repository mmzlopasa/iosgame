using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Recipe jobs: the copper smelter turns 2 ore and 1 wood into 1 bronze.</summary>
    public class RecipeTests
    {
        const double Rate = 0.02; // bronze per smelter per second (content.json, balance.jobs.smelting)

        /// <summary>A settlement with a finished smelter, nobody else at work and the given stock.</summary>
        static (Game game, ManualClock clock) Smelter(double ore, double wood, int smelters = 2)
        {
            var (game, clock) = Fixtures.NewGame();
            game.TestResearch("copper_smelting");
            game.AddCompleted("copper_smelter", 8, 8);
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.State.Resources = new ResourceBundle(food: 500, wood: wood, ore: ore);
            if (smelters > 0) Expect.Success(game.SetWorkers(JobType.Smelting, smelters));
            return (game, clock);
        }

        [Test]
        public void SmeltersCastBronzeFromOreAndWood()
        {
            var (game, clock) = Smelter(ore: 100, wood: 50);
            Assert.That(game.Processing(JobType.Smelting).State, Is.EqualTo(ProcessingState.Working));
            game.Run(100, clock);
            double bronze = 2 * Rate * 100;
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(bronze).Within(1e-6));
            Assert.That(game.State.Resources.Ore, Is.EqualTo(100 - 2 * bronze).Within(1e-6));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(50 - bronze).Within(1e-6));
            Assert.That(game.Made(ResourceType.Bronze), Is.EqualTo(bronze).Within(1e-6));
        }

        [Test]
        public void NothingIsMadeWithoutTheInputs()
        {
            var (game, clock) = Smelter(ore: 0, wood: 50);
            var status = game.Processing(JobType.Smelting);
            Assert.That(status.State, Is.EqualTo(ProcessingState.MissingInput));
            Assert.That(status.Missing, Is.EqualTo(ResourceType.Ore));
            game.Run(100, clock);
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(0));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(50).Within(1e-9), "no wood is burnt for nothing");

            var (noWood, clock2) = Smelter(ore: 50, wood: 0);
            Assert.That(noWood.Processing(JobType.Smelting).Missing, Is.EqualTo(ResourceType.Wood));
            noWood.Run(100, clock2);
            Assert.That(noWood.State.Resources.Ore, Is.EqualTo(50).Within(1e-9));
        }

        [Test]
        public void ScarceInputsAreUsedUpExactlyAndNeverGoNegative()
        {
            var (game, clock) = Smelter(ore: 3, wood: 50);
            game.Run(100, clock);
            Assert.That(game.State.Resources.Ore, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(48.5).Within(1e-6));
            Assert.That(game.State.Resources.IsValid, Is.True);
            Assert.That(game.Processing(JobType.Smelting).State, Is.EqualTo(ProcessingState.MissingInput));
        }

        [Test]
        public void AFullStoreStopsTheFurnace()
        {
            var (game, clock) = Smelter(ore: 100, wood: 50);
            double cap = game.StorageCapacity().Bronze;
            var res = game.State.Resources;
            res.Bronze = cap - 0.5;
            game.State.Resources = res;
            game.Run(100, clock);
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(cap).Within(1e-6));
            Assert.That(game.State.Resources.Ore, Is.EqualTo(99).Within(1e-6), "only what fitted was smelted");
            var status = game.Processing(JobType.Smelting);
            Assert.That(status.State, Is.EqualTo(ProcessingState.OutputFull));
            Assert.That(status.Rate, Is.EqualTo(0));
        }

        [Test]
        public void NoSmeltersNoWork()
        {
            var (game, clock) = Smelter(ore: 100, wood: 50, smelters: 0);
            Assert.That(game.Processing(JobType.Smelting).State, Is.EqualTo(ProcessingState.NoWorkers));
            game.Run(100, clock);
            Assert.That(game.State.Resources.Ore, Is.EqualTo(100));
            Assert.That(game.JobCapacity(JobType.Smelting), Is.EqualTo(2), "the smelter has two places");
            Expect.Failure(game.SetWorkers(JobType.Smelting, 3), GameError.NoJobSlots(2));
        }

        [Test]
        public void ALongAbsenceKeepsTheRecipeExact()
        {
            // Hours away are simulated in coarse steps; the recipe must still balance to the last unit.
            var (game, clock) = Smelter(ore: 10, wood: 100);
            game.Run(3 * 3600, clock);
            Assert.That(game.State.Resources.Bronze, Is.EqualTo(5).Within(1e-6));
            Assert.That(game.State.Resources.Ore, Is.EqualTo(0).Within(1e-9));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(95).Within(1e-6));
        }

        [Test]
        public void FirewoodComesBeforeTheFurnace()
        {
            // With every system on, the homes' fuel is taken first; the smelters only get what is left.
            var (game, clock) = Fixtures.NewGame(full: true);
            game.TestResearch("copper_smelting");
            game.AddCompleted("copper_smelter", 8, 8);
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.State.SimTime = game.Content.Balance.SeasonSeconds * 3 + 1; // winter
            double fuel = game.FuelPerSecond;
            Assume.That(fuel, Is.GreaterThan(0));
            game.State.Resources = new ResourceBundle(food: 500, wood: fuel * 2, ore: 100);
            Expect.Success(game.SetWorkers(JobType.Smelting, 2));
            for (int i = 0; i < 20; i++)
            {
                game.Run(1, clock);
                Assert.That(game.State.Resources.IsValid, Is.True);
            }
            Assert.That(game.State.Resources.Wood, Is.EqualTo(0).Within(1e-6));
            Assert.That(game.Made(ResourceType.Bronze), Is.LessThan(2 * Rate * 20), "the furnace went cold with the woodpile");
            Assert.That(game.State.Resources.Ore, Is.EqualTo(100 - 2 * game.Made(ResourceType.Bronze)).Within(1e-6), "ore and bronze balance");
        }

        [Test]
        public void MadeGoodsSurviveSaveAndCountForGoals()
        {
            var (game, clock) = Smelter(ore: 100, wood: 50);
            game.Run(100, clock);
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(Json.Write(loaded.State.ToJson()), Is.EqualTo(Json.Write(game.State.ToJson())));
            var again = new Game(game.Content, loaded.State, clock);
            Assert.That(again.Made(ResourceType.Bronze), Is.EqualTo(4).Within(1e-6));

            var goal = new Condition(ConditionKind.MadeAtLeast) { Resource = ResourceType.Bronze, Amount = 20 };
            Assert.That(again.IsMet(goal), Is.False);
            Assert.That(again.Describe(goal), Is.EqualTo("Make Bronze 4/20"));
            again.State.Made["bronze"] = 20;
            Assert.That(again.IsMet(goal), Is.True);
        }

        [Test]
        public void TheBronzeDawnAsksForCastBronze()
        {
            var c = Fixtures.Content();
            var dawn = c.Milestone("first_civilization");
            Assert.That(dawn.Requirements.Any(r => r.Kind == ConditionKind.MadeAtLeast && r.Resource == ResourceType.Bronze), Is.True);
            Assert.That(c.Building("bronze_workshop").Cost.Bronze, Is.GreaterThan(0), "the workshop is built with bronze");
        }

        [Test]
        public void TheValidatorRejectsBrokenRecipes()
        {
            var c = Fixtures.Content();
            var smelting = c.JobConfig(JobType.Smelting);
            smelting.Inputs = new ResourceBundle(ore: 2, bronze: 1);
            Assert.That(ContentValidator.Validate(c).Any(p => p.Contains("uses its own output")), Is.True);
        }
    }
}
