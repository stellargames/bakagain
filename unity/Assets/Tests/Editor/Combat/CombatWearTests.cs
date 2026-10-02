namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using MeleeAttack = GameData.Resources.Combat.CombatActionDispatch.MeleeAttack;
    using GameData.Resources.Data;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// Gear wearing out in a fight — the join between <see cref="ItemDegradation"/> and a swing.
    /// </summary>
    /// <remarks>
    /// The four wear constants had sat on <see cref="CombatFormulas"/> since August with <b>no
    /// caller anywhere</b>, so nothing in the game had ever degraded. These assert the item in the
    /// SAVE's pack, which is what a repair shop and a save both read.
    /// </remarks>
    public class CombatWearTests {
        private const byte SwordId = 20, ArmorId = 44;
        private const int PartyPosition = 0;

        private static ObjectInfoSet Objects(int swordChance = 100, int armourChance = 100) =>
            new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("O") {
                    Number = SwordId, Name = "sword", ObjectType = ObjectType.Sword,
                    Flags = ObjectFlags.Degradable, DegradeChancePercent = swordChance,
                    MaxWearPerDegrade = 1, ThrustBaseDamage = 8,
                    SwingAccuracy_ArmorMod_BowAccuracy = 10, InventorySlots = 1, MaxAmount = 1,
                },
                new ObjectInfo("O") {
                    Number = ArmorId, Name = "armour", ObjectType = ObjectType.Armor,
                    Flags = ObjectFlags.Degradable, DegradeChancePercent = armourChance,
                    MaxWearPerDegrade = 1, SwingAccuracy_ArmorMod_BowAccuracy = 4,
                    InventorySlots = 1, MaxAmount = 1,
                },
            });

        private static ActorStat[] StatBlock(byte health = 40) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = 99 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            stats[(int)ActorAttribute.Speed] = new ActorStat { Base = 5, Max = 99 };
            return stats;
        }

        /// <summary>A pack for party position 0 holding the given equipped items.</summary>
        private static GameSession SessionWith(params (byte ObjectId, byte Condition)[] items) {
            var stored = new SaveGameInventoryItemData[items.Length];
            for (var i = 0; i < items.Length; i++) {
                stored[i] = new SaveGameInventoryItemData(items[i].ObjectId, items[i].Condition,
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
            Fight(ObjectInfoSet objects, params (byte, byte)[] items) {
            GameSession session = SessionWith(items);
            session.SetRosterActorForTest(400, StatBlock(), 12, gridX: 5, gridY: 5);
            var runtime = new CombatRuntime(session, null, Entries(), objects);
            CombatEncounter fight = runtime.EnterRoster(new short[] { 400 });
            return (runtime, session, fight.Party[0], fight.Enemies[0]);
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

        [Test]
        public void ALandedSwingWearsTheATTACKERSWeapon() {
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);   // roll 0 hits and passes every gate

            Assert.Less(ConditionOf(session, SwordId), 100);
        }

        /// <summary>
        /// COMBAT.C:543 and :664: a melee blow that misses floats "miss" over the defender
        /// (dmgFloatValue 1 with a negative countdown). The port marked only hits, so a miss showed
        /// nothing. DamageFloat 0 is the port's "miss".
        /// </summary>
        [Test]
        public void AMissedBlowFloatsMissOverTheDefender() {
            (CombatRuntime runtime, GameSession _, Combatant member, Combatant monster) =
                Fight(Objects(), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 99);

            Assert.AreEqual(0, monster.DamageFloat, "a miss floats 'miss'");
        }

        [Test]
        public void ALandedBlowFloatsItsDamage() {
            (CombatRuntime runtime, GameSession _, Combatant member, Combatant monster) =
                Fight(Objects(), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.Greater(monster.DamageFloat ?? 0, 0);
        }

        [Test]
        public void AMissWearsNOTHING() {
            // Both halves are gated on the hit — the armour inside the to-hit roll, the weapon in
            // the caller's hit branch.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 99);

            Assert.AreEqual(100, ConditionOf(session, SwordId));
        }

        [Test]
        public void BeingHitWearsTheDEFENDERSArmour() {
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(), (ArmorId, 100));

            runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0);

            Assert.Less(ConditionOf(session, ArmorId), 100);
        }

        [Test]
        public void ABareHandedAttackerWearsNothingButStillScuffsTheArmour() {
            // `if (weapon_item != NULL)` guards the weapon half only. A monster has no inventory
            // weapon at all, so this is the ordinary case rather than an edge one.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(), (ArmorId, 100));

            Assert.DoesNotThrow(() => runtime.ResolveMelee(monster, member, MeleeAttack.Swing, _ => 0));
            Assert.Less(ConditionOf(session, ArmorId), 100);
        }

        [Test]
        public void AnItemWhoseDegradeChanceNeverPassesKeepsItsCondition() {
            // Most attacks wear nothing; gear lasting many fights is the rule, not a bug.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(swordChance: 0), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.AreEqual(100, ConditionOf(session, SwordId));
        }

        [Test]
        public void TheWornItemIsTheONEINTHEPACK_soASaveCarriesIt() {
            // Not a copy on the Combatant: a repair shop and the save writer both read the pack, and
            // wear that only reached a combatant would vanish when the fight ended.
            (CombatRuntime runtime, GameSession session, Combatant member, Combatant monster) =
                Fight(Objects(), (SwordId, 100));

            runtime.ResolveMelee(member, monster, MeleeAttack.Swing, _ => 0);

            Assert.IsTrue(session.GetActorInventory(PartyPosition).Dirty,
                "and the container is marked so the save writes it");
        }
    }
}
