namespace BakAgain.UI.InputCore {
    /// <summary>Stable static handle to the live positional input seams, set at container build —
    /// the agent/test drivability hook for pointer + gameplay input (mirrors UiDriver for intents).</summary>
    public static class InputDriver {
        public static IPointer Pointer { get; set; }
        public static IGameplayInput Gameplay { get; set; }
        public static ICheatInput Cheat { get; set; }

        // Editor-only debug harness reads (ModelDebugState's step/rotate keys). Stays null in player
        // builds — nothing sets it there, since ModelDebugState itself is excluded from player builds.
        public static IModelDebugInput ModelDebug { get; set; }
    }
}
