namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// The creature-specific spell passes, joined to the resolver — <c>combat_ai_pick_action</c>
    /// running at the top of every targeted-cast attempt.
    /// </summary>
    /// <remarks>
    /// <b>The scan reads the acting actor's ENEMIES.</b> <see cref="OpportunisticCasts"/> carried
    /// the opposite claim, marked SETTLED, until the four spells were looked up in the shipped
    /// data: Bane of Black Slayers, Final Rest, Strength Drain and Thy Master's Will are all
    /// hostile. Handing the ally list in would have a monster draining its own pack's strength.
    /// </remarks>
    public class OpportunisticCastWiringTests {
        private const int CasterPattern = 1;

        private static Combatant Actor(int slot, int classId, int x, int y) => new Combatant {
            PartySlot = slot,
            ClassId = classId,
            Health = 20,
            Stamina = 20,
            X = x,
            Y = y,
            Flags = CombatantFlags.Ready,
        };

        /// <summary>A party caster facing one monster of the given kind, two tiles away.</summary>
        private static CombatEncounter Facing(int enemyClass, bool enemyDead = false) {
            var fight = new CombatEncounter();
            fight.Party.Add(Actor(1, 1, 3, 5));
            Combatant foe = Actor(0, enemyClass, 3, 7);
            if (enemyDead) {
                foe.Health = 0;
                foe.Flags |= CombatantFlags.Dead;
            }
            fight.Enemies.Add(foe);
            return fight;
        }

        private static MonsterTurnResolver Caster(System.Func<Combatant, int, bool> canCast) =>
            new MonsterTurnResolver(
                // canCastSpells drives the cascade to AiAction.Cast; the pattern gives it a row.
                _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                    spellcastPattern: CasterPattern),
                // *** 50 IS THE ONE VALUE THAT SATISFIES BOTH ROLLS, AND 0 SATISFIES NEITHER. ***
                // The attempt commits below 91 and the scan settles ABOVE 10 — opposite
                // directions — so anything in 11..90 reaches the passes. A fixture rolling 0
                // (which reads like "nothing random happens") skips every match instead, and the
                // two positive tests here failed on exactly that before this comment existed.
                // Rolling 100 fails the other way: no attempt ever commits.
                n => 50, null, isUnderground: false, canCast: canCast);

        [Test]
        public void ACasterFacingALivingBlackSlayerReachesForTheSpellNamedAfterIt() {
            CombatEncounter fight = Facing(OpportunisticCasts.BlackSlayerRisen);
            MonsterTurnResolver.Decision d =
                Caster((_, __) => true).Resolve(fight, fight.Party[0]);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.AreEqual(OpportunisticCasts.SpellAtLivingSlayer, d.SpellId);
            Assert.AreSame(fight.Enemies[0], d.Target);
        }

        [Test]
        public void AFallenBlackSlayerDrawsFinalRestRatherThanAnAttack() {
            // The counter to SlayerRevival, and the reason the fallen arm insists the body is
            // still on the grid: those are exactly the corpses that can get back up.
            CombatEncounter fight = Facing(OpportunisticCasts.BlackSlayerRisen, enemyDead: true);
            MonsterTurnResolver.Decision d =
                Caster((_, __) => true).Resolve(fight, fight.Party[0]);

            Assert.AreEqual(OpportunisticCasts.SpellAtFallenSlayer, d.SpellId);
        }

        [Test]
        public void AnOrdinaryCastCarriesNoSpellId() {
            // *** The distinction a consumer must not flatten. *** Only the opportunistic passes
            // name a spell; the ordinary cast path picks one later and elsewhere. Reading -1 as
            // "no cast" would drop every ordinary monster cast in the game.
            CombatEncounter fight = Facing(enemyClass: 2);
            MonsterTurnResolver.Decision d =
                Caster((_, __) => true).Resolve(fight, fight.Party[0]);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId);
            Assert.IsNotNull(d.Target, "the ordinary path still picks somebody");
        }

        [Test]
        public void ACasterThatCannotAffordTheSpellFallsBackToTheOrdinaryPath() {
            CombatEncounter fight = Facing(OpportunisticCasts.BlackSlayerRisen);
            MonsterTurnResolver.Decision d =
                Caster((_, __) => false).Resolve(fight, fight.Party[0]);

            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId);
        }

        [Test]
        public void NoCastabilitySeamMeansThePassesNeverFire() {
            // The default. A resolver built without the seam behaves exactly like a creature that
            // knows none of these spells — it must not fire them for free.
            CombatEncounter fight = Facing(OpportunisticCasts.BlackSlayerRisen);
            var noSeam = new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, true, false, CasterPattern),
                n => 50, null, isUnderground: false);

            Assert.AreEqual(OpportunisticCasts.NoSpell, noSeam.Resolve(fight, fight.Party[0]).SpellId);
        }

        [Test]
        public void TheScanReadsTheENEMIES_notTheActorsOwnSide() {
            // *** THE INVERTED CLAIM THIS CLOSES. *** Put the Black Slayer on the caster's OWN
            // side and nothing should fire: every one of these spells is hostile.
            var fight = new CombatEncounter();
            fight.Party.Add(Actor(1, 1, 3, 5));
            fight.Party.Add(Actor(2, OpportunisticCasts.BlackSlayerRisen, 4, 5));
            fight.Enemies.Add(Actor(0, 2, 3, 7));

            MonsterTurnResolver.Decision d =
                Caster((_, __) => true).Resolve(fight, fight.Party[0]);

            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId,
                "a Black Slayer standing with the caster is not a target");
        }
    }
}
