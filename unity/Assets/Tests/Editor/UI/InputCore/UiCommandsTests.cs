namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    public class UiCommandsTests {
        private static (UiCommands cmds, FakeInputLayer top) WithTopLayer(CaptureMode mode) {
            var stack = new InputLayerStack();
            var top = new FakeInputLayer("top", mode);
            stack.Push(top);
            return (new UiCommands(stack), top);
        }

        [Test]
        public void MoveFocus_DispatchesMoveIntentWithDirection() {
            var (cmds, top) = WithTopLayer(CaptureMode.Passive);
            cmds.MoveFocus(NavDirection.Previous);
            Assert.AreEqual(1, top.Received.Count);
            Assert.AreEqual(UiIntentKind.MoveFocus, top.Received[0].Kind);
            Assert.AreEqual(NavDirection.Previous, top.Received[0].Direction);
        }

        [Test]
        public void Activate_Cancel_Accelerator_Skip_DispatchRightKinds() {
            var (cmds, top) = WithTopLayer(CaptureMode.Exclusive);
            cmds.Activate();
            cmds.Cancel();
            cmds.Accelerator('q');
            cmds.Skip();
            Assert.AreEqual(UiIntentKind.Activate, top.Received[0].Kind);
            Assert.AreEqual(UiIntentKind.Cancel, top.Received[1].Kind);
            Assert.AreEqual(UiIntentKind.Accelerator, top.Received[2].Kind);
            Assert.AreEqual('q', top.Received[2].Character);
            Assert.AreEqual(UiIntentKind.Skip, top.Received[3].Kind);
        }

        [Test]
        public void IsModal_And_TopLayer_ReflectStack() {
            var (cmds, top) = WithTopLayer(CaptureMode.Exclusive);
            Assert.AreSame(top, cmds.TopLayer);
            Assert.IsTrue(cmds.IsModal);
        }
    }
}
