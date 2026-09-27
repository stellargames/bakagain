namespace BakAgain.Tests.Editor.Core {
    using System.Collections.Generic;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;

    /// <summary>
    /// TASK-510: carrying Weedwalkers (object 0x5a, Stealth +30) must show in the owner's stats, and
    /// losing them must take it away — <c>stat_actor_recalc_equip_bonuses</c>, which the original runs
    /// from <c>cmbinv_actor_pickup_item</c> for every item that reaches a party pack.
    /// </summary>
    public class GameSessionItemModifierTests {
        private const byte Weedwalkers = 0x5a;
        private const int Owner = 0;

        private static GameSession SessionCarrying(params byte[] objectIds) {
            var stored = new SaveGameInventoryItemData[objectIds.Length];
            for (var i = 0; i < objectIds.Length; i++) {
                stored[i] = new SaveGameInventoryItemData(objectIds[i], 1, 0);
            }
            var pack = new SaveGameContainerData(
                new SaveGameContainerLocationData(zone: 1, minChapter: 1, maxChapter: 9,
                    worldItemId: 0, x: 0, y: 0, actorNumber: Owner + 1),
                SaveGameContainerType.Inventory, numberOfItems: (byte)objectIds.Length, capacity: 20,
                dataTypes: 0, items: stored,
                lockData: null, dialogData: null, shopData: null, encounterData: null,
                timestamp: null, globalStateIndex: null);

            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }

            var session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("w") {
                    Number = Weedwalkers, Name = "Weedwalkers", InventorySlots = 1, MaxAmount = 1,
                    EquipAttributeMask = ActorAttributeFlag.Stealth, EquipModifierAmount = 30,
                },
            }));
            session.SetActorStatsForTest(Owner, stats);
            session.SetActiveParty(1, new byte[] { Owner });
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(1, 0, new[] { pack }),
            }), chapter: 1);
            return session;
        }

        private static int StealthModifier(GameSession session) =>
            session.StatsOf(Owner)[(int)ActorAttribute.Stealth].Modifier;

        [Test]
        public void LosingWeedwalkersTakesTheirStealthAway_andGettingThemBackRestoresIt() {
            GameSession session = SessionCarrying(Weedwalkers);
            RuntimeContainer pack = session.GetActorInventory(Owner);
            Assert.IsNotNull(pack, "fixture: the owner has a pack");

            session.RecalculateItemModifiers(Owner);
            Assert.AreEqual(30, StealthModifier(session), "carried Weedwalkers give +30 Stealth");

            Assert.IsTrue(InventoryConsume.TryConsumeOne(pack, Weedwalkers, session.ObjectInfo.GetById),
                "fixture: the pair is taken out of the pack");
            Assert.AreEqual(0, StealthModifier(session), "the bonus leaves with the item, with no extra call");

            pack.Items.Add(new RuntimeItem(Weedwalkers, 1, 0));
            pack.Items.Add(new RuntimeItem(Weedwalkers, 1, 0));
            pack.Dirty = true;
            Assert.AreEqual(30, StealthModifier(session), "two pairs still count once on the CD build");
        }
    }
}
