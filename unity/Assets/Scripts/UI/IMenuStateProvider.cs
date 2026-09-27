namespace BakAgain.UI {
    using BakAgain.ResourceManagement.Loaders;

    /// <summary>
    /// Optional companion to <see cref="IActionHandler"/> for screens that have
    /// stateful Toggle/radio widgets (e.g. Preferences). <see cref="UserInterfaceLoader"/>
    /// queries this when rendering a Toggle element to decide which icon frame
    /// (on/off) to show. The handler owns all grouping logic (radio exclusivity,
    /// flag bits) — the loader stays a generic renderer.
    /// </summary>
    internal interface IMenuStateProvider {
        /// <summary>Returns true when the widget with this ActionId is "on"
        /// (radio selected, or checkbox set).</summary>
        bool GetToggleState(int actionId);
    }
}
