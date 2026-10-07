namespace BakAgain.UI.Inventory {
    using UnityEngine;

    /// <summary>
    /// The inventory screen's default text pens, in one place.
    ///
    /// <para><c>invui_draw_text_aligned_shadow</c> (INVENTOR.C:161) takes a foreground and a shadow
    /// colour where <b>-1 means "default"</b>, and resolves that default itself: pen <c>0x9F</c>
    /// for the text and pen 0 for the shadow drawn one pixel down-right. Almost every string on the
    /// screen — grid quantity labels, the inspect view's four lines, the money readout — passes -1
    /// for both, so they all share one colour by construction in the original. Three separate
    /// literals here would let them drift apart for no reason a player could see.</para>
    /// </summary>
    internal static class InventoryTextStyle {
        /// <summary>INVENTOR.PAL pen 0x9F — a warm tan — the pen <c>invui_draw_text_aligned_shadow</c>
        /// substitutes for a -1 foreground.</summary>
        internal static readonly Color DefaultTextColor = new(244f / 255f, 196f / 255f, 164f / 255f);

        /// <summary>The pen substituted for a -1 shadow colour: pen 0, black.</summary>
        internal static readonly Color ShadowColor = Color.black;
    }
}
