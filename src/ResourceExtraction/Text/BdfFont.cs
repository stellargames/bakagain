namespace ResourceExtraction.Text;

using GameData.Resources.Font;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// Reads a language pack's pixel font — a standard BDF file (Adobe's Glyph Bitmap Distribution
/// Format, which FontForge, Bits'n'Picas and every X11 tool write) — into the game's own font
/// (TASK-778).
/// </summary>
/// <remarks>
/// <b>Merged, not substituted.</b> A pack adds the letters its language needs, and may redraw
/// existing ones; everything else keeps the player's own glyphs. The glyphs are taken in the game
/// font's pixels and sit on its baseline, so the pack's letters are measured and drawn exactly as
/// the originals are, at the shape the font declares.
///
/// <para><b>The cell does not grow.</b> The line pitch, the font size and every box are measured
/// from the original cell, so ink above or below it is clipped and reported — a pack font is drawn
/// to fit the original's rows, as the original's own capitals are.</para>
/// </remarks>
public static class BdfFont {
    /// <summary>Merge the BDF's encoded glyphs into <paramref name="font"/>.</summary>
    /// <returns>The code points whose ink did not fit the cell.</returns>
    public static IReadOnlyList<int> MergeInto(FontResource font, TextReader bdf) {
        if (font.PixelFormat != FontPixelFormat.Monochrome) {
            throw new ArgumentException($"{font.Id} is not a monochrome font.", nameof(font));
        }
        int baseline = font.CapitalBaseline();
        var clipped = new List<int>();

        // Merged only once the whole file has read: a file that breaks part-way changes nothing.
        var added = new Dictionary<int, FontGlyph>();
        int encoding = -1, advance = -1, w = 0, h = 0, xOff = 0, yOff = 0;
        string? line;
        while ((line = bdf.ReadLine()) != null) {
            string[] f = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0) {
                continue;
            }
            switch (f[0]) {
                case "STARTCHAR":
                    // Each glyph states its own metrics; none carry over from the one before.
                    encoding = -1; advance = -1; w = h = xOff = yOff = 0;
                    break;
                case "ENCODING":
                    encoding = int.Parse(f[1], CultureInfo.InvariantCulture);
                    break;
                case "DWIDTH":
                    advance = int.Parse(f[1], CultureInfo.InvariantCulture);
                    break;
                case "BBX":
                    w = int.Parse(f[1], CultureInfo.InvariantCulture);
                    h = int.Parse(f[2], CultureInfo.InvariantCulture);
                    xOff = int.Parse(f[3], CultureInfo.InvariantCulture);
                    yOff = int.Parse(f[4], CultureInfo.InvariantCulture);
                    break;
                case "BITMAP":
                    var rows = new string[h];
                    for (int r = 0; r < h; r++) {
                        rows[r] = bdf.ReadLine()?.Trim() ?? string.Empty;
                    }
                    if (encoding >= 0) {
                        // No DWIDTH: the pen advances past the glyph's own box.
                        FontGlyph glyph = Place(rows, font.Height, baseline, advance >= 0 ? advance : xOff + w, w, h, xOff, yOff, out bool lost);
                        added[encoding] = glyph;
                        if (lost) {
                            clipped.Add(encoding);
                        }
                    }
                    encoding = -1;
                    break;
            }
        }
        foreach (KeyValuePair<int, FontGlyph> glyph in added) {
            font.ExtraGlyphs[glyph.Key] = glyph.Value;
        }
        return clipped;
    }

    /// <summary>One glyph's BITMAP rows into a cell of the game font, its BBX bottom <paramref name="yOff"/> rows above the baseline.</summary>
    private static FontGlyph Place(string[] hexRows, int height, int baseline, int advance,
        int w, int h, int xOff, int yOff, out bool lost) {
        int bytesPerRow = Math.Max(1, (advance + 7) / 8);
        var glyph = new FontGlyph { Width = advance, BytesPerRow = bytesPerRow };
        for (int y = 0; y < height; y++) {
            glyph.Rows.Add(new byte[bytesPerRow]);
        }

        lost = false;
        int top = baseline - (yOff + h);
        for (int r = 0; r < h; r++) {
            for (int c = 0; c < w; c++) {
                int nibble = c / 4 < hexRows[r].Length ? Convert.ToInt32(hexRows[r][c / 4].ToString(), 16) : 0;
                if ((nibble & (0x8 >> (c % 4))) == 0) {
                    continue;
                }
                int x = xOff + c;
                int y = top + r;
                if (x < 0 || x >= advance || y < 0 || y >= height) {
                    lost = true;
                    continue;
                }
                glyph.Rows[y][x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return glyph;
    }
}
