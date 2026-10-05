namespace BetrayalAtKrondor.Tests.Text;

using GameData.Money;
using GameData.Resources.Dialog;
using GameData.Resources.Text;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// English grammar stays the original's code; another language brings its own in ICU
/// MessageFormat (TASK-777).
/// </summary>
[Collection(UiStringsCollection.Name)]
public class LanguageGrammarTests : IDisposable {
    private readonly UiStringCatalog _saved = UiStrings.Catalog;

    public void Dispose() => UiStrings.Catalog = _saved;

    private static void Use(string locale, Dictionary<string, string>? text = null) =>
        UiStrings.Catalog = UiStringCatalog.Embedded.TranslatedBy(new LanguagePack(locale, text ?? new()));

    private static readonly int[] OwlIsACreature = { DialogSlotTable.CreatureActor };

    [Fact]
    public void EnglishKeepsItsArticleAndPossessiveRules() {
        Use("en");
        Assert.Equal("He saw an Owl", TextVariableResolver.Substitute("He saw a @0", new[] { "Owl" }, null, OwlIsACreature));
        Assert.Equal("the Harpies claws", TextVariableResolver.Substitute("the @0s claws", new[] { "Harpy" }, null, OwlIsACreature));
    }

    [Fact]
    public void AnotherLanguageGetsNoEnglishRules() {
        Use("nl");
        Assert.Equal("Hij zag da Owl", TextVariableResolver.Substitute("Hij zag da @0", new[] { "Owl" }, null, OwlIsACreature));
        Assert.Equal("de Harpys klauwen", TextVariableResolver.Substitute("de @0s klauwen", new[] { "Harpy" }, null, OwlIsACreature));
    }

    [Fact]
    public void ATranslationCanSelectOnWhatASlotHolds() {
        Use("de");
        const string text = "Er sah {kind0, select, creature {die} other {}} @0.";
        Assert.Equal("Er sah die Eule.", TextVariableResolver.Substitute(text, new[] { "Eule" }, null, OwlIsACreature));
        Assert.Equal("Er sah  Owyn.", TextVariableResolver.Substitute(text, new[] { "Owyn" }, null, new[] { 1 }));
    }

    [Fact]
    public void EnglishMoneyIsTheOriginalsWording() {
        Use("en");
        Assert.Equal("1 sovereign and 1 royal", MoneyFormatter.Format(11, CurrencyStyle.SovereignsAndRoyals));
        Assert.Equal("3 sovereigns and 2 royals", MoneyFormatter.Format(32, CurrencyStyle.SovereignsAndRoyals));
        Assert.Equal("0 sovereign", MoneyFormatter.Format(0, CurrencyStyle.SovereignsAndRoyals));
    }

    [Fact]
    public void AnotherLanguagesMoneyFollowsItsOwnPluralRules() {
        Use("pl", new Dictionary<string, string> {
            [UiTemplates.MoneyProse] = "{case, select, royals {{r, plural, one {# grosz} few {# grosze} other {# groszy}}} "
                + "sovereigns {{s, plural, one {# korona} few {# korony} other {# koron}}} "
                + "other {{s, plural, one {# korona} few {# korony} other {# koron}} i {r, plural, one {# grosz} few {# grosze} other {# groszy}}}}",
        });

        Assert.Equal("2 korony i 5 groszy", MoneyFormatter.Format(25, CurrencyStyle.SovereignsAndRoyals));
        Assert.Equal("5 koron", MoneyFormatter.Format(50, CurrencyStyle.SovereignsAndRoyals));
        Assert.Equal("3 grosze", MoneyFormatter.Format(3, CurrencyStyle.SovereignsAndRoyals));
    }
}
