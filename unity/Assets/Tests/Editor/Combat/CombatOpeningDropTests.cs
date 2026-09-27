namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// What surprising an encounter actually buys.
    /// </summary>
    /// <remarks>
    /// <b>It TAKES a turn away; it does not GRANT one.</b> Read from the disassembly rather than
    /// guessed: <c>runCombatEncounter</c>'s pre-round loop (@0x62b2d) clears each enemy's ready bit
    /// instead of running its AI, for every enemy due before the party's first turn. A port that
    /// gave the party a free round instead would be twice as generous and look plausible.
    ///
    /// <para><b>canassa calls this parameter <c>b_has_fired</c> and that name is wrong</b> — nothing
    /// about it concerns firing.</para>
    /// </remarks>
    public class CombatOpeningDropTests {
        private static ActorStat[] StatBlock() {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 40, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        private static PartyCombatEntries Entries() =>
            new PartyCombatEntries("P1.DAT", new List<SaveGameCombatData> {
                new SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            });

        private static (CombatRuntime Runtime, CombatEncounter Fight) Fight() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(), 12, gridX: 6, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), null);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401 });
            return (runtime, fight);
        }

        [Test]
        public void WithoutTheDropEveryEnemyTakesItsOpeningTurn() {
            var (runtime, _) = Fight();
            var acted = new List<Combatant>();

            runtime.AdvanceToPartyTurn(m => acted.Add(m));

            Assert.Greater(acted.Count, 0, "enemies act before the party when nobody is surprised");
        }

        [Test]
        public void WithTheDropEveryEnemyFORFEITSItsOpeningTurn() {
            var (runtime, _) = Fight();
            runtime.PartyHasTheDrop = true;
            var acted = new List<Combatant>();

            Combatant acting = runtime.AdvanceToPartyTurn(m => acted.Add(m));

            Assert.AreEqual(0, acted.Count, "a surprised enemy does not act");
            Assert.IsNotNull(acting, "and the party still gets its turn");
            Assert.IsTrue(acting.IsPartyMember);
        }

        [Test]
        public void TheDropIsSpentOnTheFirstPartyTurn() {
            // *** ONE forfeit each, once. *** The original's loop runs until the picker lands on a
            // party member; it is not a standing advantage for the whole fight.
            var (runtime, _) = Fight();
            runtime.PartyHasTheDrop = true;

            runtime.AdvanceToPartyTurn(_ => { });
            Assert.IsFalse(runtime.PartyHasTheDrop, "the drop does not survive the opening round");
        }
    }
}
