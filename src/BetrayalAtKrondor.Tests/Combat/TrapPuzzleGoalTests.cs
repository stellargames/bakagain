namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// When a trap puzzle ends. Two traps here: the goal is a row rather than a tile, and the test says
/// "solved" on a grid that has no exit at all.
/// </summary>
public class TrapPuzzleGoalTests {
    private static CombatGrid GridWithExitAt(params (int X, int Y)[] exits) {
        var grid = new CombatGrid();
        foreach ((int x, int y) in exits) {
            grid.SetTerrain(x, y, CombatTerrain.Exit);
        }
        return grid;
    }

    [Fact]
    public void ReachingTheExitsROWEndsItNotTheTile() {
        // Nobody has to stand on the marked cell. Testing the tile would leave puzzles unsolvable
        // wherever the intended path arrives beside the exit rather than on it.
        CombatGrid grid = GridWithExitAt((3, 9));

        Assert.True(TrapPuzzleGoal.PartyIsOut(grid, new[] { 9 }));
        Assert.True(TrapPuzzleGoal.PartyIsOut(grid, new[] { 11 }));
        Assert.False(TrapPuzzleGoal.PartyIsOut(grid, new[] { 8 }));
    }

    [Fact]
    public void OneMemberIsEnough() {
        CombatGrid grid = GridWithExitAt((3, 9));

        Assert.True(TrapPuzzleGoal.PartyIsOut(grid, new[] { 0, 2, 9 }));
    }

    [Fact]
    public void NobodyOutMeansNotSolved() {
        CombatGrid grid = GridWithExitAt((3, 9));

        Assert.False(TrapPuzzleGoal.PartyIsOut(grid, new[] { 0, 4, 8 }));
        Assert.False(TrapPuzzleGoal.PartyIsOut(grid, new int[0]));
    }

    [Fact]
    public void AGridWithNoExitReportsRowZeroAndSoAnyoneOnItIsOut() {
        // Which is why HasExit is a separate question and has to be asked first.
        var grid = new CombatGrid();

        Assert.False(TrapPuzzleGoal.HasExit(grid));
        Assert.Equal(0, TrapPuzzleGoal.ExitRow(grid));
        Assert.True(TrapPuzzleGoal.PartyIsOut(grid, new[] { 0 }));
    }

    [Fact]
    public void HasExitFindsOne() {
        Assert.True(TrapPuzzleGoal.HasExit(GridWithExitAt((0, 12))));
    }

    [Fact]
    public void StaggeredExitsResolveToTheRightmostColumnsRow() {
        // The scan is column-major and each column with an exit overwrites the answer, so it is the
        // last column that wins — not the first found and not the nearest.
        CombatGrid grid = GridWithExitAt((1, 4), (6, 10));

        Assert.Equal(10, TrapPuzzleGoal.ExitRow(grid));
    }

    [Fact]
    public void ExitsOnOneRowAreUnaffectedByThatQuirk() {
        // Which is why the shipped puzzles never notice it.
        CombatGrid grid = GridWithExitAt((0, 11), (3, 11), (7, 11));

        Assert.Equal(11, TrapPuzzleGoal.ExitRow(grid));
    }

    [Fact]
    public void ANullGridIsNotAnError() {
        Assert.False(TrapPuzzleGoal.HasExit(null));
        Assert.Equal(0, TrapPuzzleGoal.ExitRow(null));
        Assert.False(TrapPuzzleGoal.PartyIsOut(null, null));
    }

    [Fact]
    public void AnExitTileIsWhatMarksAGridAsAPuzzleAtAll() {
        // The same predicate answers "is this a puzzle" and "does the goal test mean anything".
        Assert.True(TrapPuzzleGoal.IsTrapPuzzle(GridWithExitAt((2, 8))));
        Assert.False(TrapPuzzleGoal.IsTrapPuzzle(new CombatGrid()));
    }
}
