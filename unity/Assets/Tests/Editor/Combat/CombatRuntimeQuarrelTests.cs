namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData.Resources.Combat;
    using GameData.Resources.Data;
    using NUnit.Framework;

    /// <summary>
    /// What the SHOOT menu is built from: how many quarrels of each kind the acting character has.
    /// </summary>
    /// <remarks>
    /// <b>Against a real pack, not a stub.</b> The count comes out of the session's runtime
    /// containers, and the two things that can go wrong — reading the quantity field and mapping an
    /// object id to a kind — both live on that path. A test with no session would answer all zeroes
    /// and prove nothing.
    /// </remarks>
    public class CombatRuntimeQuarrelTests {
        // The pack of party position 0 (stored actor number 1). Quantity lives in the item's
        // Variable field, which is what ammunition counts read.
        private static GameSession SessionWithPack(params (int ObjectId, int Quantity)[] items) {
            var stored = new SaveGameInventoryItemData[items.Length];
            for (var i = 0; i < items.Length; i++) {
                // Quantity is the VARIABLE field (the second argument), which is exactly what makes
                // an empty quiver distinguishable from one arrow.
                stored[i] = new SaveGameInventoryItemData((byte)items[i].ObjectId,
                    (byte)items[i].Quantity, 0);
            }

            var pack = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 1, maxChapter: 9,
                    worldItemId: 0, x: 0, y: 0, actorNumber: 1),
                SaveGameContainerType.Inventory, numberOfItems: (byte)items.Length, capacity: 20,
                dataTypes: 0, items: stored,
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

            var session = new GameSession();
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { pack }),
            }), chapter: 1);
            return session;
        }

        private static Combatant PartyMemberAt(int partyPosition) => new Combatant {
            PartySlot = 1,
            ClassId = partyPosition,
            Health = 10,
            Stamina = 10,
            Flags = CombatantFlags.Ready,
        };

        [Test]
        public void CountsAreByKind_andTheObjectIdsAreNotInKindOrder() {
            // 0x2a is kind 3 and 0x27 is kind 4 — the two the id run swaps. A port that walked
            // 0x24..0x2b straight through would report these as each other's ammunition.
            var runtime = new CombatRuntime(SessionWithPack((0x2a, 6), (0x27, 9)));

            int[] counts = runtime.QuarrelsFor(PartyMemberAt(0));

            Assert.AreEqual(6, counts[3]);
            Assert.AreEqual(9, counts[4]);
            Assert.AreEqual(0, counts[2], "nothing else is carried");
        }

        [Test]
        public void AnEmptyQuiverCountsZero_notOne() {
            // Ammunition stores its quantity in the field an ordinary item leaves at zero, so a
            // count that treats an entry as one item hands the archer a shot they cannot take.
            var runtime = new CombatRuntime(SessionWithPack((0x24, 0)));

            Assert.AreEqual(0, runtime.QuarrelsFor(PartyMemberAt(0))[0]);
        }

        [Test]
        public void SameKindInTwoStacksIsSummed() {
            var runtime = new CombatRuntime(SessionWithPack((0x24, 5), (0x24, 7)));

            Assert.AreEqual(12, runtime.QuarrelsFor(PartyMemberAt(0))[0]);
        }

        [Test]
        public void AnEnemyCarriesNothing() {
            // Its ammunition is a monster-profile field, not a pack — and asking the session for
            // "party position ClassId" would hand back a party member's quiver.
            var runtime = new CombatRuntime(SessionWithPack((0x24, 5)));
            var monster = new Combatant { PartySlot = 0, ClassId = 0, Health = 10 };

            foreach (int count in runtime.QuarrelsFor(monster)) {
                Assert.AreEqual(0, count);
            }
        }

        [Test]
        public void EveryKindHasASlot() {
            var runtime = new CombatRuntime(SessionWithPack());

            Assert.AreEqual(QuarrelInventory.ObjectIdByKind.Length,
                runtime.QuarrelsFor(PartyMemberAt(0)).Length);
        }
    }
}
