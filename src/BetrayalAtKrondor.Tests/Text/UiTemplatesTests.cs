namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Text;
using System.Collections.Generic;
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
}
