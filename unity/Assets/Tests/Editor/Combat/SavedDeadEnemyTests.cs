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
    /// An enemy that died in an earlier attempt at the same encounter is fielded dead (TASK-1061).
    /// </summary>
    /// <remarks>
    /// combatenc_pty_load_chap_state copies each roster actor's whole saved CombatantState into the
    /// arena (CBENC.C:78-79) and resets only its target (CBENC.C:92-93), so the flags byte, CAF_DEAD
    /// included, is what the fight starts with. Measured in the original on the c49 barn save: Nago
    /// read flags 0x03, never acted, and the fight ended when his three warriors fell.
    /// </remarks>
    public class SavedDeadEnemyTests {
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
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5, dead: true);
            session.SetRosterActorForTest(401, StatBlock(), 12, gridX: 6, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), null);
            return (runtime, runtime.EnterRoster(new short[] { 400, 401 }));
        }

        [Test]
        public void ASavedDeadEnemyIsFieldedDead() {
            var (_, fight) = Fight();

            Assert.IsTrue(fight.Enemies[0].IsDead, "CAF_DEAD in the saved record");
            Assert.IsFalse(fight.Enemies[1].IsDead, "a live record stays live");
        }

        [Test]
        public void ASavedDeadEnemyNeverActs() {
            var (runtime, fight) = Fight();
            var acted = new List<Combatant>();

            runtime.AdvanceToPartyTurn(m => acted.Add(m));

            CollectionAssert.DoesNotContain(acted, fight.Enemies[0]);
        }
    }
}
