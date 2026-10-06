namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Text;
using Xunit;

/// <summary>The languages the Preferences screen cycles through (TASK-782).</summary>
public class LanguageChoiceTests {
    [Fact]
    public void EnglishFirst_ThenTheInstalledPacksInOrder_PseudoOnlyInDevelopment() {
        Assert.Equal(new[] { "en", "de", "nl" }, LanguageChoice.Available(new[] { "nl", "de" }, development: false));
        Assert.Equal(new[] { "en", "de", "nl", "qps" }, LanguageChoice.Available(new[] { "nl", "de" }, development: true));
    }

    [Fact]
    public void APackNamedLikeABuiltInIsNotListedTwice() {
        Assert.Equal(new[] { "en", "nl" }, LanguageChoice.Available(new[] { "en", "nl", "qps" }, development: false));
    }

    [Fact]
    public void NextWrapsAndAnUnknownCurrentStartsOver() {
        string[] all = { "en", "nl", "qps" };
        Assert.Equal("nl", LanguageChoice.Next(all, "en"));
        Assert.Equal("en", LanguageChoice.Next(all, "qps"));
        Assert.Equal("en", LanguageChoice.Next(all, "fr"));
    }

    [Fact]
    public void ALanguageIsNamedInItself() {
        Assert.Equal("English", LanguageChoice.DisplayName("en"));
        Assert.Equal("Nederlands", LanguageChoice.DisplayName("nl"));
        Assert.Equal("Deutsch", LanguageChoice.DisplayName("de"));
        Assert.Equal("Pseudo", LanguageChoice.DisplayName("qps"));
        Assert.Equal("xx-made-up", LanguageChoice.DisplayName("xx-made-up"));
    }
}
