namespace BakAgain.Tests.Editor.Combat {
    using System.Reflection;
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData.Resources.Combat;
    using GameData.Resources.Spells;
    using NUnit.Framework;

    /// <summary>
    /// Hocho's Haven and Skin of the Dragon stop a SPELL, not only a swing — TASK-377's last site.
    /// </summary>
    /// <remarks>
    /// <b>These pin the CALL SITE, which is where this class of bug lives.</b>
    /// <c>AbsorbShieldWiringTests</c> already pins <c>CombatFormulas.ApplyDamage</c> itself and
    /// passes whatever the arguments say — so a caller handing it <c>absorbPool: null</c> and
    /// <c>negated: false</c> is invisible to it. That is exactly how <c>ApplySpellDamage</c> kept
    /// the defect after TASK-377 fixed the melee and ranged sites: its doc says it "bypasses armour
    /// the way the cost does", which is true of the armour flag and false of the other two.
    ///
    /// <para>The original settles it in <c>combat_arena_apply_damage</c> (COMBAT.C:339 and :355):
    /// the type-6 absorb and the type-0x17 negation are both gated on <c>source_type == 0</c>, and
    /// every spell-at-target call passes 0 (CSPELL.C:529, :550, :592, :1035, :1528, :2444). Only
    /// self-damage and recoil pass 1.</para>
    /// </remarks>
    public class SpellDamageRespectsDefensiveSpellsTests {
        private const int Magnitude = 12;
        private const int StartingStamina = 30;
        private const int StartingHealth = 40;

        // The shield's points are its slot Duration -- see CombatRuntime.AbsorbPoolOf.
        private const int ShieldPoints = 40;

        private static (CombatRuntime Runtime, CombatEncounter Fight, Combatant Target) Fight() {
            var runtime = new CombatRuntime(session: null);
            var fight = new CombatEncounter();
            var target = new Combatant {
                PartySlot = 0, ClassId = 2,
                Stamina = StartingStamina, Health = StartingHealth,
                X = 3, Y = 3, Flags = CombatantFlags.Ready,
            };
            fight.Enemies.Add(target);
            typeof(CombatRuntime).GetProperty("Encounter").SetMethod.Invoke(runtime, new object[] { fight });
            return (runtime, fight, target);
        }

        // ApplySpellDamage is private; it IS the seam under test, so reach it directly rather than
        // through a spell record that would drag half the cast pipeline in.
        private static void Cast(CombatRuntime runtime, Combatant target, int magnitude) =>
            typeof(CombatRuntime)
                .GetMethod("ApplySpellDamage", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runtime, new object[] { target, magnitude, (System.Func<int, int>)(n => 0) });

        [Test]
        public void AnAbsorbShieldStopsSpellDamage_NotOnlyBlows() {
            (CombatRuntime runtime, CombatEncounter fight, Combatant target) = Fight();
            fight.Effects.Register(target, SpellIds.HochosHaven, investedCost: 1,
                duration: ShieldPoints);

            Cast(runtime, target, Magnitude);

            Assert.AreEqual(StartingStamina, target.Stamina, "the shield should have taken it");
            Assert.AreEqual(StartingHealth, target.Health);
        }

        [Test]
        public void TheShieldIsSPENTBySpellDamage_soItCannotAbsorbForEver() {
            (CombatRuntime runtime, CombatEncounter fight, Combatant target) = Fight();
            fight.Effects.Register(target, SpellIds.HochosHaven, investedCost: 1,
                duration: ShieldPoints);

            Cast(runtime, target, Magnitude);

            int slot = fight.Effects.Find(target, SpellIds.HochosHaven);
            Assert.AreNotEqual(ActiveSpellEffectPool.None, slot, "the shield should still be up");
            Assert.AreEqual(ShieldPoints - Magnitude, fight.Effects[slot].Duration,
                "reading the pool without writing it back absorbs the same hit for ever");
        }

        [Test]
        public void SkinOfTheDragonZeroesSpellDamage() {
            (CombatRuntime runtime, CombatEncounter fight, Combatant target) = Fight();
            fight.Effects.Register(target, SpellIds.SkinOfTheDragon, investedCost: 1,
                duration: 5);

            Cast(runtime, target, Magnitude);

            Assert.AreEqual(StartingStamina, target.Stamina);
            Assert.AreEqual(StartingHealth, target.Health);
        }

        [Test]
        public void WithNoDefenceUpTheSpellStillLands() {
            // The control. Without this the three above would pass against a build that simply
            // dropped all spell damage on the floor.
            (CombatRuntime runtime, _, Combatant target) = Fight();

            Cast(runtime, target, Magnitude);

            Assert.AreEqual(StartingStamina - Magnitude, target.Stamina);
            Assert.AreEqual(StartingHealth, target.Health);
        }
    }
}
