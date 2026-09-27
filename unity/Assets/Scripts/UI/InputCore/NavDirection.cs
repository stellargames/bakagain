namespace BakAgain.UI.InputCore {
    // PageUp/PageDown are the coarse-step directions (menu_pollInput scancodes 0x49/0x51). Only a
    // surface that HAS a coarse step reads them — today just the quantity picker's ±5. Every other
    // layer ignores them structurally rather than by a guard: NavigableLayer's Spatial() gets
    // dx = dy = 0 and finds no candidate, ScreenInputLayer's HandleMove falls to its default. So
    // these are additive, not a behaviour change to any existing screen.
    // First/Last are Home/End. Same deal: the overhead map reads them as its five-step zoom
    // (scancodes 0x47/0x4f), a list surface would read them as jump-to-ends, and everything else
    // ignores them the same structural way it ignores PageUp/PageDown.
    public enum NavDirection { Next, Previous, Up, Down, Left, Right, PageUp, PageDown, First, Last }
}
