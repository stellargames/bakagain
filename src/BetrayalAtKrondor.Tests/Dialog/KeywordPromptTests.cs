namespace BetrayalAtKrondor.Tests.Dialog;

using GameData.Resources.Dialog;
using Xunit;

/// <summary>
/// The "asked about:" prompt, and the farewell sharing the dismiss code.
/// </summary>
public class KeywordPromptTests {
    [Fact]
    public void ThePromptIsTheSpeakersNameAndALiteralSuffix() {
        Assert.Equal("Gorath asked about:", KeywordPrompt.PromptFor("Gorath"));
    }

    [Fact]
    public void TheNameGoesInVerbatim() {
        // Straight concatenation — no placeholder substitution, no punctuation beyond the suffix.
        Assert.StartsWith("Owyn", KeywordPrompt.PromptFor("Owyn"));
        Assert.EndsWith(" asked about:", KeywordPrompt.PromptFor("Owyn"));
    }

    [Fact]
    public void ChoosingTheFarewellAndDismissingArTheSamePath() {
        // Which is why the farewell's action id is 1 — 1 is the dismiss code.
        Assert.Equal(DialogChoiceMenu.DismissedResult, KeywordMenu.FarewellActionId);
    }

    [Fact]
    public void AskingRecordsTheTopicForNextTimeTheGridIsBuilt() {
        // A set asked-about flag is what greys the topic out.
        Assert.True(KeywordMenu.AlreadyAsked(1));
    }

    [Fact]
    public void ASpeakerWithNoNameStillGetsTheHeadingNotItsTemplate() {
        // A name the lookup could not find reaches here as null; the heading must not fall back to
        // the raw "{name} asked about:" pattern.
        Assert.Equal(" asked about:", KeywordPrompt.PromptFor(null!));
    }
}
