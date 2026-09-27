namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.GameState;
    using NUnit.Framework;

    /// <summary>
    /// A dialog writing one of the middle event ids must reach the FIELD, not the flag overlay.
    /// </summary>
    /// <remarks>
    /// <b>The shipped dialogs write these ids 38 times</b> (counted over <c>generated/DDX</c>), and
    /// every one of them landed in the overlay — where nothing reads it — until now. The two that
    /// matter most are 30016 (the party-death state) and 30017 (the world-loop exit that ends a
    /// chapter), between them written by ten shipped dialogs.
    /// </remarks>
    public class GameSessionEventFieldTests {
        private static int Id(int offset) => GameStateEventFields.FieldBase + offset;

        [Test]
        public void TheActorRegisterIsResetWhenItNamesSomeoneNotInTheParty() {
            // TASK-493, V102CD dialog_combatant_name_table_init: every play re-checks nEvtArgActor0
            // against the active party and falls back to global 30005, rewriting the global.
            var session = new GameSession();
            session.SetActiveParty(3, new byte[] { 0, 1, 2 });
            session.EventActor = 5;
            int chapterSpeaker = session.GetGlobalValue(Id(5)) ?? 0;

            GameData.Resources.Dialog.DialogSlotContext context =
                BakAgain.Core.Services.DialogSlotContextFactory.FromSession(session);

            Assert.AreEqual(chapterSpeaker, context.PrimaryActorId);
            Assert.AreEqual(chapterSpeaker, session.EventActor);
        }

        [Test]
        public void TheWorldLoopExitReachesChapterTransitionPending() {
            var session = new GameSession();

            session.SetGlobalValue(Id(17), 2);

            Assert.AreEqual(2, session.ChapterTransitionPending);
        }

        [Test]
        public void ThePartyDeathStateReachesItsField() {
            var session = new GameSession();

            session.SetGlobalValue(Id(16), 1);

            Assert.AreEqual(1, session.PartyDeathState);
        }

        [Test]
        public void SettingTheChapterEventCHANGESTHECHAPTER() {
            // There is no `go_to_chapter` routine to find: changing the chapter is this write.
            var session = new GameSession();

            session.SetGlobalValue(Id(7), 4);

            Assert.AreEqual(4, session.Chapter);
        }

        [Test]
        public void TheChapterEventREADSBACKTheLiveChapter() {
            // TASK-570. The write half above has always worked; the READ fell through to the parsed
            // save, so the two halves disagreed about one key. GSTATE.C:73 answers Var 7 from
            // g_gameState.nChapter — one storage read both ways — while the port has the Chapter
            // property (live) and the save's ChapterNumber (frozen at load).
            //
            // On a bare session there is no parsed save at all, which is the shape TASK-485 hit and
            // recorded as a harness trap: the read answered null, a "chapter 4" control read 0 and
            // passed for the wrong reason.
            //
            // Live consequence, 2026-09-17: dialog 2300004 (the Krondor sewer ladder) branches on
            // Var 7, so a chapter-2 party took chapter 1's "advance the chapter" arm and jumped
            // straight to chapter 3, skipping all of chapter 2.
            var session = new GameSession();

            session.SetGlobalValue(Id(7), 4);

            Assert.AreEqual(4, session.Chapter, "the write reaches the field");
            Assert.AreEqual(4, session.GetGlobalValue(Id(7)),
                "and Var 7 reads the same value back, without a parsed save to fall through to");
        }

        [Test]
        public void ClearingTheLastActionSnapshotIgnoresTheValue() {
            var session = new GameSession { LastRestTicks = 12345 };

            session.SetGlobalValue(Id(6), 99);

            Assert.AreEqual(0, session.LastRestTicks, "a reset, not an assignment");
        }

        [Test]
        public void AnUnmappedIdInTheRangeStaysInTheOverlay() {
            // 30004 has no field and two shipped dialogs write it. It must not be silently mapped
            // onto a neighbour.
            var session = new GameSession();

            session.SetGlobalValue(Id(4), 7);

            Assert.AreEqual(7, session.GetGlobalValue(Id(4)));
            Assert.AreEqual(0, session.Chapter, "and nothing else moved");
            Assert.AreEqual(0, session.ChapterTransitionPending);
        }

        [Test]
        public void AnOrdinaryFlagIsStillJustAFlag() {
            var session = new GameSession();

            session.SetGlobalFlag(8127, true);

            Assert.AreEqual(1, session.GetGlobalValue(8127));
            Assert.AreEqual(0, session.Chapter);
        }
    }
}
