namespace BakAgain.UI.InputCore {
    using UnityEngine.InputSystem;
    using VContainer;
    using VContainer.Unity;

    // The one place the input core's DI registration lives — called from RootLifetimeScope and from
    // tests, so the wiring is verified without loading a scene. Registers the ownership stack + intent
    // seam, the agent/test UiDriver hook, the action-map context, and the device InputAdapter.
    public static class InputCoreInstaller {
        /// <summary>Registers the input core. <paramref name="pointerOverride"/>/<paramref name="gameplayOverride"/>
        /// are an optional test/agent seam: when supplied, that instance is registered for
        /// <see cref="IPointer"/>/<see cref="IGameplayInput"/> (and becomes the resolved
        /// <see cref="InputDriver"/> handle) instead of <see cref="SystemInputSource"/> — e.g. pass a
        /// <c>FakePointer</c> to drive a container's pointer consumers deterministically in a test,
        /// without touching real devices. <see cref="SystemInputSource"/> is still registered and still
        /// backs the non-overridden interfaces (<see cref="ICheatInput"/>, editor <c>IModelDebugInput</c>),
        /// so a partial override (e.g. pointer only) leaves gameplay/cheat/debug reads on the real device.
        /// The default (no-arg) call is byte-compatible with the pre-override behaviour.</summary>
        public static void RegisterInputCore(
            IContainerBuilder builder,
            IPointer pointerOverride = null,
            IGameplayInput gameplayOverride = null) {
            builder.Register<InputLayerStack>(Lifetime.Singleton);
            builder.Register<IUiCommands, UiCommands>(Lifetime.Singleton);

            // One shared DefaultInputActions instance backs BOTH the InputContext (action-map switching)
            // and the InputAdapter (UI nav/submit/cancel) — injected into the adapter, not new'd there —
            // so switching to a Gameplay context via InputContext actually disables the adapter's UI map
            // (one map owns the devices). The scene InputSystemUIInputModule keeps its own asset for
            // pointer events.
            //
            // *** THE CONTAINER MUST OWN IT, AND DISABLING MUST BEAT DESTROYING. *** Two separate
            // faults, one after the other, both of which end as monitors on <Mouse>/position that
            // outlive the state behind them:
            //
            //   `new` + RegisterInstance leaked one enabled asset per container build, because
            //   VContainer disposes singletons it CREATES and deliberately not instances handed to
            //   it. Registering the type fixed that — and introduced the second fault, because the
            //   generated DefaultInputActions.Dispose() is `Object.Destroy(asset)` with no Disable
            //   in front of it. Destroying an asset whose maps are still enabled leaves their
            //   monitors registered, and every mouse move then dispatches into a dead
            //   InputActionState: "Map index out of range in ProcessControlStateChange".
            //
            // SharedInputActions owns both halves in the only order that works. It is the one
            // disposable the container holds for this, which is what keeps the order out of
            // VContainer's teardown sequence — see the type for the measurements.
            builder.Register<SharedInputActions>(Lifetime.Singleton);
            // Resolved THROUGH the owner rather than registered in their own right: a second
            // registration of DefaultInputActions would hand the container an IDisposable that
            // destroys the asset, with nothing ordering it after the disable.
            builder.Register(c => c.Resolve<SharedInputActions>().Asset, Lifetime.Singleton);
            builder.Register<InputContext>(Lifetime.Singleton);

            builder.Register<SystemInputSource>(Lifetime.Singleton);
            // The Android touch aids' state (spec 2026-09-29-android-touch-aids-design.md).
            builder.Register<TouchInputState>(Lifetime.Singleton);
            if (pointerOverride != null) {
                builder.RegisterInstance(pointerOverride).As<IPointer>();
            } else {
                builder.Register<IPointer>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
            }
            if (gameplayOverride != null) {
                builder.RegisterInstance(gameplayOverride).As<IGameplayInput>();
            } else {
                builder.Register<IGameplayInput>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
            }
            builder.Register<ICheatInput>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
            builder.Register<IMapOptionInput>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
            // The combat overlay's G toggle — edge-triggered, like IMapOptionInput and unlike
            // ICheatInput's hold. Never overridden by a test driver: nothing drives it but a key.
            builder.Register<ICombatOverlayInput>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
#if UNITY_EDITOR
            // Debug-only harness reads (ModelDebugState) — excluded from player builds, mirroring
            // ModelDebugState's own #if UNITY_EDITOR registration in RootLifetimeScope.
            builder.Register<IModelDebugInput>(c => c.Resolve<SystemInputSource>(), Lifetime.Singleton);
#endif

            // InputAdapter as an entry point: Start() subscribes + strips the module; Tick() polls
            // Tab/numpad/letters; Dispose() unsubscribes.
            builder.RegisterEntryPoint<InputAdapter>(Lifetime.Singleton);

            builder.RegisterBuildCallback(container => {
                UiDriver.Stack = container.Resolve<InputLayerStack>();
                UiDriver.Commands = container.Resolve<IUiCommands>();
                TouchInputState.Instance = container.Resolve<TouchInputState>();
                InputDriver.Pointer = container.Resolve<IPointer>();
                InputDriver.Gameplay = container.Resolve<IGameplayInput>();
                InputDriver.Cheat = container.Resolve<ICheatInput>();
#if UNITY_EDITOR
                InputDriver.ModelDebug = container.Resolve<IModelDebugInput>();
#endif
            });
        }
    }
}
