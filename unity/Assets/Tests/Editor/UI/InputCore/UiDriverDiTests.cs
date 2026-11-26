namespace BakAgain.Tests.Editor.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using VContainer;

    public class UiDriverDiTests {
        [Test]
        public void Installer_WiresUiDriver_AndDrivesPushedLayer() {
            // The build points the global UiDriver at this throwaway container: put the game's back after,
            // or every later test drives a dead stack (found by EnhancedSettingsPagesTests, 2026-11-26).
            IUiCommands commands = UiDriver.Commands;
            InputLayerStack stack = UiDriver.Stack;
            var builder = new ContainerBuilder();
            InputCoreInstaller.RegisterInputCore(builder);
            try {
                using (builder.Build()) {                       // Build() runs the build callback
                    Assert.IsNotNull(UiDriver.Commands, "UiDriver.Commands set at container build");
                    Assert.IsNotNull(UiDriver.Stack, "UiDriver.Stack set at container build");

                    var layer = new FakeInputLayer("probe", CaptureMode.Exclusive);
                    UiDriver.Stack.Push(layer);
                    UiDriver.Commands.Activate();
                    Assert.AreEqual(1, layer.Received.Count, "agent-driven intent reached the pushed layer");
                    Assert.AreEqual(UiIntentKind.Activate, layer.Received[0].Kind);
                }
            } finally {
                UiDriver.Commands = commands;
                UiDriver.Stack = stack;
            }
        }
    }
}
