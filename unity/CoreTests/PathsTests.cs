using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    /// <summary>Paths worn by feet: walking wears the ground, trodden cells help building sites, unused paths fade.</summary>
    public class PathsTests
    {
        [Test]
        public void FeetWearPathsFromTheHeartToTheHomes()
        {
            var (game, clock) = Fixtures.NewGame();
            Assert.That(game.TroddenCells(), Is.Empty);
            // A home out in the meadow, its family walking to the fire and back.
            var heart = game.Heart;
            var home = game.AddCompleted("shelter", heart.X + 7, heart.Y + 1);
            game.State.Population = game.HousingCapacity();
            game.Run(3 * 3600, clock);
            var trodden = game.TroddenCells().ToList();
            Assert.That(trodden, Is.Not.Empty, "three hours of walking leave paths");
            Assert.That(trodden.Any(c => c.x > heart.X && c.x < home.X), Is.True, "between the fire and the new home");
            Assert.That(game.PathsVersion, Is.GreaterThan(0));
            // No path runs under a building.
            foreach (var b in game.State.Buildings)
            {
                var fp = game.FootprintOf(b);
                Assert.That(trodden.Any(c => c.x >= b.X && c.x < b.X + fp.Width && c.y >= b.Y && c.y < b.Y + fp.Height), Is.False, b.DefinitionId);
            }
            Assert.That(game.State.Wear.Values.Max(), Is.LessThanOrEqualTo(game.Content.Balance.Paths.MaxWear + 1e-9));
        }

        [Test]
        public void SitesBesideAPathAreBuiltSooner()
        {
            var (game, _) = Fixtures.NewGame();
            var site = game.State.Buildings.First();
            Assert.That(game.BesidePath(site), Is.False);
            int w = game.Content.Map.Width;
            game.State.Wear[site.Y * w + site.X - 1] = game.Content.Balance.Paths.Threshold;
            Assert.That(game.IsTrodden(site.X - 1, site.Y), Is.True);
            Assert.That(game.BesidePath(site), Is.True);
        }

        [Test]
        public void UnusedPathsGrassOver()
        {
            var (game, clock) = Fixtures.NewGame();
            game.State.Population = 0;
            int w = game.Content.Map.Width;
            // A lone path far from anyone's way.
            int cell = 2 * w + 2;
            game.State.Wear[cell] = game.Content.Balance.Paths.MaxWear;
            Assert.That(game.IsTrodden(2, 2), Is.True);
            for (int h = 0; h < 100; h++) game.Run(3600, clock); // one hour at a time (a longer absence is capped)
            Assert.That(game.IsTrodden(2, 2), Is.False);
        }

        [Test]
        public void WearSurvivesSaveAndLoad()
        {
            var (game, clock) = Fixtures.NewGame();
            game.Run(2 * 3600, clock);
            Assume.That(game.State.Wear, Is.Not.Empty);
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, clock.Now());
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded), loaded.Reason);
            Assert.That(loaded.State.Wear.Count, Is.EqualTo(game.State.Wear.Count));
            foreach (var kv in game.State.Wear) Assert.That(loaded.State.Wear[kv.Key], Is.EqualTo(kv.Value).Within(1e-3));
        }
    }
}
