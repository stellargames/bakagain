namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Text;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Sentences the port builds from pieces are ICU MessageFormat templates a translation can reorder
/// (TASK-776). English is still the original's pieces, byte for byte.
/// </summary>
[Collection(UiStringsCollection.Name)]
public class UiTemplatesTests {
    [Fact]
    public void APercentageIsTheNumberAndItsSign_OrTheTranslationsForm() {
        UiStringCatalog saved = UiStrings.Catalog;
        try {
            UiStrings.Catalog = UiStringCatalog.Embedded;
            Assert.Equal("55%", UiTemplates.Percent(55));
            UiStrings.Catalog = Translated(UiTemplates.PercentKey, "{n} %");   // French spaces its sign
            Assert.Equal("55 %", UiTemplates.Percent(55));
        } finally {
            UiStrings.Catalog = saved;
        }
    }

    private static UiStringCatalog Translated(string key, string text) =>
        UiStringCatalog.Embedded.TranslatedBy(new LanguagePack("de", new Dictionary<string, string> { [key] = text }));

    [Fact]
    public void TheItemStatusJoinerIsATemplate() {
        // INVINSP.C:377-383 strcat's ", " between "Using" and the item's state (TASK-775).
        Assert.Equal("Using, Repairable",
            UiTemplates.Format(UiTemplates.ItemUsingAndState, ("using", (object)"Using"), ("state", (object)"Repairable")));

        UiStringCatalog previous = UiStrings.Catalog;
        try {
            UiStrings.Catalog = UiStringCatalog.From(new Dictionary<string, string> {
                [UiTemplates.ItemUsingAndState] = "{using} \u2014 {state}",
            });
            Assert.Equal("In gebruik \u2014 Repareerbaar",
                UiTemplates.Format(UiTemplates.ItemUsingAndState, ("using", (object)"In gebruik"), ("state", (object)"Repareerbaar")));
        } finally {
            UiStrings.Catalog = previous;
        }
    }

    [Fact]
    public void EnglishIsTheOriginalConcatenation() {
        UiStringCatalog en = UiStringCatalog.Embedded;

        Assert.Equal("Gorath asked about:", UiTemplates.Format(en, UiTemplates.AskedAbout, ("name", "Gorath")));
        Assert.Equal("7 quarrels remaining", UiTemplates.Format(en, UiTemplates.QuarrelsRemaining, ("count", 7)));
        Assert.Equal("35 of 40", UiTemplates.Format(en, UiTemplates.CurrentOfMax, ("current", 35), ("max", 40)));
    }

    [Fact]
    public void ATranslationReordersAndPluralises() {
        UiStringCatalog de = Translated(UiTemplates.QuarrelsRemaining,
            "Noch {count, plural, one {# Bolzen} other {# Bolzen übrig}}");

        Assert.Equal("Noch 1 Bolzen", UiTemplates.Format(de, UiTemplates.QuarrelsRemaining, ("count", 1)));
        Assert.Equal("Gefragt hat Owyn:", UiTemplates.Format(
            Translated(UiTemplates.AskedAbout, "Gefragt hat {name}:"), UiTemplates.AskedAbout, ("name", "Owyn")));
    }

    [Fact]
    public void ThePiecesAreLiteralText_NotTemplateSyntax() {
        var exe = UiStringCatalog.FromJson("""{ "base:uistring:dialog.asked_about_suffix": "'s {odd} question:" }""");

        Assert.Equal("Pug's {odd} question:", UiTemplates.Format(exe, UiTemplates.AskedAbout, ("name", "Pug")));
    }

    [Fact]
    public void TheTemplatesAreCatalogEntries_SoAPackAndTheTemplateReachThem() {
        Assert.Equal("{name} asked about:", UiStringCatalog.Embedded.Get(UiTemplates.AskedAbout));
    }

    [Fact]
    public void EveryBootKeyHasAnEnglishDefault_AndTheApostrophesSurvive() {
        var english = new Dictionary<string, string>(UiTemplates.EnglishFor(UiStringCatalog.Embedded));
        var keys = typeof(UiTemplates).GetFields().Where(f => f.Name.StartsWith("Boot")).Select(f => (string)f.GetValue(null)!);
        foreach (string key in keys) {
            Assert.False(string.IsNullOrWhiteSpace(english.GetValueOrDefault(key)), key);
        }
        Assert.Equal("Couldn't copy the files: the phone needs about 14 MB free.",
            UiTemplates.Format(UiTemplates.BootCopyFailed, ("mb", 14)));
        Assert.Equal("STEP 2 OF 3", UiTemplates.Format(UiTemplates.BootStepOf, ("n", 2)));
        Assert.Contains("C:\\GOG Games\\Betrayal at Krondor", UiTemplates.Format(UiTemplates.BootHelpBodyWindows));
        Assert.DoesNotContain("phone", UiTemplates.Format(UiTemplates.BootHelpBodyLinux));
        Assert.DoesNotContain("phone", UiTemplates.Format(UiTemplates.BootHelpBodyWindows));
        string android = UiTemplates.Format(UiTemplates.BootHelpBodyAndroid);
        Assert.DoesNotContain("Browse", android);
        foreach (string n in new[] { "1. ", "2. ", "3. ", "4. " }) Assert.Contains(n, android);
        Assert.DoesNotContain("5. ", android);
        foreach (string key in keys) Assert.DoesNotContain("C:\\KRONDOR", UiTemplates.Format(key));
        Assert.Equal("1 of 3 is OK.", UiTemplates.Format(UiTemplates.BootOkCount, ("ok", 1)));
        Assert.Equal("2 of 3 are OK.", UiTemplates.Format(UiTemplates.BootOkCount, ("ok", 2)));
        Assert.Contains("can't be read yet", UiTemplates.Format(UiTemplates.BootInstallerUnreadableAndroid));
        Assert.Contains("\nC:\\GOG Games\\Betrayal at Krondor\n", android);
        Assert.Equal("Tap the version to copy it.", UiTemplates.Format(UiTemplates.BootVersionCopyAndroid));
        Assert.Equal("Click the version to copy it.", UiTemplates.Format(UiTemplates.BootVersionCopy));
        Assert.Equal("Get them on my phone", UiTemplates.Format(UiTemplates.BootPhoneHelpLink));
        Assert.Equal("Couldn't copy the files: this computer needs about 14 MB free. Use this, or choose something else.",
            UiTemplates.Format(UiTemplates.BootCopyFailedFound, ("mb", 14)));
        Assert.Equal("KRONDOR.001 is cut short: your copy is damaged. Reinstall or download the game again from GOG.",
            UiTemplates.Format(UiTemplates.BootDamagedInPlace, ("file", "KRONDOR.001")));
    }
}
