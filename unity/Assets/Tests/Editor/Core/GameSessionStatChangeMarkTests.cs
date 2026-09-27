namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;

    /// <summary>
    /// The two writes <c>stat_combatant_modify</c> makes after a change (STAT.C:300-307).
    /// </summary>
    /// <remarks>
    /// <b>The original does them INSIDE the routine, so no caller can forget.</b> The port returns
    /// the signal on <see cref="StatEngine.StatChange"/> instead, and until 2026-09-21 all thirteen
    /// call sites discarded it — so <c>CharacterSheetView</c>'s read-and-clear could only ever read
    /// 0 and no rating was ever highlighted (TASK-611).
    ///
    /// <para><b>The asymmetry is the whole rule and is what a tidy implementation gets wrong:</b>
    /// the sheet mark follows <c>SignalsImprovement</c> — <i>any</i> change to a skill, up or down —
    /// while the party-dirty bit follows an <i>increase</i> only. Health and Stamina are the
    /// exception to the first half: they mark on a gain and not on a loss, because losing health is
    /// not an improvement worth telling the player about.</para>
    /// </remarks>
    public class GameSessionStatChangeMarkTests {
        private const int Character = 0;

        private static GameSession Session() {
            var stats = new ActorStat[17];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 40, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 60, Max = 99 };
            var session = new GameSession();
            session.SetActorStatsForTest(Character, stats);
            session.SetActiveParty(1, new byte[] { Character });
            return session;
        }

        private static bool Marked(GameSession session, ActorAttribute attribute) =>
            (session.GetGlobalValue(
                CharacterSheetRow.ChangedFlagFor(Character, (int)attribute)) ?? 0) != 0;

        private static bool PartyImproved(GameSession session) =>
            (session.PartyDirtyFlags & CharacterSheetRow.ImprovedDirtyBit) != 0;

        /// <summary>
        /// A delta of N whole points. <c>StatEngine.Modify</c> takes 1/256ths — it banks the
        /// sub-unit remainder in <c>Experience</c>, which is what makes repeated small skill uses
        /// add up — so a bare 5 moves nothing at all and every assertion here would pass vacuously.
        /// </summary>
        private static long Points(int n) => (long)n << 8;

        // Self-check: these tests are about the MARK, so each one first proves the change it is
        // marking actually happened. Without this a units mistake reads as "the mark is broken".
        private static void AssertBaseMoved(GameSession session, ActorAttribute attribute,
            int before) =>
            Assert.AreNotEqual(before, session.StatsOf(Character)[(int)attribute].Base,
                $"the {attribute} change did not move the stored value; the test is not measuring "
                + "what it claims");

        [Test]
        public void ASkillGainMarksTheSheetAndTheParty() {
            GameSession session = Session();
            Assert.IsFalse(Marked(session, ActorAttribute.Stealth), "nothing marked yet");

            session.ModifyStatOf(Character, ActorAttribute.Stealth, Points(5));

            AssertBaseMoved(session, ActorAttribute.Stealth, 40);
            Assert.IsTrue(Marked(session, ActorAttribute.Stealth));
            Assert.IsTrue(PartyImproved(session));
        }

        [Test]
        public void ASkillLOSSStillMarksTheSheet_butIsNotAnImprovement() {
            // STAT.C:301 — `(1 < stat_idx) || (origBase < slot->base)`. For a skill the first half
            // is true, so ANY movement marks. The dirty bit at :305 tests the increase separately.
            GameSession session = Session();

            session.ModifyStatOf(Character, ActorAttribute.Stealth, Points(-5));

            AssertBaseMoved(session, ActorAttribute.Stealth, 40);
            Assert.IsTrue(Marked(session, ActorAttribute.Stealth), "a skill moving at all shows");
            Assert.IsFalse(PartyImproved(session), "losing a skill is not an improvement");
        }

        [Test]
        public void LosingHEALTHMarksNothing() {
            // The other half of :301: for stat_idx 0 and 1 only an increase qualifies.
            GameSession session = Session();

            session.ModifyStatOf(Character, ActorAttribute.Health, Points(-10));

            AssertBaseMoved(session, ActorAttribute.Health, 60);
            Assert.IsFalse(Marked(session, ActorAttribute.Health));
            Assert.IsFalse(PartyImproved(session));
        }

        [Test]
        public void GainingHealthDoesMarkIt() {
            // The control for the test above: without it, "health never marks" would also pass.
            GameSession session = Session();

            session.ModifyStatOf(Character, ActorAttribute.Health, Points(10));

            AssertBaseMoved(session, ActorAttribute.Health, 60);
            Assert.IsTrue(Marked(session, ActorAttribute.Health));
        }

        [Test]
        public void AChangeThatMovesNothingMarksNothing() {
            // The second condition at :300 is `slot->base != origBase`. A zero delta is a no-op and
            // must not light the sheet up.
            GameSession session = Session();

            session.ModifyStatOf(Character, ActorAttribute.Stealth, 0);

            Assert.IsFalse(Marked(session, ActorAttribute.Stealth));
            Assert.IsFalse(PartyImproved(session));
        }

        [Test]
        public void BroadcastSkillUseMarksEveryActiveMember() {
            // The highest-traffic path into this: scouting, stealth, barding and haggling all award
            // party-wide, and each member's own sheet should light up.
            var stats0 = new ActorStat[17];
            var stats1 = new ActorStat[17];
            foreach (ActorStat[] set in new[] { stats0, stats1 }) {
                for (var i = 0; i < set.Length; i++) {
                    set[i] = new ActorStat { Base = 40, Max = 99 };
                }
                set[(int)ActorAttribute.Health] = new ActorStat { Base = 60, Max = 99 };
            }
            var session = new GameSession();
            session.SetActorStatsForTest(0, stats0);
            session.SetActorStatsForTest(1, stats1);
            session.SetActiveParty(2, new byte[] { 0, 1 });

            // Enough to cross a whole point through the SkillUse scaling in one call; a single
            // use banks a remainder and moves nothing, which is faithful and untestable here.
            session.BroadcastSkillUse(ActorAttribute.Scouting, (int)Points(20));

            foreach (int character in new[] { 0, 1 }) {
                Assert.IsTrue(
                    (session.GetGlobalValue(CharacterSheetRow.ChangedFlagFor(
                        character, (int)ActorAttribute.Scouting)) ?? 0) != 0,
                    $"character {character} should have been marked");
            }
        }
    }
}
