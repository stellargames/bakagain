namespace BetrayalAtKrondor.Tests.Character;

using GameData.Resources.Spells;
using Xunit;

/// <summary>
/// castCombatSpell — the path that lets a trap or a script cast a spell with no caster behind it.
/// </summary>
public class SyntheticCasterTests {
    [Fact]
    public void TheNegatedPowerIsWhatMakesACasterlessCastWork() {
        // A negative cost exempts the cast from the to-hit roll and from billing — exactly what a
        // caster with no skill and no health needs.
        Assert.True(SpellCostModifiers.IsNegated(-14));
        Assert.False(SpellHitResolution.CanMiss(targetingType: 0, costWasNegated: true,
            hasTarget: true));
    }

    [Fact]
    public void AndItStillArrivesAsAPositiveMagnitude() {
        // The dispatcher strips the sign, so the effect scales from the power that was asked for.
        Assert.Equal(14, SpellCostModifiers.Effective(
            -14, surcharged: false, targetIsWeak: false));
    }
}
