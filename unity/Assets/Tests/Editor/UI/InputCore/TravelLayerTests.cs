namespace BakAgain.Tests.Editor.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class TravelLayerTests {
        private static (TravelLayer layer, List<NavWidget> widgets, int[] cancelCount) Make() {
            var widgets = new List<NavWidget> {
                new NavWidget(new VisualElement(), "fwd", new Rect(0, 0, 10, 10), () => { }, null),
                new NavWidget(new VisualElement(), "map", new Rect(20, 0, 10, 10), () => { }, null),
            };
            var cancelCount = new[] { 0 };
            var layer = new TravelLayer("travel", widgets, () => cancelCount[0]++);
            return (layer, widgets, cancelCount);
        }

        [Test]
        public void IsPassiveGameplayLayer() {
            var (layer, _, _) = Make();
            Assert.AreEqual(CaptureMode.Passive, layer.CaptureMode);
            Assert.IsTrue(layer.WantsFocus);
            Assert.AreEqual("travel", layer.Id);
        }

        [Test]
        public void Cancel_InvokesOnCancel() {
            var (layer, _, cancel) = Make();
            Assert.IsTrue(layer.HandleIntent(UiIntent.Cancel()));
            Assert.AreEqual(1, cancel[0]);
        }

        [Test]
        public void Arrows_And_Activate_And_Accelerator_AreConsumedNoOps() {
            var (layer, _, cancel) = Make();
            Assert.IsTrue(layer.HandleIntent(UiIntent.Move(NavDirection.Up)), "arrows consumed (movement, not nav)");
            Assert.IsTrue(layer.HandleIntent(UiIntent.Activate()));
            Assert.IsTrue(layer.HandleIntent(UiIntent.Accelerator('m')));
            Assert.AreEqual(0, cancel[0], "none of these trigger onCancel");
        }

        [Test]
        public void OnActiveChanged_TogglesWidgetPickability() {
            var (layer, widgets, _) = Make();
            layer.OnActiveChanged(false);
            foreach (NavWidget w in widgets) {
                Assert.IsFalse(w.Element.focusable);
                Assert.AreEqual(PickingMode.Ignore, w.Element.pickingMode);
            }
            layer.OnActiveChanged(true);
            foreach (NavWidget w in widgets) {
                Assert.IsTrue(w.Element.focusable);
                Assert.AreEqual(PickingMode.Position, w.Element.pickingMode);
            }
        }

        // The movement gate contract: travel is the resolved input target only when nothing modal
        // sits above it. This is exactly what TravelLayerHost.IsInputActive checks.
        [Test]
        public void Gate_TravelIsResolvedTarget_UntilExclusiveAbove() {
            var stack = new InputLayerStack();
            var (travel, _, _) = Make();
            stack.Push(travel);
            Assert.AreSame(travel, stack.ResolveInputTarget(), "travel owns input when alone");

            var modal = new FakeInputLayer("dialog", CaptureMode.Exclusive);
            stack.Push(modal);
            Assert.AreNotSame(travel, stack.ResolveInputTarget(), "a modal above takes the input target");

            stack.Remove(modal);
            Assert.AreSame(travel, stack.ResolveInputTarget(), "closing the modal returns input to travel");
        }
    }
}
