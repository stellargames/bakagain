namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Dialog;
using GameData.Resources.Text;
using System.Collections.Generic;
using Xunit;

/// <summary>A language pack replaces each string it has a translation for, by key (TASK-773).</summary>
public class LanguagePackTests {
    private static Dialog TwoEntries() {
        var dialog = new Dialog("DIAL_Z01.DDX");
        dialog.Entries.Add(new DialogEntry { Key = "base:ddx:DIAL_Z01:10", Text = "Hello." });
        dialog.Entries.Add(new DialogEntry { Key = "base:ddx:DIAL_Z01:20", Text = "Farewell." });
        return dialog;
    }

    [Fact]
    public void ATranslatedKeyIsReplacedAndAMissingOneKeepsTheEnglish() {
        var pack = new LanguagePack("nl", new Dictionary<string, string> {
            ["base:ddx:DIAL_Z01:10"] = "Hallo.",
        });
        Dialog dialog = TwoEntries();

        int translated = pack.Apply(dialog, "DIAL_Z01.DDX");

        Assert.Equal(1, translated);
        Assert.Equal("Hallo.", dialog.Entries[0].Text);
        Assert.Equal("Farewell.", dialog.Entries[1].Text);
    }

    [Fact]
    public void AnEmptyTranslationIsUntranslated_notABlankString() {
        // PO's convention: an empty msgstr means "not translated yet".
        var pack = new LanguagePack("nl", new Dictionary<string, string> { ["base:ddx:DIAL_Z01:10"] = "" });
        Dialog dialog = TwoEntries();

        pack.Apply(dialog, "DIAL_Z01.DDX");

        Assert.Equal("Hello.", dialog.Entries[0].Text);
    }

    [Fact]
    public void TheEnglishPackChangesNothing() {
        Dialog dialog = TwoEntries();
        Assert.Equal(0, LanguagePack.English.Apply(dialog, "DIAL_Z01.DDX"));
        Assert.Equal("Hello.", dialog.Entries[0].Text);
    }
}

/// <summary>The EXE's UI strings take a pack's translations by the same keys (TASK-773).</summary>
public class UiStringCatalogTranslationTests {
    [Fact]
    public void TheCatalogTakesAPacksTranslationAndKeepsTheRest() {
        UiStringCatalog english = UiStringCatalog.FromJson(
            "{\"base:uistring:dialog.yes\":\"Yes\",\"base:uistring:dialog.no\":\"No\"}");
        var pack = new LanguagePack("nl", new Dictionary<string, string> { ["base:uistring:dialog.yes"] = "Ja" });

        UiStringCatalog dutch = english.TranslatedBy(pack);

        Assert.Equal("Ja", dutch.Get("base:uistring:dialog.yes"));
        Assert.Equal("No", dutch.Get("base:uistring:dialog.no"));
        Assert.Equal("Yes", english.Get("base:uistring:dialog.yes"));   // the original is untouched
    }
}
