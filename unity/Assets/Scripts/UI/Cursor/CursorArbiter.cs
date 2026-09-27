namespace BakAgain.UI.Cursor {
    using UnityEngine;

    /// <summary>The pure, scene-independent core of cursor positioning: last-active-input-wins
    /// arbitration between the mouse/pen pointer and a programmatic keyboard warp.
    ///
    /// <para>Mirrors the original game's single warp (<c>SetCursorPosition</c> @ 0x2ab39):
    /// keyboard menu navigation moves the pointer to the focused widget, but any real mouse
    /// movement immediately reclaims ownership. All positions are canonical (1600×1200).</para></summary>
    public class CursorArbiter {
        public Vector2 Position { get; private set; }

        /// <summary>Programmatic warp (keyboard menu nav → focused widget centre).</summary>
        public void WarpTo(Vector2 canonical) => Position = canonical;

        /// <summary>Pointer moved: a non-zero device delta reclaims position to the live pointer;
        /// a zero delta (no physical movement) leaves a prior warp in place.</summary>
        public void OnPointerMoved(Vector2 delta, Vector2 canonicalPointerPos) {
            if (delta.sqrMagnitude > 0f) Position = canonicalPointerPos;
        }
    }
}
