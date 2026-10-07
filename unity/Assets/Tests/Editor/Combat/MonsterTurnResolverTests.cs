namespace BakAgain.Tests.Editor.Combat {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// What a monster decides on its turn — the AI wired into the turn loop's resolver seam.
    /// </summary>
    public class MonsterTurnResolverTests {
        private static readonly int[] Thresholds = { 85, 55, 45, 35, 25, 20, 10, 5, 5, 0 };

        // Class 0x40 is not in the species-routine set, so it falls through the cascade.
        private const int CascadeClass = 0x40;

        // 0x13 is the first of the species-routine classes, and the only distance-conditional one.
        private const int SpeciesClass = 0x13;

        // Named for what the BODIES do, not for the original's function names -- all three of
        // those are misleading. 0x38 opens with a ranged knockback, 0x29 is gated on a coin flip
        // with no minimum distance, and 0x1d shoots from three tiles and charges nothing.
        private const int RangedKnockbackSpeciesClass = 0x38;
        private const int CoinFlipRangedSpeciesClass = 0x29;
        private const int ShootAtRangeSpeciesClass = 0x1d;

        private static Combatant Monster(int classId = CascadeClass, int speed = 5, int x = 0, int y = 0) =>
            new Combatant {
                PartySlot = 0, ClassId = classId,   // dropping ClassId here silently disabled the
                Health = 20, Stamina = 10,          // species-routine test: everything fell through
                Speed = speed, X = x, Y = y,        // the cascade as class 0.
            };

        // *** HEALTH 20, NOT 10, AND THE DIFFERENCE IS LOAD-BEARING FOR THE CAST TESTS. ***
        // The cast gate is `health > g_anStatCheckThreshold[i]` and those entries hold 10, so a
        // caster on exactly 10 can never commit an attempt. It still SELECTS on every attempt —
        // combatenc_ai_pick_target_by_role clears and refills actor->inner->target each time — so
        // such a caster ends its turn aiming wherever the LAST slot in its row pointed, which is
        // usually nowhere. Every "the row's mode decides WHO" test below reads the FIRST committing
        // slot, so they need a caster that can actually commit. At 10 they passed by accident.
        private const int CastGateFloor = 10;

        /// <summary>
        /// A caster whose first-pass clearance is 0 — <c>4 - 100/25</c>.
        /// </summary>
        /// <remarks>
        /// <b>The role tests need this or they measure the clearance instead.</b> Clearance counts
        /// the caster's OWN side near the candidate, and the caster itself is in that roster — so a
        /// novice refuses the party member standing next to it and reaches for the far one, which
        /// looks exactly like a role filter picking someone unexpected. Pinning the skill at expert
        /// leaves the row's mode as the only thing deciding.
        /// </remarks>
        private const int ExpertCaster = 100;

        private static Combatant Member(int x, int y, int speed = 1) =>
            new Combatant { PartySlot = 1, Health = 10, Stamina = 10, Speed = speed, X = x, Y = y };

        private static CombatEncounter Fight(Combatant monster, params Combatant[] party) {
            var enc = new CombatEncounter();
            enc.Enemies.Add(monster);
            foreach (Combatant m in party) {
                enc.Party.Add(m);
            }
            enc.BeginRound();
            return enc;
        }

        private static MonsterTurnResolver Resolver(
            int fleeThreshold = 0, int staminaPercent = 100, bool canCast = false, bool canShoot = false,
            int roll = 99, bool underground = false, int crossbowAccuracy = 0,
            int spellcastPattern = 0, bool partyCanCast = false, bool partyCanShoot = false,
            int partyStaminaPercent = 100,
            // *** DEFAULTS TO A SHOOTER AS THE DATA SHIPS THEM, NOT TO 0. *** Pattern 0 means the
            // crossbow branch yields nothing (MonsterActionPatterns.Shoots), so a 0 default would
            // make every `canShoot: true` test below quietly assert melee. Every monster in MONST
            // that shoots at all carries (2,7) here, so 2 is the honest stand-in; the pattern-0
            // case gets its own tests rather than being the default nobody notices.
            int crossbowPattern = 2, int meleeMovePattern = 0) =>
            new MonsterTurnResolver(
                // *** The profile MUST distinguish the sides. *** A single shared profile makes the
                // party members casters too, which silently satisfies a Spellcaster-role slot and
                // turns a fall-through test into a first-slot match.
                c => c.IsPartyMember
                    ? new MonsterTurnResolver.Profile(0, partyStaminaPercent, partyCanCast, partyCanShoot)
                    : new MonsterTurnResolver.Profile(
                        fleeThreshold, staminaPercent, canCast, canShoot, spellcastPattern,
                        crossbowAccuracy, healthPercent: 100, castingSkill: ExpertCaster,
                        crossbowPattern: crossbowPattern, meleeMovePattern: meleeMovePattern),
                _ => roll, Thresholds, underground, canCast: (_, id) => id == 22, spells: KindOneBook);

        /// <summary>
        /// One castable kind-1 spell (Mind Melt's slot). Without a spell an attempt that finds a
        /// target is a failed attempt and the row walks on (TASK-844, CBTAI.C:363-370), so the
        /// targeting tests need something to cast.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<int, GameData.Resources.Spells.Spell>
            KindOneBook = MonsterCastClearanceTests.Book((22, 1));

        [Test]
        public void ACascadeMonsterWithNoCapabilitiesClosesToMelee() {
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d = Resolver().Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
            Assert.AreSame(d.Target, m.Target, "the decision is recorded on the monster");
        }

        [Test]
        public void SpellcastingOutranksShootingWhichOutranksMelee() {
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Cast,
                Resolver(canCast: true, canShoot: true).Resolve(enc, m).Action);
            // A pattern-2 shooter only shoots a target its row names (TASK-528), so the party here is
            // missile-capable and every attempt commits.
            Assert.AreEqual(AiAction.Shoot,
                Resolver(canCast: false, canShoot: true, roll: 0, partyCanShoot: true, crossbowAccuracy: 75).Resolve(enc, m).Action);
            Assert.AreEqual(AiAction.MeleeOrMove,
                Resolver(canCast: false, canShoot: false).Resolve(enc, m).Action);
        }

        [Test]
        public void AClassWithItsOwnRoutineNeverReachesTheCascade() {
            // The class switch sits IN FRONT of the cascade, so a spell-capable creature on that
            // list still runs its routine and never reaches the cascade's Cast. 0x38 at two tiles
            // takes its own RANGED branch, and Shoot is something the cascade would never produce
            // for a canCast creature — which is what makes it evidence.
            Combatant m = Monster(RangedKnockbackSpeciesClass);
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Shoot,
                Resolver(canCast: true).Resolve(enc, m).Action);
        }

        // ---- the species switch (CBENC.C:925) ---------------------------------------------

        [Test]
        public void ASpeciesClassATTACKSInsteadOfForfeitingItsTurn() {
            // *** THE BUG THIS FIXES. *** Every one of the thirteen bespoke classes used to resolve
            // to AiAction.SpeciesSpecific, and CombatRuntime.ResolveEnemyTurn carries out ONLY
            // MeleeOrMove — so they all stood still through their own turns while reading, from the
            // outside, like an AI that had been "decided correctly and not acted on".
            //
            // ADJACENT on purpose: 0x38's ranged branch needs two tiles, so at one tile it takes the
            // near fallback and this asserts the half that actually executes. The first version of
            // this test put the target at two tiles and expected melee, which was my own misreading
            // of combataiact_actor_melee_attack — a name whose first branch is ranged.
            Combatant m = Monster(RangedKnockbackSpeciesClass);
            CombatEncounter enc = Fight(m, Member(1, 0));

            MonsterTurnResolver.Decision d = Resolver().Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
            Assert.AreSame(d.Target, m.Target, "the decision is recorded on the monster");
        }

        [Test]
        public void ARangedSpeciesClassShootsRatherThanClosing() {
            // 0x29 is one of the four on combataiact_ranged_attack_turn. Lumping these in with the
            // melee group would have them walk into contact, which is the opposite of the routine.
            Combatant m = Monster(CoinFlipRangedSpeciesClass);
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Shoot, Resolver().Resolve(enc, m).Action);
        }

        [Test]
        public void ASpeciesClassAimsAtTheNEARESTOpponentEvenWhenAnAllyAlreadyHasIt() {
            // Every species routine opens with combatenc_find_nearest_actor, which has no attacker
            // cap. Measured in entry 93 on 2026-09-14: five 0x39s all shot Gorath, the party member
            // nearest to each of them. The melee selector's cap would send this one to the far member.
            Combatant m = Monster(CoinFlipRangedSpeciesClass, x: 2, y: 4);
            Combatant nearMember = Member(2, 2);
            Combatant farMember = Member(6, 4);
            CombatEncounter enc = Fight(m, nearMember, farMember);
            Combatant ally = Monster(x: 7, y: 9);
            ally.Target = nearMember;
            enc.Enemies.Add(ally);

            Assert.AreSame(nearMember, Resolver().Resolve(enc, m).Target);
        }

        [Test]
        public void THESOCALLEDChargeClassShootsFromThreeTilesAndCLOSESInside() {
            // 0x1d's routine is named combataiact_action_charge_near and charges nothing: at three
            // tiles with a passing roll it shoots, and only closer does it fall back.
            Combatant far = Monster(ShootAtRangeSpeciesClass);
            Assert.AreEqual(AiAction.Shoot,
                Resolver().Resolve(Fight(far, Member(3, 0)), far).Action);

            Combatant near = Monster(ShootAtRangeSpeciesClass);
            Assert.AreEqual(AiAction.MeleeOrMove,
                Resolver().Resolve(Fight(near, Member(2, 0)), near).Action);
        }

        [Test]
        public void THEDISTANCEConditionalClassMeleesWhenAdjacent() {
            // 0x13 alone branches on range: combataiact_pick_melee_or_missl swings when the nearest
            // actor is within one tile (CBTAIACT.C:28).
            Combatant m = Monster(SpeciesClass);
            CombatEncounter enc = Fight(m, Member(1, 0));

            MonsterTurnResolver.Decision d = Resolver().Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
        }

        [Test]
        public void ATRangeTheDistanceConditionalClassCastsOrShootsOnTheRoll() {
            // Past arm's length the original rolls RND(10) >= distance: high picks the SPELL, low
            // the shot. Distance here is 2, so 99 casts and 0 shoots.
            //
            // *** BOTH BRANCHES KEEP THE NEAREST ACTOR. *** The original finds the target once,
            // before it decides, so the cast does NOT go through the spell-pattern rows the cascade
            // uses — which is why this class is resolved outside PickCastTarget.
            Combatant near = Monster(SpeciesClass);
            CombatEncounter castEnc = Fight(near, Member(2, 0));
            MonsterTurnResolver.Decision cast = Resolver(roll: 99).Resolve(castEnc, near);

            Assert.AreEqual(AiAction.Cast, cast.Action);
            Assert.AreSame(castEnc.Party[0], cast.Target);
            // TASK-508: without the spell id RunEnemyTurn casts nothing.
            Assert.AreEqual(MonsterTurnRoutines.DefaultSpellKind, cast.SpellId);
            Assert.IsNull(cast.CastPower, "combat_ai_actor_cast_spell uses the AI's usual power");

            Combatant other = Monster(SpeciesClass);
            CombatEncounter shootEnc = Fight(other, Member(2, 0));

            Assert.AreEqual(AiAction.Shoot, Resolver(roll: 0).Resolve(shootEnc, other).Action);
        }

        [Test]
        public void THEWANDERINGClassCastsItsRolledSpellAtHalfItsHealth() {
            // CBTAIACT.C:71 -- roll < 0x32 picks spell 5, the power is halfStat, not the AI cost.
            const int WanderingSpeciesClass = 0x31;
            Combatant wanderer = Monster(WanderingSpeciesClass);
            CombatEncounter enc = Fight(wanderer, Member(2, 0));
            MonsterTurnResolver.Decision d = Resolver(roll: 40).Resolve(enc, wanderer);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.AreEqual(MonsterTurnRoutines.AlternateSpellKind, d.SpellId);
            Assert.AreEqual(wanderer.Health / 2, d.CastPower);
        }

        [Test]
        public void ARoutingMonsterFleesAndPicksNobody() {
            // Morale runs first and preempts everything, including the class routine.
            Combatant m = Monster(SpeciesClass);
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d =
                Resolver(fleeThreshold: 8, staminaPercent: 10, roll: 0).Resolve(enc, m);

            Assert.AreEqual(AiAction.Flee, d.Action);
            Assert.IsNull(d.Target);
            Assert.AreEqual(CombatantFlags.Fleeing, m.Flags & CombatantFlags.Fleeing);
        }

        [Test]
        public void NothingRoutsUnderground() {
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d =
                Resolver(fleeThreshold: 8, staminaPercent: 10, roll: 0, underground: true)
                    .Resolve(enc, m);

            Assert.AreNotEqual(AiAction.Flee, d.Action, "a dungeon fight is to the finish");
        }

        [Test]
        public void OnceRoutingItKeepsRoutingOnLaterTurns() {
            // The flag persists across rounds — which only holds because CAF_FLEE is 0x10 and the
            // round reset clears 0x04. It was 0x20 until the flags were fixed, and BeginRound wiped
            // the decision every round.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));
            Resolver(fleeThreshold: 8, staminaPercent: 10, roll: 0).Resolve(enc, m);

            enc.BeginRound();

            // A roll that would never start a rout; it is already routing.
            Assert.AreEqual(AiAction.Flee, Resolver(roll: 99).Resolve(enc, m).Action);
        }

        [Test]
        public void ItPicksTheNearestCandidate() {
            Combatant m = Monster(x: 0, y: 0);
            CombatEncounter enc = Fight(m, Member(9, 0), Member(2, 0), Member(6, 0));

            Assert.AreSame(enc.Party[1], Resolver().Resolve(enc, m).Target);
        }

        [Test]
        public void ADeadPartyMemberIsNeverPicked() {
            Combatant m = Monster();
            Combatant corpse = Member(1, 0);
            corpse.Flags |= CombatantFlags.Dead;
            CombatEncounter enc = Fight(m, corpse, Member(4, 0));

            Assert.AreSame(enc.Party[1], Resolver().Resolve(enc, m).Target);
        }

        [Test]
        public void ASHOOTERSkipsACandidateCrowdedByTheMONSTERSOwnSide() {
            // *** The clearance rule is about the monsters' positions, not the party's, and it is
            // RANGED-ONLY. *** A second monster already standing next to the near target pushes the
            // chooser onto the far one. Filling it from the party would invert it; applying it to a
            // melee monster would be the wrong selector's rule entirely
            // (combat_selectTargetByCriterion @0x64ff6 has no clearance parameter).
            //
            // Accuracy 25 -> clearance 3, via CombatAi.AllyClearanceForAccuracy. Clearance counts
            // monsters STRICTLY within 3, and the chooser counts itself.
            //
            // The shot is the row's missile-capable slot (radius 10) with every attempt committing,
            // and the clear target stands off the crowded line: the crowded pair would otherwise block
            // the line of fire and the shot would not be taken at all (TASK-528).
            Combatant chooser = Monster(x: 0, y: 0);
            Combatant crowder = Monster(x: 3, y: 0);
            var enc = new CombatEncounter();
            enc.Enemies.Add(chooser);
            enc.Enemies.Add(crowder);
            enc.Party.Add(Member(2, 0));    // crowded by both monsters
            enc.Party.Add(Member(0, 6));    // clear, and in a clear line
            enc.BeginRound();

            Combatant picked = Resolver(canShoot: true, crossbowAccuracy: 25, roll: 0, partyCanShoot: true)
                .Resolve(enc, chooser).Target;

            Assert.AreSame(enc.Party[1], picked);
        }

        [Test]
        public void MELEESpreadsByTheSATURATIONCap_NotByClearance() {
            // *** The melee half spreads too, by a different rule. *** Two attackers, two targets
            // gives a cap of 1, so the second monster may not pile onto the candidate the first
            // already chose even though it is nearer. Dropping the (wrong) clearance rule from the
            // melee path without this makes the pack converge on whoever is closest.
            Combatant first = Monster(x: 0, y: 0);
            Combatant second = Monster(x: 1, y: 0);
            var enc = new CombatEncounter();
            enc.Enemies.Add(first);
            enc.Enemies.Add(second);
            enc.Party.Add(Member(2, 0));    // nearest to both
            enc.Party.Add(Member(6, 0));
            enc.BeginRound();

            Combatant firstPick = Resolver().Resolve(enc, first).Target;
            Combatant secondPick = Resolver().Resolve(enc, second).Target;

            Assert.AreSame(enc.Party[0], firstPick, "the first attacker takes the nearest");
            Assert.AreSame(enc.Party[1], secondPick,
                "the second is capped off it and takes the other");
        }

        [Test]
        public void WithNobodyLeftItPicksNothingRatherThanThrowing() {
            Combatant m = Monster();
            var enc = new CombatEncounter();
            enc.Enemies.Add(m);
            enc.BeginRound();

            Assert.IsNull(Resolver().Resolve(enc, m).Target);
        }

        [Test]
        public void ItDrivesARealTurnLoopWithoutTouchingHealth() {
            // The integration this exists for: CombatRuntime advances, every enemy turn goes through
            // the AI, and control returns on the party member. Decisions only — no health changes.
            var runtime = new CombatRuntime(session: null);
            CombatEncounter enc = runtime.Enter(new List<Combatant> { Monster(speed: 9), Monster(speed: 8) });
            Combatant hero = Member(3, 0, speed: 1);
            enc.Party.Add(hero);
            MonsterTurnResolver resolver = Resolver();

            var seen = new List<Combatant>();
            Combatant up = runtime.AdvanceToPartyTurn(c => { seen.Add(c); resolver.Resolve(enc, c); });

            Assert.AreSame(hero, up);
            Assert.AreEqual(2, seen.Count);
            foreach (Combatant c in seen) {
                Assert.AreSame(hero, c.Target, "each monster chose the only party member");
                Assert.AreEqual(Monster().Health, c.Health,
                    "deciding a turn does not damage anyone");
            }
            Assert.AreEqual(10, hero.Health);
        }
    
        // --- caster targeting through the action-priority row ------------------------------------

        [Test]
        public void ACasterPicksItsTargetByTheROWSMode_NotByBeingACaster() {
            // Pattern 2's row starts at slot 2, which is target mode 0 (Anyone). A roll of 0 commits
            // every attempt, so the first slot decides.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d =
                Resolver(canCast: true, roll: 0, spellcastPattern: 2).Resolve(enc, m);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
        }

        [Test]
        public void ARowSlotDemandingARoleNobodyFillsFallsThroughToTheNextSlot() {
            // Pattern 3's row is {3, 6, 8, 7, 1, 2, 4, 5}. Slot 3 is mode 1 (Spellcaster) and slot 6
            // is mode 5 (TargetingTheLeader) — with a plain fighter in the party neither matches.
            // Slot 8 is a special routine, slot 7 is mode 3 (MissileCapable), slot 1 special again,
            // and slot 2 is mode 0 (Anyone), which finally takes.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d = Resolver(
                canCast: true, roll: 0, spellcastPattern: 3, partyCanCast: false).Resolve(enc, m);

            Assert.AreSame(enc.Party[0], d.Target, "the row falls through to the mode-0 slot");
        }

        [Test]
        public void ARowSlotThatDOESMatchTakesTheTargetImmediately() {
            // Same row, but now the party member IS a caster, so slot 3's Spellcaster mode hits on
            // the first attempt. The pair is the point: the role comes from the row, and whether it
            // matches depends on the party, not on the monster.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d = Resolver(
                canCast: true, roll: 0, spellcastPattern: 3, partyCanCast: true).Resolve(enc, m);

            Assert.AreSame(enc.Party[0], d.Target);
        }

        [Test]
        public void TheROWSMode_NotDistance_DecidesWHICHMemberACasterPicks() {
            // *** The test that actually discriminates. *** Every row is a permutation containing a
            // mode-0 (Anyone) slot, so with an always-commit roll ANY pattern eventually finds SOME
            // target — which makes "it found someone" prove nothing. What the row decides is WHO.
            //
            // Near fighter, far caster. Pattern 3's row opens on slot 3 = mode 1 (Spellcaster), so
            // it reaches past the nearer fighter for the mage. Pattern 2 opens on slot 2 = mode 0,
            // and takes the nearest.
            // *** THE FIGHTER IS OFF THE FIRING LINE, NOT MERELY OFF THE MAGE'S TILE. ***
            // The first pass demands a clear projectile path, so the original layout — fighter at
            // (1,0), mage at (6,0) — had the fighter bodily screening the mage and the attempt
            // failed for COVER rather than for role, which is not what this test measures. Nudging
            // the mage off the axis was not enough either: the trace rounds away from zero, so a
            // shallow diagonal still samples (1,0) on its first step. Moving the fighter clear of
            // the y=0 lane is the fix that does not depend on rounding.
            // *** AND OUT OF CONTACT WITH THE MONSTER AT (0,0). *** The fighter stood at (0,1),
            // one tile away, which now makes the caster disengage instead of choosing anyone —
            // AiAction.Retreat. (0,2) keeps it the NEAR one, still clear of the y=0 lane.
            Combatant fighter = Member(0, 2);
            Combatant mage = Member(6, 0);

            Combatant m1 = Monster();
            var enc1 = new CombatEncounter();
            enc1.Enemies.Add(m1);
            enc1.Party.Add(fighter);
            enc1.Party.Add(mage);
            enc1.BeginRound();
            MonsterTurnResolver picksByRole = new MonsterTurnResolver(
                c => c.IsPartyMember
                    ? new MonsterTurnResolver.Profile(0, 100, canCastSpells: c == mage, canShoot: false)
                    : new MonsterTurnResolver.Profile(0, 100, true, false, spellcastPattern: 3,
                        crossbowAccuracy: 0, healthPercent: 100, castingSkill: ExpertCaster),
                _ => 0, Thresholds, isUnderground: false, canCast: (_, id) => id == 22,
                spells: KindOneBook);

            Assert.AreSame(mage, picksByRole.Resolve(enc1, m1).Target,
                "mode 1 reaches past the nearer fighter for the caster");

            Combatant m2 = Monster();
            var enc2 = new CombatEncounter();
            enc2.Enemies.Add(m2);
            enc2.Party.Add(fighter);
            enc2.Party.Add(mage);
            enc2.BeginRound();
            MonsterTurnResolver picksNearest = new MonsterTurnResolver(
                c => c.IsPartyMember
                    ? new MonsterTurnResolver.Profile(0, 100, canCastSpells: c == mage, canShoot: false)
                    : new MonsterTurnResolver.Profile(0, 100, true, false, spellcastPattern: 2,
                        crossbowAccuracy: 0, healthPercent: 100, castingSkill: ExpertCaster),
                _ => 0, Thresholds, isUnderground: false, canCast: (_, id) => id == 22,
                spells: KindOneBook);

            Assert.AreSame(fighter, picksNearest.Resolve(enc2, m2).Target,
                "mode 0 just takes the nearest");
        }

        [Test]
        public void ACasterTooWornDownToActPicksNobody() {
            // Combined health+stamina under 5 short-circuits the attempt loop in the original.
            var m = new Combatant {
                PartySlot = 0, ClassId = CascadeClass, Health = 2, Stamina = 2, Speed = 5,
            };
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d =
                Resolver(canCast: true, roll: 0, spellcastPattern: 2).Resolve(enc, m);

            Assert.AreEqual(AiAction.Cast, d.Action);
            Assert.IsNull(d.Target);
        }

        [Test]
        public void WhenNoAttemptCommitsTheCasterCastsNothingAndAdvances() {
            // A roll of 99 never commits (the gate is < 91), so the row is walked and nothing is
            // taken. The caster turn's tail then advances on anyone (99 > 10, full pool) — CBTAI.C:373.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d = Resolver(canCast: true, roll: 99, spellcastPattern: 2).Resolve(enc, m);
            Assert.AreEqual(OpportunisticCasts.NoSpell, d.SpellId);
            Assert.AreEqual(AiAction.MeleeOrMove, d.Fallback);
            Assert.AreSame(enc.Party[0], d.Target, "the advance's target, not a cast's");
        }

        [Test]
        public void APatternOfZeroCastsAtNobody() {
            // Pattern 0 has no row: the original never enters the loop.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.IsNull(Resolver(canCast: true, roll: 0, spellcastPattern: 0).Resolve(enc, m).Target);
        }

        [Test]
        public void ANonCasterIsUnaffectedByItsSpellcastPattern() {
            // A creature only ever exercises its highest-tier capability, so a melee creature's
            // spellcast pattern is never read.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d =
                Resolver(canCast: false, roll: 0, spellcastPattern: 5).Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
        }

        [Test]
        public void ACAPABLESHOOTERWITHPATTERNZERONeverShoots() {
            // *** CAPABILITY AND WILLINGNESS ARE DIFFERENT FIELDS. *** AccuracyCrossbow picks the
            // crossbow branch; CrossbowPattern decides whether that branch yields anything.
            // monster_chooseCrossbowAction tests the pattern at the head of its attempt loop —
            // before the first attempt — so a capable shooter with 0 takes no shot and falls
            // through to the advance-on-target half of the fatigue/morale fallback.
            //
            // This is not a hypothetical: 8 of the 16 monsters in MONST with AccuracyCrossbow > 0
            // carry CrossbowPattern (0,0) — MONST19/29/31/32/33/41/42/43 — so half the creatures
            // that CAN shoot never do.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.MeleeOrMove,
                Resolver(canShoot: true, crossbowPattern: 0).Resolve(enc, m).Action);
        }

        // CBTAITRN.C:361-367: with no attempt run, the crossbow turn ends on the fatigue/morale roll —
        // advance under 75 or at full stamina, otherwise rest. Pattern 0 runs no attempt (TASK-807).
        [Test]
        public void APatternZeroShooterTiredAndUnluckyRests() {
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Rest,
                Resolver(canShoot: true, crossbowPattern: 0, staminaPercent: 50, roll: 99).Resolve(enc, m).Action);
        }

        [Test]
        public void APATTERNEDSHOOTERStillShoots() {
            // The pair matters: without this, "pattern 0 melees" would also pass if the gate had
            // disabled shooting outright.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Shoot,
                Resolver(canShoot: true, crossbowPattern: 2, roll: 0, partyCanShoot: true, crossbowAccuracy: 75).Resolve(enc, m).Action);
        }

        // ---- the two row walks (TASK-528, CBTAITRN.C:331 / CMBTAI.C:496) --------------------------

        [Test]
        public void APatternTwoShooterWithNobodyItsRowNamesFallsThroughToFollowingAnyone() {
            // Row 2 from attempt 1 is engage, casters, shooters, wounded, engaged, the leader's
            // attackers, then follow-anyone-within-6. It never reads slot 2, the "shoot anyone" shot.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision d = Resolver(canShoot: true, roll: 0).Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(enc.Party[0], d.Target);
            // CBTAITRN.C:369 clears the target as the turn's LAST line, after the chosen slot has run —
            // so the resolver only marks it and CombatRuntime.ResolveEnemyTurn clears it afterwards.
            Assert.IsTrue(d.ClearsTargetAfterTurn, "take_actor_turn clears the target on the way out");
        }

        [Test]
        public void AShooterThatCommitsNothingAdvancesAtFullStaminaAndRestsBelowIt() {
            Combatant fresh = Monster();
            Assert.AreEqual(AiAction.MeleeOrMove,
                Resolver(canShoot: true, roll: 99, staminaPercent: 100).Resolve(Fight(fresh, Member(2, 0)), fresh).Action);

            Combatant worn = Monster();
            Assert.AreEqual(AiAction.Rest,
                Resolver(canShoot: true, roll: 99, staminaPercent: 50).Resolve(Fight(worn, Member(2, 0)), worn).Action);
        }

        [Test]
        public void APatternTwoMeleeCreaturePrefersAnEngagedTargetToANearerIdleOne() {
            // Row 2 walks follow(anyone within 6), follow(wounded), then follow(engaged) across the whole
            // field — so with nobody inside six, the member already fighting is chosen over the closer one.
            Combatant m = Monster(x: 0, y: 0);
            Combatant idle = Member(7, 0);
            Combatant engaged = Member(0, 9);
            var enc = new CombatEncounter();
            enc.Enemies.Add(m);
            enc.Party.Add(idle);
            enc.Party.Add(engaged);
            engaged.Target = m;
            enc.BeginRound();

            MonsterTurnResolver.Decision d = Resolver(roll: 99, meleeMovePattern: 2).Resolve(enc, m);

            Assert.AreEqual(AiAction.MeleeOrMove, d.Action);
            Assert.AreSame(engaged, d.Target);
        }

        [Test]
        public void ACasterThatCastsNothingAdvancesSlowlyOrGuards_AndDropsItsTarget() {
            // CBTAI.C:373-383. No catalogue, so no slot casts. Full pool and a roll over 10: advance,
            // capped at max(nearest, 7) - 6 = 1 step. A roll of 5: guard. Either way the target clears.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            MonsterTurnResolver.Decision moves =
                Resolver(canCast: true, roll: 50, spellcastPattern: 2).Resolve(enc, m);
            Assert.AreEqual(AiAction.Cast, moves.Action, "it is still the caster turn");
            Assert.AreEqual(AiAction.MeleeOrMove, moves.Fallback);
            Assert.AreEqual(1, moves.MaxSteps);
            Assert.IsTrue(moves.ClearsTargetAfterTurn);

            m.Target = null;
            MonsterTurnResolver.Decision guards =
                Resolver(canCast: true, roll: 5, spellcastPattern: 2).Resolve(enc, m);
            Assert.AreEqual(AiAction.Rest, guards.Fallback);
            Assert.IsTrue(guards.ClearsTargetAfterTurn);
        }

        [Test]
        public void ThePatternGateDoesNotOutrankCasting() {
            // The cascade order is unchanged: a caster is still a caster, and the crossbow gate
            // never runs for it. Guards against moving the gate above the Cast branch.
            Combatant m = Monster();
            CombatEncounter enc = Fight(m, Member(2, 0));

            Assert.AreEqual(AiAction.Cast,
                Resolver(canCast: true, canShoot: true, crossbowPattern: 0, spellcastPattern: 3)
                    .Resolve(enc, m).Action);
        }
    }
}
