namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Spells;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Monster;
    using NUnit.Framework;
    using System.Linq;
    using System.Collections.Generic;

    /// <summary>
    /// Putting a conjured creature on the grid — <c>CombatRuntime.Summon</c>.
    /// </summary>
    /// <remarks>
    /// <b><c>EnterRoster</c> could not do this.</b> It builds enemies from session roster slots and
    /// reads their stats off the save; a conjured creature has neither a slot nor saved stats, which
    /// is why summoning needed a second entrance rather than an extra argument.
    /// </remarks>
    public class SummonSpawnTests {
        private const int Conjured = 46;   // 0x2e, the Horn of Algon Kokoon's creature

        private static MonsterStats Template(int id, ushort health = 12, ushort crossbow = 0) =>
            new MonsterStats($"MONST{id:00}") {
                CreatureId = id,
                Health = new StatRange { Min = health, Max = health },
                Stamina = new StatRange { Min = 9, Max = 9 },
                Speed = new StatRange { Min = 4, Max = 4 },
                Strength = new StatRange { Min = 21, Max = 21 },
                AccuracyCrossbow = new StatRange { Min = crossbow, Max = crossbow },
                AccuracyMelee = new StatRange { Min = 33, Max = 33 },
                AccuracyCasting = new StatRange { Min = 0, Max = 0 },
                Defense = new StatRange { Min = 17, Max = 17 },
            };

        private static CombatRuntime Runtime(params (int Id, MonsterStats Stats)[] templates) =>
            Runtime(null, templates);

        private static CombatRuntime Runtime(System.Action<int> playSfx,
            params (int Id, MonsterStats Stats)[] templates) {
            var map = new Dictionary<int, MonsterStats>();
            foreach ((int id, MonsterStats stats) in templates) {
                map[id] = stats;
            }
            var runtime = new CombatRuntime(null, playSfx: playSfx) { MonsterTemplates = map };
            runtime.Enter(new List<Combatant>());
            return runtime;
        }

        [Test]
        public void ASummonPlaysTheCREATIONCue() {
            // combat_summon_creature @0x67688 plays mcreate once the summon is committed -- between
            // the optional tile prompt and writing the grid position. MonsterSummon has carried the
            // id since it was ported and nothing played it: the whole type was reachable only
            // through InitialFlags, so the type-level audit counted it as consumed.
            var played = new List<int>();
            CombatRuntime runtime = Runtime(played.Add, (Conjured, Template(Conjured)));

            runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.AreEqual(new[] { MonsterSummon.Sound }, played);
            Assert.AreEqual(0x3a, MonsterSummon.Sound, "mcreate (58), the lighting spells' cue");
        }

        [Test]
        public void AFailedSummonIsSILENT() {
            // No template, nothing spawned -- and the cue must not play, or the player hears a
            // creature arrive that did not.
            var played = new List<int>();
            CombatRuntime runtime = Runtime(played.Add, (Conjured, Template(Conjured)));

            Assert.IsNull(runtime.Summon(Conjured + 1, x: 3, y: 4, rnd: _ => 0));
            Assert.IsEmpty(played);
        }

        [Test]
        public void ASummonJoinsThePARTYSSideWithItsRolledStats() {
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.IsNotNull(summon);
            // *** IT FIGHTS FOR THE PARTY. *** This asserted Enemies until 2026-08-31 and pinned
            // the defect: cspell_summon_monster uses combat_actor_party_add, and A is the party's
            // side (COMBAT.C:125-130 / CBENC.C:908 / the index-100 split). See TASK-268.
            Assert.Contains(summon, runtime.Encounter.Party);
            Assert.IsFalse(runtime.Encounter.Enemies.Contains(summon));
            Assert.IsFalse(summon.IsPartyMember,
                "on the party's SIDE without being one of its CHARACTERS — PartySlot stays 0");
            Assert.AreEqual(Conjured, summon.ClassId);
            Assert.AreEqual(3, summon.X);
            Assert.AreEqual(4, summon.Y);
            Assert.AreEqual(12, summon.Health);
            Assert.AreEqual(4, summon.Speed);
        }

        [Test]
        public void ASummonIsTOLDApartFromBothTheOtherKindsOfActor() {
            // *** THE ONE QUESTION THAT CANNOT BE ANSWERED BY EITHER FIELD ALONE. ***
            // IsPartyMember is false for a summon AND for every enemy; being in Party is true for a
            // summon AND for every character. It takes both, which is why the runtime answers it
            // rather than the Combatant. What reads it is ProfileOf, to give a summon zero morale.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            var character = new Combatant { PartySlot = 1 };
            var enemy = new Combatant();
            runtime.Encounter.Party.Add(character);
            runtime.Encounter.Enemies.Add(enemy);

            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.IsTrue(runtime.IsSummoned(summon));
            Assert.IsFalse(runtime.IsSummoned(character), "a party character is not conjured");
            Assert.IsFalse(runtime.IsSummoned(enemy), "an enemy is on the other side");
            Assert.IsFalse(runtime.IsSummoned(null));
        }

        [Test]
        public void ASummonsMoraleIsZERO_whichIsWhatStopsItRouting() {
            // MonsterSummon.Morale has been modelled from the start and had no consumer: ProfileOf
            // built every profile from MONST unconditionally, so a conjured creature carried its
            // KIND's flee threshold and could turn and run from the fight the party called it into.
            // The zero lands in MonsterMorale.Routs, whose last line is `return morale != 0`.
            Assert.AreEqual(0, MonsterSummon.Morale);
            Assert.IsFalse(GameData.Resources.Combat.MonsterMorale.Routs(
                staminaPercent: 1, morale: MonsterSummon.Morale, rollPercent: 0,
                thresholds: new[] { 85, 85, 85, 85 }, isUnderground: false),
                "the worst stamina and the worst roll still must not rout a summon");
            // The same creature with its kind's nerve DOES rout on that roll — otherwise the
            // assertion above passes for a reason that has nothing to do with the zero.
            Assert.IsTrue(GameData.Resources.Combat.MonsterMorale.Routs(
                staminaPercent: 1, morale: 20, rollPercent: 0,
                thresholds: new[] { 85, 85, 85, 85 }, isUnderground: false));
        }

        [Test]
        public void ITSSTATBLOCKIsRegistered_orItFightsAtZeroOnEverythingTheBlockHolds() {
            // *** The half a "just add a Combatant" implementation misses. *** A Combatant carries
            // only health, stamina and speed; a swing reads accuracy, strength and defence out of
            // the runtime's own map. A summon left out of it swings at zero and defends at zero,
            // with nothing to say so.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            Combatant summon = runtime.Summon(Conjured, 1, 1, rnd: _ => 0);
            ActorStat[] stats = runtime.StatsFor(summon);

            Assert.IsNotNull(stats, "no stat block means every derived stat reads zero");
            Assert.AreEqual(33, stats[(int)ActorAttribute.AccuracyMelee].Base);
            Assert.AreEqual(17, stats[(int)ActorAttribute.Defense].Base);
            Assert.AreEqual(21, stats[(int)ActorAttribute.Strength].Base);
        }

        [Test]
        public void EVERYRolledStatSetsMAXAsWellAsBASE() {
            // stats[i].max = stats[i].base = result. A stat whose Max is 0 is INERT — both read and
            // write short-circuit — so setting only Base would make every one of them read as absent
            // and the creature would arrive with no stats at all.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            ActorStat[] stats = runtime.StatsFor(runtime.Summon(Conjured, 1, 1, rnd: _ => 0));

            foreach (ActorAttribute attribute in MonsterStatRoll.RolledAttributes) {
                ActorStat stat = stats[(int)attribute];
                Assert.AreEqual(stat.Base, stat.Max, $"{attribute} max must equal its rolled base");
            }
        }

        [Test]
        public void ITLandsNOTREADY_soItDoesNotActOnTheRoundItAppears() {
            // MonsterSummon.InitialFlags ASSIGNS rather than ORs, so Ready is clear. Setting the
            // summon bit on an otherwise ready combatant hands the caster a free extra action the
            // moment the spell resolves.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            Combatant summon = runtime.Summon(Conjured, 1, 1, rnd: _ => 0);

            Assert.AreEqual(MonsterSummon.InitialFlags, summon.Flags);
            Assert.IsFalse(summon.Flags.HasFlag(CombatantFlags.Ready));
            Assert.IsTrue(summon.Flags.HasFlag(CombatantFlags.AiSummon));
        }

        [Test]
        public void THETEMPLATESwapChangesTheNUMBERSAndNotWhatTheThingIS() {
            // Creature 0x12 with an intact crossbow rolls creature 10's file — but it is still
            // creature 0x12. Copying the template id into ClassId would make it a different monster
            // to every species-keyed rule in the fight.
            CombatRuntime runtime = Runtime(
                (MonsterStatRoll.SubstitutingCreature,
                    Template(MonsterStatRoll.SubstitutingCreature, crossbow: 0)),
                (MonsterStatRoll.SubstitutedTemplate,
                    Template(MonsterStatRoll.SubstitutedTemplate, crossbow: 55)));

            Combatant armed = runtime.Summon(MonsterStatRoll.SubstitutingCreature, 1, 1,
                hasIntactCrossbow: true, rnd: _ => 0);

            Assert.AreEqual(MonsterStatRoll.SubstitutingCreature, armed.ClassId,
                "it keeps its own type; only the stats come from elsewhere");
            Assert.AreEqual(55, runtime.StatsFor(armed)[(int)ActorAttribute.AccuracyCrossbow].Base,
                "and the swap is what gives it the skill to fire what it carries");
        }

        [Test]
        public void WithoutTheCrossbowItRollsItsOwnFileAndCannotShoot() {
            CombatRuntime runtime = Runtime(
                (MonsterStatRoll.SubstitutingCreature,
                    Template(MonsterStatRoll.SubstitutingCreature, crossbow: 0)),
                (MonsterStatRoll.SubstitutedTemplate,
                    Template(MonsterStatRoll.SubstitutedTemplate, crossbow: 55)));

            Combatant bare = runtime.Summon(MonsterStatRoll.SubstitutingCreature, 1, 1,
                hasIntactCrossbow: false, rnd: _ => 0);

            Assert.AreEqual(0, runtime.StatsFor(bare)[(int)ActorAttribute.AccuracyCrossbow].Base);
        }

        [Test]
        public void AMissingTemplateSpawnsNOTHINGRatherThanAZeroStatCreature() {
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            Assert.IsNull(runtime.Summon(creatureType: 99, 1, 1, rnd: _ => 0));
            Assert.IsEmpty(runtime.Encounter.Enemies);
        }

        /// <summary>Fill the fight's actor array to <paramref name="actors"/> before summoning.</summary>
        /// <remarks>
        /// Added directly to Party because that IS the seven-actor array — the same list Summon
        /// appends to. Going through Summon to fill it would make the test depend on the rule it is
        /// checking.
        /// </remarks>
        private static void FillTo(CombatRuntime runtime, int actors) {
            while (runtime.Encounter.Party.Count < actors) {
                runtime.Encounter.Party.Add(new Combatant());
            }
        }

        [Test]
        public void ARefusedSummonTELLSThePlayer() {
            // *** A REFUSAL THAT ONLY LOGS IS INVISIBLE. *** The original shows ddx 145 and gives
            // up, and nothing refunds the cast — so without the dialog a full field looks to the
            // player exactly like a spell that did nothing. The rule and the id were both modelled
            // long before anything could reach them.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            var shown = new System.Collections.Generic.List<int>();
            runtime.ShowDialog = shown.Add;
            FillTo(runtime, MonsterSummon.FightActorCapacity);

            Assert.IsNull(runtime.Summon(Conjured, 1, 1, rnd: _ => 0));
            CollectionAssert.AreEqual(new[] { MonsterSummon.NoRoomDialog }, shown);
        }

        [Test]
        public void ASummonThatFITSSaysNothing() {
            // The other half: the dialog belongs to the refusal, not to summoning.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            var shown = new System.Collections.Generic.List<int>();
            runtime.ShowDialog = shown.Add;
            FillTo(runtime, MonsterSummon.FightActorCapacity - 1);

            Assert.IsNotNull(runtime.Summon(Conjured, 1, 1, rnd: _ => 0));
            CollectionAssert.IsEmpty(shown);
        }

        [Test]
        public void ASeventhActorIsRefused_andNothingIsPlaced() {
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            FillTo(runtime, MonsterSummon.FightActorCapacity);

            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.IsNull(summon, "combat_actor_slot_append refuses at seven");
            Assert.AreEqual(MonsterSummon.FightActorCapacity, runtime.Encounter.Party.Count,
                "a refused summon must not occupy a slot");
        }

        /// <summary>The boundary, from the other side — six accept, seven refuse.</summary>
        /// <remarks>
        /// The original's test is <c>jge</c> on the count BEFORE the append, so the capacity is the
        /// first REFUSED count and not the last accepted one. Asserting only the refusal would pass
        /// just as well for an off-by-one that refused at six.
        /// </remarks>
        [Test]
        public void TheSIXTHActorStillFits() {
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            FillTo(runtime, MonsterSummon.FightActorCapacity - 1);

            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.IsNotNull(summon);
            Assert.AreEqual(MonsterSummon.FightActorCapacity, runtime.Encounter.Party.Count);
        }

        /// <summary>A refused summon is silent — the cue belongs to the placement, not the attempt.</summary>
        /// <remarks>
        /// The same rule <see cref="AFailedSummonIsSILENT"/> pins for a missing template, checked
        /// again on the refusal that can now actually happen: <c>audio_PlaySound</c> is downstream
        /// of the append in <c>combat_summon_creature</c> @0x67655, so a full field never sounds.
        /// </remarks>
        [Test]
        public void ARefusedSummonIsSILENT() {
            // SILENT as in no SOUND — the creation cue plays after the room check, so a refusal
            // never reaches it. It does still SPEAK: see ARefusedSummonTELLSThePlayer.
            var played = new List<int>();
            CombatRuntime runtime = Runtime(played.Add, (Conjured, Template(Conjured)));
            FillTo(runtime, MonsterSummon.FightActorCapacity);

            runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            CollectionAssert.IsEmpty(played);
        }

        /// <summary>A summon SPELL is a cast: the creature appears and the caster pays.</summary>
        /// <remarks>
        /// <b>Found 2026-09-14: casting one was free.</b> The player's kind-6 cast armed a placement and
        /// the click went straight to <see cref="CombatRuntime.Summon"/>, never through the cast
        /// resolution that bills the caster. In the original <c>cspell_summon_monster</c> is called
        /// from inside <c>cspell_resolve_cast</c> (CSPELL.C:1329), whose tail bills the caster for
        /// kinds 5, 6 and 8 (CSPELL.C:1536). The item path stays unbilled — its cost was the item.
        /// </remarks>
        [Test]
        public void ASummonSPELLBillsTheCaster_whileTheItemPathDoesNot() {
            var session = new GameSession();
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 60, Max = 99 };
            session.SetActorStatsForTest(0, stats);
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, stats, 12, gridX: 5, gridY: 5);
            var entries = new PartyCombatEntries("P1.DAT", new List<SaveGameCombatData> {
                new SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            });
            var runtime = new CombatRuntime(session, null, entries, null) {
                MonsterTemplates = new Dictionary<int, MonsterStats> { [Conjured] = Template(Conjured) },
            };
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            Combatant caster = fight.Party[0];
            var spell = new Spell("S") { TargetingType = 6, EffectSubject = Conjured };

            int beforeItem = caster.Stamina + caster.Health;
            Assert.IsNotNull(runtime.Summon(Conjured, x: 1, y: 1, rnd: _ => 0));
            Assert.AreEqual(beforeItem, caster.Stamina + caster.Health, "an item's summon bills nobody");

            int beforeSpell = caster.Stamina + caster.Health;
            Combatant conjured = runtime.SummonByCast(caster, spell, spellId: 38, power: 5, x: 2, y: 1,
                rnd: _ => 0);

            Assert.IsNotNull(conjured, "the spell still conjures");
            CollectionAssert.Contains(fight.Party, conjured);
            Assert.Less(caster.Stamina + caster.Health, beforeSpell,
                "the caster pays for a summon spell, as for any cast");
        }

        /// <summary>Dannon's Delusions' decoy: a helpless copy that expires with its spell.</summary>
        /// <remarks>
        /// <c>cspell_summon_actor</c>: 1 health, 1 stamina, speed 0, ready, with a Dannon's Delusions
        /// slot lasting the cast's magnitude. The slot is what stops it acting, and the status tick
        /// removes it on expiry — clearing its tile, leaving no corpse.
        /// </remarks>
        [Test]
        public void ADecoyIsHelpless_andLeavesTheFieldWhenItsSpellRunsOut() {
            var session = new GameSession();
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            session.SetActorStatsForTest(0, stats);
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, stats, 12, gridX: 5, gridY: 5);
            var entries = new PartyCombatEntries("P1.DAT", new List<SaveGameCombatData> {
                new SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            });
            var runtime = new CombatRuntime(session, null, entries, null);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });

            Combatant decoy = runtime.SummonDecoy(fight.Party[0], duration: 2, x: 1, y: 1);

            Assert.IsNotNull(decoy);
            CollectionAssert.Contains(fight.Party, decoy);
            Assert.AreEqual(1, decoy.Health);
            Assert.AreEqual(1, decoy.Stamina);
            Assert.AreEqual(0, decoy.Speed);
            Assert.IsFalse(decoy.CanAct(strict: true), "its Dannon's Delusions slot stops it acting");
            Assert.IsTrue(runtime.Grid.IsOccupied(1, 1));

            fight.BeginRound(runtime.Grid);
            Assert.IsFalse(decoy.IsDead, "one round left");
            fight.BeginRound(runtime.Grid);
            Assert.IsTrue(decoy.IsDead, "the spell ran out");
            Assert.IsFalse(runtime.Grid.IsOccupied(1, 1), "and its tile is free again");
        }

        [Test]
        public void NoFightMeansNoSummon() {
            var runtime = new CombatRuntime(null) {
                MonsterTemplates = new Dictionary<int, MonsterStats> { [Conjured] = Template(Conjured) },
            };

            Assert.IsNull(runtime.Summon(Conjured, 1, 1, rnd: _ => 0));
        }
        [Test]
        public void ASummonOCCUPIESItsTile_soTheNextOneCannotStandOnIt() {
            // *** FOUND BY DRIVING THE HORN OF ALGON KOKOON IN A LIVE FIGHT. *** Its one use is two
            // placements, and without the grid being marked both conjured creatures landed on the
            // same cell with nothing refusing the second click — SummonPlacement.Accepts asks the
            // grid, and the grid had not been told. cspell_summon_monster ends with
            // combatgrid_tile_set_word for exactly this reason.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));

            Assert.IsTrue(GameData.Resources.Combat.SummonPlacement.Accepts(runtime.Grid, 3, 4),
                "the cell is free to begin with");

            runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            Assert.IsTrue(runtime.Grid.IsOccupied(3, 4));
            Assert.IsFalse(GameData.Resources.Combat.SummonPlacement.Accepts(runtime.Grid, 3, 4),
                "and a second placement on it is refused");
        }

        [Test]
        public void ASummonsAITurnTargetsAnENEMY_notTheParty() {
            // *** THE HALF THAT WOULD SILENTLY INVERT. *** A summon standing on the party's side
            // looks right until it swings. SidesFor used to read IsPartyMember, which is false for
            // a summon (PartySlot 0), so it would have taken the PARTY as its opponents — the same
            // bug one layer down from the list it lives in.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            // Somebody on each side, so the assertion below cannot pass by being vacuous: the
            // conjurer to be protected, and a monster to be fought.
            var conjurer = new Combatant { PartySlot = 1, ClassId = 0, X = 0, Y = 0, Health = 10 };
            var monster = new Combatant { PartySlot = 0, ClassId = 9, X = 6, Y = 6, Health = 10 };
            runtime.Encounter.Party.Add(conjurer);
            runtime.Encounter.Enemies.Add(monster);

            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);
            Assert.IsNotNull(summon);

            (System.Collections.Generic.IReadOnlyList<Combatant> opponents, _) =
                SidesOf(runtime, summon);

            Assert.IsTrue(opponents.Contains(monster), "it fights the monster");
            Assert.IsFalse(opponents.Contains(conjurer),
                "and NOT the character who conjured it — the inversion this test exists for");
            Assert.IsFalse(opponents.Contains(summon), "nor itself");
        }

        /// <summary>Whatever the AI would treat as this actor's opponents.</summary>
        private static (System.Collections.Generic.IReadOnlyList<Combatant> Opponents,
            System.Collections.Generic.IReadOnlyList<Combatant> Allies) SidesOf(
            CombatRuntime runtime, Combatant actor) {
            System.Reflection.MethodInfo m = typeof(BakAgain.Combat.MonsterTurnResolver).GetMethod(
                "SidesFor", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            object r = m.Invoke(null, new object[] { runtime.Encounter, actor });
            System.Type t = r.GetType();
            return ((System.Collections.Generic.IReadOnlyList<Combatant>)t.GetField("Item1").GetValue(r),
                (System.Collections.Generic.IReadOnlyList<Combatant>)t.GetField("Item2").GetValue(r));
        }

        [Test]
        public void ASurvivingSummonDoesNotKeepAWIPEDPartysFightAlive() {
            // *** THE OTHER THING MOVING IT BETWEEN LISTS COULD HAVE BROKEN. *** IsOver reads
            // PartyAlive, and a summon now lives in Encounter.Party. It is safe because PartyAlive
            // tests IsPartyMember — the "has a character" question, which is the right one there —
            // but that is a load-bearing coincidence of the two meanings, so it is pinned rather
            // than assumed.
            CombatRuntime runtime = Runtime((Conjured, Template(Conjured)));
            var conjurer = new Combatant { PartySlot = 1, ClassId = 0, X = 0, Y = 0, Health = 10 };
            var monster = new Combatant { PartySlot = 0, ClassId = 9, X = 6, Y = 6, Health = 10 };
            runtime.Encounter.Party.Add(conjurer);
            runtime.Encounter.Enemies.Add(monster);
            Combatant summon = runtime.Summon(Conjured, x: 3, y: 4, rnd: _ => 0);

            // *** ZERO HEALTH IS NOT DEATH. *** IsDead reads the Dead FLAG, which the kill path
            // sets; emptying the stats leaves the combatant alive and this test green for the
            // wrong reason. It failed exactly that way when written.
            conjurer.Health = 0;
            conjurer.Stamina = 0;
            conjurer.Flags |= CombatantFlags.Dead;

            Assert.IsFalse(summon.IsDead, "the spider is still standing");
            Assert.AreEqual(0, runtime.Encounter.PartyAlive(),
                "and it does not count as a living party member");
            Assert.IsTrue(runtime.Encounter.IsOver(), "so the wipe ends the fight");
        }

    }
}
