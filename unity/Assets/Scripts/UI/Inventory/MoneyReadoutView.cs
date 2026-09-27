namespace BakAgain.UI.Inventory {
    using BakAgain.UI.Layout;
    using GameData.Money;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The party-money readout in the inventory screen's lower right — "123s 4r"
    /// (<c>UI_DrawInventory</c> @0x56dd0, INVENTOR.C:530-543). Spec:
    /// <c>docs/specs/party-money-display.md</c> §3.1.
    ///
    /// <para>Drawn on <b>every</b> mode of the screen — a member's own inventory, a loot container,
    /// the combat inventory and a shop — because the original writes it unconditionally at the end
    /// of the shared grid render, gated on nothing.</para>
    ///
    /// <para><b>This class owns no coordinates and no wording.</b> Where it sits comes from
    /// <see cref="InventoryLayout.MoneyReadout"/>; what it says comes from
    /// <see cref="MoneyFormatter"/> with <see cref="CurrencyStyle.Abbreviated"/>; what colour it is
    /// comes from <see cref="InventoryTextStyle"/>. What stays here is the one fact that is neither
    /// geometry nor text: the anchor is the number's RIGHT edge (the original's
    /// <c>alignment == 2</c>, which <c>invui_draw_text_aligned_shadow</c> implements as
    /// <c>x -= textWidth</c>), so the number grows leftward as the party gets richer instead of
    /// sliding out from under its own button.</para>
    /// </summary>
    internal sealed class MoneyReadoutView {
        private const string TextName = "inventory_money";
        private const string ShadowName = "inventory_money_shadow";

        private readonly List<VisualElement> _elements = new();
        private bool _insetWarned;

        /// <summary>Remove the readout. Safe before anything has been drawn; used by the screen's
        /// own teardown and by <see cref="Render"/> before it repaints.
        /// <para><b>Not by the item-inspect view.</b> The original's inspect state skips the grid
        /// render, but skipping a draw into a framebuffer leaves the old pixels standing — and the
        /// number sits at VGA (0x103, 0xb7), outside the `13,11,294,121` rect INVINSP.C clears. So
        /// the purse is on screen behind an item description in the original, and clearing it here
        /// was a retained-mode mistranslation of "does not redraw".</para></summary>
        internal void Clear() {
            foreach (VisualElement e in _elements) {
                e.RemoveFromHierarchy();
            }
            _elements.Clear();
        }

        /// <param name="partyMoneyInRoyals">The purse straight off the session — ROYALS, ten to the
        /// sovereign. Splitting it is <see cref="MoneyFormatter"/>'s job, not the caller's.</param>
        internal void Render(VisualElement root, int partyMoneyInRoyals, InventoryLayout layout) {
            Clear();
            layout ??= new InventoryLayout();
            LayoutHint at = layout.MoneyReadout;
            if (root == null || at == null) {
                return; // an override may drop the readout entirely
            }
            string text = MoneyFormatter.Format(partyMoneyInRoyals, CurrencyStyle.Abbreviated);
            // Shadow first, so the text lands on top of it — the original's draw order.
            AddLabel(root, text, at, layout, shadow: true);
            AddLabel(root, text, at, layout, shadow: false);
        }

        private void AddLabel(VisualElement root, string text, LayoutHint at, InventoryLayout layout,
            bool shadow) {
            var label = new Label(text) {
                name = shadow ? ShadowName : TextName,
                // The REQ's own action-34 ClickArea sits under this and is the button; a label that
                // took picks would shadow it exactly where the player aims.
                pickingMode = PickingMode.Ignore,
            };
            LayoutApplier.Apply(label, at);
            if (shadow) {
                // The anchor is a RIGHT inset, so displacing the shadow one pixel to the right means
                // one pixel LESS of it — the sign is the caller's to supply (LayoutApplier.ShadowInset).
                label.style.right = ShadowInset(at.Right, -layout.TextShadowOffsetX, "TextShadowOffsetX");
                label.style.top = ShadowInset(at.Top, layout.TextShadowOffsetY, "TextShadowOffsetY");
            }
            label.style.color = shadow
                ? InventoryTextStyle.ShadowColor
                : InventoryTextStyle.DefaultTextColor;
            // Belt and braces with the right inset: an auto-width label already hugs its text, but
            // an override that gave the hint a Width would otherwise left-align the number inside it.
            label.style.unityTextAlign = TextAnchor.UpperRight;
            // Anchored right/top — the pair of edges the hint pins — so the game font's aspect
            // stretch grows away from them instead of dragging the anchor.
            GameFontText.Apply(label, GameFontText.AnchorX.Right, GameFontText.AnchorY.Top);
            root.Add(label);
            _elements.Add(label);
        }

        private StyleLength ShadowInset(LayoutLength inset, float offset, string property) =>
            LayoutApplier.ShadowInset(inset, offset, "InventoryLayout.MoneyReadout (" + property + ")",
                "the money readout's text shadow is drawn without its offset", ref _insetWarned);
    }
}
