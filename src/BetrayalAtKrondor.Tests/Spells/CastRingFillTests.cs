namespace BetrayalAtKrondor.Tests.Spells;

using GameData.Resources.Spells;
using Xunit;
using static GameData.Resources.Spells.CastRingLayout;

/// <summary>The slider's opening sweep, cspell_select_power CSPELL.C:2041-2052 (TASK-815).</summary>
public class CastRingFillTests {
    [Fact]
    public void TheSweepHas29TurnFramesThenOnePerPower() {
        Assert.Equal(29 + 20, FillFrameCount(20));
    }

    [Fact]
    public void TheFirstFrameTurnsOnlyTheLastPosition() {
        Assert.Equal(IconFor(SliderRingIcon, 29, markAnchors: true), FillIconAt(0, 29, 1, 20));
        Assert.Equal(SliderFilledIcon, FillIconAt(0, 28, 1, 20));
        Assert.Equal(SliderFilledIcon, FillIconAt(0, 4, 1, 20)); // unmarked in this phase
    }

    [Fact]
    public void TheLastTurnFrameLeavesOnlyPositionZero() {
        Assert.Equal(SliderFilledIcon, FillIconAt(28, 0, 1, 20));
        Assert.Equal(IconFor(SliderRingIcon, 1, markAnchors: true), FillIconAt(28, 1, 1, 20));
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(5, 15)]
    public void TheLastFrameIsTheRestingSlider(int min, int max) {
        int last = FillFrameCount(max) - 1;
        for (int p = 0; p < PositionCount; p++) {
            int resting = IsInAffordableBand(p, min, max)
                ? IconFor(SliderFilledIcon, p, BandMarksAnchors)
                : IconFor(SliderRingIcon, p, markAnchors: true);
            Assert.Equal(resting, FillIconAt(last, p, min, max));
        }
    }

    [Fact]
    public void TheBandGrowsFromTheMinimum() {
        // Second band frame of a 5-15 spell: reach 2, still below the minimum's position 4.
        Assert.Equal(IconFor(SliderRingIcon, 4, markAnchors: true), FillIconAt(30, 4, 5, 15));
        // Reach 5 covers position 4 only.
        Assert.Equal(IconFor(SliderFilledIcon, 4, BandMarksAnchors), FillIconAt(33, 4, 5, 15));
        Assert.Equal(IconFor(SliderRingIcon, 5, markAnchors: true), FillIconAt(33, 5, 5, 15));
    }
}
