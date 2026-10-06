namespace BetrayalAtKrondor.Tests.Location;

using GameData.Resources.Location;
using Xunit;

/// <summary>
/// The full map names the town under the pointer — <c>fmap_hit_test_cursor</c> and the label
/// placement in <c>fmap_screen_run</c> (canassa SCREENS/FMAP.C), TASK-793.
/// </summary>
public class FullMapTownHoverTests {
    // The shipped header (3,3,9,9) scaled into canonical space (x5 / x6).
    private static FullMapTowns Towns() => new("FMAP_TWN.DAT") {
        IconAnchorX = 15, IconAnchorY = 18, IconWidth = 45, IconHeight = 54,
        Towns = {
            new FullMapTown { Name = "Krondor", X = 400, Y = 600 },
            new FullMapTown { Name = "Sethanon", X = 1000, Y = 300 },
        },
    };

    [Fact]
    public void ThePointerOverATownsIconFindsIt() {
        // x0 = X - (W - anchor)/2 = 385, inclusive to x0 + W = 430; y0 = 600 - 18 = 582 .. 636.
        Assert.Equal(0, Towns().TownAt(400, 600));
        Assert.Equal(0, Towns().TownAt(385, 582));
        Assert.Equal(0, Towns().TownAt(430, 636));
        Assert.Equal(1, Towns().TownAt(1010, 310));
    }

    [Fact]
    public void JustOutsideTheIconFindsNothing() {
        Assert.Equal(-1, Towns().TownAt(384, 600));
        Assert.Equal(-1, Towns().TownAt(431, 600));
        Assert.Equal(-1, Towns().TownAt(400, 581));
        Assert.Equal(-1, Towns().TownAt(400, 637));
    }

    [Fact]
    public void TheLabelIsCentredOverTheTownOneLabelHeightAbove() {
        (int centreX, int top) = Towns().LabelPlacement(0, labelHeight: 66);
        Assert.Equal(400 + 15 / 2, centreX);
        Assert.Equal(600 - 66, top);
    }
}
