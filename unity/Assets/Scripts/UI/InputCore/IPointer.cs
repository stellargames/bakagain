namespace BakAgain.UI.InputCore {
    using UnityEngine;

    /// <summary>Device-agnostic pointer: the single seam for pointer position + clicks. The only
    /// implementation that touches the Input System is SystemInputSource; consumers inject this.</summary>
    public interface IPointer {
        /// <summary>
        /// A <b>hovering</b> pointer exists: draw a software cursor, and answer hover queries.
        /// </summary>
        /// <remarks>
        /// <b>False for touch, on purpose.</b> A finger has no position until it is down, so there
        /// is nothing to hover and nothing to draw a cursor at. Gate a CLICK on
        /// <see cref="CanPoint"/> instead — a press that already happened carries its own position,
        /// and asking whether a cursor was hovering is the wrong question about it.
        /// </remarks>
        bool IsPresent { get; }             // false when no pointer device is active (pure gamepad)

        /// <summary>
        /// A press carries a usable screen position — true for touch as well as for a mouse.
        /// </summary>
        /// <remarks>
        /// The split exists because <see cref="IsPresent"/> was answering two questions and only one
        /// of them is about cursors: of its five readers, the cursor and both hover queries were
        /// right to exclude touch, while the world click and the compass press were not. See
        /// <c>docs/superpowers/specs/2026-09-21-touch-input-design.md</c>.
        /// </remarks>
        bool CanPoint { get; }
        Vector2 ScreenPosition { get; }     // Input System screen coords (bottom-left origin)
        Vector2 Delta { get; }              // per-frame move delta
        Vector2 Scroll { get; }             // wheel/scroll delta
        IPointerButton Primary { get; }     // left mouse / touch
        IPointerButton Secondary { get; }   // right mouse / (mobile) long-press
    }

    public interface IPointerButton {
        bool IsDown { get; }                // level
        bool PressedThisFrame { get; }      // rising edge
        bool ReleasedThisFrame { get; }     // falling edge
    }
}
