using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class NodeTests
    {
        static (Game game, ManualClock clock) WoodGame()
        {
            var (game, clock) = Fixtures.NewGame(c =>
            {
                c.Balance.ForestWood = 10;
                c.Balance.TreeRegrowSeconds = 600;
                c.Balance.GatherReach = 1;
            });
            return (game, clock);
        }

        static (int x, int y) ForestNextToCamp(Game game)
        {
            for (int y = 0; y < game.Content.Map.Height; y++)
                for (int x = 0; x < game.Content.Map.Width; x++)
                    if (game.TerrainNow(x, y) == TerrainType.Grass && game.IsRevealed(x, y) && game.PlacementIssues("wood_camp", x, y).All(i => i.Kind == PlacementIssueKind.Unaffordable))
                        return (x, y);
            return (-1, -1);
        }

        [Test]
        public void ForestsRunOutAndTheCampStops()
        {
            var (game, clock) = WoodGame();
            var (x, y) = ForestNextToCamp(game);
            game.AddCompleted("wood_camp", x, y);
            game.State.Population = 20;
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 3));
            double reachable = game.ReachableAmount(JobType.Woodcutting);
            Assert.That(reachable, Is.GreaterThan(0).And.LessThanOrEqualTo(9 * 10));
            double before = game.State.Resources.Wood;
            clock.Advance(3600);
            game.Update();
            Assert.That(game.ReachableAmount(JobType.Woodcutting), Is.EqualTo(0).Within(1e-6));
            Assert.That(game.IsExhausted(JobType.Woodcutting), Is.True);
            Assert.That(game.Economy().Production.Wood, Is.EqualTo(0));
            Assert.That(game.State.NodeCleared.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ClearedForestIsOpenLandAndGrowsBack()
        {
            var (game, clock) = WoodGame();
            var (x, y) = ForestNextToCamp(game);
            game.AddCompleted("wood_camp", x, y);
            game.State.Population = 20;
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 3));
            var cell = game.GatherCells(JobType.Woodcutting).First();
            clock.Advance(3600);
            game.Update();
            Assert.That(game.TerrainNow(cell.x, cell.y), Is.EqualTo(TerrainType.Grass));
            Expect.Success(game.SetWorkers(JobType.Woodcutting, 0));
            clock.Advance(700);
            game.Update();
            Assert.That(game.TerrainNow(cell.x, cell.y), Is.EqualTo(TerrainType.Forest));
            Assert.That(game.NodeAt(cell.x, cell.y).Remaining, Is.EqualTo(10));
        }

        [Test]
        public void MarkedTreesAreCutFirst()
        {
            var (game, _) = WoodGame();
            var (x, y) = ForestNextToCamp(game);
            game.AddCompleted("wood_camp", x, y);
            // A forest cell far from the camp, marked by the player.
            var far = Enumerable.Range(0, 21).SelectMany(yy => Enumerable.Range(0, 21).Select(xx => (xx, yy)))
                .First(c => game.TerrainNow(c.xx, c.yy) == TerrainType.Forest && game.IsRevealed(c.xx, c.yy)
                            && !game.GatherCells(JobType.Woodcutting).Contains((c.xx, c.yy)));
            Expect.Success(game.ToggleHarvestOrder(far.xx, far.yy));
            Assert.That(game.NodeAt(far.xx, far.yy).Ordered, Is.True);
            Assert.That(game.GatherCells(JobType.Woodcutting).First(), Is.EqualTo((far.xx, far.yy)));
            Expect.Success(game.ToggleHarvestOrder(far.xx, far.yy));
            Assert.That(game.State.HarvestOrders, Is.Empty);
        }

        [Test]
        public void NodesSurviveSaveAndLoad()
        {
            var (game, _) = WoodGame();
            game.State.NodeTaken[5] = 3.5;
            game.State.NodeCleared[7] = 120;
            game.State.HarvestOrders.Add(9);
            var copy = game.State.Clone();
            Assert.That(copy.NodeTaken[5], Is.EqualTo(3.5));
            Assert.That(copy.NodeCleared[7], Is.EqualTo(120));
            Assert.That(copy.HarvestOrders, Is.EqualTo(new[] { 9 }));
        }
    }
}
