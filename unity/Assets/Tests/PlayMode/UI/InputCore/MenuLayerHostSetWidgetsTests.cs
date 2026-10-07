namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// MenuLayerHost owns the pushed layer, so a screen that builds extra widgets (the inventory
    /// grid) hands them over here rather than holding its own layer reference.
    /// </summary>
    public class MenuLayerHostSetWidgetsTests {
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        [Test]
        public void SetWidgets_IsRefused_WhenNoLayerIsPushed() {
            // A host whose loader has not built yet has no layer; the screen must be able to call
            // unconditionally and be told nothing happened, rather than NRE.
            // The GameObject is created INACTIVE and never activated: that keeps
            // UserInterfaceLoader.OnEnable (which logs an error for a missing address, failing the
            // test on an unexpected log) from ever running. Awake does not run either, which is
            // fine — SetWidgets reads only _layer, which is null in both cases.
            _go = new GameObject("host");
            _go.SetActive(false);
            var host = _go.AddComponent<MenuLayerHost>();

            Assert.IsFalse(host.SetWidgets(new List<NavWidget>()));
        }

        [Test]
        public void SetWidgets_WithAPushedLayer_ForwardsTheListAndReturnsTrue() {
            // The positive case: SetWidgets' whole purpose is to hand a freshly-composed widget list
            // (chrome + the inventory's own item cells) over to the layer MenuLayerHost is already
            // holding, so it doesn't have to churn the input stack on every re-render. Only the
            // "nothing pushed yet" refusal above was covered before this test.
            //
            // Driving a real PushLayer would mean standing up a full UserInterfaceLoader build; the
            // narrower way to arrange a pushed layer — without pulling in that whole async chain —
            // is to set the private `_layer` field this host maintains directly via reflection, a
            // pattern this repo's tests already use for private state (e.g.
            // InventoryPointerTests setting InventoryMenu's `_displayed`/`_stage`).
            _go = new GameObject("host");
            _go.SetActive(false);
            var host = _go.AddComponent<MenuLayerHost>();

            int fired = 0;
            var widget = new NavWidget(new VisualElement(), null, new Rect(0, 0, 10, 10), () => fired++, null);
            var layer = new NavigableLayer("t", CaptureMode.Passive, new List<NavWidget>(), null, null);

            typeof(MenuLayerHost).GetField("_layer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, layer);

            Assert.IsTrue(host.SetWidgets(new List<NavWidget> { widget }),
                "a pushed NavigableLayer must accept the new list and report success");

            // Confirm the list actually reached the SAME layer instance (not just a true return
            // value): focus + activate through the layer's own public API, which only the widget
            // just handed to it via SetWidgets can satisfy.
            layer.FocusIndex(0);
            layer.HandleIntent(UiIntent.Activate());
            Assert.AreEqual(1, fired, "SetWidgets must forward the list to the layer it holds");
        }

        [Test]
        public void Rebuild_UnderAScreenInputLayer_LeavesTheButtonsUnfocusable() {
            // TASK-847: UI Toolkit's own runtime input walks focus over focusable buttons with the
            // arrows and clicks the focused one on Enter. A type-2 screen owns those keys through
            // its ScreenInputLayer, so its buttons must never take focus -- including after a
            // rebuild (SetEntryState re-marks them focusable and raises Built).
            _go = new GameObject("host");
            _go.SetActive(false);
            var host = _go.AddComponent<MenuLayerHost>();
            typeof(MenuLayerHost).GetField("_layer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, new ScreenInputLayer("t", new NullScreen()));

            var button = new VisualElement { focusable = true };
            typeof(MenuLayerHost).GetMethod("OnBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(host, new object[] { new List<NavWidget> { new NavWidget(button, null, new Rect(0, 0, 10, 10), null, null) } });

            Assert.IsFalse(button.focusable);
        }

        private sealed class NullScreen : IScreenInput {
            public bool WantsText => false;
            public bool OnDirection(NavDirection dir, bool ctrl) => false;
            public bool OnTab(bool shift) => false;
            public void OnText(char c) { }
            public bool OnEdit(EditKey key) => false;
            public void OnSubmit() { }
            public void OnCancel() { }
        }
    }
}
