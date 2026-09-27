namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The grid contributes its cells to the screen's nav model. In the original the item cells ARE
    /// menu entries — cmbinv_combat_encounter_begin appends one per item to the same page->pEntries
    /// list as the chrome hotspots (canassa CMBINV.C:108, 142) — so they must be NavWidgets, not a
    /// parallel mechanism.
    /// </summary>
    public class ItemGridRendererNavTests {
        private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
            new ObjectInfo("a") { Number = 90, Name = "Thing", InventorySlots = 1, ObjectType = ObjectType.Misc },
        });

        private static RuntimeContainer Container(int items) {
            var c = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Chest };
            for (int i = 0; i < items; i++) { c.Items.Add(new RuntimeItem(90, 1, 0)); }
            return c;
        }

        private static DesignFrame Frame() =>
            new DesignFrame { Width = 1600, Height = 1200, Fit = LayoutFit.Contain };

        [Test]
        public void Render_EmitsOneNavWidgetPerCell_InPackOrder() {
            var root = new VisualElement();
            var widgets = new List<NavWidget>();

            new ItemGridRenderer().Render(root, Container(3), Objects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(), Frame(),
                navWidgets: widgets);

            Assert.AreEqual(3, widgets.Count);
            for (int i = 0; i < 3; i++) {
                Assert.AreEqual($"item_slot_{i}", widgets[i].Element.name,
                    "widget order must be the renderer's pack order");
            }
        }

        [Test]
        public void EmittedWidgets_AreFocusable_AndCarryNoLabelOrSecondary() {
            var root = new VisualElement();
            var widgets = new List<NavWidget>();

            new ItemGridRenderer().Render(root, Container(1), Objects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(), Frame(),
                navWidgets: widgets);

            Assert.IsTrue(widgets[0].Element.focusable);
            // The original numbers the first item entry 0x80 and recovers the index as id - 0x80.
            Assert.AreEqual(0x80, widgets[0].ActionId);
            // No label: the first-letter accelerator matches labels, and an item entry has none in
            // the original. No Secondary: there is no keyboard route to a secondary action, and the
            // original's inspect needs the right-drag state a keyboard cannot produce.
            Assert.IsNull(widgets[0].Label);
            Assert.IsNull(widgets[0].Secondary);
        }

        [Test]
        public void EmittedWidgets_PrimaryReportsTheSlot() {
            var root = new VisualElement();
            var widgets = new List<NavWidget>();
            int activated = -1;

            new ItemGridRenderer().Render(root, Container(3), Objects(), resources: null,
                onSlotPicked: slot => activated = slot, InventoryLayoutMode.Loot,
                new InventoryLayout(), Frame(), navWidgets: widgets);
            widgets[2].Primary();

            Assert.AreEqual(2, activated);
        }

        [Test]
        public void EmittedWidgets_PrimaryReportsTheSlot_ViaOnSlotActivated_TheProductionWiring() {
            // The live inventory screen wires onSlotPicked: null (so the Clickable manipulator is
            // never attached — see the Render XML doc — leaving the screen's own drag-capture
            // pointer handling unopposed) and passes onSlotActivated instead, for Enter/accelerator
            // on a focused cell. EmittedWidgets_PrimaryReportsTheSlot above only proves the fallback
            // (onSlotPicked alone, a bare test harness); this is the actual production wiring.
            var root = new VisualElement();
            var widgets = new List<NavWidget>();
            int activated = -1;

            new ItemGridRenderer().Render(root, Container(3), Objects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(), Frame(),
                navWidgets: widgets, onSlotActivated: slot => activated = slot);
            widgets[2].Primary();

            Assert.AreEqual(2, activated);
        }

        [Test]
        public void Render_ClearsThePreviousWidgets_SoARerenderDoesNotAccumulate() {
            var root = new VisualElement();
            var widgets = new List<NavWidget>();
            var renderer = new ItemGridRenderer();

            renderer.Render(root, Container(3), Objects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Loot, new InventoryLayout(), Frame(), navWidgets: widgets);
            renderer.Render(root, Container(2), Objects(), resources: null, onSlotPicked: null,
                InventoryLayoutMode.Loot, new InventoryLayout(), Frame(), navWidgets: widgets);

            Assert.AreEqual(2, widgets.Count);
        }

        [Test]
        public void CellRect_IsZero_BeforeLayout_RatherThanNaN() {
            // worldBound is NaN until a layout pass; a NaN rect would make Contains() and the
            // spatial-nav distance maths silently wrong, so it degrades to empty instead.
            var root = new VisualElement();
            var widgets = new List<NavWidget>();

            new ItemGridRenderer().Render(root, Container(1), Objects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(), Frame(),
                navWidgets: widgets);
            Rect r = widgets[0].CanonicalRect;

            Assert.IsFalse(float.IsNaN(r.x) || float.IsNaN(r.y)
                || float.IsNaN(r.width) || float.IsNaN(r.height));
        }
    }
}
