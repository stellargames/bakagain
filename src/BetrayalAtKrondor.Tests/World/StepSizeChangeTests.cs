namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// When a step-size change triggers the roaming-encounter reset —
/// <c>worldmove_rgn_chap_trans_apply</c> (WORLDMOV.C:65).
/// </summary>
public class StepSizeChangeTests {
    [Fact]
    public void ANINCREASEResetsTheRoamers() =>
        Assert.True(StepSizeChange.ResetsRoamers(1600, 2400));

    [Fact]
    public void ADECREASEDoesNot() =>
        Assert.False(StepSizeChange.ResetsRoamers(2400, 1600));

    [Fact]
    public void NOCHANGEDoesNot() =>
        Assert.False(StepSizeChange.ResetsRoamers(1600, 1600));

    [Fact]
    public void THEBASELINEMovesEvenWhenNothingFired() {
        // *** THE ASYMMETRY, AND THE ONLY REASON NewBaseline IS A FUNCTION. *** The original assigns
        // nLastSeenStepSpeed OUTSIDE the increase check. Storing it only when the sweep ran would
        // leave the baseline high after a decrease, and the next increase back to a previously-seen
        // value would then be judged "not an increase" and silently skip the reset.
        Assert.Equal(1600, StepSizeChange.NewBaseline(2400, 1600));

        // Lower, then back up: the second move IS an increase against the re-baselined value.
        int baseline = StepSizeChange.NewBaseline(2400, 1600);
        Assert.True(StepSizeChange.ResetsRoamers(baseline, 2400));
    }

    [Fact]
    public void THEGRIDSTRIDEArmFollowsTheSameRule() {
        // Same shape, different action (worldmove_plr_hdg_align_grid). Named separately so a reader
        // is not left deducing that the two arms agree.
        Assert.True(StepSizeChange.AlignsHeading(1024, 2048));
        Assert.False(StepSizeChange.AlignsHeading(2048, 1024));
    }

    // worldmove_align_to_grid_stride (WORLDMOV.C:438-460): nearest multiple of the stride, a half up.
    [Theory]
    [InlineData(2048, 4096, 4096)]   // exactly half way rounds up
    [InlineData(2047, 4096, 0)]
    [InlineData(5000, 4096, 4096)]
    [InlineData(8192, 4096, 8192)]   // already aligned
    [InlineData(65000, 4096, 0)]     // rounds up past 0xF000 and wraps to north
    [InlineData(3000, 1024, 3072)]
    public void THEHEADINGSnapsToTheNearestStride(int yaw, int stride, int expected) =>
        Assert.Equal(expected, StepSizeChange.AlignedHeading((ushort)yaw, stride));
}
