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

    // TASK-826: the German release's article and case codes, over the pack's per-creature grammar.
    private static readonly int[] Creature1 = { DialogSlotTable.NoActor, DialogSlotTable.CreatureActor };

    private static string German(string text, string name, int id) =>
        TextVariableResolver.Substitute(text, new[] { "Owyn", name }, null, Creature1,
            new[] { "", GermanCaseCodes.CreatureNoun(id) });

    private static void UseGerman() => Use("de", new Dictionary<string, string> {
        [GermanCaseCodes.Key("mnames:24", "gender")] = "m",            // Schurke, a weak masculine
        [GermanCaseCodes.Key("mnames:24", "acc")] = "Schurken",
        [GermanCaseCodes.Key("mnames:24", "dat")] = "Schurken",
        [GermanCaseCodes.Key("mnames:24", "pl")] = "Schurken",
        [GermanCaseCodes.Key("mnames:44", "gender")] = "f",            // Spinne
        [GermanCaseCodes.Key("mnames:44", "pl")] = "Spinnen",
        [GermanCaseCodes.Key("mnames:61", "gender")] = "n",            // a neuter with an adjective
        [GermanCaseCodes.Key("mnames:61", "def")] = "Schwarze Ding",
        [GermanCaseCodes.Key("mnames:61", "dat")] = "Schwarzen Ding",
        [GermanCaseCodes.Key("mnames:15", "gender")] = "name",         // a person
        [GermanCaseCodes.Key("objinfo:7", "gender")] = "f",            // an item with an adjective
        [GermanCaseCodes.Key("objinfo:7", "def")] = "rote Tasse",
        [GermanCaseCodes.Key("objinfo:7", "dat")] = "roten Tasse",
        [GermanCaseCodes.Key("objinfo:8", "gender")] = "pl",           // a plural noun
        [GermanCaseCodes.Key("objinfo:8", "dat")] = "Stiefeln",
        [GermanCaseCodes.Key("objinfo:9", "gender")] = "name",         // a possessive name
    });

    private static string GermanItem(string text, string name, int id) =>
        TextVariableResolver.Substitute(text, new[] { "Owyn", name }, null, null,
            new[] { "", GermanCaseCodes.ObjectNoun(id) });

    [Theory]
    [InlineData("Er legte @d @1as hin.", "Rote Tasse", 7, "Er legte die rote Tasse hin.")]
    [InlineData("mit @d @1ds", "Rote Tasse", 7, "mit der roten Tasse")]
    [InlineData("@D @1ns ist leer.", "Rote Tasse", 7, "Die rote Tasse ist leer.")]
    [InlineData("Er legte @d @1as hin.", "Stiefel", 8, "Er legte die Stiefel hin.")]
    [InlineData("mit @d @1ds", "Stiefel", 8, "mit den Stiefeln")]
    [InlineData("mit @i @1ds", "Stiefel", 8, "mit Stiefeln")]
    [InlineData("Er legte @d @1as hin.", "Annas Buch", 9, "Er legte Annas Buch hin.")]
    [InlineData("@D @1ns ist alt.", "Annas Buch", 9, "Annas Buch ist alt.")]
    public void GermanItemsFollowTheirOwnGrammar(string text, string name, int id, string expected) {
        UseGerman();
        Assert.Equal(expected, GermanItem(text, name, id));
    }

    [Fact]
    public void APersonTakesNoArticle() {
        UseGerman();
        Assert.Equal("Wir sehen Gorath.", German("Wir sehen @d @1as.", "Gorath", 15));
        // A party member in the slot is a person whatever the pack says.
        Assert.Equal("Owyn lacht.", TextVariableResolver.Substitute("@D @0ns lacht.", new[] { "Owyn" }, null, new[] { 1 }, new[] { "" }));
    }

    [Theory]
    [InlineData("@D @1ns lacht.", "Schurke", 24, "Der Schurke lacht.")]
    [InlineData("Wir sehen @d @1as.", "Schurke", 24, "Wir sehen den Schurken.")]
    [InlineData("Wir reden mit @d @1ds.", "Schurke", 24, "Wir reden mit dem Schurken.")]
    [InlineData("Nur @i @1ns stand dort.", "Schurke", 24, "Nur ein Schurke stand dort.")]
    [InlineData("@I @1ns kommt.", "Schurke", 24, "Ein Schurke kommt.")]
    [InlineData("mit @i @1ds", "Schurke", 24, "mit einem Schurken")]
    [InlineData("Die @1np laufen.", "Schurke", 24, "Die Schurken laufen.")]
    [InlineData("@D @1ns lacht.", "Spinne", 44, "Die Spinne lacht.")]
    [InlineData("Wir sehen @d @1as.", "Spinne", 44, "Wir sehen die Spinne.")]
    [InlineData("Wir reden mit @d @1ds.", "Spinne", 44, "Wir reden mit der Spinne.")]
    [InlineData("mit @i @1ds", "Spinne", 44, "mit einer Spinne")]
    [InlineData("@I @1ns kommt.", "Spinne", 44, "Eine Spinne kommt.")]
    [InlineData("@D @1ns lacht.", "Schwarzes Ding", 61, "Das Schwarze Ding lacht.")]
    [InlineData("Wir sehen @d @1as.", "Schwarzes Ding", 61, "Wir sehen das Schwarze Ding.")]
    [InlineData("Wir sehen @i @1as.", "Schwarzes Ding", 61, "Wir sehen ein Schwarzes Ding.")]
    [InlineData("Wir reden mit @d @1ds.", "Schwarzes Ding", 61, "Wir reden mit dem Schwarzen Ding.")]
    public void GermanArticlesAndCasesFollowTheCreaturesGender(string text, string name, int id, string expected) {
        UseGerman();
        Assert.Equal(expected, German(text, name, id));
    }

    [Fact]
    public void AGermanCreatureWithNoGrammarIsMasculineInItsOwnName() {
        UseGerman();
        Assert.Equal("Wir sehen den Kobold.", German("Wir sehen @d @1as.", "Kobold", 53));
        Assert.Equal("Ein Kobold kommt.", German("@I @1ns kommt.", "Kobold", 53));
    }

    [Fact]
    public void GermanCodesLeaveThePlainTokensToTheResolver() {
        UseGerman();
        Assert.Equal("Owyn sah den Schurken, Owyn.", TextVariableResolver.Substitute("@0 sah @d @1as, @.",
            new[] { "Owyn", "Schurke" }, "Owyn", Creature1, new[] { "", GermanCaseCodes.CreatureNoun(24) }));
    }

    [Fact]
    public void OnlyGermanReadsTheGermanCodes() {
        Use("nl");
        Assert.Equal("@d Schurkens", German("@d @1ns", "Schurke", 24)); // unchanged behaviour
    }

    [Fact]
    public void ACreatureSlotNamesItsNoun() {
        var context = new DialogSlotContext { CreatureType = 24, CreatureNameOf = _ => "Schurke" };
        var table = new DialogSlotTable();
        DialogSlotPopulator.Assign(table, 1, 17, 0, context);
        Assert.Equal("mnames:24", table.Nouns[1]);
        DialogSlotPopulator.Assign(table, 1, 1, 0, context);
        Assert.Equal("", table.Nouns[1]);
    }

    [Fact]
    public void AnObjectSlotNamesItsNoun() {
        var context = new DialogSlotContext { KeyObjectId = 12, ObjectNameOf = _ => "Tasse" };
        var table = new DialogSlotTable();
        DialogSlotPopulator.Assign(table, 1, 18, 0, context);
        Assert.Equal("objinfo:12", table.Nouns[1]);
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
