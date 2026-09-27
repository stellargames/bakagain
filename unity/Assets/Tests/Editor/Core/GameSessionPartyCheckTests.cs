namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The dialog party checks — global keys 40001..40013, which
    /// <c>evtcond_range_d_read_handler</c> answers one bespoke query at a time (EVTCOND.C:80-170).
    /// </summary>
    /// <remarks>
    /// <b>These are not a range test, whatever the shared key range suggests.</b> Check 3 counts
    /// slots of one object above a condition floor, check 4 is the armour repair scan, and 5, 12
    /// and 13 read containers at hard-coded world coordinates in zones the party is not standing
    /// in. Covered here are the five added for TASK-410; 1, 2, 9, 10 and 11 predate them.
    /// </remarks>
    [TestFixture]
    public class GameSessionPartyCheckTests {
        private const byte KingdomArmour = 0x30;   // check 3 counts THIS object
        private const byte Rations = 0x48;
        private const byte PoisonedRations = 0x49;
        private const byte SpoiledRations = 0x4a;
        private const byte Helmet = 0x31;

        // The coordinates EVTCOND.C hard-codes. Repeated here deliberately: a test that read them
        // from the constants it is checking would pass with both wrong.
        private static readonly (int Zone, int X, int Y)[] PoisonStashes = {
            (5, 0x16b2fb, 0x111547), (5, 0x16b2fb, 0x110f20), (5, 0x16b33a, 0x11083c),
        };
        private const int RationStashZone = 3, RationStashX = 0x13f560, RationStashY = 0xf4ba0;

        private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
            new ObjectInfo("O") { Number = KingdomArmour, Name = "kingdom armour",
                ObjectType = ObjectType.Armor },
            new ObjectInfo("O") { Number = Helmet, Name = "helm", ObjectType = ObjectType.Armor },
            new ObjectInfo("O") { Number = Rations, Name = "rations", ObjectType = ObjectType.Food },
            new ObjectInfo("O") { Number = PoisonedRations, Name = "poisoned",
                ObjectType = ObjectType.Food },
            new ObjectInfo("O") { Number = SpoiledRations, Name = "spoiled",
                ObjectType = ObjectType.Food },
        });

        private static SaveGameContainerData Pack(params (byte Id, byte Condition)[] items) {
            var slots = new SaveGameInventoryItemData[24];
            for (var i = 0; i < items.Length; i++) {
                slots[i] = new SaveGameInventoryItemData(items[i].Id, items[i].Condition, 0);
            }

            return new SaveGameContainerData(
                new SaveGameContainerLocationData(
                    zone: 0, minChapter: 0, maxChapter: 9, worldItemId: 0, x: 0, y: 0,
                    actorNumber: 1),                       // actor number 1 == party position 0
                SaveGameContainerType.Inventory, (byte)items.Length, 24,
                (SaveGameContainerDataType)0, slots,
                null, null, null, null, null, null);
        }

        private static SaveGameContainerData Stash(
            int zone, int x, int y, params (byte Id, byte Count)[] items) {
            var slots = new SaveGameInventoryItemData[8];
            for (var i = 0; i < items.Length; i++) {
                slots[i] = new SaveGameInventoryItemData(items[i].Id, items[i].Count, 0);
            }

            return new SaveGameContainerData(
                new SaveGameContainerLocationData(
                    zone: zone, minChapter: 0, maxChapter: 9, worldItemId: 0, x: x, y: y,
                    actorNumber: 0),
                SaveGameContainerType.FixedWorldItem, (byte)items.Length, 8,
                (SaveGameContainerDataType)0, slots,
                null, null, null, null, null, null);
        }

        private static GameSession World(params SaveGameContainerData[] containers) {
            var session = new GameSession();
            session.SetObjectInfo(Objects());
            var zones = new SaveGameZoneContainerEntryData[containers.Length];
            for (var i = 0; i < containers.Length; i++) {
                zones[i] = new SaveGameZoneContainerEntryData(
                    (short)containers[i].Location.Zone, 0, new[] { containers[i] });
            }

            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(zones), chapter: 1);
            session.SetActiveParty(1, new byte[] { 0 });
            return session;
        }

        // Check 6 (EVTCOND.C:120): Owyn's staff and Gorath's sword, both EQUIPPED. It is what
        // chapter 4's Sar-Sargoth doors forbid on; with no case the party was walled in for good.
        [Test]
        public void Check6_WantsOwynsStaffAndGorathsSwordInHand() {
            const byte Sword = 0x12, Staff = 0x03;
            const ushort Equipped = (ushort)ItemFlags.Equipped;
            SaveGameContainerData ArmedPack(int actorNumber, byte id, ushort flags) {
                var slots = new SaveGameInventoryItemData[24];
                slots[0] = new SaveGameInventoryItemData(id, 90, flags);
                return new SaveGameContainerData(
                    new SaveGameContainerLocationData(zone: 0, minChapter: 0, maxChapter: 9,
                        worldItemId: 0, x: 0, y: 0, actorNumber: (short)actorNumber),
                    SaveGameContainerType.Inventory, 1, 24, (SaveGameContainerDataType)0, slots,
                    null, null, null, null, null, null);
            }
            GameSession Party(ushort owynFlags, ushort gorathFlags) {
                GameSession session = World(ArmedPack(2, Sword, gorathFlags), ArmedPack(3, Staff, owynFlags));
                session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                    new ObjectInfo("O") { Number = Sword, Name = "sword", ObjectType = ObjectType.Sword },
                    new ObjectInfo("O") { Number = Staff, Name = "staff", ObjectType = ObjectType.Staff },
                }));
                session.SetActiveParty(2, new byte[] { 1, 2 });
                return session;
            }

            Assert.AreEqual(1, Party(Equipped, Equipped).GetGlobalValue(40006));
            Assert.AreEqual(0, Party(0, Equipped).GetGlobalValue(40006), "Owyn's staff still in the pack");
            Assert.AreEqual(0, Party(Equipped, 0).GetGlobalValue(40006), "Gorath's sword still in the pack");
        }

        [Test]
        public void Check3_CountsSlotsAboveTheConditionFloor_AndWantsMoreThanFive() {
            // `count_out > 5`, and count_out is one per SLOT at condition >= 0x46 (EVTCOND.C:42).
            GameSession six = World(Pack(
                (KingdomArmour, 70), (KingdomArmour, 100), (KingdomArmour, 70),
                (KingdomArmour, 99), (KingdomArmour, 80), (KingdomArmour, 70)));
            Assert.AreEqual(1, six.GetGlobalValue(40003), "six sound pieces is more than five");

            // Same six pieces, one of them a point below the floor: five, and five is not enough.
            GameSession five = World(Pack(
                (KingdomArmour, 69), (KingdomArmour, 100), (KingdomArmour, 70),
                (KingdomArmour, 99), (KingdomArmour, 80), (KingdomArmour, 70)));
            Assert.AreEqual(0, five.GetGlobalValue(40003), "condition 69 must not count");
        }

        [Test]
        public void Check4_IsTheRepairScan_EquippedOrNot() {
            Assert.AreEqual(1, World(Pack((Helmet, 40))).GetGlobalValue(40004), "a dented helm");
            Assert.AreEqual(0, World(Pack((Helmet, 100), (KingdomArmour, 100)))
                .GetGlobalValue(40004), "nothing below full condition");
            // Not armour: the walk tests the CATEGORY, so a battered ration stack is not damage.
            Assert.AreEqual(0, World(Pack((Rations, 1))).GetGlobalValue(40004));
        }

        [Test]
        public void Check5_NeedsAllThreeStashesTainted_NotAny() {
            // The original seeds its result to 1 and clears it on the first stash with none, so
            // "any" is the easy inversion — and it would fire the branch two stashes early.
            SaveGameContainerData[] all = {
                Stash(PoisonStashes[0].Zone, PoisonStashes[0].X, PoisonStashes[0].Y,
                    (PoisonedRations, 7)),
                Stash(PoisonStashes[1].Zone, PoisonStashes[1].X, PoisonStashes[1].Y,
                    (PoisonedRations, 7)),
                Stash(PoisonStashes[2].Zone, PoisonStashes[2].X, PoisonStashes[2].Y,
                    (PoisonedRations, 7)),
            };
            Assert.AreEqual(1, World(all).GetGlobalValue(40005));

            SaveGameContainerData[] twoOfThree = {
                all[0], all[1],
                Stash(PoisonStashes[2].Zone, PoisonStashes[2].X, PoisonStashes[2].Y,
                    (Rations, 7)),   // clean rations in the third
            };
            Assert.AreEqual(0, World(twoOfThree).GetGlobalValue(40005));
        }

        [Test]
        public void Check12_AnswersTheCOUNT_AndCheck13AsksWhetherItIsTainted() {
            // 12 returns the count itself — `return res`, not `res != 0` — and charges count, so
            // two stacks of seven answer 14.
            GameSession clean = World(
                Stash(RationStashZone, RationStashX, RationStashY, (Rations, 7), (Rations, 7)));
            Assert.AreEqual(14, clean.GetGlobalValue(40012));
            Assert.AreEqual(0, clean.GetGlobalValue(40013), "clean rations are not tainted");

            GameSession spoiled = World(
                Stash(RationStashZone, RationStashX, RationStashY, (SpoiledRations, 3)));
            Assert.AreEqual(0, spoiled.GetGlobalValue(40012), "spoiled is a different object");
            Assert.AreEqual(1, spoiled.GetGlobalValue(40013));
        }

        [Test]
        public void AStashOnlyInOBJFIXED_StillAnswers() {
            // The two-pass lookup: the save shadows the shipped file, and a container the save has
            // never carried must still resolve. Consulting only the save answers 0 here.
            GameSession session = World();
            session.FixedObjects = new FixedObjectSet("OBJFIXED.DAT") {
                Containers = {
                    Stash(RationStashZone, RationStashX, RationStashY, (Rations, 5)),
                },
            };

            Assert.AreEqual(5, session.GetGlobalValue(40012));
        }

        [Test]
        public void ChecksNobodyAsks_FallThroughRatherThanAnswering() {
            // 7 and 8 are absent from the switch entirely. With no save state loaded, falling
            // through reads null — which is what tells them apart from a check that answered 0.
            // (6 used to be listed here as "no shipped dialog asks it". No DIALOG does — but five
            // chapter-4 door TRIGGERS forbid on it, and leaving it unanswered walled the party into
            // the Sar-Sargoth cells. See Check6_WantsOwynsStaffAndGorathsSwordInHand.)
            GameSession session = World(Pack((Helmet, 40)));

            Assert.IsNull(session.GetGlobalValue(40007));
            Assert.IsNull(session.GetGlobalValue(40008));
        }
    }
}
