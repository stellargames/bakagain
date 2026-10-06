namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Book;
using GameData.Resources.Dialog;
using GameData.Resources.Text;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// A book's first paragraph lacks its first letter: an illuminated capital from BOOK.BMX draws it
/// (TASK-781). A translation brings its own first letter, which picks the capital.
/// </summary>
public class BookDropCapTests {
    // C21.BOK: BOOK.BMX #5 is the "A" of "A whisper led him through madness."
    private static BookResource C21() {
        var paragraph = new Paragraph();
        paragraph.TextSegments.Add(new TextSegment { Text = "whisper led him through madness.", FontStyle = FontStyle.Normal });
        var page = new Page();
        page.Images.Add(new BookImage { X = 60, Y = 22, ImageNumber = 5 });
        page.ReservedAreas.Add(new ReservedArea { X = 60, Y = 22, X2 = 230, Y2 = 219 });   // the text wraps around it
        page.Paragraphs.Add(paragraph);
        page.Paragraphs.Add(new Paragraph { TextSegments = { new TextSegment { Text = "He stumbled forward." } } });
        var book = new BookResource("C21.BOK");
        book.Pages.Add(page);
        return book;
    }

    private static TextSlot First(BookResource book) => TextSlots.Of(book, "C21.BOK").First();

    [Fact]
    public void TheParagraphReadsWithItsCapitalsLetter() {
        Assert.Equal("A whisper led him through madness.", First(C21()).Text);
        Assert.Equal("He stumbled forward.", TextSlots.Of(C21(), "C21.BOK").Last().Text);
    }

    [Fact]
    public void ATranslationsFirstLetterPicksItsCapital() {
        BookResource book = C21();
        First(book).Text = "Tijdens zijn waanzin leidde een fluistering hem.";

        Assert.Equal(6, book.Pages[0].Images[0].ImageNumber);   // BOOK.BMX #6 is the T
        Assert.Single(book.Pages[0].ReservedAreas);
        Assert.Equal("ijdens zijn waanzin leidde een fluistering hem.", book.Pages[0].Paragraphs[0].TextSegments[0].Text);
    }

    [Fact]
    public void ALetterWithNoCapitalIsWrittenOutAndThePictureGoes() {
        BookResource book = C21();
        First(book).Text = "Ein Flüstern führte Gorath durch den Wahnsinn.";

        Assert.Empty(book.Pages[0].Images);
        Assert.Empty(book.Pages[0].ReservedAreas);   // or the text would wrap around nothing
        Assert.Equal("Ein Flüstern führte Gorath durch den Wahnsinn.", book.Pages[0].Paragraphs[0].TextSegments[0].Text);
    }

    [Fact]
    public void EveryCapitalHasOneLetterAndTheVinesHaveNone() {
        Assert.Equal("BPJLGATOIDS", string.Concat(new[] { 0, 1, 2, 3, 4, 5, 6, 15, 16, 17, 18 }.Select(i => BookDropCaps.LetterOf(i))));
        Assert.Null(BookDropCaps.LetterOf(7));
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
