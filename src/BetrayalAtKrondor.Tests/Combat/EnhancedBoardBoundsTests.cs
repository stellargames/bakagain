namespace BetrayalAtKrondor.Tests.Combat;

using System.Linq;
using GameData.Resources.Combat;
using Xunit;

/// <summary>Where the Enhanced board ends, and which cells a trap's goal tint covers.</summary>
public class EnhancedBoardBoundsTests {
    [Fact]
    public void NoCellIsOffTheBoard() => Assert.True(EnhancedBoardBounds.IsOffBoard(null, false));

    [Fact]
    public void UndergroundRowSevenIsOffTheBoard_AboveGroundItIsNot() {
        Assert.True(EnhancedBoardBounds.IsOffBoard((3, 7), underground: true));
        Assert.False(EnhancedBoardBounds.IsOffBoard((3, 7), underground: false));
        Assert.False(EnhancedBoardBounds.IsOffBoard((3, 6), underground: true));
    }

    [Fact]
    public void AGridWithoutAnExitHasNoGoalCells() =>
        Assert.Empty(EnhancedBoardBounds.GoalCells(new CombatGrid(underground: false), false));

    [Fact]
    public void GoalCellsAreTheStandableCellsOnTheExitRowOnly() {
        // A move changes the row by at most 1, so a hero reaching a later row has crossed the exit row and
        // the trap has ended there (TrapPuzzleGoal.PartyIsOut, row >= exitRow): only that row is the target.
        var grid = new CombatGrid(underground: false);
        grid.SetTerrain(0, 6, CombatTerrain.Exit);
        grid.SetTerrain(4, 6, CombatTerrain.Wall);
        grid.SetTerrain(5, 6, CombatTerrain.Crystal);
        grid.SetTerrain(6, 6, CombatTerrain.Pushable);
        grid.SetTerrain(2, 7, CombatTerrain.Trap);

        var cells = EnhancedBoardBounds.GoalCells(grid, false);

        Assert.Equal(new[] { (0, 6), (1, 6), (2, 6), (3, 6), (7, 6) }, cells);
        Assert.DoesNotContain((4, 6), cells);
        Assert.DoesNotContain((5, 6), cells);
        Assert.DoesNotContain((6, 6), cells);
        Assert.DoesNotContain((2, 7), cells);
        Assert.All(cells, c => Assert.Equal(6, c.Row));
    }

    [Fact]
    public void UndergroundGoalCellsAreTheExitRowsCells() {
        var grid = new CombatGrid(underground: true);
        grid.SetTerrain(0, 5, CombatTerrain.Exit);

        var cells = EnhancedBoardBounds.GoalCells(grid, true);

        Assert.Equal(CombatGrid.Width, cells.Count);
        Assert.All(cells, c => Assert.Equal(5, c.Row));
        Assert.Contains((0, 5), cells);
        Assert.DoesNotContain((1, 6), cells);
    }
}
