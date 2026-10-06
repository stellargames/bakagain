namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Font;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Letters a translation needs and the font lacks are composed from the font's own base letters
/// and a small set of pixel marks (TASK-778).
/// </summary>
public class GlyphSynthesisTests {
    // A 10-row cell, baseline at row 8: capitals on rows 1-7, lowercase on rows 4-7.
    private static FontGlyph Box(int width, int top, int bottom, int inkRight) {
        var g = new FontGlyph { Width = width, BytesPerRow = 1 };
        for (int y = 0; y < 10; y++) {
            byte row = 0;
            if (y >= top && y <= bottom) {
                for (int x = 0; x <= inkRight; x++) {
                    row |= (byte)(0x80 >> x);
                }
            }
            g.Rows.Add(new[] { row });
        }
        return g;
    }

    private static FontResource Font() {
        var font = new FontResource("GAME.FNT") { Height = 10, FirstCharacter = 'A' };
        for (int c = 'A'; c <= 'z'; c++) {
            font.Glyphs.Add(c >= 'a' ? Box(5, 4, 7, 3) : c <= 'Z' ? Box(5, 1, 7, 3) : Box(3, 0, -1, 0));
        }
        // 'i': a dot on row 2, a stem on rows 4-7.
        FontGlyph i = font.Glyphs['i' - 'A'];
        i.Width = 2;
        for (int y = 0; y < 10; y++) {
            i.Rows[y][0] = y == 2 || y >= 4 && y <= 7 ? (byte)0x80 : (byte)0;
        }
        return font;
    }

    private static bool Row(FontGlyph g, int y) => Enumerable.Range(0, g.Width).Any(x => g.IsSet(x, y));

    [Fact]
    public void AFontsBlankPlaceholderDoesNotCountAsTheLetter() {
        // PUZZLE.FNT and ALIEN.FNT carry 251 glyphs from character 0, and their 228 is an empty
        // 1-pixel placeholder: read as "the font has ä", a German riddle's "käme" drew as "kme".
        FontResource font = Font();
        var full = new FontResource("PUZZLE.FNT") { Height = 10, FirstCharacter = 0 };
        for (int c = 0; c < 251; c++) {
            full.Glyphs.Add(c >= 'A' && c <= 'z' ? font.Glyphs[c - 'A'] : Box(1, 0, -1, 0));
        }

        GlyphSynthesis.AddComposed(full, new[] { 0xE4 });

        Assert.True(full.ExtraGlyphs.ContainsKey(0xE4));
    }

    [Fact]
    public void AnUmlautSitsAboveTheLetterWithARowBetween() {
        FontResource font = Font();
        Assert.Empty(GlyphSynthesis.AddComposed(font, new[] { (int)'ä' }));

        FontGlyph a = font.GlyphFor('a')!, ae = font.GlyphFor('ä')!;
        Assert.Equal(a.Width, ae.Width);
        Assert.True(Row(ae, 2));
        Assert.False(Row(ae, 3));
        Assert.All(Enumerable.Range(4, 6), y => Assert.Equal(a.Rows[y], ae.Rows[y]));
    }

    [Fact]
    public void ACapitalUsesTheRowsItHas() {
        FontResource font = Font();
        GlyphSynthesis.AddComposed(font, new[] { (int)'Ä' });

        FontGlyph glyph = font.GlyphFor('Ä')!;
        Assert.True(Row(glyph, 0), "the mark goes in the one free row");
        Assert.Equal(font.GlyphFor('A')!.Rows[1], glyph.Rows[1]);
    }

    [Fact]
    public void TheDotOfAnIGoesBeforeItsMarkComesOn() {
        FontResource font = Font();
        GlyphSynthesis.AddComposed(font, new[] { (int)'í' });

        FontGlyph glyph = font.GlyphFor('í')!;
        // The stem now starts at row 4, so a two-row acute sits on rows 1-2 with row 3 clear —
        // where the dot was is the acute's lower half, not the dot.
        Assert.True(Row(glyph, 1), "the acute's top");
        Assert.False(Row(glyph, 3));
        Assert.True(glyph.IsSet(0, 4));
    }

    [Fact]
    public void ACedillaHangsBelowTheBaseline() {
        FontResource font = Font();
        GlyphSynthesis.AddComposed(font, new[] { (int)'ç' });

        Assert.True(Row(font.GlyphFor('ç')!, 8));
    }

    [Fact]
    public void StrokeAndJoinedLettersAreMade() {
        FontResource font = Font();
        Assert.Empty(GlyphSynthesis.AddComposed(font, new[] { (int)'ø', 'Ø', 'æ', 'Æ', 'œ', 'ł' }));

        Assert.True(font.GlyphFor('æ')!.Width > font.GlyphFor('a')!.Width);
    }

    [Fact]
    public void WhatCannotBeMadeIsReportedAndNothingElseChanges() {
        FontResource font = Font();
        FontGlyph original = font.GlyphFor('A')!;

        IReadOnlyList<int> missing = GlyphSynthesis.AddComposed(font, new[] { (int)'A', 'ж', '中' });

        Assert.Equal(new[] { (int)'ж', '中' }, missing);
        Assert.Same(original, font.GlyphFor('A'));
        Assert.Null(font.GlyphFor('ж'));
    }

    [Fact]
    public void SharpSIsAReshapedB() {
        FontResource font = Font();
        // A B: stem on column 0, bars on rows 1, 4 and 7 closing onto it.
        FontGlyph b = font.Glyphs['B' - 'A'];
        string[] shape = { "....", "###.", "#..#", "#..#", "###.", "#..#", "#..#", "###.", "....", "...." };
        for (int y = 0; y < 10; y++) {
            b.Rows[y][0] = System.Convert.ToByte(shape[y].Replace('#', '1').Replace('.', '0').PadRight(8, '0'), 2);
        }

        Assert.Empty(GlyphSynthesis.AddComposed(font, new[] { (int)'ß' }));

        FontGlyph sharp = font.GlyphFor('ß')!;
        Assert.False(sharp.IsSet(0, 1), "the stem's top corner is an arch");
        Assert.False(sharp.IsSet(1, 4), "the middle bar is open at the stem");
        Assert.False(sharp.IsSet(1, 7), "so is the foot");
        Assert.True(sharp.IsSet(0, 5) && sharp.IsSet(2, 7));
    }

    [Fact]
    public void APacksOwnGlyphIsNeverReplaced() {
        FontResource font = Font();
        var drawn = new FontGlyph { Width = 7, BytesPerRow = 1 };
        font.ExtraGlyphs['é'] = drawn;

        GlyphSynthesis.AddComposed(font, new[] { (int)'é' });

        Assert.Same(drawn, font.GlyphFor('é'));
    }
}
