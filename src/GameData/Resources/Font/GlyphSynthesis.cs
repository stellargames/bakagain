namespace GameData.Resources.Font;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Makes the letters a translation needs out of the font's own (TASK-778): an accented letter is its
/// base letter with a small pixel mark stamped on, so Dutch, German, French, Spanish, Scandinavian,
/// Polish or Czech text draws in the player's own font with no font file at all.
/// </summary>
/// <remarks>
/// <para><b>Unicode already says how a letter is built.</b> Canonical decomposition (NFD) splits
/// <c>ä</c> into <c>a</c> + U+0308, <c>ç</c> into <c>c</c> + U+0327; the marks are drawn here, a
/// handful of pixels each, and placed above the base letter's ink with a row between when the cell
/// has room, touching when it has less, and in the top row when it has none (a capital in GAME.FNT
/// has one free row). A few letters that do not decompose are built too: <c>ø</c> (a stroke), the
/// <c>æ</c>/<c>œ</c> ligatures (two letters joined), and typographic punctuation (curly quotes,
/// dashes, the ellipsis) from its ASCII counterpart.</para>
///
/// <para><b>Only what is missing</b>, and never over a glyph a pack drew itself. What cannot be
/// built — <c>ß</c>, other scripts — is returned, so the loader can say which letters will not draw.</para>
/// </remarks>
public static class GlyphSynthesis {
    // A mark's variants, tallest first; '#' is ink.
    private sealed record Mark(bool Above, string[][] Variants);

    private static readonly Dictionary<int, Mark> Marks = new() {
        [0x0300] = new(true, new[] { new[] { "#.", ".#" }, new[] { "#." } }),            // grave
        [0x0301] = new(true, new[] { new[] { ".#", "#." }, new[] { ".#" } }),            // acute
        [0x0302] = new(true, new[] { new[] { ".#.", "#.#" }, new[] { "###" } }),         // circumflex
        [0x0303] = new(true, new[] { new[] { ".#.#", "#.#." }, new[] { "###" } }),       // tilde
        [0x0304] = new(true, new[] { new[] { "###" } }),                                 // macron
        [0x0306] = new(true, new[] { new[] { "#.#", ".#." }, new[] { "#.#" } }),         // breve
        [0x0307] = new(true, new[] { new[] { "#" } }),                                   // dot above
        [0x0308] = new(true, new[] { new[] { "#.#" } }),                                 // diaeresis
        [0x030A] = new(true, new[] { new[] { ".#.", "#.#", ".#." }, new[] { "###" } }),  // ring
        [0x030B] = new(true, new[] { new[] { ".#.#", "#.#." }, new[] { "#.#" } }),       // double acute
        [0x030C] = new(true, new[] { new[] { "#.#", ".#." }, new[] { ".#." } }),         // caron
        [0x0326] = new(false, new[] { new[] { ".#", "#." } }),                           // comma below
        [0x0327] = new(false, new[] { new[] { ".#", "#." } }),                           // cedilla
        [0x0328] = new(false, new[] { new[] { "#.", ".#" } }),                           // ogonek
    };

    /// <summary>Letters with a stroke through the base letter.</summary>
    private static readonly Dictionary<int, char> Stroked = new() {
        ['ø'] = 'o', ['Ø'] = 'O', ['ł'] = 'l', ['Ł'] = 'L',
    };

    /// <summary>Two letters joined into one, sharing a column.</summary>
    private static readonly Dictionary<int, string> Joined = new() {
        ['æ'] = "ae", ['Æ'] = "AE", ['œ'] = "oe", ['Œ'] = "OE",
    };

    /// <summary>Characters drawn as several side by side, each keeping its own advance.</summary>
    private static readonly Dictionary<int, string> Sequence = new() {
        ['…'] = "...",
    };

    /// <summary>Typographic punctuation drawn as its ASCII counterpart.</summary>
    private static readonly Dictionary<int, char> Plain = new() {
        ['‘'] = '\'', ['’'] = '\'', ['‚'] = ',', ['“'] = '"', ['”'] = '"', ['„'] = '"',
        ['–'] = '-', ['—'] = '-', ['«'] = '<', ['»'] = '>', [' '] = ' ',
    };

    /// <summary>
    /// Add a glyph for every character in <paramref name="characters"/> the font lacks and can be
    /// built from it.
    /// </summary>
    /// <returns>The characters still missing, in first-seen order.</returns>
    public static IReadOnlyList<int> AddComposed(FontResource font, IEnumerable<int> characters) {
        var missing = new List<int>();
        foreach (int c in characters.Distinct()) {
            if (Draws(font, c) || c < 0x20) {
                continue;
            }
            Grid? made = Build(font, c);
            if (made == null) {
                missing.Add(c);
            } else {
                font.ExtraGlyphs[c] = made.ToGlyph();
            }
        }
        return missing;
    }

