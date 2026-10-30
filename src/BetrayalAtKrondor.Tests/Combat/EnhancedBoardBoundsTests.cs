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
    public void GoalCellsAreTheStandableCellsOnAndPastTheExitRow() {
        var grid = new CombatGrid(underground: false);
        grid.SetTerrain(0, 6, CombatTerrain.Exit);
        grid.SetTerrain(4, 6, CombatTerrain.Wall);
        grid.SetTerrain(5, 6, CombatTerrain.Crystal);
        grid.SetTerrain(6, 6, CombatTerrain.Pushable);
        grid.SetTerrain(2, 7, CombatTerrain.Trap);

        var cells = EnhancedBoardBounds.GoalCells(grid, false);

        Assert.All(new[] { (0, 6), (1, 6), (2, 6), (3, 6), (7, 6), (2, 7) }, c => Assert.Contains(c, cells));
        Assert.DoesNotContain((4, 6), cells);
        Assert.DoesNotContain((5, 6), cells);
        Assert.DoesNotContain((6, 6), cells);
        Assert.DoesNotContain(cells, c => c.Row < 6);
    }

    [Fact]
    public void UndergroundGoalCellsStopAtTheLastPlayableRow() {
        var grid = new CombatGrid(underground: true);
        grid.SetTerrain(0, 5, CombatTerrain.Exit);

        var cells = EnhancedBoardBounds.GoalCells(grid, true);

        Assert.Contains((1, 6), cells);
        Assert.NotEmpty(cells);
        Assert.DoesNotContain(cells, c => c.Row >= CombatGrid.UndergroundPlayableRows);
    }
}
