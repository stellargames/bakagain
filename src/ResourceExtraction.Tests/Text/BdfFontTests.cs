namespace ResourceExtraction.Tests.Text;

using GameData.Resources.Font;
using ResourceExtraction.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>A language pack's pixel font is a standard BDF file, merged into the game's font (TASK-778).</summary>
public class BdfFontTests {
    // A 10-row cell whose capital 'A' sits on rows 1-7, so the baseline is row 8.
    private static FontResource Game() {
        var a = new FontGlyph { Width = 5, BytesPerRow = 1 };
        for (int y = 0; y < 10; y++) {
            a.Rows.Add(new[] { y is >= 1 and <= 7 ? (byte)0xF0 : (byte)0 });
        }
        return new FontResource("GAME.FNT") { Height = 10, FirstCharacter = 'A', Glyphs = { a } };
    }

    private const string Bdf = """
        STARTFONT 2.1
        FONT -pack-game
        SIZE 10 75 75
        FONTBOUNDINGBOX 6 12 0 -2
        CHARS 3
        STARTCHAR adieresis
        ENCODING 228
        SWIDTH 500 0
        DWIDTH 5 0
        BBX 4 6 0 0
        BITMAP
        90
        00
        60
        90
        90
        70
        ENDCHAR
        STARTCHAR tall
        ENCODING 8364
        DWIDTH 6 0
        BBX 2 12 0 -2
        BITMAP
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        C0
        ENDCHAR
        STARTCHAR unencoded
        ENCODING -1
        DWIDTH 3 0
        BBX 1 1 0 0
        BITMAP
        80
        ENDCHAR
        ENDFONT
        """;

    private static (FontResource Font, IReadOnlyList<int> Clipped) Merged() {
        FontResource font = Game();
        IReadOnlyList<int> clipped = BdfFont.MergeInto(font, new StringReader(Bdf));
        return (font, clipped);
    }

    [Fact]
    public void ANewLetterSitsOnTheGameFontsBaseline() {
        FontGlyph glyph = Merged().Font.GlyphFor('ä')!;

        Assert.Equal(5, glyph.Width);
        // Six rows ending on row 7, the row above the baseline, like the capital A.
        Assert.True(glyph.IsSet(0, 2) && glyph.IsSet(3, 2));    // the dots
        Assert.False(glyph.IsSet(0, 3));
        Assert.True(glyph.IsSet(1, 7) && glyph.IsSet(3, 7));     // the bowl's bottom
        Assert.False(glyph.IsSet(0, 8));
    }

    [Fact]
    public void TheOriginalGlyphsStay() {
        FontResource font = Merged().Font;
        Assert.Same(font.Glyphs[0], font.GlyphFor('A'));
    }

    [Fact]
    public void InkOutsideTheCellIsClippedAndReported() {
        (FontResource font, IReadOnlyList<int> clipped) = Merged();

        Assert.Equal(new[] { 8364 }, clipped);
        Assert.True(font.GlyphFor(8364)!.IsSet(0, 0));
        Assert.True(font.GlyphFor(8364)!.IsSet(0, 9));
    }

    [Fact]
    public void AnUnencodedGlyphIsSkipped() =>
        Assert.Equal(new[] { 'A', 228, 8364 }, Merged().Font.AllGlyphs().Select(g => g.Character));

    [Fact]
    public void APackGlyphForAnExistingLetterReplacesIt() {
        FontResource font = Game();
        BdfFont.MergeInto(font, new StringReader(Bdf.Replace("ENCODING 228", "ENCODING 65")));

        Assert.Equal(new[] { 'A', 8364 }, font.AllGlyphs().Select(g => g.Character));
        Assert.True(font.GlyphFor('A')!.IsSet(0, 2));   // the pack's dots, not the original's A
    }

    [Fact]
    public void AFileThatBreaksPartWayChangesNothing() {
        // A pack font that fails on its second glyph must not leave the first one merged: a half-merged
        // font draws some letters from the pack and some not, with nothing saying which.
        const string broken = """
            STARTFONT 2.1
            STARTCHAR adieresis
            ENCODING 228
            DWIDTH 5 0
            BBX 4 1 0 0
            BITMAP
            90
            ENDCHAR
            STARTCHAR broken
            ENCODING notanumber
            ENDCHAR
            ENDFONT
            """;
        FontResource font = Game();
        Assert.ThrowsAny<System.Exception>(() => BdfFont.MergeInto(font, new StringReader(broken)));
        Assert.Empty(font.ExtraGlyphs);
    }

    [Fact]
    public void AGlyphWithoutItsOwnWidthDoesNotBorrowThePreviousOne() {
        const string twoGlyphs = """
            STARTFONT 2.1
            STARTCHAR wide
            ENCODING 228
            DWIDTH 7 0
            BBX 4 1 0 0
            BITMAP
            90
            ENDCHAR
            STARTCHAR nowidth
            ENCODING 246
            BBX 4 1 0 0
            BITMAP
            90
            ENDCHAR
            ENDFONT
            """;
        FontResource font = Game();
        BdfFont.MergeInto(font, new StringReader(twoGlyphs));
        Assert.Equal(7, font.ExtraGlyphs[228].Width);
        Assert.Equal(4, font.ExtraGlyphs[246].Width);   // its own box, not the 7 before it
    }
}
