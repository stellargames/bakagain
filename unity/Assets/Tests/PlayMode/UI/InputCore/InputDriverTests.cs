namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using VContainer;

    /// <summary>Covers the InputCoreInstaller override hook (Task 9): a fake registered via the
    /// override parameters must be what the container resolves AND what the InputDriver agent/test
    /// handle points at after Build() — closing the "installer resolution never tested" gap.</summary>
    public class InputDriverTests {
        [Test]
        public void Installer_PointerOverride_ResolvesFakeAndSetsInputDriver() {
            var builder = new ContainerBuilder();
            var fakePointer = new FakePointer();
            InputCoreInstaller.RegisterInputCore(builder, pointerOverride: fakePointer);
            using (var container = builder.Build()) {          // Build() runs the build callback
                Assert.AreSame(fakePointer, container.Resolve<IPointer>(),
                    "container resolves the overridden IPointer instance");
                Assert.AreSame(fakePointer, InputDriver.Pointer,
                    "InputDriver.Pointer is set to the overridden IPointer instance at build time");
            }
        }

        [Test]
        public void Installer_GameplayOverride_ResolvesFakeAndSetsInputDriver() {
            var builder = new ContainerBuilder();
            var fakeGameplay = new FakeGameplayInput();
            InputCoreInstaller.RegisterInputCore(builder, gameplayOverride: fakeGameplay);
            using (var container = builder.Build()) {
                Assert.AreSame(fakeGameplay, container.Resolve<IGameplayInput>(),
                    "container resolves the overridden IGameplayInput instance");
                Assert.AreSame(fakeGameplay, InputDriver.Gameplay,
                    "InputDriver.Gameplay is set to the overridden IGameplayInput instance at build time");
            }
        }

        [Test]
        public void Installer_RegistersTheMapOptionKey() {
            // OverheadMapScreen injects IMapOptionInput, so an unregistered seam fails the whole
            // screen at construction rather than just losing the 'N' toggle.
            var builder = new ContainerBuilder();
            InputCoreInstaller.RegisterInputCore(builder);
            using (var container = builder.Build()) {
                Assert.IsInstanceOf<SystemInputSource>(container.Resolve<IMapOptionInput>());
            }
        }

        [Test]
        public void Installer_NoOverride_StillResolvesRealPointerAndGameplay() {
            // Byte-compatibility guard: the default (no-arg) call path must keep resolving
            // SystemInputSource for both interfaces, same as before the override hook existed.
            var builder = new ContainerBuilder();
            InputCoreInstaller.RegisterInputCore(builder);
            using (var container = builder.Build()) {
                Assert.IsInstanceOf<SystemInputSource>(container.Resolve<IPointer>());
                Assert.IsInstanceOf<SystemInputSource>(container.Resolve<IGameplayInput>());
            }
        }
    }
}
