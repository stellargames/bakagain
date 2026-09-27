namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using NUnit.Framework;

    [TestFixture]
    public class GameSessionFlagsTests {
        [Test]
        public void TheTimeOfDayGlobalsReadTheLiveClock_NotTheSaveTheSessionLoaded() {
            // TASK-549. Dialog Var 9/10/12 are globals 30009 (night), 30010 (day) and 30012 (hour).
            // A session that loaded at 02:20 answered them from the parsed save for the rest of the
            // day, so Brother Jeremy's door (dial_z19:8862, Var 9 arm) stayed shut at 07:34.
            const int UnitsPerHour = GameData.Resources.GameState.GameTime.UnitsPerHour;
            const long Day6 = 6L * GameData.Resources.GameState.GameTime.UnitsPerDay;
            var session = new GameSession { GameTimeIn2Seconds = Day6 + (2 * UnitsPerHour) };

            Assert.AreEqual(1, session.GetGlobalValue(30009), "02:00 is night");
            Assert.AreEqual(0, session.GetGlobalValue(30010));
            Assert.AreEqual(2, session.GetGlobalValue(30012));

            session.GameTimeIn2Seconds = Day6 + (7 * UnitsPerHour);

            Assert.AreEqual(0, session.GetGlobalValue(30009), "07:00 is day");
            Assert.AreEqual(1, session.GetGlobalValue(30010));
            Assert.AreEqual(7, session.GetGlobalValue(30012));

            session.GameTimeIn2Seconds = Day6 + (20 * UnitsPerHour);

            Assert.AreEqual(1, session.GetGlobalValue(30009), "20:00 is night again");
        }

        [Test]
        public void GetGlobalValue_ReadsOverlayFlag_UnsetKeyFallsThrough() {
            var session = new GameSession();
            session.SetGlobalFlag(8127, true);

            // An explicitly-set overlay key wins; an unset key isn't in the overlay, so with no
            // save state loaded it falls through to null (rather than masking _state with 0).
            Assert.AreEqual(1, session.GetGlobalValue(8127));
            Assert.IsNull(session.GetGlobalValue(8128));
        }

        [Test]
        public void HydratingASaveDropsTheRuntimeOverlayThatWouldShadowIt() {
            // *** LOADING A SAVE USED TO KEEP THE ABANDONED GAME'S FLAGS. *** GetGlobalValue reads
            // the overlay before the parsed save, and only Clear() — the way back to the main menu —
            // ever dropped it. The in-game Restore goes LoadGameMenu -> GameFlow.LoadSave ->
            // GameStateLoader -> GameSession.Initialize and never passes through Clear.
            //
            // Driven 2026-09-13: walking onto Northwarden's Town trigger set ScoutTried(0) — global
            // 5200 — and every later load of a save carrying it CLEAR still read 1, so the town
            // refused to open and the party walked past the gate.
            var session = new GameSession();
            session.SetGlobalFlag(5200, true);
            Assert.AreEqual(1, session.GetGlobalValue(5200), "the overlay answers before a load");

            session.Initialize(new SaveGameBuilder().Build(), GameSessionSource.LoadSave);

            Assert.AreNotEqual(1, session.GetGlobalValue(5200),
                "a loaded save must not be shadowed by the previous game's flag writes");
        }

        [Test]
        public void TheAffordabilityGateReadsTheLivePurseAndTheLiveQuote() {
            // *** BOTH OPERANDS OF VAR 3 MOVE DURING PLAY. *** The mender, the inn, the temple and
            // the shop all reach a text-less router on their accept branch whose only test is global
            // 30003, and its default arm is the line that turns a pauper away. Answering it from a
            // loaded save's copy of gold and of the quote let every one of those gates pass:
            // measured at Highcastle on 2026-09-13, a 173-royal repair quote against 143 royals was
            // agreed to and the party came out at -30.
            var session = new GameSession { PartyGold = 143 };
            session.SetGlobalValue(GameData.Resources.Dialog.DialogSlotPopulator.QuotedAmountGlobalKey, 173);

            Assert.AreEqual(0,
                session.GetGlobalValue(
                    GameData.Resources.Dialog.DialogBranchWalker.CanAffordQuoteGlobalKey),
                "143 royals does not cover a 173-royal quote");
            Assert.AreEqual(143,
                session.GetGlobalValue(
                    GameData.Resources.Dialog.DialogBranchWalker.GoldRoyalsGlobalKey));
            Assert.AreEqual(14,
                session.GetGlobalValue(
                    GameData.Resources.Dialog.DialogBranchWalker.GoldSovereignsGlobalKey));

            session.PartyGold = 200;

            Assert.AreEqual(1,
                session.GetGlobalValue(
                    GameData.Resources.Dialog.DialogBranchWalker.CanAffordQuoteGlobalKey),
                "and the very same gate has to open once the purse does cover it");
        }

        [Test]
        public void SetGlobalFlag_False_OverridesToZero() {
            var session = new GameSession();
            session.SetGlobalFlag(8127, true);
            session.SetGlobalFlag(8127, false);

            Assert.AreEqual(0, session.GetGlobalValue(8127));
        }
    }
}
