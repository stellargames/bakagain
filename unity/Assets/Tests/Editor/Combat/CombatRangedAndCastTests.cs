namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using GameData.Resources.Object;
    using GameData.Resources.Spells;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Firing a quarrel and casting a spell at a target — the two arms target selection resolves to.
    /// </summary>
    /// <remarks>
    /// <b>Both shipped without a Unity-side check.</b> <see cref="CombatRuntime.ResolveShot"/> and
    /// <see cref="CombatRuntime.ResolveCast"/> compose models that are each tested on their own; what
    /// is untested is the composition, which is where the three ranged/melee asymmetries and the
    /// combat cost path actually live.
    /// </remarks>
    public class CombatRangedAndCastTests {
        private const byte CrossbowId = 30, ArmorId = 44;

        // Quarrel kind 0. It is BOTH the damage record (its swing base adds to the crossbow's) and
        // the accuracy record RangedAmmoAccuracy.RecordFor(0) names, which is why one object serves.
        private const byte QuarrelId = 0x24;
        private const int PartyPosition = 0;

        // rnd(n) at its floor and its ceiling. The floor lands every roll; the ceiling misses every
        // roll a chance under 100 could make, and stays in range for the wear and armour draws too —
        // a constant 99 would be out of range for rnd(2).
        private static readonly System.Func<int, int> AlwaysLow = _ => 0;
        private static readonly System.Func<int, int> AlwaysHigh = n => n > 0 ? n - 1 : 0;

        private static ObjectInfoSet Objects() =>
            new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("O") {
                    Number = CrossbowId, Name = "crossbow", ObjectType = ObjectType.Crossbow,
                    Flags = ObjectFlags.Degradable, DegradeChancePercent = 100,
                    MaxWearPerDegrade = 1, SwingBaseDamage = 6,
                    InventorySlots = 1, MaxAmount = 1,
                },
                new ObjectInfo("O") {
                    Number = QuarrelId, Name = "quarrel", ObjectType = ObjectType.Misc,
                    SwingBaseDamage = 4, SwingAccuracy_ArmorMod_BowAccuracy = 10,
                    InventorySlots = 1, MaxAmount = 25,
                },
                new ObjectInfo("O") {
                    Number = ArmorId, Name = "armour", ObjectType = ObjectType.Armor,
                    Flags = ObjectFlags.Degradable, DegradeChancePercent = 100,
                    MaxWearPerDegrade = 1, SwingAccuracy_ArmorMod_BowAccuracy = 40,
                    InventorySlots = 1, MaxAmount = 1,
                },
            });

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

        private static GameSession SessionWith(params (byte ObjectId, byte Variable)[] items) {
            var stored = new SaveGameInventoryItemData[items.Length];
            for (var i = 0; i < items.Length; i++) {
                stored[i] = new SaveGameInventoryItemData(items[i].ObjectId, items[i].Variable,
                    (ushort)ItemFlags.Equipped);
            }

            var pack = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 1, maxChapter: 9,
                    worldItemId: 0, x: 0, y: 0, actorNumber: PartyPosition + 1),
                SaveGameContainerType.Inventory, numberOfItems: (byte)items.Length, capacity: 20,
                dataTypes: 0, items: stored,
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

            var session = new GameSession();
            session.SetActorStatsForTest(PartyPosition, StatBlock());
            session.SetActiveParty(1, new byte[] { (byte)PartyPosition });
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { pack }),
            }), chapter: 1);
            return session;
        }

        private static PartyCombatEntries Entries() =>
            new PartyCombatEntries("P1.DAT", new List<SaveGameCombatData> {
                new SaveGameCombatData(0, 0, 3, 4, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            });

        private static (CombatRuntime Runtime, GameSession Session, Combatant Member, Combatant Monster)
            Fight(params (byte, byte)[] items) {
            GameSession session = SessionWith(items);
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), Objects());
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, session, fight.Party[0], fight.Enemies[0]);
        }

        // ------------------------------------------------------------------ Survivors recover

        private static Combatant EnterAfterHours(int hours) {
            // Health 60 and stamina 20 of 99 each: a pool of 80 against 198.
            GameSession session = SessionWith();
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.EncounterFoughtTimes.Stamp(7, 120000);
            session.GameTimeIn2Seconds = 120000 + hours * GameData.Resources.World.EncounterFoughtTimes.TicksPerRecoveryPoint;
            var runtime = new CombatRuntime(session, null, Entries(), Objects());
            return runtime.EnterRoster(new short[] { 400 }, encounterNumber: 7).Enemies[0];
        }

        [Test]
        public void ASURVIVORRecoversAPointAnHourOnTheWayBackIn() {
            // CBENC.C:79-84.
            Combatant monster = EnterAfterHours(14);
            Assert.AreEqual(80 + 14, monster.Health + monster.Stamina);
        }

        [Test]
        public void ASURVIVORLeftAlone130HoursIsDrainedByTheSixteenBitShift() {
            // 130 << 8 wraps to -32256 in sixteen bits: a drain of 126, which empties a pool of 80.
            Combatant monster = EnterAfterHours(130);
            Assert.AreEqual(0, monster.Health + monster.Stamina);
        }

        // ------------------------------------------------------------------ Mad God's Rage

        private static Spell MadGodsRageSpell() =>
            new Spell("21") {
                TargetingType = 1, AnimationEffectType = 8, MinimumCost = 20, MaximumCost = 20,
                Calculation = SpellCalculation.FixedAmount, Damage = 100,
            };

        private static (CombatRuntime Runtime, CombatEncounter Fight, List<int> Cues) RageFight(
            GameSession session) {
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(), 12, gridX: 6, gridY: 5);
            var cues = new List<int>();
            var runtime = new CombatRuntime(session, null, Entries(), Objects(), playSfx: cues.Add);
            return (runtime, runtime.EnterRoster(new short[] { 400, 401 }), cues);
        }

        [Test]
        public void MADGODSRAGEGoesRoundUntilEveryOpponentFalls_AndBillsOnlyThreePerStrike() {
            // CSPELL.C:879. Every draw at 0: both opponents struck every round for 15 + 0 + 5, every
            // strike exploding with cue 0x1d. No up-front bill -- the tail that charges the 20 is
            // skipped -- so the caster's whole loss is 3 per strike.
            var (runtime, fight, cues) = RageFight(SessionWith());
            Combatant caster = fight.Party[0];
            int before = caster.Health + caster.Stamina;

            Assert.IsTrue(runtime.ResolveCast(caster, fight.Enemies[0], MadGodsRageSpell(),
                SpellIds.MadGodsRage, 20, _ => 0));

            Assert.IsTrue(fight.Enemies[0].IsDead, "the aimed opponent");
            Assert.IsTrue(fight.Enemies[1].IsDead, "the storm takes the whole side, not the target");
            int strikes = cues.FindAll(c => c == SpellCastSound.MadGodsRagePerTargetSound).Count;
            Assert.Greater(strikes, 2, "more than one round");
            Assert.AreEqual(before - 3 * strikes, caster.Health + caster.Stamina);
            Assert.AreEqual(1, cues.FindAll(c => c == 78).Count, "one quake for the whole storm");
        }

        [Test]
        public void MADGODSRAGEStopsAfterTheRoundThatSpendsTheCaster() {
            // The other direction: the go-again test is at the top of the round, so a caster with 3
            // points pays for both strikes of round one and the storm ends with both still standing.
            GameSession session = SessionWith();
            ActorStat[] frail = StatBlock();
            frail[(int)ActorAttribute.Health] = new ActorStat { Base = 3, Max = 99 };
            frail[(int)ActorAttribute.Stamina] = new ActorStat { Base = 0, Max = 99 };
            session.SetActorStatsForTest(PartyPosition, frail);
            var (runtime, fight, cues) = RageFight(session);

            runtime.ResolveCast(fight.Party[0], fight.Enemies[0], MadGodsRageSpell(),
                SpellIds.MadGodsRage, 20, _ => 0);

            Assert.AreEqual(2, cues.FindAll(c => c == SpellCastSound.MadGodsRagePerTargetSound).Count);
            Assert.IsFalse(fight.Enemies[0].IsDead);
            Assert.IsFalse(fight.Enemies[1].IsDead);
        }

        // ------------------------------------------------------------------ Evil Seek

        [Test]
        public void EVILSEEKChainsThroughEveryOpponentInRosterOrderAtEightyPercentAHop() {
            // CSPELL.C:565. Investment 10 -> the chain starts at 20; every later hop keeps 80%,
            // truncated: 20, 16, 12. The next victim is the first living opponent in roster order not
            // yet hit (:486), and the delivery's own magnitude is zeroed, so nothing lands twice.
            GameSession session = SessionWith();
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(), 12, gridX: 6, gridY: 5);
            session.SetRosterActorForTest(402, StatBlock(), 12, gridX: 7, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), Objects());
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401, 402 });
            int[] before = fight.Enemies.ConvertAll(e => e.Health + e.Stamina).ToArray();

            runtime.ResolveCast(fight.Party[0], fight.Enemies[0], new Spell("44") {
                TargetingType = 1, MinimumCost = 10, MaximumCost = 10,
                Calculation = SpellCalculation.FixedAmount, Damage = 100,
            }, SpellIds.EvilSeek, 10, _ => 0);

            CollectionAssert.AreEqual(new[] { 20, 16, 12 },
                new[] { 0, 1, 2 }.Select(i => before[i] - (fight.Enemies[i].Health + fight.Enemies[i].Stamina)).ToArray(),
                "each hop's damage, not the record's 100 and not a single target");
        }

        [Test]
        public void EVILSEEKStartsNoChainWhenTheAimedTargetResists() {
            // CSPELL.C:1370 -- the per-spell switch, case 44 included, runs only when the AIMED target
            // does not resist. Enemy 0's creature resists Evil Seek and the other two do not; they
            // still take nothing, because no chain starts. The original did exactly this to an
            // entry-306 rogue (TASK-541).
            GameSession session = SessionWith();
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(), 13, gridX: 6, gridY: 5);
            session.SetRosterActorForTest(402, StatBlock(), 13, gridX: 7, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), Objects()) {
                SpellResistance = WeaknessListing(spellNumber: SpellIds.EvilSeek, creatureType: 12),
            };
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401, 402 });
            int[] before = fight.Enemies.ConvertAll(e => e.Health + e.Stamina).ToArray();

            runtime.ResolveCast(fight.Party[0], fight.Enemies[0], new Spell("44") {
                TargetingType = 1, MinimumCost = 10, MaximumCost = 10,
                Calculation = SpellCalculation.FixedAmount, Damage = 100,
            }, SpellIds.EvilSeek, 10, _ => 0);

            CollectionAssert.AreEqual(new[] { 0, 0, 0 },
                new[] { 0, 1, 2 }.Select(i => before[i] - (fight.Enemies[i].Health + fight.Enemies[i].Stamina)).ToArray(),
                "a resisted first victim starts no chain, so the two non-resisters are untouched too");
        }

        // ------------------------------------------------------------------ Flamecast

        [Test]
        public void FLAMECASTSplashesEveryoneButTheTargetWithinTwoCells() {
            // COMBAT.C:417 via CSPELL.C:1487. A 40-point hit: the target takes 40, a neighbour one cell
            // away takes (40 >> 2) - 1 = 9, and one three cells away takes nothing.
            GameSession session = SessionWith();
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            session.SetRosterActorForTest(401, StatBlock(), 12, gridX: 6, gridY: 5);
            session.SetRosterActorForTest(402, StatBlock(), 12, gridX: 5, gridY: 8);
            var runtime = new CombatRuntime(session, null, Entries(), Objects());
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400, 401, 402 });
            fight.Party[0].X = 0;
            fight.Party[0].Y = 0;
            int[] before = fight.Enemies.ConvertAll(e => e.Health + e.Stamina).ToArray();

            runtime.ResolveCast(fight.Party[0], fight.Enemies[0], new Spell("4") {
                TargetingType = 0, MinimumCost = 10, MaximumCost = 10,
                Calculation = SpellCalculation.FixedAmount, Damage = 40,
            }, SpellIds.Flamecast, 10, _ => 0);

            CollectionAssert.AreEqual(new[] { 40, 9, 0 },
                new[] { 0, 1, 2 }.Select(i => before[i] - (fight.Enemies[i].Health + fight.Enemies[i].Stamina)).ToArray(),
                "the target takes the hit alone, the neighbour a quarter less a cell, the far one nothing");
        }

        // ------------------------------------------------------------------ Steelfire and Invitation

        [Test]
        public void STEELFIREMarksTheTargetsEquippedSword() {
            // CSPELL.C:1112 -- the first equipped item of category 1 in the TARGET's pack gets 0x200.
            const byte SwordId = 5;
            GameSession session = SessionWith((SwordId, 50));
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            var objects = new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("O") {
                    Number = SwordId, Name = "sword", ObjectType = ObjectType.Sword,
                    InventorySlots = 1, MaxAmount = 1,
                },
            });
            var runtime = new CombatRuntime(session, null, Entries(), objects);
            Combatant member = runtime.EnterRoster(new short[] { 400 }).Party[0];

            runtime.ResolveCast(member, member, new Spell("25") {
                TargetingType = 2, AnimationEffectType = -1, MinimumCost = 10, MaximumCost = 10,
                Calculation = SpellCalculation.CostTimesDuration, Damage = 0, Duration = 1,
            }, SpellIds.Steelfire, 10, _ => 0);

            ushort flags = session.GetActorInventory(member.ClassId).Items[0].ItemFlags;
            Assert.AreNotEqual(0, flags & (ushort)ItemFlags.SteelFired);
        }

        [Test]
        public void NIGHTFINGERSBurnsTheGloryHandAndPutsTheTargetsPackUp() {
            // CSPELL.C:1084 -- the caster's Glory Hand goes, and the target's pack is opened.
            const byte GloryHandId = (byte)SpellCastRoutines.GloryHandObjectId;
            GameSession session = SessionWith((GloryHandId, 1));
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            var objects = new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("O") {
                    Number = GloryHandId, Name = "Glory Hand", ObjectType = ObjectType.Misc,
                    InventorySlots = 1, MaxAmount = 1,
                },
            });
            var runtime = new CombatRuntime(session, null, Entries(), objects);
            Combatant member = runtime.EnterRoster(new short[] { 400 }).Party[0];
            GameData.Resources.Inventory.RuntimeContainer opened = null;
            runtime.OpenStolenPack = pack => opened = pack;

            runtime.ResolveCast(member, member, new Spell("12") {
                TargetingType = 4, AnimationEffectType = 6, MinimumCost = 14, MaximumCost = 14,
                ObjectId = GloryHandId, Calculation = SpellCalculation.NonCostRelated, Damage = 0,
            }, SpellIds.Nightfingers, 14, _ => 0);

            GameData.Resources.Inventory.RuntimeContainer pack = session.GetActorInventory(member.ClassId);
            Assert.AreSame(pack, opened);
            Assert.AreEqual(0, GameData.Resources.Inventory.InventoryQuery.CountByKind(pack, GloryHandId));
        }

        [Test]
        public void INVITATIONPullsTheTargetOneCellPerPointOfPower_UpToTheDistance() {
            // CSPELL.C:637 -- min(distance, intensity) steps at the caster's cell.
            var (runtime, _, member, monster) = Fight();
            int before = CombatGrid.ChebyshevDistance(member.X, member.Y, monster.X, monster.Y);
            Assume.That(before, Is.GreaterThan(1), "the fixture must leave room to pull");

            runtime.ResolveCast(member, monster, new Spell("30") {
                TargetingType = 1, AnimationEffectType = 14, MinimumCost = 1, MaximumCost = 10,
                Calculation = SpellCalculation.NonCostRelated, Damage = 0,
            }, SpellIds.Invitation, 1, _ => 0);

            Assert.AreEqual(before - 1,
                CombatGrid.ChebyshevDistance(member.X, member.Y, monster.X, monster.Y));
        }

        // ------------------------------------------------------------------ Winds of Eortis

        private static Spell WindsOfEortisSpell() =>
            new Spell("27") {
                TargetingType = 0, AnimationEffectType = 12, MinimumCost = 1, MaximumCost = 14,
                Calculation = SpellCalculation.NonCostRelated, Damage = 0,
            };

        private static (int Dx, int Dy) PushStep(Combatant caster, Combatant victim) {
            int direction = SpellCastRoutines.KnockbackDirection(victim.X - caster.X, victim.Y - caster.Y);
            return (SpellCastRoutines.KnockbackDx(direction), SpellCastRoutines.KnockbackDy(direction));
        }

        [Test]
        public void WINDSOFEORTISPushesTheVictimOneCellPerPointOfCost_AndBillsTheCasterOnce() {
            // CSPELL.C:653/357. The routine pays the cost itself and ends the cast, so the tail's bill
            // never runs: the caster's whole loss is the cost, once.
            var (runtime, _, member, monster) = Fight();
            (int dx, int dy) = PushStep(member, monster);
            Assume.That(dx != 0 || dy != 0, "the fixture must not sit along the inert direction");
            int startX = monster.X, startY = monster.Y;
            int before = member.Health + member.Stamina;

            Assert.IsTrue(runtime.ResolveCast(member, monster, WindsOfEortisSpell(),
                SpellIds.WindsOfEortis, 2, _ => 0));

            Assert.AreEqual((startX + 2 * dx, startY + 2 * dy), (monster.X, monster.Y));
            Assert.AreEqual(before - 2, member.Health + member.Stamina);
        }

        [Test]
        public void WINDSOFEORTISStopsAtTheFirstCellTheVictimCannotEnter() {
            var (runtime, _, member, monster) = Fight();
            (int dx, int dy) = PushStep(member, monster);
            int startX = monster.X, startY = monster.Y;
            runtime.Grid.SetOccupied(startX + 2 * dx, startY + 2 * dy, true);

            runtime.ResolveCast(member, monster, WindsOfEortisSpell(), SpellIds.WindsOfEortis, 5, _ => 0);

            Assert.AreEqual((startX + dx, startY + dy), (monster.X, monster.Y));
        }

        // ------------------------------------------------------------------ Thy Master's Will

        private const byte WyvernEggId = 17;

        private static Spell ThyMastersWillSpell() =>
            new Spell("41") {
                TargetingType = 4, AnimationEffectType = ThyMastersWill.EffectKind,
                ObjectId = WyvernEggId, MinimumCost = 20, MaximumCost = 20,
                Calculation = SpellCalculation.NonCostRelated, Damage = 0,
            };

        private static (CombatRuntime Runtime, GameSession Session, Combatant Member, Combatant Monster,
            List<int> Cues) WillFight(int creatureType) {
            GameSession session = SessionWith((WyvernEggId, 1));
            session.SetRosterActorForTest(400, StatBlock(), creatureType, gridX: 5, gridY: 5);
            var objects = new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("O") {
                    Number = WyvernEggId, Name = "Wyvern's Egg", ObjectType = ObjectType.Misc,
                    InventorySlots = 1, MaxAmount = 1,
                },
            });
            var cues = new List<int>();
            var runtime = new CombatRuntime(session, null, Entries(), objects, playSfx: cues.Add);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, session, fight.Party[0], fight.Enemies[0], cues);
        }

        [Test]
        public void THYMASTERSWillSendsAWyvernRunningAndSpendsTheEgg() {
            // CSPELL.C:852 -- effect kind 18: a creature of type 0x29-0x2b gets cue 0x51, is routed
            // (combatenc_actor_flee_tile_east picks its exit now) and the caster loses one egg.
            var (runtime, session, member, wyvern, cues) = WillFight(ThyMastersWill.FirstAffectedCreatureType);
            // The first draw lands the cast; every later one clears MonsterFleeDestination's
            // RND(100) > 50 bar, which a constant low roll never does -- the scan would pick nothing.
            var draws = 0;
            System.Func<int, int> landThenAccept = n => draws++ == 0 ? 0 : AlwaysHigh(n);

            runtime.ResolveCast(member, wyvern, ThyMastersWillSpell(), spellId: 41, power: 20, landThenAccept);

            Assert.IsTrue((wyvern.Flags & CombatantFlags.Fleeing) != 0, "the wyvern routs");
            Assert.IsNotNull(wyvern.FleeDestination, "and knows where it is running to");
            CollectionAssert.Contains(cues, FieldSpells.GeneralSound);
            Assert.AreEqual(0, ConditionOf(session, WyvernEggId), "the egg is spent");
        }

        [Test]
        public void THYMASTERSWillDoesNothingToAnythingButAWyvern() {
            var (runtime, session, member, other, cues) = WillFight(12);

            runtime.ResolveCast(member, other, ThyMastersWillSpell(), spellId: 41, power: 20, AlwaysLow);

            Assert.IsTrue((other.Flags & CombatantFlags.Fleeing) == 0, "no rout");
            CollectionAssert.DoesNotContain(cues, FieldSpells.GeneralSound);
            Assert.AreEqual(1, ConditionOf(session, WyvernEggId), "and the egg is kept");
        }

        private static byte ConditionOf(GameSession session, byte objectId) {
            foreach (GameData.Resources.Inventory.RuntimeItem item
                     in session.GetActorInventory(PartyPosition).Items) {
                if (item.ObjectId == objectId) {
                    return item.Variable;
                }
            }
            return 0;
        }

        private static int Pool(Combatant c) => c.Health + c.Stamina;

        // ------------------------------------------------------------------ monsters shooting

        private const int EncounterNumber = 7;

        private static MonsterTurnResolver Shooter(System.Func<int, int> roll) => new MonsterTurnResolver(
            _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: false, canShoot: true,
                crossbowAccuracy: 99, crossbowPattern: 2),
            roll, fleeThresholds: null, isUnderground: false);

        /// <summary>
        /// A monster of <paramref name="creatureType"/> three tiles east of the member in the same row,
        /// its body container holding <paramref name="quarrels"/> and, optionally, an equipped crossbow.
        /// </summary>
        /// <remarks>
        /// The body is the monster's pack: the original binds actor_record to
        /// actorspawn_objfixed(100, slot, encounter), so it sits in zone 100 at (roster slot, encounter).
        /// </remarks>
        private static (CombatRuntime Runtime, GameSession Session, Combatant Member, Combatant Monster,
            List<int> Cues) MonsterShooterFight(int creatureType, byte quarrels, bool crossbow = true,
                int gridX = 6) {
            var stored = new List<SaveGameInventoryItemData>();
            if (crossbow) {
                stored.Add(new SaveGameInventoryItemData(CrossbowId, 100, (ushort)ItemFlags.Equipped));
            }
            if (quarrels > 0) {
                stored.Add(new SaveGameInventoryItemData(QuarrelId, quarrels, 0));
            }
            var body = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 100, minChapter: 1, maxChapter: 9,
                    worldItemId: 0, x: 0, y: EncounterNumber, actorNumber: 0),
                SaveGameContainerType.NpcInventory, numberOfItems: (byte)stored.Count, capacity: 20,
                dataTypes: 0, items: stored.ToArray(),
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

            var session = new GameSession();
            session.SetActorStatsForTest(PartyPosition, StatBlock());
            session.SetActiveParty(1, new byte[] { (byte)PartyPosition });
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(100, 0, new[] { body }),
            }), chapter: 1);
            session.SetRosterActorForTest(400, StatBlock(), creatureType, gridX: (byte)gridX, gridY: 4);

            var cues = new List<int>();
            var runtime = new CombatRuntime(session, null, Entries(), Objects(), playSfx: cues.Add);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 },
                encounterNumber: EncounterNumber);
            return (runtime, session, fight.Party[0], fight.Enemies[0], cues);
        }

        [Test]
        public void AMonsterShooterFiresAQuarrelFromItsOwnBody() {
            // *** EVERY SHOOTER IN THE GAME USED TO FORFEIT THIS TURN (TASK-502). *** ResolveEnemyTurn
            // logged Shoot as "decided but not carried out", and wiring it alone would not have fired
            // either: the quarrel count answered zero for every monster. The original counts
            // actor_record, which is the body a corpse is later looted from — corpses carry quarrels.
            var (runtime, session, member, monster, cues) = MonsterShooterFight(creatureType: 12, quarrels: 10);
            int before = Pool(member);

            MonsterTurnResolver.Decision decision =
                runtime.ResolveEnemyTurn(monster, Shooter(_ => 0), AlwaysLow);

            Assert.AreEqual(AiAction.Shoot, decision.Action);
            Assert.AreEqual((6, 4), (monster.X, monster.Y), "it shoots from where it stands");
            Assert.Less(Pool(member), before, "and the quarrel lands");
            Assert.AreEqual(9, QuarrelInventory.Count(session.GetLiveContainerAt(100, 0, EncounterNumber)),
                "spent from its own body");
            CollectionAssert.Contains(cues, RangedShotSound.CrossbowFiringCue,
                "its crossbow is found in the same body");
        }

        // combat_ai_take_turn's tail (CBTAI.C:373-381): a caster whose slots cast nothing advances
        // through combataipath_select_target, which walks and never swings (CMBTAI.C). Timirianya's
        // Pantathians, who cannot cast there, used to punch the party to death on arrival.
        [Test]
        public void ACasterWithNothingToCastClosesInButDoesNotSwing() {
            var (runtime, _, member, monster, _) = MonsterShooterFight(
                creatureType: 25, quarrels: 0, crossbow: false, gridX: 5);
            int before = Pool(member);
            var caster = new MonsterTurnResolver(
                _ => new MonsterTurnResolver.Profile(0, 100, canCastSpells: true, canShoot: false,
                    castingSkill: 50),
                n => n - 1, fleeThresholds: null, isUnderground: false);

            // High for the contact-parry roll, low after it: a swing, if one is made, lands.
            var calls = 0;
            MonsterTurnResolver.Decision decision = runtime.ResolveEnemyTurn(monster, caster,
                n => calls++ == 0 ? n - 1 : 0);

            Assert.AreEqual(AiAction.MeleeOrMove, decision.Fallback, "control: the caster's advance");
            Assert.AreEqual(1, CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y),
                "it closed in");
            Assert.AreEqual(before, Pool(member), "and did not swing");
        }

        [Test]
        public void AMonsterShooterWithNothingToFireClosesInInstead() {
            // Without a quarrel every crossbow attempt declines and the turn falls to the advance.
            // Standing still would be the old forfeit wearing a new reason.
            var (runtime, _, member, monster, _) = MonsterShooterFight(creatureType: 12, quarrels: 0);

            runtime.ResolveEnemyTurn(monster, Shooter(_ => 0), AlwaysLow);

            Assert.Less(CombatGrid.ChebyshevDistance(monster.X, monster.Y, member.X, member.Y), 3,
                "it walked toward the member");
        }

        [Test]
        public void ASpeciesHeavyShotHitsWholeWithItsLaunchAndImpactCues() {
            // combataiact_ranged_attack_turn: no to-hit roll, no armour, RNDR(0x14, 0x1d), cue 0x12
            // before the flight and 0x15 after it. 0x39 takes the heavy shot whatever the roll says;
            // the resolver's rolls sit at their ceilings so the abort roll lets it shoot.
            var (runtime, _, member, monster, cues) = MonsterShooterFight(
                creatureType: MonsterTurnRoutines.AlwaysHeavyCreature, quarrels: 0, crossbow: false);
            int before = Pool(member);

            runtime.ResolveEnemyTurn(monster, Shooter(n => n - 1), AlwaysLow);

            CollectionAssert.AreEqual(
                new[] { MonsterTurnRoutines.HeavyShotLaunchCue, MonsterTurnRoutines.HeavyShotImpactCue },
                cues);
            Assert.AreEqual(before - 0x14, Pool(member), "the floor of the band, taken whole");
        }

        [Test]
        public void AThrownRockNeedsNoQuarrel() {
            // CBTAIACT.C:188 calls combataiturn_ranged_attack(actor, target, 8) with no selection in
            // front of it. The quarrel selector refuses kind 8, so routing the rock through it would
            // leave every charging creature unable to throw anything.
            var (runtime, _, member, monster, cues) = MonsterShooterFight(
                creatureType: 0x1f, quarrels: 0, crossbow: false);
            int before = Pool(member);

            runtime.ResolveEnemyTurn(monster, Shooter(n => n - 1), AlwaysLow);

            Assert.Less(Pool(member), before);
            CollectionAssert.Contains(cues, RangedShotSound.RockImpactCue);
        }

        // ------------------------------------------------------------------ shooting

        [Test]
        public void AShotThatLandsTakesFromTheTargetsPool() {
            var (runtime, _, member, monster) = Fight((CrossbowId, 100), (QuarrelId, 10));
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveShot(member, monster, quarrelKind: 0, AlwaysLow));
            Assert.Less(Pool(monster), before);
        }

        [Test]
        public void TheCrossbowWearsEVENOnAMiss() {
            // *** THE ASYMMETRY A COPY OF THE MELEE PATH LOSES. *** The original's
            // cbstat_damage_equipped_items(attacker, 2, 0x100) is the routine's LAST statement,
            // outside the hit branch — so missing still costs the shooter condition. Melee wears the
            // weapon only on a hit, and putting both inside `if (hit)` is the easy mistake.
            var (runtime, session, member, monster) = Fight((CrossbowId, 100), (QuarrelId, 10));

            Assert.IsFalse(runtime.ResolveShot(member, monster, quarrelKind: 0, AlwaysHigh),
                "a maximum roll must miss a chance well under 100");
            Assert.Less(ConditionOf(session, CrossbowId), 100);
        }

        [Test]
        public void ACorpseIsNotShotAtAll() {
            var (runtime, session, member, monster) = Fight((CrossbowId, 100), (QuarrelId, 10));
            monster.Flags |= CombatantFlags.Dead;

            Assert.IsFalse(runtime.ResolveShot(member, monster, quarrelKind: 0, AlwaysLow));
            Assert.AreEqual(100, ConditionOf(session, CrossbowId),
                "a refused shot is not a miss — nothing is fired, so nothing wears");
        }

        // ------------------------------------------------------------------ casting

        // *** NOT 1. *** These tests want a spell with no per-spell handler, and spell 1 is Dannon's
        // Delusions — one of the five whose magnitude the original zeroes before delivery. Using it
        // as a stand-in "generic spell" made three of them assert that a spell which deals nothing
        // deals damage, and they only came apart when the zeroing was finally wired. Candle Glow has
        // no per-spell arm (CSPELL.C:1372-1455) and no SpellCastTail.HookFor, and is not Skyfire,
        // whose metal-gear rule lives in the magnitude itself.
        private const int OrdinarySpell = SpellIds.CandleGlow;

        private static Spell SpellOf(int damage, int targetingType = 1) =>
            new Spell("S") {
                TargetingType = targetingType,
                Calculation = SpellCalculation.FixedAmount,
                Damage = damage,
            };

        [Test]
        public void TheCastCostsEXACTLYThePower_soNothingReducesIt() {
            // *** THE COMBAT COST BYPASSES ARMOUR AND SHIELDS. *** SpellCasting.ApplyCost models the
            // FIELD path; the combat one hands the cost to apply-damage with armour off and the
            // shield flag set. Billing through the ordinary pipeline would let a well-armoured mage
            // cast at a discount.
            //
            // Asserted as an EXACT number rather than by comparing an armoured caster with a bare
            // one. That comparison was the first thing written here and it was a tautology: the
            // charge passes armorRating 0 unconditionally, so both sides were equal by construction
            // and the test passed with the armour reduction switched back on. A number fails.
            var (runtime, _, member, monster) = Fight((ArmorId, 100));
            int before = Pool(member);

            runtime.ResolveCast(member, monster, SpellOf(5), spellId: OrdinarySpell, power: 8, AlwaysLow);

            Assert.AreEqual(8, before - Pool(member),
                "the caster pays the power exactly — armour rating 40 must not discount it");
        }

        [Test]
        public void SpellDamageReachesTheTarget() {
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveCast(member, monster, SpellOf(9), spellId: OrdinarySpell, power: 4,
                AlwaysLow));
            Assert.Less(Pool(monster), before);
        }

        [Test]
        public void AVulnerableTargetTakesTheFLATMagnitudeAndNotDoubleIt() {
            // *** THE CALL-SITE TEST THE VULNERABILITY WIRING NEVER HAD. *** SpellAffinityWiringTests
            // exercised the table lookup and the arithmetic separately, so ResolveCast applying the
            // doubling at the WRONG POINT sailed through all of it for as long as it existed.
            //
            // cspell_resolve_cast doubles its COST and then calls cspell_compute_effect_magnitude on
            // the result. A FixedAmount spell ignores its cost, so a vulnerable target takes the
            // record's damage word unchanged. Doubling the computed magnitude instead — which this
            // method used to do — gives 18 here.
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            runtime.SpellWeakness = WeaknessListing(spellNumber: 1, creatureType: monster.ClassId);
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveCast(member, monster, SpellOf(9), spellId: OrdinarySpell, power: 4,
                AlwaysLow));

            Assert.AreEqual(9, before - Pool(monster),
                "the flat damage word, not twice it — the doubling belongs on the cost");
        }

        [Test]
        public void StrengthDrainDeliversNoDamageAtAllDespiteItsDamageWord() {
            // *** A SPELL CAN COMPUTE A MAGNITUDE THE ORIGINAL THROWS AWAY. *** Its handler zeroes
            // the magnitude before the delivery switch; the whole effect is the Strength transfer in
            // SpellCastRoutines. The record's damage field says otherwise and is a trap.
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveCast(member, monster, SpellOf(9),
                spellId: SpellIds.StrengthDrain, power: 4, AlwaysLow));

            Assert.AreEqual(before, Pool(monster), "the magnitude is zeroed, not delivered");
        }

        [Test]
        public void BaneOfBlackSlayersHitsCreatureTwentyTwoAndNothingElse() {
            // A restriction with no field to express it: 50-75 damage against exactly one creature
            // and nothing at all against every other. Zeroing unconditionally would break the spell;
            // not zeroing at all makes it a general-purpose nuke.
            var (runtime, _, member, slayer) = Fight((CrossbowId, 100));
            slayer.ClassId = (int)GameData.Resources.World.CreatureType.BlackSlayer;
            int slayerBefore = Pool(slayer);

            Assert.IsTrue(runtime.ResolveCast(member, slayer, SpellOf(9),
                spellId: SpellIds.BaneOfBlackSlayers, power: 4, AlwaysLow));
            Assert.Less(Pool(slayer), slayerBefore, "a black slayer takes the damage word");

            var (other, _, caster, bystander) = Fight((CrossbowId, 100));
            int bystanderBefore = Pool(bystander);

            Assert.IsTrue(other.ResolveCast(caster, bystander, SpellOf(9),
                spellId: SpellIds.BaneOfBlackSlayers, power: 4, AlwaysLow));
            Assert.AreEqual(bystanderBefore, Pool(bystander),
                "and anything else takes nothing, however large the damage word");
        }

        private static SpellAffinityTable WeaknessListing(int spellNumber, int creatureType) {
            var table = new SpellAffinityTable("test");
            for (var i = 0; i <= spellNumber; i++) {
                table.Spells.Add(new SpellAffinity { SpellNumber = i });
            }
            table.Spells[spellNumber].CreatureTypes.Add(creatureType);
            return table;
        }

        [Test]
        public void ATargetingTypeTwoCastHEALSTheTargetsPool() {
            // *** THE HEAL IS ITS OWN DELIVERY. *** SpellCastTail routes targeting type 2 to
            // combat_ai_resolve_hit, which moves the combined pool — it never reaches
            // combat_arena_apply_damage, whose `damage >= 1` gate is why the DAMAGE path cannot
            // restore anything. Getting this wrong the other way costs the game its ordinary heal.
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            monster.Health = 10;
            monster.Stamina = 0;
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveCast(member, monster, SpellOf(12, targetingType: 2),
                spellId: OrdinarySpell, power: 4, AlwaysLow));

            Assert.Greater(Pool(monster), before, "type 2 restores rather than damages");
        }

        [Test]
        public void AHealCannotTakeAnyonePastFourFifthsOfFull() {
            // Casting on someone already at the ceiling does nothing at all — while still costing.
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            int before = Pool(monster);
            int casterBefore = Pool(member);

            runtime.ResolveCast(member, monster, SpellOf(200, targetingType: 2), spellId: OrdinarySpell,
                power: 4, AlwaysLow);

            Assert.LessOrEqual(Pool(monster) - before, 200, "the ceiling, not the magnitude");
            Assert.Less(Pool(member), casterBefore, "and the caster paid for it either way");
        }

        [Test]
        public void AHealDoesNOTMoveTheTargetsPool() {
            // *** A NEGATIVE MAGNITUDE ON THE DAMAGE PATH IS A NO-OP, NOT A HEAL. ***
            // combat_arena_apply_damage gates its whole body on `damage >= 1`, so the original
            // simply does nothing with it. This test caught a real regression the moment the heal
            // delivery was added: passing an UNSIGNED magnitude here turned a
            // spell that does nothing into one that deals ten damage. (That absolute value is
            // gone now — the magnitude stays signed end to end.) The heal has its own
            // delivery — see ATargetingTypeTwoCastHEALSTheTargetsPool.
            var (runtime, _, member, monster) = Fight((CrossbowId, 100));
            int before = Pool(monster);

            Assert.IsTrue(runtime.ResolveCast(member, monster, SpellOf(-10, targetingType: 1),
                spellId: OrdinarySpell, power: 4, AlwaysLow),
                "a negative magnitude on the DAMAGE path lands and delivers nothing");
            Assert.AreEqual(before, Pool(monster));
        }

        [Test]
        public void AGroundAimedCastLandsWithNoTargetAtAll() {
            // SpellTargetingRules.CastsWithoutATarget: types 5, 6 and 8 reach the dispatcher with a
            // null target by design, so a null must be an ordinary outcome rather than a refusal.
            var (runtime, _, member, _) = Fight((CrossbowId, 100));
            int before = Pool(member);

            Assert.IsTrue(runtime.ResolveCast(member, null, SpellOf(9, targetingType: 5),
                spellId: OrdinarySpell, power: 4, AlwaysLow));
            Assert.Less(Pool(member), before, "and the caster is still billed for it");
        }
        [Test]
        public void StrengthDrainMovesStrengthFromTheTargetToTheCASTER() {
            // *** THE SECOND HALF IS INVISIBLE FROM THE SPELL RECORD. *** The record, the
            // description and the dispatcher all say "damage"; the routine takes Strength from the
            // target and GIVES it to the caster. That transfer is what makes the spell worth its
            // 10-20 cost against a strong enemy rather than being a plain debuff.
            //
            // THE TWO HALVES LAND IN DIFFERENT PLACES, which is the whole point of the charSlot
            // branch: the monster target's loss is a permanent attribute change and shows on its
            // stat, while the party caster's gain is a TIMED MODIFIER and shows only in the eight-
            // slot table. Asserting both against the same surface would fail whichever way round
            // the fixture was built.
            var (runtime, session, member, monster) = Fight((CrossbowId, 100));
            ActorStat targetStrength = runtime.StatsFor(monster)[(int)ActorAttribute.Strength];
            int targetBefore = targetStrength.Base;
            Assert.Greater(targetBefore, 0, "fixture precondition: the target has Strength to lose");

            // Damage word -1 makes the divisor 1, so the drain is exactly the cost invested.
            Assert.IsTrue(runtime.ResolveCast(member, monster, DrainSpell(),
                spellId: SpellIds.StrengthDrain, power: 4, AlwaysLow));

            Assert.Less(targetStrength.Base, targetBefore, "the monster target lost Strength");
            Assert.Greater(CasterStrengthModifier(session, member), 0,
                "and the party caster banked a timed Strength modifier");
        }

        [Test]
        public void TheDrainIsCLAMPEDToWhatTheTargetStillHas() {
            // Read back against CURRENT Strength before either write, so draining a nearly-spent
            // enemy gives the caster nearly nothing. Taking half the REQUESTED amount instead would
            // over-reward a cast aimed at someone who has little left.
            var (runtime, session, member, monster) = Fight((CrossbowId, 100));
            ActorStat targetStrength = runtime.StatsFor(monster)[(int)ActorAttribute.Strength];
            targetStrength.Base = 2;

            runtime.ResolveCast(member, monster, DrainSpell(), spellId: SpellIds.StrengthDrain,
                power: 40, AlwaysLow);

            // 40 was asked for; 2 was available, so the caster banks half of TWO, not half of forty.
            Assert.LessOrEqual(CasterStrengthModifier(session, member), 1);
        }

        private static Spell DrainSpell() => new Spell("S") {
            TargetingType = 1, Calculation = SpellCalculation.Special2, Damage = -1,
        };

        /// <summary>The Strength value sitting in the caster's timed-modifier table.</summary>
        private static int CasterStrengthModifier(GameSession session, Combatant caster) {
            var total = 0;
            for (var i = 0; i < ActorStatModifiers.SlotsPerCharacter; i++) {
                ActorStatModifiers.Slot slot =
                    session.StatModifiers[ActorStatModifiers.IndexOf(caster.ClassId, i)];
                if (!slot.IsEmpty
                    && ActorStatModifiers.Affects(slot, ActorAttribute.Strength)) {
                    total += slot.Value;
                }
            }
            return total;
        }

        // Chapter 8's power source is not a party-only rule: cspell_check_castable refuses every
        // spell to a caster with no equipped Crystal Staff (CSPELL.C:1581-1597), and the AI's picker
        // asks it too (CSPELL.C:289). Timirianya's Pantathians carry nothing, so they never cast there;
        // the port let them, and two of them with a Nethermander wiped the party at the Cup dwelling.
        [TestCase(7, 40)]
        [TestCase(8, -1)]
        public void ACreatureWithNoStaffHasNothingToCastWithInChapterEight(int chapter, int expected) {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(
                System.Array.Empty<SaveGameZoneContainerEntryData>()), chapter);
            var runtime = new CombatRuntime(session);
            var pantathian = new Combatant { PartySlot = 0, ClassId = 25, Health = 20, Stamina = 20 };

            Assert.AreEqual(expected, runtime.CastingBudget(pantathian));
        }
    }
}
