namespace BakAgain.UI {
    using BakAgain.Graphics;
    using System.Collections.Generic;
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
    /// <para><b>Size.</b> <see cref="FontSizePx"/>, from the font's own cell and pixel width — or, for
    /// Enhanced mode's book font, from the line pitch (<see cref="GameFonts.IsBookFont"/>).</para>
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
                GameFonts.Build(); // IsBookFont is decided by the build
                if (GameFonts.IsBookFont) {
                    // A real typeface has no cell to count: its em is set from the line pitch it must
                    // fill, and it is drawn with its own (square) proportions — no stretch.
                    return BookFontLayout.EmPerLinePitch * LinePitchPx(DialogLineGapVgaRows);
                }
                return GameFntSizePx;
            }
        }

        // GAME.FNT's own size: FntTrueType's em, the cell plus one row in pixel widths.
        private static float GameFntSizePx {
            get {
                GameData.Resources.Font.FontResource font = GameFonts.GameFont;
                return (float)((font.Height + 1) * font.PixelWidth);
            }
        }

        /// <summary>
        /// A size chosen for GAME.FNT (layout data written against its metrics), in the font that
        /// draws: unchanged with GAME.FNT; with the book font, scaled by the book em over GAME.FNT's,
        /// as <see cref="FontSizePx"/> itself is.
        /// </summary>
        internal static float InDrawnFont(float gameFontSizePx) =>
            GameFonts.IsBookFont ? gameFontSizePx * FontSizePx / GameFntSizePx : gameFontSizePx;

        private static readonly GameData.Resources.Layout.EnhancedFontLayout BookFontLayout = new();

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
            caption.AddToClassList(CaptionClass);
            // Apply wants PreWrap because dialog prose needs its spaces; a button caption is sized
            // by the REQ's own rect and must not reflow inside it.
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            chrome.Add(caption);
            caption.RegisterCallback<GeometryChangedEvent>(_ => FitWidth(caption, chrome));
            return caption;
        }

        /// <summary>The smallest a caption shrinks to, as a share of the game font's size.</summary>
        internal const float MinFitScale = 0.6f;

        /// <summary>
        /// A caption wider than its button is drawn smaller until it fits — the fit policy for fixed
        /// captions (TASK-779). A translation runs a third past English and the REQ rects were drawn
        /// around the English words.
        /// </summary>
        /// <remarks>
        /// <b>A caption that fits keeps the game font's own size exactly</b>, so English, which was
        /// laid out to fit, is untouched. Re-run on every geometry change, so a caption whose text
        /// changes later (the touch buttons' Cast/Thrust) refits; it only writes when the size
        /// actually moves, which is what stops the callback feeding itself.
        ///
        /// <para><b>With the book font, one size per screen.</b> Its widths are not the ones the
        /// buttons were drawn around, so several captions shrink, and each shrinking on its own put
        /// four sizes on one menu. Every caption on the screen (the UIDocument the button is in)
        /// takes the smallest of their fits instead. GAME.FNT keeps the per-caption fit unchanged.</para>
        /// </remarks>
        internal static void FitWidth(Label caption, VisualElement box) {
            if (GameFonts.IsBookFont) {
                FitScreen(ScreenOf(caption));
                return;
            }
            float scale = FitScale(caption, box);
            if (!float.IsNaN(scale)) {
                Resize(caption, FontSizePx * scale);
            }
        }

        /// <summary>The class every <see cref="Caption"/> label carries, so a screen can find its captions.</summary>
        internal const string CaptionClass = "game-caption";

        /// <summary>One caption size for every caption under <paramref name="screen"/>: the smallest fit.</summary>
        /// <remarks>"Screen" is the caption's UIDocument — so a dialog overlay's choice, keyword and
        /// farewell buttons share one size, and the Preferences Enhanced panel shares the screen's.</remarks>
        // ponytail: every caption's geometry change remeasures all N captions on its screen (N² text
        // measures per layout pass); fine for menus of a few dozen buttons, cache per-caption fits
        // and refit once per frame if a screen with hundreds of captions ever appears.
        internal static void FitScreen(VisualElement screen) {
            List<Label> captions = screen.Query<Label>(className: CaptionClass).ToList();
            float scale = 1f;
            foreach (Label c in captions) {
                // A button that is not drawn does not get a say in the screen's size.
                if (c.resolvedStyle.display == DisplayStyle.None || c.resolvedStyle.visibility == Visibility.Hidden
                    || c.parent?.resolvedStyle.display == DisplayStyle.None) {
                    continue;
                }
                float s = FitScale(c, c.parent);
                if (!float.IsNaN(s)) {
                    scale = Mathf.Min(scale, s);
                }
            }
            foreach (Label c in captions) {
                Resize(c, FontSizePx * scale);
            }
        }

        // The top of the caption's own UIDocument: the element just under the panel's root.
        private static VisualElement ScreenOf(VisualElement element) {
            while (element.parent != null && element.parent.parent != null) {
                element = element.parent;
            }
            return element;
        }

        private static void Resize(Label caption, float size) {
            if (Mathf.Abs(size - caption.resolvedStyle.fontSize) > 0.5f) {
                caption.style.fontSize = size;
            }
        }

        // The scale (of FontSizePx) at which the caption fits its box, or NaN when it cannot be told yet.
        private static float FitScale(Label caption, VisualElement box) {
            float room = box?.contentRect.width ?? 0f;
            float current = caption.resolvedStyle.fontSize;
            if (room <= 0f || current <= 0f || string.IsNullOrEmpty(caption.text)) {
                return float.NaN;
            }
            float full = FontSizePx;
            Vector2 measured = caption.MeasureTextSize(caption.text, 0f, VisualElement.MeasureMode.Undefined,
                0f, VisualElement.MeasureMode.Undefined);
            float natural = measured.x * full / current;
            if (natural * MinFitScale > room + 0.5f) {
                TextOverflowReport.Report(TextOverflowReport.Caption, caption.text,
                    $"{natural * MinFitScale:0} px at the smallest fit, {room:0} px of room");
            }
            return natural <= room ? 1f : Mathf.Max(MinFitScale, room / natural);
        }
    }
}
