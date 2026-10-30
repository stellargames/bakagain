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

    /// <summary>The cells the party is trying to reach: in a trap, the playable cells ON the exit row that a
    /// hero can stand on. Empty outside a trap.</summary>
    /// <remarks>Never the rows past it: a move changes the row by at most 1, so a hero bound for a later row
    /// crosses the exit row first, and the trap ends there (<c>TrapPuzzleGoal.PartyIsOut</c>,
    /// row &gt;= exitRow).</remarks>
    public static List<(int Column, int Row)> GoalCells(CombatGrid grid, bool underground) {
        var cells = new List<(int Column, int Row)>();
        if (!TrapPuzzleGoal.IsTrapPuzzle(grid)) {
            return cells;
        }
        int row = TrapPuzzleGoal.ExitRow(grid);
        for (var column = 0; column < CombatGrid.Width; column++) {
            if (CombatArenaPlacement.IsPlayable(column, row, underground)
                && grid.TerrainAt(column, row) is CombatTerrain.Open or CombatTerrain.Exit or CombatTerrain.Trap) {
                cells.Add((column, row));
            }
        }
        return cells;
    }
}
