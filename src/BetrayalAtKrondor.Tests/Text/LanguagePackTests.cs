namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Book;
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
/// <summary>A book paragraph is one string; its italic segments travel as &lt;i&gt; pairs (TASK-774).</summary>
public class BookParagraphMarkupTests {
    private const FontStyle ItalicStyle = FontStyle.Normal | FontStyle.Italic;

    private static BookResource Pug() {
        var paragraph = new Paragraph();
        paragraph.TextSegments.Add(new TextSegment { Text = "Perhaps it can be tamed", FontStyle = ItalicStyle, Color = 3 });
        paragraph.TextSegments.Add(new TextSegment { Text = "", FontStyle = FontStyle.Normal, Color = 3 });
        paragraph.TextSegments.Add(new TextSegment { Text = ", Pug thought.", FontStyle = FontStyle.Normal, Color = 3 });
        var page = new Page();
        page.Paragraphs.Add(paragraph);
        var book = new BookResource("C61.BOK");
        book.Pages.Add(page);
        return book;
    }

    private static TextSlot Slot(BookResource book) => Assert.Single(TextSlots.Of(book, "C61.BOK"));

    [Fact]
    public void TheItalicRunIsMarked() {
        Assert.Equal("<i>Perhaps it can be tamed</i>, Pug thought.", Slot(Pug()).Text);
    }

    [Fact]
    public void ATranslationPutsItsItalicWhereItsTagsAre() {
        BookResource book = Pug();
        Slot(book).Text = "Er dachte: <i>Vielleicht lässt es sich zähmen</i>.";

        List<TextSegment> segments = book.Pages[0].Paragraphs[0].TextSegments;
        Assert.Equal(new[] { "Er dachte: ", "Vielleicht lässt es sich zähmen", "." },
            segments.ConvertAll(s => s.Text));
        Assert.Equal(new[] { FontStyle.Normal, ItalicStyle, FontStyle.Normal },
            segments.ConvertAll(s => s.FontStyle));
        Assert.All(segments, s => Assert.Equal(3, s.Color));
    }
}

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
