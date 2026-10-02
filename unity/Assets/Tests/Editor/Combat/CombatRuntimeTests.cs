namespace BakAgain.Tests.Editor.Combat {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using MeleeAttack = GameData.Resources.Combat.CombatActionDispatch.MeleeAttack;
    using NUnit.Framework;

    /// <summary>
    /// Entering, advancing and leaving a fight — the combat umbrella's own scope
    /// (docs/specs/combat-arena.md). No screen, no AI, no camera: this is the part that decides a
    /// fight, so it is asserted without one being rendered.
    /// </summary>
    public class CombatRuntimeTests {
        private static Combatant Enemy(int speed, int health = 10) => new Combatant {
            PartySlot = 0,
            Health = health,
            Stamina = health,
            Speed = speed,
            Flags = CombatantFlags.Ready,
        };

        // No GameSession: the party side comes out empty, which is exactly what lets these tests
        // exercise the loop's own rules without standing a session up.
        private static CombatRuntime Runtime() => new CombatRuntime(session: null);

        private static Combatant PartyMember(int speed, int health = 10) => new Combatant {
            PartySlot = 1,
            Health = health,
            Stamina = health,
            Speed = speed,
            Flags = CombatantFlags.Ready,
        };

        /// <summary>
        /// A fight with a party side. <b>Needed for anything that advances turns</b>: with no party
        /// member the encounter is already over (PartyAlive() == 0 is a wipe), so the loop returns
        /// before resolving anything — which is right, and quietly makes such a test vacuous.
        /// </summary>
        private static ActorStat[] StatBlock(byte health, byte stamina = 20, byte speed = 5,
            byte casting = 20) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 1, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = stamina, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = speed, Max = 99 };
            stats[(int)ActorAttribute.AccuracyCasting] = new ActorStat { Base = casting, Max = 99 };
            return stats;
        }

        private static GameData.Resources.Data.SaveGameCombatData EntryAt(byte x, byte y) =>
            new GameData.Resources.Data.SaveGameCombatData(
                0, 0, x, y, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static GameData.Resources.Combat.PartyCombatEntries Entries(
            params (byte X, byte Y)[] tiles) {
            var slots = new List<GameData.Resources.Data.SaveGameCombatData>();
            foreach ((byte X, byte Y) t in tiles) {
                slots.Add(EntryAt(t.X, t.Y));
            }
            return new GameData.Resources.Combat.PartyCombatEntries("P1.DAT", slots);
        }

        private static GameSession SessionWithParty(int members) {
            var session = new GameSession();
            var indices = new byte[members];
            for (var i = 0; i < members; i++) {
                indices[i] = (byte)i;
                session.SetActorStatsForTest(i, StatBlock(health: 30));
            }
            session.SetActiveParty((byte)members, indices);
            return session;
        }

        /// <summary>A fight with one party member at (3,4) and enemies at the given tiles.</summary>
        private static (CombatRuntime Runtime, CombatEncounter Fight) FightWithEnemiesAt(
            ActorStat[] partyStats, params (byte X, byte Y)[] enemyTiles) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, partyStats);
            session.SetActiveParty(1, new byte[] { 0 });

            var slots = new List<short>();
            for (var i = 0; i < enemyTiles.Length; i++) {
                short slot = (short)(400 + i);
                session.SetRosterActorForTest(slot, StatBlock(health: 20),
                    gridX: enemyTiles[i].X, gridY: enemyTiles[i].Y);
                slots.Add(slot);
            }

            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            return (runtime, runtime.EnterRoster(slots));
        }

        [Test]
        public void AnAdjacentENEMYTakesAwayTheCastOption() {
            // The per-turn rule. Capability is not a character-sheet fact: this same character can
            // cast next turn if the enemy steps away.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));

            Assert.AreEqual((3, 4), (fight.Party[0].X, fight.Party[0].Y));
            Assert.AreEqual((4, 4), (fight.Enemies[0].X, fight.Enemies[0].Y));
            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanCast);
        }

        [Test]
        public void ADistantEnemyLeavesTheCastOptionAlone() {
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)7, (byte)0));

            Assert.IsTrue(runtime.CapabilitiesFor(fight.Party[0]).CanCast);
        }

        [Test]
        public void AnADJACENTALLYDoesNotTakeAwayTheCastOption() {
            // *** The distinction a port drops. *** The measurement is to the nearest OPPONENT, so a
            // bunched-up party is not a disarmed one. Searching "all combatants" would look right in
            // any test with a single character and be wrong in every real fight.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActorStatsForTest(1, StatBlock(health: 30));
            session.SetActiveParty(2, new byte[] { 0, 1 });

            // Both party entries name the SAME tile, so placement puts them side by side.
            var runtime = new CombatRuntime(session, null,
                Entries(((byte)3, (byte)4), ((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);

            Assert.AreEqual(2, fight.Party.Count);
            Assert.AreEqual(1, CombatGrid.ChebyshevDistance(
                fight.Party[0].X, fight.Party[0].Y, fight.Party[1].X, fight.Party[1].Y),
                "the two are adjacent after placement");
            Assert.IsTrue(runtime.CapabilitiesFor(fight.Party[0]).CanCast, "an ally is not a threat");
        }

        [Test]
        public void ZeroCastingSkillRefusesEvenWithRoom() {
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30, casting: 0), ((byte)7, (byte)0));

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanCast);
        }

        [Test]
        public void ADownedCasterCannotCast() {
            // The shipped threshold ladder reduces to health > 0 - this pins the end-to-end effect.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 0), ((byte)7, (byte)0));

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanCast);
        }

        private const byte QuarrelObjectId = 0x24;
        private const byte CrossbowObjectId = 50;

        /// <summary>An ObjectInfoSet describing a crossbow that wears out, and a quarrel stack.</summary>
        private static GameData.Resources.Object.ObjectInfoSet ShootingObjects() =>
            new GameData.Resources.Object.ObjectInfoSet("OBJINFO.DAT",
                new System.Collections.Generic.List<GameData.Resources.Object.ObjectInfo> {
                    new GameData.Resources.Object.ObjectInfo("crossbow") {
                        Number = CrossbowObjectId,
                        ObjectType = GameData.ObjectType.Crossbow,
                        Flags = GameData.Resources.Object.ObjectFlags.Degradable,
                    },
                    new GameData.Resources.Object.ObjectInfo("quarrels") {
                        Number = QuarrelObjectId,
                        ObjectType = GameData.ObjectType.Misc,
                    },
                });

        /// <summary>A party member holding <paramref name="bolts"/> quarrels and a crossbow.</summary>
        private static void GiveArcherPack(GameSession session, byte bolts = 10,
            byte crossbowCondition = 50, ushort crossbowFlags = 0x40) {
            // actorNumber 1 is party position 0 - position is always one below the actor number.
            var pack = new GameData.Resources.Data.SaveGameContainerData(
                new GameData.Resources.Data.SaveGameContainerLocationData(
                    zone: 1, minChapter: 1, maxChapter: 9, worldItemId: 0, x: 0, y: 0, actorNumber: 1),
                GameData.Resources.Data.SaveGameContainerType.Inventory,
                numberOfItems: 2, capacity: 8, dataTypes: 0,
                items: new[] {
                    new GameData.Resources.Data.SaveGameInventoryItemData(
                        CrossbowObjectId, crossbowCondition, crossbowFlags),
                    new GameData.Resources.Data.SaveGameInventoryItemData(QuarrelObjectId, bolts, 0),
                },
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

            session.SetZoneContainersForTest(
                new GameData.Resources.Data.SaveGameZoneContainerStateData(new[] {
                    new GameData.Resources.Data.SaveGameZoneContainerEntryData(1, 0, new[] { pack }),
                }), chapter: 1);
        }

        private static (CombatRuntime Runtime, CombatEncounter Fight) ArcherAgainst(
            (byte X, byte Y) enemyTile, byte bolts = 10, ushort crossbowFlags = 0x40) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: 20),
                gridX: enemyTile.X, gridY: enemyTile.Y);
            GiveArcherPack(session, bolts, crossbowFlags: crossbowFlags);

            var runtime = new CombatRuntime(session, null,
                Entries(((byte)3, (byte)4)), ShootingObjects());
            return (runtime, runtime.EnterRoster(new short[] { 400 }));
        }

        [Test]
        public void RestingSpendsTheTurnAndRecovers() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);
            Combatant member = fight.Party[0];

            int healed = runtime.ResolveRest(member);

            // StatBlock caps Health and Stamina at 99 each, so (99 + 99) / 30 = 6.
            Assert.AreEqual(6, healed, "off the CEILINGS, not the current values");
            Assert.IsTrue((member.Flags & CombatantFlags.DefendCommand) != 0);
            Assert.IsTrue((member.Flags & CombatantFlags.Ready) == 0, "the turn is spent");
            Assert.IsTrue((member.Flags & CombatantFlags.Parry) == 0,
                "Rest is not Defend - only Defend sets Parry, which feeds the to-hit penalty");
        }

        [Test]
        public void RestingHealsTheCombatantAndNotJustTheSavedRecord() {
            // *** THE TEST ABOVE NAMES THIS AND DOES NOT CHECK IT. *** "RestingSpendsTheTurnAndRecovers"
            // asserts the returned amount and the flags and never looks at the combatant, which is
            // how ResolveRest came to heal nothing at all: it read and wrote the SESSION's stats,
            // while a fight's damage lives on the Combatant and WriteBack only ever copies
            // combatant -> session. The pool it saw was the undamaged one, already at
            // RestAction.HealCapPercent, so ModifyHealthPool refused the heal outright.
            //
            // Measured live before the fix: combatant hp30 sta0, session hp66 sta26, ResolveRest
            // answered "healed 3" and moved neither.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);
            Combatant member = fight.Party[0];

            // The fight has hurt them. This is the state the saved record does not know about.
            member.Health = 10;
            member.Stamina = 0;

            int healed = runtime.ResolveRest(member);

            Assert.Greater(healed, 0, "there is room to heal");
            Assert.Greater(member.Health + member.Stamina, 10,
                "rest must move the COMBATANT, which is what the arena reads and what movement costs");
        }

        [Test]
        public void DefendingSetsParryAndSpendsTheTurn() {
            // The command that had no effect at all: OnCombatCommand answered Defend by RESTING, so
            // Parry was never set by anything in the game.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);
            Combatant member = fight.Party[0];

            runtime.ResolveDefend(member);

            Assert.IsTrue((member.Flags & CombatantFlags.Parry) != 0, "Parry is the whole point");
            Assert.IsTrue((member.Flags & CombatantFlags.Ready) == 0, "the turn is spent");
            Assert.IsTrue((member.Flags & CombatantFlags.DefendCommand) == 0,
                "that flag belongs to Rest and feeds nothing in the to-hit path");
        }

        [Test]
        public void APuzzlesGridIsAdoptedRatherThanRebuilt() {
            // A fresh CombatGrid would drop every crystal the puzzle placed, and the fight would look
            // fine right up until nothing could be solved.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            TrapPuzzle puzzle = TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.RedCrystal, 2, 5),
                ((int)TrapElementType.Exit, 1, 11),
            });

            runtime.EnterRoster(null, puzzle);

            Assert.AreEqual(CombatTerrain.Crystal, runtime.Grid.TerrainAt(2, 5));
            Assert.AreSame(puzzle.Grid, runtime.Grid);
        }

        [Test]
        public void AnExitMakesTheEncounterAnObjectiveSoKillingEveryoneDoesNotEndIt() {
            // HasObjective is what keeps a puzzle running with no enemies left. Left false, a room
            // whose enemies are already dead would end on turn one and never be solved.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null, TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.Exit, 1, 11),
            }));

            Assert.IsTrue(fight.HasObjective);
            Assert.IsFalse(fight.IsOver(), "no enemies, but the exit still has to be reached");
        }

        [Test]
        public void AnOrdinaryEncounterIsNotAnObjective() {
            // IsTrapPuzzle is HasExit, so an encounter with elements but no exit must stay an
            // ordinary fight — otherwise every crystal room would refuse to end.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null, TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.RedCrystal, 2, 5),
            }));

            Assert.IsFalse(fight.HasObjective);
        }

        [Test]
        public void APuzzlesOwnPartyTilesBeatP1Dat() {
            // A puzzle's entry points are part of its design; starting the party on their ordinary
            // battle tiles can put them the wrong side of a crystal run.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null, TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.ActorSlot0, 6, 2),
            }, underground: false, partySize: 1));

            Assert.AreEqual(6, fight.Party[0].X);
            Assert.AreEqual(2, fight.Party[0].Y);
        }

        [Test]
        public void ASlotThePuzzleDoesNotPlaceKeepsItsP1DatTile() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActorStatsForTest(1, StatBlock(health: 30));
            session.SetActiveParty(2, new byte[] { 0, 1 });
            var runtime = new CombatRuntime(session, null,
                Entries(((byte)3, (byte)4), ((byte)5, (byte)6)));

            CombatEncounter fight = runtime.EnterRoster(null, TrapPuzzleBuilder.Build(new[] {
                ((int)TrapElementType.ActorSlot0, 6, 2),
            }, underground: false, partySize: 2));

            Assert.AreEqual(6, fight.Party[0].X, "slot 0 was placed by the puzzle");
            Assert.AreEqual(5, fight.Party[1].X, "slot 1 was not, so P1.DAT still decides");
            Assert.AreEqual(6, fight.Party[1].Y);
        }

        /// <summary>A one-member fight, ready to retreat from.</summary>
        private static (CombatRuntime Runtime, CombatEncounter Fight) RetreatFight(
            bool escapeAllowed = true) {
            // Through the real path: the lock is an element in the encounter's TRAPS.DAT record, not
            // a flag the caller sets, so a test that set a flag would not exercise the decode.
            TrapPuzzle puzzle = escapeAllowed
                ? null
                : TrapPuzzleBuilder.Build(
                    new[] { ((int)TrapElementType.RetreatLock, 0, 0) });
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null, puzzle);
            return (runtime, fight);
        }

        [Test]
        public void APassingRollGetsThePartyOut() {
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight();

            CombatRuntime.RetreatOutcome outcome = runtime.ResolveRetreat(fight.Party[0], _ => 0);

            Assert.IsTrue(outcome.Escaped);
            Assert.AreEqual(CombatCommands.RetreatEscapeDialog, outcome.DialogRecord);
            Assert.IsTrue((fight.Party[0].Flags & CombatantFlags.Ready) != 0,
                "getting out does not spend the turn — the fight is over");
        }

        [Test]
        public void AFailedRetreatCostsTheTurn() {
            // Otherwise retreat is a free reroll every round and the 50% becomes a certainty.
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight();

            CombatRuntime.RetreatOutcome outcome = runtime.ResolveRetreat(fight.Party[0], _ => 99);

            Assert.IsFalse(outcome.Escaped);
            Assert.IsTrue((fight.Party[0].Flags & CombatantFlags.Ready) == 0);
        }

        [Test]
        public void ALockedEncounterRefusesEvenOnAPassingRoll() {
            // TRAPS.DAT's retreat lock, tested last and ANDed with the rest.
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight(escapeAllowed: false);

            CombatRuntime.RetreatOutcome outcome = runtime.ResolveRetreat(fight.Party[0], _ => 0);

            Assert.IsFalse(outcome.Escaped, "the roll passed; the encounter still refuses");
        }

        [Test]
        public void ADeadPARTYMemberBlocksTheAttemptAndChangesTheLine() {
            // Both halves in one assertion on purpose: the roll and the dialog tell one story, and
            // each alone reads fine with the two refusals swapped.
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight();
            fight.Party.Add(new Combatant { PartySlot = 2, Flags = CombatantFlags.Dead });

            CombatRuntime.RetreatOutcome outcome = runtime.ResolveRetreat(fight.Party[0], _ => 0);

            Assert.IsFalse(outcome.Escaped, "a passing roll cannot save a party that has lost someone");
            Assert.AreEqual(CombatCommands.RetreatRefusedMismatchDialog, outcome.DialogRecord);
        }

        [Test]
        public void ADeadSUMMONDoesNotBlockTheAttemptButDoesChangeTheLine() {
            // The two "someone is down" tests are DIFFERENT: the escape gate counts only real party
            // members (charSlot != 0), the refusal selector counts every slot on the party's side.
            // Collapsing them puts the wrong line on screen whenever a conjured ally has fallen.
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight();
            fight.Party.Add(new Combatant { PartySlot = 0, Flags = CombatantFlags.Dead });

            Assert.IsTrue(runtime.ResolveRetreat(fight.Party[0], _ => 0).Escaped,
                "a dead summon must not block the escape");

            (runtime, fight) = RetreatFight();
            fight.Party.Add(new Combatant { PartySlot = 0, Flags = CombatantFlags.Dead });
            Assert.AreEqual(CombatCommands.RetreatRefusedMismatchDialog,
                runtime.ResolveRetreat(fight.Party[0], _ => 99).DialogRecord,
                "but it does make the living count differ from the slot count");
        }

        [Test]
        public void AWholeStandingPartyThatSimplyMissedGetsTheOtherLine() {
            (CombatRuntime runtime, CombatEncounter fight) = RetreatFight();

            CombatRuntime.RetreatOutcome outcome = runtime.ResolveRetreat(fight.Party[0], _ => 99);

            Assert.AreEqual(CombatCommands.RetreatRefusedDialog, outcome.DialogRecord);
        }

        [Test]
        public void DefendingRecoversNothing() {
            // Unlike Rest, it costs the turn and gives nothing back except the to-hit penalty.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);

            int before = session.StatsOf(0)[(int)ActorAttribute.Health].Base
                + session.StatsOf(0)[(int)ActorAttribute.Stamina].Base;
            runtime.ResolveDefend(fight.Party[0]);
            int after = session.StatsOf(0)[(int)ActorAttribute.Health].Base
                + session.StatsOf(0)[(int)ActorAttribute.Stamina].Base;

            Assert.AreEqual(before, after, "defending must not heal");
        }

        [Test]
        public void TheRecoveryReachesTheSessionsLiveStats() {
            // The point of routing through the runtime: it must move the stats the rest of the game
            // reads, not a copy on the Combatant.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 30));
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);

            int before = session.StatsOf(0)[(int)ActorAttribute.Health].Base
                + session.StatsOf(0)[(int)ActorAttribute.Stamina].Base;
            runtime.ResolveRest(fight.Party[0]);
            int after = session.StatsOf(0)[(int)ActorAttribute.Health].Base
                + session.StatsOf(0)[(int)ActorAttribute.Stamina].Base;

            Assert.Greater(after, before, "the pool actually moved");
        }

        [Test]
        public void AMonsterRestsAndRecovers() {
            // It used to recover NOTHING, for want of a stat block rather than by any rule. The
            // original's gate opens `!actor->charSlot ||`, so a creature with no character slot is
            // never suppressed — only a party member can be.
            var session = new GameSession();
            var runtime = new CombatRuntime(session);
            session.SetRosterActorForTest(400, StatBlock(health: 20), gridX: 5, gridY: 5);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            Combatant monster = fight.Enemies[0];

            int healed = runtime.ResolveRest(monster);

            Assert.Greater(healed, 0);
            Assert.IsTrue((monster.Flags & CombatantFlags.DefendCommand) != 0, "the stance applies");
            Assert.IsTrue((monster.Flags & CombatantFlags.Ready) == 0, "and the turn is still spent");
        }

        [Test]
        public void WalkingOntoCrystalGroundHurtsTheWalkerAndStopsTheWalk() {
            // *** EVERY HAZARD A WALK FIRED USED TO BE DISCARDED. *** All three CombatWalk.Walk
            // sites passed the puzzle and the cover predicate but no onHazard, so crystal ground,
            // tile traps and cannons were computed and thrown away. TASK-241's audit could not see
            // it — CombatWalk has three production callers, so the type reads as consumed while its
            // hazard half is dead.
            //
            // Asserted through the DEATH rather than a health delta: crystal is a flat 100 against a
            // 40/20 pool, and CombatWalk reads the actor's death back out of the callback, so this
            // pins the applying and the cutting-short together. A monster that survived would have
            // walked on and swung, which is what the second assert is for.
            //
            // *** THE TERRAIN IS PAINTED DIRECTLY AND NOT BUILT FROM A PUZZLE, BECAUSE A CRYSTAL
            // THAT IS STILL STANDING BLOCKS ITS OWN TILE. *** That is the rule, not a fixture
            // quirk: GridCellIsBlocked @0x2d7b7 blocks a cell that holds a trap element, and a
            // crystal is one. The crystal GROUND (terrain 3) outlives the crystal object, so the
            // tile becomes walkable — and this damage arm reachable — only once the crystal is
            // pushed away or destroyed. Building the fixture from a puzzle is what this test was
            // written with first, and it failed for exactly that reason rather than for the wiring
            // it is about. Painting the terrain is the post-crystal state in one line.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int memberBefore = member.Health + member.Stamina;

            // Both tiles it could step to, so the test asserts the rule and not the step picker.
            runtime.Grid.SetTerrain(4, 4, CombatTerrain.Crystal);
            runtime.Grid.SetTerrain(4, 5, CombatTerrain.Crystal);

            runtime.ResolveEnemyTurn(monster, Melee(), _ => 0);

            Assert.IsTrue(monster.IsDead, "a flat 100 through a 40/20 pool");
            Assert.AreEqual(memberBefore, member.Health + member.Stamina,
                "and it never reached the party member to swing");
        }

        [Test]
        public void AWalkThatDoesNotArriveDropsTheTargetAndSkipsTheSwing() {
            // *** THE ORIGINAL DROPS A TARGET IT COULD NOT REACH. *** combataipath_select_target ends
            // with `if (target && not-at-dest && walk_path(actor, 0) == 0) target = 0;`
            // (CMBTAI.C:371-376), so a monster that cannot get beside its target stops aiming at it
            // and picks again next turn. Keeping it is what leaves one shuffling against an obstacle
            // for the rest of the fight.
            // *** ENOUGH SPEED TO SET OFF, NOT ENOUGH TO ARRIVE. *** The party member stands at
            // (3,4) and the enemy is placed four cells away with one point of movement, so the walk
            // runs and stops short — which is the case the rule is about.
            //
            // Two earlier shapes of this test passed with the rule REMOVED and mutation caught both:
            // a speed of zero and a pair of walled approach cells each made the resolver choose
            // nobody, so the target was already null and IsNull proved nothing. The monster has to
            // genuinely acquire a target and genuinely fail to reach it.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)7, (byte)7));
            Combatant member = fight.Party[0];
            Combatant monster = fight.Enemies[0];
            int memberBefore = member.Health + member.Stamina;
            monster.Speed = 1;

            runtime.ResolveEnemyTurn(monster, Melee(), AttacksOnContact());

            Assert.AreNotEqual((7, 7), (monster.X, monster.Y), "it did set off");
            Assert.IsNull(monster.Target, "and stops aiming at what it could not reach");
            Assert.AreEqual(memberBefore, member.Health + member.Stamina, "and does not swing");
        }

        [Test]
        public void ThePlayersMoveWalksToTheTileClearsTheTargetAndEndsTheTurn() {
            // *** THE PARTY COULD NOT MOVE AT ALL UNTIL THIS EXISTED. *** Every CombatWalk.Walk call
            // site was AI — melee approach, monster turn, flee step — so a player could attack,
            // shoot, cast and rest but never walk, and a trap-puzzle room could not be solved
            // because solving one IS walking into diamonds.
            //
            // The three assertions are the three rules of the original's move arm @0x6279f, which
            // are not guessable from the UI: it walks, it CLEARS THE TARGET, and it ends the turn.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            member.Target = monster;
            int startX = member.X;
            int startY = member.Y;

            bool moved = runtime.MoveToTile(member, startX + 2, startY, _ => 0);

            Assert.IsTrue(moved);
            Assert.AreNotEqual((startX, startY), (member.X, member.Y), "it walked");
            Assert.IsNull(member.Target, "walking away drops the target");
            Assert.IsTrue((member.Flags & CombatantFlags.Ready) == 0, "and the turn is spent");
            Assert.IsFalse(runtime.Grid.IsOccupied(startX, startY), "the tile it left is free");
            Assert.IsTrue(runtime.Grid.IsOccupied(member.X, member.Y), "the one it took is not");
        }

        [Test]
        public void AMoveToAnUnreachableTileCostsNothing() {
            // *** THIS ASSERTED THE OPPOSITE UNTIL 2026-09-09, ON A WRONG READING OF THE ORIGINAL. ***
            // The old comment said "the move arm clears mayAct with no 'did it get anywhere' test".
            // It has one, and it is the first thing the arm does: COMBAT.C:2324 gates the whole
            // block on `combatgrid_cursor_tile_movable()`, clears CAF_READY and the target only
            // INSIDE it, and the else branch merely returns to the idle state. A click on a tile
            // the actor cannot reach costs nothing at all.
            //
            // Measured in a live fight before the fix: a speed-4 member at (2,2) clicking (2,6) —
            // exactly four cells — reported success, spent the turn and stood still. Four of those
            // and the encounter could not be advanced by either side.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            member.Target = monster;

            bool moved = runtime.MoveToTile(member, member.X, member.Y, _ => 0);

            Assert.IsFalse(moved, "it reports that it went nowhere");
            Assert.IsTrue((member.Flags & CombatantFlags.Ready) != 0,
                "and the turn is still there to spend");
            Assert.AreSame(monster, member.Target, "a refused move does not drop the target either");
        }

        /// <summary>
        /// Just Flamecast, the spell a cannon casts.
        /// </summary>
        /// <remarks>
        /// Built rather than loaded because SPELLS.DAT is not on the path in an Editor test. The
        /// fields are the shipped row (generated/DAT/spells.json, id 4:
        /// <c>Flamecast,1,20,True,0,...,CostTimesDamage,3,0</c>). The damage test asserts only that
        /// the walker LOST pool, so a drift in those numbers changes how hard the cannon hits and
        /// does not quietly pass a wrong one.
        ///
        /// <para><b><c>AnimationEffectType</c> is load-bearing and was missing.</b> The shipped
        /// value is 3, the one type that flies a projectile, so leaving it at the default silently
        /// took the two projectile cues off every cannon test — which is how a cue-order assertion
        /// found it and a damage assertion could not.</para>
        /// </remarks>
        private static GameData.Resources.Spells.SpellList Flamecast() {
            var spell = new GameData.Resources.Spells.Spell("4") {
                Name = "Flamecast",
                MinimumCost = 1,
                MaximumCost = 20,
                TargetingType = 0,
                AnimationEffectType = 3,
                Calculation = SpellCalculation.CostTimesDamage,
                Damage = 3,
                Duration = 0,
            };
            var list = new GameData.Resources.Spells.SpellList("SPELLS.DAT");
            list.Spells[CannonLine.SpellId] = spell;
            return list;
        }

        [Test]
        public void ACannonWithLineOnTheTileShootsWhoeverStepsThere() {
            // *** A CANNON DEALS NO NUMBER — IT CASTS. *** Cast_Flamecast @0x2fb80 publishes the
            // cannon's cell and calls castCombatSpell(target, Flamecast, power = 20, actorId = -2),
            // which synthesises a caster and passes -power. That NEGATIVE sign is the whole
            // difference from a combatant's cast: it takes the cast off the to-hit path and
            // silences the cue and wind-up. Both rules were already modelled in GameData and both
            // were reached with a hard-coded false, because ResolveCast refused a negative power at
            // the door — so no caller could ever exercise them.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: 40), gridX: 5, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4))) {
                Spells = Flamecast(),
            };

            // North-facing cannons on both columns the monster can step into, so this asserts the
            // rule and not the step picker. Column 3 is deliberately left clear: the party member
            // stands there, and a living combatant blocks a cannon's line — which is what would
            // have made a row-wise fixture test the cover rule by accident instead.
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 },
                TrapPuzzleBuilder.Build(new[] { (-12, 4, 0), (-12, 5, 0) }));
            Combatant monster = fight.Enemies[0];
            int before = monster.Health + monster.Stamina;

            runtime.ResolveEnemyTurn(monster, Melee(), _ => 0);

            Assert.Less(monster.Health + monster.Stamina, before,
                "it stepped into a cannon's line and was shot");
        }

        [Test]
        public void ACannonIsHeardAtTheMuzzleAndAgainAtTheTileCast() {
            // The two cues cspell_apply_step_tile_spell's caller pair makes: audio_play(1) in
            // combatgrid_actor_step_to_tile, then audio_play(10) in the tile cast itself. The third
            // id 1 in the list is the projectile's own launch, from ResolveCast — Flamecast's
            // AnimationEffectType is 3, so it flies, and the original plays that cue too. Asserting
            // the whole ORDERED list rather than "contains 10" is what keeps that doubling honest:
            // a future de-duplication would have to change this expectation on purpose.
            var cues = new System.Collections.Generic.List<int>();
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: 40), gridX: 5, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)),
                playSfx: cues.Add) {
                Spells = Flamecast(),
            };

            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 },
                TrapPuzzleBuilder.Build(new[] { (-12, 4, 0), (-12, 5, 0) }));

            runtime.ResolveEnemyTurn(fight.Enemies[0], Melee(), _ => 0);

            CollectionAssert.AreEqual(
                new[] {
                    CannonLine.MuzzleCue, CannonLine.FireCue,
                    GameData.Resources.Combat.SpellProjectileSound.LaunchCue,
                    GameData.Resources.Combat.SpellProjectileSound.ImpactCue,
                },
                cues,
                "muzzle, tile cast, then the projectile's own launch and impact");
        }

        [Test]
        public void DespairThyEyesTakesTwentyOffTheTargetsAccuracies() {
            // CostTimesDuration used to RETURN after registering its lingering effect, so the
            // per-spell switch below it never ran and Despair's whole point, -20 on all three
            // accuracies, never happened (TASK-658). The original's calculation case breaks and
            // falls through (CSPELL.C:1344-1370). Measured there on a Shade (SAVE90): melee
            // accuracy 51 -> 31, max unchanged, crossbow and casting left at 0.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            ActorStat melee = runtime.StatsFor(monster)[(int)ActorAttribute.AccuracyMelee];
            melee.Max = 51;
            melee.Base = 51;
            int healthBefore = monster.Health;

            runtime.ResolveCast(member, monster, DespairThyEyes(),
                GameData.Resources.Spells.SpellIds.DespairThyEyes, 2, _ => 0);

            Assert.AreEqual(31, melee.Base, "twenty off the target's melee accuracy");
            Assert.AreEqual(51, melee.Max, "a debuff, not a lowered ceiling");
            Assert.AreEqual(healthBefore, monster.Health,
                "and no damage: a Cost x Duration magnitude is 0 (case 2)");
        }

        /// <summary>Despair Thy Eyes as shipped (generated/DAT/spells.csv, id 3).</summary>
        private static GameData.Resources.Spells.Spell DespairThyEyes() =>
            new GameData.Resources.Spells.Spell("3") {
                Name = "Despair Thy Eyes",
                MinimumCost = 2,
                MaximumCost = 2,
                TargetingType = 1,
                AnimationEffectType = 2,
                Calculation = SpellCalculation.CostTimesDuration,
                Damage = 20,
                Duration = 1,
            };

        /// <summary>Just Mirrorwall, the click-placed tile spell.</summary>
        /// <remarks>
        /// The shipped row (generated/DAT/spells.json, id 14): kind 5, EffectSubject 7, cost band
        /// 20-20, Duration 1. Both 5 and 7 matter — kind 5 is what puts the spell on the tile arm at
        /// all, and EffectSubject is the terrain it writes, so a fixture that got either wrong would
        /// test a spell that is not this one.
        /// </remarks>
        private static GameData.Resources.Spells.Spell Mirrorwall() =>
            new GameData.Resources.Spells.Spell("14") {
                Name = "Mirrorwall",
                MinimumCost = 20,
                MaximumCost = 20,
                TargetingType = 5,
                EffectSubject = 7,
                AnimationEffectType = 5,
                Calculation = SpellCalculation.NonCostRelated,
                Damage = 0,
                Duration = 1,
            };

        [Test]
        public void LimsKragmaOnAnADJACENTTargetCostsTheCasterNOTHING() {
            // CSPELL.C:1417-1423 sets pSpell = 0 within one cell, and the delivery switch below is
            // wrapped in `if (pSpell != 0)` — the same switch that bills the caster. So the refusal
            // has to be FREE, which is what separates this from Skyfire, where the cast runs and
            // only the magnitude is zero. Asserting the pool is the whole point: a version that
            // returned early AFTER the charge would pass a damage-only assertion.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.X = member.X + 1;
            monster.Y = member.Y;
            int before = member.Health + member.Stamina;

            bool cast = runtime.ResolveCast(member, monster, Mirrorwall(),
                GameData.Resources.Spells.SpellIds.TouchOfLimsKragma, 20, _ => 0);

            Assert.IsFalse(cast, "an adjacent Touch of Lims-Kragma does not happen");
            Assert.AreEqual(before, member.Health + member.Stamina,
                "and it costs the caster nothing — the charge is inside the skipped switch");
        }

        [Test]
        public void LimsKragmaTwoCellsAwayProceedsNormally() {
            // The other direction, which is what makes the test above discriminating: the same spell
            // at two cells is an ordinary cast. Without this, gating on the wrong comparison — or on
            // nothing at all — would still pass.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.X = member.X + 2;
            monster.Y = member.Y;
            int before = member.Health + member.Stamina;

            bool cast = runtime.ResolveCast(member, monster, Mirrorwall(),
                GameData.Resources.Spells.SpellIds.TouchOfLimsKragma, 20, _ => 0);

            Assert.IsTrue(cast, "at two cells the cast proceeds");
            Assert.AreNotEqual(before, member.Health + member.Stamina, "and the caster pays for it");
        }

        [Test]
        public void MirrorwallStampsTheCLICKEDCellWithItsOwnTerrainKind() {
            // The kind-5 arm of cspell_resolve_cast's wind-up switch: the cell you clicked, the
            // spell's EffectSubject as the terrain, and Duration * the invested cost as the timer.
            // Deliberately NOT the target's cell and NOT the Damage word — that is PaintTileEffect,
            // the other tile path, which reads different fields and stamps somewhere else.
            (CombatRuntime runtime, Combatant member, Combatant _) = MeleeFight();

            runtime.ResolveCast(member, null, Mirrorwall(), 14, 20, _ => 0, groundCell: (2, 2));

            Assert.AreEqual((CombatTerrain)7, runtime.Grid.TerrainAt(2, 2),
                "the clicked cell carries the spell's own tile kind");
        }

        [Test]
        public void ASecondMirrorwallOnTheSameCellIsRefusedSILENTLY() {
            // combatgrid_place_tile_fx_at_cur writes only if the cell holds no element AND its
            // terrain reads 0. A wall is not 0, so it cannot be stacked — and the refusal is not
            // reported: ResolveCast still returns true, because the placement lives in the wind-up
            // switch and the cast is paid for either way. Asserting the return value is the point;
            // a version that reported the refusal would pass a terrain-only assertion.
            (CombatRuntime runtime, Combatant member, Combatant _) = MeleeFight();
            runtime.ResolveCast(member, null, Mirrorwall(), 14, 20, _ => 0, groundCell: (2, 2));

            bool second = runtime.ResolveCast(member, null, Mirrorwall(), 14, 20, _ => 0,
                groundCell: (2, 2));

            Assert.IsTrue(second, "the cast is not reported as failed");
            Assert.AreEqual((CombatTerrain)7, runtime.Grid.TerrainAt(2, 2),
                "and the cell still holds the FIRST wall, unchanged");
        }

        [Test]
        public void AMirrorwallOnAnOCCUPIEDCellIsRefused() {
            // The other half of the same guard, and the one a terrain test cannot see: the cell is
            // bare floor and somebody is standing on it.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();

            runtime.ResolveCast(member, null, Mirrorwall(), 14, 20, _ => 0,
                groundCell: (monster.X, monster.Y));

            Assert.AreEqual(CombatTerrain.Open, runtime.Grid.TerrainAt(monster.X, monster.Y),
                "you cannot lay a wall on somebody's feet");
        }

        /// <summary>
        /// What every cast in these three tests is invested at, and therefore what the caster loses
        /// even when nothing reflects. Named because two of the assertions are ONLY meaningful
        /// against it — see the reflection test.
        /// </summary>
        private const int CastCost = 20;

        [Test]
        public void AMirrorwallOnTheFlightPathThrowsTheCastBackAtTheCaster() {
            // The delivery arm is an either/or: `if (pending) tile_spell(caster) else damage(target)`.
            // So the TARGET must come through untouched and the CASTER must lose pool — asserting
            // only the caster's loss would pass on a version that hit both.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Spells = Flamecast();
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(
                         member.X, member.Y, monster.X, monster.Y)) {
                if (x == monster.X && y == monster.Y) {
                    break;
                }
                runtime.Grid.SetTerrain(x, y, SpellReflection.Reflector);
                break;
            }
            int casterBefore = member.Health + member.Stamina;
            int targetBefore = monster.Health + monster.Stamina;

            runtime.ResolveCast(member, monster, runtime.Spells.Spells[CannonLine.SpellId],
                CannonLine.SpellId, 20, _ => 0);

            Assert.AreEqual(targetBefore, monster.Health + monster.Stamina,
                "the wall took the cast, so the target is untouched");
            // *** THE CAST'S COST IS NOT THE EVIDENCE. *** A caster pays 20 for this cast whether
            // or not anything reflects, so "the caster lost pool" passes on a build with no
            // Mirrorwall at all. The reflected damage is what must show ON TOP of it.
            Assert.Less(member.Health + member.Stamina, casterBefore - CastCost,
                "and the caster wore the spell as well as paying for it");
        }

        [Test]
        public void AMISSEDCastIsReflectedToo() {
            // The pending-tile test is in the DELIVERY switch, which a miss reaches — only the
            // damage inside the else is gated on the hit. Returning early on a miss made a
            // Mirrorwall useless against exactly the casts a player most wants it to stop, and how
            // often that happens is decided by the caster's accuracy rather than by the wall.
            //
            // roll 99 forces the miss: RangedHits compares the roll against a chance that is well
            // under 99 at this distance, where the reflection test above uses roll 0 to hit.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Spells = Flamecast();
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(
                         member.X, member.Y, monster.X, monster.Y)) {
                if (x == monster.X && y == monster.Y) {
                    break;
                }
                runtime.Grid.SetTerrain(x, y, SpellReflection.Reflector);
                break;
            }
            int casterBefore = member.Health + member.Stamina;
            int targetBefore = monster.Health + monster.Stamina;

            runtime.ResolveCast(member, monster, runtime.Spells.Spells[CannonLine.SpellId],
                CannonLine.SpellId, CastCost, _ => 99);

            Assert.AreEqual(targetBefore, monster.Health + monster.Stamina,
                "the miss reached no target either way");
            Assert.Less(member.Health + member.Stamina, casterBefore - CastCost,
                "and the wall threw the missed cast back regardless");
        }

        [Test]
        public void TWOMirrorwallsBehaveAsONE_theNEARESTReflects() {
            // *** THE REFLECTION CANNOT CHAIN, AND IT IS GEOMETRY. *** The wall that reflects is the
            // FIRST one crossed from the caster, so every cell between it and the caster has already
            // been tested and found not to be a reflector — the return path is provably clear, and a
            // second wall further along is never reached. Written as a test because the first draft
            // of this feature assumed a second wall would swallow the cast, and it does not.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Spells = Flamecast();
            var crossed = new System.Collections.Generic.List<(int X, int Y)>();
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(
                         member.X, member.Y, monster.X, monster.Y)) {
                if (x == monster.X && y == monster.Y) {
                    break;
                }
                crossed.Add((x, y));
            }
            Assert.GreaterOrEqual(crossed.Count, 2, "fixture needs two cells between the two");
            foreach ((int x, int y) in crossed) {
                runtime.Grid.SetTerrain(x, y, SpellReflection.Reflector);
            }
            int casterBefore = member.Health + member.Stamina;
            int targetBefore = monster.Health + monster.Stamina;

            runtime.ResolveCast(member, monster, runtime.Spells.Spells[CannonLine.SpellId],
                CannonLine.SpellId, CastCost, _ => 0);

            Assert.Less(member.Health + member.Stamina, casterBefore - CastCost,
                "the nearest wall threw it back, exactly as one wall would");
            Assert.AreEqual(targetBefore, monster.Health + monster.Stamina,
                "and the target was never reached");
        }

        [Test]
        public void AMirrorwallSWALLOWSStrengthDrainRatherThanReflectingIt() {
            // cspell_apply_step_tile_spell's ONE guard is `spell_id != 0x2a`, and it wraps the whole
            // body — so an intercepted Strength Drain reaches neither end. Both sides asserted,
            // because a version that simply skipped the interception would hit the target.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            var drain = new GameData.Resources.Spells.Spell("42") {
                Name = "Strength Drain",
                MinimumCost = 10,
                MaximumCost = 20,
                TargetingType = 0,
                AnimationEffectType = 3,
                Calculation = SpellCalculation.Special2,
                Damage = -1,
                Duration = 0,
            };
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(
                         member.X, member.Y, monster.X, monster.Y)) {
                if (x == monster.X && y == monster.Y) {
                    break;
                }
                runtime.Grid.SetTerrain(x, y, SpellReflection.Reflector);
                break;
            }
            int casterBefore = member.Health + member.Stamina;
            int targetBefore = monster.Health + monster.Stamina;

            runtime.ResolveCast(member, monster, drain,
                GameData.Resources.Spells.SpellIds.StrengthDrain, 20, _ => 0);

            Assert.AreEqual(casterBefore - CastCost, member.Health + member.Stamina,
                "the caster paid for the cast and took nothing back");
            Assert.AreEqual(targetBefore, monster.Health + monster.Stamina,
                "and the target is untouched too — the wall swallowed it");
        }

        [Test]
        public void ASpellThatFLIESNOTHINGPassesAMirrorwallUnharmed() {
            // The arming is a SPRITE over the cell, so a spell whose animation is a palette flash
            // has nothing to catch. Same fixture as the reflection test with one field changed,
            // which is the only difference the rule turns on.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            GameData.Resources.Spells.SpellList list = Flamecast();
            list.Spells[CannonLine.SpellId].AnimationEffectType = 4;
            foreach ((int x, int y) in CombatLineOfFire.CellsCrossed(
                         member.X, member.Y, monster.X, monster.Y)) {
                if (x == monster.X && y == monster.Y) {
                    break;
                }
                runtime.Grid.SetTerrain(x, y, SpellReflection.Reflector);
                break;
            }
            int casterBefore = member.Health + member.Stamina;
            int targetBefore = monster.Health + monster.Stamina;

            runtime.ResolveCast(member, monster, list.Spells[CannonLine.SpellId],
                CannonLine.SpellId, 20, _ => 0);

            // Flamecast splashes everyone near its target, the caster included (COMBAT.C:417), so the
            // caster's loss is the cost plus that splash; what this test is about is that NOTHING
            // came back off the wall on top of it.
            int hit = targetBefore - (monster.Health + monster.Stamina);
            int splash = System.Math.Max(0, GameData.Resources.Spells.SpellCastRoutines.FlamecastSplashDamage(
                hit, CombatGrid.ChebyshevDistance(member.X, member.Y, monster.X, monster.Y)));
            Assert.AreEqual(casterBefore - CastCost - splash, member.Health + member.Stamina,
                "the caster paid for the cast and its own splash, and nothing came back");
            Assert.Less(monster.Health + monster.Stamina, targetBefore, "and the target was hit");
        }

        [Test]
        public void ASwingCostsTheATTACKERAPointBeforeItEvenRolls() {
            // resolveSwingAttack bills the attacker one point and only then tries to hit, so a fight
            // drains the swinger as well as the swung-at. Asserted on a MISS so the cost cannot be
            // confused with anything the defender did back: roll 99 misses at this distance.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = member.Health + member.Stamina;

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 99);

            Assert.AreEqual(before - MeleeExchange.SwingCost, member.Health + member.Stamina,
                "the swinger paid for the swing whether or not it landed");
        }

        [Test]
        public void ASwingIsFREEForAnAttackerDownToItsLastPoint() {
            // The gate is stat_actor_get(attacker, 0x10, 4) > 1 — the pool, which for index 0x10 is
            // simply health + stamina. It is what stops the cost finishing off a combatant who has
            // one point left, so the boundary is asserted rather than the comfortable case.
            Assert.IsFalse(MeleeExchange.SwingIsBilled(1), "a pool of exactly 1 swings for free");
            Assert.IsTrue(MeleeExchange.SwingIsBilled(2), "a pool of 2 is billed");
        }

        /// <summary>A party member and one monster, both with real stat blocks.</summary>
        private static (CombatRuntime Runtime, Combatant Member, Combatant Monster) MeleeFight(
            byte memberHealth = 40, byte monsterHealth = 40) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: memberHealth));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: monsterHealth), gridX: 5, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, fight.Party[0], fight.Enemies[0]);
        }

        // *** A WOUNDED FIGHTER IS A WORSE FIGHTER. *** Every combat read in the original is
        // stat_actor_get(actor, stat, 0), which scales by health through g_abStatRatio — 1, fully
        // weighted, for Strength, Defense, Speed and all three accuracies. Measured in the running
        // original with Locklear's health at 55 of 100 and then at 10: melee accuracy 58 -> 11,
        // strength 17 -> 4. The port read ActorStat.Base and none of that reached a swing
        // (TASK-478). The fixture's member has AccuracyMelee 1, so the drop shows as the preview
        // falling to the floor the stat table allows.
        [Test]
        public void AWoundedAttackerReadsLowerThanAHealthyOne() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight(memberHealth: 90);
            // The shared block leaves every skill at 1, which is its own floor once scaled — give
            // this attacker room to lose something.
            ActorStat[] stats = runtime.StatsFor(member);
            stats[(int)ActorAttribute.AccuracyMelee].Base = 60;
            stats[(int)ActorAttribute.Strength].Base = 40;

            member.Health = 90;
            CombatRuntime.MeleePreview healthy = runtime.MeleePreviewFor(member, monster);

            member.Health = 1;
            CombatRuntime.MeleePreview hurt = runtime.MeleePreviewFor(member, monster);

            Assert.Less(hurt.SwingAccuracy, healthy.SwingAccuracy,
                "the swing gets less accurate as the attacker is hurt");
            Assert.Less(hurt.SwingDamage, healthy.SwingDamage,
                "and weaker — strength is health-scaled too");
        }

        // The picker re-reads speed rather than using the one the fighter walked in with:
        // combatenc_pick_next compares `stat_actor_get(cand, 2, 0)` for every candidate and takes
        // the winner's as the acting budget. Measured in the running original, Locklear's speed is
        // 4 at health 55 of 100 and 1 at health 10.
        [Test]
        public void SpeedIsReReadAtPickTimeSoAWoundedFighterSlowsDown() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight(memberHealth: 90);
            runtime.StatsFor(member)[(int)ActorAttribute.Speed].Base = 40;
            member.Health = 90;
            runtime.AdvanceToPartyTurn();
            int healthy = member.Speed;

            member.Health = 1;
            runtime.AdvanceToPartyTurn();

            Assert.Less(member.Speed, healthy, "a wounded fighter acts slower");
            Assert.Greater(healthy, 1, "and the healthy reading was not already at the floor");
        }

        [Test]
        public void AnEnemyKeepsItsStatBlockSoItCanBeFoughtAtAll() {
            // EnterRoster used to read the roster stats for three numbers and drop them. Everything a
            // swing needs beyond health lives in that block and there is no way back to it: ClassId
            // means the CREATURE TYPE for an enemy, not the table row it came from.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();

            Assert.IsNotNull(runtime.StatsFor(monster), "the enemy's block");
            Assert.IsNotNull(runtime.StatsFor(member), "and the party member's");
        }

        [Test]
        public void ASwingThatLandsComesOffTheDefendersPoolStaminaFirst() {
            // Nothing in the game could take damage before this: MeleeExchange, CombatFormulas and
            // the advancement rules were all ported with no caller.
            //
            // *** DAMAGE IS TAKEN FROM STAMINA BEFORE HEALTH. *** Asserting against Health alone
            // reads as "the swing did nothing" for every hit a character has the stamina to absorb,
            // which is most of them — the mistake this test was written with the first time.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = monster.Health + monster.Stamina;

            MeleeExchange.Result result = runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(result.Hit, "roll 0 against any positive chance");
            Assert.Greater(result.Damage, 0);
            Assert.AreEqual(before - result.Damage, monster.Health + monster.Stamina);
            Assert.Less(monster.Stamina, 20, "and stamina is where it landed");
        }

        [Test]
        public void AMissTakesNothingOff() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = monster.Health + monster.Stamina;

            MeleeExchange.Result result = runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 99);

            Assert.IsFalse(result.Hit);
            Assert.AreEqual(before, monster.Health + monster.Stamina);
        }

        [Test]
        public void AMonsterCanSwingBackAndTheDamageReachesTheSAVEsStats() {
            // Combatant.Health/Stamina are copies. Left unwritten, a party member's wounds are undone
            // the moment Leave() writes the combatant back over the live stats.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            ActorStat[] stats = runtime.StatsFor(member);
            ActorStat health = stats[(int)ActorAttribute.Health];
            ActorStat stamina = stats[(int)ActorAttribute.Stamina];
            int before = health.Base + stamina.Base;

            MeleeExchange.Result result = runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(result.Hit);
            Assert.AreEqual(before - result.Damage, health.Base + stamina.Base,
                "the save's stats, not just the copy");
        }

        [Test]
        public void OnlyThePARTYSideTrainsFromASwing() {
            // A monster's stat objects are the shared roster TEMPLATE, so training them would improve
            // every creature of that type for the rest of the game.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            ActorStat monsterMelee = runtime.StatsFor(monster)[(int)ActorAttribute.AccuracyMelee];
            int before = monsterMelee.Experience;

            runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0);

            Assert.AreEqual(before, monsterMelee.Experience, "the monster learnt nothing");
        }

        [Test]
        public void ALethalSwingActuallyKILLSRatherThanJustEmptyingThePool() {
            // *** MeleeExchange REPORTS DefenderDown and deliberately does not act on it. *** Left
            // unacted, the defender sits at 0 health with IsDead false — so the fight never reaches
            // IsOver(), no corpse is ever owed, and the body keeps blocking its tile. Asserting
            // "health == 0" would pass in exactly that broken state, which is why this asserts the
            // FLAG and the TILE instead.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight(monsterHealth: 1);
            monster.Stamina = 0;
            int x = monster.X;
            int y = monster.Y;

            MeleeExchange.Result result = runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(result.DefenderDown, "the swing was lethal");
            Assert.IsTrue(monster.IsDead, "and it is flagged dead, not merely emptied");
            Assert.IsFalse(runtime.Grid.IsOccupied(x, y), "the body stops blocking its tile");
            Assert.IsNotNull(runtime.LastDeath, "and what it left behind is recorded");
        }

        [Test]
        public void KillingTheLastEnemyENDSTheFight() {
            // The consequence that makes the flag load-bearing: IsOver() reads IsDead, so without
            // the kill a won fight would run forever with a 0-health enemy still standing.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight(monsterHealth: 1);
            monster.Stamina = 0;

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(runtime.Encounter.IsOver());
        }

        [Test]
        public void SwingingAtACorpseIsAMissRatherThanAnError() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.Flags |= CombatantFlags.Dead;

            Assert.IsFalse(runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0).Hit);
            Assert.IsFalse(runtime.ResolveMelee(member, null, MeleeAttack.Swing, _ => 0).Hit);
        }

        /// <summary>An AI that always melees whoever is nearest.</summary>
        /// <remarks>
        /// <b>No flee thresholds on purpose.</b> These tests are about carrying a decision out, not
        /// about morale, and the profile's threshold of 0 never routs anyway — so an absent table
        /// says "morale is not what is being asserted" rather than copying the shipped numbers into
        /// a second file where they could drift.
        /// </remarks>
        private static MonsterTurnResolver Melee() => new MonsterTurnResolver(
            _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: false, canShoot: false),
            n => 0, fleeThresholds: null, isUnderground: false);

        /// <summary>
        /// A roll stub that answers the CONTACT roll with "attack" and everything else with 0.
        /// </summary>
        /// <remarks>
        /// <b>Only the first hundred-sided roll is redirected.</b> SwingIfAdjacent asks twice: first
        /// combataipath_followup_action's parry check (CMBTAI.C:381), then
        /// combatenc_ai_attempt_melee's swing-or-thrust pick. Answering both with a high number
        /// avoids the parry and then makes the attack miss, which fails the same assertion for the
        /// opposite reason — so the stub counts.
        /// </remarks>
        private static System.Func<int, int> AttacksOnContact() {
            var asked = false;
            return bound => {
                if (bound == 100 && !asked) {
                    asked = true;
                    return GameData.Resources.Combat.CombatAi.ParryRollBound + 1;
                }
                return 0;
            };
        }

        [Test]
        public void AMonsterClosesTheDistanceAndSwings() {
            // The seam AdvanceToPartyTurn has always taken and nobody ever passed: every enemy
            // forfeited its turn, so a fight could not be lost.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = member.Health + member.Stamina;

            // *** THE CONTACT ROLL HAS TO SAY "ATTACK". *** combataipath_followup_action parries on
            // RND(100) <= 25 (CMBTAI.C:381-388), so a stub that answers every roll with 0 makes the
            // monster raise its guard and this test asserts a swing that the game never promised.
            // The rule is real; the fixture has to pick the arm it is about.
            runtime.ResolveEnemyTurn(monster, Melee(), AttacksOnContact());

            Assert.AreEqual(1,
                CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y),
                "it walks up beside the target, not onto it");
            Assert.Less(member.Health + member.Stamina, before, "and then it swings");
        }

        [Test]
        public void MovingKeepsTheGridsOccupancyHonest() {
            // Neither CombatWalk nor CombatMovement touches occupancy. Left to them, every tile
            // anyone has stood on keeps a phantom blocker and the fight seizes up.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int startX = monster.X;
            int startY = monster.Y;

            runtime.ResolveEnemyTurn(monster, Melee(), _ => 0);

            Assert.AreNotEqual((startX, startY), (monster.X, monster.Y), "it did move");
            Assert.IsFalse(runtime.Grid.IsOccupied(startX, startY), "the tile it left is free");
            Assert.IsTrue(runtime.Grid.IsOccupied(monster.X, monster.Y), "the one it took is not");
        }

        [Test]
        public void ADecisionThatIsNotMeleeIsNotActedOn() {
            // A Cast is not carried out by the runtime: HotspotService.RunEnemyTurn delivers it,
            // because the spell catalogue lives there. Flee and Shoot used to be on this list and no
            // longer are — see the rout tests below and CombatRangedAndCastTests.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            var caster = new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                    spellcastPattern: 2),
                n => 0, fleeThresholds: null, isUnderground: false);
            int before = member.Health + member.Stamina;
            int startX = monster.X;

            MonsterTurnResolver.Decision decision = runtime.ResolveEnemyTurn(monster, caster, _ => 0);

            Assert.AreEqual(AiAction.Cast, decision.Action);
            Assert.AreEqual(before, member.Health + member.Stamina, "nobody was hurt");
            Assert.AreEqual(startX, monster.X, "and it did not walk either");
        }

        /// <summary>The same fight with the monster standing in CONTACT with the party member.</summary>
        private static (CombatRuntime Runtime, Combatant Member, Combatant Monster)
            EngagedFight() {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: 40), gridX: 4, gridY: 4);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, fight.Party[0], fight.Enemies[0]);
        }

        /// <summary>A caster whose spellcast pattern actually yields a cast.</summary>
        private static MonsterTurnResolver EngagedCaster() => new MonsterTurnResolver(
            _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                spellcastPattern: 1, castingSkill: 50),
            n => 50, fleeThresholds: null, isUnderground: false);

        [Test]
        public void AnEngagedCasterActuallyWalksAwayRatherThanDecidingTo() {
            // *** THE HALF THAT MAKES THE GATE WORTH HAVING. *** Suppressing the cast on its own
            // would leave the caster standing there doing nothing, which is not what the original
            // does — combataiturn_pick_tile_or_attack spends the whole turn repositioning. A
            // Retreat that nothing carries out is the same "decided and not acted on" shape the
            // rout used to have.
            (CombatRuntime runtime, Combatant member, Combatant monster) = EngagedFight();
            Assert.AreEqual(1,
                CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y),
                "fixture: the caster starts in contact");
            int before = CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y);

            // A roll of 90 clears the 15% abandon and accepts every tie the cell search offers.
            MonsterTurnResolver.Decision decision =
                runtime.ResolveEnemyTurn(monster, EngagedCaster(), _ => 90);

            Assert.AreEqual(AiAction.Retreat, decision.Action);
            Assert.Greater(CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y),
                before, "it put ground between itself and the party");
            Assert.IsTrue(runtime.Grid.IsOccupied(monster.X, monster.Y),
                "and the grid followed it, as MoveTo exists to ensure");
        }

        [Test]
        public void NoResolverMeansTheTurnIsSkippedRatherThanGuessed() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = member.Health + member.Stamina;

            runtime.ResolveEnemyTurn(monster, null, _ => 0);

            Assert.AreEqual(before, member.Health + member.Stamina);
        }

        /// <summary>The shipped flee-threshold table — <c>CombatAffinityTables.AiFleeThresholds</c>.</summary>
        private static readonly int[] FleeThresholds = { 85, 55, 45, 35, 25, 20, 10, 5, 5, 0 };

        /// <summary>
        /// An AI whose monster routs on its first turn.
        /// </summary>
        /// <remarks>
        /// Morale 8 with stamina at 10% lands on index 0 — the 85%-to-rout end of the table — so a
        /// roll of 0 always routs. Morale is deliberately NOT 0 here: 0 never routs at all, which is
        /// the guard <see cref="MonsterFleeDestination.WontMoveMorale"/> documents.
        /// </remarks>
        private static MonsterTurnResolver Router() => new MonsterTurnResolver(
            _ => new MonsterTurnResolver.Profile(8, 10, canCastSpells: false, canShoot: false),
            n => 0, FleeThresholds, isUnderground: false);

        /// <summary>A roll that accepts every improvement the destination scan considers.</summary>
        private static int AcceptingRoll(int _) => 90;

        [Test]
        public void ARoutedMonsterWalksInsteadOfStandingStill() {
            // *** THE BUG THIS FIXES. *** The resolver returned Flee with no destination and
            // ResolveEnemyTurn carried out only MeleeOrMove, so a routed monster decided to run and
            // then stood on its tile forfeiting every remaining turn — it could never arrive, so it
            // could never leave the fight either.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));
            Combatant monster = fight.Enemies[0];
            int startY = monster.Y;

            MonsterTurnResolver.Decision decision =
                runtime.ResolveEnemyTurn(monster, Router(), AcceptingRoll);

            Assert.AreEqual(AiAction.Flee, decision.Action);
            Assert.IsNotNull(monster.FleeDestination, "it picked somewhere to run to");
            Assert.Greater(monster.Y, startY, "and moved toward it — the scan maximises Y");
        }

        [Test]
        public void AFleeingMonsterDoesNotAlsoAttack() {
            // monster_combatTurn jumps past the whole species switch and capability cascade once
            // CAF_FLEE is set, so a routing creature adjacent to someone does not get a free swing
            // on its way out.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));
            Combatant member = fight.Party[0];
            Combatant monster = fight.Enemies[0];
            int before = member.Health + member.Stamina;

            runtime.ResolveEnemyTurn(monster, Router(), AcceptingRoll);

            Assert.AreEqual(before, member.Health + member.Stamina, "nobody was hurt");
            Assert.IsNull(monster.Target, "and it dropped whoever it was fighting");
        }

        [Test]
        public void TheFleeDestinationIsChosenOnceAndThenKept() {
            // The scan is deliberately noisy (RND(100) > 50 per improvement), so re-rolling it every
            // turn would send the monster wandering between destinations instead of leaving. The
            // original picks once, at rout time, and stores it on the actor.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));
            Combatant monster = fight.Enemies[0];

            runtime.ResolveEnemyTurn(monster, Router(), AcceptingRoll);
            (int X, int Y)? first = monster.FleeDestination;
            // A roll that would refuse every improvement, so a re-scan could not reproduce it.
            runtime.ResolveEnemyTurn(monster, Router(), _ => 0);

            Assert.IsNotNull(first);
            Assert.AreEqual(first, monster.FleeDestination, "the same tile, not a fresh scan");
        }

        [Test]
        public void ASpeedZeroMonsterStillEdgesTowardTheExit() {
            // combatenc_flee_walk_and_exit_field tops the budget up to one step before walking:
            //   if (combat_moveStepsRemaining == 0) combat_moveStepsRemaining = 1;
            // Without it a creature with no Speed is trapped in the fight forever.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));
            Combatant monster = fight.Enemies[0];
            monster.Speed = 0;
            int startY = monster.Y;

            runtime.ResolveEnemyTurn(monster, Router(), AcceptingRoll);

            Assert.Greater(monster.Y, startY, "one step is still a step");
        }

        [Test]
        public void ReachingTheDestinationTakesTheMonsterOffTheFieldWithoutACorpse() {
            // handleActorDeath(actor, 0) — the zero is the corpse/no-corpse switch, and the damage
            // path passes 1. A routed monster that got home leaves nothing to loot, and the removal
            // persists so a revisit does not find it standing.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)4, (byte)4));
            Combatant monster = fight.Enemies[0];
            monster.Speed = 2;

            // Enough turns for a two-step walk to cross the grid, whatever tile it picked.
            for (var turn = 0; turn < 20 && !monster.IsDead; turn++) {
                runtime.ResolveEnemyTurn(monster, Router(), AcceptingRoll);
            }

            Assert.IsTrue(monster.IsDead, "it left the field");
            Assert.AreEqual(monster.FleeDestination.Value.X, monster.X);
            Assert.AreEqual(monster.FleeDestination.Value.Y, monster.Y);
            Assert.AreEqual(DeathOutcome.RemovedFromField, runtime.LastDeath);
            Assert.IsFalse(runtime.LeavesCorpse(monster), "and left nothing to loot");
            Assert.IsFalse(runtime.Grid.IsOccupied(monster.X, monster.Y), "its tile is free again");
        }

        [Test]
        public void ASwingIsREFUSEDOnADiagonalWhereAThrustWalksIn() {
            // The two buttons are two different attacks with different reach: implementing both as
            // "attack if adjacent" removes the game's only click-to-engage, and implementing both
            // as "approach then attack" lets a swing move the attacker, which it never does.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Grid.SetOccupied(member.X, member.Y, false);
            member.X = monster.X - 1;
            member.Y = monster.Y - 1;   // pure diagonal
            runtime.Grid.SetOccupied(member.X, member.Y, true);
            member.Speed = 6;

            Assert.AreEqual(CombatRuntime.MeleeClick.Refused,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Swing, _ => 0),
                "a diagonal is not orthogonally adjacent, and the swing never moves anybody");

            Assert.AreEqual(CombatRuntime.MeleeClick.Struck,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Thrust, _ => 0));
            Assert.IsTrue(
                GameData.Resources.Combat.CombatGrid.OrthogonallyAdjacent(
                    member.X, member.Y, monster.X, monster.Y),
                "the thrust walked into contact first");
        }

        [Test]
        public void AnEnemyBeyondTheMovementAllowanceCannotBeStruckAtAll() {
            // Reach and movement are one budget. The refusal must not spend the turn, or a misclick
            // on a far enemy would silently pass the character.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Grid.SetOccupied(member.X, member.Y, false);
            member.X = 0;
            member.Y = 0;
            runtime.Grid.SetOccupied(member.X, member.Y, true);
            member.Speed = 1;

            Assert.AreEqual(CombatRuntime.MeleeClick.Refused,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Thrust, _ => 0));
            Assert.AreEqual(0, member.X, "and it did not walk");
        }

        [Test]
        public void AnExhaustedCharacterCanStillThrustButNotSwing() {
            // The heavier attack is the one that runs out first: the swing needs a combined pool
            // above 1 and the thrust has no such test.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Grid.SetOccupied(member.X, member.Y, false);
            member.X = monster.X - 1;
            member.Y = monster.Y;   // orthogonally adjacent, so the swing's other gate passes
            runtime.Grid.SetOccupied(member.X, member.Y, true);
            member.Speed = 6;
            member.Health = 1;
            member.Stamina = 0;

            Assert.AreEqual(CombatRuntime.MeleeClick.Refused,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Swing, _ => 0));
            Assert.AreEqual(CombatRuntime.MeleeClick.Struck,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Thrust, _ => 0));
        }

        [Test]
        public void AnAdjacentSwingStrikesWithoutMoving() {
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            runtime.Grid.SetOccupied(member.X, member.Y, false);
            member.X = monster.X - 1;
            member.Y = monster.Y;
            runtime.Grid.SetOccupied(member.X, member.Y, true);
            member.Speed = 6;
            int before = monster.Health + monster.Stamina;

            Assert.AreEqual(CombatRuntime.MeleeClick.Struck,
                runtime.ResolveMeleeClick(member, monster, MeleeAttack.Swing, _ => 0));
            Assert.AreEqual(monster.X - 1, member.X, "the swing moved nobody");
            Assert.Less(monster.Health + monster.Stamina, before);
        }

        private const int SwordObjectId = 3;

        /// <summary>A party member wielding a sword with the given damage fields.</summary>
        /// <remarks>
        /// Built on <see cref="GiveArcherPack"/>'s shape rather than a second mechanism: the pack has
        /// to exist on the session BEFORE the runtime reads it, so arming cannot be done after the
        /// fight starts.
        /// </remarks>
        private static (CombatRuntime Runtime, Combatant Member, Combatant Monster) SwordFight(
            int swingAccuracy = 0, int thrustDamage = 0, int swingDamage = 0,
            ushort swordFlags = 0x40, int thrustAccuracy = 0) {
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 40));
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetRosterActorForTest(400, StatBlock(health: 40), gridX: 5, gridY: 5);

            var pack = new GameData.Resources.Data.SaveGameContainerData(
                new GameData.Resources.Data.SaveGameContainerLocationData(
                    zone: 1, minChapter: 1, maxChapter: 9, worldItemId: 0, x: 0, y: 0, actorNumber: 1),
                GameData.Resources.Data.SaveGameContainerType.Inventory,
                numberOfItems: 1, capacity: 8, dataTypes: 0,
                items: new[] {
                    new GameData.Resources.Data.SaveGameInventoryItemData(SwordObjectId, 100, swordFlags),
                },
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);
            session.SetZoneContainersForTest(
                new GameData.Resources.Data.SaveGameZoneContainerStateData(new[] {
                    new GameData.Resources.Data.SaveGameZoneContainerEntryData(1, 0, new[] { pack }),
                }), chapter: 1);

            var objects = new GameData.Resources.Object.ObjectInfoSet("OBJINFO.DAT",
                new System.Collections.Generic.List<GameData.Resources.Object.ObjectInfo> {
                    new GameData.Resources.Object.ObjectInfo("sword") {
                        Number = SwordObjectId,
                        ObjectType = GameData.ObjectType.Sword,
                        SwingAccuracy_ArmorMod_BowAccuracy = swingAccuracy,
                        ThrustAccuracy = thrustAccuracy,
                        ThrustBaseDamage = thrustDamage,
                        SwingBaseDamage = swingDamage,
                    },
                });

            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)), objects);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, fight.Party[0], fight.Enemies[0]);
        }

        private static int SwingDamageOf(CombatRuntime runtime, Combatant attacker,
            Combatant defender) => MeleeDamageOf(runtime, attacker, defender, MeleeAttack.Swing);

        private static int MeleeDamageOf(CombatRuntime runtime, Combatant attacker,
            Combatant defender, MeleeAttack attack) {
            int before = defender.Health + defender.Stamina;
            runtime.ResolveMelee(attacker, defender, attack, _ => 0);
            return before - (defender.Health + defender.Stamina);
        }

        [Test]
        public void AnArmedPartyMemberHitsHarderThanABareHandedOne() {
            // Equipment was not read at all before this: everyone swung bare-handed, which shows up
            // as too little damage rather than as a visible fault.
            (CombatRuntime bare, Combatant bareMember, Combatant bareMonster) = MeleeFight();
            int bareDamage = SwingDamageOf(bare, bareMember, bareMonster);

            (CombatRuntime armed, Combatant member, Combatant monster) =
                SwordFight(swingAccuracy: 30, swingDamage: 12);

            Assert.Greater(SwingDamageOf(armed, member, monster), bareDamage);
        }

        [Test]
        public void EACHAttackReadsItsOwnDamageField_TheyAreNotCrossed() {
            // *** THIS TEST USED TO ASSERT THE OPPOSITE, AND IT PASSED. *** It was called
            // DamageComesFromTHRUSTNotSwing and it pinned a reading assembled from two different
            // routines: the to-hit from combat_arena_melee_attack (the SWING) and the damage from
            // combat_arena_resolve_melee_swing (which passes attack_type = 1, so the THRUST). Read
            // either body whole and it pairs like its name. What is actually swapped is canassa's
            // two FUNCTION names -- see CombatActionDispatch.AccuracyOf.
            //
            // Each weapon here has one large field and one small one, so a crossed reading would
            // invert both assertions rather than merely weaken them.
            (CombatRuntime swingy, Combatant swingyMember, Combatant swingyMonster) =
                SwordFight(swingAccuracy: 30, thrustAccuracy: 30, swingDamage: 40, thrustDamage: 4);

            Assert.Greater(
                MeleeDamageOf(swingy, swingyMember, swingyMonster, MeleeAttack.Swing),
                MeleeDamageOf(swingy, swingyMember, swingyMonster, MeleeAttack.Thrust),
                "a swing does the weapon's SWING damage");

            (CombatRuntime thrusty, Combatant thrustyMember, Combatant thrustyMonster) =
                SwordFight(swingAccuracy: 30, thrustAccuracy: 30, swingDamage: 4, thrustDamage: 40);

            Assert.Greater(
                MeleeDamageOf(thrusty, thrustyMember, thrustyMonster, MeleeAttack.Thrust),
                MeleeDamageOf(thrusty, thrustyMember, thrustyMonster, MeleeAttack.Swing),
                "a thrust does the weapon's THRUST damage");
        }

        [Test]
        public void EachAttackRollsAgainstItsOwnAccuracyField() {
            // A weapon accurate on one attack and hopeless on the other: the accurate attack lands
            // at a roll the other misses. Crossing the fields swaps which one connects.
            (CombatRuntime runtime, Combatant member, Combatant monster) =
                SwordFight(swingAccuracy: 90, thrustAccuracy: 0, swingDamage: 10, thrustDamage: 10);

            Assert.IsTrue(runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 50).Hit,
                "the swing rolls the weapon's swing accuracy");
            Assert.IsFalse(runtime.ResolveMelee(member, monster, MeleeAttack.Thrust, _ => 50).Hit,
                "the thrust rolls the weapon's thrust accuracy, which this weapon does not have");
        }

        [Test]
        public void ASwordInThePackButNotInHandIsNotWielded() {
            // Equipped is 0x40; without it the weapon is carried, not wielded.
            (CombatRuntime armed, Combatant member, Combatant monster) =
                SwordFight(swingAccuracy: 30, thrustDamage: 12, swordFlags: 0);
            (CombatRuntime bare, Combatant bareMember, Combatant bareMonster) = MeleeFight();

            Assert.AreEqual(SwingDamageOf(bare, bareMember, bareMonster),
                SwingDamageOf(armed, member, monster));
        }

        [Test]
        public void AMonsterIsAlwaysBareHandedBecauseItsWeaponIsNotAnItem() {
            // Its damage comes from the creature's own stats; there is no pack to read.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();

            Assert.DoesNotThrow(() => runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0));
        }

        [Test]
        public void ParryLastsExactlyUntilYourOwnNextTurn() {
            // *** The loop between two halves that were wired separately. *** Defend sets Parry;
            // CombatEncounter.PickNext clears it on whoever it picks. Neither end asserted the
            // other, so "a guard lasts one round" was implemented twice and demonstrated nowhere —
            // and a guard that never expired would look like a very tough character rather than a
            // bug.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();

            runtime.ResolveDefend(member);
            Assert.IsTrue((member.Flags & CombatantFlags.Parry) != 0, "the guard is up");

            // roll 99: the enemy swings and misses, so the member survives to be picked again.
            Combatant next = runtime.AdvanceToPartyTurn(
                m => runtime.ResolveEnemyTurn(m, Melee(), _ => 99));

            Assert.AreSame(member, next, "the same character comes back round");
            Assert.IsTrue((member.Flags & CombatantFlags.Parry) == 0,
                "and the guard is spent — cleared by the picker, not by the round boundary");
        }

        private static GameData.Resources.Data.SaveGameCombatData Record(int creatureType) =>
            new GameData.Resources.Data.SaveGameCombatData(
                targetActorPointer: 0x4321, creatureType: (short)creatureType,
                xOnGrid: 0, yOnGrid: 0, targetXOnGrid: 0, targetYOnGrid: 0,
                combatStatus: 1, animEffectType: 0, activeSpellEffectSlot: 0, unusedPadding: 0,
                animDurationTimer: 0, monsterSpellAbility: 3, meleeAttackType: 4,
                rangedAttackType: 5, movementAiType: 6, preferredArrowType: -1,
                lastSpellSymbolFile: 0, floatingDamageValue: 0, floatingDamageTimer: -1);

        [Test]
        public void ASaveCollectsOneEditPerENEMYAtItsRosterSlot() {
            // The slot is the whole point: a combat record is keyed by the ACTOR-table index the
            // enemy came from, and Combatant does not carry it. Losing it would write the fight onto
            // whichever actor happens to sit at index 0.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.X = 6;
            monster.Y = 7;

            System.Collections.Generic.IReadOnlyList<GameData.Resources.Data.DirtyCombatantEdit> edits =
                runtime.CollectDirtyCombatantEdits(slot => Record(creatureType: 9));

            Assert.AreEqual(1, edits.Count, "one enemy, one edit — the party is not in this block");
            Assert.AreEqual(400, edits[0].ActorSlot, "the roster slot it entered with");
            Assert.AreEqual(6, edits[0].Record.XOnGrid);
            Assert.AreEqual(7, edits[0].Record.YOnGrid);
            Assert.AreEqual(4, edits[0].Record.MeleeAttackType,
                "and everything the fight does not own is carried through");
        }

        [Test]
        public void ASlotWithNoExistingRecordIsSkippedRatherThanInvented() {
            // Most of the 22 bytes come from the creature's own data, so composing a record from
            // nothing writes zeros the engine reads as real values.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();

            Assert.IsEmpty(runtime.CollectDirtyCombatantEdits(_ => null));
        }

        [Test]
        public void OutsideAFightThereIsNothingToWrite() {
            var runtime = new CombatRuntime(new GameSession());

            Assert.IsEmpty(runtime.CollectDirtyCombatantEdits(slot => Record(9)));
        }

        [Test]
        public void APoisonedCharacterRestsButRecoversNOTHING() {
            // *** RestAction.RecoveryAllowed had no caller, so resting healed through any
            // affliction. *** The gate is the character's CONDITION ranks — not, as a comment here
            // said for most of a day, "skill-train bytes": canassa's base+displacement runs past
            // aSkillTrainRate into abActorStatusRanks.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 20));
            session.SetActiveParty(1, new byte[] { 0 });
            var conditions = new GameData.Resources.Character.ActorConditions();
            conditions[GameData.ActorCondition.Poisoned] = 30;
            session.SetActorConditionsForTest(0, conditions);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);

            int healed = runtime.ResolveRest(fight.Party[0]);

            Assert.AreEqual(0, healed, "the affliction blocks the recovery");
            Assert.IsTrue((fight.Party[0].Flags & CombatantFlags.Ready) == 0,
                "but the turn is still spent — it is a refused heal, not a refused action");
        }

        [Test]
        public void BeingHEALEDIsTheOneConditionThatDoesNotBlockIt() {
            // Six of the seven ranks are read and the skipped one is Healing — the only entry that
            // is a benefit. A port that tested all seven would stop a healing character resting.
            var session = new GameSession();
            session.SetActorStatsForTest(0, StatBlock(health: 20));
            session.SetActiveParty(1, new byte[] { 0 });
            var conditions = new GameData.Resources.Character.ActorConditions();
            conditions[GameData.ActorCondition.Healing] = 40;
            session.SetActorConditionsForTest(0, conditions);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));
            CombatEncounter fight = runtime.EnterRoster(null);

            Assert.Greater(runtime.ResolveRest(fight.Party[0]), 0);
        }

        [Test]
        public void APoisonedCombatantActuallyTakesPoisonDamage() {
            // *** PoisonTick had no caller. *** A poisoned combatant never took damage, and because
            // the flag stayed set and still displayed, the condition looked like it was working —
            // the same shape as Defend healing instead of guarding.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.Flags |= CombatantFlags.Poisoned;
            int before = monster.Health + monster.Stamina;

            // Advancing past the monster's turn is what runs the picker, and the picker is where
            // the outgoing actor's poison ticks.
            runtime.AdvanceToPartyTurn(m => runtime.ResolveEnemyTurn(m, Melee(), _ => 99));

            Assert.Less(monster.Health + monster.Stamina, before);
        }

        [Test]
        public void APoisonTickFloatsItsDamageAndFlinches() {
            // combat_arena_poison_tick goes through combat_arena_apply_damage with knockback 2
            // (COMBAT.C:209), which floats the damage and starts the recoil (COMBAT.C:377-385).
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.Flags |= CombatantFlags.Poisoned;
            int before = monster.Health + monster.Stamina;

            runtime.AdvanceToPartyTurn(m => runtime.ResolveEnemyTurn(m, Melee(), _ => 99));

            Assert.AreEqual(before - (monster.Health + monster.Stamina), monster.DamageFloat);
            Assert.AreEqual(2, monster.HitReactionRemap);
        }

        [Test]
        public void APoisonResistantClassTakesHalfAndARoundedAwayTickFloatsMiss() {
            // The tick passes damage flags 1 (COMBAT.C:209), and cbstat_apply_weakness_penalty halves
            // for a class whose mask has that bit (CBSTAT.C:154): a roll of 1 becomes 0, which floats
            // "miss" (COMBAT.C:386-389). Measured on a chapter-1 monster in Spice86: "miss", and "1".
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            var table = new GameData.Resources.Combat.CombatAffinityTables("t");
            for (int i = 0; i <= monster.ClassId; i++) {
                table.Creatures.Add(new GameData.Resources.Combat.CreatureAffinity { ClassId = i });
            }
            table.Creatures[monster.ClassId].ResistanceFlags = 1;
            runtime.Affinity = table;
            monster.Flags |= CombatantFlags.Poisoned;

            int ticks = 0;
            for (int round = 0; round < 12; round++) {
                int before = monster.Health + monster.Stamina;
                monster.DamageFloat = null;
                bool acted = false;
                runtime.AdvanceToPartyTurn(m => {
                    acted = true;
                    runtime.ResolveEnemyTurn(m, Melee(), _ => 99);
                });
                runtime.ResolveRest(member);
                if (!acted) {
                    continue;   // no turn, so no outgoing tick
                }
                ticks++;
                int lost = before - (monster.Health + monster.Stamina);

                Assert.LessOrEqual(lost, 1, $"round {round}");
                Assert.AreEqual(lost, monster.DamageFloat, $"round {round}: 0 is the port's \"miss\"");
            }
            Assert.GreaterOrEqual(ticks, 4);
        }

        [Test]
        public void ALethalPoisonTickKills() {
            // A tick that empties health dies on the spot (COMBAT.C:407-409), before the pick.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            monster.Flags |= CombatantFlags.Poisoned;

            // Brought low DURING its turn: health-scaled speed would otherwise never pick it, and
            // the tick is the outgoing actor's.
            runtime.AdvanceToPartyTurn(m => {
                runtime.ResolveEnemyTurn(m, Melee(), _ => 99);
                m.Stamina = 0;
                m.Health = 1;
            });

            Assert.IsTrue(monster.IsDead);
        }

        [Test]
        public void AnUnpoisonedCombatantIsUntouchedByThePicker() {
            // The discriminating half: if the tick ignored the flag, every fight would bleed.
            //
            // *** "UNTOUCHED" MEANS "NOT POISONED", AND HERE IT ALSO MEANS UNCHANGED. *** The roll
            // driven in is 99, so MonsterMeleeChoice.Pick takes the `roll > melee + swingAccuracy`
            // arm and the monster THRUSTS — and only the swing bills its attacker
            // (CombatActionDispatch.BillsTheAttacker). So nothing is charged and the pool must not
            // move at all.
            //
            // This assertion has now been both ways round, which is worth remembering: it briefly
            // expected `before - SwingCost` while the port billed every melee attack, and that
            // expectation was a symptom of the over-billing rather than a fact about the game.
            // It stays discriminating either way — a poison bleed shows up as a DROP.
            (CombatRuntime runtime, Combatant member, Combatant monster) = MeleeFight();
            int before = monster.Health + monster.Stamina;

            runtime.AdvanceToPartyTurn(m => runtime.ResolveEnemyTurn(m, Melee(), _ => 99));

            Assert.AreEqual(before, monster.Health + monster.Stamina,
                "a thrust costs the attacker nothing, and nothing bled it either");
        }

        [Test]
        public void DefendingNobodyIsHarmless() {
            Assert.AreEqual(0, new CombatRuntime(new GameSession()).ResolveRest(null));
        }

        [Test]
        public void AnArmedArcherWithRoomCanActuallyShoot() {
            // *** The case the old guard test could NOT express. *** It asserted false with an empty
            // fixture, so it read the same whether canShoot was hardcoded or computed. This one only
            // passes if the pack, the object table and the condition rule all actually connect.
            (CombatRuntime runtime, CombatEncounter fight) = ArcherAgainst(((byte)7, (byte)0));

            Assert.IsTrue(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void AnAdjacentEnemyTakesTheShotAway() {
            (CombatRuntime runtime, CombatEncounter fight) = ArcherAgainst(((byte)4, (byte)4));

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void AnEmptyQuiverRefusesEvenWithTheCrossbowInHand() {
            (CombatRuntime runtime, CombatEncounter fight) = ArcherAgainst(((byte)7, (byte)0), bolts: 0);

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void ACrossbowInThePackButNotInHandRefuses() {
            // Equipped is 0x40; without it the weapon is carried, not wielded.
            (CombatRuntime runtime, CombatEncounter fight) =
                ArcherAgainst(((byte)7, (byte)0), crossbowFlags: 0);

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void ABrokenCrossbowRefuses() {
            // Broken is 0x10, and it overrides the condition the Degradable type would report.
            (CombatRuntime runtime, CombatEncounter fight) =
                ArcherAgainst(((byte)7, (byte)0), crossbowFlags: 0x40 | 0x10);

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void ShootingIsRefusedUntilInventoryIsReachable() {
            // Deliberate, not an oversight: the quiver and weapon checks need inventory a Combatant
            // cannot reach. Offering a shot the actor cannot take would be worse than refusing.
            (CombatRuntime runtime, CombatEncounter fight) =
                FightWithEnemiesAt(StatBlock(health: 30), ((byte)7, (byte)0));

            Assert.IsFalse(runtime.CapabilitiesFor(fight.Party[0]).CanShoot);
        }

        [Test]
        public void APartyMemberEntersOnItsP1DatTile() {
            // Character index 0 is charSlot 1, so it reads entry slot 0 - the same "position is one
            // below the actor number" convention the container owner numbering uses.
            GameSession session = SessionWithParty(1);
            var runtime = new CombatRuntime(session, null, Entries(((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null);

            Assert.AreEqual(1, fight.Party.Count);
            Assert.AreEqual((3, 4), (fight.Party[0].X, fight.Party[0].Y));
        }

        [Test]
        public void PartyMembersWhoseENTRYTilesCollideDoNotShareOne() {
            // *** Real, not hypothetical. *** The shipped P1.DAT gives (1,1), (6,2) and (4,0) to two
            // slots each, so the party collides with ITSELF on entry and the placement pass has to
            // separate them. Without it two characters stand on one tile and one is invisible.
            GameSession session = SessionWithParty(2);
            var runtime = new CombatRuntime(session, null,
                Entries(((byte)3, (byte)4), ((byte)3, (byte)4)));

            CombatEncounter fight = runtime.EnterRoster(null);

            Assert.AreEqual(2, fight.Party.Count);
            Assert.AreEqual((3, 4), (fight.Party[0].X, fight.Party[0].Y), "the first keeps its tile");
            Assert.AreNotEqual((fight.Party[0].X, fight.Party[0].Y),
                (fight.Party[1].X, fight.Party[1].Y), "the second is moved off it");
            Assert.IsTrue(runtime.Grid.IsOccupied(fight.Party[1].X, fight.Party[1].Y));
        }

        [Test]
        public void WithNoP1DatThePartyStillEntersRatherThanTheFightFailing() {
            GameSession session = SessionWithParty(1);

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(null);

            Assert.AreEqual(1, fight.Party.Count, "a missing file must not cost the party the fight");
        }

        [Test]
        public void AnEnemyEntersOnItsSavedTile() {
            var session = new GameSession();
            session.SetRosterActorForTest(412, StatBlock(health: 30), gridX: 5, gridY: 9);

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(new short[] { 412 });

            Assert.AreEqual(5, fight.Enemies[0].X);
            Assert.AreEqual(9, fight.Enemies[0].Y);
        }

        [Test]
        public void TwoEnemiesSavedOnOneTileDoNotBothStandOnIt() {
            // *** The reason placement is a pass rather than a copy. *** Saved tiles are a wish: the
            // records can name the same one, and stacking would hide an enemy under another.
            var session = new GameSession();
            session.SetRosterActorForTest(412, StatBlock(health: 30), gridX: 5, gridY: 9);
            session.SetRosterActorForTest(413, StatBlock(health: 30), gridX: 5, gridY: 9);

            var runtime = new CombatRuntime(session);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 412, 413 });

            Combatant first = fight.Enemies[0];
            Combatant second = fight.Enemies[1];
            Assert.AreEqual((5, 9), (first.X, first.Y), "the first keeps the tile it asked for");
            Assert.AreNotEqual((first.X, first.Y), (second.X, second.Y), "the second is moved off it");
            Assert.IsTrue(runtime.Grid.IsOccupied(second.X, second.Y), "and its tile is marked taken");
        }

        [Test]
        public void AnEncounterFieldsTheActorsItsRosterNames() {
            // The end-to-end shape a Comb hotspot will use: encounter number -> actor slots -> stats.
            // The two lookups are keyed differently and it matters — the encounter number indexes the
            // 700 enemy-party records, the values index the 1730-slot actor table.
            var session = new GameSession();
            session.SetEncounterRosterForTest(encounterNumber: 118, 412, 413);
            session.SetRosterActorForTest(412, StatBlock(health: 30), creatureType: 7);
            session.SetRosterActorForTest(413, StatBlock(health: 40), creatureType: 7);

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(session.RosterOf(118));

            Assert.AreEqual(2, fight.Enemies.Count);
            Assert.AreEqual(30, fight.Enemies[0].Health);
            Assert.AreEqual(40, fight.Enemies[1].Health);
        }

        [Test]
        public void AnEncounterThatNamesNobodyIsNullRatherThanAnEmptyRoster() {
            // So a caller cannot mistake "fields nobody" for "a fight with no enemies in it".
            var session = new GameSession();
            session.SetEncounterRosterForTest(encounterNumber: 118);

            Assert.IsNull(session.RosterOf(118));
            Assert.IsNull(session.RosterOf(999), "an encounter with no record at all");
        }

        [Test]
        public void ARosterEnemyTakesItsStatsFromTheRosterTable() {
            // The lookup this whole method turns on. Without a populated session the enemy side comes
            // out empty either way, which is how a wrong table went unnoticed through a green suite.
            var session = new GameSession();
            session.SetRosterActorForTest(412, StatBlock(health: 37, stamina: 22, speed: 9));

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(new short[] { 412 });

            Assert.AreEqual(1, fight.Enemies.Count, "slot 412 is a real actor and must be fielded");
            Assert.AreEqual(37, fight.Enemies[0].Health);
            Assert.AreEqual(22, fight.Enemies[0].Stamina);
            Assert.AreEqual(9, fight.Enemies[0].Speed);
        }

        [Test]
        public void ALowRosterSlotDoesNotPickUpThePartyMemberWhoSharesItsNumber() {
            // *** THE REGRESSION. *** Roster slots index the save's 1730-entry actor table; StatsOf
            // reads the six-entry PARTY array. Resolving a roster slot through the party table put
            // party member 3 on the field as the enemy.
            var session = new GameSession();
            session.SetActorStatsForTest(3, StatBlock(health: 55));   // party member 3
            // ...and nothing at roster slot 3.

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(new short[] { 3 });

            Assert.IsEmpty(fight.Enemies, "no roster actor at slot 3, so there is no enemy to field");
        }

        [Test]
        public void AnEnemyCarriesItsCreatureClassNotItsRosterSlot() {
            // ClassId drives the species-keyed rules (always-acts, vanishes-on-death, conjurable).
            // Putting the slot there makes slots 54/56/57 immune or unsummonable by coincidence.
            var session = new GameSession();
            session.SetRosterActorForTest(56, StatBlock(health: 12), creatureType: 21);

            CombatEncounter fight = new CombatRuntime(session).EnterRoster(new short[] { 56 });

            Assert.AreEqual(21, fight.Enemies[0].ClassId, "creature class, not the slot number");
            Assert.AreNotEqual(56, fight.Enemies[0].ClassId);
        }

        [Test]
        public void ARosterSlotWithNoActorBehindItIsSkippedRatherThanFieldedAtZeroHealth() {
            // Admitting an unresolvable slot would put a 0-health combatant on the field, and
            // BeginRound would then have to cope with an enemy that is already dead on arrival.
            CombatEncounter fight = Runtime().EnterRoster(new short[] { 3, 4 });

            Assert.IsEmpty(fight.Enemies, "no session, so neither slot resolves to stats");
        }

        [Test]
        public void AnAbsentRosterIsAnEmptyFightRatherThanAThrow() {
            // A Comb record can point at a roster that was never populated.
            Assert.IsEmpty(Runtime().EnterRoster(null).Enemies);
        }

        private static CombatRuntime FightWith(Combatant party, params Combatant[] enemies) {
            var rt = Runtime();
            CombatEncounter enc = rt.Enter(new List<Combatant>(enemies));
            enc.Party.Add(party);
            return rt;
        }

        [Test]
        public void EnteringWithNoSessionStillBuildsTheOpposingSide() {
            var rt = Runtime();
            CombatEncounter enc = rt.Enter(new List<Combatant> { Enemy(5), Enemy(3) });

            Assert.IsTrue(rt.InCombat);
            Assert.AreEqual(2, enc.Enemies.Count);
            Assert.AreEqual(0, enc.Party.Count);
        }

        [Test]
        public void TheOpposingSideIsNeverTreatedAsPartyMembers() {
            // The arrays are the sides; PartySlot is what makes a combatant "the party". A caller
            // handing us a roster with a stray slot set must not smuggle an enemy onto the party
            // side's rules — nobody-dies would then apply to a monster.
            var rt = Runtime();
            var ringer = Enemy(5);
            ringer.PartySlot = 3;
            CombatEncounter enc = rt.Enter(new List<Combatant> { ringer });

            Assert.IsFalse(enc.Enemies[0].IsPartyMember);
        }

        [Test]
        public void BothSidesAreCappedAtTheSlotCount() {
            var many = new List<Combatant>();
            for (var i = 0; i < CombatModeEntry.SideSlots + 4; i++) {
                many.Add(Enemy(1));
            }
            CombatEncounter enc = Runtime().Enter(many);

            Assert.AreEqual(CombatModeEntry.SideSlots, enc.Enemies.Count);
        }

        [Test]
        public void EnteringTwiceDoesNotReplaceTheLiveEncounter() {
            var rt = Runtime();
            CombatEncounter first = rt.Enter(new List<Combatant> { Enemy(5) });
            CombatEncounter second = rt.Enter(new List<Combatant> { Enemy(9), Enemy(9) });

            Assert.AreSame(first, second, "a second Enter must not throw the fight away");
            Assert.AreEqual(1, rt.Encounter.Enemies.Count);
        }

        [Test]
        public void MonsterTurnsResolveWithoutTheCallerSteppingThroughThem() {
            // The player never steps through monster turns: control comes back only when a party
            // member is up. A slow party member means all three enemies go first.
            Combatant slowcoach = PartyMember(speed: 1);
            CombatRuntime rt = FightWith(slowcoach, Enemy(5), Enemy(4), Enemy(3));

            var resolved = new List<Combatant>();
            Combatant up = rt.AdvanceToPartyTurn(c => resolved.Add(c));

            Assert.AreSame(slowcoach, up, "control returns on the party member, not before");
            Assert.AreEqual(3, resolved.Count, "and every enemy acted on the way there");
            foreach (Combatant c in resolved) {
                Assert.IsFalse(c.IsPartyMember, "only enemy turns are resolved for the caller");
            }
        }

        [Test]
        public void AFastPartyMemberComesUpBeforeAnyEnemyActs() {
            Combatant sprinter = PartyMember(speed: 9);
            CombatRuntime rt = FightWith(sprinter, Enemy(5), Enemy(4));

            var resolved = new List<Combatant>();
            Assert.AreSame(sprinter, rt.AdvanceToPartyTurn(c => resolved.Add(c)));
            Assert.IsEmpty(resolved);
        }

        [Test]
        public void AnEnemyTurnThatNeverEndsIsAbandonedRatherThanSpinning() {
            // The guard exists so a broken AI seam reports itself instead of hanging the game.
            //
            // Re-arming the ACTING enemy is not enough to loop: EndTurn runs after the resolver and
            // clears exactly that actor, so the fight still drains. It takes a resolver that keeps
            // the whole side ready — then an enemy always outranks the slow party member and the
            // party is never picked.
            CombatRuntime rt = FightWith(PartyMember(speed: 1), Enemy(9), Enemy(9));
            CombatEncounter enc = rt.Encounter;

            Combatant up = rt.AdvanceToPartyTurn(
                _ => { foreach (Combatant e in enc.Enemies) { e.Flags |= CombatantFlags.Ready; } },
                maxTurns: 16);

            Assert.IsNull(up);
            Assert.IsTrue(rt.InCombat, "abandoning a pick must not silently end the fight");
        }

        [Test]
        public void AFightWithNoPartyIsAlreadyOver() {
            // PartyAlive() == 0 is a wipe, so the loop ends immediately rather than letting the
            // enemies mill around. Stated because it is what made two of these tests vacuous.
            var rt = Runtime();
            rt.Enter(new List<Combatant> { Enemy(5) });

            var resolved = new List<Combatant>();
            Assert.IsNull(rt.AdvanceToPartyTurn(c => resolved.Add(c)));
            Assert.IsEmpty(resolved);
        }

        [Test]
        public void AdvancingOutsideCombatIsANoOp() {
            Assert.IsNull(Runtime().AdvanceToPartyTurn());
        }

        [Test]
        public void LeavingEndsTheEncounter() {
            var rt = Runtime();
            rt.Enter(new List<Combatant> { Enemy(5) });
            rt.Leave();

            Assert.IsFalse(rt.InCombat);
            Assert.IsNull(rt.Encounter);
            Assert.DoesNotThrow(() => rt.Leave(), "leaving twice is harmless");
        }

        [Test]
        public void NobodyDiesInCombat() {
            // *** The rule most likely to be ported wrong. *** A party member reduced below one
            // health walks out on exactly 1 health and 0 stamina; party death is not a combat
            // outcome. Asserted on the combatant because there is no session to write back to.
            var rt = Runtime();
            CombatEncounter enc = rt.Enter(new List<Combatant> { Enemy(5) });
            var downed = new Combatant {
                PartySlot = 1, Health = -6, Stamina = 4, Speed = 3, Flags = CombatantFlags.Dead,
            };
            enc.Party.Add(downed);

            rt.Leave();

            Assert.AreEqual(CombatModeEntry.DownedPartyMemberHealth, downed.Health);
            Assert.AreEqual(CombatModeEntry.DownedPartyMemberStamina, downed.Stamina);
            Assert.IsFalse(downed.IsDead, "a downed party member leaves the field alive");
        }

        [Test]
        public void AHealthyPartyMemberIsLeftAloneOnTheWayOut() {
            var rt = Runtime();
            CombatEncounter enc = rt.Enter(new List<Combatant> { Enemy(5) });
            var fine = new Combatant { PartySlot = 1, Health = 12, Stamina = 7, Speed = 3 };
            enc.Party.Add(fine);

            rt.Leave();

            Assert.AreEqual(12, fine.Health);
            Assert.AreEqual(7, fine.Stamina, "stamina is only zeroed for someone who went down");
        }

        [Test]
        public void TheNobodyDiesRuleDoesNotRescueTheOtherSide() {
            // It is a statement about the PARTY side, not about whoever is acting. Applying it to
            // the enemies would make monsters unkillable.
            var rt = Runtime();
            CombatEncounter enc = rt.Enter(new List<Combatant> { Enemy(5) });
            enc.Enemies[0].Health = -3;
            enc.Enemies[0].Flags |= CombatantFlags.Dead;

            rt.Leave();

            Assert.AreEqual(-3, enc.Enemies[0].Health);
            Assert.IsTrue(enc.Enemies[0].IsDead);
        }
        [Test]
        public void HEALINGONTHECOMBATSCREENSurvivesTheEndOfTheFight() {
            // *** THE HALF OF THE ROUND TRIP THAT WAS MISSING. *** The combat inventory screen edits
            // the SESSION's stats, while a fight's Combatant carries its own copies and Leave()
            // flushes those out over the top. So a healing potion drunk mid-fight was consumed and
            // then undone. Written as the whole journey — heal the session, close the screen, end
            // the fight — because each half passes on its own.
            (CombatRuntime runtime, Combatant member, Combatant _) = MeleeFight();
            ActorStat[] stats = runtime.StatsFor(member);
            ActorStat health = stats[(int)ActorAttribute.Health];

            // Wound the combatant, as a fight would, so the stale value is distinguishable.
            member.Health = 1;
            // Then heal on the screen, which writes the session and not the combatant.
            health.Base = health.Max;

            runtime.RefreshPartyStatsFromSession();
            runtime.Leave();

            Assert.AreEqual(health.Max, health.Base,
                "the healing survived; without the refresh the combatant's 1 is written back");
        }

        [Test]
        public void TheRefreshLeavesENEMIESAlone() {
            // An enemy has no screen to be edited on and its stats live in the roster, so re-reading
            // them would undo the damage the fight has just done.
            (CombatRuntime runtime, Combatant _, Combatant monster) = MeleeFight();
            monster.Health = 1;

            runtime.RefreshPartyStatsFromSession();

            Assert.AreEqual(1, monster.Health);
        }

    }
}