    /// <summary>
    /// Whether the font already draws <paramref name="c"/>. Outside ASCII a glyph with no ink is
    /// not the letter: PUZZLE.FNT and ALIEN.FNT carry 251 glyphs from character 0, mostly empty
    /// placeholders, and reading those as letters left a riddle's "käme" drawn as "kme".
    /// </summary>
    private static bool Draws(FontResource font, int c) {
        FontGlyph? glyph = font.GlyphFor(c);
        if (glyph == null) {
            return false;
        }
        if (c < 0x80 || font.ExtraGlyphs.ContainsKey(c)) {
            return true;
        }
        for (int y = 0; y < glyph.Rows.Count; y++) {
            for (int x = 0; x < glyph.Width; x++) {
                if (glyph.PixelAt(x, y) != 0) {
                    return true;
                }
            }
        }
        return false;
    }

    private static Grid? Build(FontResource font, int c) {
        if (Plain.TryGetValue(c, out char plain)) {
            return Grid.Of(font, plain);
        }
        bool shared = Joined.TryGetValue(c, out string? parts);
        if (shared || Sequence.TryGetValue(c, out parts)) {
            Grid? joined = Grid.Of(font, parts![0]);
            for (int i = 1; joined != null && i < parts.Length; i++) {
                Grid? next = Grid.Of(font, parts[i]);
                joined = next == null ? null : joined.Join(next, shared);
            }
            return joined;
        }
        if (c == 'ß' || c == 'ẞ') {
            Grid? sharp = Grid.Of(font, 'B');
            sharp?.ReshapeIntoSharpS();
            return sharp;
        }
        if (Stroked.TryGetValue(c, out char stroked)) {
            Grid? grid = Grid.Of(font, stroked);
            grid?.Stroke(char.ToLowerInvariant(stroked) == 'l');
            return grid;
        }
        if (c > 0xFFFF) {
            return null;
        }
        string parts2 = ((char)c).ToString().Normalize(NormalizationForm.FormD);
        if (parts2.Length < 2 || !parts2.Skip(1).All(m => Marks.ContainsKey(m))) {
            return null;
        }
        Grid? baseGrid = Grid.Of(font, parts2[0]);
        if (baseGrid == null) {
            return null;
        }
        if ((parts2[0] == 'i' || parts2[0] == 'j') && parts2.Skip(1).Any(m => Marks[m].Above)) {
            baseGrid.RemoveTittle();
        }
        (int left, int right) = baseGrid.InkColumns();
        foreach (char m in parts2.Skip(1)) {
            baseGrid.Stamp(Marks[m], left, right);
        }
        return baseGrid;
    }

    /// <summary>A glyph as a mutable pixel grid.</summary>
    private sealed class Grid {
        private bool[,] _ink;

        private Grid(int height, int width) {
            Height = height;
            Width = width;
            _ink = new bool[height, width];
        }

        private int Height { get; }
        private int Width { get; set; }

        public static Grid? Of(FontResource font, char c) {
            FontGlyph? g = font.GlyphFor(c);
            if (g == null) {
                return null;
            }
            var grid = new Grid(font.Height, g.Width);
            for (int y = 0; y < font.Height; y++) {
                for (int x = 0; x < g.Width; x++) {
                    grid._ink[y, x] = g.PixelAt(x, y) != 0;
                }
            }
            return grid;
        }

        private bool RowHasInk(int y) => Enumerable.Range(0, Width).Any(x => _ink[y, x]);

        private int TopInk() {
            int y = 0;
            while (y < Height && !RowHasInk(y)) {
                y++;
            }
            return y;
        }

        private int BottomInk() {
            int y = Height - 1;
            while (y >= 0 && !RowHasInk(y)) {
                y--;
            }
            return y;
        }

