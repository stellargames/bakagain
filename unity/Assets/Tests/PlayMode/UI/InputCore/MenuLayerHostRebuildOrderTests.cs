namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A REQ rebuild must not move its screen above whatever is on top of it (TASK-563).
    /// </summary>
    /// <remarks>
    /// <c>UserInterfaceLoader.SetEntryState</c> ends with <c>Built?.Invoke(_navWidgets)</c>, so
    /// every "grey that button out" raises <c>Built</c> — not only a first build. Routing that to
    /// <c>PushLayer</c> popped and re-pushed, landing the screen on the TOP of the stack.
    ///
    /// <para>Measured live on 2026-09-21 with the camp screen and an announcement dialog up:
    /// <c>[InGameScreen][CampScreen][dialog-narrative]</c> became
    /// <c>[InGameScreen][dialog-narrative][CampScreen]</c> after one
    /// <c>CampMenu.ApplyButtonState()</c>. With the dialog's layer buried no Activate could reach
    /// it, so it was never acknowledged, <c>_announcing</c> stayed true and the rest's
    /// <c>finally</c> never ran — the stranded <c>IsResting</c> that wedged camping for a whole
    /// session.</para>
    /// </remarks>
    public class MenuLayerHostRebuildOrderTests {
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        // The same arrangement MenuLayerHostSetWidgetsTests uses: an INACTIVE GameObject, so
        // UserInterfaceLoader.OnEnable never runs and cannot log about a missing address, with the
        // private state this host would otherwise build asynchronously set directly.
        private MenuLayerHost HostHoldingALayer(InputLayerStack stack, out NavigableLayer mine) {
            _go = new GameObject("host");
            _go.SetActive(false);
            var host = _go.AddComponent<MenuLayerHost>();
            mine = new NavigableLayer("screen", CaptureMode.Passive, new List<NavWidget>(), null, null);

            typeof(MenuLayerHost).GetField("_layer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, mine);
            typeof(MenuLayerHost).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, stack);
            return host;
        }

        private static void RaiseBuilt(MenuLayerHost host) =>
            typeof(MenuLayerHost)
                .GetMethod("OnBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(host, new object[] { new List<NavWidget>() });

        [Test]
        public void ARebuildLeavesADialogAboveTheScreenStillOnTop() {
            var stack = new InputLayerStack();
            MenuLayerHost host = HostHoldingALayer(stack, out NavigableLayer mine);
            stack.Push(mine);
            var dialog = new ActionLayer("dialog-narrative", () => { }, () => { });
            stack.Push(dialog);

            RaiseBuilt(host);

            Assert.AreSame(dialog, stack.Top,
                "a rebuild re-pushed the screen over the dialog, which is how TASK-563 stranded a rest");
            Assert.AreEqual(2, stack.Layers.Count, "the rebuild must not add or drop a layer");
            Assert.AreSame(mine, stack.Layers[0]);
        }

        [Test]
        public void ARebuildWithNothingAboveKeepsTheScreenExactlyWhereItWas() {
            // The control: without it the fix could be "never touch the stack at all", which would
            // also pass the test above and would leave a first build with no layer pushed.
            var stack = new InputLayerStack();
            MenuLayerHost host = HostHoldingALayer(stack, out NavigableLayer mine);
            stack.Push(mine);

            RaiseBuilt(host);

            Assert.AreSame(mine, stack.Top);
            Assert.AreEqual(1, stack.Layers.Count);
        }

        [Test]
        public void TheFIRSTBuildStillPushes() {
            // The other half of the control: a host with no layer yet must still push one, or a
            // freshly opened screen would take no input at all.
            var stack = new InputLayerStack();
            _go = new GameObject("host");
            _go.SetActive(false);
            var host = _go.AddComponent<MenuLayerHost>();
            // PushLayer reads the loader to choose Navigable vs ScreenInput, so this one needs the
            // component the other two never reach. The GameObject stays inactive, so the loader's
            // own OnEnable never runs and it logs nothing.
            var loader = _go.AddComponent<BakAgain.ResourceManagement.Loaders.UserInterfaceLoader>();
            typeof(MenuLayerHost).GetField("_loader", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, loader);
            typeof(MenuLayerHost).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, stack);

            RaiseBuilt(host);

            Assert.AreEqual(1, stack.Layers.Count, "a first Built must push a layer");
        }
    }
}
