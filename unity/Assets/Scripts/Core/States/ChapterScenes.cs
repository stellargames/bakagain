namespace BakAgain.Core.States {
    using GameData.Resources.Data;

    /// <summary>Pure scene-selection rules for chapter scenes (shared by the
    /// ChapterScenesPlayer, the Contents screen, and their tests).</summary>
    public static class ChapterScenes {
        /// <summary>How many leading parts to play for the mode: StartOnly = 1, Full = all —
        /// clamped to the parts the chapter actually has.</summary>
        public static int PartCount(Chapter chapter, ChapterScenesMode mode) {
            int available = chapter?.Parts?.Count ?? 0;
            int wanted = mode == ChapterScenesMode.StartOnly ? 1 : available;
            return wanted < available ? wanted : available;
        }

        /// <summary>Which parts to play for the mode, as a start index and a count, clamped to the
        /// parts the chapter has. EndOnly is part 2 alone — <c>gmain_play_chapter_cutscene(n, 2, 1)</c>
        /// (GMAIN.C:745). A part is a book OR an animation, so only a chapter with neither C&lt;n&gt;2.BOK nor
        /// C&lt;n&gt;2.ADS closes with nothing.</summary>
        public static (int First, int Count) PartRange(Chapter chapter, ChapterScenesMode mode) {
            if (mode != ChapterScenesMode.EndOnly) {
                return (0, PartCount(chapter, mode));
            }
            int available = chapter?.Parts?.Count ?? 0;
            return available >= 2 ? (1, 1) : (1, 0);
        }

        /// <summary>Whether the CHAPTER&lt;n&gt; title card plays. The original plays that script's
        /// channel <c>part</c>, and every shipped CHAPTER&lt;n&gt;.ADS has channel 1 only, so a
        /// chapter's close has no card.</summary>
        public static bool PlaysIntro(ChapterScenesMode mode) => mode != ChapterScenesMode.EndOnly;

        /// <summary>Whether a chapter's scenes can be re-watched from the Contents screen: only chapters
        /// the player has moved PAST — strictly before the current chapter. Faithful to the original
        /// (showTableOfContents @0x21252 replays chapter N only when currentChapter > N: <c>cmp chapterNr,N;
        /// jle → no replay</c>). So at chapter 1 nothing is replayable; chapter 1 becomes viewable once you
        /// reach chapter 2. The current chapter is visible-but-not-replayable (help dialog 329).</summary>
        public static bool IsReplayable(int chapterNumber, int currentChapter) => chapterNumber < currentChapter;
    }
}
