namespace BetrayalAtKrondor.Tests.Character;

using GameData.Resources.Combat;
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
        Assert.Equal(-14, SpellCastRoutines.SyntheticCasterPower(14));
        Assert.True(SpellCostModifiers.IsNegated(SpellCastRoutines.SyntheticCasterPower(14)));
        Assert.False(SpellHitResolution.CanMiss(targetingType: 0, costWasNegated: true,
            hasTarget: true));
    }

    [Fact]
    public void AndItStillArrivesAsAPositiveMagnitude() {
        // The dispatcher strips the sign, so the effect scales from the power that was asked for.
        Assert.Equal(14, SpellCostModifiers.Effective(
            SpellCastRoutines.SyntheticCasterPower(14), surcharged: false, targetIsWeak: false));
    }

    [Fact]
    public void StrengthDrainIsTheOneSpellThisPathRefuses() {
        // It transfers to the caster, and there is no caster to receive it.
        Assert.True(SpellCastRoutines.SyntheticCasterRefuses(SpellIds.StrengthDrain));
        Assert.False(SpellCastRoutines.SyntheticCasterRefuses(SpellIds.Flamecast));
        Assert.False(SpellCastRoutines.SyntheticCasterRefuses(SpellIds.Skyfire));
    }
}
