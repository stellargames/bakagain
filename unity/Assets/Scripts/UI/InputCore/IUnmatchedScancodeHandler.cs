namespace BakAgain.UI.InputCore {
    /// <summary>
    /// A menu screen whose loop acts on keys no entry carries, as the original's loops do with the
    /// scancode <c>menupage_run</c> returns unmatched (MENUPAGE.C:320-399).
    /// </summary>
    public interface IUnmatchedScancodeHandler {
        /// <summary>Act on a key no entry of the page carries; true when it was used.</summary>
        bool OnUnmatchedScancode(int scancode);
    }
}
