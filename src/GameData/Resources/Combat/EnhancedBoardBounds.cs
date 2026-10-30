namespace GameData.Resources.Combat;

using System.Collections.Generic;

/// <summary>
/// Where the Enhanced board ends and which cells a trap's goal tint covers. Engine-free: the Unity
/// side only turns these cells into geometry.
/// </summary>
public static class EnhancedBoardBounds {
    /// <summary>Whether a tapped cell is off the board: no cell under the pointer, or a cell the fight
    /// does not use (underground rows from <see cref="CombatGrid.UndergroundPlayableRows"/> up).</summary>
    public static bool IsOffBoard((int Column, int Row)? cell, bool underground) =>
        cell is not { } c || !CombatArenaPlacement.IsPlayable(c.Column, c.Row, underground);

    /// <summary>The cells the party is trying to reach: in a trap, every playable cell on or past the
    /// exit row that a hero can stand on. Empty outside a trap.</summary>
    public static List<(int Column, int Row)> GoalCells(CombatGrid grid, bool underground) {
        var cells = new List<(int Column, int Row)>();
        if (!TrapPuzzleGoal.IsTrapPuzzle(grid)) {
            return cells;
        }
        for (int row = TrapPuzzleGoal.ExitRow(grid); row < CombatGrid.Height; row++) {
            for (var column = 0; column < CombatGrid.Width; column++) {
                if (CombatArenaPlacement.IsPlayable(column, row, underground)
                    && grid.TerrainAt(column, row) is CombatTerrain.Open or CombatTerrain.Exit or CombatTerrain.Trap) {
                    cells.Add((column, row));
                }
            }
        }
        return cells;
    }
}
