namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using GameData.Resources.GameState;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// A lost fight reaches the SAVE — <c>combat_arena_actor_die</c> ends with
    /// <c>stat_combatant_apply_condition(actor, 6, 100)</c> (COMBAT.C:278), and that write is what
    /// re-derives the party-down byte (TASK-264).
    ///
    /// <para><b>The two halves never met.</b> Combat models a downed actor with
    /// <see cref="Combatant.Incapacitated"/>, which lives only as long as the encounter; the
    /// party-down byte is derived from the save's Near-death ranks. So a party could be wiped out
    /// in the arena and walk away with a clean condition sheet, and <c>PartyDeathState</c> could
    /// only ever be set by falling into a pit.</para>
    /// </summary>
    public class CombatPartyWipeTests {
        private const int OrdinaryClass = 12;

        private static ActorStat[] StatBlock(byte health) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private static PartyCombatEntries Entries(int members) {
            var list = new List<SaveGameCombatData>();
            for (var i = 0; i < members; i++) {
                list.Add(new SaveGameCombatData(
                    0, 0, (byte)(3 + i), 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            }
            return new PartyCombatEntries("P1.DAT", list);
        }

        /// <param name="members">How many characters are in the active party.</param>
        private static (CombatRuntime Runtime, GameSession Session, CombatEncounter Fight)
            Fight(int members = 1) {
            var session = new GameSession();
            var indices = new byte[members];
            for (var i = 0; i < members; i++) {
                indices[i] = (byte)i;
                session.SetActorStatsForTest(i, StatBlock(40));
                session.SetActorConditionsForTest(i, new ActorConditions());
            }
            session.SetActiveParty((byte)members, indices);
            session.SetRosterActorForTest(400, StatBlock(40), OrdinaryClass, gridX: 5, gridY: 5);

            var runtime = new CombatRuntime(session, null, Entries(members));
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 }, null,
                GameData.Resources.World.EncounterActorPersistence.RecordAddress.None);
            return (runtime, session, fight);
        }

        private static int NearDeathOf(GameSession session, int character) =>
            session.ConditionsOf(character)[ActorCondition.NearDeath];

        private static Combatant PartyMember(CombatEncounter fight, int classId) {
            foreach (Combatant c in fight.Party) {
                if (c.ClassId == classId) {
                    return c;
                }
            }
            return null;
        }

        [Test]
        public void APartyMemberWhoDiesInTheArenaIsNEARDEATHInTheSave() {
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();

            runtime.KillForTest(PartyMember(fight, 0));

            Assert.AreEqual(ActorConditions.MaxRank, NearDeathOf(session, 0),
                "a death in the arena must reach the save's condition ranks; combat's own "
                + "Incapacitated flag does not outlive the encounter");
        }

        /// <summary>THE POINT: the last one down puts the party down.</summary>
        [Test]
        public void THELASTMemberDownSetsThePartyDownByte_AndTheFirstDoesNot() {
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight(members: 2);

            runtime.KillForTest(PartyMember(fight, 0));
            Assert.AreEqual(PartyDownState.Standing, session.PartyDeathState,
                "one member down is not a wipe — the sweep clears on the FIRST member still up");

            runtime.KillForTest(PartyMember(fight, 1));
            Assert.AreEqual(PartyDownState.Noticed, session.PartyDeathState,
                "with nobody left standing the sweep NOTICES, which is the value that speaks "
                + "dialog 0x145 as the world loop ends");
        }

        /// <summary>A monster dying changes nothing about the party — the write is gated on
        /// <c>charSlot</c> in the original, and on IsPartyMember here.</summary>
        [Test]
        public void AMonsterDyingLeavesThePartyStanding() {
            (CombatRuntime runtime, GameSession session, CombatEncounter fight) = Fight();

            runtime.KillForTest(fight.Enemies[0]);

            Assert.AreEqual(PartyDownState.Standing, session.PartyDeathState);
            Assert.AreEqual(0, NearDeathOf(session, 0), "the party member was never touched");
        }
    }
}
