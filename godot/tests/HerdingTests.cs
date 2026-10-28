using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class HerdingTests
    {
        [Test]
        public void HerdersWorkAtPensAndPasturesAndBringFoodAndHides()
        {
            var (game, _) = Fixtures.NewGame();
            game.TestResearch("agriculture", "animal_husbandry");
            Assert.That(game.JobCapacity(JobType.Herding), Is.EqualTo(0));
            game.AddCompleted("animal_pen", 4, 4);
            game.AddCompleted("pasture", 6, 9);
            Assert.That(game.JobCapacity(JobType.Herding), Is.EqualTo(5));
            game.State.Population = 20;
            Expect.Success(game.SetWorkers(JobType.Herding, 4));
            var econ = game.Economy();
            Assert.That(econ.Production.Hides, Is.GreaterThan(0));
            Assert.That(game.Content.Building("pasture").Animals, Does.Contain("cow"));
            Assert.That(game.Content.Building("animal_pen").FoodSource, Is.EqualTo("livestock"));
        }

        [Test]
        public void HerdersBuildASkill()
        {
            var (game, clock) = Fixtures.NewGame();
            game.TestResearch("agriculture", "animal_husbandry");
            game.AddCompleted("animal_pen", 4, 4);
            game.State.Population = 10;
            Expect.Success(game.SetWorkers(JobType.Herding, 2));
            var herder = game.State.People.First(p => p.Job == "herding");
            clock.Advance(game.Content.Balance.SkillSecondsPerLevel * 4 + 1);
            game.Update();
            Assert.That(game.SkillLevel(herder, JobType.Herding), Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TheBarnNeedsAnimalBreeding()
        {
            var (game, _) = Fixtures.NewGame();
            var barn = game.Content.Building("medieval_barn");
            Assert.That(barn.RequiresTech, Is.EqualTo("animal_breeding"));
            Assert.That(barn.Slots(JobType.Herding), Is.EqualTo(4));
            Assert.That(game.Content.Technology("animal_breeding").Epoch, Is.EqualTo("epoch_medieval_kingdoms"));
        }
    }
}
