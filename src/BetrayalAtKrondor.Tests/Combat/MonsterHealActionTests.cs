namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using GameData.Resources.Spells;
using Xunit;

/// <summary>
/// Action slot 1 — heal an ally. It bypasses the spell selector entirely, names two spells by
/// number, and asks its urgency question of the wrong ally.
/// </summary>
public class MonsterHealActionTests {
    [Fact]
    public void TheHealSlotNamesTwoSpellsTheSelectorCouldNeverReturn() {
        // Gift of Sung is targeting type 2 and Hocho's Haven type 3, so neither passes the
        // martial-plus-type-0-or-1 filter every other slot uses.
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.GiftOfSung,
            isMartial: true, targetingType: 2));
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.HochosHaven,
            isMartial: true, targetingType: 3));
    }

    [Fact]
    public void AMonsterNeverHealsItself() {
        // A wounded lone caster falls through to the next action in its pattern row instead.
        Assert.False(MonsterSpellcasting.HealsSelf);
    }

    [Fact]
    public void TheUrgencyQuestionIsAskedOfTheWrongAlly() {
        // The loop keeps a running minimum and never reads it; the decision uses the last ally
        // examined. Verified from the encoded displacements.
        Assert.True(MonsterSpellcasting.HealUrgencyReadsTheLastAllyNotTheWorst);
    }

    [Fact]
    public void AndTheSpellIsPickedBeforeTheTargetIs() {
        Assert.True(MonsterSpellcasting.HealSpellIsChosenBeforeTheTarget);
        Assert.True(MonsterSpellcasting.HealsOneAllyPerAction);
    }
}
