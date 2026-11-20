namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// <see cref="ProjectileInterception"/> — crystal ground stops a shot (CACTOR.C:1114-1130; forum p=60).
/// </summary>
public class ProjectileInterceptionTests {
    private const int Crystal = CrystalChain.FirstCrystalId;

    // Two crystals at (3,2) and (3,6): the run between them is column 3, rows 3..5.
    private static TrapPuzzle TwoPoles() =>
        TrapPuzzleBuilder.Build(new List<(int, int, int)> { (Crystal, 3, 2), (Crystal, 3, 6) });

    [Fact]
    public void AShotAcrossTheRunBetweenTwoPolesIsSwallowed() {
        // A blaster at (0,4) firing east at a party member at (6,4) crosses (3,4).
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((0, 4), (6, 4)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Zapped, outcome.Fate);
        Assert.Equal((3, 4), (outcome.X, outcome.Y));
        Assert.Equal(new[] { (3, 3), (3, 4), (3, 5) }, outcome.LitRun.Where(c => c.Y is >= 3 and <= 5).OrderBy(c => c.Y).ToArray());
        Assert.Equal(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));   // the run is still there
    }

    [Fact]
    public void AShotThatCrossesNoRunReachesItsTarget() {
        var outcome = ProjectileInterception.Fly(TwoPoles(),
            ProjectileInterception.StraightPath((0, 1), (6, 1)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Reaches, outcome.Fate);
    }

    [Fact]
    public void AFlamecastShotIntoAPoleCollapsesItsRun() {
        // Down column 3 from a blaster at (3,0): the first crystal cell is the pole at (3,2).
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((3, 0), (3, 4)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Collapsed, outcome.Fate);
        Assert.NotEqual(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));
    }

    [Fact]
    public void AnyOtherShotIntoAPoleIsStoppedAndLeavesTheRun() {
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((3, 0), (3, 4)), shape: 3);

        Assert.Equal(ProjectileInterception.Fate.Stopped, outcome.Fate);
        Assert.Equal(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));
    }

    [Fact]
    public void TheStraightPathSkipsTheShooterAndEndsOnTheTarget() {
        Assert.Equal(new[] { (1, 4), (2, 4), (3, 4) }, ProjectileInterception.StraightPath((0, 4), (3, 4)).ToArray());
        Assert.Equal(new[] { (2, 1), (2, 0) }, ProjectileInterception.StraightPath((2, 2), (2, 0)).ToArray());
    }

    // ---- every flight, not only a blaster's (trap audit #4) ---------------------------------

    [Fact]
    public void AFlightSamplesEveryCellOnAStraightLineAndEndsOnTheTarget() {
        // 250 units a step on 300-unit cells (WORLDHIT.C:663-699, START.DAT): no cell is skipped.
        Assert.Equal(new[] { (1, 4), (2, 4), (3, 4), (4, 4), (5, 4), (6, 4) },
            ProjectileInterception.FlightCells(0, 4, 6, 4, ProjectileInterception.SpellSpeed, 300).ToArray());
    }

    [Fact]
    public void ADiagonalFlightStopsShortOfTheTargetsCell() {
        // The step count is the OCTAGONAL distance over the speed (WORLDHIT.C:663-668): 900+337 =
        // 1237 units, 4 steps of 250, against a true 1273. So the last step is drawn in (2,2) and
        // the target's own cell is never sampled -- a target standing on the current diagonally
        // away is not shielded by it.
        int[][] cells = ProjectileInterception.FlightCells(0, 0, 3, 3, ProjectileInterception.SpellSpeed, 300)
            .Select(c => new[] { c.X, c.Y }).ToArray();
        Assert.Contains(new[] { 1, 1 }, cells);
        Assert.Equal(new[] { 2, 2 }, cells[^1]);
    }

    [Fact]
    public void AnOpaqueDiamondStopsAShotAndASeeThroughOneDoesNot() {
        // CACTOR.C:1144-1153: element 9 or a wreck (0x28) on its tile ends the flight; 10 does not.
        TrapPuzzle opaque = TrapPuzzleBuilder.Build(new List<(int, int, int)> { (9, 3, 4) });
        TrapPuzzle clear = TrapPuzzleBuilder.Build(new List<(int, int, int)> { (10, 3, 4) });

        Assert.Equal(ProjectileInterception.Fate.Stopped, ProjectileInterception.Fly(opaque,
            ProjectileInterception.StraightPath((0, 4), (6, 4)), shape: 1).Fate);
        Assert.Equal(ProjectileInterception.Fate.Reaches, ProjectileInterception.Fly(clear,
            ProjectileInterception.StraightPath((0, 4), (6, 4)), shape: 1).Fate);
    }

    [Fact]
    public void AWreckedPoleStopsAShot() {
        TrapPuzzle puzzle = TwoPoles();
        puzzle.CollapseUntilIsolated(3, 2);
        Assert.Equal(CrystalChain.WreckElementId, puzzle.ElementAt(3, 2)?.ElementId);

        Assert.Equal(ProjectileInterception.Fate.Stopped, ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((0, 2), (6, 2)), shape: 1).Fate);
    }

    [Fact]
    public void OutOfBoundsGroundStopsAShotInAnOrdinaryFightToo() {
        // CACTOR.C:1155-1157, terrain 2: the walled corners and underground back rows.
        var grid = new CombatGrid();
        grid.SetTerrain(3, 4, CombatTerrain.OutOfBounds);

        var outcome = ProjectileInterception.Fly(grid, null, ProjectileInterception.StraightPath((0, 4), (6, 4)), shape: 1);

        Assert.Equal(ProjectileInterception.Fate.Stopped, outcome.Fate);
        Assert.Equal((3, 4), (outcome.X, outcome.Y));
    }

    [Fact]
    public void AMissThatFliesIntoTheCurrentDiesThereAndStrikesNobodyBeyondIt() {
        // The cell effect runs before the bystander test each step, and a shape of -1 breaks the
        // loop first (WORLDHIT.C:697-716).
        var miss = SpellProjectileMiss.Fly(0, 4, 6, 4, 300, n => 0, strikes: (_, _) => true,
            stops: (x, _) => x == 1);

        Assert.Null(miss.Intercepted);
        Assert.Equal((1, 4), miss.StoppedAt);
    }

    [Fact]
    public void TheLineOfFireIsBlockedByAnOpaqueDiamondAWreckAndWalls_ButNotByTheCurrent() {
        // combat_actor_trace_proj_path runs the cell effect with do_apply = 0 (CACTOR.C:1186), so the
        // current's arm returns at once and only the diamond, wreck and out-of-bounds arms stop it.
        TrapPuzzle puzzle = TrapPuzzleBuilder.Build(new List<(int, int, int)> { (9, 2, 4), (10, 4, 4), (Crystal, 3, 2), (Crystal, 3, 6) });
        puzzle.Grid.SetTerrain(5, 4, CombatTerrain.OutOfBounds);

        Assert.True(ProjectileInterception.BlocksSight(puzzle.Grid, puzzle, 2, 4));
        Assert.False(ProjectileInterception.BlocksSight(puzzle.Grid, puzzle, 4, 4));
        Assert.False(ProjectileInterception.BlocksSight(puzzle.Grid, puzzle, 3, 4));
        Assert.True(ProjectileInterception.BlocksSight(puzzle.Grid, puzzle, 5, 4));
        Assert.False(CombatLineOfFire.IsClear(0, 4, 3, 4, (x, y) => ProjectileInterception.BlocksSight(puzzle.Grid, puzzle, x, y)));
    }

    [Fact]
    public void ABlasterShotAtAClearDiamondFliesOnAndStrikesWhoeverIsBeyond() {
        // Trap audit #8: the stub on the landed cell has class -1, so the shot is a miss
        // (combatgrid_tile_blockd_kind10 is 0 for element 10) with no deflection, and the first
        // living combatant it crosses past the stub is struck (WORLDHIT.C:616-623, 697-731).
        TrapPuzzle puzzle = TrapPuzzleBuilder.Build(new List<(int, int, int)> { (10, 3, 5) });

        var past = ProjectileInterception.FlyOnPast(puzzle.Grid, puzzle, (0, 5), (3, 5),
            strikes: (x, y) => (x, y) == (6, 5) || (x, y) == (3, 5));

        Assert.Null(past.Grounded);
        Assert.Equal((6, 5), past.Struck);
    }

    [Fact]
    public void TheCurrentBeyondAClearDiamondStillSwallowsThatShot() {
        TrapPuzzle puzzle = TrapPuzzleBuilder.Build(new List<(int, int, int)> { (10, 3, 5) });
        puzzle.Grid.SetTerrain(5, 5, CombatTerrain.Crystal);

        var past = ProjectileInterception.FlyOnPast(puzzle.Grid, puzzle, (0, 5), (3, 5),
            strikes: (x, y) => (x, y) == (6, 5));

        Assert.Equal(ProjectileInterception.Fate.Zapped, past.Grounded?.Fate);
        Assert.Null(past.Struck);
    }
}
