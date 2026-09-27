namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using System;

    [TestFixture]
    public class GameSessionContainerTests {
        private static SaveGameContainerData Corpse() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 1, maxChapter: 9,
                    worldItemId: 195, x: 670423, y: 1059778, actorNumber: 0),
                (SaveGameContainerType)5, numberOfItems: 2, capacity: 4, dataTypes: 0,
                items: Array.Empty<SaveGameInventoryItemData>(),
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

        // Zone 0, (x=7, y=0) — where boot_party_state_load_from_temp (canassa BOOT.C:132) binds
        // g_gameState.shared_inventory. Its type is SharedKeys (the DOS RES_PICKLOCK_BUFFER
        // residence), NOT Inventory, which is exactly why it can't be found through the by-actor
        // member-pack index.
        private static SaveGameContainerData Keyring() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 0, minChapter: 0, maxChapter: 0,
                    worldItemId: 0, x: 7, y: 0, actorNumber: 7),
                SaveGameContainerType.SharedKeys, numberOfItems: 1, capacity: 20, dataTypes: 0,
                items: new[] { new SaveGameInventoryItemData(61, 1, 0) },
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

        [Test]
        public void SharedKeysInventory_ResolvesTheZoneZeroActorSevenContainer() {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(0, 0, new[] { Keyring() }),
            }), chapter: 1);

            GameData.Resources.Inventory.RuntimeContainer keys = session.SharedKeysInventory;

            Assert.NotNull(keys, "the party's shared keys inventory must be reachable");
            Assert.AreEqual(SaveGameContainerType.SharedKeys, keys.ContainerType);
            Assert.AreEqual(1, keys.Items.Count);
            Assert.AreEqual(61, keys.Items[0].ObjectId); // Peasant's Key, the chapter-1 starter
            Assert.IsNull(session.GetActorInventory(7),
                "type 8 is not a member pack, so it must stay out of the by-actor index");
        }

        [Test]
        public void SharedKeysInventory_IsNullWhenTheSaveHasNoSuchContainer() {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { Corpse() }),
            }), chapter: 1);

            Assert.IsNull(session.SharedKeysInventory,
                "callers must be able to see 'no keyring' — InventoryTransfer falls back to the "
                + "normal path rather than dropping the key");
        }

        [Test]
        public void GetContainerAt_ReturnsCorpseContainer() {
            var session = new GameSession();
            var containers = new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { Corpse() }),
            });
            // Arrange the session in chapter 1 with containers surfaced.
            session.SetZoneContainersForTest(containers, chapter: 1);

            SaveGameContainerData hit = session.GetContainerAt(1, 670423, 1059778);

            Assert.NotNull(hit);
            Assert.AreEqual(5, (int)hit.ContainerType);
        }

        private static SaveGameContainerData Stump(int minChapter, int maxChapter, int objectId) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 7, minChapter: minChapter, maxChapter: maxChapter,
                    worldItemId: 207, x: 803640, y: 917246, actorNumber: 0),
                SaveGameContainerType.FixedWorldItem, numberOfItems: 1, capacity: 4, dataTypes: 0,
                items: new[] { new SaveGameInventoryItemData((byte)objectId, 14, 0) },
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

        // Zone 7's stump at (803640,917246) carries two records told apart only by chapter band:
        // rations in chapter 7, Coltari Poison in every chapter. The original takes the first record
        // in file order whose band holds the chapter (ACTSPAWN.C:130-132); the location index kept
        // the LAST, so chapter 7's stump handed over poison and its 33 rations never appeared.
        [TestCase(7, 72)]
        [TestCase(3, 105)]
        public void TwoRecordsOnOneSpot_TheChapterPicksWhichOneOpens(int chapter, int expectedObject) {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(7, 0, new[] { Stump(7, 7, 72), Stump(0, 10, 105) }),
            }), chapter);

            Assert.AreEqual(expectedObject,
                session.GetRuntimeContainerAt(7, 803640, 917246).Items[0].ObjectId);
        }
    }
}
