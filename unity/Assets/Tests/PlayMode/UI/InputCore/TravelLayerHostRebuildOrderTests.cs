namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A travel-HUD rebuild must not move the travel layer above a dialog (TASK-915).
    /// </summary>
    /// <remarks>
    /// <c>InGameScreen.RefreshFollowRoadState</c> runs every frame and calls
    /// <c>UserInterfaceLoader.SetEntryState</c> whenever road travel becomes usable or not, and that
    /// ends with <c>Built?.Invoke</c>. <c>TravelLayerHost</c> answered it by popping and re-pushing,
    /// so a step that changed the road state under LaMut's Yes/No prompt turned
    /// <c>[InGameScreen][dialog-choice]</c> into <c>[dialog-choice][InGameScreen]</c> (measured live
    /// 2026-10-23) — the HUD's buttons pickable again under a modal, and <c>Stack.Top</c> no longer the
    /// dialog. The same bug <c>MenuLayerHost</c> had (TASK-563, MenuLayerHostRebuildOrderTests).
    /// </remarks>
    public class TravelLayerHostRebuildOrderTests {
        private GameObject _go;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        // An INACTIVE GameObject, so neither the host's Awake nor the loader's OnEnable runs; the
        // state the host would otherwise build is set directly.
        private TravelLayerHost Host(InputLayerStack stack) {
            _go = new GameObject("InGameScreen");
            _go.SetActive(false);
            var host = _go.AddComponent<TravelLayerHost>();
            typeof(TravelLayerHost).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(host, stack);
            return host;
        }

        private static void RaiseBuilt(TravelLayerHost host, IReadOnlyList<NavWidget> widgets) =>
            typeof(TravelLayerHost)
                .GetMethod("OnBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(host, new object[] { widgets });

        private static List<NavWidget> OneButton(out VisualElement button) {
            button = new VisualElement { pickingMode = PickingMode.Position, focusable = true };
            return new List<NavWidget> { new NavWidget(button, "road", Rect.zero, () => { }, null, 19) };
        }

        [Test]
        public void ARebuildUnderAChoiceLeavesTheChoiceOnTopAndTheHudInert() {
            var stack = new InputLayerStack();
            TravelLayerHost host = Host(stack);
            RaiseBuilt(host, new List<NavWidget>());          // the first build pushes the travel layer
            IInputLayer travel = stack.Top;
            var choice = new NavigableLayer("dialog-choice", CaptureMode.Exclusive,
                new List<NavWidget>(), null, null);
            stack.Push(choice);

            RaiseBuilt(host, OneButton(out VisualElement button));   // SetEntryState's Built

            Assert.AreSame(choice, stack.Top, "the rebuild re-pushed the travel layer over the prompt");
            Assert.AreEqual(2, stack.Layers.Count, "the rebuild must not add or drop a layer");
            Assert.AreSame(travel, stack.Layers[0]);
            Assert.AreEqual(PickingMode.Ignore, button.pickingMode,
                "a HUD button rebuilt under a modal must arrive inert");
            Assert.IsFalse(button.focusable);
        }

        [Test]
        public void ARebuildWithNothingAboveLeavesTheHudLive() {
            var stack = new InputLayerStack();
            TravelLayerHost host = Host(stack);
            RaiseBuilt(host, new List<NavWidget>());

            RaiseBuilt(host, OneButton(out VisualElement button));

            Assert.AreEqual(1, stack.Layers.Count);
            Assert.AreEqual(PickingMode.Position, button.pickingMode);
            Assert.IsTrue(button.focusable);
        }

        [Test]
        public void TheFirstBuildPushes() {
            var stack = new InputLayerStack();
            TravelLayerHost host = Host(stack);

            RaiseBuilt(host, new List<NavWidget>());

            Assert.AreEqual(1, stack.Layers.Count, "a first Built must push the travel layer");
            Assert.IsInstanceOf<TravelLayer>(stack.Top);
        }
    }
}
