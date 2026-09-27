namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections.Generic;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// Which number a grid cell shows, and when — <c>invui_grid_render</c>'s non-shop branch
    /// (INVENTOR.C:495-520, <c>UI_DrawInventory</c> @0x56caf-0x56d45).
    ///
    /// <para>The interesting case is the last one: an item with charges or condition shows its
    /// number <b>only while its slot is the highlighted one</b>, which is how you read a torch's
    /// uses left by selecting it. Everything else is either always shown or never shown.</para>
    /// </summary>
    public class ItemQuantityLabelTests {
        private const int Charged = 10;    // LimitedUses (0x2000) — a torch, a whetstone
        private const int Degradable = 11; // 0x1000 alone — condition, but only while selected
        private const int WornArmour = 12; // 0x1000 + 0x8 — condition as a percentage, always
        private const int Stack = 13;      // 0x8000 — a stack count, always
        private const int Plain = 14;      // nothing at all

        private static ObjectInfoSet Objects() => new ObjectInfoSet("objects", new List<ObjectInfo> {
            new ObjectInfo("torch") { Number = Charged, Icon = 0, InventorySlots = 1, MaxAmount = 1,
                Flags = ObjectFlags.LimitedUses },
            new ObjectInfo("sword") { Number = Degradable, Icon = 0, InventorySlots = 1, MaxAmount = 1,
                Flags = (ObjectFlags)0x1000 },
            new ObjectInfo("mail") { Number = WornArmour, Icon = 0, InventorySlots = 1, MaxAmount = 1,
                Flags = (ObjectFlags)0x1008 },
            new ObjectInfo("rations") { Number = Stack, Icon = 0, InventorySlots = 1, MaxAmount = 14,
                Flags = (ObjectFlags)0x8000 },
            new ObjectInfo("rock") { Number = Plain, Icon = 0, InventorySlots = 1, MaxAmount = 1 },
        });

        private static RuntimeContainer Pack(SaveGameContainerType type, params byte[] objectIds) {
            var container = new RuntimeContainer { ContainerType = type };
            foreach (byte id in objectIds) {
                container.Items.Add(new RuntimeItem(id, 7, 0)); // 7 of whatever the number means
            }
            return container;
        }

        private static VisualElement RenderPack(RuntimeContainer container) {
            var root = new VisualElement();
            new ItemGridRenderer().Render(root, container, Objects(), resources: null,
                onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(),
                new DesignFrame { Width = 1600, Height = 1200 });
            return root;
        }

        private static Label Quantity(VisualElement root, int slot) =>
            root.Q<Label>($"item_slot_{slot}_qty");

        private static bool Visible(Label label) =>
            label != null && label.style.display.value != DisplayStyle.None;

        private static bool Gated(Label label) =>
            label != null && label.ClassListContains(ItemGridRenderer.SelectionQuantityClass);

        [Test]
        public void AChargedItemHasItsNumberReady_ButHiddenUntilSelected() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.Inventory, Charged));

            Label qty = Quantity(root, 0);
            Assert.IsNotNull(qty, "the label is built up front; selection only reveals it");
            Assert.AreEqual("7", qty.text, "uses left comes from the item's own count");
            Assert.IsTrue(Gated(qty), "and it is marked as selection-gated");
            Assert.IsFalse(Visible(qty), "nothing is selected yet");
        }

        [Test]
        public void ADegradableItemIsGatedTheSameWay() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.Inventory, Degradable));

            Assert.IsTrue(Gated(Quantity(root, 0)));
            Assert.IsFalse(Visible(Quantity(root, 0)));
        }

        [Test]
        public void AStackCountIsAlwaysShown_AndIsNotGated() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.Inventory, Stack));

            Label qty = Quantity(root, 0);
            Assert.AreEqual("7", qty.text);
            Assert.IsTrue(Visible(qty));
            Assert.IsFalse(Gated(qty),
                "selecting and deselecting rations must never be able to hide the count");
        }

        [Test]
        public void WornArmourShowsItsConditionAsAPercentage_Always() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.Inventory, WornArmour));

            Label qty = Quantity(root, 0);
            Assert.AreEqual("7%", qty.text);
            Assert.IsTrue(Visible(qty));
            Assert.IsFalse(Gated(qty));
        }

        [Test]
        public void AnItemWithNeitherChargesNorConditionShowsNoNumberAtAll() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.Inventory, Plain));

            Assert.IsNull(Quantity(root, 0));
        }

        /// <summary>
        /// The shared keys inventory shows every count outright — the original tests the container
        /// type (RES_PICKLOCK_BUFFER, <c>containerType == 8</c>) before the highlight gate, so a
        /// key's number does not wait for a selection.
        /// </summary>
        [Test]
        public void TheSharedKeysInventoryShowsItsCountsWithoutSelecting() {
            VisualElement root = RenderPack(Pack(SaveGameContainerType.SharedKeys, Charged));

            Label qty = Quantity(root, 0);
            Assert.IsTrue(Visible(qty));
            Assert.IsFalse(Gated(qty));
        }

        [Test]
        public void EachSlotGetsItsOwnLabel_SoSelectingOneCannotRevealAnother() {
            VisualElement root = RenderPack(
                Pack(SaveGameContainerType.Inventory, Charged, Charged, Stack));

            Assert.IsTrue(Gated(Quantity(root, 0)));
            Assert.IsTrue(Gated(Quantity(root, 1)));
            Assert.IsFalse(Gated(Quantity(root, 2)));
            Assert.IsFalse(Visible(Quantity(root, 0)));
            Assert.IsFalse(Visible(Quantity(root, 1)));
        }

        // --- the reveal itself, driven through InventoryMenu -----------------------------------

        /// <summary>
        /// Selecting a charged item reveals its number and deselecting hides it again — the whole
        /// point of the gate. Driven through the menu's own selection handlers rather than a
        /// re-render, because that is how the outline works too.
        /// </summary>
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator SelectingAChargedItemRevealsItsUsesLeft() {
            var go = new UnityEngine.GameObject("InventoryMenuUnderTest");
            try {
                var document = go.AddComponent<UnityEngine.UIElements.UIDocument>();
                var panelSettings = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                document.panelSettings = panelSettings;
                var menu = go.AddComponent<BakAgain.UI.Inventory.InventoryMenu>();
                // Selection also refreshes the container image, so the menu needs its
                // collaborators — the same no-op doubles the pointer tests use.
                var session = new BakAgain.Core.GameSession();
                menu.Construct(session,
                    new BakAgain.Tests.PlayMode.UI.Inventory.NoOpResources(),
                    new BakAgain.Tests.PlayMode.UI.Inventory.NoOpNavigator(),
                    new BakAgain.Core.Services.DialogExecutor(
                        new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                        session, new BakAgain.Core.Services.GameClock(session)),
                    new BakAgain.Tests.PlayMode.UI.Inventory.NoOpDialogs());

                RuntimeContainer displayed = Pack(SaveGameContainerType.Inventory, Charged, Stack);
                VisualElement stage = document.rootVisualElement;
                new ItemGridRenderer().Render(stage, displayed, Objects(), resources: null,
                    onSlotPicked: null, InventoryLayoutMode.Loot, new InventoryLayout(),
                    new DesignFrame { Width = 1600, Height = 1200 });

                const System.Reflection.BindingFlags Priv =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(BakAgain.UI.Inventory.InventoryMenu).GetField("_displayed", Priv)
                    .SetValue(menu, displayed);
                typeof(BakAgain.UI.Inventory.InventoryMenu).GetField("_stage", Priv)
                    .SetValue(menu, stage);

                Assert.IsFalse(Visible(Quantity(stage, 0)), "hidden before anything is selected");

                typeof(BakAgain.UI.Inventory.InventoryMenu).GetMethod("SelectItem", Priv)
                    .Invoke(menu, new object[] { 0, 1 });
                Assert.IsTrue(Visible(Quantity(stage, 0)), "selecting the torch shows its uses left");
                Assert.IsTrue(Visible(Quantity(stage, 1)), "and the stack count is untouched");

                typeof(BakAgain.UI.Inventory.InventoryMenu).GetMethod("SelectItem", Priv)
                    .Invoke(menu, new object[] { 1, 1 });
                Assert.IsFalse(Visible(Quantity(stage, 0)),
                    "selecting elsewhere puts the torch's number away again");
                Assert.IsTrue(Visible(Quantity(stage, 1)),
                    "and selecting a stack cannot hide its always-on count");

                typeof(BakAgain.UI.Inventory.InventoryMenu).GetMethod("DeselectItem", Priv)
                    .Invoke(menu, null);
                Assert.IsTrue(Visible(Quantity(stage, 1)), "deselecting must not hide it either");

                UnityEngine.Object.DestroyImmediate(panelSettings);
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
            yield return null;
        }
    }
}
