using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class SpeedupTests
    {
        static void Clear(Game g) => g.State.Speedups.Clear();

        static void Give(Game g, SpeedupKind kind, int minutes, int count) => g.State.Speedups[Speedups.Key(kind, minutes)] = count;

        [Test]
        public void NewGameStartsWithTheStarterPackOnce()
        {
            var (game, _) = Fixtures.NewGame();
            Assert.That(game.SpeedupCount(SpeedupKind.General, 5), Is.EqualTo(3));
            Assert.That(game.SpeedupCount(SpeedupKind.Construction, 5), Is.EqualTo(2));
            Assert.That(game.State.SpeedupStarterGiven, Is.True);
            // Loading the save again does not hand it out a second time.
            var again = new Game(game.Content, game.State.Clone(), new ManualClock());
            Assert.That(again.SpeedupCount(SpeedupKind.General, 5), Is.EqualTo(3));
        }

        [Test]
        public void OldSavesGetTheStarterPackOnLoad()
        {
            var (game, _) = Fixtures.NewGame();
            var json = game.State.ToJson();
            json.Remove("speedups");
            json.Remove("speedupStarterGiven");
            var old = GameState.FromJson(new JObj(Json.Parse(Json.Write(json))));
            Assert.That(old.Speedups, Is.Empty);
            var loaded = new Game(game.Content, old, new ManualClock());
            Assert.That(loaded.SpeedupCount(SpeedupKind.General, 5), Is.EqualTo(3));
        }

        [Test]
        public void SaveRoundTripKeepsSpeedups()
        {
            var (game, _) = Fixtures.NewGame();
            Give(game, SpeedupKind.Exploration, 60, 4);
            var store = new MemorySaveStore();
            store.Save(game.State, game.Content, 0);
            var loaded = store.Load(game.Content);
            Assert.That(loaded.Result, Is.EqualTo(LoadResult.Loaded));
            Assert.That(loaded.State.Speedups[Speedups.Key(SpeedupKind.Exploration, 60)], Is.EqualTo(4));
        }

        [Test]
        public void TamperedSpeedupsAreRejected()
        {
            var (game, _) = Fixtures.NewGame();
            game.State.Speedups["construction_7"] = 1;
            game.State.Speedups["research_5"] = -2;
            var problems = SaveCodec.Validate(game.State, game.Content);
            Assert.That(problems.Any(p => p.Contains("construction_7")), Is.True);
            Assert.That(problems.Any(p => p.Contains("negative")), Is.True);
        }

        [Test]
        public void ResearchSpeedupTakesTimeOffAndFinishesWithReward()
        {
            var (game, clock) = Fixtures.NewGame();
            Clear(game);
            game.TestGrant(new ResourceBundle(knowledge: 50));
            Expect.Success(game.StartResearch("improved_stone_tools"));
            double total = game.State.ActiveResearch.RemainingSeconds;
            Give(game, SpeedupKind.Research, 1, 1);
            Give(game, SpeedupKind.Construction, 60, 1);

            Expect.Success(game.UseSpeedup(SpeedupTask.Research, SpeedupKind.Research, 1));
            Assert.That(game.State.ActiveResearch.RemainingSeconds, Is.EqualTo(total - 60).Within(1e-6));
            Assert.That(game.SpeedupCount(SpeedupKind.Research, 1), Is.EqualTo(0));

            // Wrong kind and none left are refused without spending anything.
            Assert.That(game.UseSpeedup(SpeedupTask.Research, SpeedupKind.Construction, 60).Ok, Is.False);
            Assert.That(game.SpeedupCount(SpeedupKind.Construction, 60), Is.EqualTo(1));
            Assert.That(game.UseSpeedup(SpeedupTask.Research, SpeedupKind.Research, 1).Ok, Is.False);

            Give(game, SpeedupKind.General, 60, 1);
            game.DrainNotices();
            Expect.Success(game.UseSpeedup(SpeedupTask.Research, SpeedupKind.General, 60));
            Assert.That(game.State.ActiveResearch, Is.Null);
            Assert.That(game.State.Researched, Does.Contain("improved_stone_tools"));
            // Discovering it earns research speed-ups.
            Assert.That(game.SpeedupCount(SpeedupKind.Research, 15), Is.EqualTo(1));
            Assert.That(game.DrainNotices().Any(n => n.Message.Contains("Speed-ups earned")), Is.True);
            // Finished jobs cannot be sped up.
            Assert.That(game.UseSpeedup(SpeedupTask.Research, SpeedupKind.Research, 15).Ok, Is.False);
            Assert.That(game.SpeedupCount(SpeedupKind.Research, 15), Is.EqualTo(1));
        }

        [Test]
        public void ConstructionSpeedupFinishesTheBuildingOnceAndRewards()
        {
            var (game, clock) = Fixtures.NewGame();
            Clear(game);
            var placed = game.PlaceBuilding("foraging_camp", 4, 5);
            Expect.Success(placed);
            var task = SpeedupTask.Building(placed.Value);
            Assert.That(game.SpeedupTasks(), Does.Contain(task));
            Assert.That(game.TaskSecondsLeft(task), Is.GreaterThan(0));
            Give(game, SpeedupKind.Construction, 5, 1);
            game.DrainNotices();
            Expect.Success(game.UseSpeedup(task, SpeedupKind.Construction, 5));
            Assert.That(game.State.Building(placed.Value).Status, Is.EqualTo(BuildingStatus.Complete));
            Assert.That(game.SpeedupCount(SpeedupKind.Construction, 1), Is.EqualTo(2), "a new building earns 2× 1 min");
            game.Run(100, clock);
            Assert.That(game.DrainNotices().Count(n => n.Message.Contains("Construction complete")), Is.EqualTo(1));
            Assert.That(game.SpeedupCount(SpeedupKind.Construction, 1), Is.EqualTo(2), "rewarded once");
        }

        [Test]
        public void ExplorationSpeedupBringsTheScoutsHome()
        {
            var (game, _) = Fixtures.NewGame();
            Clear(game);
            Expect.Success(game.StartExpedition("land_1_0"));
            Give(game, SpeedupKind.Exploration, 180, 1);
            Expect.Success(game.UseSpeedup(SpeedupTask.Expedition, SpeedupKind.Exploration, 180));
            Assert.That(game.State.ActiveExpedition, Is.Null);
            Assert.That(game.State.ExploredRegions, Does.Contain("land_1_0"));
            Assert.That(game.SpeedupCount(SpeedupKind.Exploration, 15), Is.EqualTo(1));
        }

        [Test]
        public void FinishPlanPrefersTheJobsOwnKindAndWastesLittle()
        {
            var (game, _) = Fixtures.NewGame();
            Clear(game);
            game.TestGrant(new ResourceBundle(knowledge: 50));
            Expect.Success(game.StartResearch("improved_stone_tools"));
            game.State.ActiveResearch.RemainingSeconds = 17 * 60 + 30; // 17.5 minutes
            Give(game, SpeedupKind.Research, 15, 1);
            Give(game, SpeedupKind.Research, 1, 1);
            Give(game, SpeedupKind.General, 5, 2);
            Give(game, SpeedupKind.General, 60, 1);
            Give(game, SpeedupKind.Construction, 5, 3); // never fits research

            var plan = game.FinishPlan(SpeedupTask.Research);
            // 15 + 1 research, then one general 5 covers the last 1.5 minutes (not the hour).
            Assert.That(plan.Sum(s => s.Minutes * s.Count), Is.EqualTo(21));
            Assert.That(plan.Any(s => s.Kind == SpeedupKind.Construction), Is.False);
            Assert.That(plan.Any(s => s.Minutes == 60), Is.False);
            Assert.That(plan.Single(s => s.Kind == SpeedupKind.General).Count, Is.EqualTo(1));

            Expect.Success(game.UseSpeedupsToFinish(SpeedupTask.Research));
            Assert.That(game.State.Researched, Does.Contain("improved_stone_tools"));
            Assert.That(game.SpeedupCount(SpeedupKind.General, 60), Is.EqualTo(1));
            Assert.That(game.SpeedupCount(SpeedupKind.Construction, 5), Is.EqualTo(3));
        }

        [Test]
        public void FinishPlanUsesEverythingWhenItCannotCover()
        {
            var (game, _) = Fixtures.NewGame();
            Clear(game);
            Expect.Success(game.StartExpedition("land_1_0"));
            Give(game, SpeedupKind.General, 1, 2);
            Expect.Success(game.UseSpeedupsToFinish(SpeedupTask.Expedition));
            Assert.That(game.State.ActiveExpedition, Is.Not.Null);
            Assert.That(game.OwnedSpeedups(), Is.Empty);
            Assert.That(game.UseSpeedupsToFinish(SpeedupTask.Expedition).Ok, Is.False);
        }

        [Test]
        public void KeysParseBack()
        {
            foreach (var kind in Speedups.Kinds)
                foreach (int m in Speedups.Sizes)
                {
                    Assert.That(Speedups.TryParse(Speedups.Key(kind, m), out var k, out var mm), Is.True);
                    Assert.That((k, mm), Is.EqualTo((kind, m)));
                }
            Assert.That(Speedups.TryParse("general_2", out _, out _), Is.False);
            Assert.That(Speedups.TryParse("gold_5", out _, out _), Is.False);
        }
    }
}
