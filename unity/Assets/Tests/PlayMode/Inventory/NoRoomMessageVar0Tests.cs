namespace BakAgain.Tests.PlayMode.Inventory {
    using GameData.Resources.Data;
    using NUnit.Framework;

    /// <summary>
    /// Pins the coupling between <see cref="SaveGameContainerType"/> and the Var-0 leaves of the
    /// "doesn't fit" dialog record (DDX 1800008).
    ///
    /// <para><c>InventoryMenu.ShowTransferResult</c> passes the destination container's type byte
    /// straight through as Var 0, because that is literally what the original does —
    /// <c>sub_ovr157_4E3</c> @0x54be9 reads
    /// <c>es:[bx+container.metaData.containerType]</c> into <c>global_30000</c> (the Var-0 global)
    /// on the failed-transfer branch and then shows record 1800008.</para>
    ///
    /// <para>So the mapping is not a translation table we own — it is an identity, and it holds only
    /// as long as these byte values keep matching the record's branch conditions. Renumbering the
    /// enum would silently reroute the messages (a full corpse would start claiming it was a wooden
    /// chest) with nothing else failing, so the values are asserted here with the leaf text each one
    /// selects. If this test fails, re-check DDX 1800008's branches before "fixing" the numbers.</para>
    /// </summary>
    public class NoRoomMessageVar0Tests {
        // Every Var 0 that DDX 1800008 has an explicit branch for; anything else takes the default.
        private static readonly int[] BranchedVar0Values = { 1, 2, 3, 4, 5, 7, 10 };

        // Var 0 -> the leaf DDX 1800008 selects. Verified against generated/DDX/DIAL_Z18.json:
        // branches exist for 1, 2, 3, 4, 5, 7, 10 plus an unconditional default.
        [TestCase(SaveGameContainerType.Inventory, 1, "\"waved off the ...\" — a party member refusing")]
        [TestCase(SaveGameContainerType.Bag, 2, "the canvas sack")]
        [TestCase(SaveGameContainerType.Chest, 4, "the wooden chest")]
        [TestCase(SaveGameContainerType.Corpse, 5, "pawed the dead body")]
        [TestCase(SaveGameContainerType.NpcInventory, 7, "pawed the dead body (shares the corpse leaf)")]
        public void ContainerType_MatchesTheDdxVar0Leaf(SaveGameContainerType type, int expectedVar0,
            string leaf) {
            Assert.AreEqual(expectedVar0, (int)type,
                "SaveGameContainerType." + type + " must stay " + expectedVar0 +
                " — DDX 1800008 routes that Var 0 to: " + leaf);
        }

        // These have no branch of their own and must fall to the record's default leaf ("there isn't
        // enough room"). Listed explicitly so that adding a branch for one is a deliberate act.
        [TestCase(SaveGameContainerType.Free)]
        [TestCase(SaveGameContainerType.FixedWorldItem)]
        [TestCase(SaveGameContainerType.SharedKeys)]
        [TestCase(SaveGameContainerType.ScriptedLoot)]
        public void UnmodelledKinds_FallToTheDefaultLeaf(SaveGameContainerType type) {
            int var0 = (int)type;
            CollectionAssert.DoesNotContain(BranchedVar0Values, var0,
                "SaveGameContainerType." + type + " (" + var0 + ") now collides with a Var-0 branch " +
                "of DDX 1800008, so it would silently render that kind's message instead of the default.");
        }
    }
}
