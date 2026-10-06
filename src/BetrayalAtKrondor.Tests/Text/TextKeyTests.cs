namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Content;
using GameData.Resources.Text;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Every player-visible string has one stable key — the PO msgctxt a translation is filed under
/// (TASK-772). Keys derive from the original data's own positions, so they are the same on every
/// machine and survive re-extraction.
/// </summary>
public class TextKeyTests {
    [Fact]
    public void EachDomainHasItsShape() {
        Assert.Equal("base:bok:C11:0.2", TextKey.BookParagraph("C11.BOK", 0, 2));
        Assert.Equal("base:req:REQ_MAIN:7", TextKey.UiLabel("REQ_MAIN.DAT", 7));
        Assert.Equal("base:req:REQ_MAIN:7:alt", TextKey.UiLabelAlt("REQ_MAIN.DAT", 7));
        Assert.Equal("base:in:IN_SAVE:1", TextKey.InputFieldLabel("IN_SAVE.DAT", 1));
        Assert.Equal("base:lbl:LBL_OPT:3", TextKey.MenuLabel("LBL_OPT.DAT", 3));
        Assert.Equal("base:keyword:255", TextKey.Keyword(255));
        Assert.Equal("base:fmap:4", TextKey.TownName(4));
        Assert.Equal("base:cred:title", TextKey.CreditsTitle);
        Assert.Equal("base:cred:9:role", TextKey.CreditRole(9));
        Assert.Equal("base:cred:9:name", TextKey.CreditName(9));
        Assert.Equal("base:objinfo:5:name", TextKey.ItemName(5));
        Assert.Equal("base:spell:3:name", TextKey.SpellName(3));
        Assert.Equal("base:spell:3:cost", TextKey.SpellDoc(ContentKey.ForBase("spell", 3), TextKey.SpellDocField.Cost));
        Assert.Equal("base:mnames:40", TextKey.MonsterName(40));
    }

    [Fact]
    public void TheResourceNameIsNormalisedSoAKeyDoesNotDependOnHowTheFileWasNamed() {
        Assert.Equal(TextKey.UiLabel("REQ_MAIN.DAT", 1), TextKey.UiLabel("req_main.dat", 1));
        Assert.Equal(TextKey.UiLabel("REQ_MAIN.DAT", 1), TextKey.UiLabel("REQ_MAIN", 1));
        Assert.Equal(TextKey.BookParagraph("C11.BOK", 0, 0), TextKey.BookParagraph("c11", 0, 0));
    }

    [Fact]
    public void EverySpellDocFieldHasADistinctKey() {
        var seen = new HashSet<string>();
        foreach (TextKey.SpellDocField f in System.Enum.GetValues<TextKey.SpellDocField>()) {
            Assert.True(seen.Add(TextKey.SpellDoc("base:spell:0", f)), f.ToString());
        }
        Assert.DoesNotContain(TextKey.SpellName(0), seen);
    }

    [Fact]
    public void KeysAreValidContentKeys() =>
        Assert.True(ContentKey.IsValid(TextKey.BookParagraph("C11.BOK", 1, 1)));
}
