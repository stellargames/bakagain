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
        private bool _enhanced, _combatOn;

        [SetUp]
        public void EnhancedCombatOn() {
            _enhanced = GameOptions.Enhanced;
            _combatOn = GameOptions.GetFeature(EnhancedFeature.Combat);
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.Combat, true);
        }

        [TearDown]
        public void Restore() {
            GameOptions.Enhanced = _enhanced;
            GameOptions.SetFeature(EnhancedFeature.Combat, _combatOn);
        }

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

        private static void Struck(Combatant target, Combatant by, FloatCause cause) {
            target.DamageFloatBy = by;
            target.DamageFloatCause = cause;
        }

        [Test]
        public void AFloatOnAnEnemyAddsToItsSeenTotal() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            runtime.NoteFloatedDamage(fight.Enemies[0], 6);
            runtime.NoteFloatedDamage(fight.Enemies[0], 0);
            runtime.NoteFloatedDamage(fight.Enemies[0], -4);
            runtime.NoteFloatedDamage(fight.Party[0], 9);

            Assert.AreEqual(6, runtime.Knowledge.Seen(0), "a miss adds nothing, a heal is not damage");
            Assert.AreEqual(0, runtime.Knowledge.Seen(1));
        }

        [Test]
        public void TheLogNamesActorVerbAndTargetAndHowItWentForEachKindOfFloat() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            Combatant hero = fight.Party[0];
            Combatant foeB = fight.Enemies[1];
            string heroName = runtime.LogNameForTest(hero);

            Struck(foeB, hero, FloatCause.Thrust);
            runtime.NoteFloatedDamage(foeB, 0);
            Struck(foeB, hero, FloatCause.Swing);
            runtime.NoteFloatedDamage(foeB, 4);
            Struck(foeB, hero, FloatCause.Shoot);
            runtime.NoteFloatedDamage(foeB, 6);
            Struck(foeB, hero, FloatCause.Cast);
            runtime.NoteFloatedDamage(foeB, 2);
            Struck(hero, foeB, FloatCause.Swing);
            runtime.NoteFloatedDamage(hero, 4);
            runtime.NoteFloatedDamage(hero, -3);

            string foe = runtime.LogNameForTest(foeB, nameFoe: true);
            CollectionAssert.AreEqual(new[] {
                heroName + " thrusts at B: miss",
                heroName + " swings at B: hit 4",
                heroName + " shoots B: hit 6",
                heroName + " casts at B: hit 2",
                foe + " swings at " + heroName + ": hit 4",
                heroName + " gains 3",
            }, runtime.Log.Last(10));
            StringAssert.EndsWith(" B", foe);
        }

        [Test]
        public void ClassicKeepsNoLog_ButTheSeenTotalIsStillKept() {
            GameOptions.SetFeature(EnhancedFeature.Combat, false);
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            runtime.NoteFloatedDamage(fight.Enemies[0], 6);
            runtime.LogLine("anything");

            Assert.IsEmpty(runtime.Log.Last(10), "nothing is written in Classic");
            Assert.AreEqual(6, runtime.Knowledge.Seen(0), "the invisible store may be filled");
        }

        [Test]
        public void TheRoundCountsUpFromOneAndClearsWithTheFight() {
            (CombatRuntime runtime, CombatEncounter fight) = Fight();
            Assert.AreEqual(1, runtime.Log.Round);
            for (int i = 0; i < 40 && runtime.Log.Round < 2; i++) {
                Combatant up = runtime.AdvanceToPartyTurn(_ => { });
                if (up == null) {
                    break;
                }
                fight.EndTurn();   // the party member passes
            }
            Assert.AreEqual(2, runtime.Log.Round, "the round boundary advanced the count");
            runtime.Leave();
            Assert.AreEqual(0, runtime.Log.Round);
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
