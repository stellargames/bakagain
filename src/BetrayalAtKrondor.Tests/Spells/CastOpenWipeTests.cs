namespace BetrayalAtKrondor.Tests.Spells;

using GameData.Resources.Spells;
using Xunit;

/// <summary>cspell_gfx_wipe_horizontal_split (CSPELL.C:2081) over the cast panel (TASK-815).</summary>
public class CastOpenWipeTests {
    [Fact]
    public void ItStepsThreePixelsFromTheCentreFor49Ticks() {
        Assert.Equal(3, CastOpenWipe.StepVga);
        Assert.Equal(161, CastOpenWipe.CentreVga);
        // left = 158, 155, ... 14: 49 values >= 13.
        Assert.Equal(49, CastOpenWipe.StepCount);
    }

    [Fact]
    public void TheBandWidensSymmetricallyAndEndsAsTheWholeRect() {
        Assert.Equal((158 * 5, 6 * 5), CastOpenWipe.RevealedBand(1));
        Assert.Equal((155 * 5, 12 * 5), CastOpenWipe.RevealedBand(2));
        Assert.Equal((65, 1480), CastOpenWipe.RevealedBand(CastOpenWipe.StepCount));
        Assert.Equal((65, 66, 1480, 612), CastOpenWipe.CanonicalRect);
    }
}
