namespace BakAgain.Book {

    /// <summary>
    /// Character widths and heights of the two text fonts, read from the extracted FNT resources.
    /// Used by BakTextWrapper and every self-sizing label to replicate the original's measurement.
    /// </summary>
    /// <remarks>
    /// <b>Read from the font, not from a copy of it (TASK-765).</b> This used to carry BOOK.FNT's
    /// and GAME.FNT's width tables as literals, so a replacement font rendered with its own glyphs
    /// but wrapped and sized to the original's widths. <see cref="BakAgain.UI.GameFonts"/> loads the
    /// resources synchronously on first use, so the measurement is available whenever text is laid
    /// out.
    /// </remarks>
    public static class BakFontData {
        private static GameData.Resources.Font.FontResource Font(int fontIndex) =>
            fontIndex == 0 ? BakAgain.UI.GameFonts.BookFont : BakAgain.UI.GameFonts.GameFont;

        /// <summary>
        /// Returns the canonical-px (1280×960 book space) advance width of a
        /// character in the specified font. Font 0 = BOOK.FNT, Font 1+ =
        /// GAME.FNT (fallback). The raw width times the font's own pixel width
        /// (2 for BOOK.FNT's EGA pixels), so wrap math compares like-for-like
        /// with canonical BOK coordinates; an integer factor preserves the DOS
        /// wrapper's exact break decisions.
        /// </summary>
        public static int GetCharWidth(char ch, int fontIndex) {
            return (int)System.Math.Round(GetRawCharWidth(ch, fontIndex) * Font(fontIndex).PixelWidth);
        }

        /// <summary>
        /// The character's advance in RAW FNT pixels — the table entry itself, with no space
        /// conversion applied. This is <c>getCharMetrics</c> (<c>0x1613e</c>): index the width
        /// table by <c>ch - firstChar</c> and return 0 for anything outside it.
        ///
        /// <para>Zero for out-of-range is not a fallback, it is the engine's answer. The original
        /// widens the char to a SIGNED int before subtracting <c>firstChar</c> (<c>0x16149</c>),
        /// so every byte ≥ 0x80 — which is what DDX's inline formatting codes are, surfaced as
        /// CP437 glyphs — goes negative and takes the out-of-range branch. Those codes therefore
        /// occupy no width in the original's own wrap and width sums, and must not here either.</para>
        ///
        /// <para>Callers that lay text out in canonical space scale this by the font's
        /// <c>PixelWidth</c> (see <see cref="GetCharWidth"/>). Wrap decisions are made in raw FNT
        /// px, where they are exactly the DOS wrapper's integer comparisons.</para>
        ///
        /// <para>The one metric this does NOT reproduce is the tab: <c>getCharMetrics</c> gives
        /// <c>'\t'</c> the current <c>tabWidth</c> (<c>0x16176</c>) rather than 0. That global is
        /// set per dialog style from <c>dialogTypeData.field_B</c> (<c>0x490b7</c>), so it is not
        /// a font property and does not belong in a font table — see
        /// <c>DialogTextFormatter</c>, which resolves tabs before anything measures them.</para>
        /// </summary>
        public static int GetRawCharWidth(char ch, int fontIndex) =>
            // GlyphFor answers null outside the font, which is the zero the engine gives.
            Font(fontIndex).GlyphFor(ch)?.Width ?? 0;

        /// <summary>
        /// A whole string's width in RAW FNT pixels — <c>getStringWidthInPixels</c> (@0x15be5),
        /// reached through <c>calculateTextWidth</c> (@0x15bd4).
        /// </summary>
        /// <remarks>
        /// <b>A plain sum, with NO inter-character spacing.</b> That is the difference between this
        /// and the wrapping loops in <c>BakTextWrapper</c>, which add an <c>extraCharSpacing</c>
        /// term per character: they are a different original routine, and borrowing their arithmetic
        /// here would widen every measured label by its length. Everything that SIZES ITSELF to its
        /// text goes through this one — the dialog button row, the menu entries, the keyword grid.
        ///
        /// <para>Out-of-range characters contribute nothing, which is
        /// <see cref="GetRawCharWidth"/>'s own rule and the engine's answer rather than a fallback.</para>
        /// </remarks>
        public static int MeasureRaw(string text, int fontIndex) {
            if (string.IsNullOrEmpty(text)) {
                return 0;
            }
            var width = 0;
            foreach (char ch in text) {
                width += GetRawCharWidth(ch, fontIndex);
            }
            return width;
        }

        /// <summary>The widest of several strings in RAW FNT pixels.</summary>
        /// <remarks>
        /// One width for a whole row of buttons: <c>CreateMenuEntriesFromDialogData</c> keeps a
        /// running maximum over every label (@0x4b2cf) and gives every button that same width, so
        /// they come out uniform rather than fitted to their own text.
        ///
        /// <para><b>THIS IS THE ONLY TEXT MEASUREMENT IN THE PROJECT — do not write a second one in
        /// GameData.</b> One was written there (<c>Font.FontMetrics</c>, against
        /// <c>FontResource</c>) and deleted on 2026-08-30: same routine, same two IDA addresses,
        /// same remarks, found only by an unconsumed-model audit. It looks like the better-layered
        /// home right up until you need a width, because this class carries the FNT tables as DATA
        /// and so answers with no font loaded — which is what the dialog row needs, being placed as
        /// it is built, before any font resource resolves and while UI Toolkit's own measurement
        /// still returns zero. A GameData twin has to be handed a FontResource that does not exist
        /// yet at the moment of the call.</para>
        /// </remarks>
        public static int WidestRaw(System.Collections.Generic.IEnumerable<string> texts, int fontIndex) {
            var widest = 0;
            if (texts == null) {
                return widest;
            }
            foreach (string text in texts) {
                int width = MeasureRaw(text, fontIndex);
                if (width > widest) {
                    widest = width;
                }
            }
            return widest;
        }

        /// <summary>The font slot the UI draws in: GAME.FNT, selected once at boot and never
        /// swapped (<c>InitializeGameHardware</c> @0x41a52). Slot 0 is BOOK.FNT.</summary>
        public const int GameFontIndex = 1;

        /// <summary>
        /// Returns the line height for the specified font.
        /// </summary>
        public static int GetFontHeight(int fontIndex) => Font(fontIndex).Height;
    }
}
