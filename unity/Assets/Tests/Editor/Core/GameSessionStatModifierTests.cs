namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using NUnit.Framework;

    /// <summary>
    /// The eight timed stat modifiers, finally reaching a stat read.
    /// </summary>
    /// <remarks>
    /// <b>The model and the loader were both finished and joined to nothing</b>, so every temporary
    /// buff and debuff on a party member did nothing at all — and a missing modifier just means the
    /// stat reads its unmodified value, so it never looked broken (TASK-251). These pin the join.
    /// </remarks>
    public class GameSessionStatModifierTests {
        private const int Character = 0;
        private const ActorAttribute Attr = ActorAttribute.Stealth;

        private static int MaskOf(ActorAttribute a) => 1 << (int)a;

        private static (GameSession Session, ActorStat[] Stats) SessionWithStat(byte value) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = value, Max = 99 };
            }
            // Health at full, so the health scaling neither drags the value nor zeroes it.
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 99, Max = 99 };
            var session = new GameSession();
            session.SetActorStatsForTest(Character, stats);
            session.SetActiveParty(1, new byte[] { Character });
            return (session, stats);
        }

        private static int Read(GameSession session, ActorStat[] stats, bool inCombat) =>
            StatEngine.Get(stats[(int)Attr], Attr, stats[(int)ActorAttribute.Health],
                StatReadMode.Unscaled, session.PartyEffectsFor(Character, Attr, inCombat));

        private static void PutSlot(GameSession session, int slot, ActorStatModifiers.Slot entry) =>
            session.StatModifiers[ActorStatModifiers.IndexOf(Character, slot)] = entry;

        [Test]
        public void AFlatModifierMovesTheValue() {
            var (session, stats) = SessionWithStat(40);
            Assert.AreEqual(40, Read(session, stats, inCombat: false));

            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: 1, statMask: MaskOf(Attr), value: 15, appliedAt: 0, expiresAt: 0));

            Assert.AreEqual(55, Read(session, stats, inCombat: false));
        }

        [Test]
        public void AModifierForAnotherAttributeIsNotApplied() {
            var (session, stats) = SessionWithStat(40);
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: 1, statMask: MaskOf(ActorAttribute.Barding), value: 15, appliedAt: 0,
                expiresAt: 0));

            Assert.AreEqual(40, Read(session, stats, inCombat: false));
        }

        [Test]
        public void ACombatOnlyModifierIsSkippedOutOfCombat_andNOTExpired() {
            // *** THE SKIP IS NOT AN EXPIRY. *** The original returns before the expiry test, so a
            // combat-only buff read a hundred times out of a fight is still there when one starts.
            // Freeing the slot on that read would silently destroy the buff.
            var (session, stats) = SessionWithStat(40);
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: (int)ActorStatModifiers.ModifierFlags.CombatOnly | 1,
                statMask: MaskOf(Attr), value: 15, appliedAt: 0, expiresAt: 0));

            Assert.AreEqual(40, Read(session, stats, inCombat: false), "not applied out of combat");
            Assert.IsFalse(
                session.StatModifiers[ActorStatModifiers.IndexOf(Character, 0)].IsEmpty,
                "and the slot survives the read");
            Assert.AreEqual(55, Read(session, stats, inCombat: true), "still there for the fight");
        }

        [Test]
        public void AnExpiredModifierIsFreedByTheReadThatNoticesIt() {
            // The eight slots are a fixed table: without this they fill with dead entries and
            // SlotToFill starts evicting LIVE modifiers to make room.
            var (session, stats) = SessionWithStat(40);
            session.GameTimeIn2Seconds = 5000;
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: (int)ActorStatModifiers.ModifierFlags.Expires | 1,
                statMask: MaskOf(Attr), value: 15, appliedAt: 0, expiresAt: 100));

            Assert.AreEqual(40, Read(session, stats, inCombat: false),
                "a modifier that lapsed contributes nothing on the read that notices");
            Assert.IsTrue(session.StatModifiers[ActorStatModifiers.IndexOf(Character, 0)].IsEmpty,
                "and the slot is freed");
            Assert.IsTrue(session.StatModifiersDirty, "so the save must write the block back");
        }

        [Test]
        public void ALiveModifierForAnotherStatIsNotExpiredByThisRead() {
            // Expiry is a side effect of being read for a MATCHING stat — a slot for another
            // attribute is never examined, so its lapse goes unnoticed. Faithful, not an oversight.
            var (session, stats) = SessionWithStat(40);
            session.GameTimeIn2Seconds = 5000;
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: (int)ActorStatModifiers.ModifierFlags.Expires | 1,
                statMask: MaskOf(ActorAttribute.Barding), value: 15, appliedAt: 0, expiresAt: 100));

            Read(session, stats, inCombat: false);

            Assert.IsFalse(session.StatModifiers[ActorStatModifiers.IndexOf(Character, 0)].IsEmpty);
        }

        [Test]
        public void PercentageModifiersScaleRatherThanAdd() {
            var (session, stats) = SessionWithStat(40);
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: (int)ActorStatModifiers.ModifierFlags.Percentage | 1,
                statMask: MaskOf(Attr), value: 50, appliedAt: 0, expiresAt: 0));

            Assert.AreEqual(60, Read(session, stats, inCombat: false), "40 * 150 / 100");
        }

        /// <summary>A party whose members differ only in the value every stat of theirs carries.</summary>
        private static GameSession PartyWithStats(params byte[] values) {
            var session = new GameSession();
            var ids = new byte[values.Length];
            for (byte i = 0; i < values.Length; i++) {
                var stats = new ActorStat[16];
                for (var j = 0; j < stats.Length; j++) {
                    stats[j] = new ActorStat { Base = values[i], Max = 99 };
                }
                stats[(int)ActorAttribute.Health] = new ActorStat { Base = 99, Max = 99 };
                session.SetActorStatsForTest(i, stats);
                ids[i] = i;
            }
            session.SetActiveParty((byte)values.Length, ids);
            return session;
        }

        // *** STEALTH IS THE PARTY'S WORST. *** getHighestValueInParty (@0x43538) opens with
        // `cmp [bp+attributeNumber], Stealth / jnz` and seeds THAT arm with 30000, keeping the
        // smallest — the party is only as quiet as its clumsiest member. The numbers below are the
        // live chapter-1 party: measured 2026-09-13 with the same save in both games, the original
        // answered 29 where this answered 33.
        [Test]
        public void StealthIsThePartysWorst_andNamesThatMember() {
            GameSession session = PartyWithStats(29, 31, 33);

            int value = session.PartyExtreme(ActorAttribute.Stealth, out int who);

            Assert.AreEqual(29, value, "the party is only as quiet as its clumsiest member");
            Assert.AreEqual(0, who, "and the skill-use award follows the member who supplied it");
        }

        [Test]
        public void EveryOtherAttributeIsThePartysBest() {
            GameSession session = PartyWithStats(29, 31, 33);

            int value = session.PartyExtreme(ActorAttribute.Scouting, out int who);

            Assert.AreEqual(33, value);
            Assert.AreEqual(2, who);
        }

        // stat_party_find_extreme writes nEvtArgStat (STAT.C:335), which dialog slot kind 12 names
        // (DIALOG.C:486). Read from the save alone, the scouting warning named roster 0, Locklear,
        // to a chapter-3 party he was not in.
        [Test]
        public void PartyBestBecomesTheDialogsTertiaryActor() {
            GameSession session = PartyWithStats(29, 31, 33);
            Assert.AreEqual(0, session.DialogTertiaryActorId, "control: nothing has asked yet");

            session.PartyExtreme(ActorAttribute.Scouting, out int who);

            Assert.AreEqual(2, who);
            Assert.AreEqual(who, session.DialogTertiaryActorId);
        }

        // *** A PARTY ACTION TRAINS THE PARTY. *** stat_party_broadcast_status_op(stat, 1, 3) walks
        // activeParty; the three hotspot rolls awarded only the member PartyExtreme named, so the
        // party trained about a third as fast at Scouting and Stealth (TASK-474).
        [Test]
        public void BroadcastSkillUseReachesEveryActiveMember() {
            GameSession session = PartyWithStats(29, 31, 33);

            session.BroadcastSkillUse(ActorAttribute.Scouting, 1);

            // A single skill use rarely moves `Base` — it banks sub-unit progress in `Experience`
            // — so the assertion is on the pair, which is what the original advances.
            var start = new[] { 29, 31, 33 };
            for (byte c = 0; c < 3; c++) {
                ActorStat stat = session.StatsOf(c)[(int)ActorAttribute.Scouting];
                Assert.Greater(stat.Base * 256 + stat.Experience, start[c] * 256,
                    $"member {c} learned nothing from the party's success");
            }
        }

        [Test]
        public void PartyBestReadsThroughTheModifiers() {
            // The party skill checks — scouting, lockpicking, haggling — used to read past both the
            // modifiers and the afflictions while the character sheet showed the player otherwise.
            var (session, _) = SessionWithStat(40);
            PutSlot(session, 0, new ActorStatModifiers.Slot(
                flags: 1, statMask: MaskOf(Attr), value: 20, appliedAt: 0, expiresAt: 0));

            int best = session.PartyExtreme(Attr, out int who);

            Assert.AreEqual(Character, who);
            Assert.Greater(best, 40, "the buff has to reach the check that uses it");
        }
    }
}
