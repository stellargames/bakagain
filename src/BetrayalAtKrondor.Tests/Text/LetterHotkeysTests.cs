namespace BetrayalAtKrondor.Tests.Text;

using GameData.Resources.Menu;
using GameData.Resources.Text;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>A pack moves letter keys by <c>port:hotkey:&lt;letter&gt;</c>, as a permutation (TASK-784).</summary>
public class LetterHotkeysTests {
    private static LanguagePack Pack(params (char English, string Key)[] moves) =>
        new LanguagePack("nl", moves.ToDictionary(m => LetterHotkeys.Key(m.English), m => m.Key));

    [Fact]
    public void WithoutAPackEveryLetterIsItself() {
        for (char c = 'a'; c <= 'z'; c++) {
            Assert.Equal(c, LetterHotkeys.Resolve(LanguagePack.English, c));
            Assert.Equal(c, LetterHotkeys.Resolve(LanguagePack.English, char.ToUpperInvariant(c)));
        }
    }

    [Fact]
    public void AMovedLetterIsPressedByItsNewKeyAndItsOldKeyGoesDead() {
        LanguagePack nl = Pack(('M', "K"));

        Assert.Equal('m', LetterHotkeys.Resolve(nl, 'k'));
        Assert.Equal('m', LetterHotkeys.Resolve(nl, 'K'));
        Assert.Equal('\0', LetterHotkeys.Resolve(nl, 'm'));
        Assert.Equal('e', LetterHotkeys.Resolve(nl, 'e'));
    }

    [Fact]
    public void TheMapIsAPermutation() {
        LanguagePack swap = Pack(('M', "K"), ('K', "M"), ('E', "E"));
        var pressed = Enumerable.Range('a', 26).Select(c => LetterHotkeys.Resolve(swap, (char)c)).ToList();

        Assert.Equal(26, pressed.Distinct().Count());
        Assert.Equal('k', LetterHotkeys.Resolve(swap, 'm'));
        Assert.Equal('m', LetterHotkeys.Resolve(swap, 'k'));
    }

    [Fact]
    public void TwoLettersOnOneKeyTheFirstWinsAndNoKeyPressesTwoThings() {
        LanguagePack clash = Pack(('M', "K"), ('O', "K"));
        var pressed = Enumerable.Range('a', 26).Select(c => LetterHotkeys.Resolve(clash, (char)c)).Where(c => c != '\0');

        Assert.Equal('m', LetterHotkeys.Resolve(clash, 'k'));
        Assert.Equal(pressed.Count(), pressed.Distinct().Count());
    }

    [Fact]
    public void ANonLetterKeyCanPressALetter() {
        Assert.Equal('m', LetterHotkeys.Resolve(Pack(('M', "К")), 'к')); // Cyrillic
    }

    [Fact]
    public void AnEntryThatIsNotOneCharacterIsIgnored() {
        LanguagePack bad = Pack(('M', "Kaart"));

        Assert.Equal('m', LetterHotkeys.Resolve(bad, 'm'));
        Assert.Equal('k', LetterHotkeys.Resolve(bad, 'k'));
    }

    [Fact]
    public void TheTemplateListsTravelLettersAndLetterScancodeActions() {
        var page = new UserInterface("REQ_TEST.DAT") {
            MenuEntries = [new UiElement { ActionId = 0x1f }, new UiElement { ActionId = 72 }], // S, Up arrow
        };

        string used = new string(LetterHotkeys.Used(new[] { page }).ToArray());

        Assert.Equal("BCEFMORS", used);
    }
}
