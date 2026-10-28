namespace BakAgain.Tests.Editor.Combat {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>What the player knows: Inspect results, damage seen, the fight log (TASK-972 phase 1).</summary>
    public class CombatKnowledgeWiringTests {
        private static ActorStat[] Stats() {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 50, Max = 99 };
            }
            return stats;
        }

        private static (CombatRuntime Runtime, CombatEncounter Fight) Fight() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, Stats());
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, Stats(), gridX: 7, gridY: 0);
            session.SetRosterActorForTest(401, Stats(), gridX: 7, gridY: 2);
            var entries = new PartyCombatEntries("P1.DAT", new List<GameData.Resources.Data.SaveGameCombatData> {
                new GameData.Resources.Data.SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) });
            var runtime = new CombatRuntime(session, null, entries);
            return (runtime, runtime.EnterRoster(new short[] { 400, 401 }));
        }

        [Test]
        public void AnInspectRecordsOnlyTheRevealedRows() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            var rolls = new Queue<int>();
            rolls.Enqueue(0);   // the first offered row (Health) passes ...
            int Roll(int n) => rolls.Count > 0 ? rolls.Dequeue() : 99;   // ... every other fails

            runtime.AssessmentLines(fight.Party[0], fight.Enemies[1], Roll);

            Assert.IsTrue(runtime.Knowledge.TryGet(1, ActorAttribute.Health, out int health));
            Assert.AreEqual(50, health);
            Assert.IsFalse(runtime.Knowledge.TryGet(1, ActorAttribute.Defense, out _), "a failed roll is not recorded");
            Assert.IsFalse(runtime.Knowledge.Inspected(0), "knowledge is per enemy");
        }

        [Test]
        public void AFloatOnAnEnemyAddsToItsSeenTotal() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            runtime.NoteFloatedDamage(fight.Enemies[0], 6);
            runtime.NoteFloatedDamage(fight.Enemies[0], 0);
            runtime.NoteFloatedDamage(fight.Enemies[0], -4);
            runtime.NoteFloatedDamage(fight.Party[0], 9);

            Assert.AreEqual(6, runtime.Knowledge.Seen(0));
            Assert.AreEqual(0, runtime.Knowledge.Seen(1));
            CollectionAssert.AreEqual(new[] { "6", "miss", "4", "9" }, runtime.Log.Last(10));
        }

        [Test]
        public void TheFightEndingClearsKnowledgeAndLog() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            runtime.NoteFloatedDamage(fight.Enemies[0], 6);
            runtime.Knowledge.Record(0, ActorAttribute.Defense, 3);

            runtime.Leave();

            Assert.AreEqual(0, runtime.Knowledge.Seen(0));
            Assert.IsFalse(runtime.Knowledge.Inspected(0));
            Assert.IsEmpty(runtime.Log.Last(10));
        }
    }
}
