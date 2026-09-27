namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.GameState;
    using NUnit.Framework;

    /// <summary>
    /// What the save writer is handed for the story flags (TASK-210).
    /// </summary>
    public class GameSessionDirtyFlagTests {
        [Test]
        public void AFlagSetThisSessionIsOfferedToTheWriter() {
            var session = new GameSession();

            session.SetGlobalFlag(8127, true);

            Assert.AreEqual(1, session.DirtyGlobalFlags.Count);
            Assert.AreEqual(1, session.DirtyGlobalFlags[8127]);
        }

        [Test]
        public void OnlyWhatCHANGEDIsOffered() {
            // The rest of the story state is already in the backing body and the writer applies
            // these onto it — a session that set one flag must not rewrite 1113 bytes from a
            // partial view.
            var session = new GameSession();

            Assert.IsEmpty(session.DirtyGlobalFlags);
        }

        [Test]
        public void AFieldWriteNEVERReachesTheFlagSet() {
            // The 30000-range ids share the key space but are game-state fields, and SetGlobalValue
            // routes them to their own homes. If one leaked in here the writer would try to find it
            // a bit in a bitmap it does not belong to.
            var session = new GameSession();

            session.SetGlobalValue(GameStateEventFields.FieldBase + 17, 2);   // world-loop exit
            session.SetGlobalValue(GameStateEventFields.FieldBase + 7, 3);    // chapter

            Assert.IsEmpty(session.DirtyGlobalFlags);
            Assert.AreEqual(2, session.ChapterTransitionPending);
            Assert.AreEqual(3, session.Chapter);
        }

        [Test]
        public void AHighFlagIsOfferedToo() {
            var session = new GameSession();

            session.SetGlobalFlag(56013, true);

            Assert.AreEqual(1, session.DirtyGlobalFlags[56013]);
            Assert.IsTrue(GlobalFlagLayout.IsHighFlag(56013));
        }
    }
}
