using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class OwnMapTests
    {
        /// <summary>A small valley: a river down the middle, woods to the west, stony hills and a mountain to the east.</summary>
        static List<string> Rows()
        {
            var rows = new List<string>();
            for (int y = 0; y < 128; y++)
            {
                var r = new char[128];
                for (int x = 0; x < 128; x++)
                    r[x] = x >= 60 && x < 64 ? '~' : x < 20 ? 'T' : x > 110 ? 'M' : x > 100 && y < 40 ? 'R' : '.';
                rows.Add(new string(r));
            }
            return rows;
        }

        [Test]
        public void OwnValleyStartsNewGamesAndOldSavesKeepTheirValley()
        {
            var root = Fixtures.Content();
            var clock = new ManualClock();
            // A save from before: the hand-drawn valley.
            var old = Game.NewGame(root, clock);
            var store = new MemorySaveStore();
            store.Save(old.State, old.Content, clock.Now());

            root.UseOwnMap(OwnMap.Build(Rows(), (40, 64), "Green Hollow"));
            Assert.That(root.ForNewGame.Map.Name, Is.EqualTo(OwnMap.Name));
            Assert.That(root.ForNewGame.Map.Title, Is.EqualTo("Green Hollow"));
            Assert.That(root.ForNewGame.Regions.Count(r => r.Home), Is.EqualTo(1));
            Assert.That(root.ForNewGame.Regions.Count, Is.EqualTo(65));

            var loaded = store.Load(root);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(new Game(root, loaded.State, clock).Content.Map.Name, Is.EqualTo(root.Map.Name));

            // A new game on the own valley founds where the map suggests and survives a save.
            var game = Game.NewGame(root.ForNewGame, clock);
            var fire = game.State.Buildings.First(b => b.DefinitionId == "campfire");
            Assert.That((fire.X, fire.Y), Is.EqualTo((40, 64)));
            store.Save(game.State, game.Content, clock.Now());
            loaded = store.Load(root);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(new Game(root, loaded.State, clock).Content.Map.Name, Is.EqualTo(OwnMap.Name));
        }

        [Test]
        public void BadSettleSpotOrOddSizeStillGivesAPlayableValley()
        {
            var rows = Rows().Take(50).Select(r => r.Substring(0, 90) + "xyz").ToList();
            var map = OwnMap.Build(rows, (61, 10)); // the marker sits in the river
            Assert.That(map.Width, Is.EqualTo(128));
            Assert.That(map.Rows.All(r => r.Length == 128 && r.All(c => Enums.TerrainFromSymbol(c).HasValue)));
            var fire = map.StartBuildings.First(b => b.BuildingId == "campfire");
            Assert.That(map.Terrain(fire.X, fire.Y), Is.EqualTo(TerrainType.Grass));
            var content = OwnMap.ContentFor(Fixtures.Content(), map);
            var game = Game.NewGame(content, new ManualClock());
            Assert.That(game.State.Founded, Is.True);
        }

        [Test]
        public void GeneratedValleysFromOldSavesStillLoad()
        {
            var root = Fixtures.Content();
            var clock = new ManualClock();
            var gen = Game.NewGame(root.ForMap(MapSettings.Of(99, 3, 2, 0, 3, 3, 3, 2).MapName), clock);
            var store = new MemorySaveStore();
            store.Save(gen.State, gen.Content, clock.Now());
            root.UseOwnMap(OwnMap.Build(Rows()));
            var loaded = store.Load(root);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
        }
    }
}
