namespace ResourceExtraction.Tests;

using ResourceExtraction.Extractors;
using Xunit;

public class BokTextNewlineTests {
    [Fact]
    public void ATrailingNewlineIsNotALine() {
        // C21.BOK's italic "Gorath!\n" (BOOKTEXT.C:270-363): the book renderer never breaks on '\n',
        // it only ends a line on overflow or a control code, so the original shows "Gorath!" as the
        // paragraph's last line. Kept, it became a justified line plus an empty one, and the extra
        // line pushed "rose to his feet..." onto a page of its own.
        Assert.Equal("Gorath!", BokExtractor.BookText("Gorath!\n"));
    }

    [Fact]
    public void ANewlineInsideTextIsOnlyABreakOpportunity() {
        Assert.Equal("one two", BokExtractor.BookText("one\ntwo"));
    }
}

public class BokTextRunTests {
    [Fact]
    public void ACp437UmlautIsTextNotAControlCode() {
        // BOOKTEXT.C:119 ends a run only on a byte whose high nibble is F; the German books carry
        // CP437 'ä' (0x84) and 'ß' (0xE1), which decode above the old 0xB1 char limit.
        var bytes = new byte[] { (byte)'S', 0x84, (byte)'t', 0xE1, 0xF0 };
        using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes));
        Assert.Equal("Sätß", BokExtractor.ReadTextRun(reader));
        Assert.Equal(0xF0, reader.ReadByte());
    }
}
