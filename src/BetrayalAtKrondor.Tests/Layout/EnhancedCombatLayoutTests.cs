namespace BetrayalAtKrondor.Tests.Layout;

using GameData.Resources.Layout;
using Xunit;

public class EnhancedCombatLayoutTests {
    [Fact]
    public void DefaultsMatchTheApprovedFrames() {
        var layout = new EnhancedCombatLayout();

        // Desktop
        Assert.Equal(0.27f, layout.BandHeight);
        Assert.Equal(new float[] { 278, 340, 304, 256, 360 }, layout.BandSlotWidths);
        Assert.Equal(2, layout.LedgerLogLines);
        Assert.Equal(56f, layout.PartyCoinPx);
        Assert.Equal(7, layout.MaxPartyCoins);

        // Phone
        Assert.Equal(54f, layout.TileWidthPt);
        Assert.Equal(50f, layout.TileHeightPt);
        Assert.Equal(12f, layout.ConfirmCancelGapPt);
        Assert.Equal(3, layout.MaxHeroCoins);
        Assert.Equal(44f, layout.FoeChipPt);
        Assert.Equal(11f, layout.MinTextPt);

        // Shared
        Assert.Equal(0.25f, layout.OrbitDegreesPerPixel);
        Assert.Equal(25f, layout.PitchMinDegrees);
        Assert.Equal(55f, layout.PitchMaxDegrees);
        Assert.Equal(40f, layout.StartPitchDegrees);
        Assert.Equal(0.2f, layout.ZoomMin);
        Assert.Equal(1f, layout.ZoomMax);
        Assert.Equal(0.6f, layout.PhoneStartZoom);
        Assert.Equal(0.55f, layout.SeeThroughAlpha);
        Assert.Equal(67, layout.HitPipLikelyAtLeast);
        Assert.Equal(34, layout.HitPipEvenAtLeast);
        Assert.Equal(0.5f, layout.LongPressSeconds);
        Assert.Equal(0.3f, layout.RetapCommitMinSeconds);
        Assert.Equal(0.06f, layout.MarkFrameCells);
        Assert.Equal(0.05f, layout.MarkLiftCells);
        Assert.Equal(4, layout.MarkReachRgba.Length);
    }

    [Fact]
    public void BandSlotWidthsCoverTheWidth() {
        // 1600 wide, less the frame's margins and gaps, as drawn.
        var layout = new EnhancedCombatLayout();

        Assert.Equal(1538f, System.Linq.Enumerable.Sum(layout.BandSlotWidths));
    }
}
