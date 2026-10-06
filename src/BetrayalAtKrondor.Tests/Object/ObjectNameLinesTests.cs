namespace BetrayalAtKrondor.Tests.Object;

using GameData.Resources.Object;
using Xunit;

public class ObjectNameLinesTests {
    // --- a translated name (TASK-779) ---

    [Fact]
    public void ATranslationWhoseOffsetIsNoLongerASpaceBreaksNearTheMiddle() {
        // "Zweihänder Schwert" is 18 long; offset 4 was a space only in the English.
        Assert.Equal(new[] { "Zweihänder", "Schwert" }, ObjectNameLines.Split("Zweihänder Schwert", 4));
    }

    [Fact]
    public void ATranslatorsOwnLineBreakWins() {
        Assert.Equal(new[] { "Lange", "Zweihänder Klinge" }, ObjectNameLines.Split("Lange\nZweihänder Klinge", 4));
        Assert.Equal(new[] { "Ein", "Name" }, ObjectNameLines.Split("Ein\nName", 0));
    }

    [Fact]
    public void AnAuthoredOneLineNameStaysOneLine_AndAWordWithNoSpaceToo() {
        Assert.Equal(new[] { "Schwert" }, ObjectNameLines.Split("Schwert", 0));
        Assert.Equal(new[] { "Zweihänderschwert" }, ObjectNameLines.Split("Zweihänderschwert", 4));
    }

    [Fact]
    public void EnglishStillBreaksAtTheAuthoredByte() {
        Assert.Equal(new[] { "Elven", "Crossbow" }, ObjectNameLines.Split("Elven Crossbow", 5));
    }
}
