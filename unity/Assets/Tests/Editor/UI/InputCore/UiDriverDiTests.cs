namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using VContainer;

    public class UiDriverDiTests {
        [Test]
        public void Installer_WiresUiDriver_AndDrivesPushedLayer() {
            var builder = new ContainerBuilder();
            InputCoreInstaller.RegisterInputCore(builder);
            using (builder.Build()) {                       // Build() runs the build callback
                Assert.IsNotNull(UiDriver.Commands, "UiDriver.Commands set at container build");
                Assert.IsNotNull(UiDriver.Stack, "UiDriver.Stack set at container build");

                var layer = new FakeInputLayer("probe", CaptureMode.Exclusive);
                UiDriver.Stack.Push(layer);
                UiDriver.Commands.Activate();
                Assert.AreEqual(1, layer.Received.Count, "agent-driven intent reached the pushed layer");
                Assert.AreEqual(UiIntentKind.Activate, layer.Received[0].Kind);
            }
        }
    }
}