        public (int Left, int Right) InkColumns() {
            int left = Width, right = -1;
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    if (_ink[y, x]) {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                    }
                }
            }
            return right < 0 ? (0, Math.Max(0, Width - 1)) : (left, right);
        }

        /// <summary>Clear the dot of an i or j — the ink above the first empty row under the top.</summary>
        public void RemoveTittle() {
            int top = TopInk();
            int y = top;
            while (y < Height && RowHasInk(y)) {
                y++;
            }
            if (y >= Height || BottomInk() < y) {
                return; // no gap: nothing that is a separate dot
            }
            for (int r = top; r < y; r++) {
                for (int x = 0; x < Width; x++) {
                    _ink[r, x] = false;
                }
            }
        }

        public void Stamp(Mark mark, int left, int right) {
            string[] rows = mark.Variants[^1];
            int top = -1;
            if (mark.Above) {
                int baseTop = TopInk();
                foreach (string[] variant in mark.Variants) {
                    for (int gap = 1; gap >= 0 && top < 0; gap--) {
                        if (baseTop - gap - variant.Length >= 0) {
                            rows = variant;
                            top = baseTop - gap - variant.Length;
                        }
                    }
                    if (top >= 0) {
                        break;
                    }
                }
                if (top < 0) {
                    // No room even touching: squash the letter by the rows the mark and a gap need.
                    Squash(rows.Length + 1 - baseTop);
                    top = Math.Max(0, TopInk() - 1 - rows.Length);
                }
            } else {
                rows = mark.Variants[0];
                top = BottomInk() + 1;
            }
            int width = rows[0].Length;
            int x0 = Math.Max(0, left + (right - left + 1 - width) / 2);
            Widen(x0 + width);
            for (int r = 0; r < rows.Length; r++) {
                for (int x = 0; x < width; x++) {
                    if (rows[r][x] == '#' && top + r < Height) {
                        _ink[top + r, x0 + x] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Lower a letter's top by <paramref name="rows"/>, keeping its baseline: drop rows from its
        /// upper half, the one that differs least from the row below it (a capital's straight
        /// sides), so the shape loses height where it is least visible.
        /// </summary>
        private void Squash(int rows) {
            for (int n = 0; n < rows; n++) {
                int top = TopInk(), bottom = BottomInk();
                if (bottom - top < 2) {
                    return;
                }
                int middle = (top + bottom) / 2;
                int drop = middle, fewest = int.MaxValue;
                for (int y = middle; y > top; y--) {
                    int differ = Enumerable.Range(0, Width).Count(x => _ink[y, x] != _ink[y + 1, x]);
                    if (differ < fewest) {
                        fewest = differ;
                        drop = y;
                    }
                }
                for (int y = drop; y > 0; y--) {
                    for (int x = 0; x < Width; x++) {
                        _ink[y, x] = _ink[y - 1, x];
                    }
                }
                for (int x = 0; x < Width; x++) {
                    _ink[0, x] = false;
                }
            }
        }

        /// <summary>
        /// ß from B: the same stem and two bowls at ascender height, reshaped so it stops reading as a
        /// B — the stem's top corner rounded into an arch, and the bars that close the bowls onto the
        /// stem (the middle and the bottom) opened by one pixel, as pixel fonts draw an ß.
        /// </summary>
        public void ReshapeIntoSharpS() {
            int top = TopInk();
            // The stem: the column with ink on the most rows, and its neighbours nearly as full.
            int[] inkRows = Enumerable.Range(0, Width)
                .Select(x => Enumerable.Range(0, Height).Count(y => _ink[y, x])).ToArray();
            int most = inkRows.Max();
            int stem = Array.IndexOf(inkRows, most);
            int stemRight = stem;
            while (stemRight + 1 < Width && inkRows[stemRight + 1] >= most - 2) {
                stemRight++;
            }
            for (int y = top + 2; y < Height && stemRight + 1 < Width; y++) {
                if (_ink[y, stemRight] && _ink[y, stemRight + 1]) {
                    _ink[y, stemRight + 1] = false;
                }
            }
            for (int x = 0; x < Width; x++) {
                if (_ink[top, x]) {
                    if (x <= stem) {
                        _ink[top, x] = false;
                    }
                    break;
                }
            }
        }

        /// <summary>A stroke: a diagonal across a round letter, a short bar across a stem.</summary>
        public void Stroke(bool acrossStem) {
            (int left, int right) = InkColumns();
            int top = TopInk(), bottom = BottomInk();
            if (acrossStem) {
                int stem = left;
                int y = (top + bottom + 1) / 2;
                Widen(stem + 2);
                for (int x = Math.Max(0, stem - 1); x <= stem + 1; x++) {
                    _ink[y, x] = true;
                }
                return;
            }
            for (int y = top; y <= bottom; y++) {
                int x = left + (right - left) * (bottom - y) / Math.Max(1, bottom - top);
                _ink[y, x] = true;
            }
        }

        /// <summary>This letter followed by <paramref name="next"/> — sharing one column for a
        /// ligature, at its full advance otherwise.</summary>
        public Grid Join(Grid next, bool shareColumn) {
            int offset = shareColumn ? InkColumns().Right : Width;
            var joined = new Grid(Height, offset + next.Width);
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    joined._ink[y, x] |= _ink[y, x];
                }
                for (int x = 0; x < next.Width; x++) {
                    joined._ink[y, offset + x] |= next._ink[y, x];
                }
            }
            return joined;
        }

        private void Widen(int width) {
            if (width <= Width) {
                return;
            }
            var wider = new bool[Height, width];
            for (int y = 0; y < Height; y++) {
                for (int x = 0; x < Width; x++) {
                    wider[y, x] = _ink[y, x];
                }
            }
            _ink = wider;
            Width = width;
        }

        public FontGlyph ToGlyph() {
            int bytes = Math.Max(1, (Width + 7) / 8);
            var glyph = new FontGlyph { Width = Width, BytesPerRow = bytes };
            for (int y = 0; y < Height; y++) {
                var row = new byte[bytes];
                for (int x = 0; x < Width; x++) {
                    if (_ink[y, x]) {
                        row[x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
                glyph.Rows.Add(row);
            }
            return glyph;
        }
    }
}
