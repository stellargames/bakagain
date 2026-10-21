namespace GameData.Resources.Scene;

using System;
using System.Collections.Generic;
using GameData.Resources.World;

/// <summary>
/// Where a town or background trigger puts the party before its scene opens —
/// <c>approachWalkToScenePosition</c> (ovr180 @0x6dc6a).
///
/// <para>Every shipped <c>DEF_TOWN</c> record asks for this, so it is the normal way a location is
/// entered rather than a special case.</para>
/// </summary>
public static class TownApproach {
    /// <summary>The sub-tile cell east of the tile's edge, from the record's packed offset.</summary>
    /// <remarks>
    /// <b>The offset is two bytes in one field</b> — X low, Y high — like several other fields in
    /// this data. Reading it as a single number sends the party to a cell that does not exist.
    /// </remarks>
    public static int SubTileX(int approachTileOffset) => approachTileOffset & 0xFF;

    /// <inheritdoc cref="SubTileX"/>
    public static int SubTileY(int approachTileOffset) => (approachTileOffset >> 8) & 0xFF;

    /// <summary>
    /// Where the approach walk STARTS, and where the party is left when the location closes.
    /// </summary>
    /// <param name="currentTileX">The tile the party is standing on when the trigger fires.</param>
    /// <param name="currentTileY">As <paramref name="currentTileX"/>.</param>
    /// <param name="approachTileOffset">The record's packed sub-tile offset.</param>
    /// <remarks>
    /// <b>The destination is relative to the party, not absolute.</b> The routine reads the party's
    /// current tile and combines it with the record's sub-tile offsets, so one record serves every
    /// tile its trigger covers — a town gate spanning several tiles walks you to the same spot
    /// <i>within whichever tile you stepped on</i>. Treating the offset as a world position would
    /// send the party to the corner of the map.
    ///
    /// <para>Centred within the sub-cell, via <see cref="WorldPlacement.CentreOf"/> — the same
    /// placement rule as a chapter start or a teleport arrival.</para>
    /// </remarks>
    public static (long X, long Y) DestinationOf(int currentTileX, int currentTileY, int approachTileOffset) => (
        WorldPlacement.CentreOf(currentTileX, SubTileX(approachTileOffset)),
        WorldPlacement.CentreOf(currentTileY, SubTileY(approachTileOffset)));

    /// <summary>
    /// <b>The party turns to face the destination heading before it starts moving.</b>
    /// </summary>
    /// <remarks>
    /// The routine writes the record's heading straight into the camera rotation and only then runs
    /// the travel loop, so the approach is a walk in a fixed direction rather than a turn-as-you-go.
    /// It is NOT what the party is left facing — see <see cref="FacingAfter"/>.
    ///
    /// <para><b>Deliberately callerless.</b> A pinned fact about the approach's order; the remark above says what it decides.</para>
    /// </remarks>
    public static bool HeadingIsSetBeforeWalking => true;

    /// <summary>
    /// <b>The party is left facing AWAY from the door</b> — the record's heading plus 180°.
    /// </summary>
    /// <remarks>
    /// <c>approachWalkToScenePosition</c> @0x6ddc2 puts the camera back at the destination and
    /// writes <c>heading + 0x8000</c> into its yaw before the scene runs (canassa MAP.C:556), so
    /// leaving the location the party looks back the way it came. Measured in the original
    /// (2026-10-02): LaMut's <c>def_town:0</c>, heading 0x4000 (west), leaves it facing east, 0xC000.
    /// </remarks>
    public static short FacingAfter(int heading) => unchecked((short)(heading + 0x8000));

    /// <summary>
    /// <b>Underground, the approach walks in half-size steps.</b>
    /// </summary>
    /// <remarks>
    /// The step is halved when the zone is an underground one, so the same record takes twice as many
    /// steps below ground. It changes only the pacing of the walk, not where it ends.
    /// </remarks>
    public static int StepFor(int step, bool underground) => underground ? step >> 1 : step;

    /// <summary>
    /// The cells the approach walk passes, from the record's cell to where the road ends.
    /// </summary>
    /// <remarks>
    /// <c>map_animate_camera_to_tile</c> (MAP.C:503-561) puts the camera on the record's cell with
    /// the record's heading, then loops <c>worldmove_crossing_check_8dir</c> — the follow-road step
    /// (WORLDMOV.C:357-424) — until it refuses, turning at each cell centre the way
    /// <c>worldmove_sweep_adjacent_cells</c> says. So the walk is road travel, and it is built from
    /// <see cref="RoadTravel"/>'s own rules. Every step from a centre lands on the next centre, so the
    /// cell list is the path at any step size; the step only sets the pace (<see cref="SecondsFor"/>).
    /// <para>Each point carries the heading the walk LEAVES it on. A fork keeps the heading (the
    /// sweep returns 0), so the walk stops there unless the road also runs straight on.</para>
    /// </remarks>
    public static List<(int X, int Y, ushort Heading)> WalkPath(int x, int y, ushort heading,
        Func<int, int, bool> isRoadAt) {
        var path = new List<(int X, int Y, ushort Heading)> { (x, y, heading) };
        // ponytail: the original has no limit and would spin on a road that loops; no shipped
        // approach does, so this only keeps a modded one from hanging the game.
        for (int i = 0; i < MaxWalkCells; i++) {
            if (!RoadTravel.IsCompassHeading(heading) || !RoadTravel.IsOnLatticeLine(heading, x, y)) {
                break;
            }
            (int px, int py) = RoadTravel.ProbeOrigin(x, y, heading);
            if (!RoadTravel.ProbeAdjacentCell(px, py, heading, isRoadAt)) {
                break;
            }
            (int dx, int dy) = RoadTravel.AxisOffset(heading, RoadTravel.CellSize);
            x += dx;
            y += dy;
            if (RoadTravel.FindContinuation(x, y, heading, false, isRoadAt, out ushort turn) == RoadSweep.Turn) {
                heading = turn;
            }
            path.Add((x, y, heading));
        }
        return path;
    }

    private const int MaxWalkCells = 1000;

    /// <summary>
    /// Seconds one world step of the walk takes on screen.
    /// </summary>
    /// <remarks>
    /// The original renders one frame per step and has no clock in the loop, so its pace is the
    /// machine's. This is the owner's DOSBox reading (2026-10-21): LaMut's 24-cell approach at the
    /// Large step took about 5.5 s.
    /// </remarks>
    public const double SecondsPerStep = 0.23;

    /// <summary>How long the walk lasts: one <see cref="SecondsPerStep"/> per world step.</summary>
    /// <param name="step">The world step, already through <see cref="StepFor"/>.</param>
    public static double SecondsFor(List<(int X, int Y, ushort Heading)> path, int step) =>
        step <= 0 ? 0 : (path.Count - 1) * ((double)RoadTravel.CellSize / step) * SecondsPerStep;

    /// <summary>The walk's position and heading after <paramref name="cells"/> cells of it.</summary>
    public static (int X, int Y, ushort Heading) PoseAt(List<(int X, int Y, ushort Heading)> path, double cells) {
        int last = path.Count - 1;
        if (cells >= last) {
            return path[last];
        }
        int i = Math.Max(0, (int)Math.Floor(cells));
        double t = cells - i;
        (int ax, int ay, ushort heading) = path[i];
        (int bx, int by, _) = path[i + 1];
        return ((int)Math.Round(ax + ((bx - ax) * t)), (int)Math.Round(ay + ((by - ay) * t)), heading);
    }
}
