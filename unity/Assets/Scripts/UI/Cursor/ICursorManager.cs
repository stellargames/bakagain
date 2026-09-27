namespace BakAgain.UI.Cursor {
    using GameData.Resources.Cursor;
    using UnityEngine;

    /// <summary>The software mouse cursor service. Models the original game's two-set,
    /// single-active-index cursor (POINTER for UI, POINTERG for world/combat/GDS scenes).
    ///
    /// <para>The primary path is data-driven: <see cref="SelectSet"/> selects the active set and
    /// <see cref="SetByIndex"/> picks an image by index (a widget's <c>cursor</c> field, exactly
    /// as <c>sub_seg030_97F</c> calls <c>SetPointerImage(uiElement.cursor)</c>). <see cref="Set"/>
    /// is a convenience over the semantic <c>cursor-map.json</c> table.</para></summary>
    public interface ICursorManager {
        /// <summary>Select the active cursor set: "POINTER" (UI default) | "POINTERG" (world/combat/GDS).</summary>
        void SelectSet(string setName);

        /// <summary>PRIMARY data-driven path: show image <paramref name="index"/> of the active set.
        /// A negative index falls back to the default arrow (index 0), mirroring SetPointerImage(-1).</summary>
        void SetByIndex(int index);

        /// <summary>Semantic convenience: resolve a <see cref="GameCursor"/> via cursor-map.json then SetByIndex.</summary>
        void Set(GameCursor cursor);

        void Hide();
        void Show();

        /// <summary>Keyboard menu navigation: warp the cursor to the focused widget centre (canonical px).
        /// Any subsequent real pointer movement reclaims ownership.</summary>
        void WarpTo(Vector2 canonicalPosition);

        /// <summary>The cursor's owned canonical position (exposed for tests/diagnostics).</summary>
        Vector2 CanonicalPosition { get; }
    }
}
