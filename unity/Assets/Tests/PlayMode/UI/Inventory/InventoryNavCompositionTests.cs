namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The screen's own entry list, mirroring cmbinv_combat_encounter_begin (canassa CMBINV.C:60):
    /// chrome first, then one entry per item. The catch-all is excluded because the original trims
    /// the live list to seven chrome entries plus the items — REQ entry 36 (action 128) is not live
    /// in the grid view at all, which is the same trim NeutralizeCatchAllHotspot models for picking.
    /// </summary>
    public class InventoryNavCompositionTests {
        private static NavWidget Chrome(int actionId) =>
            new NavWidget(new VisualElement { name = $"hotspot_{actionId}" }, null, Rect.zero,
                () => { }, null, actionId);

        private static NavWidget Item(int slot) =>
            new NavWidget(new VisualElement { name = $"item_slot_{slot}" }, null, Rect.zero,
                () => { }, null, 0x80 + slot);

        [Test]
        public void Compose_PutsChromeFirst_ThenGrid() {
            var chrome = new List<NavWidget> { Chrome(22), Chrome(2) };
            var grid = new List<NavWidget> { Item(0), Item(1) };

            IReadOnlyList<NavWidget> result = InventoryMenu.ComposeNavWidgets(chrome, grid);

            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("hotspot_22", result[0].Element.name);
            Assert.AreEqual("hotspot_2", result[1].Element.name);
            Assert.AreEqual("item_slot_0", result[2].Element.name);
            Assert.AreEqual("item_slot_1", result[3].Element.name);
        }

        [Test]
        public void Compose_DropsTheFullScreenCatchAll() {
            var chrome = new List<NavWidget> { Chrome(128), Chrome(22) };

            IReadOnlyList<NavWidget> result =
                InventoryMenu.ComposeNavWidgets(chrome, new List<NavWidget>());

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("hotspot_22", result[0].Element.name,
                "Tab must never land on the invisible 1600x1200 hotspot");
        }

        [Test]
        public void Compose_KeepsItemSlotZero_WhoseActionIdEqualsTheCatchAlls() {
            // 0x80 is both the catch-all's action and the first item's, so the filter must run
            // over chrome only. A filter over the whole list would silently eat slot 0.
            IReadOnlyList<NavWidget> result =
                InventoryMenu.ComposeNavWidgets(new List<NavWidget> { Chrome(128) },
                    new List<NavWidget> { Item(0) });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("item_slot_0", result[0].Element.name);
        }

        [Test]
        public void Compose_ToleratesNulls() {
            Assert.AreEqual(0, InventoryMenu.ComposeNavWidgets(null, null).Count);
        }
    }
}
