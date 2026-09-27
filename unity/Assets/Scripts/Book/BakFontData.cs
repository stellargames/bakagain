namespace BakAgain.Book {
    using BakAgain.Graphics;

    /// <summary>
    /// Character width tables extracted from the original game's FNT files.
    /// Used by BakTextWrapper to replicate exact line-breaking behavior.
    /// </summary>
    public static class BakFontData {
        // BOOK.FNT: height=15, baseline=10, firstChar=32 (space), 96 characters
        // Proportional serif font used for book/scroll text at 640x350 EGA resolution.
        private static readonly byte[] BookWidths = {
            // 32-47:  sp  !  "  #  $  %  &  '  (  )  *  +  ,  -  .  /
                        8, 3, 6, 9, 8,12,10, 3, 5, 5, 8, 8, 3, 8, 3,11,
            // 48-63:   0  1  2  3  4  5  6  7  8  9  :  ;  <  =  >  ?
                        7, 7, 8, 7, 9, 7, 7, 8, 7, 7, 3, 3, 6, 7, 6, 7,
            // 64-79:   @  A  B  C  D  E  F  G  H  I  J  K  L  M  N  O
                       11,14,10, 8,11,11,10,11,13, 9, 6,13,10,15,12, 9,
            // 80-95:   P  Q  R  S  T  U  V  W  X  Y  Z  [  \  ]  ^  _
                       10,10,13, 8,10,12,10,13,14, 9,10, 5, 7, 5, 8, 9,
            // 96-111:  `  a  b  c  d  e  f  g  h  i  j  k  l  m  n  o
                        8,10, 8, 7,11, 8, 8, 8,10, 7, 5,10, 6,15,11, 8,
            // 112-127: p  q  r  s  t  u  v  w  x  y  z  {  |  }  ~ DEL
                        9, 9, 7, 7, 7,11, 9,14,11, 9, 9, 6, 2, 6, 1, 1,
        };

        private const int BookFirstChar = 32;
        public const int BookFontHeight = 15;

        // GAME.FNT: height=10, baseline=8, firstChar=32 (space), 95 characters
        // Compact UI font used for in-game text at 320x200 VGA resolution.
        private static readonly byte[] GameWidths = {
            // 32-47:  sp  !  "  #  $  %  &  '  (  )  *  +  ,  -  .  /
                        4, 2, 4, 8, 6, 7, 7, 3, 3, 3, 6, 6, 3, 4, 2, 4,
            // 48-63:   0  1  2  3  4  5  6  7  8  9  :  ;  <  =  >  ?
                        5, 4, 5, 5, 5, 5, 5, 5, 5, 5, 2, 3, 4, 4, 4, 5,
            // 64-79:   @  A  B  C  D  E  F  G  H  I  J  K  L  M  N  O
                        7, 5, 5, 5, 5, 5, 5, 5, 5, 4, 5, 5, 5, 8, 5, 5,
            // 80-95:   P  Q  R  S  T  U  V  W  X  Y  Z  [  \  ]  ^  _
                        5, 5, 5, 5, 6, 5, 6, 8, 6, 5, 5, 3, 4, 3, 4, 5,
            // 96-110:  `  a  b  c  d  e  f  g  h  i  j  k  l  m  n
                        3, 5, 5, 5, 5, 5, 5, 5, 5, 2, 5, 5, 2, 8, 5,
            // 111-126: o  p  q  r  s  t  u  v  w  x  y  z  {  |  }  ~
                        5, 5, 5, 5, 5, 4, 5, 5, 8, 5, 5, 5, 4, 2, 4, 5,
        };

        private const int GameFirstChar = 32;
        public const int GameFontHeight = 10;

        /// <summary>
        /// Returns the canonical-px (1280×960 book space) advance width of a
        /// character in the specified font. Font 0 = BOOK.FNT, Font 1+ =
        /// GAME.FNT (fallback). The width tables above are raw FNT pixels (RE
        /// data — keep them as extracted); the EGA horizontal factor converts
        /// an advance to canonical px so wrap math compares like-for-like with
        /// canonical BOK coordinates. The integer factor preserves the DOS
        /// wrapper's exact break decisions.
        /// </summary>
        public static int GetCharWidth(char ch, int fontIndex) {
            return GetRawCharWidth(ch, fontIndex) * Canonical.EgaScaleX;
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
        /// <para>Callers that lay text out in a canonical space scale this themselves by that
        /// space's horizontal factor: <see cref="Canonical.EgaScaleX"/> for the 1280×960 book
        /// frame (see <see cref="GetCharWidth"/>), <see cref="Canonical.VgaScaleX"/> for the
        /// 1600×1200 UI frame. Wrap decisions are made in raw FNT px, where they are exactly the
        /// DOS wrapper's integer comparisons.</para>
        ///
        /// <para>The one metric this does NOT reproduce is the tab: <c>getCharMetrics</c> gives
        /// <c>'\t'</c> the current <c>tabWidth</c> (<c>0x16176</c>) rather than 0. That global is
        /// set per dialog style from <c>dialogTypeData.field_B</c> (<c>0x490b7</c>), so it is not
        /// a font property and does not belong in a font table — see
        /// <c>DialogTextFormatter</c>, which resolves tabs before anything measures them.</para>
        /// </summary>
        public static int GetRawCharWidth(char ch, int fontIndex) {
            byte[] widths;
            int firstChar;

            if (fontIndex == 0) {
                widths = BookWidths;
                firstChar = BookFirstChar;
            } else {
                widths = GameWidths;
                firstChar = GameFirstChar;
            }

            int index = ch - firstChar;
            if (index < 0 || index >= widths.Length)
                return 0;
            return widths[index];
        }

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
        public static int GetFontHeight(int fontIndex) {
            return fontIndex == 0 ? BookFontHeight : GameFontHeight;
        }
    }
}
