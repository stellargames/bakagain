namespace BakAgain.Tests.PlayMode.World {
    using System;
    using BakAgain.World.Interaction;
    using GameData.Resources.Data;
    using NUnit.Framework;

    public class ContainerInteractionHandlerTests {
        private static readonly InteractionProfile Chest = new() {
            ActionableContainerTypes = new[] { SaveGameContainerType.Chest, SaveGameContainerType.ScriptedLoot },
            OpensLoot = true, HasLock = true,
        };

        private static SaveGameContainerData Container(SaveGameContainerType type, SaveGameContainerLockData? lockData) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(1, 1, 9, 195, 670423, 1059778, 0),
                type, 0, 4, 0, Array.Empty<SaveGameInventoryItemData>(),
                lockData, null, null, null, null, null);

        private static SaveGameContainerData WithEncounter(SaveGameContainerEncounterData encounter) =>
            new SaveGameContainerData(
                new SaveGameContainerLocationData(1, 1, 9, 195, 670423, 1059778, 0),
                SaveGameContainerType.Chest, 0, 4, 0, Array.Empty<SaveGameInventoryItemData>(),
                null, null, null, encounter, null, null);

        /// <summary>
        /// The flag a container sets by being OPENED is its encounter subrecord's SECOND word.
        /// </summary>
        /// <remarks>
        /// <c>cmbinv_inventory_screen_run</c> (canassa SRC/SCREENS/CMBINV.C) writes
        /// <c>wGame_state_event_id</c> as the container screen comes up, and in the 9-byte
        /// <c>ActorSubrec08_HotspotAction</c> that word is our <c>GlobalDataKey2</c>. Brother
        /// Jeremy's box carries 56012 and he withholds Thiful's Bird Migrations until it is set.
        /// TASK-560.
        /// </remarks>
        [Test]
        public void AContainersOpenEventKeyIsItsSecondGlobalDataKey() {
            var encounter = new SaveGameContainerEncounterData(0, 56012, 0, 0, 0, 0, 0);
            Assert.AreEqual(56012, ContainerInteractionHandler.OpenEventKeyOf(WithEncounter(encounter)));
        }

        /// <summary>
        /// Key1 is NOT the open event — reading it as one would set the wrong flag.
        /// </summary>
        /// <remarks>
        /// The control that makes the test above mean something: our model documents Key2 by
        /// inheriting Key1's "the global whose value GATES this encounter", which is true of Key1
        /// and wrong of Key2. A shipped zone-7 ScriptedLoot carries Key1 8086 with no Key2, and
        /// opening it must set nothing.
        /// </remarks>
        [Test]
        public void TheFirstGlobalDataKeyIsNotTheOpenEvent() {
            var gateOnly = new SaveGameContainerEncounterData(8086, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual(0, ContainerInteractionHandler.OpenEventKeyOf(WithEncounter(gateOnly)));
        }

        [Test]
        public void AContainerWithNoEncounterSubrecordSetsNothing() {
            Assert.AreEqual(0, ContainerInteractionHandler.OpenEventKeyOf(
                Container(SaveGameContainerType.Chest, null)));
            Assert.AreEqual(0, ContainerInteractionHandler.OpenEventKeyOf(null));
        }

        [Test]
        public void OpenChest_Primary_ShowsOpenDdx_AndLoots() {
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, null), isPrimary: true);
            Assert.AreEqual(194, d.DialogId);
            Assert.IsTrue(d.OpenLoot);
        }

        /// <summary>
        /// A primary click on a plain locked chest opens the PICKLOCK SCREEN, and says nothing.
        /// </summary>
        /// <remarks>
        /// <b>This test used to expect ddx 91, and that was wrong.</b> handle_Container's click
        /// switch (case 2 @0x77469) calls the picklock screen DIRECTLY — no prompt, no describe.
        /// The 91/92/154/194/195 ids around it are the EXAMINE texts; the "unlock dialog" a player
        /// remembers is the screen itself, not a confirm.
        ///
        /// <para>Deliberate contract change, not a regression: the old expectation encoded the
        /// placeholder behaviour from before the screen existed.</para>
        /// </remarks>
        [Test]
        public void LockedChest_Primary_OpensTheLock_AndSaysNothing() {
            var locked = new SaveGameContainerLockData(0, 17, 0, 0);
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, locked), isPrimary: true);
            Assert.AreEqual(ContainerInteractionHandler.NoDialog, d.DialogId);
            Assert.IsTrue(d.OpensLock);
            Assert.IsFalse(d.OpenLoot);
        }

        [Test]
        public void LockedChest_Secondary_StillDescribes() {
            // The examine path is unchanged — that is where ddx 91 belongs.
            var locked = new SaveGameContainerLockData(0, 17, 0, 0);
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, locked), isPrimary: false);
            Assert.AreEqual(91, d.DialogId);
            Assert.IsFalse(d.OpensLock);
        }

        [Test]
        public void OnlyAPlainLockedChestOpensTheLock() {
            // A trapped or puzzle chest has its own arm in the original's switch; neither reaches
            // the picklock screen.
            var trapped = new SaveGameContainerLockData(0x04, 17, 0, 9);
            var puzzle = new SaveGameContainerLockData(0, 0, 35, 0);
            Assert.IsFalse(ContainerInteractionHandler.Decide(
                Chest, Container(SaveGameContainerType.Chest, trapped), isPrimary: true).OpensLock);
            Assert.IsFalse(ContainerInteractionHandler.Decide(
                Chest, Container(SaveGameContainerType.Chest, puzzle), isPrimary: true).OpensLock);
        }

        /// <summary>
        /// A primary click on a puzzle chest opens the CIPHER SCREEN, and says nothing.
        /// </summary>
        /// <remarks>
        /// <b>This test used to expect ddx 92, and it was wrong for the same reason ddx 91 was.</b>
        /// handle_Container's click switch (case 1 @0x77454) hands the puzzle id straight to
        /// UI_RunCipherPuzzle — no prompt, no describe — and stores its answer in the very flag
        /// that opens the loot. 92 is the EXAMINE text, which the secondary path still shows.
        ///
        /// <para>Deliberate contract change, not a regression: the old expectation encoded the
        /// placeholder behaviour from before the screen existed.</para>
        /// </remarks>
        [Test]
        public void PuzzleChest_Primary_OpensTheCipher_AndSaysNothing() {
            var puzzle = new SaveGameContainerLockData(0, 0, 35, 0);
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, puzzle), isPrimary: true);
            Assert.AreEqual(ContainerInteractionHandler.NoDialog, d.DialogId);
            Assert.IsTrue(d.OpensPuzzle);
            Assert.IsFalse(d.OpenLoot);
            Assert.IsFalse(d.OpensLock, "a puzzle chest has no lock to pick");
        }

        [Test]
        public void PuzzleChest_Secondary_StillDescribes() {
            // The examine path is unchanged — that is where ddx 92 belongs.
            var puzzle = new SaveGameContainerLockData(0, 0, 35, 0);
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, puzzle), isPrimary: false);
            Assert.AreEqual(92, d.DialogId);
            Assert.IsFalse(d.OpensPuzzle);
        }

        [Test]
        public void ATrappedChestOpensNEITHERScreen() {
            // Traps are their own arm (case 3) and are still unbuilt, so a trapped chest must not
            // fall into either of the two screens that now exist.
            var trapped = new SaveGameContainerLockData(0x04, 17, 0, 9);
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, trapped), isPrimary: true);
            Assert.IsFalse(d.OpensPuzzle);
            Assert.IsFalse(d.OpensLock);
        }

        [Test]
        public void NonActionableType_NotImportant_NoLoot() {
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.FixedWorldItem, null), isPrimary: true);
            Assert.AreEqual(154, d.DialogId);
            Assert.IsFalse(d.OpenLoot);
        }

        [Test]
        public void OpenChest_Examine_ShowsExamineDdx_NoLoot() {
            var d = ContainerInteractionHandler.Decide(Chest, Container(SaveGameContainerType.Chest, null), isPrimary: false);
            Assert.AreEqual(195, d.DialogId);
            Assert.IsFalse(d.OpenLoot);
        }
    }
}
