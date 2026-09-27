namespace BakAgain.UI {
    using BakAgain.Graphics;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Styles a label as the original's plain game font. Single owner of the two facts that make
    /// game text match the DOS original, both of which are easy to get wrong independently:
    ///
    /// <list type="number">
    /// <item><b>Size.</b> <see cref="Canonical.GameFontSizePx"/>, which is the size whose advance
    /// widths match the extracted GAME.FNT table — NOT the character-cell height times
    /// <see cref="Canonical.VgaScaleY"/>, which over-sizes by ~8% (task-45).</item>
    /// <item><b>Vertical stretch.</b> Canonical space is anisotropic — <see cref="Canonical.VgaScaleX"/>
    /// horizontally against <see cref="Canonical.VgaScaleY"/> vertically, because mode 13h pixels
    /// aren't square — while a font renderer scales isotropically. Sizing for correct widths
    /// therefore leaves glyphs VgaScaleY/VgaScaleX too short, so the text is stretched back.
    /// Measured against a capture of the original: this takes the inspect view's three text rows
    /// from 6/7/7 pixels tall to 7/8/9, exactly matching, with widths within 2%.</item>
    /// </list>
    ///
    /// <para>The stretch needs an anchor, since scaling moves everything but the origin. Pass the
    /// edge the text is positioned from: the top for text placed by its glyph-cell top (the
    /// original's usual convention), the bottom for a label pinned to a cell's lower edge.</para>
    /// </summary>
    internal static class GameFontText {
        /// <summary>Horizontal anchor: the edge the text's x position refers to.</summary>
        internal enum AnchorX { Left, Centre, Right }

        /// <summary>
        /// Vertical anchor: the edge the text's y position refers to. <see cref="Middle"/> is for
        /// text that is CENTRED in a box it does not size — a dialog body spanning its panel, say
        /// — where neither edge is the anchor and stretching from one would walk the block off
        /// centre by half the growth.
        /// </summary>
        internal enum AnchorY { Top, Middle, Bottom }

        private static float StretchY => (float)Canonical.VgaScaleY / Canonical.VgaScaleX;

        /// <summary>
        /// The height of one line of game text in canonical px — GAME.FNT's character CELL, which
        /// is what <c>font_DrawWrappedTextBlock</c> (@0x4b94e) reads out of the selected font to
        /// advance by. Callers that need to place something BELOW a line of text want this, not
        /// <see cref="Canonical.GameFontSizePx"/>: a font size is an em, and once the stretch
        /// above is applied it is neither the width nor the height of anything on screen.
        ///
        /// <para>Note this is the cell only. Text that FLOWS over several lines advances by the
        /// cell plus a leading the caller states — see <see cref="LinePitchPx"/>.</para>
        /// </summary>
        internal static float LineHeightPx =>
            BakAgain.Book.BakFontData.GameFontHeight * Canonical.VgaScaleY;

        /// <summary>
        /// The leading the dialog renderer adds between lines, in VGA rows.
        /// <c>RenderDialogText</c> passes <c>lineGapExtra = 1</c> to
        /// <c>font_DrawWrappedTextBlock</c> (the <c>push 1</c> at <c>0x490d1</c> and
        /// <c>0x49109</c>, restated by the paging check at <c>0x4909c</c> as
        /// <c>fontHeights? + 1</c>).
        ///
        /// <para>It is a DIALOG datum, not a font one: the routine's only other caller — the
        /// spell list at <c>0x57d62</c> — passes 0. That is what rules out expressing it as extra
        /// line height on the shared <c>Game SDF</c> asset, which would silently lend the
        /// dialog's leading to every other surface drawn in the same font.</para>
        /// </summary>
        internal const int DialogLineGapVgaRows = 1;

        /// <summary>
        /// The distance between the tops of consecutive lines of flowing game text, in canonical
        /// px. <c>font_DrawWrappedTextBlock</c> advances by <c>fontHeight + lineGapExtra</c>
        /// (@0x4bb07) where <c>fontHeight</c> is the byte it read out of the selected font
        /// (@0x4b94e) — so for dialog this is 10 + 1 = 11 VGA rows, and NOT
        /// <see cref="LineHeightPx"/>, which is the cell alone.
        ///
        /// <para>Reproduced by placing each line ourselves rather than by styling one block of
        /// prose: UI Toolkit has no line-height property, and <c>unityParagraphSpacing</c> —
        /// the only spacing knob that reaches explicit breaks — resolves through the text engine
        /// at roughly 0.55x the value set, a ratio nothing in the game data states. See
        /// <see cref="GameTextBlock"/>.</para>
        /// </summary>
        internal static float LinePitchPx(int lineGapExtraVgaRows) =>
            (BakAgain.Book.BakFontData.GameFontHeight + lineGapExtraVgaRows) * Canonical.VgaScaleY;

        /// <summary>
        /// The horizontal edge a label's text is anchored from, read off the alignment it was
        /// built with — the stretch needs an anchor and this is where every caller gets it. Only
        /// the vertical half of the stretch is non-unit, so this changes nothing today; it is
        /// passed so the anchor stays truthful if it ever does.
        /// </summary>
        internal static AnchorX HorizontalAnchor(TextAnchor align) => align switch {
            TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter
                => AnchorX.Centre,
            TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight
                => AnchorX.Right,
            _ => AnchorX.Left,
        };

        /// <summary>Apply the game font's size and aspect correction to a label.</summary>
        internal static void Apply(VisualElement element, AnchorX anchorX, AnchorY anchorY) {
            if (element == null) {
                return;
            }
            element.style.fontSize = Canonical.GameFontSizePx;
            // Whitespace is CONTENT in this game's text, not formatting. The original advances the
            // pen by the space's width as many times as there are spaces, and DialogTextFormatter
            // renders the original's tab as a run of them — so a text engine that collapses runs
            // and trims the leading ones (CSS `white-space: normal`, which UI Toolkit began
            // honouring in 6.5) deletes every paragraph indent in the game. PreWrap preserves the
            // run and still allows wrapping for the surfaces that want it.
            element.style.whiteSpace = WhiteSpace.PreWrap;
            element.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(
                Length.Percent(anchorX switch {
                    AnchorX.Left => 0f,
                    AnchorX.Centre => 50f,
                    _ => 100f,
                }),
                Length.Percent(anchorY switch {
                    AnchorY.Top => 0f,
                    AnchorY.Middle => 50f,
                    _ => 100f,
                })));
            element.style.scale = new StyleScale(new Scale(new Vector2(1f, StretchY)));
        }

        /// <summary>
        /// Puts a button's caption in an INNER label that carries the stretch, and returns it.
        /// </summary>
        /// <param name="chrome">
        /// The element wearing the chrome — its background, borders and bevel. A
        /// <c>.text-button</c>, or a <c>.file-picker-row</c>, which has the same shape: a background
        /// and a bevel on the same element as the text.
        /// </param>
        /// <param name="anchorX">
        /// Where the caption sits, and therefore what it scales about. Centre for a button, Left for
        /// a row.
        /// </param>
        /// <remarks>
        /// <b>A UI Toolkit button IS its text element, which is why the stretch could not simply be
        /// added to the rule.</b> <c>.req-label</c> gets <c>scale: 1 1.2</c> directly because a label
        /// is only text; doing that to <c>.text-button</c> would stretch the wooden chrome and the
        /// bevel with it. So the caption moves to a child and the chrome stays unscaled.
        ///
        /// <para><b>Without this, every button in the game rendered a sixth too short.</b> Canonical
        /// space is anisotropic — x5 across against x6 down — and a font renderer scales
        /// isotropically, so text sized for correct ADVANCES needs stretching back vertically.
        /// <see cref="Apply"/> has always done that for labels; buttons were the surface it could not
        /// reach.</para>
        ///
        /// <para><b>The caption is inert to the pointer.</b> It sits inside a clickable element, and
        /// a child that takes hits changes what the click target is — the manipulators and the nav
        /// widgets are all registered on the chrome.</para>
        ///
        /// <para><b>Colour and shadow are INHERITED rather than restated.</b> Both are inherited
        /// properties in UI Toolkit, so <c>.keyword-asked</c>'s recolouring of an already-asked topic
        /// still reaches the caption without the rule having to know this structure exists.</para>
        /// </remarks>
        internal static Label Caption(VisualElement chrome, string text,
            AnchorX anchorX = AnchorX.Centre) {
            var caption = new Label(text) {
                name = "caption",
                pickingMode = PickingMode.Ignore,
            };
            // The anchor is the transform origin: a centred button caption scales about its middle,
            // a left-aligned row about its left edge, so neither drifts sideways as it stretches.
            Apply(caption, anchorX, AnchorY.Middle);
            // Apply wants PreWrap because dialog prose needs its spaces; a button caption is sized
            // by the REQ's own rect and must not reflow inside it.
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            chrome.Add(caption);
            return caption;
        }
    }
}
