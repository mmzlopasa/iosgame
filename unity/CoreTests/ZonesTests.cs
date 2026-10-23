using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Zones painted with the brush: homes built by the villagers, woodcutting and forestry, pastures.</summary>
    public class ZonesTests
    {
        /// <summary>A free spot near the heart where a home could go up now.</summary>
        static (int x, int y) FreeSpot(Game game, string home)
        {
            var heart = game.Heart;
            for (int r = 3; r < 14; r++)
                for (int y = heart.Y - r; y <= heart.Y + r; y++)
                    for (int x = heart.X - r; x <= heart.X + r; x++)
                        if (game.PlacementIssues(home, x, y).Count == 0) return (x, y);
            Assert.Fail("no free spot near the heart");
            return (0, 0);
        }

        [Test]
        public void ZonesArePaintedOnOpenExploredLand()
        {
            var (game, _) = Fixtures.NewGame();
            var (x, y) = FreeSpot(game, "shelter");
            Expect.Success(game.SetZone(x, y, Game.HomesZone));
            Assert.That(game.ZoneAt(x, y), Is.EqualTo(Game.HomesZone));
            Assert.That(game.SetZone(x, y, "castle").Ok, Is.False);
            Expect.Success(game.SetZone(x, y, null));
            Assert.That(game.ZoneAt(x, y), Is.Null);

            var map = game.Content.Map;
            var water = Enumerable.Range(0, map.Width * map.Height).Select(i => (x: i % map.Width, y: i / map.Width))
                                  .FirstOrDefault(c => map.Terrain(c.x, c.y) == TerrainType.Water && game.IsRevealed(c.x, c.y));
            if (map.Terrain(water.x, water.y) == TerrainType.Water)
                Assert.That(game.SetZone(water.x, water.y, Game.HomesZone).Ok, Is.False, "no homes on water");
        }

        [Test]
        public void VillagersBuildAHomeInTheZoneWhenTheHomesAreFull()
        {
            var (game, clock) = Fixtures.NewGame();
            game.State.Resources = new ResourceBundle(food: 1000, wood: 1000, stone: 1000, hides: 200);
            var home = game.ZoneHome();
            Assert.That(home, Is.Not.Null);
            var (x, y) = FreeSpot(game, home.Id);
            for (int dy = 0; dy < home.Footprint.Height; dy++)
                for (int dx = 0; dx < home.Footprint.Width; dx++)
                    Expect.Success(game.SetZone(x + dx, y + dy, Game.HomesZone));

            // Beds to spare: nothing happens.
            game.State.Population = game.HousingCapacity() - 3;
            int before = game.State.Buildings.Count;
            game.Run(120, clock);
            Assert.That(game.State.Buildings.Count, Is.EqualTo(before));

            game.State.Population = game.HousingCapacity();
            game.Run(120, clock);
            var built = game.State.Buildings.Skip(before).ToList();
            Assert.That(built.Count, Is.EqualTo(1));
            Assert.That(built[0].DefinitionId, Is.EqualTo(home.Id));
            Assert.That((built[0].X, built[0].Y), Is.EqualTo((x, y)));
            Assert.That(game.State.Resources.Wood, Is.LessThan(1000), "the villagers pay for it from the stores");
        }

        [Test]
        public void ForestryIsSparedAndWoodcuttingZonesComeFirst()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var cells = game.GatherCells(JobType.Woodcutting);
            Assume.That(cells.Count, Is.GreaterThan(3));
            var last = cells.Last();
            var first = cells.First();
            Expect.Success(game.SetZone(last.x, last.y, Game.WoodcuttingZone));
            Assert.That(game.GatherCells(JobType.Woodcutting).First(), Is.EqualTo(last));
            Expect.Success(game.SetZone(first.x, first.y, Game.ForestryZone));
            Assert.That(game.GatherCells(JobType.Woodcutting), Does.Not.Contain(first));
        }

        [Test]
        public void PasturesGiveTheHerdersRoom()
        {
            var (game, _) = Fixtures.NewGame();
            game.State.Resources = new ResourceBundle(food: 1000, wood: 1000, stone: 1000);
            var (x, y) = FreeSpot(game, "shelter");
            for (int i = 0; i < 8; i++) game.SetZone(x + i % 4, y + i / 4, Game.PastureZone);
            int painted = game.ZoneCells(Game.PastureZone);
            Assume.That(painted, Is.GreaterThanOrEqualTo(4));
            Assert.That(game.JobCapacity(JobType.Herding), Is.EqualTo(0), "no animals kept yet");
            game.TestResearch("animal_husbandry");
            var pen = game.Content.Buildings.First(b => b.Slots(JobType.Herding) > 0);
            game.TestResearch(pen.RequiresTech);
            var (px, py) = FreeSpot(game, pen.Id);
            var b = game.AddCompleted(pen.Id, px, py);
            int own = game.SlotsOf(b, pen, JobType.Herding);
            Assert.That(game.JobCapacity(JobType.Herding), Is.EqualTo(own + painted / 4));
        }

        [Test]
        public void ZonesSurviveSaveAndLoad()
        {
            var (game, clock) = Fixtures.NewGame();
            var (x, y) = FreeSpot(game, "shelter");
            Expect.Success(game.SetZone(x, y, Game.PastureZone));
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(loaded.State.Zones.Single().Value, Is.EqualTo(Game.PastureZone));
        }
    }
}
