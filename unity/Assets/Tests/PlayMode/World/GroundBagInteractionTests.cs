namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using NUnit.Framework;

    /// <summary>
    /// A dropped bag has to be interactable the moment it lands. This is a regression guard for a
    /// bug found by play-testing the discard (task-47): the interaction handler decided what a
    /// click says from the immutable save snapshot, which still describes a claimed bag's record as
    /// a parked free slot at zone 255 — so walking up to a pile you had just dropped answered "this
    /// must not be very important" (154) instead of opening it (158).
    /// </summary>
    public class GroundBagInteractionTests {
        // The zone table's "bag" row (entry 166), as ResourceExtraction builds it.
        private static readonly InteractionProfile BagProfile = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Bag },
            ExamineDialogId = 93, ActionDialogId = 158, NotActionableDialogId = 154,
            OpensLoot = true, HasLock = false,
        };

        private static SaveGameContainerData FreeSlot() =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 255, minChapter: 0, maxChapter: 10,
                    worldItemId: 0, x: 0, y: 0, actorNumber: 0),
                SaveGameContainerType.Free, numberOfItems: 0, capacity: 20,
                dataTypes: SaveGameContainerDataType.Timestamp | SaveGameContainerDataType.SelfSpawn,
                items: new SaveGameInventoryItemData[20],
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: 0, globalStateIndex: null);

        private static GameSession SessionWithPool() {
            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { FreeSlot() }),
            }), chapter: 1);
            session.CurrentZone = 1;
            return session;
        }

        [Test]
        public void ADroppedBag_IsFoundByTheLiveLookup_AndOpensRatherThanShrugging() {
            GameSession session = SessionWithPool();
            GroundDropTarget target = session.ResolveGroundDropTarget(1, 669600, 1064800);
            target.Container.Items.Add(new RuntimeItem(48, 1, 0));

            // What the snapshot says — the record is still a parked pool slot, so it has nothing
            // at this position. Deciding from this is the bug.
            Assert.IsNull(session.GetContainerAt(1, 669600, 1064800));

            RuntimeContainer live = session.GetLiveContainerAt(1, 669600, 1064800);
            Assert.NotNull(live, "the bag standing here must be visible to world interaction");
            Assert.AreEqual(SaveGameContainerType.Bag, live.ContainerType);

            Assert.AreEqual(158, InteractionDialogResolver.Resolve(
                BagProfile, live.ContainerType, live.DialogId, isPrimary: true));
            Assert.AreEqual(93, InteractionDialogResolver.Resolve(
                BagProfile, live.ContainerType, live.DialogId, isPrimary: false));
        }

        /// <summary>A parked pool slot is not something you can walk up to and open.</summary>
        [Test]
        public void AnUnclaimedPoolSlot_IsNotAnInteractableContainer() {
            GameSession session = SessionWithPool();

            Assert.IsNull(session.GetLiveContainerAt(255, 0, 0));
            Assert.AreEqual(154, InteractionDialogResolver.Resolve(
                BagProfile, null, null, isPrimary: true));
        }

        /// <summary>Once looted dry the bag is gone, so the spot answers "nothing here" again.</summary>
        [Test]
        public void AnEmptiedBag_StopsBeingInteractableAtThatSpot() {
            GameSession session = SessionWithPool();
            GroundDropTarget target = session.ResolveGroundDropTarget(1, 669600, 1064800);
            target.Container.Items.Add(new RuntimeItem(48, 1, 0));

            target.Container.Items.Clear();
            Assert.IsTrue(session.ReleaseGroundBagIfEmpty(target.Container));

            Assert.IsNull(session.GetLiveContainerAt(1, 669600, 1064800));
        }

        /// <summary>The overload taking a snapshot must keep answering exactly as it did — every
        /// authored corpse/well/chest still flows through it.</summary>
        [Test]
        public void SnapshotOverload_StillAgreesWithTheFieldOverload() {
            var corpse = new SaveGameContainerData(
                new SaveGameContainerLocationData(1, 1, 9, 195, 670423, 1059778, 0),
                SaveGameContainerType.Bag, 0, 4, 0, System.Array.Empty<SaveGameInventoryItemData>(),
                null, null, null, null, null, null);

            Assert.AreEqual(
                InteractionDialogResolver.Resolve(BagProfile, corpse, isPrimary: true),
                InteractionDialogResolver.Resolve(BagProfile, corpse.ContainerType,
                    corpse.DialogData?.DialogId, isPrimary: true));
        }
    }
}
