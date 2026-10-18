namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>The Enhanced shop shelf's scroll arithmetic (TASK-890): the whole stock in one
    /// strip, a peek of the next row, drag/wheel offsets held inside the strip, More as a page.</summary>
    public class ShelfScrollTests {
        [Test]
        public void AShelfThatFits_IsNotShrunk() {
            Assert.AreEqual(1f, ShelfScroll.PeekScale(totalRows: 2, visibleRows: 2));
            Assert.AreEqual(1f, ShelfScroll.PeekScale(totalRows: 1, visibleRows: 2));
        }

        [Test]
        public void AShelfThatOverflows_ShrinksSoPartOfTheNextRowShows() {
            float s = ShelfScroll.PeekScale(totalRows: 5, visibleRows: 2);
            float rowsShown = 2f / s;
            Assert.Greater(rowsShown, 2.2f, "a visible slice of the third row");
            Assert.Less(rowsShown, 2.5f, "but no more than half of it");
        }

        [Test]
        public void TheOffset_StaysInsideTheStrip() {
            Assert.AreEqual(0f, ShelfScroll.Clamp(-50f, content: 1000f, viewport: 400f));
            Assert.AreEqual(600f, ShelfScroll.Clamp(900f, content: 1000f, viewport: 400f));
            Assert.AreEqual(250f, ShelfScroll.Clamp(250f, content: 1000f, viewport: 400f));
            Assert.AreEqual(0f, ShelfScroll.Clamp(80f, content: 300f, viewport: 400f), "nothing to scroll");
        }

        [Test]
        public void More_PagesDownThenWrapsToTheTop() {
            float a = ShelfScroll.Paged(0f, content: 1000f, viewport: 400f);
            Assert.Greater(a, 0f);
            Assert.LessOrEqual(a, 400f, "never skips a row unseen");
            float b = ShelfScroll.Paged(a, content: 1000f, viewport: 400f);
            float end = ShelfScroll.Paged(ShelfScroll.Paged(b, 1000f, 400f), 1000f, 400f);
            Assert.AreEqual(600f, ShelfScroll.Paged(550f, 1000f, 400f), "the last page stops at the end");
            Assert.AreEqual(0f, ShelfScroll.Paged(600f, 1000f, 400f), "from the end, back to the top");
            Assert.LessOrEqual(end, 600f);
        }

        [Test]
        public void Revealing_ScrollsJustEnoughToShowACell() {
            Assert.AreEqual(100f, ShelfScroll.Revealing(100f, top: 150f, bottom: 300f, viewport: 400f), "already shown");
            Assert.AreEqual(300f, ShelfScroll.Revealing(100f, top: 550f, bottom: 700f, viewport: 400f), "below: bottom edge");
            Assert.AreEqual(50f, ShelfScroll.Revealing(100f, top: 50f, bottom: 200f, viewport: 400f), "above: top edge");
        }
    

        private static ItemGridRenderer RenderShelf(int items, bool scrolls, out VisualElement root) {
            var objects = new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("rock") { Number = 1, Icon = 0, InventorySlots = 1, MaxAmount = 1 },
            });
            var shop = new RuntimeContainer { ContainerType = SaveGameContainerType.FixedWorldItem, IsShop = true };
            for (int i = 0; i < items; i++) shop.Items.Add(new RuntimeItem(1, 1, 0));
            root = new VisualElement();
            var renderer = new ItemGridRenderer();
            renderer.Render(root, shop, objects, resources: null, onSlotPicked: null, InventoryLayoutMode.Shop,
                new InventoryLayout(), new DesignFrame { Width = 1600, Height = 1200 }, shelfScrolls: scrolls);
            return renderer;
        }

        [Test]
        public void AScrollingShelf_HoldsTheWholeStockOnOneStrip() {
            ItemGridRenderer r = RenderShelf(10, scrolls: true, out VisualElement root);
            Assert.NotNull(r.ShelfContent);
            Assert.AreEqual(10, r.ShelfContent.childCount, "every item, not a page of six");
            Assert.AreEqual(4, r.ShelfRows, "ten items three across");
            Assert.AreEqual(2, r.ShelfVisibleRows);
            Assert.AreSame(r.ShelfViewport, r.ShelfContent.parent);
            Assert.NotNull(root.Q("item_slot_9"));
        }

        [Test]
        public void APagedShelf_StillShowsSix() {
            ItemGridRenderer r = RenderShelf(10, scrolls: false, out VisualElement root);
            Assert.IsNull(r.ShelfContent);
            Assert.NotNull(root.Q("item_slot_5"));
            Assert.IsNull(root.Q("item_slot_6"), "the faithful shelf pages");
        }
    }
}
