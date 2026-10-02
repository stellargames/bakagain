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
