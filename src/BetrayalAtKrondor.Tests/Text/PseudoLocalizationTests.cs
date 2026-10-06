namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Dialog;
using GameData.Resources.Text;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// The built-in debug language (TASK-780): every string accented, a third longer and bracketed, so
/// an untranslatable string and a box too small for a translation both show at a glance.
/// </summary>
[Collection(UiStringsCollection.Name)]
public class PseudoLocalizationTests {
    [Fact]
    public void LettersAreAccentedAndTheStringIsBracketedAndLonger() {
        string pseudo = PseudoLocalization.Of("Hello there");

        Assert.StartsWith("[", pseudo);
        Assert.EndsWith("]", pseudo);
        Assert.DoesNotContain("Hello", pseudo);
        Assert.True(pseudo.Length >= "Hello there".Length * 1.3, pseudo);
    }

    [Theory]
    [InlineData("<hi/>Into <hi/>a Dark<reset/> Night")]
    [InlineData("{name} asked about:")]
    [InlineData("%d sovereigns and %2$d royals, 100%%")]
    [InlineData("@4 gaped at @ the sight")]
    [InlineData("#Gorath#\nLeave me.")]
    [InlineData("<i>Perhaps</i>, Pug thought.")]
    [InlineData("{count, plural, one {# quarrel} other {# quarrels}} left")]
    public void WhatTheEngineParsesIsLeftAlone(string english) {
        string pseudo = PseudoLocalization.Of(english);

        foreach (string token in new[] { "<hi/>", "<reset/>", "{name}", "%d", "%2$d", "%%", "@4", "#Gorath#",
                     "<i>", "</i>", "{count, plural, one {# quarrel} other {# quarrels}}" }) {
            if (english.Contains(token)) {
                Assert.Contains(token, pseudo);
            }
        }
        Assert.Equal(System.Text.RegularExpressions.Regex.Matches(english, "@").Count,
            System.Text.RegularExpressions.Regex.Matches(pseudo, "@").Count);
    }

    [Fact]
    public void ASpeakerPrefixAndLeadingWhitespaceStayInFrontOfTheBracket() {
        Assert.StartsWith("#Gorath#\n[", PseudoLocalization.Of("#Gorath#\nLeave me."));
        Assert.StartsWith("\t[", PseudoLocalization.Of("\tThe road went on."));
    }

    [Fact]
    public void TheTemplateStillFormats() {
        var pseudo = new Dictionary<string, string> { [UiTemplates.AskedAbout] = PseudoLocalization.Of("{name} asked about:") };
        UiStringCatalog catalog = UiStringCatalog.Embedded.TranslatedBy(new LanguagePack("en", pseudo));

        Assert.Contains("Owyn", UiTemplates.Format(catalog, UiTemplates.AskedAbout, ("name", "Owyn")));
    }

    [Fact]
    public void ThePseudoPackTranslatesEveryResourceString() {
        var dialog = new Dialog("DIAL_Z01.DDX");
        dialog.Entries.Add(new DialogEntry { Key = "base:ddx:dial_z01:10", Text = "Hello." });

        Assert.Equal(1, LanguagePack.Pseudo.Apply(dialog, "DIAL_Z01.DDX"));
        Assert.Equal(PseudoLocalization.Of("Hello."), dialog.Entries[0].Text);
        Assert.Equal(PseudoLocalization.Of(UiStringCatalog.Embedded.Get(UiTemplates.AskedAbout)),
            UiStringCatalog.Embedded.TranslatedBy(LanguagePack.Pseudo).Get(UiTemplates.AskedAbout));
    }

    [Fact]
    public void ItsLettersAreOnesTheFontsCanCompose() {
        foreach (int c in LanguagePack.Pseudo.Characters()) {
            Assert.True(c < 0x80 || "àéïöüÅÉÎØÜçñýÇÑšž".Contains((char)c), $"U+{c:X4}");
        }
    }
}
