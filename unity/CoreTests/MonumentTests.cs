using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Monuments built part by part: the plan, costs and time, effects, favour and the model name.</summary>
    public class MonumentTests
    {
        static (Game game, ManualClock clock, PlacedBuilding manor) Manor()
        {
            var (game, clock) = Fixtures.NewGame();
            foreach (var job in Enums.Jobs) Expect.Success(game.SetWorkers(job, 0));
            game.TestResearch("written_law");
            var manor = game.AddCompleted("lords_manor", 6, 12);
            game.State.Resources = new ResourceBundle(food: 5000, wood: 2000, stone: 2000, planks: 500, tools: 50);
            return (game, clock, manor);
        }

        static void Finish(Game game, ManualClock clock, PlacedBuilding b)
        {
            for (int i = 0; i < 40 && b.Status != BuildingStatus.Complete; i++) game.Run(600, clock);
            Assert.That(b.Status, Is.EqualTo(BuildingStatus.Complete));
        }

        [Test]
        public void ThePlanSaysWhichPartsCanGoUp()
        {
            var (game, _, manor) = Manor();
            var def = game.Content.Building("lords_manor");
            Assert.That(def.IsMonument, Is.True);
            Assert.That(game.PartStatus(manor, def.Part("great_hall")), Is.EqualTo(Game.PartState.Available));
            Assert.That(game.PartStatus(manor, def.Part("manor_kitchen")), Is.EqualTo(Game.PartState.Locked));
            Assert.That(game.AddPart(manor.Id, "manor_kitchen").Ok, Is.False, "the kitchens join the hall");
            Assert.That(game.AddPart(manor.Id, "moat").Ok, Is.False);
        }

        [Test]
        public void APartCostsItsGoodsTakesItsTimeAndThenStands()
        {
            var (game, clock, manor) = Manor();
            var hall = game.Content.Building("lords_manor").Part("great_hall");
            double wood = game.State.Resources.Wood;
            Expect.Success(game.AddPart(manor.Id, "great_hall"));
            Assert.That(game.State.Resources.Wood, Is.EqualTo(wood - hall.Cost.Wood).Within(1e-9));
            Assert.That(manor.Status, Is.EqualTo(BuildingStatus.Upgrading));
            Assert.That(manor.IsOperational, Is.True, "the plot stays in use while a part goes up");
            Assert.That(game.AddPart(manor.Id, "great_hall").Ok, Is.False, "busy");
            Finish(game, clock, manor);
            Assert.That(manor.Parts, Is.EqualTo(new[] { "great_hall" }));
            Assert.That(manor.PendingPart, Is.Null);
            Assert.That(game.PartStatus(manor, game.Content.Building("lords_manor").Part("manor_kitchen")), Is.EqualTo(Game.PartState.Available));
            Assert.That(game.SpriteOf(manor), Is.EqualTo("lords_manor+great_hall"));
        }

        [Test]
        public void PartsBringTheirEffectsCommunityAndFavour()
        {
            var (game, clock, manor) = Manor();
            var crown = game.Estate("crown");
            double favour = game.FavourPerHour(crown);
            int community = game.ApprovalFactors().Where(f => f.Label == "Gathering places").Sum(f => f.Points);
            double cap = game.StorageCapacity().Coins;
            Expect.Success(game.AddPart(manor.Id, "great_hall"));
            Finish(game, clock, manor);
            Assert.That(game.FavourPerHour(crown), Is.EqualTo(favour + 4).Within(1e-6));
            Assert.That(game.ApprovalFactors().Single(f => f.Label == "Gathering places").Points, Is.EqualTo(community + 3));
            Expect.Success(game.AddPart(manor.Id, "solar_tower"));
            Finish(game, clock, manor);
            Assert.That(game.StorageCapacity().Coins, Is.EqualTo(cap * 1.25).Within(1e-6));
            Assert.That(game.SpriteOf(manor), Is.EqualTo("lords_manor+great_hall+solar_tower"));
        }

        [Test]
        public void PartsSurviveSaveAndLoad()
        {
            var (game, clock, manor) = Manor();
            Expect.Success(game.AddPart(manor.Id, "great_hall"));
            Finish(game, clock, manor);
            Expect.Success(game.AddPart(manor.Id, "manor_kitchen"));
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            var b = loaded.State.Building(manor.Id);
            Assert.That(b.Parts, Is.EqualTo(new[] { "great_hall" }));
            Assert.That(b.PendingPart, Is.EqualTo("manor_kitchen"));
        }

        [Test]
        public void EveryMonumentIsUnlockedAndItsPlanReachesEveryPart()
        {
            var c = Fixtures.Content();
            foreach (var def in c.Buildings.Where(b => b.IsMonument))
            {
                Assert.That(c.Technologies.Any(t => t.Effects.Any(e => e.Kind == EffectKind.UnlockBuilding && e.BuildingId == def.Id)), Is.True, def.Id);
                Assert.That(def.Parts.Count, Is.EqualTo(5), def.Id);
                Assert.That(def.Parts[0].Requires, Is.Empty, $"{def.Id} starts somewhere");
            }
        }
    }
}
