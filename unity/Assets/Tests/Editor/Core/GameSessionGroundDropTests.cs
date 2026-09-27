namespace BakAgain.Tests.Editor.Core {
    using BakAgain.Core;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The session half of the discard-to-ground path (task-47): resolving where a dropped item
    /// goes, and keeping the location index honest as pool slots are claimed and released.
    /// </summary>
    [TestFixture]
    public class GameSessionGroundDropTests {
        private const SaveGameContainerDataType PoolFlags =
            SaveGameContainerDataType.Timestamp | SaveGameContainerDataType.SelfSpawn;

        // A parked pool record, exactly as every travel zone ships them: zone 255, (0,0),
        // capacity 20, chapter band 0..10, timestamp + self-spawn set.
        private static SaveGameContainerData FreeSlot() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 255, minChapter: 0, maxChapter: 10,
                    worldItemId: 0, x: 0, y: 0, actorNumber: 0),
                SaveGameContainerType.Free, numberOfItems: 0, capacity: 20, dataTypes: PoolFlags,
                items: new SaveGameInventoryItemData[20],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: 0, globalStateIndex: null);

        private static SaveGameContainerData Chest(int x, int y) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 3, minChapter: 1, maxChapter: 9,
                    worldItemId: 91, x: x, y: y, actorNumber: 0),
                SaveGameContainerType.Chest, numberOfItems: 0, capacity: 6, dataTypes: 0,
                items: new SaveGameInventoryItemData[6],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

        private static GameSession SessionWith(params SaveGameContainerData[] zone3) {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(3, 0, zone3),
            }), chapter: 1);
            session.CurrentZone = 3;
            return session;
        }

        // Hand-placed loot: residence RES_SELF_SPAWN, drawn with the box row, no WLD placement.
        private static SaveGameContainerData Loot(int x, int y, int minChapter, int maxChapter) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 3, minChapter: minChapter, maxChapter: maxChapter,
                    worldItemId: 162, x: x, y: y, actorNumber: 0),
                SaveGameContainerType.ScriptedLoot, numberOfItems: 0, capacity: 2,
                dataTypes: SaveGameContainerDataType.Lock,
                items: new SaveGameInventoryItemData[2],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

        /// <summary>
        /// ACTSPAWN.C:83 spawns a RES_SELF_SPAWN record when the chapter is inside its band. That is
        /// the only thing that puts the DIE chest south of Yabon in the world (TASK-557): a plain
        /// chest is a WLD placement and not this pass's business, and a record banded to another
        /// chapter stays out.
        /// </summary>
        [Test]
        public void SelfSpawnedLoot_IsTheScriptedLootInsideItsChapterBand() {
            GameSession session = SessionWith(
                Loot(669600, 992800, minChapter: 0, maxChapter: 10),
                Loot(672800, 876000, minChapter: 2, maxChapter: 2),
                Chest(700000, 900000));

            List<RuntimeContainer> loot = session.SelfSpawnedLootInZone(3);

            Assert.AreEqual(1, loot.Count);
            Assert.AreEqual(669600, loot[0].X);
            Assert.AreEqual(992800, loot[0].Y);
            Assert.AreEqual(162, loot[0].WorldItemId);
            Assert.AreEqual(0, session.SelfSpawnedLootInZone(1).Count, "another zone's list is not this zone's");
        }

        /// <summary>Free pool records all park at zone 255 / (0,0). Indexing them by location would
        /// collapse them onto one key and lose every slot but the last, so they must stay out of it
        /// and remain reachable through the per-zone scan.</summary>
        [Test]
        public void FreePoolRecords_AreNotCollapsedIntoTheLocationIndex() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot(), FreeSlot());

            Assert.IsNull(session.GetRuntimeContainerAt(255, 0, 0),
                "a parked pool slot is not a container standing at (255,0,0)");
            Assert.AreEqual(3, session.PoolRecordsInZone(3).Count,
                "all three slots must survive as claimable records");
        }

        [Test]
        public void ResolveGroundDropTarget_ClaimsAFreeSlotAndPlacesItAtThePartysFeet() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot());
            session.GameTimeIn2Seconds = 4242;

            GroundDropTarget target = session.ResolveGroundDropTarget(3, 1000, 2000);

            Assert.IsTrue(target.Resolved);
            Assert.IsTrue(target.IsNewBag, "a claimed slot is a new bag, so the drop narrates it");
            Assert.IsFalse(target.Recycled);
            Assert.AreEqual(SaveGameContainerType.Bag, target.Container.ContainerType);
            Assert.AreEqual(3, target.Container.Zone);
            Assert.AreEqual(1000, target.Container.X);
            Assert.AreEqual(2000, target.Container.Y);
            Assert.AreEqual(4242, target.Container.Timestamp);
            Assert.IsTrue(target.Container.HeaderDirty, "the claim rewrote the record's identity");

            Assert.AreSame(target.Container, session.GetRuntimeContainerAt(3, 1000, 2000),
                "the claimed bag must be findable where it now stands — that is how the world "
                + "interaction path loots it");
        }

        /// <summary>The engine's first pass (<c>actorspawn_objfixed</c>) finds a container already
        /// on the spot and simply transfers into it, leaving the pool untouched.</summary>
        [Test]
        public void ResolveGroundDropTarget_MergesIntoAContainerAlreadyOnThatSpot() {
            GameSession session = SessionWith(Chest(500, 600), FreeSlot());

            GroundDropTarget target = session.ResolveGroundDropTarget(3, 500, 600);

            Assert.IsTrue(target.Resolved);
            Assert.IsFalse(target.IsNewBag, "no spare bag is involved, so no bag line is played");
            Assert.AreEqual(SaveGameContainerType.Chest, target.Container.ContainerType);
            Assert.AreEqual(1, session.PoolRecordsInZone(3).FindAll(
                c => c.ContainerType == SaveGameContainerType.Free).Count);
        }

        [Test]
        public void ResolveGroundDropTarget_TwoDropsAtDifferentSpots_ClaimDistinctSlots() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot());

            GroundDropTarget first = session.ResolveGroundDropTarget(3, 10, 20);
            GroundDropTarget second = session.ResolveGroundDropTarget(3, 30, 40);

            Assert.AreNotSame(first.Container, second.Container);
            Assert.AreSame(first.Container, session.GetRuntimeContainerAt(3, 10, 20));
            Assert.AreSame(second.Container, session.GetRuntimeContainerAt(3, 30, 40));
        }

        /// <summary>Dropping again where a bag already sits must reuse it rather than burn a second
        /// pool slot — the first pass finds it by exact position.</summary>
        [Test]
        public void ResolveGroundDropTarget_SecondDropOnTheSameSpot_ReusesTheBagAlreadyThere() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot());

            GroundDropTarget first = session.ResolveGroundDropTarget(3, 10, 20);
            GroundDropTarget again = session.ResolveGroundDropTarget(3, 10, 20);

            Assert.AreSame(first.Container, again.Container);
            Assert.IsFalse(again.IsNewBag);
        }

        [Test]
        public void ResolveGroundDropTarget_PoolExhausted_RecyclesAndMovesTheBagsLocationKey() {
            GameSession session = SessionWith(FreeSlot());
            GroundDropTarget first = session.ResolveGroundDropTarget(3, 10, 20);
            Assert.IsTrue(first.Resolved);

            GroundDropTarget second = session.ResolveGroundDropTarget(3, 77, 88);

            Assert.IsTrue(second.Resolved);
            Assert.IsTrue(second.Recycled, "the only slot had to be reused");
            Assert.AreSame(first.Container, second.Container);
            Assert.IsNull(session.GetRuntimeContainerAt(3, 10, 20),
                "the recycled bag left its old spot; a stale key there would loot a pile that is "
                + "no longer standing");
            Assert.AreSame(second.Container, session.GetRuntimeContainerAt(3, 77, 88));
        }

        [Test]
        public void ResolveGroundDropTarget_ZoneWithNoPoolAtAll_IsUnresolvedSoTheDropIsRefused() {
            GameSession session = SessionWith(Chest(1, 2));

            GroundDropTarget target = session.ResolveGroundDropTarget(3, 900, 900);

            Assert.IsFalse(target.Resolved, "with nothing to claim the item must stay in the pack");
        }

        [Test]
        public void ReleaseGroundBagIfEmpty_FreesTheSlotAndUnindexesIt() {
            GameSession session = SessionWith(FreeSlot());
            GroundDropTarget target = session.ResolveGroundDropTarget(3, 10, 20);
            target.Container.Items.Add(new RuntimeItem(80, 1, 0));

            Assert.IsFalse(session.ReleaseGroundBagIfEmpty(target.Container),
                "a bag with loot in it stays on the ground");

            target.Container.Items.Clear();

            Assert.IsTrue(session.ReleaseGroundBagIfEmpty(target.Container));
            Assert.AreEqual(SaveGameContainerType.Free, target.Container.ContainerType);
            Assert.IsNull(session.GetRuntimeContainerAt(3, 10, 20));
            Assert.AreEqual(1, session.PoolRecordsInZone(3).FindAll(
                c => c.ContainerType == SaveGameContainerType.Free).Count,
                "the slot is back in the pool for the next drop");
        }

        [Test]
        public void GroundBagsInZone_ListsOnlyPlacedBags() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot(), Chest(1, 2));
            Assert.IsEmpty(session.GroundBagsInZone(3));

            session.ResolveGroundDropTarget(3, 10, 20);

            List<RuntimeContainer> bags = session.GroundBagsInZone(3);
            Assert.AreEqual(1, bags.Count);
            Assert.AreEqual(10, bags[0].X);
        }

        /// <summary>A claimed bag has to reach the save writer with its rewritten header, or the
        /// pile would be gone on reload while the pool slot stayed consumed.</summary>
        [Test]
        public void CollectDirtyContainerEdits_CarriesTheClaimedHeaderAndTimestamp() {
            GameSession session = SessionWith(FreeSlot());
            session.GameTimeIn2Seconds = 1234;
            GroundDropTarget target = session.ResolveGroundDropTarget(3, 0x1122, 0x3344);
            target.Container.Items.Add(new RuntimeItem(80, 1, 0));

            IReadOnlyList<DirtyContainerEdit> edits = session.CollectDirtyContainerEdits();

            Assert.AreEqual(1, edits.Count);
            DirtyContainerEdit edit = edits[0];
            Assert.NotNull(edit.HeaderBytes, "the claim changed the record's identity, not just items");
            Assert.AreEqual(3, edit.HeaderBytes[0]);                                  // zone
            Assert.AreEqual(0x0A, edit.HeaderBytes[1]);                               // chapters 0..10
            Assert.AreEqual((byte)SaveGameContainerType.Bag, edit.HeaderBytes[12]);
            Assert.AreEqual(20, edit.HeaderBytes[14], "capacity must not move — the record's size "
                + "is what keeps the save patch exact");
            Assert.AreEqual(1, edit.NumberOfItems);
            Assert.GreaterOrEqual(edit.TimestampOffset, 0);
            Assert.AreEqual(1234, edit.Timestamp);
        }

        /// <summary>
        /// The recycler destroys the least-recently-TOUCHED bag, so touching has to mean "last
        /// used", not "created" — otherwise a pile the player keeps returning to is the first one
        /// thrown away. Every content change marks the record dirty, and that is the moment the
        /// stamp moves.
        /// </summary>
        [Test]
        public void AGroundBagIsRestampedWheneverItsContentsChange() {
            GameSession session = SessionWith(FreeSlot());
            session.GameTimeIn2Seconds = 1000;
            GroundDropTarget target = session.ResolveGroundDropTarget(3, 1000, 2000);
            Assert.AreEqual(1000, target.Container.Timestamp, "claimed at 1000");

            // Hours later the player comes back and takes something out of the pile.
            session.GameTimeIn2Seconds = 5000;
            target.Container.Items.Add(new RuntimeItem(80, 1, 0));
            target.Container.Dirty = true;

            Assert.AreEqual(5000, target.Container.Timestamp,
                "the pile was last used at 5000, and that is what the recycler must order by");
        }

        [Test]
        public void TheOlderPileIsTheOneRecycled_EvenIfItWasClaimedSecond() {
            GameSession session = SessionWith(FreeSlot(), FreeSlot());
            session.GameTimeIn2Seconds = 100;
            GroundDropTarget first = session.ResolveGroundDropTarget(3, 10, 20);
            session.GameTimeIn2Seconds = 200;
            GroundDropTarget second = session.ResolveGroundDropTarget(3, 30, 40);

            // The player keeps using the FIRST pile, so the second is now the stale one.
            session.GameTimeIn2Seconds = 900;
            first.Container.Items.Add(new RuntimeItem(80, 1, 0));
            first.Container.Dirty = true;

            session.GameTimeIn2Seconds = 1000;
            GroundDropTarget third = session.ResolveGroundDropTarget(3, 50, 60);

            Assert.IsTrue(third.Recycled);
            Assert.AreSame(second.Container, third.Container,
                "the pile nobody has touched since it was made is the one to go");
        }

        /// <summary>A plain chest has no timestamp sub-record, so nothing to stamp and no header
        /// write — looting one must stay an item-only edit.</summary>
        [Test]
        public void AContainerWithNoTimestampSubRecordIsUnaffected() {
            GameSession session = SessionWith(Chest(500, 600));
            RuntimeContainer chest = session.GetRuntimeContainerAt(3, 500, 600);
            session.GameTimeIn2Seconds = 7777;

            chest.Items.Add(new RuntimeItem(80, 1, 0));
            chest.Dirty = true;

            Assert.IsNull(chest.Timestamp);
            Assert.IsFalse(chest.HeaderDirty);
        }

        /// <summary>Looting must not start writing headers: that path is unchanged and its edits
        /// stay item-only.</summary>
        [Test]
        public void CollectDirtyContainerEdits_PlainLootingStillPatchesItemsOnly() {
            GameSession session = SessionWith(Chest(500, 600));
            RuntimeContainer chest = session.GetRuntimeContainerAt(3, 500, 600);
            chest.Items.Add(new RuntimeItem(80, 1, 0));
            chest.Dirty = true;

            IReadOnlyList<DirtyContainerEdit> edits = session.CollectDirtyContainerEdits();

            Assert.AreEqual(1, edits.Count);
            Assert.IsNull(edits[0].HeaderBytes);
            Assert.AreEqual(-1, edits[0].TimestampOffset);
        }
    }
}
