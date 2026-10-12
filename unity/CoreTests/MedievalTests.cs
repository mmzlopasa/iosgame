using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class MedievalTests
    {
        static Game ReadyForTheCharter()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.State.ReachedMilestones.Add("stable_village");
            game.State.ReachedMilestones.Add("first_civilization");
            game.State.EpochId = "epoch_first_civilizations";
            game.State.StageId = "farming_village";
            game.State.Chapter = 5;
            game.State.SimTime = 11 * 4 * game.Content.Balance.SeasonSeconds + 1;
            game.TestResearch("bronze_working", "written_law");
            game.State.Population = 40;
            game.State.Buildings.First(b => b.DefinitionId == "campfire").Level = 30;
            return game;
        }

        [Test]
        public void MedievalTechnologiesWaitForTheirEra()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.TestResearch("written_law");
            var status = game.GetResearchStatus("masonry");
            Assert.That(status.State, Is.EqualTo(ResearchState.Blocked));
            Assert.That(status.Missing, Does.Contain("the Medieval Kingdoms era"));
            Assert.That(game.StartResearch("masonry").Ok, Is.False);
        }

        [Test]
        public void TheCharterOpensTheMiddleAges()
        {
            var game = ReadyForTheCharter();
            Assert.That(game.CanClaimMilestone("the_charter"), Is.False, "needs beauty and roads");
            for (int x = 0; x < 30; x++) game.State.Decorations.Add(new PlacedDecoration { Id = 100 + x, DefinitionId = "dirt_path", X = x, Y = 40 });
            for (int i = 0; i < 15; i++) game.State.Decorations.Add(new PlacedDecoration { Id = 200 + i, DefinitionId = "well", X = i, Y = 50 });
            Assert.That(game.CanClaimMilestone("the_charter"), Is.True,
                string.Join(", ", game.MilestoneRequirements("the_charter").Where(r => !r.Met).Select(r => r.Text)));
            Expect.Success(game.ClaimMilestone("the_charter"));
            Assert.That(game.State.EpochId, Is.EqualTo("epoch_medieval_kingdoms"));
            Assert.That(game.StageName, Is.EqualTo("Chartered Town"));
            Assert.That(game.GetResearchStatus("masonry").State, Is.EqualTo(ResearchState.Available));
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(6), "the charter ends chapter 5");
        }

        [Test]
        public void MedievalBuildingsAreRealBuildings()
        {
            var game = ReadyForTheCharter();
            game.TestResearch("masonry", "ironworking", "watermill", "guilds", "burgher_rights", "monastic_orders");
            foreach (var id in new[] { "medieval_cottage", "medieval_house", "medieval_townhouse", "medieval_market", "medieval_mill", "medieval_smithy", "medieval_church" })
            {
                var def = game.Content.Building(id);
                Assert.That(def, Is.Not.Null, id);
                Assert.That(game.IsUnlocked(def), Is.True, id);
            }
            Assert.That(game.Content.Building("medieval_church").Slots(JobType.Research), Is.EqualTo(3));
            Assert.That(game.Content.Building("medieval_mill").RequiresAdjacentTerrain, Is.EqualTo(TerrainType.Water));
        }

        [Test]
        public void TheLastChaptersFollowTheTownMilestones()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            game.DismissStory();
            game.State.Chapter = 6;
            game.State.ReachedMilestones.Add("market_town");
            clock.Advance(1);
            game.Update();
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(7));
            game.State.ReachedMilestones.Add("walled_town");
            clock.Advance(1);
            game.Update();
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(8));
            Assert.That(game.CurrentChapter.Locked, Is.True);
        }
    }
}
