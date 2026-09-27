namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using MeleeAttack = GameData.Resources.Combat.CombatActionDispatch.MeleeAttack;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// A killed encounter actor stays killed — the write <c>combat_arena_actor_die</c> ends with,
    /// which had no caller on our side at all.
    /// </summary>
    /// <remarks>
    /// <b>These assert the SAVE's block, not the combatant.</b> A dead flag on a Combatant lives
    /// exactly as long as the fight; what makes the group gone on a revisit is the twelve bytes this
    /// writes, and nothing in the fight itself would notice their absence.
    /// </remarks>
    public class CombatRemovalPersistenceTests {
        // Creature class 49 vanishes on death; anything outside {49, 56, 57} leaves a corpse.
        private const int VanishesClass = 49;
        private const int OrdinaryClass = 12;

        private const int RefPair = 3;
        private const int RecordIndex = 2;

        private static ActorStat[] StatBlock(byte health) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 0, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private static GameData.Resources.Data.SaveGameCombatData EntryAt(byte x, byte y) =>
            new GameData.Resources.Data.SaveGameCombatData(
                0, 0, x, y, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static PartyCombatEntries Entries() =>
            new PartyCombatEntries("P1.DAT",
                new System.Collections.Generic.List<GameData.Resources.Data.SaveGameCombatData> {
                    EntryAt(3, 4),
                });

        /// <summary>
        /// One party member against one enemy of <paramref name="creatureType"/>, on one health so a
        /// single landed swing kills it.
        /// </summary>
        private static (CombatRuntime Runtime, GameSession Session, Combatant Member, Combatant Monster)
            FightAgainst(int creatureType,
                EncounterActorPersistence.RecordAddress? address = null) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(1), creatureType, gridX: 5, gridY: 5);

            var runtime = new CombatRuntime(session, null, Entries());
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 }, null, address);
            return (runtime, session, fight.Party[0], fight.Enemies[0]);
        }

        private static EncounterObjectStates.Entry StateOf(GameSession session, int slot) =>
            session.EncounterActorStates[EncounterObjectStates.IndexOf(RefPair, RecordIndex, slot)];

        [Test]
        public void AVanishingEnemyIsRecordedAsRemoved() {
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                FightAgainst(VanishesClass,
                    new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.AreEqual(DeathOutcome.RemovedFromField, runtime.LastDeath);
            Assert.AreEqual(EncounterObjectStates.KindRemoved, StateOf(session, 0).Kind);
        }

        [Test]
        public void ThePoseIsZeroed_theRecordSaysGoneNotGoneFromHere() {
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                FightAgainst(VanishesClass,
                    new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));
            monster.X = 5;
            monster.Y = 5;

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            EncounterObjectStates.Entry entry = StateOf(session, 0);
            Assert.AreEqual(0, entry.WorldXOffset);
            Assert.AreEqual(0, entry.WorldYOffset);
            Assert.AreEqual(0, entry.Facing);
        }

        [Test]
        public void AnEnemyThatLEAVESACORPSEIsNotRecordedAsRemoved() {
            // The original's `removed` flag stays clear on that path, and the difference is the
            // whole point: a corpse is still there to be looted, and to be found again on a revisit
            // if the encounter is reset.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                FightAgainst(OrdinaryClass,
                    new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.AreEqual(DeathOutcome.LeavesCorpse, runtime.LastDeath);
            Assert.IsTrue(StateOf(session, 0).IsEmpty, "nothing was written");
        }

        [Test]
        public void AFightWithNoKnownRecordWritesNothing() {
            // rgnenc_persist_actor_removed scans the chunk's record-id list and returns without
            // writing when the id is absent — the sixth encounter in a chunk has no slot. Writing
            // one anyway would stamp "gone" onto a real encounter's actors.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                FightAgainst(VanishesClass, address: null);

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.AreEqual(DeathOutcome.RemovedFromField, runtime.LastDeath,
                "the death still happens; only the persistence is declined");
            Assert.AreEqual(0, session.EncounterActorStates.CountOfKind(
                EncounterObjectStates.KindRemoved));
        }

        [Test]
        public void AFallenPARTYMemberIsNeverStampedIntoAnEncountersRoster() {
            // The original scans the ENEMY array for the dead actor and returns if it is not there.
            // A party member is in the other one, so nothing is written — and a port that indexed by
            // "position in the fight" would mark an enemy slot when a companion went down.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                FightAgainst(VanishesClass,
                    new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));
            // The member has 40 health and no stamina; keep swinging until they drop.
            for (var i = 0; i < 40 && !member.IsDead; i++) {
                runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0);
            }

            Assert.IsTrue(member.IsDead, "the party member actually went down");
            Assert.AreEqual(0, session.EncounterActorStates.CountOfKind(
                EncounterObjectStates.KindRemoved));
        }

        [Test]
        public void TheSlotIsTheEnemysPositionInTheFight() {
            // Two enemies: killing the SECOND must write slot 1, not slot 0. A removal written at a
            // fixed slot would look right in every one-enemy encounter and be wrong in every other.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(40), VanishesClass, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(1), VanishesClass, gridX: 6, gridY: 5);

            var runtime = new CombatRuntime(session, null, Entries());
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401 }, null,
                new EncounterActorPersistence.RecordAddress(RefPair, RecordIndex));

            runtime.ResolveMelee(fight.Party[0], fight.Enemies[1], MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(StateOf(session, 0).IsEmpty, "the first enemy is still standing");
            Assert.AreEqual(EncounterObjectStates.KindRemoved, StateOf(session, 1).Kind);
        }
    }
}
