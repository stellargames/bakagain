namespace BakAgain.UI {
    using UnityEngine;

    /// <summary>
    /// Data + geometry source for the file-picker elements an
    /// <c>UserInterfaceLoader</c> renders. The REQ definitions ship with
    /// zero-sized FilePicker entries (the engine sets geometry at runtime via
    /// <c>menu_sub_38790</c> per dialog); the screen controller therefore
    /// supplies position, dimensions, item list, and selection through this
    /// interface, by ActionId.
    /// <para>
    /// Implemented as a sibling MonoBehaviour on the same GameObject as the
    /// <c>UserInterfaceLoader</c> (the loader picks it up via
    /// <c>GetComponent&lt;IFilePickerSource&gt;()</c>, mirroring the existing
    /// <c>IActionHandler</c> wiring).
    /// </para>
    /// </summary>
    public interface IFilePickerSource {
        /// <summary>
        /// Pixel rectangle the picker occupies in canvas coordinates (the
        /// loader's canvas is sized from the REQ's Width/Height, normally
        /// 320×200). Decoded from the engine's runtime
        /// <c>(xOffset, yOffset, totalWidth, visibleRows)</c> args.
        /// </summary>
        Rect GetPickerRect(int actionId);

        /// <summary>How many rows are visible at once before scrolling kicks
        /// in. Matches the engine's <c>arg_6</c> on the picker-create call.</summary>
        int GetVisibleRows(int actionId);

        /// <summary>Total item count in the picker's backing list.</summary>
        int GetItemCount(int actionId);

        /// <summary>Display text for a row. Indices are 0-based; out-of-range
        /// indices return <c>string.Empty</c>.</summary>
        string GetItemLabel(int actionId, int index);

        /// <summary>Currently-highlighted row, or <c>-1</c> when nothing is
        /// selected. The loader uses this to draw the selection bar.</summary>
        int GetSelectedIndex(int actionId);

        /// <summary>Called by the loader when the user clicks a row.</summary>
        void OnItemSelected(int actionId, int index);

        /// <summary>Called by the loader when the user double-clicks a row. For the
        /// saves list this selects the slot and triggers Restore, mirroring the
        /// engine's double-click-to-load (dialog_LoadGame synthesises the Restore
        /// action @ 0x6e61d).</summary>
        void OnItemActivated(int actionId, int index);
    }
}
