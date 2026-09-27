namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.Dialog;
    using NUnit.Framework;

    /// <summary>
    /// Var 5, the chapter's speaker, is computed (GSTATE.C:80): Pug when he is in the party, else
    /// g_chapterDefaultSpeaker[chapter - 1]. Read from the save, chapter 4's dialogs named James.
    /// </summary>
    public class GameSessionChapterSpeakerTests {
        private static GameSession InChapter(int chapter, params byte[] party) {
            var session = new GameSession();
            session.SetGlobalValue(DialogBranchWalker.ChapterGlobalKey, chapter);
            session.SetActiveParty((byte)party.Length, party);
            return session;
        }

        [Test]
        public void ChapterFourSpeaksAsGorath() {
            GameSession session = InChapter(4, 1, 2);
            Assert.AreEqual(1, session.GetGlobalValue(DialogSlotPopulator.ChapterSpeakerGlobalKey));
        }

        [Test]
        public void PugInThePartyAlwaysSpeaks() {
            GameSession session = InChapter(3, 4, 3, 1);
            Assert.AreEqual(3, session.GetGlobalValue(DialogSlotPopulator.ChapterSpeakerGlobalKey));
        }
    }
}
