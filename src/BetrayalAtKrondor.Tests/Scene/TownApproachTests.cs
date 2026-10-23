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
    public void ThePoseSlidesAlongThePathAtTheWorldStepAndTurnsAtTheCentre() {
        // West two cells, the second leaving south-west (0x6000): a hold frame, a frame per world
        // step, a frame per turn stride at the bend, a hold frame.
        var path = new System.Collections.Generic.List<(int X, int Y, ushort Heading)> {
            (C(16), C(20), 0x4000), (C(15), C(20), 0x6000), (C(14), C(19), 0x6000),
        };

        Assert.Equal(2 + 2 + 4, TownApproach.FramesFor(path, step: 1600, turnStride: 2048));
        Assert.Equal(2 + 8 + 4, TownApproach.FramesFor(path, step: 400, turnStride: 2048));
        Assert.Equal(2 + 2 + 8, TownApproach.FramesFor(path, step: 1600, turnStride: 1024));
        Assert.Equal(4 * TownApproach.SecondsPerFrame, TownApproach.SecondsFor(path, 1600, 0), 6);

        Assert.Equal((C(16) - (Cell / 2), C(20), (ushort)0x4000), TownApproach.PoseAt(path, 1600, 2048, frames: 1.5));
        Assert.Equal((C(15), C(20), (ushort)0x5000), TownApproach.PoseAt(path, 1600, 2048, frames: 4));
        Assert.Equal((C(14), C(19), (ushort)0x6000), TownApproach.PoseAt(path, 1600, 2048, frames: 99));
    }

    // The LaMut approach as the original walked it on t926 SAVE01 (2026-10-23, Spice86): each cell
    // with the yaw it left on, read off the camera frame by frame. Sub-cells of tile 10 from (16,20)
    // west into tile 9; global cell = tile * 40 + sub. (WalkPath builds this from the zone's road;
    // the test is about its pace, so it takes the measured cells as given.)
    private static readonly (int X, int Y, ushort Heading)[] LaMutWalk = {
        (416, 20, 0x4000), (415, 20, 0x4000), (414, 20, 0x6000), (413, 19, 0x6000), (412, 18, 0x4000),
        (411, 18, 0x4000), (410, 18, 0x4000), (409, 18, 0x4000), (408, 18, 0x4000), (407, 18, 0x2000),
        (406, 19, 0x2000), (405, 20, 0x2000), (404, 21, 0x2000), (403, 22, 0x4000), (402, 22, 0x4000),
        (401, 22, 0x4000), (400, 22, 0x4000), (399, 22, 0x4000), (398, 22, 0x6000), (397, 21, 0x4000),
        (396, 21, 0x4000), (395, 21, 0x4000), (394, 21, 0x4000), (393, 21, 0x4000), (392, 21, 0x4000),
        (391, 21, 0x4000), (390, 21, 0x4000),
    };

    [Fact]
    public void LaMutsApproachTakesTheOriginalsFrameCount() {
        // Measured in the original at Large step / Medium turn (KRONDOR.CFG 02 01): 26 steps, six
        // 45-degree bends turned 2048 a frame (4 frames each), plus the first and last frames
        // (MAP.C:534-536, :550-553) = 52 frames in 33.9M cycles, 0.664M a frame. Its own held walk
        // on the same run cost 1.164M cycles a step, and the port walks 8.84 steps a second
        // (ClassicMovementDriver.RepeatStepsPerSecond), so a frame is 0.570 / 8.84 = 0.0645 s and
        // the walk 3.35 s.
        var path = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(LaMutWalk,
            c => (C(c.X), C(c.Y), c.Heading)));

        Assert.Equal(52, TownApproach.FramesFor(path, step: 1600, turnStride: 2048));
        Assert.InRange(TownApproach.SecondsFor(path, step: 1600, turnStride: 2048), 3.3, 3.4);
    }
}
