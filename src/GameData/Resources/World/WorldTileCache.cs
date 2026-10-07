namespace GameData.Resources.World;

/// <summary>
/// The nine-slot world tile cache — <c>InitWorldTileSystem</c> (0x72a00),
/// <c>CheckAndLoadNewTile</c> (0x72b02) and <c>FindLoadedTileByCoords</c> (0x72ec7), ovr185.
/// </summary>
/// <remarks>
/// <b>The function names are misleading and the shape is not what they suggest.</b>
/// <c>CheckAndLoadNewTile</c> loads nothing: it works out which tile the camera is standing in, and
/// if that tile is already in the cache it SWAPS it into slot 0. If it is not cached the function
/// returns and does nothing at all. The loading happens in <c>UpdateWorldItemsForTile</c>, which
/// despite its name is the routine that calls <c>LoadTzzxxyy.WLD</c> and keeps the ring around the
/// current tile populated.
///
/// <para><b>Slot 0 is the tile the party is standing in.</b> The crossing test compares against
/// slot 0 only, and the lookup searches slots 1..8. The port keeps none of the slots — only which
/// tiles are resident (<see cref="IsResident"/>).</para>
/// </remarks>
public static class WorldTileCache {
    /// <summary>Slots in the cache — one current tile and its eight neighbours.</summary>
    public const int Slots = 9;

    /// <summary>World units per tile, the divisor that turns a position into a tile coordinate.</summary>
    public const int TileWorldSize = 64000;

    /// <summary>The tile coordinate a world position falls in.</summary>
    /// <remarks>
    /// <b>Truncating integer division, and then narrowed to a BYTE.</b> The original divides the
    /// 32-bit position and keeps <c>al</c>, so a coordinate outside 0..255 wraps rather than
    /// erroring. Modelled as the division only; the narrowing belongs to whatever stores it.
    /// </remarks>
    public static int TileOf(long worldPosition) => (int)(worldPosition / TileWorldSize);

    /// <summary>
    /// How far from the party's own tile a tile can be and still be resident.
    /// </summary>
    /// <remarks>
    /// One, in Chebyshev distance — the party's tile plus the eight around it, which is the
    /// <see cref="Slots"/> the original keeps and the reason there are nine of them rather than
    /// some other number.
    /// </remarks>
    public const int ResidentRadius = 1;

    /// <summary>
    /// Whether a tile is one of the nine the party's position keeps resident.
    /// </summary>
    /// <remarks>
    /// <b>This is the part of the original's design worth keeping, and the nine SLOTS are not.</b>
    /// The storage — nine fixed partitions of one 62216-byte allocation — is a 16-bit memory
    /// budget; WHICH tiles matter is a game-facing rule, because it is what the original keeps
    /// populated ahead of the party and therefore what it is ever willing to draw.
    ///
    /// <para>Chebyshev rather than Euclidean: the ring is a 3x3 square, so a diagonal neighbour is
    /// as resident as an orthogonal one. Measuring it as a radius would drop the four corners and
    /// show the player an empty quadrant when they walk diagonally toward a boundary.</para>
    /// </remarks>
    public static bool IsResident(int tileX, int tileY, int partyTileX, int partyTileY) {
        int dx = tileX - partyTileX;
        int dy = tileY - partyTileY;
        if (dx < 0) {
            dx = -dx;
        }
        if (dy < 0) {
            dy = -dy;
        }
        return (dx > dy ? dx : dy) <= ResidentRadius;
    }

    /// <summary>
    /// The first of the twenty global keys cleared when the party crosses into another tile.
    /// </summary>
    /// <remarks>
    /// <b>Twenty keys, which is BOTH transient hotspot blocks</b> — the scout-tried flags at 5200
    /// and the spotted flags at 5210. Clearing only the first leaves a spot earned on one tile
    /// buying a sneak-past on the next; see <c>HotspotService</c>, which clears both for this reason.
    /// </remarks>
    public const int FirstClearedGlobal = 5200;

    /// <summary>Slots in each of the two transient blocks.</summary>
    /// <remarks>
    /// Ten, so the twenty cleared keys are two ten-slot blocks and not one twenty-slot one — which
    /// is the whole reason <see cref="ScoutTriedFlagKey"/> and <see cref="ScoutedFlagKey"/> are ten
    /// apart and mean different things.
    /// </remarks>
    public const int SlotsPerTransientBlock = 10;

    /// <summary>The "a scout roll was tried for this hotspot" flag.</summary>
    public static int ScoutTriedFlagKey(int hotspotIndex) => FirstClearedGlobal + hotspotIndex;

    /// <summary>The "this hotspot has been spotted" flag.</summary>
    /// <remarks>
    /// The second block, <see cref="SlotsPerTransientBlock"/> above the first. Both are cleared on a
    /// crossing; clearing only the first lets a spot earned on one tile buy a sneak-past on the next.
    /// </remarks>
    public static int ScoutedFlagKey(int hotspotIndex) =>
        FirstClearedGlobal + SlotsPerTransientBlock + hotspotIndex;
}
