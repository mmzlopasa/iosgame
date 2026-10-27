using System.Collections.Generic;
using System.Linq;

namespace Founders.Core
{
    /// <summary>
    /// The founders' story: chapters that each need time (a minimum game year) as well as
    /// achievements, so the eras come slowly. Finishing a chapter records it in the Legacy
    /// Timeline and opens the next one with a story scene.
    /// </summary>
    public sealed partial class Game
    {
        public ChapterDefinition CurrentChapter => Content.Chapters.FirstOrDefault(c => c.Number == State.Chapter);

        public ChapterDefinition Chapter(string id) => Content.Chapters.FirstOrDefault(c => c.Id == id);

        public List<RequirementStatus> ChapterRequirements() => Status(CurrentChapter?.Requirements ?? new List<Condition>());

        /// <summary>The story scene waiting to be shown, if any.</summary>
        public ChapterDefinition PendingStory => State.PendingStory == null ? null : Chapter(State.PendingStory);

        public void DismissStory() => State.PendingStory = null;

        /// <summary>Starts the story with the opening scene of the first chapter (new games only).</summary>
        internal void BeginStory()
        {
            var first = Content.Chapters.FirstOrDefault();
            if (first == null) return;
            State.Chapter = first.Number;
            State.PendingStory = first.Id;
        }

        void AdvanceChapters()
        {
            // One chapter at a time: each one deserves its own scene.
            var chapter = CurrentChapter;
            if (chapter == null || chapter.Locked || chapter.Requirements.Count == 0) return;
            if (!chapter.Requirements.All(IsMet)) return;
            var next = Content.Chapters.FirstOrDefault(c => c.Number > chapter.Number);
            if (next == null) return;
            State.Chapter = next.Number;
            State.PendingStory = next.Id;
            AddLegacy("chapter", $"Chapter {chapter.Number}: {chapter.Title}",
                      chapter.LegacyText ?? $"The chapter of {chapter.Title} came to an end.", chapter.Id);
            Notice(NoticeKind.Milestone, $"Chapter {next.Number} begins: {next.Title}.");
            GrantSpeedups(Speedups.ChapterFinished, $"Chapter {chapter.Number} done!");
        }
    }
}
