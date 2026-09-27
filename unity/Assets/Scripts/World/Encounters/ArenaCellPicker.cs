namespace BakAgain.World.Encounters {
    /// <summary>
    /// Turns a point on the arena floor into the grid cell under it.
    /// </summary>
    /// <remarks>
    /// <b>The exact inverse of how an actor is placed</b>, and written to be read against it.
    /// <c>HotspotService.PlaceCombatants</c> does:
    /// <code>
    /// (across, away) = CombatArenaPlacement.CellOffset(c.X, c.Y, cellSize);
    /// (dx, dy)       = ProximityMath.Rotate(across, away, session.Rotation);
    /// world          = (session.PositionX + dx, session.PositionY + dy);
    /// </code>
    /// so this subtracts the party, un-rotates by the <b>negated</b> heading, and asks
    /// <c>CombatArenaPlacement.CellAt</c>. Rotating by <c>+Rotation</c> here instead is the obvious
    /// mistake and is not obviously wrong on screen: it mirrors the arena through the party, so
    /// clicks still land on plausible cells and only the far half of the grid is unreachable.
    ///
    /// <para><b>The round trip is not bit-exact.</b> <c>Rotate</c> works in Q14 fixed point and its
    /// <c>&gt;&gt; 14</c> floors, so a cell centre taken out and brought back can move by a unit or
    /// two. That is far inside a 300-unit cell, but it means a click sitting exactly on a seam may
    /// resolve either side of it — which is what a seam is for, not a defect.</para>
    ///
    /// <para><b>WE FLOOR WHERE THE ORIGINAL TRUNCATES, AND THAT IS A DECISION RATHER THAN A
    /// MATCH.</b> <c>arena_worldPointToCell</c> @0x2da21 divides with <c>idiv</c>, which truncates
    /// toward zero. Inside the arena the shifted numerator is non-negative and the two agree
    /// exactly; they differ only OUTSIDE, on the near/left side, where a small negative numerator
    /// truncates to column 0 there and floors to -1 here. So the original accepts a click just off
    /// the left edge as column 0 and we refuse it.
    ///
    /// <para>Kept as it is: refusing an off-grid click is the tidier behaviour and nothing depends
    /// on the forgiving version. Recorded because the paragraph above argues for flooring from the
    /// centre line, and the <c>+1200</c> shift means the sign only goes negative outside the grid —
    /// so that argument does not actually reach this case, and someone re-deriving it should know
    /// the binary disagrees.</para></para>
    /// </remarks>
    public static class ArenaCellPicker {
        /// <summary>
        /// The cell under a floor point, or null when it is off the grid.
        /// </summary>
        /// <param name="pointX">Click point in BaK world units.</param>
        /// <param name="pointY">Likewise.</param>
        /// <param name="partyX">The party's world position — the arena is laid out around it.</param>
        /// <param name="partyY">Likewise.</param>
        /// <param name="rotation">The party's heading, in BaK angle space.</param>
        /// <param name="cellSize"><c>StartData.CombatGridCellSize</c>.</param>
        public static (int Column, int Row)? CellAt(
            int pointX, int pointY, int partyX, int partyY, int rotation, int cellSize) {
            (int across, int away) = Collision.ProximityMath.Rotate(
                pointX - partyX, pointY - partyY, -rotation);
            return GameData.Resources.Combat.CombatArenaPlacement.CellAt(across, away, cellSize);
        }
    }
}
