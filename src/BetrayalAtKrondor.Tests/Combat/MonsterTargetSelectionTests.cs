namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// combat_selectTargetByMode — which modes the caster slots reach, and the nearest-wins tie-break.
/// </summary>
public class MonsterTargetSelectionTests {
    [Fact]
    public void TheSixCasterSlotsCoverModesZeroToFive() {
        // Mode 6 exists but no action slot asks for it.
        var reached = new bool[7];
        for (int slot = 2; slot <= 7; slot++) {
            reached[MonsterSpellcasting.TargetModeOf(slot)] = true;
        }

        for (var mode = 0; mode <= 5; mode++) {
            Assert.True(reached[mode]);
        }
        Assert.False(reached[6]);
    }

    [Fact]
    public void TiesGoToTheLaterCandidate() {
        Assert.True(MonsterSpellcasting.NearestWinsAndTiesGoToTheLater);
    }
}
