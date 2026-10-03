namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

public class HorizonPanoramaTests {
    [Fact]
    public void FacingNorthTheLeftEdgeIsThreePanelsIn() {
        // yaw 0: uQuad = (-1 >> 14) & 3 = 3, uScroll = 0 -> panel 3 at x 0 (SKYREND.C:194-196).
        Assert.Equal((768 + 13) / 1024.0, HorizonPanorama.LeftEdgeRingFraction(0, 256), 6);
    }

    [Fact]
    public void TurningScrollsOnePixelPerSixtyFourYawUnits() {
        // yaw 0x4000 (a quarter turn): Y = 256 -> uQuad 2, uScroll 0 -> panel 2 at x 0.
        Assert.Equal((512 + 13) / 1024.0, HorizonPanorama.LeftEdgeRingFraction(0x4000, 256), 6);
        Assert.Equal((768 - 1 + 13) / 1024.0, HorizonPanorama.LeftEdgeRingFraction(64, 256), 6);
    }

    [Fact]
    public void NativeSize_ViewportShows294OfTheRingAnd29RowsOf101() {
        Assert.Equal(294 / 1024.0, HorizonPanorama.VisibleRingFraction(256), 6);
        Assert.Equal(29 / 101.0, HorizonPanorama.HeightFraction(29), 6);
    }

    [Fact]
    public void TheOriginalsYawRunsAntiClockwise() {
        // c296 (zone 3) faces E with saved yaw 49152, and a right turn subtracts 4096 (bak-drive pos).
        // A clockwise heading in degrees therefore maps to -degrees (TASK-763).
        Assert.Equal(0, HorizonPanorama.YawFromClockwiseDegrees(0));
        Assert.Equal(49152, HorizonPanorama.YawFromClockwiseDegrees(90));
        Assert.Equal(13 / 1024.0, HorizonPanorama.LeftEdgeRingFraction(HorizonPanorama.YawFromClockwiseDegrees(90), 256), 6);
    }
}
