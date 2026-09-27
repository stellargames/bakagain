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
    /// Casts that hang a timed effect on their target rather than dealing damage.
    /// </summary>
    /// <remarks>
    /// <b>SpellEffectMagnitude answers 0 for this calculation ON PURPOSE.</b> The duration spells
    /// are a different arm of <c>cspell_resolve_cast</c>'s subkind switch — which is what our
    /// SpellCalculation enum is, offset by one. Treating that zero as "no effect" gives every buff
    /// and debuff in the game nothing at all.
    /// </remarks>
    public class SpellLingeringEffectTests {
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

        private static Spell TimedSpell(int durationField) =>
            new Spell("S") {
                TargetingType = 1,
                Calculation = SpellCalculation.CostTimesDuration,
                Duration = durationField,
            };

        private static int EffectCount(CombatEncounter fight, Combatant on) {
            var n = 0;
            foreach (ActiveSpellEffect _ in fight.Effects.EffectsOf(on)) {
                n++;
            }
            return n;
        }

        [Test]
        public void ACostTimesDurationCastHangsAnEffect_ratherThanDealingDamage() {
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            int before = target.Health + target.Stamina;

            Assert.IsTrue(runtime.ResolveCast(fight.Party[0], target, TimedSpell(3),
                spellId: 7, power: 4, AlwaysLow));

            Assert.AreEqual(1, EffectCount(fight, target), "the effect is on the chain");
            Assert.AreEqual(before, target.Health + target.Stamina, "and no damage was dealt");
        }

        [Test]
        public void ANegativeDurationFieldDIVIDESRatherThanMultiplying() {
            // *** THE SIGN FLIPS THE ARITHMETIC WITH NO FLAG TO ANNOUNCE IT. *** power 12 with a
            // field of 3 lasts 36 (+1); with a field of -3 it lasts 4 (+1). Reading the field as a
            // plain length inverts every spell that scales down.
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];

            runtime.ResolveCast(fight.Party[0], target, TimedSpell(-3), spellId: 7, power: 12,
                AlwaysLow);

            foreach (ActiveSpellEffect e in fight.Effects.EffectsOf(target)) {
                // A fresh encounter leaves everyone READY, so no bonus tick here — the bonus has
                // its own test below.
                Assert.AreEqual(4, e.Duration, "12 / 3");
                return;
            }
            Assert.Fail("no effect was registered");
        }

        [Test]
        public void AnEffectOnANOTREADYTargetLastsOneTickLonger() {
            // *** THE BONUS DEPENDS ON THE TARGET'S STATE, NOT THE CASTER'S OR THE SPELL'S. ***
            // A flat tick added AFTER the arithmetic, so it shifts every duration by one — easy to
            // miss entirely, and it is the whole of AdjustDurationForTarget.
            var (ready, readyFight) = Fight();
            Combatant readyTarget = readyFight.Enemies[0];
            readyTarget.Flags |= CombatantFlags.Ready;
            ready.ResolveCast(readyFight.Party[0], readyTarget, TimedSpell(2), spellId: 7, power: 5,
                AlwaysLow);

            var (spent, spentFight) = Fight();
            Combatant spentTarget = spentFight.Enemies[0];
            spentTarget.Flags &= ~CombatantFlags.Ready;
            spent.ResolveCast(spentFight.Party[0], spentTarget, TimedSpell(2), spellId: 7, power: 5,
                AlwaysLow);

            int readyDuration = -1, spentDuration = -1;
            foreach (ActiveSpellEffect e in readyFight.Effects.EffectsOf(readyTarget)) {
                readyDuration = e.Duration;
            }
            foreach (ActiveSpellEffect e in spentFight.Effects.EffectsOf(spentTarget)) {
                spentDuration = e.Duration;
            }

            Assert.AreEqual(10, readyDuration, "5 * 2, no bonus — the target is ready");
            Assert.AreEqual(11, spentDuration, "and one longer on a target that has acted");
        }

        [Test]
        public void TheRoundTickAgesEffectsAndFreesTheActorWhenTheyLapse() {
            // The effects age once per round, BEFORE Ready is restored — so an effect that expires
            // this round frees the actor for it rather than one round late.
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];
            target.Flags |= CombatantFlags.Ready;

            runtime.ResolveCast(fight.Party[0], target, TimedSpell(1), spellId: 7, power: 2,
                AlwaysLow);
            Assert.AreEqual(1, EffectCount(fight, target));

            for (var round = 0; round < 3; round++) {
                fight.BeginRound();
            }

            Assert.AreEqual(0, EffectCount(fight, target), "two ticks of duration, three rounds");
        }
        [Test]
        public void TheInfinityPoolAmplifiesEXACTLYONECast() {
            // *** ONE-SHOT. *** g_bStormAmplify is raised by the Pool's arm and cleared by the
            // resolution that reads it. A flag that stuck would amplify every later cast in the
            // fight for free — the item is spent, so the second cast must be plain again.
            var (runtime, fight) = Fight();
            Combatant target = fight.Enemies[0];

            runtime.SurchargeNextCast = true;
            runtime.ResolveCast(fight.Party[0], target, TimedSpell(3), spellId: 7, power: 8,
                AlwaysLow);
            Assert.IsFalse(runtime.SurchargeNextCast, "the cast took the flag with it");

            var durations = new List<int>();
            foreach (ActiveSpellEffect e in fight.Effects.EffectsOf(target)) {
                durations.Add(e.Duration);
            }
            Assert.AreEqual(1, durations.Count);
            Assert.AreEqual(36, durations[0], "(8 + 8/2) * 3, not 8 * 3");

            var (plain, plainFight) = Fight();
            plain.ResolveCast(plainFight.Party[0], plainFight.Enemies[0], TimedSpell(3),
                spellId: 7, power: 8, AlwaysLow);
            foreach (ActiveSpellEffect e in plainFight.Effects.EffectsOf(plainFight.Enemies[0])) {
                Assert.AreEqual(24, e.Duration, "an unamplified cast of the same spell");
                return;
            }
            Assert.Fail("no effect was registered");
        }
    }
}
