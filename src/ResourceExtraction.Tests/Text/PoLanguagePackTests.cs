namespace ResourceExtraction.Tests.Text;

using GameData.Resources.Text;
using ResourceExtraction.Text;
using System.IO;
using Xunit;

/// <summary>A language pack is a gettext PO file, read by gettext's own rules (TASK-773).</summary>
public class PoLanguagePackTests {
    private const string Po = """
        msgid ""
        msgstr ""
        "Language: nl\n"
        "Content-Type: text/plain; charset=UTF-8\n"
        "Plural-Forms: nplurals=2; plural=(n != 1);\n"

        #. DIAL_Z01.DDX
        msgctxt "base:ddx:DIAL_Z01:10"
        msgid "Hello."
        msgstr "Hallo."

        msgctxt "base:ddx:DIAL_Z01:20"
        msgid "A long line "
        "that continues."
        msgstr "Een lange regel "
        "die doorloopt, met \"aanhalingstekens\" en é."

        #, fuzzy
        msgctxt "base:ddx:DIAL_Z01:30"
        msgid "Guessed."
        msgstr "Gegokt."

        msgctxt "base:ddx:DIAL_Z01:40"
        msgid "Not yet."
        msgstr ""
        """;

    private static LanguagePack Read() => PoLanguagePack.Read(new StringReader(Po));

    [Fact]
    public void TheLocaleComesFromTheHeader() => Assert.Equal("nl", Read().Locale);

    [Fact]
    public void AnEntryIsFiledUnderItsMsgctxt() {
        Assert.True(Read().TryGet("base:ddx:DIAL_Z01:10", out string text));
        Assert.Equal("Hallo.", text);
    }

    [Fact]
    public void ContinuationLinesAndEscapesAreTheStandardOnes() {
        Assert.True(Read().TryGet("base:ddx:DIAL_Z01:20", out string text));
        Assert.Equal("Een lange regel die doorloopt, met \"aanhalingstekens\" en é.", text);
    }

    [Fact]
    public void AFuzzyEntryIsNotUsed_asGettextDoesAtRuntime() =>
        Assert.False(Read().TryGet("base:ddx:DIAL_Z01:30", out _));

    [Fact]
    public void AnEmptyMsgstrIsUntranslated() {
        Assert.False(Read().TryGet("base:ddx:DIAL_Z01:40", out _));
        Assert.Equal(2, Read().TranslatedCount);
    }

    [Fact]
    public void TheLocaleIsTheFoldersNotTheHeaders() {
        // PO editors write "nl_NL", or no Language line at all; the game knows the pack by its
        // folder, and the setting, the restart notice and the grammar all compare against that.
        string po = Po.Replace("\"Language: nl\\n\"\n", "\"Language: nl_NL\\n\"\n");
        Assert.Equal("nl", PoLanguagePack.Read(new StringReader(po), "nl").Locale);
        Assert.Equal("nl", PoLanguagePack.Read(new StringReader(Po.Replace("\"Language: nl\\n\"\n", "")), "nl").Locale);
    }
}
