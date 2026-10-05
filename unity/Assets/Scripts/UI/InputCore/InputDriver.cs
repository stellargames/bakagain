namespace BakAgain.UI.InputCore {
    /// <summary>Stable static handle to the live positional input seams, set at container build —
    /// the agent/test drivability hook for pointer + gameplay input (mirrors UiDriver for intents).</summary>
    public static class InputDriver {
        public static IPointer Pointer { get; set; }
        public static IGameplayInput Gameplay { get; set; }
        public static ICheatInput Cheat { get; set; }

        /// <summary>Either Shift key — the original's <c>key_is_down(0x2a) || key_is_down(0x36)</c>.</summary>
        public static bool ShiftHeld => Gameplay is { } keys && (keys.LeftShift || keys.RightShift);

        // Editor-only debug harness reads (ModelDebugState's step/rotate keys). Stays null in player
        // builds — nothing sets it there, since ModelDebugState itself is excluded from player builds.
        public static IModelDebugInput ModelDebug { get; set; }
    }
}
