namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// A grid spell painting fire on the floor, and the floor burning whoever stands in it.
    /// </summary>
    public class TerrainFieldRuntimeTests {
        private static readonly System.Func<int, int> AlwaysLow = _ => 0;

        private static ActorStat[] StatBlock() {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 60, Max = 99 };
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
            var runtime = new CombatRuntime(session, null, Entries(), null);
            return (runtime, runtime.EnterRoster(new short[] { 400 }));
        }

        // Wrath of Killian's shape: the damage word IS the terrain kind it paints.
        private static Spell WrathOfKillian() =>
            new Spell("S") {
                TargetingType = 1,
                Calculation = SpellCalculation.CombatGridElement,
                Damage = CombatCapability.DenyingTerrain,
                Duration = 1,
            };

        [Test]
        public void TheGridArmPaintsTheCellUNDERTheTarget() {
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];

            Assert.IsTrue(runtime.ResolveCast(fight.Party[0], target, WrathOfKillian(),
                spellId: 19, power: 3, AlwaysLow));

            Assert.AreEqual(CombatCapability.DenyingTerrain,
                (int)runtime.Grid.TerrainAt(target.X, target.Y));
            Assert.AreEqual(3, runtime.Grid.EffectTimerAt(target.X, target.Y),
                "duration word 1 times the power");
        }

        [Test]
        public void StandingInItBURNS_andTheSweepAgesTheFieldToo() {
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            runtime.ResolveCast(fight.Party[0], target, WrathOfKillian(), spellId: 19, power: 3,
                AlwaysLow);
            int before = target.Health + target.Stamina;

            int burned = runtime.TickTerrainEffects(AlwaysLow);

            Assert.AreEqual(1, burned);
            Assert.AreEqual(before - TerrainDamage.MinimumDamage, target.Health + target.Stamina,
                "the low roll of the 10..19 band");
            Assert.AreEqual(2, runtime.Grid.EffectTimerAt(target.X, target.Y), "and it aged");
        }

        [Test]
        public void AFieldOnItsLastTickSTILLBurnsBeforeItLapses() {
            // *** WHY THE DECAY AND THE DAMAGE ARE ONE SWEEP. *** The original ages the cell before
            // asking whether its occupant burns, so a field expiring this tick still bites. Two
            // separate passes over the grid would let it expire without its last bite.
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            runtime.Grid.SetTileEffect(target.X, target.Y,
                (CombatTerrain)CombatCapability.DenyingTerrain, 0);
            int before = target.Health + target.Stamina;

            Assert.AreEqual(1, runtime.TickTerrainEffects(AlwaysLow), "it burned on the way out");
            Assert.Less(target.Health + target.Stamina, before);
        }

        [Test]
        public void AnEmptyFieldBurnsDownWithoutHurtingAnyone() {
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            runtime.Grid.SetTileEffect(0, 0, (CombatTerrain)CombatCapability.DenyingTerrain, 1);
            int before = target.Health + target.Stamina;

            Assert.AreEqual(0, runtime.TickTerrainEffects(AlwaysLow));
            Assert.AreEqual(before, target.Health + target.Stamina);
            Assert.AreEqual(0, runtime.Grid.EffectTimerAt(0, 0), "aged all the same");
        }
    }
}
