namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// What a pattern-driven monster does with an enemy in contact — the head of the cascade turn,
    /// <c>combataiturn_pick_tile_or_attack</c> (CBTAITRN.C:32).
    /// </summary>
    /// <remarks>
    /// <b>The caster and the shooter answer the same fact in opposite ways, and neither was wired.</b>
    /// <c>combatenc_ai_sel_execute_action</c> enters the caster branch through
    /// <c>combatenc_actor_can_cast_spells(actor, 0)</c> — the zero withholds the adjacency test — so
    /// an engaged caster is admitted and then spends the turn backing away. The shooter branch's door
    /// (<c>combatenc_show_missile_stat_row</c>) demands <c>1 &lt; nearestDist</c> outright, so an
    /// engaged shooter never gets in and melees instead.
    ///
    /// <para>Both were missing because <c>HotspotService.ProfileOf</c> fills
    /// <see cref="MonsterTurnResolver.Profile.CanCastSpells"/> and <c>CanShoot</c> from the MONST
    /// template's accuracies alone, which know nothing about where anyone is standing.</para>
    /// </remarks>
    public class MonsterDisengageWiringTests {
        /// <summary>Pattern 1's row is 1,8,6,3,7,2,4,5 — enough for the cascade to produce a cast.</summary>
        private const int CasterPattern = 1;

        /// <summary>A crossbow pattern that yields a shot; pattern 0 declines and would hide the rule.</summary>
        private const int ShootingPattern = 2;

        private static Combatant Actor(int slot, int x, int y) => new Combatant {
            PartySlot = slot,
            ClassId = 2,
            Health = 20,
            Stamina = 20,
            X = x,
            Y = y,
            Flags = CombatantFlags.Ready,
        };

        private static MonsterTurnResolver Caster() =>
            new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                    spellcastPattern: CasterPattern, crossbowAccuracy: 0, healthPercent: 100,
                    castingSkill: 50),
                n => 50, null, isUnderground: false, canCast: (_, __) => false);

        private static MonsterTurnResolver Shooter() =>
            new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: false, canShoot: true,
                    spellcastPattern: 0, crossbowPattern: ShootingPattern, crossbowAccuracy: 50,
                    healthPercent: 100),
                n => 50, null, isUnderground: false);

        /// <summary>The monster at (3,8) with one party member <paramref name="apart"/> tiles up.</summary>
        private static CombatEncounter Field(int apart) {
            var fight = new CombatEncounter();
            fight.Party.Add(Actor(1, 3, 8 - apart));
            fight.Enemies.Add(Actor(0, 3, 8));
            return fight;
        }

        [Test]
        public void AnEngagedCasterBacksAwayInsteadOfCasting() {
            // *** THE BUG. *** Closing with an enemy spellcaster shuts it down in the original, and
            // is a real tactic against them; the port let it keep casting from contact.
            CombatEncounter fight = Field(apart: 1);

            MonsterTurnResolver.Decision decision = Caster().Resolve(fight, fight.Enemies[0]);

            Assert.AreEqual(AiAction.Retreat, decision.Action);
        }

        [Test]
        public void ACasterWithRoomStillCasts() {
            // The other direction: a confirming reading alone cannot tell a working gate from one
            // that refuses everything.
            CombatEncounter fight = Field(apart: 2);

            MonsterTurnResolver.Decision decision = Caster().Resolve(fight, fight.Enemies[0]);

            Assert.AreEqual(AiAction.Cast, decision.Action);
        }

        [Test]
        public void EngagementIsContactOnly_NotTheCastersOwnClearance() {
            // EngagementRange is 1 and RangeIsClear is `>= 2` — the same boundary from both sides.
            // Two tiles is already clear, which is what makes the rule "in contact" rather than
            // "nearby", and getting it off by one would silence casters across half the arena.
            Assert.IsTrue(MonsterSpellcasting.MustDisengageBeforeCasting(1));
            Assert.IsFalse(MonsterSpellcasting.MustDisengageBeforeCasting(2));
            Assert.IsFalse(CombatCapability.RangeIsClear(1));
            Assert.IsTrue(CombatCapability.RangeIsClear(2));
        }

        [Test]
        public void AnEngagedShooterMeleesRatherThanShooting() {
            // NOT a retreat: the shooter is turned away at the branch door and falls through to
            // ordinary melee. Same fact about the field, opposite handling.
            CombatEncounter fight = Field(apart: 1);

            MonsterTurnResolver.Decision decision = Shooter().Resolve(fight, fight.Enemies[0]);

            Assert.AreEqual(AiAction.MeleeOrMove, decision.Action);
        }

        [Test]
        public void AShooterWithRoomStillShoots() {
            CombatEncounter fight = Field(apart: 2);

            MonsterTurnResolver.Decision decision = Shooter().Resolve(fight, fight.Enemies[0]);

            Assert.AreEqual(AiAction.Shoot, decision.Action);
        }
    }
}
