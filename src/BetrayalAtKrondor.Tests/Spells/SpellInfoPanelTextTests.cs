namespace BetrayalAtKrondor.Tests.Spells;

using GameData.Resources.Spells;
using GameData.Resources.Text;
using Xunit;

/// <summary>
/// The cast screen's computed lines come from the EXE's own templates, so a language pack
/// translates them (TASK-775: the qps sweep found them in plain English).
/// </summary>
[Collection(BetrayalAtKrondor.Tests.Text.UiStringsCollection.Name)]
public class SpellInfoPanelTextTests {
    [Fact]
    public void TheEnglishIsTheOriginals() {
        Assert.Equal("Cost: 5 Health+Stamina", SpellInfoPanel.CostLine(5));
        Assert.Equal("Damage: 12", SpellInfoPanel.DamageLine(12));
        Assert.Equal("Health/Stamina:  80 of 85", SpellInfoPanel.HealthStaminaLine(80, 85));
    }

    [Fact]
    public void ATranslatedCatalogIsUsed() {
        UiStringCatalog previous = UiStrings.Catalog;
        try {
            UiStrings.Catalog = UiStringCatalog.From(new System.Collections.Generic.Dictionary<string, string> {
                ["base:uistring:combat.spell_cost_format"] = "Kost: %d gezondheid",
                ["base:uistring:combat.spell_damage_format"] = "Schade: %d",
                ["base:uistring:combat.health_stamina_format"] = "Gezondheid:  %d van %d",
            });

            Assert.Equal("Kost: 5 gezondheid", SpellInfoPanel.CostLine(5));
            Assert.Equal("Schade: 12", SpellInfoPanel.DamageLine(12));
            Assert.Equal("Gezondheid:  80 van 85", SpellInfoPanel.HealthStaminaLine(80, 85));
        } finally {
            UiStrings.Catalog = previous;
        }
    }
}
