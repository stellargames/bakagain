namespace ResourceExtraction.Tests.Text;

using GameData.Resources.Text;
using ResourceExtraction.Text;
using System.IO;
using Xunit;

/// <summary>The translator's template is a standard POT made from the player's English (TASK-783).</summary>
public class PotTemplateTests {
    private static readonly TextEntry[] Entries = {
        new("base:ddx:dial_z00:10274", "hadn't been <hi/>expected<reset/>.\"", "DIAL_Z00.DDX"),
        new("base:keyword:255", "Yes", "KEYWORD.DAT"),
        new("base:keyword:256", "Yes", "KEYWORD.DAT"),
    };

    private static string Pot() {
        var text = new StringWriter();
        PotTemplate.Write(Entries, text);
        return text.ToString();
    }

    [Fact]
    public void EachStringIsAnEntryUnderItsKeyWithItsSource() {
        string pot = Pot();

        Assert.Contains("msgctxt \"base:keyword:255\"", pot);
        Assert.Contains("#: KEYWORD.DAT", pot);
        Assert.Contains("msgid \"hadn't been <hi/>expected<reset/>.\\\"\"", pot);
        Assert.Contains("charset=UTF-8", pot);
    }

    [Fact]
    public void TwoStringsWithTheSameEnglishStayTwoEntries() {
        string pot = Pot();
        Assert.Contains("msgctxt \"base:keyword:256\"", pot);
    }

    [Fact]
    public void AFilledInTemplateIsALanguagePack() {
        string po = Pot()
            .Replace("\"Language: \\n\"", "\"Language: nl\\n\"")
            .Replace("msgid \"Yes\"\nmsgstr \"\"", "msgid \"Yes\"\nmsgstr \"Ja\"");
        LanguagePack pack = PoLanguagePack.Read(new StringReader(po));

        Assert.Equal("nl", pack.Locale);
        Assert.True(pack.TryGet("base:keyword:255", out string yes));
        Assert.Equal("Ja", yes);
        Assert.Equal(2, pack.TranslatedCount);
    }
}
