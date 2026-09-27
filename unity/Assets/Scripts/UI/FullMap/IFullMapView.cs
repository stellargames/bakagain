namespace BakAgain.UI.FullMap {
    using BakAgain.UI.Navigation;

    /// <summary>
    /// Chapter-intro world-map screen. Shows FULLMAP.SCX with the party-position icon, fades in,
    /// holds for a minimum visible time, and fades out on hide. An <see cref="IScreen"/>: the
    /// new-game/load flow calls <see cref="SetMarker"/> then pushes it (the chapter-description
    /// dialog renders over it as a tooltip) and pops it afterwards; the future in-game Map button
    /// pushes the same screen.
    /// </summary>
    public interface IFullMapView : IScreen {
        /// <summary>
        /// Set the party marker before the screen is pushed. When <paramref name="showMarker"/> is
        /// true the party icon (<paramref name="iconIndex"/> into <c>fmap_icn.bmx</c>) is placed at
        /// <paramref name="xPercent"/>/<paramref name="yPercent"/> of the map (0..100, from the
        /// top-left) — resolution-independent. When false the map shows without a marker (chapter 8).
        /// </summary>
        void SetMarker(bool showMarker, float xPercent, float yPercent, int iconIndex);

        /// <summary>
        /// How the player leaves this map, or <c>null</c> when they cannot.
        /// </summary>
        /// <remarks>
        /// <b>The same view serves two opposite contracts.</b> As a loading screen it dismisses
        /// itself and must offer no way out; opened from the map screen it is an ordinary screen
        /// the player leaves through REQ_FMAP's one widget. Passing null in the first case and a
        /// pop in the second is what keeps them apart — and having neither is what made the
        /// player-opened map a dead end, with no button, no key and no click able to close it.
        ///
        /// <para>Set before pushing, like <see cref="SetMarker"/> — the typed pre-push setter
        /// pattern. Both callers set it explicitly, because it persists between shows.</para>
        /// </remarks>
        void SetExitAffordance(System.Action onExit);
    }
}
