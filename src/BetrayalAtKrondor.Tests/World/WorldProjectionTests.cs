namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.Spells;
using GameData.Resources.World;
using System;
using Xunit;

/// <summary>
/// The world camera's lens: isotropic, square pixels, the original's horizontal field (TASK-764).
/// </summary>
/// <remarks>
/// The original projects <c>X * (1 &lt;&lt; shift) / Z</c> VGA pixels in both axes. A VGA pixel is 5
/// canonical units across and 6 down, so the port's square camera takes the HORIZONTAL focal length
/// (5 << shift canonical units, which the extractors emit as <c>FocalLength</c>) and the 1.2 goes into
/// the world's heights (<see cref="WorldUp"/>), never into the camera's aspect.
/// </remarks>
public class WorldProjectionTests {
    /// <summary>START.DAT's viewport, canonical: VGA 294x101 scaled 5x6.</summary>
    private const int TravelViewWidth = 1470;
    private const int TravelViewHeight = 606;

    /// <summary>START.DAT's shift 9 as a canonical focal length.</summary>
    private const int TravelFocalLength = 5 << 9;

    [Fact]
    public void TheHorizontalFieldIsTheOriginals() {
        // The original's half-width is 147 VGA px at a focal of 512 VGA px. A square camera given
        // the viewport's own aspect must land exactly there.
        double vFov = WorldProjection.VerticalFovDegrees(TravelViewHeight, TravelFocalLength);
        double tanHalfV = Math.Tan(vFov * Math.PI / 360.0);
        double tanHalfH = tanHalfV * TravelViewWidth / TravelViewHeight;

        Assert.Equal(147.0 / 512.0, tanHalfH, 9);
    }

    [Fact]
    public void TheVerticalFieldIsTheOriginalsScreenFieldInSquarePixels() {
        // 101 VGA rows at 6 canonical units each, against a focal of 512 * 5: tan = 0.6 * 101 / 512.
        // This is the 13.50 degrees TASK-439 replaced with 11.27 plus a 6:5 camera aspect — right
        // for a square world, which is what the extractor's world-up bake makes.
        double vFov = WorldProjection.VerticalFovDegrees(TravelViewHeight, TravelFocalLength);

        Assert.Equal(0.6 * 101 / 512, Math.Tan(vFov * Math.PI / 360.0), 9);
        Assert.Equal(13.50, vFov, 2);
    }

    [Fact]
    public void TheLocatorInsetIsSHORTERThanTheTravelViewSoItsFovIsNarrower() {
        // The inset is its own rectangle, not the travel one — 167x89 against 294x101. Sharing the
        // zone's focal length is not the same as sharing its FOV.
        (int _, int _, int _, int height) = FieldSpells.LocatorViewport;
        double locator = WorldProjection.VerticalFovDegrees(height * 6, TravelFocalLength);
        double overhead = WorldProjection.VerticalFovDegrees(TravelViewHeight, TravelFocalLength);

        Assert.Equal(89, height);
        Assert.True(locator < overhead, $"locator {locator:F2} should be narrower than map {overhead:F2}");
    }

    [Fact]
    public void UndergroundShiftEightSeesTwiceTheGroundOfShiftNine() {
        // Z10-Z12 ship 8 (ZONE.C:76-77 reads it per zone): half the focal length, twice the ground.
        double nine = WorldProjection.VerticalFovDegrees(TravelViewHeight, 5 << 9);
        double eight = WorldProjection.VerticalFovDegrees(TravelViewHeight, 5 << 8);
        double ratio = Math.Tan(eight * Math.PI / 360.0) / Math.Tan(nine * Math.PI / 360.0);
        Assert.Equal(2.0, ratio, 6);
    }

    [Fact]
    public void AZeroHeightOrFocalIsRejectedRatherThanReturningZeroDegrees() {
        // A camera with FOV 0 renders nothing and looks like a black screen, not like a bad
        // argument. The map screens read their height from data that can arrive unresolved.
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldProjection.VerticalFovDegrees(0, TravelFocalLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldProjection.VerticalFovDegrees(TravelViewHeight, 0));
    }
}
