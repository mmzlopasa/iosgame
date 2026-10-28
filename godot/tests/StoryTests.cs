using System.Linq;
using Founders.Core;
using NUnit.Framework;

namespace Founders.Tests
{
    public class StoryTests
    {
        [Test]
        public void NewGameOpensTheFirstChapter()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(1));
            Assert.That(game.PendingStory?.Id, Is.EqualTo("ch1_riverside_camp"));
            game.DismissStory();
            Assert.That(game.PendingStory, Is.Null);
        }

        [Test]
        public void TheVillageNeedsTheCampfireAtLevelTen()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var gate = game.MilestoneRequirements("stable_village").Single(r => r.Text.Contains(" level ") && r.Text.EndsWith("/10"));
            Assert.That(gate.Met, Is.False);
            game.State.Buildings.First(b => b.DefinitionId == "campfire").Level = 10;
            Assert.That(game.MilestoneRequirements("stable_village").Single(r => r.Text.Contains(" level ") && r.Text.EndsWith("/10")).Met, Is.True);
        }

        [Test]
        public void ChapterAdvancesWhenItsRequirementsAreMet()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            game.DismissStory();
            game.State.ReachedMilestones.Add("stable_village");
            clock.Advance(1);
            game.Update();
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(2));
            Assert.That(game.PendingStory?.Id, Is.EqualTo("ch2_stable_village"));
            Assert.That(game.State.Legacy.Any(l => l.Title == "Chapter 1: The Riverside Camp"), Is.True);

            // Chapter 2 needs time as well as roads, beauty and people.
            game.DismissStory();
            game.State.Population = 14;
            clock.Advance(1);
            game.Update();
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(2));
            Assert.That(game.ChapterRequirements().Any(r => !r.Met), Is.True);
        }

        [Test]
        public void TheBronzeDawnWaitsForItsChapter()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            var gate = game.MilestoneRequirements("first_civilization").Single(r => r.Text.StartsWith("Reach chapter"));
            Assert.That(gate.Met, Is.False);
            Assert.That(gate.Text, Does.Contain("The Bronze Dawn"));
            game.State.Chapter = 3;
            Assert.That(game.MilestoneRequirements("first_civilization").Single(r => r.Text.StartsWith("Reach chapter")).Met, Is.True);
        }

        [Test]
        public void LockedChaptersNeverAdvance()
        {
            var (game, clock) = Fixtures.NewGame(full: true);
            var locked = game.Content.Chapters.First(c => c.Locked);
            game.State.Chapter = locked.Number;
            clock.Advance(1);
            game.Update();
            Assert.That(game.CurrentChapter.Number, Is.EqualTo(locked.Number));
            Assert.That(locked.Era, Is.EqualTo("Exploration"));
        }

        [Test]
        public void ChapterSurvivesSaveAndLoad()
        {
            var (game, _) = Fixtures.NewGame(full: true);
            game.State.Chapter = 3;
            game.State.PendingStory = "ch3_bronze_dawn";
            var copy = game.State.Clone();
            Assert.That(copy.Chapter, Is.EqualTo(3));
            Assert.That(copy.PendingStory, Is.EqualTo("ch3_bronze_dawn"));
        }
    }
}
