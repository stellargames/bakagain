namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The paperdoll box's drag-time pulse (task-44; <c>invui_portr_panel_fill_pulsing</c> @0x563D6,
    /// INVENTOR.C:181 + its call site at 696-703).
    ///
    /// <para>Two things are pinned. The <b>pen</b>: the original computes
    /// <c>phase &gt; 3 ? 0x71 - phase : 0x6B + phase</c> over <c>phase % 6</c>, a triangular
    /// 6B→6E→6C sweep. The <b>gate</b>: it strokes the box on every drag frame but with pen 0 —
    /// black on a black fill, i.e. invisible — unless the cursor is inside the box AND the item is
    /// one this member could equip (the call site passes the item's category, nonzero exactly
    /// then). The port only builds the border element when it would be visible, so "is there a
    /// border" here IS that gate.</para>
    /// </summary>
    public class PaperdollPulseTests {
        private const byte SwordId = 3;
        private const byte RationsId = 72;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        // ---- the pen table -------------------------------------------------------------

        [Test]
        public void PulsePen_IsTheOriginalsTriangularSweep() {
            int[] expected = { 0x6B, 0x6C, 0x6D, 0x6E, 0x6D, 0x6C };
            for (int phase = 0; phase < 12; phase++) {
                // The caller's counter runs mod 12 (one counter feeds the head circles too); every
                // consumer takes it mod 6, so the second half repeats the first.
                Assert.AreEqual(expected[phase % 6], InventoryMenu.PulsePen(phase),
                    $"phase {phase}: pen must be (phase % 6 > 3 ? 0x71 - m : 0x6B + m)");
            }
        }

        [Test]
        public void PulsePen_PeaksAtTheBrightestPen_AndNeverLeavesTheFourPenRange() {
            for (int phase = 0; phase < 6; phase++) {
                int pen = InventoryMenu.PulsePen(phase);
                Assert.GreaterOrEqual(pen, 0x6B);
                Assert.LessOrEqual(pen, 0x6E);
            }
            Assert.AreEqual(0x6E, InventoryMenu.PulsePen(3), "the sweep peaks at phase 3");
        }

        // ---- the gate ------------------------------------------------------------------

        [Test]
        public void BorderAppears_OnlyWhileDraggingAnEquippableItemInsideTheBox() {
            InventoryMenu menu = BuildMenu(SwordId, out VisualElement fill);

            UpdateBorder(menu, dragging: true, slot: 0, point: new Vector2(100f, 100f));
            Assert.IsNotNull(Border(fill), "a sword dragged over the paperdoll must pulse the box");

            UpdateBorder(menu, dragging: true, slot: 0, point: new Vector2(5000f, 5000f));
            Assert.IsNull(Border(fill), "outside the box the original picks pen 0 — nothing visible");

            UpdateBorder(menu, dragging: true, slot: 0, point: new Vector2(100f, 100f));
            Assert.IsNotNull(Border(fill), "re-entering the box must bring the pulse back");

            UpdateBorder(menu, dragging: false, slot: 0, point: new Vector2(100f, 100f));
            Assert.IsNull(Border(fill), "the box only strokes during a drag");
        }

        [Test]
        public void NoBorder_ForACategoryThisMemberCannotEquip() {
            InventoryMenu menu = BuildMenu(RationsId, out VisualElement fill);

            UpdateBorder(menu, dragging: true, slot: 0, point: new Vector2(100f, 100f));

            Assert.IsNull(Border(fill),
                "cmbinv_member_can_equip_cat rejects Food, so the pulse gate never arms — the same "
                + "gate that makes the drop a silent snap-back");
        }

        [Test]
        public void NoBorder_WhenALootContainerIsDisplayed() {
            InventoryMenu menu = BuildMenu(SwordId, out VisualElement fill,
                SaveGameContainerType.Chest);

            UpdateBorder(menu, dragging: true, slot: 0, point: new Vector2(100f, 100f));

            Assert.IsNull(Border(fill),
                "the equip branch requires bResidence == 1; a container view has no paperdoll at all");
        }

        // ---- harness -------------------------------------------------------------------

        private static VisualElement Border(VisualElement fill) =>
            fill.Q<VisualElement>("paperdoll_pulse_border");

        private static void UpdateBorder(InventoryMenu menu, bool dragging, int slot, Vector2 point) =>
            typeof(InventoryMenu)
                .GetMethod("UpdatePaperdollBorder", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(menu, new object[] { dragging, slot, point });

        // An InventoryMenu with a displayed one-item container and a paperdoll fill element big
        // enough that (100,100) is inside it and (5000,5000) is not. The fill is the drop target by
        // construction (MemberEquip reads its resolved geometry), so no layout hint is needed.
        private InventoryMenu BuildMenu(byte objectId, out VisualElement fill,
            SaveGameContainerType containerType = SaveGameContainerType.Inventory) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            var session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("s") {
                    Number = SwordId, Name = "Broadsword", InventorySlots = 2,
                    ObjectType = ObjectType.Sword,
                },
                new ObjectInfo("r") {
                    Number = RationsId, Name = "Rations", InventorySlots = 1,
                    ObjectType = ObjectType.Food,
                },
            }));
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());

            var displayed = new RuntimeContainer { Capacity = 24, ContainerType = containerType };
            displayed.Items.Add(new RuntimeItem(objectId, 100, 0));
            Set(menu, "_displayed", displayed);

            fill = new VisualElement {
                name = "paperdoll_fill",
                style = { position = Position.Absolute, left = 0, top = 0, width = 1000, height = 1000 },
            };
            document.rootVisualElement.Add(fill);
            Set(menu, "_stage", document.rootVisualElement);
            Set(menu, "_paperdollFill", fill);

            MethodInfo validateLayout = document.rootVisualElement.panel.GetType().GetMethod(
                "ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(document.rootVisualElement.panel, null);
            return menu;
        }

        private static void Set(InventoryMenu menu, string field, object value) =>
            typeof(InventoryMenu).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(menu, value);
    }
}
