namespace ResourceExtraction.Tests.Text;

using GameData.Resources.Text;
using ResourceExtraction.Text;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>The translator's template is a standard POT made from the player's English (TASK-783).</summary>
public class PotTemplateTests {
    private static readonly TextEntry[] Entries = {
        new("base:ddx:dial_z00:10274", "hadn't been <hi/>expected<reset/>.\"", "DIAL_Z00.DDX"),
        new("base:keyword:255", "Yes", "KEYWORD.DAT"),
        new("base:keyword:256", "Yes", "KEYWORD.DAT"),
        new("base:uistring:money.gold_and_silver", "%d sovereigns and %d royals", "KRONDOR.EXE"),
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

    [Fact]
    public void APrintfStringIsFlaggedCFormat_SoTheToolsCheckItsPlaceholders() {
        string pot = Pot();
        int flag = pot.IndexOf("#, c-format", System.StringComparison.Ordinal);

        Assert.True(flag >= 0);
        Assert.True(flag > pot.IndexOf("msgctxt \"base:keyword:256\"", System.StringComparison.Ordinal),
            "only on the printf entry");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(pot, "c-format"));
    }

    [Fact]
    public void ATemplateIsFlaggedIcuMessageFormat() {
        var text = new StringWriter();
        PotTemplate.Write(new[] { new TextEntry(UiTemplates.AskedAbout, "{name} asked about:", "BaK-Again") }, text);

        Assert.Contains("#, icu-message-format", text.ToString());
        Assert.DoesNotContain("c-format", text.ToString());
    }
}

/// <summary>A string drawn on one line of a fixed box tells the translator how much room it has (TASK-779).</summary>
public class PotRoomTests {
    [Fact]
    public void AStringWithRoomSaysHowManyCharactersFit() {
        var text = new StringWriter();
        PotTemplate.Write(new[] { new TextEntry("base:req:REQ_OPT0:1", "Restore", "REQ_OPT0.DAT", Room: 70) },
            text, measure: s => s.Length * 5);

        // "Restore" takes 35 px of 70, so twice its seven letters fit.
        Assert.Contains("#. One line, 70 px wide. The English takes 35 px: about 14 characters fit.", text.ToString());
    }

    [Fact]
    public void AStringThatFlowsHasNoRoomComment() {
        var text = new StringWriter();
        PotTemplate.Write(new[] { new TextEntry("base:keyword:255", "Yes", "KEYWORD.DAT") }, text, measure: s => s.Length);

        Assert.DoesNotContain("#. One line", text.ToString());
    }

    [Fact]
    public void ATextButtonsCaptionHasItsWidthInGamePixels() {
        var ui = new GameData.Resources.Menu.UserInterface("REQ_X.DAT") {
            Frame = new GameData.Resources.Layout.DesignFrame { Width = 1600, Height = 1200 },
            MenuEntries = new[] {
                new GameData.Resources.Menu.UiElement { ElementType = GameData.Resources.Menu.ElementType.TextButton, Width = 350, Label = "Restore", Visible = true },
                new GameData.Resources.Menu.UiElement { ElementType = GameData.Resources.Menu.ElementType.TextButton, Width = 60, Label = "Temple of Ishap" },
                new GameData.Resources.Menu.UiElement { ElementType = GameData.Resources.Menu.ElementType.ClickArea, Width = 350, Label = "Zone" },
            },
        };

        var slots = TextSlots.Of(ui, "REQ_X.DAT").ToList();

        Assert.Equal(70, slots[0].Room);
        Assert.Null(slots[1].Room); // hidden: a hit zone, its name drawn elsewhere
        Assert.Null(slots[2].Room);
    }
}
