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
    /// The two post-animation hooks whose whole effect lives there — CSPELL.C:1458-1486.
    /// </summary>
    /// <remarks>
    /// <c>cspell_resolve_cast</c>, after <c>cspell_invoke_effect</c> and only on a hit: case 0x20
    /// (Final Rest) takes the target off the grid and kills it; case 0x24 (The Fetters of Rime)
    /// plays 0x4d and adds a Grief of 1000 Nights slot of <c>intensity * Duration</c>, which is what
    /// freezes the victim (Grief is one of the three incapacitating effects).
    /// </remarks>
    public class PostAnimationSpellTests {
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

        // The shipped records (generated/DAT/spells.json).
        private static Spell Fetters() => new Spell("S") {
            TargetingType = 0, Calculation = SpellCalculation.CostTimesDamage, Damage = 2, Duration = 1,
        };

        private static Spell FinalRest() => new Spell("S") {
            TargetingType = 7, Calculation = SpellCalculation.NonCostRelated, Damage = 0, Duration = 0,
        };

        [Test]
        public void TheFettersOfRimeFreezeTheTarget() {
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];

            Assert.IsTrue(runtime.ResolveCast(fight.Party[0], target, Fetters(),
                SpellIds.FettersOfRime, power: 5, AlwaysLow));

            int slot = fight.Effects.Find(target, SpellIds.GriefOfAThousandNights);
            Assert.AreNotEqual(ActiveSpellEffectPool.None, slot, "Grief is registered");
            // Measured in the original: power 5 on a chapter-1 rogue left a type-13 slot of 5.
            Assert.AreEqual(5, fight.Effects[slot].Duration, "cost x the record's Duration word");
            Assert.IsTrue(target.Incapacitated, "and Grief is what stops it acting");
        }

        [Test]
        public void FinalRestTakesABodyOffTheFieldSoItNeverRises() {
            // The type-7 cursor (COMBAT.C:2202-2207) only accepts a CAF_DEAD actor; Final Rest's
            // arm then removes it from the grid and kills it again with no animation.
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            target.ClassId = SlayerRevival.RisenType;
            fight.Kill(target);
            target.RevivalCountdown = 0;   // due to rise on the next sweep

            runtime.ResolveCast(fight.Party[0], target, FinalRest(), SpellIds.FinalRest, power: 5, AlwaysLow);

            Assert.AreEqual(SlayerRevival.OffGrid, target.X, "off the grid");
            Assert.AreEqual(DeathOutcome.RemovedFromField, runtime.LastDeath, "removed, not left as a corpse");
            runtime.TickSlayerRevivals(AlwaysLow);
            Assert.IsTrue(target.IsDead, "and it does not rise");
        }
    }
}
