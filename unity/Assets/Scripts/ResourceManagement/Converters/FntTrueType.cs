namespace BakAgain.ResourceManagement.Converters {
    using GameData.Resources.Font;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Writes a monochrome .FNT bitmap font as a TrueType file whose glyphs are the font's own
    /// pixels as squares, so Unity's text engines can render the player's fonts. The game's fonts
    /// used to ship as hand-converted TTFs, which put Dynamix's glyphs in the repository.
    /// </summary>
    /// <remarks>
    /// <para>The metrics reproduce those old TTFs, which the text layout is calibrated against
    /// (<see cref="BakAgain.UI.GameFontText"/>, <c>Canonical.GameFontSizePx</c>): the em is the
    /// cell height plus one pixel, the ascender is the row below where most capitals end,
    /// and the descender takes the rest of the cell. Measured on the old GAME and BOOK TTFs: em
    /// 11 and 16 px, ascender 8 and 11 px, descender 2 and 4 px, and a glyph advance of its width.</para>
    /// <para>Each glyph is a set of rectangles, one per vertical stack of identical horizontal
    /// runs of ink. They touch but never overlap, which a non-zero fill renders as their union.</para>
    /// <para><b>Pixels take the shape the font declares</b> (<see cref="FontResource.PixelWidth"/>,
    /// <see cref="FontResource.PixelHeight"/>): a row is that much taller than a column is wide, and
    /// the em stays a count of pixel WIDTHS, so advances and font sizes are unchanged. The original's
    /// fonts come out 1.2x (VGA) or 1.37x (the EGA book) taller natively — no text needs stretching
    /// afterwards, and a square-pixel mod font is not stretched at all (TASK-765).</para>
    /// </remarks>
    public static class FntTrueType {
        /// <summary>Font units per pixel. The em (units) is this times the em in pixels.</summary>
        private const int UnitsPerPixel = 128;

        public static byte[] Build(FontResource font, string familyName) {
            if (font.PixelFormat != FontPixelFormat.Monochrome) {
                throw new ArgumentException($"{font.Id} is not a monochrome font.", nameof(font));
            }
            int emPx = font.Height + 1;
            int ascentPx = font.CapitalBaseline();
            int descentPx = font.Height - ascentPx;
            int unitsPerEm = emPx * UnitsPerPixel;
            double rowUnits = UnitsPerPixel * (font.PixelWidth > 0 && font.PixelHeight > 0
                ? font.PixelHeight / font.PixelWidth : 1.0);
            short Y(int rowsAboveBaseline) => (short)Math.Round(rowsAboveBaseline * rowUnits);

            // Glyph 0 is .notdef (empty); glyph i+1 is the i-th character in code order, so a
            // language pack's letters (TASK-778) sit beside the file's ASCII run.
            List<(int Character, FontGlyph Glyph)> characters = font.AllGlyphs().ToList();
            var glyphs = new List<Glyph> { new Glyph(0, new List<Rect>(), ascentPx, Y) };
            foreach (var (_, g) in characters) {
                glyphs.Add(new Glyph(g.Width * UnitsPerPixel, Rectangles(g, font.Height), ascentPx, Y));
            }

            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] glyf = Glyf(glyphs, out uint[] loca);
            tables["glyf"] = glyf;
            tables["loca"] = Write(w => { foreach (uint o in loca) w.U32(o); });
            tables["hmtx"] = Write(w => {
                foreach (Glyph g in glyphs) {
                    w.U16(g.Advance);
                    w.I16(g.XMin);
                }
            });
            short xMin = glyphs.Where(g => g.HasInk).Select(g => g.XMin).DefaultIfEmpty().Min();
            short xMax = glyphs.Where(g => g.HasInk).Select(g => g.XMax).DefaultIfEmpty().Max();
            short yMin = glyphs.Where(g => g.HasInk).Select(g => g.YMin).DefaultIfEmpty().Min();
            short yMax = glyphs.Where(g => g.HasInk).Select(g => g.YMax).DefaultIfEmpty().Max();
            short ascender = Y(ascentPx);
            short descender = Y(-descentPx);
            int maxAdvance = glyphs.Max(g => g.Advance);
            int firstChar = characters.Count > 0 ? characters[0].Character : 0;
            int lastChar = characters.Count > 0 ? characters[^1].Character : 0;

            tables["head"] = Write(w => {
                w.U32(0x00010000);
                w.U32(0x00010000);
                w.U32(0); // checkSumAdjustment, patched below
                w.U32(0x5F0F3CF5);
                w.U16(0x000B); // y=0 baseline, x=0 lsb, integer ppem
                w.U16(unitsPerEm);
                w.U32(0); w.U32(0); // created
                w.U32(0); w.U32(0); // modified
                w.I16(xMin); w.I16(yMin); w.I16(xMax); w.I16(yMax);
                w.U16(0); // macStyle
                w.U16(8); // lowestRecPPEM
                w.I16(2); // fontDirectionHint
                w.I16(1); // indexToLocFormat: long
                w.I16(0);
            });
            tables["hhea"] = Write(w => {
                w.U32(0x00010000);
                w.I16(ascender); w.I16(descender); w.I16(0);
                w.U16(maxAdvance);
                w.I16(glyphs.Where(g => g.HasInk).Select(g => g.XMin).DefaultIfEmpty().Min());
                w.I16((short)glyphs.Where(g => g.HasInk).Select(g => g.Advance - g.XMax).DefaultIfEmpty().Min());
                w.I16(xMax);
                w.I16(1); w.I16(0); w.I16(0);
                w.I16(0); w.I16(0); w.I16(0); w.I16(0);
                w.I16(0);
                w.U16(glyphs.Count);
            });
            tables["maxp"] = Write(w => {
                w.U32(0x00010000);
                w.U16(glyphs.Count);
                w.U16(glyphs.Max(g => g.Rects.Count * 4));
                w.U16(glyphs.Max(g => g.Rects.Count));
                w.U16(0); w.U16(0);
                w.U16(2);
                for (int i = 0; i < 8; i++) {
                    w.U16(0);
                }
            });
            tables["OS/2"] = Write(w => {
                w.U16(4);
                w.I16((short)glyphs.Skip(1).Select(g => g.Advance).DefaultIfEmpty().Average());
                w.U16(400); w.U16(5); w.U16(0);
                for (int i = 0; i < 10; i++) {
                    w.I16(0); // sub/superscript sizes and offsets, strikeout size and position
                }
                w.I16(0);
                for (int i = 0; i < 10; i++) {
                    w.U8(0); // panose
                }
                w.U32(1); w.U32(0); w.U32(0); w.U32(0); // Basic Latin
                w.Bytes(Encoding.ASCII.GetBytes("BAKA"));
                w.U16(0x0040); // REGULAR
                w.U16(Math.Min(firstChar, 0xFFFF)); w.U16(Math.Min(lastChar, 0xFFFF));
                w.I16(ascender); w.I16(descender); w.I16(0);
                w.U16(ascender); w.U16(-descender);
                w.U32(1); w.U32(0); // Latin 1
                w.I16(ascender); w.I16(ascender); // x-height, cap height
                w.U16(0); w.U16(32); w.U16(1);
            });
            tables["cmap"] = Cmap(characters.Select(c => c.Character).ToList());
            tables["name"] = Name(familyName);
            tables["post"] = Write(w => {
                w.U32(0x00030000);
                w.U32(0);
                w.I16((short)-UnitsPerPixel); w.I16(UnitsPerPixel);
                w.U32(0); w.U32(0); w.U32(0); w.U32(0); w.U32(0);
            });

            return Assemble(tables);
        }

        private readonly struct Rect {
            public readonly int X0, X1, Top, Bottom; // pixel columns [X0,X1), rows [Top,Bottom)

            public Rect(int x0, int x1, int top, int bottom) {
                X0 = x0; X1 = x1; Top = top; Bottom = bottom;
            }
        }

        private static List<Rect> Rectangles(FontGlyph g, int height) {
            var done = new List<Rect>();
            var open = new Dictionary<(int, int), int>(); // run -> top row
            for (int y = 0; y <= height; y++) {
                var runs = new HashSet<(int, int)>();
                if (y < height) {
                    for (int x = 0; x < g.Width;) {
                        if (g.PixelAt(x, y) == 0) {
                            x++;
                            continue;
                        }
                        int start = x;
                        while (x < g.Width && g.PixelAt(x, y) != 0) {
                            x++;
                        }
                        runs.Add((start, x));
                    }
                }
                foreach (var run in open.Keys.Where(r => !runs.Contains(r)).ToList()) {
                    done.Add(new Rect(run.Item1, run.Item2, open[run], y));
                    open.Remove(run);
                }
                foreach (var run in runs) {
                    if (!open.ContainsKey(run)) {
                        open[run] = y;
                    }
                }
            }
            return done;
        }

        private sealed class Glyph {
            public readonly int Advance;
            public readonly List<(short X0, short X1, short Top, short Bottom)> Rects = new();

            /// <param name="ascentPx">Rows above the baseline, which sits at y = 0.</param>
            /// <param name="y">Font units for a count of rows above the baseline.</param>
            public Glyph(int advance, List<Rect> pixels, int ascentPx, Func<int, short> y) {
                Advance = advance;
                foreach (Rect r in pixels) {
                    Rects.Add(((short)(r.X0 * UnitsPerPixel), (short)(r.X1 * UnitsPerPixel),
                        y(ascentPx - r.Top), y(ascentPx - r.Bottom)));
                }
            }

            public bool HasInk => Rects.Count > 0;
            public short XMin => HasInk ? Rects.Min(r => r.X0) : (short)0;
            public short XMax => HasInk ? Rects.Max(r => r.X1) : (short)0;
            public short YMin => HasInk ? Rects.Min(r => r.Bottom) : (short)0;
            public short YMax => HasInk ? Rects.Max(r => r.Top) : (short)0;
        }

        private static byte[] Glyf(List<Glyph> glyphs, out uint[] loca) {
            loca = new uint[glyphs.Count + 1];
            using var stream = new MemoryStream();
            var w = new BigEndian(stream);
            for (int i = 0; i < glyphs.Count; i++) {
                loca[i] = (uint)stream.Position;
                Glyph g = glyphs[i];
                if (!g.HasInk) {
                    continue;
                }
                w.I16((short)g.Rects.Count);
                w.I16(g.XMin); w.I16(g.YMin); w.I16(g.XMax); w.I16(g.YMax);
                for (int c = 0; c < g.Rects.Count; c++) {
                    w.U16(c * 4 + 3);
                }
                w.U16(0); // no instructions
                for (int p = 0; p < g.Rects.Count * 4; p++) {
                    w.U8(1); // on curve, full-size coordinates
                }
                var xs = new List<int>();
                var ys = new List<int>();
                foreach (var r in g.Rects) {
                    // Clockwise outer contour: bottom-left, top-left, top-right, bottom-right.
                    xs.AddRange(new int[] { r.X0, r.X0, r.X1, r.X1 });
                    ys.AddRange(new int[] { r.Bottom, r.Top, r.Top, r.Bottom });
                }
                int px = 0;
                foreach (int x in xs) {
                    w.I16((short)(x - px));
                    px = x;
                }
                int py = 0;
                foreach (int y in ys) {
                    w.I16((short)(y - py));
                    py = y;
                }
                while (stream.Position % 4 != 0) {
                    w.U8(0);
                }
            }
            loca[glyphs.Count] = (uint)stream.Position;
            return stream.ToArray();
        }

        /// <summary>
        /// Format 4: one segment per run of consecutive characters, then the required 0xFFFF end.
        /// Glyph ids are the characters' positions in code order, plus one for .notdef — so each
        /// segment's delta maps its first character onto its first glyph. Characters past the BMP
        /// are not mapped; no language a pack would carry needs them.
        /// </summary>
        private static byte[] Cmap(List<int> characters) => Write(w => {
            var starts = new List<int>();
            var ends = new List<int>();
            var deltas = new List<int>();
            for (int i = 0; i < characters.Count && characters[i] < 0xFFFF; i++) {
                if (starts.Count > 0 && characters[i] == ends[^1] + 1) {
                    ends[^1] = characters[i];
                    continue;
                }
                starts.Add(characters[i]);
                ends.Add(characters[i]);
                deltas.Add((i + 1 - characters[i]) & 0xFFFF);
            }
            starts.Add(0xFFFF); ends.Add(0xFFFF); deltas.Add(1);

            int segments = starts.Count;
            int entrySelector = (int)Math.Floor(Math.Log(segments, 2));
            int searchRange = 2 << entrySelector;
            w.U16(0); w.U16(1);
            w.U16(3); w.U16(1); w.U32(12);
            w.U16(4); w.U16(16 + segments * 8); w.U16(0);
            w.U16(segments * 2); w.U16(searchRange); w.U16(entrySelector); w.U16(segments * 2 - searchRange);
            foreach (int end in ends) {
                w.U16(end);
            }
            w.U16(0);
            foreach (int start in starts) {
                w.U16(start);
            }
            foreach (int delta in deltas) {
                w.U16(delta);
            }
            foreach (int _ in starts) {
                w.U16(0);
            }
        });

        private static byte[] Name(string family) {
            var records = new (int Id, string Text)[] {
                (1, family), (2, "Regular"), (3, family + " BaK-Again"), (4, family), (6, family.Replace(" ", "")),
            };
            byte[][] strings = records.Select(r => Encoding.BigEndianUnicode.GetBytes(r.Text)).ToArray();
            return Write(w => {
                w.U16(0); w.U16(records.Length); w.U16(6 + records.Length * 12);
                int offset = 0;
                for (int i = 0; i < records.Length; i++) {
                    w.U16(3); w.U16(1); w.U16(0x409); w.U16(records[i].Id);
                    w.U16(strings[i].Length); w.U16(offset);
                    offset += strings[i].Length;
                }
                foreach (byte[] s in strings) {
                    w.Bytes(s);
                }
            });
        }

        private static byte[] Assemble(SortedDictionary<string, byte[]> tables) {
            int n = tables.Count;
            int entrySelector = (int)Math.Floor(Math.Log(n, 2));
            int searchRange = (1 << entrySelector) * 16;
            using var stream = new MemoryStream();
            var w = new BigEndian(stream);
            w.U32(0x00010000);
            w.U16(n); w.U16(searchRange); w.U16(entrySelector); w.U16(n * 16 - searchRange);
            uint offset = (uint)(12 + n * 16);
            foreach (var t in tables) {
                w.Bytes(Encoding.ASCII.GetBytes(t.Key));
                w.U32(Checksum(t.Value));
                w.U32(offset);
                w.U32((uint)t.Value.Length);
                offset += (uint)((t.Value.Length + 3) & ~3);
            }
            long headOffset = 0;
            foreach (var t in tables) {
                if (t.Key == "head") {
                    headOffset = stream.Position;
                }
                w.Bytes(t.Value);
                while (stream.Position % 4 != 0) {
                    w.U8(0);
                }
            }
            byte[] font = stream.ToArray();
            uint adjustment = 0xB1B0AFBA - Checksum(font);
            font[headOffset + 8] = (byte)(adjustment >> 24);
            font[headOffset + 9] = (byte)(adjustment >> 16);
            font[headOffset + 10] = (byte)(adjustment >> 8);
            font[headOffset + 11] = (byte)adjustment;
            return font;
        }

        private static uint Checksum(byte[] data) {
            uint sum = 0;
            for (int i = 0; i < data.Length; i += 4) {
                uint word = 0;
                for (int b = 0; b < 4; b++) {
                    word = (word << 8) | (i + b < data.Length ? data[i + b] : 0u);
                }
                sum += word;
            }
            return sum;
        }

        private static byte[] Write(Action<BigEndian> write) {
            using var stream = new MemoryStream();
            write(new BigEndian(stream));
            return stream.ToArray();
        }

        private sealed class BigEndian {
            private readonly Stream _s;

            public BigEndian(Stream s) {
                _s = s;
            }

            public void U8(int v) => _s.WriteByte((byte)v);
            public void U16(int v) { U8(v >> 8); U8(v); }
            public void I16(short v) => U16(v);
            public void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
            public void Bytes(byte[] b) => _s.Write(b, 0, b.Length);
        }
    }
}
