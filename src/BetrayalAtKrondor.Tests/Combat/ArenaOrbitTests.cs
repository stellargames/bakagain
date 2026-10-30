namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using GameData.Resources.Layout;
using Xunit;

/// <summary>The Enhanced combat camera's orbit around the board's centre (TASK-972 phase 1).</summary>
public class ArenaOrbitTests {
    private static readonly EnhancedCombatLayout Layout = new();
    private const int Cell = 300;

    private static ArenaOrbit Make(double yaw = 0, double pitch = 45, double distance = 4000) =>
        new(Layout, (1000, 2000), yaw, pitch, distance);

    private static double Horizontal(ArenaOrbit o) {
        var e = o.EyePosition();
        return System.Math.Sqrt(((e.X - 1000) * (e.X - 1000)) + ((e.Y - 2000) * (e.Y - 2000)));
    }

    [Fact]
    public void ADragTurnsAroundTheBoardCentre_TheDistanceStays() {
        var o = Make();
        var before = o.EyePosition();
        o.Drag(100, 0);
        var after = o.EyePosition();

        Assert.NotEqual(before.X, after.X);
        Assert.Equal(100 * Layout.OrbitDegreesPerPixel, o.YawDegrees, 6);
        double Dist(ArenaOrbit x) {
            var e = x.EyePosition();
            return System.Math.Sqrt(((e.X - 1000) * (e.X - 1000)) + ((e.Y - 2000) * (e.Y - 2000)) + (e.Height * e.Height));
        }
        Assert.Equal(4000, Dist(o), 3);
        Assert.Equal(4000 * System.Math.Cos(45 * System.Math.PI / 180), Horizontal(o), 3);
    }

    [Fact]
    public void PitchIsClampedToTheLayoutLimits() {
        var o = Make();
        o.Drag(0, 100000);
        Assert.Equal(Layout.PitchMaxDegrees, o.PitchDegrees, 6);
        o.Drag(0, -100000);
        Assert.Equal(Layout.PitchMinDegrees, o.PitchDegrees, 6);
    }

    [Fact]
    public void UndergroundPivotsOnThePlayableRows() {
        var (_, awayLow) = CombatArenaPlacement.CellOffset(0, 0, Cell);
        var (_, awayLast) = CombatArenaPlacement.CellOffset(0, CombatGrid.UndergroundPlayableRows - 1, Cell);
        var (_, awayTop) = CombatArenaPlacement.CellOffset(0, CombatGrid.Height - 1, Cell);

        var under = ArenaOrbit.BoardCentre(Cell, underground: true);
        var above = ArenaOrbit.BoardCentre(Cell, underground: false);

        Assert.Equal((awayLow + awayLast) / 2.0, under.Y, 6);
        Assert.Equal((awayLow + awayTop) / 2.0, above.Y, 6);
        var (a0, _) = CombatArenaPlacement.CellOffset(0, 0, Cell);
        var (a7, _) = CombatArenaPlacement.CellOffset(CombatGrid.Width - 1, 0, Cell);
        Assert.Equal((a0 + a7) / 2.0, under.X, 6);
    }

    [Fact]
    public void BoardCornersAreTheOuterEdgesOfThePlayableCells() {
        var (a0, y0) = CombatArenaPlacement.CellOffset(0, 0, Cell);
        var (a7, yTop) = CombatArenaPlacement.CellOffset(CombatGrid.Width - 1, CombatGrid.Height - 1, Cell);
        var (_, yUnder) = CombatArenaPlacement.CellOffset(0, CombatGrid.UndergroundPlayableRows - 1, Cell);

        var above = ArenaOrbit.BoardCorners(Cell, underground: false);
        Assert.Equal((a0 - 150.0, y0 - 150.0), above[0]);
        Assert.Equal((a7 + 150.0, yTop + 150.0), above[3]);
        Assert.Equal(yUnder + 150.0, ArenaOrbit.BoardCorners(Cell, underground: true)[3].Y);
    }

    [Fact]
    public void StartPitchIsInsideTheLimits() {
        Assert.InRange(Layout.StartPitchDegrees, Layout.PitchMinDegrees, Layout.PitchMaxDegrees);
    }

    [Fact]
    public void ResetReturnsToTheStartPose() {
        var o = Make(10, 40, 3000);
        o.Drag(300, 50);
        o.Zoom(0.7);
        o.TurnQuarter(1);
        o.Reset();

        Assert.Equal(10, o.YawDegrees, 6);
        Assert.Equal(40, o.PitchDegrees, 6);
        Assert.Equal(3000, o.Distance, 6);
    }

    [Fact]
    public void TurnQuarterAddsNinetyDegrees() {
        var o = Make(10);
        o.TurnQuarter(1);
        Assert.Equal(100, o.YawDegrees, 6);
        o.TurnQuarter(-1);
        o.TurnQuarter(-1);
        Assert.Equal(-80, o.YawDegrees, 6);
    }

    [Fact]
    public void ZoomIsClamped() {
        var o = Make(distance: 1000);
        o.Zoom(1000);
        Assert.Equal(1000 * Layout.ZoomMax, o.Distance, 2);
        o.Zoom(0.0001);
        Assert.Equal(1000 * Layout.ZoomMin, o.Distance, 2);
        o.Zoom(2);
        Assert.Equal(1000 * Layout.ZoomMin * 2, o.Distance, 2);
    }

