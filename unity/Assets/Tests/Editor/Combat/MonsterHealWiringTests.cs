namespace BakAgain.Tests.Editor.Combat {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using GameData.Resources.Spells;
    using NUnit.Framework;

    /// <summary>
    /// Action slot 1 joined to the resolver — the AI spending its turn on an ally instead of an
    /// enemy. <see cref="MonsterHealTurn"/>.
    /// </summary>
    public class MonsterHealWiringTests {
        /// <summary>Pattern 1's row is 1,8,6,3,7,2,4,5 — so slot 1 gets the first attempt.</summary>
        private const int PatternWhoseFirstSlotIsTheHeal = 1;

        private readonly Dictionary<Combatant, int> _healthPercent = new Dictionary<Combatant, int>();

        private Combatant Actor(int slot, int health, int percent) {
            var c = new Combatant {
                PartySlot = slot,
                ClassId = 2,
                Health = health,
                Stamina = 20,
                // *** THE OPPONENT SIDE IS PARKED OUT OF CONTACT ON PURPOSE. ***
                // Everyone used to sit on column 3 at y = slot + 4, which put the caster at (3,5)
                // one tile from the enemy at (3,4) — and an engaged caster does not cast at all, it
                // retreats (AiAction.Retreat), so every one of these support-turn tests measured a
                // state the game cannot reach. Off the column as well as away from it: the ally at
                // (3,6) would otherwise screen the target and the cast would fail for cover.
                X = slot == 0 ? 6 : 3,
                Y = slot == 0 ? 9 : slot + 4,
                Flags = CombatantFlags.Ready,
            };
            _healthPercent[c] = percent;
            return c;
        }

        private MonsterTurnResolver Resolver(System.Func<Combatant, int, bool> canCast) =>
            new MonsterTurnResolver(
                c => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                    spellcastPattern: PatternWhoseFirstSlotIsTheHeal, crossbowAccuracy: 0,
                    healthPercent: _healthPercent.TryGetValue(c, out int p) ? p : 100),
                // 50 clears the morale check, commits the attempt (< 91) and is the RND(80) the
                // spell choice is tested against. See OpportunisticCastWiringTests for why 0 and
                // 100 each fail one of those.
                n => 50, null, isUnderground: false, canCast: canCast);

        [Test]
        public void AWoundedAllyDrawsTheRestore() {
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 5, percent: 25));
            fight.Enemies.Add(Actor(0, health: 20, percent: 100));

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.AreEqual(SpellIds.GiftOfSung, d.SpellId);
            Assert.AreSame(fight.Party[1], d.Target);
        }

        [Test]
        public void TheRecipientIsNOTRecordedAsTheCastersTarget() {
            // *** THE ONE THING THE WIRING MUST NOT DO. *** monster_healAnAlly casts straight and
            // never writes actor->inner->target. That field feeds AttackersAlready and
            // TargetsTheLeader, so an ally in it would have the pack counting a healer among the
            // attackers converging on a party member nobody is attacking.
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 5, percent: 25));
            fight.Enemies.Add(Actor(0, health: 20, percent: 100));

            Resolver((_, __) => true).Resolve(fight, caster);

            Assert.IsNull(caster.Target, "the heal's recipient is not a target");
        }

        [Test]
        public void TheSpellIsChosenFromTheLASTOpponentScanned() {
            // The running minimum is computed and never read. Two opponents at 1 and 99: the
            // minimum would fire the restore, the last one does not, so it falls to the ward.
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 5, percent: 25));
            fight.Enemies.Add(Actor(0, health: 1, percent: 5));
            fight.Enemies.Add(Actor(0, health: 99, percent: 100));

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreEqual(SpellIds.HochosHaven, d.SpellId);
        }

        [Test]
        public void AProbeAlreadyCarryingTheWardStopsTheSupportTurn() {
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 5, percent: 25));
            fight.Enemies.Add(Actor(0, health: 99, percent: 100));
            // The probe is party[0] — the caster itself here — and NOT the recipient.
            fight.Effects.Register(caster, SpellIds.HochosHaven, investedCost: 1, duration: 5,
                flag: 0);

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreNotEqual(SpellIds.HochosHaven, d.SpellId);
        }

        [Test]
        public void AHealthyPackMeansTheTurnFallsThroughToAnAttack() {
            // Nobody eligible, so slot 1 declines and the ordinary cast path takes the turn.
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 20, percent: 100));
            fight.Enemies.Add(Actor(0, health: 20, percent: 100));

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreEqual(MonsterHealTurn.NoSpell, d.SpellId);
            Assert.IsNotNull(d.Target, "and it picks an enemy instead");
        }

        [Test]
        public void ACasterThatIsItselfTheOnlyWoundedOneDoesNotHealItself() {
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 20, percent: 30);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 20, percent: 100));
            fight.Enemies.Add(Actor(0, health: 20, percent: 100));

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreEqual(MonsterHealTurn.NoSpell, d.SpellId);
        }

        [Test]
        public void ACasterTooHurtToHelpSkipsTheSupportTurn() {
            // Health must be strictly above the ladder's 10.
            var fight = new CombatEncounter();
            Combatant caster = Actor(1, health: 10, percent: 100);
            fight.Party.Add(caster);
            fight.Party.Add(Actor(2, health: 5, percent: 25));
            fight.Enemies.Add(Actor(0, health: 20, percent: 100));

            MonsterTurnResolver.Decision d = Resolver((_, __) => true).Resolve(fight, caster);

            Assert.AreEqual(MonsterHealTurn.NoSpell, d.SpellId);
        }
    }
}
