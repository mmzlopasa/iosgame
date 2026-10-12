using System;
using System.IO;
using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    static class Fixtures
    {
        static string contentJson;

        public static string ContentJson =>
            contentJson ??= File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "content.json"));

        /// <summary>Fresh, validated copy of the shipped content (safe to mutate per test).</summary>
        public static GameContent Content() => GameContent.Load(ContentJson);

        /// <summary>
        /// A new game. By default seasons and approval effects are switched off so rule tests can
        /// check exact rates; <paramref name="full"/> plays with every system on (as shipped).
        /// </summary>
        public static (Game game, ManualClock clock) NewGame(Action<GameContent> configure = null, bool full = false)
        {
            var c = Content();
            if (!full)
            {
                c.Balance.SeasonSeconds = 0;
                c.Balance.ApprovalAffectsWork = false;
                c.Balance.SkillBonusPerLevel = 0;
                c.Balance.RoadWorkBonus = 0;
                c.Balance.ForestWood = c.Balance.RockStone = c.Balance.VeinOre = 0; // endless land (NodeTests cover it)
                c.Balance.Nodes.Clear();
                foreach (var t in c.Technologies) t.HeartLevel = null; // research rules without the heart's pacing
            }
            configure?.Invoke(c);
            var clock = new ManualClock();
            return (Game.NewGame(c, clock), clock);
        }
    }

    static class TestExtensions
    {
        /// <summary>Give resources directly.</summary>
        public static void TestGrant(this Game g, ResourceBundle r) => g.State.Resources = g.State.Resources + r;
        /// <summary>Mark technologies researched.</summary>
        public static void TestResearch(this Game g, params string[] ids) => g.State.Researched.AddRange(ids);
        /// <summary>Run time forward through the clock (the real path the app uses).</summary>
        public static void Run(this Game g, double seconds, ManualClock clock)
        {
            clock.Advance(seconds);
            g.Update();
        }

        public static PlacedBuilding AddCompleted(this Game g, string def, int x, int y, int? id = null)
        {
            var b = new PlacedBuilding
            {
                Id = id ?? g.State.NextBuildingId, DefinitionId = def, X = x, Y = y, Status = BuildingStatus.Complete,
                RemainingWork = 0, TotalWork = 1, History = { def }, FoundedAtSimTime = 0,
            };
            g.State.Buildings.Add(b);
            if (id == null) g.State.NextBuildingId += 1;
            return b;
        }
    }

    static class Expect
    {
        public static void Success(GameResult r) => Assert.That(r.Ok, Is.True, $"Expected success, got {r}");
        public static void Success<T>(GameResult<T> r) => Assert.That(r.Ok, Is.True, $"Expected success, got {r}");

        public static void Failure(GameResult r, GameError expected)
        {
            Assert.That(r.Ok, Is.False, $"Expected failure {expected}, got success");
            Assert.That(r.Error, Is.EqualTo(expected));
        }

        public static void Failure<T>(GameResult<T> r, GameError expected)
        {
            Assert.That(r.Ok, Is.False, $"Expected failure {expected}, got success");
            Assert.That(r.Error, Is.EqualTo(expected));
        }

        public static void Issues(System.Collections.Generic.List<PlacementIssue> actual, params PlacementIssue[] expected) =>
            Assert.That(actual.ToArray(), Is.EqualTo(expected), string.Join(", ", actual));
    }
}
