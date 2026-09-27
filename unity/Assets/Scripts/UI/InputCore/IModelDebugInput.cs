namespace BakAgain.UI.InputCore {
    /// <summary>Debug-only step/rotate reads for the ModelDebugState viewer harness (bracket/comma/
    /// period/minus/equals/arrow keys that page through models and zones). Deliberately NOT folded
    /// into IGameplayInput — these are dev-tool-only intents, not gameplay movement, and would
    /// pollute that seam. Implemented by SystemInputSource behind #if UNITY_EDITOR since
    /// ModelDebugState never ships in player builds (see RootLifetimeScope).</summary>
    public interface IModelDebugInput {
        bool PrevModel { get; }    // '[' or ',' pressed this frame
        bool NextModel { get; }    // ']' or '.' pressed this frame
        bool PrevZone { get; }     // '-' or numpad '-' pressed this frame
        bool NextZone { get; }     // '=' or numpad '+' pressed this frame
        bool RotateLeft { get; }   // left arrow held
        bool RotateRight { get; }  // right arrow held
    }
}
