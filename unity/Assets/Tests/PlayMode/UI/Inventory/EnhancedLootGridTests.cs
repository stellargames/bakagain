namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced side-by-side loot (TASK-944): the pack beside the portraits and the loot beside the
    /// container window, each drawn over squares that are its real capacity. The capacity is the
    /// original's (<c>cmbinv_has_space_for_item</c>, CMBINV.C:624-659: 20 slots in a member's pack,
    /// 28 in anything else, items of 1/2/4 slots) and InventoryTransfer is its port; these tests hold
    /// the squares to that rule.
    /// </summary>
    public class EnhancedLootGridTests {
        private const byte Rock = 1, Sword = 2, Armour = 3;
        private static readonly DesignFrame Frame = new() { Width = 1600, Height = 1200 };

        private static readonly ObjectInfoSet Objects = new("objects", new List<ObjectInfo> {
            new("rock") { Number = Rock, InventorySlots = 1, MaxAmount = 1 },
            new("sword") { Number = Sword, InventorySlots = 2, MaxAmount = 1 },
            new("armour") { Number = Armour, InventorySlots = 4, MaxAmount = 1 },
        });

        private static RuntimeContainer Member() =>
            new() { ContainerType = SaveGameContainerType.Inventory, Capacity = 24 };

        private static RuntimeContainer Chest(int capacity = 200) =>
            new() { ContainerType = SaveGameContainerType.Chest, Capacity = capacity };

        /// <summary>Hands items over one at a time while the original's room test lets them in, sorted
        /// as the screen sorts before every render. Returns how many went in.</summary>
        private static int Fill(RuntimeContainer c, params byte[] ids) {
            int taken = 0;
            foreach (byte id in ids) {
                var item = new RuntimeItem(id, 1, 0);
                if (!InventoryTransfer.HasRoomFor(c, item, Objects)) continue;
                c.Items.Add(item);
                taken++;
            }
            InventoryOrder.Consolidate(c, Objects, c.ContainerType == SaveGameContainerType.Inventory);
            return taken;
        }

        private static VisualElement Render(RuntimeContainer c, InventoryLayoutMode mode, bool squares,
            ItemGridRenderer renderer = null) {
            var root = new VisualElement();
            (renderer ?? new ItemGridRenderer()).Render(root, c, Objects, resources: null, onSlotPicked: null,
                mode, new InventoryLayout(), Frame, squares: squares);
            return root;
        }

        private static List<VisualElement> Squares(VisualElement root) =>
            root.Query(className: ItemGridRenderer.SquareClass).ToList();

        private static int Cells(VisualElement root, int count) =>
            Enumerable.Range(0, count).Count(i => root.Q($"item_slot_{i}") != null);

        [Test]
        public void APack_HasTwentySquares_AndTheRuleRefusesTheTwentyFirstRock() {
            RuntimeContainer pack = Member();
            int taken = Fill(pack, Enumerable.Repeat(Rock, 30).ToArray());
            VisualElement root = Render(pack, InventoryLayoutMode.Member, squares: true);

            Assert.AreEqual(20, Squares(root).Count, "a member's pack is 20 slots");
            Assert.AreEqual(20, taken, "the original's room test takes 20 one-slot items and refuses the 21st");
            Assert.AreEqual(20, Cells(root, 20), "every item taken has a square");
        }

        [Test]
        public void APackFilledWithMixedSizes_ShowsEverythingTheRuleTook() {
            RuntimeContainer pack = Member();
            // Big things first, then small: the rule keeps four slots for one-slot items (the +4 in
            // pass 1), and the grid keeps its last column for them.
            int taken = Fill(pack, Armour, Armour, Armour, Sword, Sword, Sword, Rock, Rock, Rock, Rock, Rock,
                Rock, Rock, Rock);
            VisualElement root = Render(pack, InventoryLayoutMode.Member, squares: true);

            Assert.AreEqual(pack.Items.Count, taken);
            Assert.AreEqual(taken, Cells(root, taken), "an item the rule took is never off the grid");
            Assert.IsFalse(InventoryTransfer.HasRoomFor(pack, new RuntimeItem(Rock, 1, 0), Objects),
                "the pack is full by the rule");
        }

        [Test]
        public void ThePackSquares_AreTheGeneralGridBesideThePaperdoll() {
            VisualElement root = Render(Member(), InventoryLayoutMode.Member, squares: true);
            var layout = new InventoryLayout();
            float cw = layout.GridArea.Grid.CellWidth.Value;
            float left = layout.GridArea.Left.Value + layout.MemberShiftX.Value + 2 * cw; // past the paperdoll

            Rect bound = Bound(root, Squares(root));
            Assert.AreEqual(left, bound.xMin, 0.01f);
            Assert.AreEqual(5 * cw, bound.width, 0.01f, "five columns");
        }

        [Test]
        public void TheLoot_HasTheContainersTwentyEightSquares() {
            RuntimeContainer chest = Chest();
            Fill(chest, Rock, Sword);
            VisualElement root = Render(chest, InventoryLayoutMode.Loot, squares: true);

            Assert.AreEqual(28, Squares(root).Count, "anything not a member holds 28 slots");
            Assert.AreEqual(2, Cells(root, 2));
        }

        [Test]
        public void AnEmptyContainer_StillShowsItsSquares() {
            Assert.AreEqual(28, Squares(Render(Chest(), InventoryLayoutMode.Loot, squares: true)).Count);
        }

        [Test]
        public void TheLoot_GrowsRows_SoAnOverfullContainerHidesNothing() {
            // A container the game filled past the 28-slot budget: nine suits of armour are 36 slots.
            RuntimeContainer chest = Chest();
            for (int i = 0; i < 9; i++) chest.Items.Add(new RuntimeItem(Armour, 1, 0));
            VisualElement root = Render(chest, InventoryLayoutMode.Loot, squares: true);

            Assert.AreEqual(9, Cells(root, 9), "every item in the container is on the screen");
            Assert.GreaterOrEqual(Squares(root).Count, 36);
            Assert.AreEqual(0, Squares(root).Count % 7, "still the 7-column grid, only taller");
        }

        [Test]
        public void SquaresRect_BoundsTheSquares() {
            var renderer = new ItemGridRenderer();
            RuntimeContainer chest = Chest();
            Fill(chest, Rock);
            VisualElement root = Render(chest, InventoryLayoutMode.Loot, squares: true, renderer);
            Rect bound = Bound(root, Squares(root));

            Assert.AreEqual(bound.xMin, renderer.SquaresRect.xMin, 0.01f);
            Assert.AreEqual(bound.yMin, renderer.SquaresRect.yMin, 0.01f);
            Assert.AreEqual(bound.size, renderer.SquaresRect.size);
        }

        [Test]
        public void WithoutSquares_TheFaithfulGridDrawsNone() {
            RuntimeContainer chest = Chest();
            Fill(chest, Rock);
            Assert.AreEqual(0, Squares(Render(chest, InventoryLayoutMode.Loot, squares: false)).Count);
            Assert.AreEqual(0, Squares(Render(Member(), InventoryLayoutMode.Member, squares: false)).Count);
        }

        // The squares' bound in root space, from their inline placement (no layout pass needed).
        private static Rect Bound(VisualElement root, List<VisualElement> squares) {
            Rect r = default;
            bool any = false;
            foreach (VisualElement s in squares) {
                VisualElement host = s.parent;
                var cell = new Rect(host.style.left.value.value + s.style.left.value.value,
                    host.style.top.value.value + s.style.top.value.value,
                    s.style.width.value.value, s.style.height.value.value);
                r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, cell.xMin), Mathf.Min(r.yMin, cell.yMin),
                    Mathf.Max(r.xMax, cell.xMax), Mathf.Max(r.yMax, cell.yMax)) : cell;
                any = true;
            }
            return r;
        }

        // ---- the swap (part 1) ----------------------------------------------------------------

        [Test]
        public void TheLootIsOnTheContainerWindowsSide_ThePackOnThePortraits() {
            var go = new GameObject("InventoryMenuUnderTest");
            try {
                var menu = go.AddComponent<InventoryMenu>();
                const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
                var stage = new VisualElement();
                typeof(InventoryMenu).GetField("_stage", any).SetValue(menu, stage);
                typeof(InventoryMenu).GetMethod("EnsureHalves", any).Invoke(menu, null);
                var loot = (VisualElement)typeof(InventoryMenu).GetField("_lootHalf", any).GetValue(menu);
                var pack = (VisualElement)typeof(InventoryMenu).GetField("_packHalf", any).GetValue(menu);

                // The Enhanced bar puts the container window bottom left and the portraits in a column
                // on the right (EnhancedInventoryLayout, .enhanced-inv__bar / __portraits).
                Assert.Less(loot.style.left.value.value, pack.style.left.value.value);
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
