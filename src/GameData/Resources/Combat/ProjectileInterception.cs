namespace GameData.Resources.Combat;

using System;
using System.Collections.Generic;

/// <summary>
/// What a projectile meets on its way across a trap grid: crystal ground stops it.
/// </summary>
/// <remarks>
/// <b>The original runs a cell's effect as the projectile is drawn into it.</b> The flight loop
/// (<c>WORLDHIT.C:686-715</c>) renders the projectile each step, and the grid renderer calls
/// <c>combat_actor_tile_entry_effect(col, row, projectile, 1, 0)</c> for the cell it is in
/// (<c>CACTOR.C:1354-1357</c>). On crystal ground (terrain 3) that effect always ends by writing -1
/// over the projectile's shape (<c>CACTOR.C:1114-1130</c>); the loop then breaks and clears the hit
/// (<c>WORLDHIT.C:714-718</c>), so nothing behind the ground is struck. Forum p=60 (TASK-1115): a
/// trap's blaster shot crossed the run between two poles and took 60 from the party member behind it;
/// in the original the run flares and swallows the shot.
///
/// <para>Three outcomes on crystal ground, in the original's order: a trap element standing there
/// stops the shot, and a shot of shape 2 (Flamecast's, <c>CSPELL.C:410-412</c>) first collapses the
/// run it holds (<c>combatgrid_shove_until_unblocked</c>, the same collapse as
/// <see cref="TrapPuzzle.CollapseUntilIsolated"/>); bare ground lights its run for the short cine
/// (<c>combatgrid_spread_tile_fx_line</c> .. <c>combatgrid_line_effect_propagate</c>), which nets the
/// ground back to what it was.</para>
/// </remarks>
public static class ProjectileInterception {
    /// <summary>The projectile shape that collapses a run it reaches through its element.</summary>
    public const int CollapsingShape = 2;

    /// <summary>How a flight ended.</summary>
    public enum Fate {
        /// <summary>Nothing stopped it: it reaches its target.</summary>
        Reaches,

        /// <summary>Bare crystal ground: the run lit and the shot is gone.</summary>
        Zapped,

        /// <summary>A trap element on crystal ground stopped it.</summary>
        Stopped,

        /// <summary>A shape-2 shot reached an element on crystal ground and collapsed its run.</summary>
        Collapsed,
    }

    /// <summary>Where and how a flight ended.</summary>
    public readonly record struct Outcome(Fate Fate, int X, int Y, IReadOnlyList<(int X, int Y)> LitRun);

    /// <summary>
    /// Flies a shot cell by cell along <paramref name="path"/> (the cells entered, the target's last)
    /// and applies what the first crystal cell does to the grid.
    /// </summary>
    public static Outcome Fly(TrapPuzzle puzzle, IEnumerable<(int X, int Y)> path, int shape) {
        foreach ((int x, int y) in path) {
            if (!CombatGrid.InBounds(x, y) || puzzle.Grid.TerrainAt(x, y) != CombatTerrain.Crystal) {
                continue;
            }
            if (puzzle.ElementAt(x, y) != null) {
                if (shape != CollapsingShape) {
                    return new Outcome(Fate.Stopped, x, y, Array.Empty<(int, int)>());
                }
                puzzle.CollapseUntilIsolated(x, y);
                return new Outcome(Fate.Collapsed, x, y, Array.Empty<(int, int)>());
            }
            return new Outcome(Fate.Zapped, x, y, puzzle.TraceCrystalLine(x, y));
        }
        return new Outcome(Fate.Reaches, -1, -1, Array.Empty<(int, int)>());
    }

    /// <summary>
    /// The cells a straight shot enters from <paramref name="from"/> to <paramref name="to"/>, the
    /// target's included and the shooter's not. A cannon only fires along a row or a column
    /// (<see cref="CannonLine"/>), so its flight crosses exactly these cells.
    /// </summary>
    public static IEnumerable<(int X, int Y)> StraightPath((int X, int Y) from, (int X, int Y) to) {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);
        if ((dx != 0 && dy != 0 && Math.Abs(to.X - from.X) != Math.Abs(to.Y - from.Y)) || (dx == 0 && dy == 0)) {
            yield return to;
            yield break;
        }
        for (int x = from.X + dx, y = from.Y + dy; ; x += dx, y += dy) {
            yield return (x, y);
            if (x == to.X && y == to.Y) {
                yield break;
            }
        }
    }
}
