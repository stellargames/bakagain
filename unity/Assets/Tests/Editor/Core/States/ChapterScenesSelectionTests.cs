namespace BakAgain.Tests.Editor.Core.States {
    using System.Collections.Generic;
    using BakAgain.Core.States;
    using GameData.Resources.Data;
    using NUnit.Framework;

    public class ChapterScenesSelectionTests {
        private static Chapter TwoPartChapter() => new Chapter {
            Number = 1, ContentsActionId = 2, IntroAnimation = "CHAPTER1",
            Parts = new List<ChapterPart> {
                new ChapterPart { Book = "C11.BOK", Animation = "C11" },
                new ChapterPart { Book = "C12.BOK", Animation = "C12" },
            },
        };

        [Test]
        public void StartOnly_SelectsOnlyFirstPart() {
            Assert.AreEqual(1, ChapterScenes.PartCount(TwoPartChapter(), ChapterScenesMode.StartOnly));
        }

        [Test]
        public void Full_SelectsAllParts() {
            Assert.AreEqual(2, ChapterScenes.PartCount(TwoPartChapter(), ChapterScenesMode.Full));
        }

        [Test]
        public void PartCount_NeverExceedsAvailableParts() {
            var onePart = new Chapter { Parts = new List<ChapterPart> { new ChapterPart() } };
            Assert.AreEqual(1, ChapterScenes.PartCount(onePart, ChapterScenesMode.Full));
            Assert.AreEqual(1, ChapterScenes.PartCount(onePart, ChapterScenesMode.StartOnly));
        }

        // A chapter's CLOSE is its second part alone: gmain_play_chapter_cutscene(chapter, 2, 1)
        // (GMAIN.C:745) shows C<n>2.BOK and plays C<n>2.ADS, and its CHAPTER<n>.ADS title card is
        // channel 2, which no shipped chapter script has -- so no intro animation either.
        [Test]
        public void EndOnly_SelectsOnlyTheSecondPartAndNoTitleCard() {
            Assert.AreEqual((1, 1), ChapterScenes.PartRange(TwoPartChapter(), ChapterScenesMode.EndOnly));
            Assert.IsFalse(ChapterScenes.PlaysIntro(ChapterScenesMode.EndOnly));
        }

        [Test]
        public void EndOnly_OnAOnePartChapterPlaysNothing() {
            var onePart = new Chapter { Parts = new List<ChapterPart> { new ChapterPart() } };
            Assert.AreEqual(0, ChapterScenes.PartRange(onePart, ChapterScenesMode.EndOnly).Count);
        }

        [Test]
        public void StartAndFull_BeginAtTheFirstPartWithTheTitleCard() {
            Assert.AreEqual((0, 1), ChapterScenes.PartRange(TwoPartChapter(), ChapterScenesMode.StartOnly));
            Assert.AreEqual((0, 2), ChapterScenes.PartRange(TwoPartChapter(), ChapterScenesMode.Full));
            Assert.IsTrue(ChapterScenes.PlaysIntro(ChapterScenesMode.StartOnly));
        }

        // Faithful to the original: a chapter is replayable only once you've moved PAST it
        // (currentChapter > N). At chapter 1 nothing is replayable; the current chapter is not.
        [Test]
        public void IsReplayable_OnlyChaptersStrictlyBeforeCurrent() {
            // At chapter 1: nothing replayable (not even chapter 1 itself).
            Assert.IsFalse(ChapterScenes.IsReplayable(1, 1), "current chapter is not replayable");
            Assert.IsFalse(ChapterScenes.IsReplayable(2, 1), "future chapter is not replayable");
            // At chapter 3: chapters 1 and 2 replayable; chapter 3 (current) and 4 (future) not.
            Assert.IsTrue(ChapterScenes.IsReplayable(1, 3));
            Assert.IsTrue(ChapterScenes.IsReplayable(2, 3));
            Assert.IsFalse(ChapterScenes.IsReplayable(3, 3), "current chapter is not replayable");
            Assert.IsFalse(ChapterScenes.IsReplayable(4, 3), "future chapter is not replayable");
        }
    }
}
