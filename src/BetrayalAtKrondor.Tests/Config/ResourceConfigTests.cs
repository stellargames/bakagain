namespace BetrayalAtKrondor.Tests.Config;

using GameData.Resources.Config;
using Xunit;

public class ResourceConfigTests {
    [Fact]
    public void TheShippedFileUnlocksNothing() {
        var c = ResourceConfig.Parse(" soundDrv  = SNDBLAST.DRV\n mouseDrv = none\n nonrotatingmap = 1\n");
        Assert.False(c.KnockKnock);
        Assert.True(c.BookmarkVerify);
    }

    [Fact]
    public void KnockKnockNeedsExactlyTwentyNineCharacters() {
        Assert.True(ResourceConfig.Parse("knockknock = " + new string('x', 29)).KnockKnock);
        Assert.False(ResourceConfig.Parse("knockknock = " + new string('x', 28)).KnockKnock);
        Assert.True(ResourceConfig.Parse("KnockKnock = " + new string('y', 29)).KnockKnock);
    }

    [Fact]
    public void BookmarkVerifyZeroTurnsThePromptOff() {
        Assert.False(ResourceConfig.Parse("bookmarkverify = 0").BookmarkVerify);
        Assert.True(ResourceConfig.Parse("bookmarkverify = 1").BookmarkVerify);
    }

    [Fact]
    public void NonRotatingMapIsReadWhenPresentAndUnsetWhenNot() {
        Assert.True(ResourceConfig.Parse(" nonrotatingmap = 1\n").NonRotatingMap);
        Assert.False(ResourceConfig.Parse("NonRotatingMap = 0").NonRotatingMap);
        Assert.Null(ResourceConfig.Parse("bookmarkverify = 1").NonRotatingMap);
        Assert.Null(ResourceConfig.Parse(null).NonRotatingMap);
    }
}
