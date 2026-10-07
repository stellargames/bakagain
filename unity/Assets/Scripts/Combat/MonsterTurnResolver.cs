namespace BakAgain.Combat {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Combat;

    /// <summary>
    /// What a monster does when its turn comes up — the seam
    /// <see cref="CombatRuntime.AdvanceToPartyTurn"/> calls, wired to the engine-independent AI.
    ///
    /// <para>Runs the original's order: the morale check first (<see cref="MonsterMorale"/>), then
    /// the class routine or the capability cascade (<see cref="CombatAi.ChooseAction"/>), then
    /// target selection (<see cref="CombatAi.SelectTarget"/>). <b>Decisions only</b> — walking,
    /// rolling the attack and resolving spells belong to whoever runs combat, which is why nothing
    /// here touches health.</para>
    /// </summary>
    public sealed class MonsterTurnResolver {
        /// <summary>Per-creature facts the AI needs that a <see cref="Combatant"/> does not carry.</summary>
        public readonly struct Profile {
            /// <summary>
            /// The rout stat — <c>MonsterStats.FleeThreshold</c>. <b>Higher means MORE likely to
            /// run</b>, and 0 never routs; see <see cref="MonsterMorale"/>.
            /// </summary>
            public readonly int FleeThreshold;

            /// <summary>Stamina as a percentage of this creature's own maximum.</summary>
            public readonly int StaminaPercent;

            /// <summary>
            /// Health as a percentage of this creature's own maximum —
            /// <c>combat_actor_stat_percent(actor, 0)</c>.
            /// </summary>
            /// <remarks>
            /// Needed by <see cref="MonsterHealTurn.CanReceive"/>, which wants a percentage and not
            /// the raw value the spell CHOICE reads. Defaults to 100, so a caller that does not
            /// supply it sees an untouched party and the heal simply finds nobody — the safe way
            /// round for a fact that is missing rather than zero.
            /// </remarks>
            public readonly int HealthPercent;

            public readonly bool CanCastSpells;
            public readonly bool CanShoot;

            /// <summary>
            /// This creature's own <c>AccuracyCrossbow</c>, which decides how much room it needs
            /// around a target before it will take the shot — see
            /// <see cref="CombatAi.AllyClearanceForAccuracy"/>. Read from the SHOOTER, never from
            /// the candidate.
            /// </summary>
            public readonly int CrossbowAccuracy;

            /// <summary>
            /// This creature's own <c>AccuracyCasting</c>, which decides how much room it demands
            /// around a cast target — <see cref="MonsterCasterTurn.ClearanceFor"/>. Read from the
            /// CASTER, exactly as <see cref="CrossbowAccuracy"/> is read from the shooter.
            /// </summary>
            public readonly int CastingSkill;

            /// <summary>
            /// <c>MonsterStats.SpellcastPattern</c> — the row of the caster's action-priority table,
            /// i.e. the order it tries its spell actions in. 0 means it never casts.
            /// </summary>
            public readonly int SpellcastPattern;

            /// <summary>
            /// <c>MonsterStats.CrossbowPattern</c> — the row of the shooter's action-priority table.
            /// <b>0 means it never shoots</b>, even when <see cref="CanShoot"/> is true.
            /// </summary>
            /// <remarks>
            /// The two are not the same question. <see cref="CanShoot"/> is capability
            /// (<c>AccuracyCrossbow</c> above zero, which is what picks the crossbow BRANCH); this
            /// is whether that branch produces anything once entered. <c>monster_chooseCrossbowAction</c>
            /// tests the pattern at the head of its attempt loop, before the first attempt, so a
            /// capable shooter with pattern 0 takes no shot at all.
            /// </remarks>
            public readonly int CrossbowPattern;

            /// <summary><c>MonsterStats.MeleeMovePattern</c> — the row the melee/move turn walks.</summary>
            public readonly int MeleeMovePattern;

            /// <summary>Health+stamina pool as a percentage of its maximum —
            /// <c>combat_actor_stat_percent(actor, 1)</c> (CACTOR.C:721). What the row walks' fatigue
            /// tests and the Wounded role read; unset, it takes <see cref="StaminaPercent"/>.</summary>
            public readonly int PoolPercent;

            public Profile(int fleeThreshold, int staminaPercent, bool canCastSpells, bool canShoot,
                int spellcastPattern = 0, int crossbowAccuracy = 0, int healthPercent = 100,
                int castingSkill = 0, int crossbowPattern = 0, int meleeMovePattern = 0, int poolPercent = -1) {
                FleeThreshold = fleeThreshold;
                StaminaPercent = staminaPercent;
                CanCastSpells = canCastSpells;
                CanShoot = canShoot;
                SpellcastPattern = spellcastPattern;
                CrossbowPattern = crossbowPattern;
                MeleeMovePattern = meleeMovePattern;
                PoolPercent = poolPercent >= 0 ? poolPercent : staminaPercent;
                CrossbowAccuracy = crossbowAccuracy;
                HealthPercent = healthPercent;
                CastingSkill = castingSkill;
            }
        }

        /// <summary>What the resolver decided, for the caller to carry out.</summary>
        public readonly struct Decision {
            public readonly AiAction Action;

            /// <summary>Who it chose, or null if nothing matched.</summary>
            public readonly Combatant Target;

            /// <summary>
            /// The specific spell an opportunistic pass reached for, or
            /// <see cref="OpportunisticCasts.NoSpell"/>.
            /// </summary>
            /// <remarks>
            /// <b>Only the opportunistic passes name a spell.</b> An ordinary
            /// <see cref="AiAction.Cast"/> leaves this unset, because the original picks that
            /// spell later and elsewhere (<c>cspell_ai_pick_castable_spell</c>, keyed on the
            /// target it just chose) — so a caller that treated -1 here as "no cast" would drop
            /// every ordinary cast in the game.
            /// </remarks>
            public readonly int SpellId;

            /// <summary>
            /// The shot a <see cref="CombatAi.SpeciesRoutine.RangedAttackTurn"/> creature chose —
            /// heavy or light, with its damage band — or the default for every other turn.
            /// </summary>
            /// <remarks>
            /// Carried rather than re-rolled: the rolls that pick it are the ones that decided to shoot
            /// at all, so rolling again to carry it out would fire a different shot from the one decided.
            /// </remarks>
            public readonly MonsterTurnRoutines.RangedTurn Ranged;

            /// <summary>
            /// The power a species-routine cast is made at, or null for the AI's usual full-strength
            /// cast (<c>cspell_actor_stat_get_comb_dflt</c>). Only the wandering routine sets it: it
            /// calls <c>cspell_resolve_cast</c> directly with half its health stat (CBTAIACT.C:71).
            /// </summary>
            public readonly int? CastPower;

            /// <summary>
            /// The turn ends with the actor's target cleared — <c>combataiturn_take_actor_turn</c>'s
            /// last line (CBTAITRN.C:369) — which has to happen AFTER the turn is carried out: a
            /// follow slot's swing records the target again (COMBAT.C:471), and clearing it first
            /// left a shooter locked on the member beside it for the rest of the fight (TASK-528).
            /// </summary>
            public readonly bool ClearsTargetAfterTurn;

            /// <summary>
            /// What a caster whose slots cast nothing does instead — <see cref="AiAction.MeleeOrMove"/>
            /// or <see cref="AiAction.Rest"/> — or null. The decision stays a <see cref="AiAction.Cast"/>:
            /// it is the caster turn's own tail (TASK-539).
            /// </summary>
            public readonly AiAction? Fallback;

            /// <summary>A cap on how far the move walks, or null for the actor's speed.</summary>
            public readonly int? MaxSteps;

            public Decision(AiAction action, Combatant target,
                int spellId = OpportunisticCasts.NoSpell,
                MonsterTurnRoutines.RangedTurn ranged = default, int? castPower = null,
                bool clearsTargetAfterTurn = false, AiAction? fallback = null, int? maxSteps = null) {
                ClearsTargetAfterTurn = clearsTargetAfterTurn;
                Fallback = fallback;
                MaxSteps = maxSteps;
                Action = action;
                Target = target;
                SpellId = spellId;
                Ranged = ranged;
                CastPower = castPower;
            }
        }

        private readonly Func<Combatant, Profile> _profileOf;
        private readonly Func<int, int> _rnd;
        private readonly IReadOnlyList<int> _fleeThresholds;
        private readonly bool _isUnderground;
        private readonly Func<Combatant, int, bool> _canCast;
        private readonly IReadOnlyDictionary<int, GameData.Resources.Spells.Spell> _spells;

        /// <param name="fleeThresholds"><c>CombatAffinityTables.AiFleeThresholds</c>.</param>
        /// <param name="isUnderground">
        /// <b>Nothing routs underground</b> — the original's check returns immediately in game mode
        /// 2, so a dungeon fight is always to the finish.
        /// </param>
        /// <param name="canCast">
        /// <c>cspell_check_castable</c> — whether this actor can cast a given spell id right now.
        /// Supplied by whoever holds the spell catalogue; left null, the opportunistic passes
        /// simply never fire, which is the same behaviour as a creature that knows none of them.
        /// </param>
        /// <remarks>
        /// <b>There is deliberately no target-distance or ally-clearance knob here.</b> There used
        /// to be, defaulting to 12 and 0, and the live caller took both defaults — so every monster
        /// searched a radius matching none of the original's three families and every monster was a
        /// perfect shot. Both are properties of the behaviour being executed, not of the AI, so they
        /// are derived per decision in <c>PickTarget</c>. Re-adding them as constructor state would
        /// reintroduce exactly that bug.
        /// </remarks>
        /// <param name="isBlocked">
        /// The arena's occupancy test, used only to answer whether a candidate can be stood next to
        /// — see <see cref="CombatAi.ApproachIsBlocked"/>. Null leaves that rule off, which is the
        /// behaviour every caller had before it existed.
        /// </param>
        /// <remarks>
        /// <b>A grid TEST, not a grid.</b> This class is documented as deciding only, and handing it
        /// the arena would make that false — a delegate keeps the dependency the size of the one
        /// question the selector asks, the same way <c>ApproachCell</c> already takes one.
        /// </remarks>
        public MonsterTurnResolver(Func<Combatant, Profile> profileOf, Func<int, int> rnd,
            IReadOnlyList<int> fleeThresholds, bool isUnderground,
            Func<Combatant, int, bool> canCast = null,
            IReadOnlyDictionary<int, GameData.Resources.Spells.Spell> spells = null,
            Func<int, int, bool> isBlocked = null) {
            _profileOf = profileOf ?? (_ => default);
            _rnd = rnd ?? (n => 0);
            _fleeThresholds = fleeThresholds;
            _isUnderground = isUnderground;
            _canCast = canCast;
            _spells = spells;
            _isBlocked = isBlocked;
        }

        private readonly Func<int, int, bool> _isBlocked;

        /// <summary>Decide this monster's turn and record the decision on it.</summary>
        public Decision Resolve(CombatEncounter encounter, Combatant monster) {
            if (encounter == null || monster == null) {
                return new Decision(AiAction.MeleeOrMove, null);
            }

            Profile profile = _profileOf(monster);

            // The morale check runs FIRST and preempts everything: a routing creature never reaches
            // the class routine or the cascade.
            if (MonsterMorale.Routs(profile.StaminaPercent, profile.FleeThreshold, _rnd(100),
                    _fleeThresholds, _isUnderground)) {
                monster.Flags |= CombatantFlags.Fleeing;
            }

            bool fleeing = (monster.Flags & CombatantFlags.Fleeing) != 0;
            AiAction action = CombatAi.ChooseAction(
                monster.ClassId, fleeing, profile.CanCastSpells, profile.CanShoot);

            if (action == AiAction.Flee) {
                // It is leaving the field; it does not pick anyone on the way out.
                monster.Target = null;
                return new Decision(action, null);
            }

            if (action == AiAction.SpeciesSpecific) {
                Decision species = ResolveSpecies(encounter, monster);
                monster.Target = species.Target;
                return species;
            }

            // *** A CAPABLE SHOOTER WITH PATTERN 0 DOES NOT SHOOT. *** The cascade above picks the
            // crossbow BRANCH from capability alone, exactly as the original does; whether that
            // branch yields an action is the pattern's call, and monster_chooseCrossbowAction tests
            // it at the head of the attempt loop — before the first attempt, not merely between
            // them. With no attempt committing, the turn falls through to the fatigue/morale
            // fallback (CBTAITRN.C:361-367) — advance or rest on the roll; see CrossbowFallback.
            //
            // *** NO SHIPPED MONSTER REACHES THIS, AND THAT IS NOT AN ACCIDENT. *** Measured over
            // all 35 MONST files: of the 16 with AccuracyCrossbow > 0, the 8 carrying
            // CrossbowPattern (0,0) — 19/29/31/32/33/41/42/43 — ALL have a species routine, and the
            // 8 carrying (2,7) — 10/22/23/24/26/48/53/55 — all have none. The class switch sits in
            // FRONT of the cascade, so the pattern-0 group never arrives here at all; they decline
            // the crossbow by taking their own routine, which is exactly why their pattern is 0.
            // The split is clean in both directions, so this gate is defence for summoned and
            // modded creatures rather than a behaviour change to the shipped game. Do not "simplify"
            // it away on the grounds that nothing hits it, and do not claim it changes shipped
            // behaviour either — an earlier commit message of mine did, and was wrong.
            if (action == AiAction.Shoot && !MonsterActionPatterns.Shoots(profile.CrossbowPattern)) {
                return CrossbowFallback(encounter, monster, profile);
            }

            // *** AN ENGAGED SHOOTER DOES NOT SHOOT, AND THAT TEST WAS MISSING ENTIRELY. ***
            // combatenc_show_missile_stat_row (CBENC.C:507) is the door to the crossbow cascade and
            // it demands `1 < nearestDist` unconditionally — no flag, no way to skip it — so a
            // pattern-driven shooter with someone in contact never enters that branch and falls
            // through to ordinary melee. Profile.CanShoot is built from AccuracyCrossbow alone
            // (HotspotService.ProfileOf), which is the template's capability and knows nothing about
            // where anyone is standing, so the port shot from contact where the original closes.
            //
            // Note this is NOT the caster's rule one line down: the shooter is turned away at the
            // door and melees, the caster is admitted and then retreats. See AiAction.Retreat.
            if (action == AiAction.Shoot
                && !CombatCapability.RangeIsClear(NearestOpponentDistance(encounter, monster))) {
                action = AiAction.MeleeOrMove;
            }

            if (action == AiAction.Cast) {
                // *** THE HEAD OF THE CASCADE TURN, NOT A SPELL PRE-CHECK. ***
                // combat_ai_take_turn (CBTAI.C:346) opens with pick_tile_or_attack(actor, 1, 1) and
                // treats a non-zero return as "acted", skipping the whole pattern loop below it. The
                // caster reached this branch through combatenc_actor_can_cast_spells(actor, 0) —
                // that zero deliberately withholds the adjacency test from the branch choice — so
                // this is where adjacency is finally read, and the answer is to back away rather
                // than to pick a different action. Substituting MeleeOrMove or Defend here would be
                // a different instruction, not a near miss.
                if (MonsterSpellcasting.MustDisengageBeforeCasting(
                        NearestOpponentDistance(encounter, monster))) {
                    // The target is deliberately left as it stands. The Flee branch clears it and
                    // says why; the original's retreat has no equivalent line, and inventing one
                    // would change the target-selection heuristics that read it.
                    return new Decision(AiAction.Retreat, monster.Target);
                }

                (Combatant castTarget, int spellId, bool recordAsTarget) =
                    PickCastTarget(encounter, monster, profile);
                // *** THE SUPPORT TURN'S RECIPIENT IS NOT A TARGET. *** It is an ALLY, and this
                // field feeds the target-selection heuristics — see TryHeal.
                if (recordAsTarget) {
                    monster.Target = castTarget;
                }
                // combat_ai_take_turn ends `actor->inner->target = 0` whatever happened (CBTAI.C:383).
                return spellId != OpportunisticCasts.NoSpell
                    ? new Decision(action, castTarget, spellId, clearsTargetAfterTurn: true)
                    : CasterFallback(encounter, monster, profile, castTarget);
            }

            // *** BOTH NON-CASTING TURNS WALK A PRIORITY ROW (TASK-528). *** This used to be one
            // PickTarget(Anyone) for either action, which no creature in the original ever does.
            return action == AiAction.Shoot
                ? WalkCrossbowRow(encounter, monster, profile)
                : WalkMeleeMoveRow(encounter, monster, profile);
        }

        /// <summary>
        /// The melee/move turn — <c>combataipath_select_action</c> (CMBTAI.C:496).
        /// </summary>
        /// <remarks>
        /// The MeleeMovePattern row from slot 0, no commit roll; the first slot that acts is the turn.
        /// Nothing acting (or pattern 0): a creature under 10% stamina rests one turn in four, a fifth of
        /// those backing off first when engaged, and everyone else follows anyone on the field —
        /// <c>follow_tgt_check(actor, 100, 0)</c>, which is all this path did before.
        /// </remarks>
        private Decision WalkMeleeMoveRow(CombatEncounter encounter, Combatant monster, Profile profile) {
            if (MonsterActionPatterns.Fights(profile.MeleeMovePattern)) {
                for (int attempt = MonsterActionPatterns.MeleeMoveFirstAttempt;
                     attempt < MonsterActionPatterns.SlotCount; attempt++) {
                    MonsterActionPatterns.Slot slot = MonsterActionPatterns.MeleeMoveSlot(
                        MonsterActionPatterns.MeleeMoveSlotFor(profile.MeleeMovePattern, attempt));
                    if (slot.Kind == MonsterActionPatterns.SlotKind.RestWhenWorn) {
                        // low_health_action rolls before it tests anything.
                        int roll = _rnd(100);
                        if (MonsterActionPatterns.LowHealthRests(profile.PoolPercent,
                                NearestOpponentDistance(encounter, monster), roll)) {
                            return new Decision(AiAction.Rest, monster.Target);
                        }
                        continue;
                    }
                    Decision? followed = Follow(encounter, monster, slot.Radius, slot.Role);
                    if (followed.HasValue) {
                        return followed.Value;
                    }
                }
            }

            if (profile.PoolPercent < MonsterActionPatterns.ExhaustedStaminaPercent
                && _rnd(100) < MonsterActionPatterns.ExhaustedRestPercent) {
                // pick_tile_or_attack(actor, 1, 0) only moves an engaged creature.
                if (_rnd(100) <= MonsterActionPatterns.ExhaustedBackOffPercent
                    && MonsterSpellcasting.MustDisengageBeforeCasting(NearestOpponentDistance(encounter, monster))) {
                    return new Decision(AiAction.Retreat, monster.Target);
                }
                return new Decision(AiAction.Rest, monster.Target);
            }
            return Follow(encounter, monster, CombatAi.MeleeSearchRadius, TargetRole.Anyone)
                ?? new Decision(AiAction.MeleeOrMove, null);
        }

        /// <summary>
        /// <c>combataipath_follow_tgt_check(actor, maxDist, mode)</c> (CMBTAI.C:391).
        /// </summary>
        /// <remarks>
        /// A target already orthogonally beside the creature is kept whatever the slot asks for; otherwise
        /// the target is dropped and chosen again by this slot's role and radius. Finding nobody is the
        /// only way the slot declines — the creature has not moved — so the walk tries the next.
        /// </remarks>
        private Decision? Follow(CombatEncounter encounter, Combatant monster, int radius, TargetRole role) {
            Combatant current = monster.Target;
            if (current != null && !current.IsDead
                && CombatGrid.OrthogonallyAdjacent(monster.X, monster.Y, current.X, current.Y)) {
                return new Decision(AiAction.MeleeOrMove, current);
            }
            monster.Target = PickTarget(encounter, monster, role, AiAction.MeleeOrMove, radiusOverride: radius);
            return monster.Target != null
                ? new Decision(AiAction.MeleeOrMove, monster.Target)
                : (Decision?)null;
        }

        /// <summary>
        /// The crossbow turn — <c>combataiturn_take_actor_turn</c> (CBTAITRN.C:331).
        /// </summary>
        /// <remarks>
        /// <para>Its opening <c>pick_tile_or_attack(actor, 1, 1)</c> cannot act here: the door into this
        /// branch (<c>combatenc_show_missile_stat_row</c>) already demanded the nearest opponent be further
        /// than one, which is exactly where that routine declines.</para>
        /// <para>The row from slot 1, each attempt on a 91% roll, none at all under 5 health. Nothing
        /// taken: advance three turns in four (always at full stamina), else rest. The target is cleared
        /// on the way out whatever happened — the routine's last line.</para>
        /// <para><b>Ceiling:</b> the per-slot quarrel test (<c>sel_consum_qrl</c>) lives in
        /// <c>CombatRuntime.FireMonsterShot</c>, so an empty pack advances instead of trying later slots.</para>
        /// </remarks>
        private Decision WalkCrossbowRow(CombatEncounter encounter, Combatant monster, Profile profile) {
            Decision decided = CrossbowAttempts(encounter, monster, profile)
                ?? CrossbowFallback(encounter, monster, profile);
            return new Decision(decided.Action, decided.Target, decided.SpellId, decided.Ranged,
                decided.CastPower, clearsTargetAfterTurn: true);
        }

        private Decision? CrossbowAttempts(CombatEncounter encounter, Combatant monster, Profile profile) {
            if (monster.Health < MonsterActionPatterns.CrossbowAttemptsMinHealth) {
                return null;
            }
            for (int attempt = MonsterActionPatterns.CrossbowFirstAttempt;
                 attempt < MonsterActionPatterns.SlotCount && MonsterActionPatterns.Shoots(profile.CrossbowPattern);
                 attempt++) {
                if (_rnd(100) >= MonsterActionPatterns.CrossbowCommitPercent) {
                    continue;
                }
                MonsterActionPatterns.Slot slot = MonsterActionPatterns.CrossbowSlot(
                    MonsterActionPatterns.CrossbowSlotFor(profile.CrossbowPattern, attempt));
                Decision? acted = slot.Kind switch {
                    MonsterActionPatterns.SlotKind.Follow => Follow(encounter, monster, slot.Radius, slot.Role),
                    MonsterActionPatterns.SlotKind.Engage =>
                        Engage(encounter, monster) ?? Shot(encounter, monster, slot.Radius, slot.Role),
                    MonsterActionPatterns.SlotKind.Shot => Shot(encounter, monster, slot.Radius, slot.Role),
                    _ => null,
                };
                if (acted.HasValue) {
                    return acted;
                }
            }
            return null;
        }

        /// <summary>
        /// <c>combataiturn_action_disp_base(actor, radius, mode)</c> (CBTAITRN.C:235): choose by role
        /// within the radius, and shoot only down a clear line of fire.
        /// </summary>
        private Decision? Shot(CombatEncounter encounter, Combatant monster, int radius, TargetRole role) {
            monster.Target = PickTarget(encounter, monster, role, AiAction.Shoot, radiusOverride: radius);
            return monster.Target != null && HasLineOfFire(encounter, monster, monster.Target)
                ? new Decision(AiAction.Shoot, monster.Target)
                : (Decision?)null;
        }

        /// <summary>
        /// <c>combataiturn_select_and_engage</c> (CBTAITRN.C:290): raise a guard against someone within
        /// reach, or against a spellcaster in the line of fire. The caller falls back to the slot's shot.
        /// </summary>
        private Decision? Engage(CombatEncounter encounter, Combatant monster) {
            foreach (Combatant other in SidesFor(encounter, monster).Opponents) {
                int distance = CombatGrid.ChebyshevDistance(monster.X, monster.Y, other.X, other.Y);
                if (monster.Speed - MonsterActionPatterns.EngageReachMargin >= distance) {
                    if (_rnd(100) >= MonsterActionPatterns.EngageNearGuardPercent) {
                        return new Decision(AiAction.Defend, monster.Target);
                    }
                    continue;
                }
                if (!_profileOf(other).CanCastSpells) {
                    continue;
                }
                monster.Target = other;
                int roll = _rnd(100);
                if (HasLineOfFire(encounter, monster, other)
                    && roll >= MonsterActionPatterns.EngageFarGuardPercent) {
                    return new Decision(AiAction.Defend, monster.Target);
                }
            }
            return null;
        }

        /// <summary>The crossbow turn's fallback — see <see cref="WalkCrossbowRow"/>.</summary>
        /// <summary>
        /// A caster turn that cast nothing — <c>combat_ai_take_turn</c>'s tail (CBTAI.C:373-381).
        /// </summary>
        /// <remarks>
        /// Above 40% of its pool a creature advances on a roll over 10, capped at its nearest opponent's
        /// distance less six so it closes slowly; otherwise, and always for a party member, it raises
        /// its guard. <c>select_target(actor, 100, 0)</c> keeps the target the last attempt aimed at.
        /// Entry 306's Rogue Mage used to stand still here while the original's wandered.
        /// </remarks>
        private Decision CasterFallback(CombatEncounter encounter, Combatant monster, Profile profile,
            Combatant lastAimed) {
            if (profile.PoolPercent <= MonsterActionPatterns.CasterAdvancePoolPercent
                || _rnd(100) <= MonsterActionPatterns.CasterAdvanceRollAbove
                || monster.IsPartyMember) {
                return new Decision(AiAction.Cast, lastAimed, clearsTargetAfterTurn: true,
                    fallback: AiAction.Rest);
            }
            Combatant target = monster.Target ?? PickTarget(encounter, monster, TargetRole.Anyone,
                AiAction.MeleeOrMove, radiusOverride: CombatAi.MeleeSearchRadius);
            return new Decision(AiAction.Cast, target, clearsTargetAfterTurn: true,
                fallback: AiAction.MeleeOrMove,
                maxSteps: MonsterActionPatterns.CasterAdvanceSteps(monster.Speed,
                    NearestOpponentDistance(encounter, monster)));
        }

        private Decision CrossbowFallback(CombatEncounter encounter, Combatant monster, Profile profile) {
            if (MonsterActionPatterns.CrossbowFallbackAdvances(_rnd(100), profile.PoolPercent)) {
                // select_target(actor, 100, 0) keeps a target an attempt left behind and searches only
                // without one (CMBTAI.C:288).
                Combatant target = monster.Target ?? PickTarget(encounter, monster, TargetRole.Anyone,
                    AiAction.MeleeOrMove, radiusOverride: CombatAi.MeleeSearchRadius);
                return new Decision(AiAction.MeleeOrMove, target);
            }
            return new Decision(AiAction.Rest, null);
        }

        /// <summary>
        /// Chebyshev distance to the nearest living opponent, or
        /// <see cref="CombatCapability.DistanceUnchecked"/> when there is none.
        /// </summary>
        /// <remarks>
        /// <b>Opponents, not everyone</b> — <see cref="CombatCapability.NearestOpponent"/> carries the
        /// reason, and <see cref="SidesFor"/> already answers which list that is for a summon.
        /// </remarks>
        private int NearestOpponentDistance(CombatEncounter encounter, Combatant monster) {
            (IReadOnlyList<Combatant> opponents, IReadOnlyList<Combatant> _) =
                SidesFor(encounter, monster);
            return CombatCapability.NearestOpponent(monster.X, monster.Y, opponents);
        }

        /// <summary>
        /// The turn a species-specific class takes — the seven branches of
        /// <c>combatenc_ai_sel_execute_action</c>'s switch (CBENC.C:925), reduced to the action kind
        /// this resolver can express.
        /// </summary>
        /// <remarks>
        /// <b>Thirteen classes used to return <see cref="AiAction.SpeciesSpecific"/> and stop there,
        /// which meant they did nothing at all.</b> That action is not carried out by
        /// <c>CombatRuntime.ResolveEnemyTurn</c> (only <see cref="AiAction.MeleeOrMove"/> is), so
        /// every giant, wasp and dog in the game stood still through its own turn while looking, from
        /// the outside, like a creature whose AI had been "decided correctly and not acted on".
        ///
        /// <para><b>Which branch a class takes is transcribed, not guessed</b> — see
        /// <see cref="CombatAi.SpeciesRoutineOf"/>. What remains unported is the detail INSIDE each
        /// routine (TASK-97): <c>combataiact_melee_random_attack</c> picks its victim at random where
        /// this picks the nearest, and the charge and wander routines have movement of their own.
        /// Those differences change who gets bitten; the difference this fixes is whether anything
        /// happens at all.</para>
        ///
        /// <para><b>Only class 0x13 depends on distance</b>, and its far branch may CAST rather than
        /// shoot — the original rolls <c>RND(10) &gt;= distance</c> for it (CBTAIACT.C:31). Both
        /// branches keep the nearest actor as the target, because the original finds it once, before
        /// it decides. That is why this does not route through
        /// <see cref="PickCastTarget"/>: the spell-pattern machinery is a different path the original
        /// does not take here.</para>
        /// </remarks>
        private Decision ResolveSpecies(CombatEncounter encounter, Combatant monster) {
            CombatAi.SpeciesRoutine? routine = CombatAi.SpeciesRoutineOf(monster.ClassId);
            // *** THE NEAREST OPPONENT, NOT THE MELEE SELECTOR'S CHOICE. *** Every species routine
            // opens with combatenc_find_nearest_actor (CBENC.C:415): the first living opponent at a
            // strictly smaller distance, and nothing else. PickTarget's melee mode caps attackers per
            // target, so a pack of shooters spread its fire across the party where the original
            // concentrates it — measured 2026-09-14 in entry 93, five 0x39s against one party: all of
            // the original's shots went to Gorath, the nearest to every one of them; ours hit all three.
            Combatant nearest = NearestOpponent(encounter, monster);
            if (routine == null || nearest == null) {
                // routine == null is not reachable: ChooseAction only returns SpeciesSpecific for a
                // class in the table.
                return new Decision(AiAction.MeleeOrMove, nearest);
            }

            int distance = CombatGrid.ChebyshevDistance(monster.X, monster.Y, nearest.X, nearest.Y);
            bool lineOfFire = CombatLineOfFire.IsClear(monster.X, monster.Y, nearest.X, nearest.Y,
                CombatLineOfFire.BlockedByLivingActor(
                    (x, y) => ActorAt(encounter, x, y), monster));

            AiAction action = SpeciesAction(routine.Value, monster, distance, lineOfFire,
                out MonsterTurnRoutines.RangedTurn ranged, out int spellId, out int? castPower);
            return new Decision(action, nearest, spellId, ranged, castPower);
        }

        /// <summary>
        /// The first living opponent at the smallest Chebyshev distance — <c>combatenc_find_nearest_actor</c>.
        /// </summary>
        /// <remarks>Strictly smaller, so a tie goes to the opponent listed first.</remarks>
        private static Combatant NearestOpponent(CombatEncounter encounter, Combatant monster) {
            Combatant best = null;
            int bestDistance = CombatCapability.DistanceUnchecked;
            foreach (Combatant c in SidesFor(encounter, monster).Opponents) {
                if (c == null || c.IsDead) {
                    continue;
                }
                int distance = CombatGrid.ChebyshevDistance(monster.X, monster.Y, c.X, c.Y);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>The live combatant on a tile, or null.</summary>
        private static Combatant ActorAt(CombatEncounter encounter, int x, int y) {
            foreach (Combatant c in encounter.AllCombatants()) {
                if (c != null && c.X == x && c.Y == y) {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Runs the creature's own routine — <b>the models in
        /// <c>GameData.Resources.Combat</c>, not a second copy of their rules.</b>
        /// </summary>
        /// <remarks>
        /// <b>These types existed, correct and unconsumed, while an earlier version of this method
        /// re-derived their thresholds from the original's FUNCTION NAMES and got three of them
        /// backwards</b> (TASK-241). They also model things that version could not: a
        /// <see cref="AiAction.Defend"/> outcome, spell kinds, and the line-of-fire gate every one
        /// of them takes — supplied here by <see cref="CombatLineOfFire"/>.
        ///
        /// <para>What this method owns is the DISPATCH — which class runs which routine — and
        /// nothing else. Every threshold, roll bound and damage band stays in the routine type that
        /// documents it.</para>
        /// </remarks>
        private AiAction SpeciesAction(CombatAi.SpeciesRoutine routine, Combatant monster,
            int distance, bool lineOfFire, out MonsterTurnRoutines.RangedTurn ranged,
            out int spellId, out int? castPower) {
            ranged = default;
            spellId = OpportunisticCasts.NoSpell;
            castPower = null;
            switch (routine) {
                case CombatAi.SpeciesRoutine.MeleeAdjacentElseCastOrShoot: {
                    // *** A CAST NAMES ITS SPELL, OR NOTHING CASTS IT. *** RunEnemyTurn only resolves a
                    // decision that carries a spell id; dropping SpellKind here left every 0x13 cast
                    // "decided but not carried out" (TASK-508). The original casts spell 4 at the AI's
                    // usual power (combat_ai_actor_cast_spell).
                    MonsterTurn turn = MonsterTurnRoutines.CloseOrRanged(
                        distance, _rnd(MonsterTurnRoutines.CloseRangeCastRollBound));
                    if (turn.Move == MonsterMove.Cast) {
                        spellId = turn.SpellKind;
                    }
                    return Translate(turn.Move);
                }

                case CombatAi.SpeciesRoutine.WalkRandomTileThenAttackOrBrace:
                    // halfStat is half the actor's health stat in the original (>> 1), and the
                    // routine tests it for != 1 rather than for a minimum.
                    MonsterTurn wander = MonsterTurnRoutines.AfterWandering(
                        distance, _rnd(100), monster.Health / 2, lineOfFire);
                    if (wander.Move == MonsterMove.Cast) {
                        // cspell_resolve_cast(actor, target, 5 or 4, halfStat): the power is the
                        // half-stat itself, not the AI's full-strength cost.
                        spellId = wander.SpellKind;
                        castPower = monster.Health / 2;
                    }
                    return Translate(wander.Move);

                case CombatAi.SpeciesRoutine.RangedAttackTurn:
                    ranged = MonsterTurnRoutines.ChooseRangedTurn(
                        lineOfFire, _rnd(MonsterTurnRoutines.AbortShotRollBound),
                        _rnd(MonsterTurnRoutines.HeavyShotRollBound), monster.ClassId);
                    return ranged.Choice == MonsterTurnRoutines.RangedChoice.Reconsider
                        ? AiAction.MeleeOrMove
                        : AiAction.Shoot;

                case CombatAi.SpeciesRoutine.RangedKnockbackElseCloseIn:
                    // MonsterMeleeTurn carries the NoTarget case the V102CD build guards with.
                    return MonsterMeleeTurn.Choose(hasTarget: true, lineOfFire, distance)
                            == MonsterMeleeTurn.Outcome.RangedAttack
                        ? AiAction.Shoot
                        : AiAction.MeleeOrMove;

                case CombatAi.SpeciesRoutine.ShootAtRangeElseDelegate:
                    return MonsterChargeTurn.Choose(distance, lineOfFire, _rnd(100))
                            == MonsterChargeTurn.Outcome.RangedAttack
                        ? AiAction.Shoot
                        : AiAction.MeleeOrMove;

                case CombatAi.SpeciesRoutine.RandomRangedVariantBeyondTwoTiles:
                    return MonsterVariantAttackTurn.Attacks(lineOfFire, distance)
                        ? AiAction.Shoot
                        : AiAction.MeleeOrMove;

                case CombatAi.SpeciesRoutine.RangedPoisonAttack:
                    return MonsterHeavyRangedTurn.Attacks(lineOfFire, distance)
                        ? AiAction.Shoot
                        : AiAction.MeleeOrMove;

                default:
                    return AiAction.MeleeOrMove;
            }
        }

        /// <summary>
        /// <see cref="MonsterMove"/> in the routines' vocabulary to this resolver's
        /// <see cref="AiAction"/>.
        /// </summary>
        /// <remarks>
        /// One-to-one now that <see cref="AiAction.Defend"/> exists. It did not before, and Defend
        /// was flattened to <see cref="AiAction.MeleeOrMove"/> — the opposite instruction, not a
        /// near-miss.
        /// </remarks>
        private static AiAction Translate(MonsterMove move) => move switch {
            MonsterMove.Melee => AiAction.MeleeOrMove,
            MonsterMove.Cast => AiAction.Cast,
            MonsterMove.Shoot => AiAction.Shoot,
            MonsterMove.Defend => AiAction.Defend,
            _ => AiAction.MeleeOrMove,
        };

        /// <summary>
        /// A caster's target, chosen the way the original chooses it: walk the creature's
        /// action-priority row, and take the first attempt that both commits and finds someone.
        /// </summary>
        /// <remarks>
        /// <b>The slot decides the targeting mode, and the mapping is not in slot order</b> — slots
        /// 2/3/4 take modes 0/1/2, then it jumps: 5→4, 6→5, 7→3. That is
        /// <see cref="MonsterSpellcasting.TargetModeOf"/>'s business; what matters here is that the
        /// role comes from the ROW, not from the action.
        ///
        /// <para>Slot 1 is the heal routine (<c>combat_ai_try_cast_heal</c>, see TryHeal); slot 8
        /// (<c>combat_ai_try_aoe_cast_spell_7</c>) is spent doing nothing, its body unreachable in the
        /// shipped binary (SlotAction.SpecialLast). When no attempt commits, CasterFallback is the
        /// tail of <c>combat_ai_take_turn</c> (CBTAI.C:373-381).</para>
        /// </remarks>

        /// <summary>
        /// Which spell this caster reaches for — <c>cspell_ai_pick_castable_spell</c> (CSPELL.C:276).
        /// </summary>
        /// <remarks>
        /// <b>Every rule here was already modelled and had no caller.</b> The routine itself was
        /// never built, so an ordinary targeted cast picked a target, spent the attempt and chose
        /// nothing — <c>RunEnemyTurn</c> then returned on the missing id and the monster did not
        /// cast at all. See TASK-379.
        ///
        /// <para><b>The scan runs high to low</b>, so a caster reaches for its most advanced
        /// matching spell first. The original seeds the loop with the spell COUNT and so reads one
        /// record past the table on its first iteration; that is a documented quirk and
        /// <see cref="MonsterSpellcasting.FirstCandidate"/> deliberately does not reproduce it.</para>
        ///
        /// <para><b>The already-on-target test is the one place the engine consults the effect pool
        /// before casting, and it looks at the TARGET.</b> Without it a caster stacks the same
        /// effect on the same victim every turn.</para>
        ///
        /// <para><b>Not modelled here:</b> the action's two-pass structure
        /// (<see cref="MonsterSpellcasting.FirstPassTargetingTypes"/> and its line-of-fire
        /// difference). That is a property of the action that calls the selector, not of the
        /// selector, and restructuring target selection around it is separate work.</para>
        /// </remarks>
        private int PickCastSpell(CombatEncounter encounter, Combatant caster, Combatant target) {
            if (_spells == null || caster == null || target == null || encounter == null) {
                return OpportunisticCasts.NoSpell;
            }

            // A silenced caster picks nothing at all — the routine's first act.
            bool silenced = encounter.Effects.Find(caster, GameData.Resources.Spells.SpellIds.ThoughtsLikeClouds)
                != GameData.Resources.Spells.ActiveSpellEffectPool.None;
            if (!MonsterSpellcasting.CanSelect(silenced)) {
                return OpportunisticCasts.NoSpell;
            }

            for (int id = MonsterSpellcasting.FirstCandidate(_spells.Count); id >= 0; id--) {
                if (!_spells.TryGetValue(id, out GameData.Resources.Spells.Spell spell)) {
                    continue;
                }
                bool selected = MonsterSpellcasting.Selects(
                    id,
                    MonsterSpellcasting.InMonsterRepertoire(id, spell.IsMartial, spell.TargetingType),
                    _canCast != null && _canCast(caster, id),
                    // The 50% roll the original takes per surviving candidate.
                    _rnd(2) == 0,
                    encounter.Effects.Find(target, id)
                        != GameData.Resources.Spells.ActiveSpellEffectPool.None);
                if (selected) {
                    return id;
                }
            }

            return OpportunisticCasts.NoSpell;
        }

        private (Combatant Target, int SpellId, bool RecordAsTarget) PickCastTarget(
            CombatEncounter encounter, Combatant monster, Profile profile) {
            // Too worn down to act at all: the original short-circuits the attempt loop entirely.
            if (!MonsterSpellcasting.WellEnoughToAct(monster.Health + monster.Stamina)) {
                return (null, OpportunisticCasts.NoSpell, false);
            }

            Combatant lastFound = null;
            for (var attempt = 0; attempt < MonsterSpellcasting.SlotCount; attempt++) {
                if (!MonsterSpellcasting.CommitsToAttempt(_rnd(100))) {
                    continue;
                }

                int slot = MonsterSpellcasting.SlotFor(profile.SpellcastPattern, attempt);
                if (slot == 0) {
                    // Pattern 0 has no row at all — the original never enters the loop for it. Not
                    // reachable through the cascade (a caster has a pattern), but SlotFor also
                    // returns 0 out of range, and ActionOf(0) falls through to TargetedCast.
                    return (null, OpportunisticCasts.NoSpell, false);
                }
                MonsterSpellcasting.SlotAction kind = MonsterSpellcasting.ActionOf(slot);
                if (kind == MonsterSpellcasting.SlotAction.SpecialFirst) {
                    (Combatant ally, int healSpell) = TryHeal(encounter, monster, profile);
                    if (healSpell != OpportunisticCasts.NoSpell) {
                        return (ally, healSpell, false);
                    }
                    continue;
                }
                if (kind != MonsterSpellcasting.SlotAction.TargetedCast) {
                    // Slot 8, and skipping it IS the behaviour: its routine is unreachable past a
                    // C precedence bug in the shipped binary, so the attempt is spent and nothing
                    // happens. See MonsterSpellcasting.SlotAction.SpecialLast.
                    continue;
                }

                // *** THE OPPORTUNISTIC PASSES RUN HERE, INSIDE THE ATTEMPT, AND NOWHERE ELSE. ***
                // combat_ai_execute_turn opens with combat_ai_pick_action and returns straight away
                // when it fires (CBTAI.C:150), and combat_ai_execute_turn is the BODY of every
                // targeted-cast slot — combat_ai_turn_kind6 and its five packet siblings are one
                // line each. So this is per attempt, not once per turn: a caster that rolls three
                // targeted-cast attempts gets three chances at a Black Slayer, each with its own
                // skip roll. Hoisting it above the loop would silently make it a tenth as likely
                // to fire, and hoisting it above the morale check (as this model's own summary once
                // implied) would let a routing creature stop to cast on its way off the field.
                OpportunisticCasts.Cast opportunistic = OpportunisticCasts.Choose(
                    OpponentCandidates(encounter, monster), _rnd,
                    spellId => _canCast != null && _canCast(monster, spellId));
                if (opportunistic.Fires) {
                    IReadOnlyList<Combatant> opponents = SidesFor(encounter, monster).Opponents;
                    return (opponents[opportunistic.TargetIndex], opportunistic.SpellId, true);
                }

                // AiTurnPackets.RoleFor, not a cast to TargetRole. The mode IS the packet index and
                // the enum happens to be numbered to match, so `(TargetRole)mode` works today — by
                // coincidence of two independently-chosen numberings. The named mapping states the
                // dependency and bounds-guards it, and it is the only production use of the packet
                // table, which was modelled and tested with nothing calling it.
                int mode = MonsterSpellcasting.TargetModeOf(slot);
                (Combatant found, bool commits) = PickCastVictim(encounter, monster, profile,
                    AiTurnPackets.RoleFor(mode));

                // *** EVERY ATTEMPT OVERWRITES THE TARGET, INCLUDING WITH NOTHING. ***
                // combatenc_ai_pick_target_by_role opens with `actor->inner->target = 0` and then
                // fills it only if a candidate is accepted (CBENC.C:551), so what the actor is left
                // aiming at is the LAST attempt's answer — null included. The gates that follow
                // decide only whether the cast happens; they never un-choose. Keeping the last
                // NON-NULL instead would have a caster remembering a target the final slot's role
                // filter rejected.
                lastFound = found;
                if (found != null && commits) {
                    return (found, PickCastSpell(encounter, monster, found), true);
                }
            }

            return (lastFound, OpportunisticCasts.NoSpell, lastFound != null);
        }

        /// <summary>
        /// The ordinary cast's victim — <b>two passes, not one</b>.
        /// <see cref="MonsterCasterTurn"/>.
        /// </summary>
        /// <remarks>
        /// <c>combat_ai_execute_turn</c> selects with a clearance derived from the caster's own
        /// skill, and only if that finds NOBODY does it retry with clearance 0 and act on whatever
        /// the wider net catches. Three things differ between the passes and all three were missing:
        ///
        /// <list type="bullet">
        /// <item><b>Clearance.</b> <c>4 - casting / 25</c> first, then 0. The single pass this
        /// replaces always used 0 — i.e. only ever the retry — so a novice caster happily targeted
        /// someone ringed by allies, which is the discrimination the first pass exists to apply.</item>
        /// <item><b>The health gate.</b> Threshold index 0 on the first pass and 1 on the retry.
        /// Both entries hold 10 today, so this changes nothing yet and is transcribed because the
        /// data keeps them apart.</item>
        /// <item><b>Line of sight.</b> The first pass traces a projectile path and refuses without
        /// one; the retry does not test it at all, so the fallback can act through cover the first
        /// pass rejected.</item>
        /// </list>
        ///
        /// <para><b>A first pass that finds somebody and then fails a gate ends the ATTEMPT, not
        /// the targeting.</b> It does not fall through to the retry — the retry arm is the
        /// <c>else</c> of "was a target found", not a general second chance — and the target it
        /// found stays chosen either way. That distinction is the whole return value here: the
        /// caller keeps the actor and only stops the turn on <c>Commits</c>.</para>
        /// </remarks>
        /// <returns>
        /// The actor chosen, and whether the gates let this attempt actually cast at them.
        /// </returns>
        private (Combatant Victim, bool Commits) PickCastVictim(CombatEncounter encounter,
            Combatant monster, Profile profile, TargetRole role) {
            Combatant first = PickTarget(encounter, monster, role, AiAction.Cast,
                MonsterCasterTurn.ClearanceFor(profile.CastingSkill));
            if (first != null) {
                bool healthy = MonsterCasterTurn.HealthAllowsCasting(monster.Health,
                    CombatCapability.ShippedHealthThresholds,
                    MonsterCasterTurn.FirstPassThresholdIndex);
                return (first, healthy && (!MonsterCasterTurn.FirstPassNeedsLineOfSight
                    || HasLineOfFire(encounter, monster, first)));
            }

            Combatant retry = PickTarget(encounter, monster, role, AiAction.Cast,
                MonsterCasterTurn.RetryClearance);
            return (retry, retry != null && MonsterCasterTurn.HealthAllowsCasting(monster.Health,
                CombatCapability.ShippedHealthThresholds,
                MonsterCasterTurn.RetryThresholdIndex));
        }

        /// <summary>Whether a projectile path traces from one actor to another.</summary>
        private bool HasLineOfFire(CombatEncounter encounter, Combatant from, Combatant to) =>
            CombatLineOfFire.IsClear(from.X, from.Y, to.X, to.Y,
                CombatLineOfFire.BlockedByLivingActor((x, y) => ActorAt(encounter, x, y), from));

        /// <summary>
        /// Action slot 1 — support an ally rather than attack. <see cref="MonsterHealTurn"/>.
        /// </summary>
        /// <remarks>
        /// <b>The recipient is deliberately NOT recorded as the caster's target.</b>
        /// <c>monster_healAnAlly</c> calls <c>Cast_Spell</c> straight and never writes
        /// <c>actor->inner->target</c> — and it must not, because that field feeds the
        /// target-selection heuristics (<c>AttackersAlready</c>, <c>TargetsTheLeader</c>). Writing
        /// an ally into it would have the pack counting a healer as one of the attackers converging
        /// on a party member who is not being attacked at all.
        ///
        /// <para>The two lists really are different — the spell is chosen from the OPPONENTS'
        /// health and cast on an ALLY. See <see cref="MonsterHealTurn"/> for why that is
        /// transcribed rather than tidied.</para>
        /// </remarks>
        private (Combatant Ally, int SpellId) TryHeal(CombatEncounter encounter, Combatant monster,
            Profile profile) {
            // The shipped ladder rather than the one CombatAffinityTables reads out of the EXE:
            // it is the same nine numbers, and threading a second list through the constructor for
            // a constant would buy nothing. If a mod ever edits that table, this is the line.
            if (_canCast == null || !MonsterHealTurn.WellEnoughToHelp(
                    monster.Health, CombatCapability.ShippedHealthThresholds)) {
                return (null, OpportunisticCasts.NoSpell);
            }

            (IReadOnlyList<Combatant> opponents, IReadOnlyList<Combatant> allies) =
                SidesFor(encounter, monster);

            var opposingHealth = new List<int>(opponents.Count);
            foreach (Combatant c in opponents) {
                opposingHealth.Add(c.Health);
            }

            // The probe is the FIRST actor of the caster's own side, and the ward's "already has
            // it" test reads exactly that actor rather than whoever ends up receiving the spell.
            Combatant probe = allies.Count > 0 ? allies[0] : null;
            bool probeWarded = probe != null && encounter.Effects != null
                && encounter.Effects.Find(probe, MonsterHealTurn.WardSpell)
                    != GameData.Resources.Spells.ActiveSpellEffectPool.None;

            int spell = MonsterHealTurn.ChooseSpell(opposingHealth,
                _rnd(MonsterHealTurn.RestoreRollBound),
                _canCast(monster, MonsterHealTurn.RestoreSpell),
                _canCast(monster, MonsterHealTurn.WardSpell),
                probeWarded);
            if (spell == MonsterHealTurn.NoSpell) {
                return (null, OpportunisticCasts.NoSpell);
            }

            var allyHealth = new List<int>(allies.Count);
            var casterIndex = -1;
            for (var i = 0; i < allies.Count; i++) {
                allyHealth.Add(_profileOf(allies[i]).HealthPercent);
                if (allies[i] == monster) {
                    casterIndex = i;
                }
            }

            int pick = MonsterHealTurn.PickRecipient(allyHealth, casterIndex);
            return pick < 0
                ? (null, OpportunisticCasts.NoSpell)
                : (allies[pick], spell);
        }

        /// <summary>
        /// The acting creature's opponents, in the shape the opportunistic passes read them.
        /// </summary>
        /// <remarks>
        /// <b>Opponents, because that is what <c>g_pCombatActiveActors</c> is during an AI turn</b>
        /// — see <see cref="OpportunisticCasts"/>, which carried the opposite claim marked
        /// "SETTLED" until the spells were looked up. Handing it the ally list here would have a
        /// monster casting Strength Drain on its own pack.
        ///
        /// <para>Index order is preserved because the pass returns an INDEX into this list, and it
        /// is the same <c>SidesFor</c> ordering the caller then reads back.</para>
        /// </remarks>
        private List<OpportunisticCasts.Candidate> OpponentCandidates(CombatEncounter encounter,
            Combatant actor) {
            IReadOnlyList<Combatant> opponents = SidesFor(encounter, actor).Opponents;
            var list = new List<OpportunisticCasts.Candidate>(opponents.Count);
            foreach (Combatant c in opponents) {
                list.Add(new OpportunisticCasts.Candidate {
                    CreatureType = c.ClassId,
                    IsDead = c.IsDead,
                    IsFleeing = (c.Flags & CombatantFlags.Fleeing) != 0,
                    // A corpse keeps its tile, so "off the grid" is the creature that LEFT — the
                    // same bar SlayerRevival applies, and the reason Final Rest can reach a body.
                    IsOnGrid = c.X >= 0 && c.Y >= 0,
                    ProjectilePathIsClear = CombatLineOfFire.IsClear(actor.X, actor.Y, c.X, c.Y,
                        CombatLineOfFire.BlockedByLivingActor(
                            (x, y) => ActorAt(encounter, x, y), actor)),
                });
            }
            return list;
        }

        /// <summary>
        /// <b>The search radius and the ally clearance are properties of the BEHAVIOUR</b>, which is
        /// why the action has to come in here rather than being fixed on the resolver.
        /// </summary>
        /// <remarks>
        /// Melee sweeps the whole field (100, exclusive), a crossbow shot sweeps 10 (inclusive), the
        /// caster wrappers and the "anyone" fallbacks sweep 6. A single resolver-wide distance
        /// matches none of them. The clearance is derived from the shooter's own crossbow accuracy
        /// and applies to the ranged path alone — <c>combat_selectTargetByCriterion</c> @0x64ff6,
        /// the melee selector, does not even take the parameter.
        /// </remarks>
        private Combatant PickTarget(CombatEncounter encounter, Combatant monster, TargetRole role,
            AiAction action, int clearanceOverride = -1, int radiusOverride = -1) {
            Profile shooter = _profileOf(monster);
            bool melee = action == AiAction.MeleeOrMove;
            // The row walks name their own radius per slot (MonsterActionPatterns.Slot.Radius).
            int maxDistance = radiusOverride >= 0 ? radiusOverride : CombatAi.SearchRadiusFor(action, role);
            // The two halves spread the pack by DIFFERENT rules, and each selector implements only
            // its own: clearance is a ranged idea, the saturation cap a melee one.
            //
            // *** THE CAST PATH HAS A CLEARANCE OF ITS OWN AND USED TO GET ZERO. ***
            // combat_ai_execute_turn opens with `4 - stat(actor, 7) / 25` and hands that to target
            // selection, so a poor caster refuses a target standing among its allies and an expert
            // accepts one. This branch only knew about the crossbow, so every monster caster
            // targeted as though it were an expert — the same shape as the shooter bug this
            // method's remarks already describe. The caller supplies it now
            // (MonsterCasterTurn.ClearanceFor); -1 means "derive it here as before".
            int clearance = clearanceOverride >= 0
                ? clearanceOverride
                : action == AiAction.Shoot
                    ? CombatAi.AllyClearanceForAccuracy(shooter.CrossbowAccuracy)
                    : 0;
            (IReadOnlyList<Combatant> opponents, IReadOnlyList<Combatant> allies) =
                SidesFor(encounter, monster);
            int attackerCap = melee
                ? CombatAi.MaxAttackersPerCandidate(LiveCount(allies), LiveCount(opponents))
                : 0;

            var candidates = new List<TargetCandidate>(opponents.Count);
            foreach (Combatant member in opponents) {
                Profile p = _profileOf(member);
                candidates.Add(new TargetCandidate {
                    X = member.X,
                    Y = member.Y,
                    IsDead = member.IsDead,
                    CanCastSpells = p.CanCastSpells,
                    CanShoot = p.CanShoot,
                    StaminaPercent = p.PoolPercent,
                    HasTarget = member.Target != null,
                    TargetIsDead = member.Target != null && member.Target.IsDead,
                    TargetsTheLeader = member.Target != null && member.Target == LeadEnemy(encounter),
                    // *** The monsters' OWN side is what disqualifies a target, not the party's. ***
                    // combatenc_party_within_cheby scans the encounter roster despite its name, so
                    // the pack spreads out instead of converging. Filling this from the party would
                    // invert the behaviour.
                    AlliesNearby = AlliesWithin(encounter, member, clearance, allies),
                    AttackersAlready = AttackersAimedAt(encounter, member, allies),
                    // Melee only, like AttackersAlready above: the ranged selector has no such rule,
                    // and asking the grid for it would answer a question that half of the AI never
                    // poses.
                    ApproachBlocked = melee
                        && CombatAi.ApproachIsBlocked(monster.X, monster.Y, member.X, member.Y,
                            _isBlocked),
                });
            }

            int index = CombatAi.SelectTarget(monster.X, monster.Y, candidates,
                maxDistance, role, clearance,
                // Only the melee selector skips at exactly maxDistance; at 100 it changes nothing,
                // at the radius-6 "engage anyone" sweep it is the difference between 5 and 6.
                excludeAtMaxDistance: melee,
                maxAttackersPerCandidate: attackerCap);
            return index >= 0 && index < opponents.Count ? opponents[index] : null;
        }

        /// <summary>
        /// The side an actor is fighting, and the side it belongs to.
        /// </summary>
        /// <remarks>
        /// <b>THIS IS THE ORIGINAL'S SIDE SWAP, MADE STRUCTURAL.</b> <c>combat_arena_swap_tgt_state</c>
        /// re-aims one global pair of actor lists so the SAME AI routine can play either side —
        /// which is how auto-resolve plays the party (<see cref="AutoResolveLoop"/>), and why an
        /// auto-resolved party fights exactly like a monster would. Deriving the two lists from the
        /// actor gets the same behaviour without a global to swap back.
        ///
        /// <para>Every selector below took <c>encounter.Party</c> as its candidates and
        /// <c>encounter.Enemies</c> as its allies outright, which was correct only because the sole
        /// caller passed a monster. Handing it a party member would have had them hunting their own
        /// line — a latent bug that could not fire while nothing tried, and would have fired the
        /// moment auto-resolve did.</para>
        /// </remarks>
        private static (IReadOnlyList<Combatant> Opponents, IReadOnlyList<Combatant> Allies) SidesFor(
            CombatEncounter encounter, Combatant actor) =>
            actor != null && encounter.Party.Contains(actor)
                ? ((IReadOnlyList<Combatant>)encounter.Enemies, encounter.Party)
                : (encounter.Party, encounter.Enemies);

        private static Combatant LeadEnemy(CombatEncounter encounter) =>
            encounter.Enemies.Count > 0 ? encounter.Enemies[0] : null;

        private static int LiveCount(IReadOnlyList<Combatant> side) {
            var live = 0;
            for (var i = 0; i < side.Count; i++) {
                if (!side[i].IsDead) {
                    live++;
                }
            }
            return live;
        }

        // How many of the monsters' own side have already chosen this candidate this round.
        private static int AttackersAimedAt(CombatEncounter encounter, Combatant candidate,
            IReadOnlyList<Combatant> allies) {
            var aimed = 0;
            foreach (Combatant enemy in allies) {
                if (!enemy.IsDead && enemy.Target == candidate) {
                    aimed++;
                }
            }
            return aimed;
        }

        private static int AlliesWithin(CombatEncounter encounter, Combatant of, int range,
            IReadOnlyList<Combatant> allies) {
            if (range == 0) {
                return 0;
            }
            var count = 0;
            foreach (Combatant enemy in allies) {
                if (Chebyshev(enemy.X, enemy.Y, of.X, of.Y) < range) {
                    count++;
                }
            }
            return count;
        }

        private static int Chebyshev(int ax, int ay, int bx, int by) {
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            return dx > dy ? dx : dy;
        }
    }
}
