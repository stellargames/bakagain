namespace BetrayalAtKrondor.Tests.Scene;

using GameData.Resources.Scene;
using GameData.Resources.World;
using Xunit;

public class TownApproachTests {
    [Fact]
    public void TheOffsetIsTwoBytesInOneField() {
        // 5136 = 0x1410 -> X 0x10, Y 0x14. Reading it as one number lands nowhere.
        Assert.Equal(0x10, TownApproach.SubTileX(5136));
        Assert.Equal(0x14, TownApproach.SubTileY(5136));
    }

    [Fact]
    public void TheDestinationIsRelativeToTheTileThePartyIsStandingOn() {
        (long ax, long ay) = TownApproach.DestinationOf(10, 20, 5136);
        (long bx, long by) = TownApproach.DestinationOf(11, 20, 5136);

        Assert.Equal(WorldPlacement.CentreOf(10, 0x10), ax);
        Assert.Equal(WorldPlacement.CentreOf(20, 0x14), ay);
        // Same record, next tile east: one tile further, same sub-cell.
        Assert.Equal(ax + WorldPlacement.TileSize, bx);
        Assert.Equal(ay, by);
    }

    [Fact]
    public void TheDestinationIsCentredInItsSubCellNotOnTheCorner() {
        (long x, long _) = TownApproach.DestinationOf(0, 0, 0x0101);

        Assert.Equal(WorldPlacement.SubCellSize + WorldPlacement.SubCellCentre, x);
    }

    [Fact]
    public void UndergroundHalvesTheStepButNotTheDestination() {
        Assert.Equal(400, TownApproach.StepFor(400, underground: false));
        Assert.Equal(200, TownApproach.StepFor(400, underground: true));
    }

    [Fact]
    public void ThePartyIsLeftFacingAwayFromTheDoor() {
        // approachWalkToScenePosition @0x6ddc2: rotation = heading + 0x8000. Measured: LaMut's
        // def_town:0 (0x4000, west) leaves the party facing east, yaw 0xC000.
        Assert.Equal(unchecked((short)0xC000), TownApproach.FacingAfter(0x4000));
        Assert.Equal((short)0x2000, TownApproach.FacingAfter(0xA000));
    }
}

public class TownApproachWalkTests {
    private const int Cell = RoadTravel.CellSize;

    private static int C(int cell) => (cell * Cell) + RoadTravel.HalfCell;

    // Road cells by (cellX, cellY); anything else is grass.
    private static System.Func<int, int, bool> Road(params (int X, int Y)[] cells) =>
        (x, y) => System.Array.IndexOf(cells, (x / Cell, y / Cell)) >= 0;

    [Fact]
    public void TheWalkFollowsTheRoadFromTheRecordCellUntilItEnds() {
        // MAP.C:503-561: start on the record's cell with its heading, step along the road
        // (worldmove_crossing_check_8dir) and stop where no road continues. West is 0x4000.
        var road = Road((16, 20), (15, 20), (14, 20), (13, 20));

        var path = TownApproach.WalkPath(C(16), C(20), 0x4000, road);

        Assert.Equal(new[] { 16, 15, 14, 13 }, path.ConvertAll(p => p.X / Cell));
        Assert.All(path, p => Assert.Equal(20, p.Y / Cell));
    }

    [Fact]
    public void TheWalkTakesTheRoadsBends() {
        // At a cell centre the sweep (worldmove_sweep_adjacent_cells) turns onto the one
        // continuation: west two cells, then south (0x8000, -y) two more.
        var road = Road((16, 20), (15, 20), (15, 19), (15, 18));

        var path = TownApproach.WalkPath(C(16), C(20), 0x4000, road);

        Assert.Equal(new[] { (16, 20), (15, 20), (15, 19), (15, 18) },
            path.ConvertAll(p => (p.X / Cell, p.Y / Cell)));
        Assert.Equal(0x8000, path[1].Heading);
    }

    [Fact]
    public void NoRoadAheadIsAWalkOfNoSteps() {
        var path = TownApproach.WalkPath(C(16), C(20), 0x4000, Road((16, 20)));

        Assert.Single(path);
    }

    [Fact]
    public void ThePoseSlidesAlongThePathAtTheWorldStep() {
        var road = Road((16, 20), (15, 20), (14, 20));
        var path = TownApproach.WalkPath(C(16), C(20), 0x4000, road);

        // One world step a frame: 1600 units = a cell per step at Large, a quarter of one at Small.
        Assert.Equal(2 * TownApproach.SecondsPerStep, TownApproach.SecondsFor(path, step: 1600), 6);
        Assert.Equal(8 * TownApproach.SecondsPerStep, TownApproach.SecondsFor(path, step: 400), 6);

        (int x, int y, ushort heading) = TownApproach.PoseAt(path, cells: 1.5);
        Assert.Equal(C(16) - (Cell * 3 / 2), x);
        Assert.Equal(C(20), y);
        Assert.Equal(0x4000, heading);
        Assert.Equal((C(14), C(20)), (TownApproach.PoseAt(path, 99).X, TownApproach.PoseAt(path, 99).Y));
    }
}
