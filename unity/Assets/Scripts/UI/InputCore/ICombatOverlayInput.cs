namespace BakAgain.UI.InputCore {
    /// <summary>
    /// The combat tactical overlay's toggle — the <b>G</b> key.
    /// </summary>
    /// <remarks>
    /// <b>Its own seam rather than a member of <see cref="IGameplayInput"/></b>, for the reason
    /// <see cref="ICheatInput"/> is separate: a one-key view toggle is not analog movement, and
    /// widening the movement interface for it would make every consumer of that interface depend on
    /// something none of them want (interface segregation).
    ///
    /// <para><b>Edge, not level.</b> The overlay flips on a press; a held key must not strobe it,
    /// which is what reading <c>IsPressed</c> would do at frame rate.</para>
    ///
    /// <para>The original's own toggle is an <c>xor</c> on <c>combatGridOverlayShown</c> reached
    /// through the combat command dispatcher (case 34), and it shows the 8x13 grid outline together
    /// with the crystal links — one flag, both halves.</para>
    /// </remarks>
    public interface ICombatOverlayInput {
        /// <summary>True on the frame G went down.</summary>
        bool ToggleOverlayPressed { get; }
    }
}
