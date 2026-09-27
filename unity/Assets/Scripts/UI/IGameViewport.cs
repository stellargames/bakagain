namespace BakAgain.UI {
    using UnityEngine;

    /// <summary>
    /// Reports the screen-space rectangle of the game's render viewport — the
    /// area of the window where in-world / cutscene content is being drawn.
    /// UI Toolkit overlays (DialogManager, future HUD elements) anchor against
    /// this so they line up with the visible game area instead of stretching
    /// across whatever letterbox bars surround it.
    ///
    /// Implementations decide where the rect comes from (a cutscene
    /// letterboxed RawImage, the active 3D camera's viewport, a full-screen
    /// fallback, etc.). Coordinates are in actual screen pixels with the
    /// Unity Screen-rect convention (origin bottom-left).
    /// </summary>
    public interface IGameViewport {
        Rect ScreenRect { get; }
    }
}
