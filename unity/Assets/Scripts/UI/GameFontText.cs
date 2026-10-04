namespace BakAgain.UI {
    using BakAgain.Graphics;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Styles a label as the original's plain game font: its size, and whitespace kept as content.
    /// </summary>
    /// <remarks>
    /// <b>No aspect stretch any more (TASK-765).</b> This used to scale every label 1.2x vertically,
    /// because the font was built with square pixels while the original's are 5 x 6 canonical units.
    /// The font now declares that shape (<c>FontResource.PixelWidth/PixelHeight</c>, set by the
    /// extractor) and <c>FntTrueType</c> builds its glyphs at it, so text renders at the original's
    /// proportions with nothing applied afterwards — and a square-pixel mod font is not stretched.
    ///
    /// <para><b>Size.</b> <see cref="FontSizePx"/>, from the font's own cell and pixel width.</para>
    /// </remarks>
    internal static class GameFontText {
        /// <summary>
        /// The height of one line of game text in canonical px — GAME.FNT's character CELL, which
        /// is what <c>font_DrawWrappedTextBlock</c> (@0x4b94e) reads out of the selected font to
        /// advance by. Callers that need to place something BELOW a line of text want this, not
        /// <see cref="FontSizePx"/>: a font size is an em, and once the stretch
        /// above is applied it is neither the width nor the height of anything on screen.
        ///
        /// <para>Note this is the cell only. Text that FLOWS over several lines advances by the
        /// cell plus a leading the caller states — see <see cref="LinePitchPx"/>.</para>
        /// </summary>
        internal static float LineHeightPx => LinePitchPx(0);

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
        internal static float LinePitchPx(int lineGapExtraVgaRows) {
            // In the font's own rows, at the height the font says a row is (TASK-765).
            GameData.Resources.Font.FontResource font = GameFonts.GameFont;
            return (float)((font.Height + lineGapExtraVgaRows) * font.PixelHeight);
        }

        /// <summary>
        /// The game font's size in canonical px: <c>FntTrueType</c>'s em is the cell plus one row,
        /// counted in pixel WIDTHS, so this is (10 + 1) x 5 = 55 for the shipped GAME.FNT — the size
        /// whose advances are the original's widths exactly (task-45, TASK-765).
        /// </summary>
        internal static float FontSizePx {
            get {
                GameData.Resources.Font.FontResource font = GameFonts.GameFont;
                return (float)((font.Height + 1) * font.PixelWidth);
            }
        }

        /// <summary>Apply the game font's size and whitespace handling to a label.</summary>
        internal static void Apply(VisualElement element) {
            if (element == null) {
                return;
            }
            element.style.fontSize = FontSizePx;
            // Whitespace is CONTENT in this game's text, not formatting. The original advances the
            // pen by the space's width as many times as there are spaces, and DialogTextFormatter
            // renders the original's tab as a run of them — so a text engine that collapses runs
            // and trims the leading ones (CSS `white-space: normal`, which UI Toolkit began
            // honouring in 6.5) deletes every paragraph indent in the game. PreWrap preserves the
            // run and still allows wrapping for the surfaces that want it.
            element.style.whiteSpace = WhiteSpace.PreWrap;
        }

        /// <summary>
        /// Puts a button's caption in an INNER label in the game font, and returns it.
        /// </summary>
        /// <param name="chrome">
        /// The element wearing the chrome — its background, borders and bevel. A
        /// <c>.text-button</c>, or a <c>.file-picker-row</c>, which has the same shape.
        /// </param>
        /// <remarks>
        /// <b>The caption is inert to the pointer.</b> It sits inside a clickable element, and a
        /// child that takes hits changes what the click target is — the manipulators and the nav
        /// widgets are all registered on the chrome.
        ///
        /// <para><b>Colour and shadow are INHERITED rather than restated.</b> Both are inherited
        /// properties in UI Toolkit, so <c>.keyword-asked</c>'s recolouring of an already-asked topic
        /// still reaches the caption without the rule having to know this structure exists.</para>
        /// </remarks>
        internal static Label Caption(VisualElement chrome, string text) {
            var caption = new Label(text) {
                name = "caption",
                pickingMode = PickingMode.Ignore,
            };
            Apply(caption);
            // Apply wants PreWrap because dialog prose needs its spaces; a button caption is sized
            // by the REQ's own rect and must not reflow inside it.
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            chrome.Add(caption);
            return caption;
        }
    }
}
