namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    public class InputLayerStackTests {
        [Test]
        public void FakeLayer_RecordsIntent() {
            var layer = new FakeInputLayer("a", CaptureMode.Passive);
            Assert.IsTrue(layer.HandleIntent(UiIntent.Activate()));
            Assert.AreEqual(1, layer.Received.Count);
            Assert.AreEqual(UiIntentKind.Activate, layer.Received[0].Kind);
        }

        [Test]
        public void Dispatch_RoutesToFocusedPassiveLayer() {
            var stack = new InputLayerStack();
            var menu = new FakeInputLayer("menu", CaptureMode.Passive);
            stack.Push(menu);
            stack.DispatchIntent(UiIntent.Activate());
            Assert.AreEqual(1, menu.Received.Count);
        }

        [Test]
        public void ExclusiveAbovePassive_RoutesToExclusive_NotPassive() {
            var stack = new InputLayerStack();
            var menu = new FakeInputLayer("menu", CaptureMode.Passive);
            var dialog = new FakeInputLayer("dialog", CaptureMode.Exclusive);
            stack.Push(menu);
            stack.Push(dialog);
            stack.DispatchIntent(UiIntent.Activate());
            Assert.AreEqual(0, menu.Received.Count, "layer beneath a modal must get no intent");
            Assert.AreEqual(1, dialog.Received.Count);
        }

        [Test]
        public void Dispatch_RoutesToExactlyOneLayer() {
            var stack = new InputLayerStack();
            var a = new FakeInputLayer("a", CaptureMode.Passive);
            var b = new FakeInputLayer("b", CaptureMode.Passive);
            stack.Push(a);
            stack.Push(b);
            stack.DispatchIntent(UiIntent.Activate());
            Assert.AreEqual(1, a.Received.Count + b.Received.Count, "single dispatch, no double-processing");
        }

        [Test]
        public void Pop_ReturnsToLayerBeneath_AndRoutesThere() {
            var stack = new InputLayerStack();
            var menu = new FakeInputLayer("menu", CaptureMode.Passive);
            var dialog = new FakeInputLayer("dialog", CaptureMode.Exclusive);
            stack.Push(menu);
            stack.Push(dialog);
            Assert.AreSame(dialog, stack.Pop());
            stack.DispatchIntent(UiIntent.Activate());
            Assert.AreEqual(1, menu.Received.Count);
            Assert.AreEqual(1, dialog.PoppedCount);
        }

        [Test]
        public void PassiveBelowExclusive_IsNotInteractable() {
            var stack = new InputLayerStack();
            var menu = new FakeInputLayer("menu", CaptureMode.Passive);
            var dialog = new FakeInputLayer("dialog", CaptureMode.Exclusive);
            stack.Push(menu);
            stack.Push(dialog);
            Assert.IsFalse(stack.IsInteractable(menu));
            Assert.IsTrue(stack.IsInteractable(dialog));
            Assert.IsFalse(menu.LastActive.Value, "OnActiveChanged(false) fired for the blocked layer");
        }

        [Test]
        public void Focus_PrefersTopmostPassiveThatWantsFocus() {
            var stack = new InputLayerStack();
            var world = new FakeInputLayer("world", CaptureMode.Passive, wantsFocus: false);
            var hud = new FakeInputLayer("hud", CaptureMode.Passive, wantsFocus: true);
            stack.Push(world);
            stack.Push(hud);
            stack.DispatchIntent(UiIntent.Move(NavDirection.Next));
            Assert.AreEqual(1, hud.Received.Count);
            Assert.AreEqual(0, world.Received.Count);
        }

        [Test]
        public void IsModal_TrueOnlyWithExclusive() {
            var stack = new InputLayerStack();
            stack.Push(new FakeInputLayer("menu", CaptureMode.Passive));
            Assert.IsFalse(stack.IsModal);
            stack.Push(new FakeInputLayer("dialog", CaptureMode.Exclusive));
            Assert.IsTrue(stack.IsModal);
        }

        [Test]
        public void Push_Duplicate_Throws() {
            var stack = new InputLayerStack();
            var menu = new FakeInputLayer("menu", CaptureMode.Passive);
            stack.Push(menu);
            Assert.Throws<System.InvalidOperationException>(() => stack.Push(menu));
        }

        [Test]
        public void RemoveWithIdPrefix_DropsOnlyTheMatchingLayers_AndPopsThem() {
            // The recovery an owner needs when it is disabled without running its own teardown: a
            // stranded Exclusive layer consumes every intent and pops for nobody.
            var stack = new InputLayerStack();
            var travel = new FakeInputLayer("travel", CaptureMode.Passive);
            var narrative = new FakeInputLayer("dialog-narrative", CaptureMode.Exclusive);
            var choice = new FakeInputLayer("dialog-choice", CaptureMode.Exclusive);
            stack.Push(travel);
            stack.Push(narrative);
            stack.Push(choice);

            Assert.AreEqual(2, stack.RemoveWithIdPrefix("dialog-"));
            Assert.AreSame(travel, stack.Top);
            Assert.IsFalse(stack.IsModal, "an intent must reach the travel layer again");
            // Both were popped, not merely dropped from the list — an owner that tracks its own
            // active state hears about it exactly once.
            Assert.AreEqual(1, narrative.PoppedCount);
            Assert.AreEqual(1, choice.PoppedCount);
        }

        [Test]
        public void RemoveWithIdPrefix_IsANoOpWhenNothingMatches() {
            // The ORDINARY path: teardown removed the layer already, and the disable that follows
            // must not touch anybody else's.
            var stack = new InputLayerStack();
            var travel = new FakeInputLayer("travel", CaptureMode.Passive);
            stack.Push(travel);

            Assert.AreEqual(0, stack.RemoveWithIdPrefix("dialog-"));
            Assert.AreEqual(0, stack.RemoveWithIdPrefix(null));
            Assert.AreSame(travel, stack.Top);
            Assert.AreEqual(0, travel.PoppedCount);
        }
    }
}
