using System;
using System.Collections.Generic;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class MapGeneratorTests
    {
        static int Count(MapDefinition m, char c) => m.Rows.Sum(r => r.Count(ch => ch == c));

        static IEnumerable<MapSettings> Variety()
        {
            foreach (var p in MapPreset.All.Where(p => p.Settings != null)) yield return p.Settings;
            yield return MapSettings.Of(1, 0, 0, 0, 0, 0, 0, 0);
            yield return MapSettings.Of(2, 5, 5, 5, 5, 5, 5, 5);
            yield return MapSettings.Of(3, 0, 0, 5, 5, 0, 5, 0);
            yield return MapSettings.Of(4, 5, 0, 0, 5, 5, 0, 5);
            var rnd = new Random(7);
            for (int i = 0; i < 40; i++)
                yield return MapSettings.Of(rnd.Next(0, MapSettings.SeedRange), rnd.Next(6), rnd.Next(6), rnd.Next(6), rnd.Next(6), rnd.Next(6), rnd.Next(6), rnd.Next(6));
        }

        [Test]
        public void CodeRoundTrips()
        {
            var s = MapSettings.Of(1234567, 3, 2, 0, 4, 5, 1, 2);
            Assert.That(s.Code, Has.Length.EqualTo(13));
            Assert.That(MapSettings.TryParse(s.Code, out var back), Is.True);
            Assert.That(back.Code, Is.EqualTo(s.Code));
            Assert.That(MapSettings.TryParse(s.MapName, out back), Is.True);
            Assert.That(MapSettings.TryParse(" " + s.Code.ToLowerInvariant() + " ", out back), Is.True);
            Assert.That(back.Seed, Is.EqualTo(1234567));
            Assert.That(MapSettings.TryParse("hello", out _), Is.False);
            Assert.That(MapSettings.TryParse("9999999ABCDEF", out _), Is.False, "levels go up to 5");
        }

        [Test]
        public void SameSettingsSameValley()
        {
            var s = MapSettings.Of(42, 3, 3, 2, 3, 3, 3, 3);
            var a = MapGenerator.Generate(s);
            var b = MapGenerator.Generate(MapSettings.Of(42, 3, 3, 2, 3, 3, 3, 3));
            Assert.That(a.Rows, Is.EqualTo(b.Rows));
            var c = MapGenerator.Generate(MapSettings.Of(43, 3, 3, 2, 3, 3, 3, 3));
            Assert.That(c.Rows, Is.Not.EqualTo(a.Rows));
        }

        [Test]
        public void SlidersChangeTheValley()
        {
            MapDefinition With(MapSettings.Feature f, int level)
            {
                var s = MapSettings.Of(99, 2, 2, 0, 2, 2, 2, 2);
                s[f] = level;
                return MapGenerator.Generate(s);
            }
            Assert.That(Count(With(MapSettings.Feature.Forest, 5), 'T'), Is.GreaterThan(Count(With(MapSettings.Feature.Forest, 0), 'T') * 3));
            Assert.That(Count(With(MapSettings.Feature.Hill, 5), 'R'), Is.GreaterThan(Count(With(MapSettings.Feature.Hill, 0), 'R') * 3));
            Assert.That(Count(With(MapSettings.Feature.Mountain, 5), 'M'), Is.GreaterThan(Count(With(MapSettings.Feature.Mountain, 1), 'M')));
            Assert.That(Count(With(MapSettings.Feature.Mountain, 0), 'M'), Is.Zero);
            Assert.That(Count(With(MapSettings.Feature.Lake, 5), '~'), Is.GreaterThan(Count(With(MapSettings.Feature.Lake, 0), '~')));
        }

        [Test]
        public void EveryValleyIsPlayable()
        {
            var root = Fixtures.Content();
            foreach (var s in Variety())
            {
                var content = root.ForMap(s.MapName);
                var map = content.Map;
                string why = $"map {s.Code}";
                Assert.That(map.Name, Is.EqualTo(s.MapName), why);
                Assert.That(map.Width, Is.EqualTo(128), why);
                Assert.That(map.Rows.Count, Is.EqualTo(128), why);
                Assert.That(map.Rows.All(r => r.Length == 128 && r.All(ch => Enums.TerrainFromSymbol(ch) != null)), why);
                Assert.That(Count(map, '~'), Is.GreaterThan(10), why + ": water for fishers and mills");
                Assert.That(Count(map, 'T'), Is.GreaterThan(300), why + ": wood");
                Assert.That(Count(map, 'R'), Is.GreaterThan(150), why + ": stone");
                Assert.That(Count(map, 'O'), Is.GreaterThanOrEqualTo(20), why + ": ore");
                // Forest can be cleared to build (what is built over is gone).
                Assert.That(Count(map, '.') + Count(map, ',') + Count(map, 'T'), Is.GreaterThan(128 * 128 / 3), why + ": room to build");
                Assert.That(content.Regions.Count, Is.EqualTo(65), why);
                Assert.That(content.Regions.Count(r => r.Home), Is.EqualTo(1), why);
                Assert.That(content.Regions.Select(r => r.Name).Distinct().Count(), Is.EqualTo(65), why);
                Assert.That(ContentValidator.Validate(content), Is.Empty, why);

                // The suggested start is a valid founding.
                var game = Game.NewUnfoundedGame(content, new ManualClock());
                var fire = map.StartBuildings.First(b => b.BuildingId == "campfire");
                Assert.That(game.CampfireIssue(fire.X, fire.Y), Is.Null, why);
                var shelter = map.StartBuildings.First(b => b.BuildingId == "shelter");
                Assert.That(game.Found(fire.X, fire.Y, shelter.X, shelter.Y).Error, Is.Null, why);
            }
        }

        [Test]
        public void GeneratedValleySavesAndLoads()
        {
            var root = Fixtures.Content();
            var s = MapPreset.Get("lakeland").Settings;
            var clock = new ManualClock();
            var game = Game.NewUnfoundedGame(root.ForMap(s.MapName), clock);
            var fire = game.Content.Map.StartBuildings[0];
            var shelter = game.Content.Map.StartBuildings[1];
            Assert.That(game.Found(fire.X, fire.Y, shelter.X, shelter.Y).Error, Is.Null);
            game.State.ExploredRegions.Add("land_3_3");

            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            // Loaded with the content as shipped: the valley is drawn again from the name in the save.
            var outcome = store.Load(Fixtures.Content());
            Assert.That(outcome.Result, Is.EqualTo(LoadResult.Loaded), outcome.Reason);
            var loaded = new Game(Fixtures.Content(), outcome.State, clock);
            Assert.That(loaded.Content.Map.Name, Is.EqualTo(s.MapName));
            Assert.That(loaded.Content.Map.Rows, Is.EqualTo(game.Content.Map.Rows));
            Assert.That(loaded.Content.Region("land_3_3").Name, Is.EqualTo(game.Content.Region("land_3_3").Name));
            loaded.Run(60, clock);
        }

        [Test]
        public void HandDrawnValleyIsUnchanged()
        {
            var root = Fixtures.Content();
            Assert.That(root.ForMap(root.Map.Name), Is.SameAs(root));
            Assert.That(root.ForMap(null), Is.SameAs(root));
            var gen = root.ForMap(MapPreset.Get("highlands").Settings.MapName);
            Assert.That(gen.ForMap(root.Map.Name), Is.SameAs(root), "back to the hand-drawn valley from a generated one");
        }

        [Test]
        public void UnknownMapStillRejected()
        {
            var (game, clock) = Fixtures.NewGame();
            game.State.MapName = "gen_nonsense";
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            Assert.That(store.Load(Fixtures.Content()).Result, Is.EqualTo(LoadResult.Corrupt));
        }
    }
}
