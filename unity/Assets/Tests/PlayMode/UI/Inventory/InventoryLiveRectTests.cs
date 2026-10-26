namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The drop tests read where a chrome element IS, not where the REQ put it: identical in the
    /// faithful layout, and the only way a drop can find a portrait the Enhanced layout has moved
    /// out of the stage (spec 2026-10-13 "Hit tests read live geometry").
    /// </summary>
    public class InventoryLiveRectTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        private InventoryMenu BuildMenu(out VisualElement root, out VisualElement stage) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            var session = new GameSession();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());
            root = document.rootVisualElement;
            stage = new VisualElement { style = { position = Position.Absolute, left = 0, top = 0, width = 1600, height = 1200 } };
            root.Add(stage);
            typeof(InventoryMenu).GetField("_stage", Any).SetValue(menu, stage);
            return menu;
        }

        private static VisualElement Box(string name, float x, float y, float w, float h) =>
            new() { name = name, style = { position = Position.Absolute, left = x, top = y, width = w, height = h } };

        private static void Layout(VisualElement root) =>
            root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(root.panel, null);

        private static int PortraitUnder(InventoryMenu menu, Vector2 stageLocal) =>
            (int)typeof(InventoryMenu).GetMethod("PortraitUnder", Any).Invoke(menu, new object[] { stageLocal });

        [Test]
        public void APortraitIsFoundWhereItIs_InTheStageOrMovedOutOfIt() {
            InventoryMenu menu = BuildMenu(out VisualElement root, out VisualElement stage);
            stage.Add(Box("hotspot_2", 70, 888, 260, 258));
            // Moved out of the stage, as the Enhanced column does, and far from its REQ rect.
            var column = Box("column", 1700, 0, 300, 1200);
            root.Add(column);
            column.Add(Box("hotspot_3", 20, 400, 260, 258));
            Layout(root);

            Assert.AreEqual(0, PortraitUnder(menu, new Vector2(200, 1000)), "in place, as the REQ put it");
            Assert.AreEqual(1, PortraitUnder(menu, new Vector2(1850, 520)), "moved: found at its new place");
            Assert.AreEqual(-1, PortraitUnder(menu, new Vector2(490, 1000)),
                "and not at its old REQ place, which no element covers any more");
        }

        [Test]
        public void ChromeDoesNothingWhileThePickerIsUp() {
            InventoryMenu menu = BuildMenu(out _, out _);
            typeof(InventoryMenu).GetField("_selectedSlot", Any).SetValue(menu, 0);
            typeof(InventoryMenu).GetField("_pickerOpen", Any).SetValue(menu, true);

            menu.PrimaryAction(2); // a portrait: would deselect and switch member

            Assert.AreEqual(0, typeof(InventoryMenu).GetField("_selectedSlot", Any).GetValue(menu),
                "the picker owns input until it answers");
        }
    }
}
