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

    /// <summary>A spell's flight speed in world units a step (<c>cspell_apply_hit_at</c>, CSPELL.C:427).</summary>
    public const int SpellSpeed = 0xfa;

    /// <summary>A weapon shot's flight speed (CBTAITRN.C:139); a quarrel of kind 8 flies at 0x5a.</summary>
    public const int ShotSpeed = 300;

    /// <summary>The shape of an ordinary weapon shot (CBTAITRN.C:128): it never collapses a run.</summary>
    public const int ShotShape = 1;

    /// <summary>
    /// Flies a shot cell by cell along <paramref name="path"/> (the cells entered, the target's last)
    /// and applies what the first crystal cell does to the grid.
    /// </summary>
    public static Outcome Fly(TrapPuzzle puzzle, IEnumerable<(int X, int Y)> path, int shape) =>
        Fly(puzzle.Grid, puzzle, path, shape);

    /// <summary>
    /// The same flight on any grid; <paramref name="puzzle"/> is null outside a trap room.
    /// </summary>
    public static Outcome Fly(CombatGrid grid, TrapPuzzle? puzzle, IEnumerable<(int X, int Y)> path, int shape) {
        foreach ((int x, int y) in path) {
            if (Enter(grid, puzzle, x, y, shape) is { } stopped) {
                return stopped;
            }
        }
        return new Outcome(Fate.Reaches, -1, -1, Array.Empty<(int, int)>());
    }

    /// <summary>
    /// What one cell does to a projectile drawn into it (<c>combat_actor_tile_entry_effect</c> with
    /// <c>do_apply = 1</c>, CACTOR.C:1110-1158), or null when it flies on.
    /// </summary>
    /// <remarks>
    /// Crystal ground is handled as above. A diamond of element 9 or a wrecked pole (0x28) stops it
    /// (terrain 5 and 14); the see-through diamond, 10, does not. Out-of-bounds ground (terrain 2)
    /// stops it, in any fight. Terrain 7 (the reflecting wall) is <c>ReflectorOnPathTo</c>'s.
    /// </remarks>
    public static Outcome? Enter(CombatGrid grid, TrapPuzzle? puzzle, int x, int y, int shape) {
        if (!CombatGrid.InBounds(x, y)) {
            return null;
        }
        switch (grid.TerrainAt(x, y)) {
            case CombatTerrain.Crystal:
                if (puzzle?.ElementAt(x, y) != null) {
                    if (shape != CollapsingShape) {
                        return new Outcome(Fate.Stopped, x, y, Array.Empty<(int, int)>());
                    }
                    puzzle.CollapseUntilIsolated(x, y);
                    return new Outcome(Fate.Collapsed, x, y, Array.Empty<(int, int)>());
                }
                return new Outcome(Fate.Zapped, x, y,
                    puzzle?.TraceCrystalLine(x, y) ?? (IReadOnlyList<(int, int)>)new[] { (x, y) });
            case CombatTerrain.Pushable:
            case (CombatTerrain)CrystalChain.WreckTerrain:
                // ponytail: the original also lets the shot pass when a combatant stands on the tile
                // (CACTOR.C:1149); an element's tile never holds one here.
                int id = puzzle?.ElementAt(x, y)?.ElementId ?? 0;
                return id == 9 || id == CrystalChain.WreckElementId
                    ? new Outcome(Fate.Stopped, x, y, Array.Empty<(int, int)>())
                    : null;
            case CombatTerrain.OutOfBounds:
                return new Outcome(Fate.Stopped, x, y, Array.Empty<(int, int)>());
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether a cell ends the line-of-fire trace (<c>combat_actor_trace_proj_path</c>, CACTOR.C:1161),
    /// which runs the same cell effect with <c>do_apply = 0</c>: the current's arm returns at once, so
    /// only an opaque diamond, a wreck and out-of-bounds ground block the line.
    /// </summary>
    /// <remarks>
    /// ponytail: terrain 7 (the reflecting wall) blocks the trace only when its caller passes flag 0;
    /// it is left out here. Add it with the flag if a Mirrorwall's effect on targeting matters.
    /// </remarks>
    public static bool BlocksSight(CombatGrid grid, TrapPuzzle? puzzle, int x, int y) =>
        CombatGrid.InBounds(x, y) && grid.TerrainAt(x, y) != CombatTerrain.Crystal
        && Enter(grid, puzzle, x, y, ShotShape) != null;

    /// <summary>
    /// The cells a projectile that HITS is drawn in, from the shooter's cell to the target's
    /// (<c>world_rndr_ranged_attack_anim</c>, WORLDHIT.C:633-699).
    /// </summary>
    /// <remarks>
    /// Both ends are cell centres; the heading is shooter to target; each step moves
    /// <paramref name="speed"/> units and the flight lasts the octagonal distance over the speed. The
    /// shooter's own cell is left out.
    /// ponytail: float trig stands in for the original's sine and arctangent tables, so a line that
    /// clips a cell's corner by a few units can be read differently; use the tables if one matters.
    /// </remarks>
    public static IEnumerable<(int X, int Y)> FlightCells(int fromX, int fromY, int toX, int toY,
        int speed, int cellSize) {
        if (speed <= 0 || cellSize <= 0) {
            yield break;
        }
        long dx = (long)(toX - fromX) * cellSize;
        long dy = (long)(toY - fromY) * cellSize;
        int steps = World.WorldDistance.Octagonal((int)dx, (int)dy) / speed;
        double heading = Math.Atan2(dy, dx);
        double x = fromX * cellSize + cellSize / 2.0;
        double y = fromY * cellSize + cellSize / 2.0;
        (int X, int Y) last = (fromX, fromY);
        for (var i = 0; i < steps; i++) {
            x += speed * Math.Cos(heading);
            y += speed * Math.Sin(heading);
            (int X, int Y) cell = ((int)Math.Floor(x / cellSize), (int)Math.Floor(y / cellSize));
            if (cell != last) {
                last = cell;
                yield return cell;
            }
        }
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