    [Fact]
    public void AStartPitchOutsideTheLimitsIsClampedUpFront() {
        var o = Make(pitch: 5);
        Assert.Equal(Layout.PitchMinDegrees, o.PitchDegrees, 6);
        o.Drag(10, 0);
        Assert.Equal(Layout.PitchMinDegrees, o.PitchDegrees, 6);
        o.Reset();
        Assert.Equal(Layout.PitchMinDegrees, o.PitchDegrees, 6);
    }

    [Fact]
    public void APitchFloorLetsADragTiltPastTheLayoutMinimum() {
        var o = Make(pitch: 40);
        o.PitchFloorDegrees = 15;
        o.Drag(0, -100000);
        Assert.Equal(15, o.PitchDegrees, 6);
    }

    [Fact]
    public void ABoardCutAtARowEndsAtThatRowsFarEdge() {
        var full = ArenaOrbit.BoardCorners(300, underground: false);
        var cut = ArenaOrbit.BoardCorners(300, underground: false, lastRow: 5);
        Assert.Equal(full[0], cut[0]);
        Assert.Equal(full[0].Y + (6 * 300), cut[2].Y, 6);
        Assert.Equal((cut[0].Y + cut[2].Y) / 2, ArenaOrbit.BoardCentre(300, false, 5).Y, 6);
    }

    [Fact]
    public void ThePhoneOrbitOpensAtItsStartZoom_ResetReturnsThere_AndNeverZoomsOutPastTheWholeBoard() {
        var o = new ArenaOrbit(Layout, (1000, 2000), 0, 45, 1000, Layout.PhoneStartZoom);
        Assert.Equal(1000 * Layout.PhoneStartZoom, o.Distance, 2);
        o.Zoom(1000);
        Assert.Equal(1000, o.Distance, 2);   // the start distance is the whole-board fit
        o.Zoom(0.0001);
        Assert.Equal(1000 * Layout.ZoomMin, o.Distance, 2);
        o.Reset();
        Assert.Equal(1000 * Layout.PhoneStartZoom, o.Distance, 2);
    }

    [Fact]
    public void TheCentreLeansTowardTheFocusAsTheCameraZoomsIn_TheWholeBoardStaysCentredAtTheFarthest() {
        var o = new ArenaOrbit(Layout, (1000, 2000), 0, 45, 1000);
        o.Focus((1300, 1400));
        Assert.Equal((1000d, 2000d), o.Centre);   // zoom 1: the whole board, centred
        o.Zoom(0.0001);
        Assert.Equal(1300, o.Centre.X, 3);        // the closest: on the focus
        Assert.Equal(1400, o.Centre.Y, 3);
        o.Reset();
        o.Zoom((1 + Layout.ZoomMin) / 2);
        Assert.Equal(1150, o.Centre.X, 3);        // half way in, half way over
        Assert.Equal(1700, o.Centre.Y, 3);
    }

    [Fact]
    public void AGlideEasesTheFocusInAndOut_ThenStops() {
        var o = new ArenaOrbit(Layout, (1000, 2000), 0, 45, 1000);
        o.Zoom(0.0001);                            // the closest: the centre is the focus
        o.Focus((0, 0));
        o.GlideFocus((1000, 0), 1);
        Assert.True(o.Advance(0.1));
        Assert.Equal(28, o.Centre.X, 3);           // slow start: smoothstep(0.1) = 0.028
        Assert.True(o.Advance(0.4));
        Assert.Equal(500, o.Centre.X, 3);          // half way at half time
        Assert.True(o.Advance(0.4));
        Assert.Equal(972, o.Centre.X, 3);          // slow end
        Assert.False(o.Advance(0.5));
        Assert.Equal(1000, o.Centre.X, 3);         // arrived, and no further
        Assert.False(o.Advance(1));
        o.GlideFocus((0, 0), 0);
        Assert.Equal(0, o.Centre.X, 3);            // no time: a jump
    }

    [Fact]
    public void ARefitMovesTheLimits_TheCameraDistanceStays() {
        var o = new ArenaOrbit(Layout, (1000, 2000), 0, 45, 1000, 0.5);
        o.Refit(1400);   // a quarter turn: the 13-cell side now lies across
        Assert.Equal(500, o.Distance, 3);    // a close view does not drift while orbiting
        o.Zoom(1000);
        Assert.Equal(1400, o.Distance, 3);   // the farthest is the new whole-board fit
        o.Refit(1000);
        Assert.Equal(1000, o.Distance, 3);   // a view past the new fit is pulled in to it
        o.Refit(0);
        Assert.Equal(1000, o.Distance, 3);   // a failed fit keeps the last
    }

    [Fact]
    public void ATrapOrbitTurnsAtMostFifteenDegreesEitherWay() {
        var o = new ArenaOrbit(Layout, (1000, 2000), 10, 45, 4000, trap: true);
        o.Drag(100000, 0);
        Assert.Equal(25, o.YawDegrees, 6);
        o.Drag(-100000, 0);
        Assert.Equal(-5, o.YawDegrees, 6);
    }

    [Fact]
    public void ATrapOrbitIgnoresQuarterTurns() {
        var o = new ArenaOrbit(Layout, (0, 0), 0, 45, 4000, trap: true);
        o.TurnQuarter(1);
        Assert.Equal(0, o.YawDegrees, 6);
    }

    [Fact]
    public void ACombatOrbitIsUnchanged() {
        var o = new ArenaOrbit(Layout, (0, 0), 0, 45, 4000);
        o.Drag(100000, 0);
        Assert.True(o.YawDegrees > 15);
        o.TurnQuarter(1);
        Assert.False(o.IsTrap);
    }
}
