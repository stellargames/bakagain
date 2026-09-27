namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Inventory;
    using NUnit.Framework;

    /// <summary>
    /// The container sub-actions — 5/6 (copy), 10 (two copies) and 14 (empty).
    /// </summary>
    /// <remarks>
    /// <b>The rule these exist to pin is that a copy is a COPY.</b>
    /// <c>itemuse_actor_spawn_clone_inv</c> writes the destination's list from the source's and
    /// then calls <c>actorspawn_destroy_and_persist</c>, which flushes the record and frees the
    /// in-memory actor — the source keeps everything. The enum called subtype 5 "MoveContainer …
    /// and dispose source", and implementing that name would strip a shop's stockroom every time a
    /// dialog restocked its shelf.
    /// </remarks>
    public class DialogContainerSubActionTests {
        private static SaveGameContainerData Chest(int zone, int x, int y, int items) {
            var slots = new SaveGameInventoryItemData[20];
            for (var i = 0; i < items; i++) {
                // Equipped (0x40) on every one, so the copy's flag clear is observable.
                slots[i] = new SaveGameInventoryItemData(
                    (byte)(10 + i), 100, (ushort)ItemFlags.Equipped);
            }

            return new SaveGameContainerData(
                new SaveGameContainerLocationData(
                    zone: zone, minChapter: 0, maxChapter: 10,
                    worldItemId: 0, x: x, y: y, actorNumber: 0),
                SaveGameContainerType.FixedWorldItem, numberOfItems: (byte)items, capacity: 20,
                dataTypes: SaveGameContainerDataType.Timestamp,
                items: slots,
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: 0, globalStateIndex: null);
        }

        private static (GameSession Session, DialogExecutor Executor) World(
            params SaveGameContainerData[] containers) {
            var session = new GameSession();
            var zones = new SaveGameZoneContainerEntryData[containers.Length];
            for (var i = 0; i < containers.Length; i++) {
                zones[i] = new SaveGameZoneContainerEntryData(
                    (short)containers[i].Location.Zone, 0, new[] { containers[i] });
            }

            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(zones), chapter: 1);
            return (session, new DialogExecutor(
                new NullLogger<DialogExecutor>(), session, new GameClock(session)));
        }

        private static void Run(DialogExecutor executor, SubActionType type) =>
            executor.ApplySubAction(new SubAction { SubActionType = type });

        [Test]
        public void RelocateContainers_CopiesBothPairsAndLeavesBOTHSourcesFull() {
            // *** THE SOURCE KEEPS EVERYTHING. *** "destroy_and_persist" frees an allocation; it
            // does not empty a container.
            (GameSession session, DialogExecutor executor) = World(
                Chest(0, 20, 1, 3), Chest(0, 30, 1, 2), Chest(15, 60, 3, 5), Chest(15, 64, 3, 4));

            Run(executor, SubActionType.RelocateContainers);

            Assert.AreEqual(3, session.GetLiveContainerAt(0, 20, 1).Items.Count, "source A emptied");
            Assert.AreEqual(2, session.GetLiveContainerAt(0, 30, 1).Items.Count, "source B emptied");
            // The destinations are OVERWRITTEN, not appended to: 5 and 4 become 3 and 2.
            Assert.AreEqual(3, session.GetLiveContainerAt(15, 60, 3).Items.Count);
            Assert.AreEqual(2, session.GetLiveContainerAt(15, 64, 3).Items.Count);
        }

        [Test]
        public void ACopiedItemStopsReadingAsEQUIPPED() {
            // flags &= 0xffbf. Something worn where it came from must not read as worn in a chest.
            (GameSession session, DialogExecutor executor) = World(
                Chest(0, 20, 1, 1), Chest(0, 30, 1, 0), Chest(15, 60, 3, 0), Chest(15, 64, 3, 0));

            Run(executor, SubActionType.RelocateContainers);

            RuntimeItem source = session.GetLiveContainerAt(0, 20, 1).Items[0];
            RuntimeItem copied = session.GetLiveContainerAt(15, 60, 3).Items[0];
            Assert.AreNotEqual(0, source.ItemFlags & (ushort)ItemFlags.Equipped,
                "the source item must be left exactly as it was");
            Assert.AreEqual(0, copied.ItemFlags & (ushort)ItemFlags.Equipped);
            Assert.AreEqual(source.ObjectId, copied.ObjectId);
        }

        [Test]
        public void TheCopyIsADEEPOne_soMendingTheOriginalDoesNotMendTheChest() {
            // Sharing the RuntimeItem instances would make the two containers alias each other,
            // and the aliasing would only show up much later, in whatever next writes a condition.
            (GameSession session, DialogExecutor executor) = World(
                Chest(0, 20, 1, 1), Chest(0, 30, 1, 0), Chest(15, 60, 3, 0), Chest(15, 64, 3, 0));

            Run(executor, SubActionType.RelocateContainers);
            session.GetLiveContainerAt(0, 20, 1).Items[0].Variable = 7;

            Assert.AreEqual(100, session.GetLiveContainerAt(15, 60, 3).Items[0].Variable);
        }

        [Test]
        public void EmptyTrapCache_ClearsItAndLeavesTheContainerInPlace() {
            (GameSession session, DialogExecutor executor) = World(Chest(3, 1308000, 1002400, 4));

            Run(executor, SubActionType.EmptyTrapCacheContainer);

            RuntimeContainer cache = session.GetLiveContainerAt(3, 1308000, 1002400);
            Assert.NotNull(cache, "the container itself must stay where it is");
            Assert.IsEmpty(cache.Items);
        }

        [Test]
        public void AMissingContainerIsSkippedRatherThanThrowing() {
            // The original dereferences both actors with no null check. Ours must not take a
            // conversation down when a chapter band excludes one of them — and subtypes 5 and 6
            // name a destination, (1, 2, 3), that exists in NO chapter of the shipped data.
            (GameSession session, DialogExecutor executor) = World(Chest(0, 20, 0, 3));

            Assert.DoesNotThrow(() => Run(executor, SubActionType.MoveContainer));
            Assert.DoesNotThrow(() => Run(executor, SubActionType.EmptyTrapCacheContainer));
            Assert.AreEqual(3, session.GetLiveContainerAt(0, 20, 0).Items.Count,
                "a copy that could not land must not have touched the source");
        }
    

        /// <summary>
        /// A dialog taking an item back removes exactly ONE, whatever `Amount` says.
        /// </summary>
        /// <remarks>
        /// The original sets both loop counters to 999 on the first match. `Amount` is the
        /// condition filter for object 120, not a quantity — treating it as one would strip a stack
        /// the author meant to thin by a single item, and 79 shipped instances go through here.
        /// </remarks>
        [Test]
        public void RemoveItem_TakesExactlyOne_evenWithAnAmount() {
            var session = new GameSession();
            session.SetActiveParty(1, new byte[] { 0 });
            session.SetActorInventoryForTest(0, PackOf(70, 70, 70));
            var executor = new DialogExecutor(
                new NullLogger<DialogExecutor>(), session, new GameClock(session));

            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new RemoveItemAction { ObjectId = 70, Amount = 3 } },
            });

            Assert.AreEqual(2, session.GetActorInventory(0).Items.Count,
                "Amount is a condition filter, not a count");
        }

        /// <summary>Object 120 is the one whose CONDITION has to match.</summary>
        /// <remarks>
        /// `item_id != 'x' || condition == nA2`, and 'x' is 120. For every other object the
        /// condition is ignored. Three shipped instances name 120, so this arm is reachable.
        /// </remarks>
        [Test]
        public void RemoveItem_MatchesTheConditionOnlyForObject120() {
            var session = new GameSession();
            session.SetActiveParty(1, new byte[] { 0 });
            var pack = new RuntimeContainer();
            pack.Items.Add(new RuntimeItem(120, 40, 0));
            pack.Items.Add(new RuntimeItem(120, 90, 0));
            session.SetActorInventoryForTest(0, pack);
            var executor = new DialogExecutor(
                new NullLogger<DialogExecutor>(), session, new GameClock(session));

            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new RemoveItemAction { ObjectId = 120, Amount = 90 } },
            });

            RuntimeContainer left = session.GetActorInventory(0);
            Assert.AreEqual(1, left.Items.Count);
            Assert.AreEqual(40, left.Items[0].Variable, "it must take the one whose condition matched");
        }

        private static RuntimeContainer PackOf(params byte[] objectIds) {
            var pack = new RuntimeContainer();
            foreach (byte id in objectIds) {
                pack.Items.Add(new RuntimeItem(id, 100, 0));
            }
            return pack;
        }

        /// <summary>
        /// UseItem takes `Amount` of them — the OPPOSITE of RemoveItem, which takes one.
        /// </summary>
        /// <remarks>
        /// The two actions carry the same two fields and read alike. `case 23` loops
        /// `for (n = 0; n < nA2; n++) consume_one(nA1)` while `case 3` stops after the first match
        /// however large its Amount. Carrying one rule across to the other is the obvious mistake,
        /// so the pair is asserted together: three taken here, one taken there.
        /// </remarks>
        [Test]
        public void UseItem_TakesAmountOfThem_whereRemoveItemTakesOne() {
            var session = new GameSession();
            session.SetActiveParty(1, new byte[] { 0 });
            // *** ONE STACK OF FIVE, NOT FIVE ITEMS. *** Variable is the stack COUNT for a
            // consumable, so five entries of 100 would be five stacks of a hundred and consuming
            // three would leave all five entries in place — which is exactly how the first version
            // of this test failed against correct code.
            var stack = new RuntimeContainer();
            stack.Items.Add(new RuntimeItem(70, 5, 0));
            session.SetActorInventoryForTest(0, stack);
            var executor = new DialogExecutor(
                new NullLogger<DialogExecutor>(), session, new GameClock(session));

            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new UseItemAction { ObjectId = 70, Amount = 3 } },
            });
            Assert.AreEqual(2, session.GetActorInventory(0).Items[0].Variable,
                "UseItem takes Amount of them");

            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new RemoveItemAction { ObjectId = 70, Amount = 3 } },
            });
            Assert.IsEmpty(session.GetActorInventory(0).Items,
                "RemoveItem takes the whole entry once, whatever Amount says");
        }

        /// <summary>Consuming names the member who actually supplied it.</summary>
        /// <remarks>
        /// itemtbl_pty_consum_one_kind sets nEvtArgActor0 to the first member up front and
        /// overwrites it with whoever had one, so a following line can speak about the character who
        /// paid. Asserting the DEFAULT too, since a test that only checks the supplier would pass
        /// with the field never written at all when the supplier happens to be first.
        /// </remarks>
        [Test]
        public void UseItem_NamesTheSupplierAsTheEventActor() {
            var session = new GameSession();
            session.SetActiveParty(2, new byte[] { 0, 1 });
            session.SetActorInventoryForTest(0, new RuntimeContainer());   // first member has none
            var theirs = new RuntimeContainer();
            theirs.Items.Add(new RuntimeItem(70, 1, 0));                   // one, so it runs out
            session.SetActorInventoryForTest(1, theirs);
            var executor = new DialogExecutor(
                new NullLogger<DialogExecutor>(), session, new GameClock(session));

            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new UseItemAction { ObjectId = 70, Amount = 1 } },
            });
            Assert.AreEqual(1, session.EventActor, "the member who actually had one");

            // Nobody has any now: the actor falls back to the first member rather than sticking.
            executor.ApplyEntryGameplayActionsForTest(new GameData.Resources.Dialog.DialogEntry {
                Actions = { new UseItemAction { ObjectId = 70, Amount = 1 } },
            });
            Assert.AreEqual(0, session.EventActor, "the default is the first member");
        }
}
}
