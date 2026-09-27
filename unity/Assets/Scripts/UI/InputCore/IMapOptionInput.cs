namespace BakAgain.UI.InputCore {
    /// <summary>
    /// The overhead map's own keyboard option toggle, abstracted so no view reads
    /// <c>Keyboard.current</c> (the same seam, and the same reason, as <see cref="ICheatInput"/>).
    /// </summary>
    /// <remarks>
    /// Not folded into <see cref="IScreenInput"/>, which only delivers typed characters to a screen
    /// that says <c>WantsText</c> — and the map says no, because turning text on there would route
    /// Home/End to caret editing and cost the map its five-step zooms.
    /// </remarks>
    public interface IMapOptionInput {
        /// <summary>'N' pressed this frame — toggles north-up on the overhead map.</summary>
        /// <remarks>
        /// Edge-triggered, unlike <see cref="ICheatInput.RevealRareCredits"/>, which holds the same
        /// key: a held 'N' read as a level would flip the option every frame it was down.
        /// </remarks>
        bool ToggleNorthUpMap { get; }
    }
}
