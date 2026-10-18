namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>The Enhanced side-by-side loot (TASK-889) draws the passive panel with a second
    /// renderer on the same stage; its cells must not answer the screen's item_slot_N lookups.</summary>
    public class LootPeerRendererTests {
        [Test]
        public void APeerRenderer_NamesItsCellsWithItsOwnPrefix() {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("rock") { Number = 1, Icon = 0, InventorySlots = 1, MaxAmount = 1 },
            });
            var chest = new RuntimeContainer { ContainerType = SaveGameContainerType.Chest };
            chest.Items.Add(new RuntimeItem(1, 1, 0));
            var root = new VisualElement();
            new ItemGridRenderer { SlotPrefix = "loot_peer_" }.Render(root, chest, objects, resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(),
                new DesignFrame { Width = 1600, Height = 1200 });
            Assert.NotNull(root.Q("loot_peer_0"));
            Assert.IsNull(root.Q("item_slot_0"), "the live panel's lookups would find the picture's cell");
        }
    }
}
