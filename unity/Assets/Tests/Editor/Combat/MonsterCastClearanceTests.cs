namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// The ordinary cast's target selection — two passes, and the first one discriminates.
    /// <see cref="MonsterCasterTurn"/>.
    /// </summary>
    /// <remarks>
    /// <b>This ran as a single pass with clearance 0, which is only ever the RETRY.</b> So every
    /// monster caster targeted as though it were an expert, the same shape as the shooter bug the
    /// resolver's own remarks describe.
    /// </remarks>
    public class MonsterCastClearanceTests {
        /// <summary>Pattern 1's row is 1,8,6,3,7,2,4,5; slot 6 is the first targeted cast in it.</summary>
        private const int CasterPattern = 1;

        private static Combatant Actor(int slot, int x, int y) => new Combatant {
            PartySlot = slot,
            ClassId = 2,
            Health = 20,
            Stamina = 20,
            X = x,
            Y = y,
            Flags = CombatantFlags.Ready,
        };

        private static MonsterTurnResolver Caster(int castingSkill) =>
            new MonsterTurnResolver(
                // Pool at 40%: a caster that casts nothing then guards rather than advancing on
                // someone (CBTAI.C:373), so Decision.Target stays the last attempt's aim these tests read.
                _ => new MonsterTurnResolver.Profile(0, 40, canCastSpells: true, canShoot: false,
                    spellcastPattern: CasterPattern, crossbowAccuracy: 0, healthPercent: 100,
                    castingSkill: castingSkill),
                // A book with one kind-1 spell: a target with no spell to cast at it is a failed
                // attempt, not a turn (TASK-844), so the target readings need something castable.
                // n == 2 is the picker's coin flip, kept heads.
                n => n == 2 ? 0 : 50, null, isUnderground: false, canCast: (_, id) => id == 22,
                spells: Book((22, 1)));

        /// <summary>A party member with the acting monster's OWN PACK crowded round it.</summary>
        /// <remarks>
        /// <b>CLEARANCE COUNTS THE MONSTERS NEAR THE TARGET, NOT THE PARTY.</b> A first cut of this
        /// fixture packed party members round the victim and measured nothing at all:
        /// <c>combatenc_party_within_cheby</c> scans the encounter roster despite its name, which is
        /// what makes the pack SPREAD OUT instead of converging, and the resolver's own remarks
        /// already said so.
        ///
        /// <para>Nothing stands on the column between (3,8) and (3,5) either, because the first
        /// pass also demands a clear projectile path — a screened target fails for a different
        /// reason and would hide whatever the clearance did.</para>
        /// </remarks>
        private static CombatEncounter CrowdedTarget() {
            var fight = new CombatEncounter();
            fight.Party.Add(Actor(1, 3, 5));
            fight.Enemies.Add(Actor(0, 3, 8));
            fight.Enemies.Add(Actor(0, 4, 6));
            fight.Enemies.Add(Actor(0, 2, 6));
            return fight;
        }

        [Test]
        public void TheSkillActuallyReachesTheSelector() {
            // A guard on the plumbing, not the rule: the clearance is derived from the CASTER'S
            // own skill, so the two ends of the ladder must not agree by construction.
            Assert.AreEqual(4, MonsterCasterTurn.ClearanceFor(0));
            Assert.AreEqual(0, MonsterCasterTurn.ClearanceFor(100));
            Assert.AreEqual(0, MonsterCasterTurn.RetryClearance);
        }

        [Test]
        public void BothEndsOfTheSkillLadderStillFindSomebody() {
            // *** THE RETRY IS WHAT MAKES THIS SAFE. *** A novice's first pass may refuse the
            // crowded target, but the second pass runs with clearance 0 and takes it anyway — so
            // adding the discrimination must not leave casters idle.
            CombatEncounter novice = CrowdedTarget();
            MonsterTurnResolver.Decision n = Caster(0).Resolve(novice, novice.Enemies[0]);
            Assert.AreEqual(AiAction.Cast, n.Action);
            Assert.IsNotNull(n.Target, "a novice still acts, via the retry");

            CombatEncounter expert = CrowdedTarget();
            MonsterTurnResolver.Decision e = Caster(100).Resolve(expert, expert.Enemies[0]);
            Assert.IsNotNull(e.Target, "and an expert on its first pass");
        }

        /// <summary>A pack-mate standing ON the firing line AND close to the victim.</summary>
        /// <remarks>
        /// The one geometry where the two passes disagree, and therefore the only one that can
        /// prove the first pass has a clearance at all. The blocker is within 4 of the victim, so a
        /// NOVICE's first pass refuses on clearance and the retry — which does not test line of
        /// sight — takes the shot anyway. An EXPERT's first pass accepts on clearance, then fails
        /// on the cover the novice never got as far as testing.
        /// </remarks>
        private static CombatEncounter ScreenedAndCrowded() {
            var fight = new CombatEncounter();
            fight.Party.Add(Actor(1, 3, 5));
            fight.Enemies.Add(Actor(0, 3, 8));
            fight.Enemies.Add(Actor(0, 3, 6));
            return fight;
        }

        [Test]
        public void ANoviceShootsPastCoverAnExpertRefusesIt() {
            // *** THE TEST THAT ACTUALLY DISCRIMINATES. *** An earlier version of this file only
            // asserted that both ends of the ladder found SOMEBODY, which stayed true with the
            // first pass reverted to clearance 0 — so it proved nothing and a mutation ran clean
            // through it. The passes differ in TWO ways at once (clearance and the line-of-sight
            // test), and this is the arrangement where those two disagree.
            //
            // Pattern 1's row puts its mode-0 slot at attempt 5 with two non-matching slots after
            // it, so a turn that never commits ends up aiming at nobody — which is what makes the
            // difference readable off Decision.Target.
            CombatEncounter novice = ScreenedAndCrowded();
            Assert.IsNotNull(Caster(0).Resolve(novice, novice.Enemies[0]).Target,
                "the novice's first pass refuses on clearance, and the retry ignores cover");

            CombatEncounter expert = ScreenedAndCrowded();
            Assert.IsNull(Caster(100).Resolve(expert, expert.Enemies[0]).Target,
                "the expert accepts on clearance and is then stopped by the cover");
        }

        [Test]
        public void TheHealthGateIsStrictlyGreaterAndSharedByBothPasses() {
            // Both threshold entries hold 10, so a caster on exactly 10 commits nothing on either
            // pass. It still SELECTS on every attempt — the gates stop the cast, not the targeting
            // — which is why a too-hurt caster ends its turn aiming wherever the LAST slot in its
            // row pointed rather than at nobody in particular.
            Assert.False(MonsterCasterTurn.HealthAllowsCasting(10,
                CombatCapability.ShippedHealthThresholds,
                MonsterCasterTurn.FirstPassThresholdIndex));
            Assert.False(MonsterCasterTurn.HealthAllowsCasting(10,
                CombatCapability.ShippedHealthThresholds,
                MonsterCasterTurn.RetryThresholdIndex));
            Assert.True(MonsterCasterTurn.HealthAllowsCasting(11,
                CombatCapability.ShippedHealthThresholds,
                MonsterCasterTurn.FirstPassThresholdIndex));
        }

        [Test]
        public void ACasterOnTheGateFloorNeverCommits() {
            CombatEncounter fight = CrowdedTarget();
            Combatant caster = fight.Enemies[0];
            caster.Health = 10;

            MonsterTurnResolver.Decision d = Caster(100).Resolve(fight, caster);

            Assert.AreEqual(AiAction.Cast, d.Action, "it still chose to cast");
            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId, "and never got a spell off");
        }

        [Test]
        public void HealthOneAboveTheGateIsEnough() {
            CombatEncounter fight = CrowdedTarget();
            Combatant caster = fight.Enemies[0];
            caster.Health = 11;

            Assert.IsNotNull(Caster(100).Resolve(fight, caster).Target);
        }

        // ------------------------------------------------ TASK-844: the spell kind per pass

        /// <summary>
        /// A catalogue dense up to the highest id — the picker scans 0..Count-1, as the shipped
        /// table is — with only the listed spells martial.
        /// </summary>
        internal static System.Collections.Generic.Dictionary<int, GameData.Resources.Spells.Spell>
            Book(params (int Id, int Kind)[] spells) {
            var book = new System.Collections.Generic.Dictionary<int, GameData.Resources.Spells.Spell>();
            for (int i = 0; i <= 44; i++) {
                book[i] = new GameData.Resources.Spells.Spell("filler");
            }
            foreach ((int id, int kind) in spells) {
                book[id] = new GameData.Resources.Spells.Spell("S") { IsMartial = true, TargetingType = kind };
            }
            return book;
        }

        /// <summary>Every roll 0: every attempt commits and every coin flip keeps its candidate.</summary>
        private static MonsterTurnResolver CasterWithBook(int castingSkill,
            System.Collections.Generic.Dictionary<int, GameData.Resources.Spells.Spell> book) =>
            new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 40, canCastSpells: true, canShoot: false,
                    spellcastPattern: CasterPattern, crossbowAccuracy: 0, healthPercent: 100,
                    castingSkill: castingSkill),
                n => 0, null, isUnderground: false, canCast: (_, id) => book[id].IsMartial,
                spells: book);

        [Test]
        public void TheFirstPassTakesKindZeroBeforeAHigherKindOne() {
            // CBTAI.C:160-163: kind 0 over the whole book first, kind 1 only on -1. One high-to-low
            // scan accepting either kind picked 22 here.
            CombatEncounter fight = CrowdedTarget();
            MonsterTurnResolver.Decision d =
                CasterWithBook(100, Book((4, 0), (22, 1))).Resolve(fight, fight.Enemies[0]);
            Assert.AreEqual(4, d.SpellId);
            Assert.IsFalse(d.RollsToHit, "the first pass's check is the line of fire");
        }

        [Test]
        public void TheRetryTakesKindOneOnlyAndRollsToHit() {
            // A novice's first pass refuses the crowded target; the retry (CBTAI.C:174) asks for
            // kind 1 alone and casts through the to-hit roll (:33-42).
            CombatEncounter fight = CrowdedTarget();
            MonsterTurnResolver.Decision d =
                CasterWithBook(0, Book((4, 0), (22, 1))).Resolve(fight, fight.Enemies[0]);
            Assert.AreEqual(22, d.SpellId);
            Assert.IsTrue(d.RollsToHit);
        }

        [Test]
        public void ARetryWithOnlyKindZeroCastsNothing() {
            CombatEncounter fight = CrowdedTarget();
            MonsterTurnResolver.Decision d =
                CasterWithBook(0, Book((4, 0))).Resolve(fight, fight.Enemies[0]);
            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId);
            Assert.IsNotNull(d.Fallback, "no spell on any slot ends in the caster's tail");
        }
    }
}
