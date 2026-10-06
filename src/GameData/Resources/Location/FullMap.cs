namespace GameData.Resources.Location;

using System.Collections.Generic;
using GameData.Resources.Data;
using GameData.Resources.World;

/// <summary>
/// FMAP_TWN.DAT — the town labels drawn on the world map screen (FULLMAP.SCX).
/// Each town has a name and a position, reversed from <c>Load_fmap_twn.dat</c>
/// (ovr183 @ 0x713cd). On disk the coordinates are 320×200 mode-13h pixels; the
/// extractor scales them into the <b>canonical 1600×1200 space</b> (×5 / ×6, via
/// <c>AspectCorrection</c>) so they line up with the FULLMAP.SCX background and
/// the REQ/GDS/Label coordinate space.
///
/// The four header words are shared icon/label geometry constants (the shipped
/// file has 3,3,9,9). They are consumed by:
///   * <c>UI_full_map</c> (0x70f60) — the label of town i is centred at
///     <c>X + IconAnchorX/2</c> horizontally (minus half the text width) and
///     drawn one label-height above <c>Y</c>.
///   * <c>sub_ovr183_652</c> (0x715b2) — the mouse hit-test builds the icon
///     rectangle <c>[X-(IconWidth-IconAnchorX)/2 .. +IconWidth]</c> ×
///     <c>[Y-(IconHeight-IconAnchorY)/2 .. +IconHeight]</c> and returns the
///     town under the cursor.
/// So (IconWidth, IconHeight) is the FMAP_ICN icon footprint and
/// (IconAnchorX, IconAnchorY) the centring anchor.
/// </summary>
public class FullMapTowns : IResource {
    public FullMapTowns(string id) {
        Id = id;
    }

    public string Id { get; }
    public ResourceType Type => ResourceType.DAT;

    // Icon/label geometry, scaled into canonical 1600×1200 space.
    public int IconAnchorX { get; set; }
    public int IconAnchorY { get; set; }
    public int IconWidth { get; set; }
    public int IconHeight { get; set; }

    public List<FullMapTown> Towns { get; set; } = new();

    /// <summary>
    /// The town whose icon is under the canonical point, or -1 — <c>fmap_hit_test_cursor</c>
    /// (SCREENS/FMAP.C:285): a box from <c>X - (IconWidth - IconAnchorX) / 2</c>, IconWidth wide and
    /// IconHeight tall, inclusive at both ends; the first town in file order wins.
    /// </summary>
    public int TownAt(int x, int y) {
        int halfW = (IconWidth - IconAnchorX) >> 1;
        int halfH = (IconHeight - IconAnchorY) >> 1;
        for (int i = 0; i < Towns.Count; i++) {
            int x0 = Towns[i].X - halfW;
            int y0 = Towns[i].Y - halfH;
            if (x >= x0 && x <= x0 + IconWidth && y >= y0 && y <= y0 + IconHeight) {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Where a town's name is drawn (<c>fmap_screen_run</c>, SCREENS/FMAP.C): centred on
    /// <c>X + IconAnchorX / 2</c>, its top one label height above the town's Y. The label height is
    /// the game font's cell plus one row (<c>fmap_twn_load</c>), which the caller knows in its own
    /// units.
    /// </summary>
    public (int CentreX, int Top) LabelPlacement(int town, int labelHeight) =>
        (Towns[town].X + IconAnchorX / 2, Towns[town].Y - labelHeight);
}

public class FullMapTown {
    public string Name { get; set; } = string.Empty;

    /// <summary>Town label X in canonical 1600×1200 space.</summary>
    public int X { get; set; }

    /// <summary>Town label Y in canonical 1600×1200 space.</summary>
    public int Y { get; set; }
}

/// <summary>
/// FMAP_XY.DAT — maps the player's (zone, world-tile) to a "you are here"
/// marker position on the world map screen, reversed from
/// <c>Load_fmap_xy.dat</c> (ovr183 @ 0x71634).
///
/// The file is exactly 12 zones (zone numbers 1..12), each a count followed by
/// that many (x, y) marker positions — one per world tile in the zone. On disk
/// these are 320×200 mode-13h pixels; the extractor scales them into the
/// <b>canonical 1600×1200 space</b> (×5 / ×6). The loader looks up
/// <c>Zones[currentZone-1].Markers[currentTile]</c>; the on-disk (-1,-1) "no
/// marker" sentinel is surfaced as a <c>null</c> list entry rather than a magic
/// coordinate, so consumers needn't know about it.
/// </summary>
public class FullMapPositions : IResource {
    public const int ZoneCount = 12;

    public FullMapPositions(string id) {
        Id = id;
    }

    public string Id { get; }
    public ResourceType Type => ResourceType.DAT;

    /// <summary>Indexed [0..11] for zone numbers 1..12.</summary>
    public List<FullMapZone> Zones { get; set; } = new();

    /// <summary>
    /// The still frame of a marker's four-frame animation that a static marker shows — the same 2
    /// the load path adds to a save header's icon.
    /// </summary>
    public const int StillFrame = 2;

    private const float CanonicalWidth = 1600f;
    private const float CanonicalHeight = 1200f;
    private const int TileWorldSize = World.WorldTileCache.TileWorldSize;

    /// <summary>
    /// The party marker for a party at (worldX, worldY) in <paramref name="zone"/> facing
    /// <paramref name="yaw"/>: the entry for the party's tile, numbered in Z##REF.DAT order
    /// (<c>fmap_xy_lookup_for_chapter</c>, FMAP.C:309-343). Hidden when the zone is not 1..12, the
    /// tile is not in the zone's ref list or past its FMAP_XY list, or the entry is a no-marker tile.
    /// </summary>
    public FullMapIcon MarkerFor(int zone, ZoneRef zoneRef, long worldX, long worldY, int yaw) {
        int tileX = (int)(worldX / TileWorldSize), tileY = (int)(worldY / TileWorldSize);
        int tile = zoneRef.Tiles.FindIndex(t => t.X == tileX && t.Y == tileY);
        MapMarker? marker = zone >= 1 && zone <= Zones.Count && tile >= 0 && tile < Zones[zone - 1].Markers.Count
            ? Zones[zone - 1].Markers[tile]
            : null;
        return marker == null
            ? new FullMapIcon(false, 0f, 0f, 0)
            : new FullMapIcon(true, marker.X / CanonicalWidth * 100f, marker.Y / CanonicalHeight * 100f,
                IconBaseFor(yaw) + StillFrame);
    }

    /// <summary>
    /// The first of the marker's four frames for a heading: the yaw rounded to the nearest eighth
    /// of a turn, a half rounding up (<c>fmap_farptr_normalize</c>), times four (FMAP.C:89-94).
    /// </summary>
    public static int IconBaseFor(int yaw) => ((((yaw & 0xFFFF) + 0x1000) >> 13) & 7) << 2;
}

public class FullMapZone {
    /// <summary>
    /// One entry per world tile in this zone, indexed by tile number. Each entry
    /// is the marker position in canonical 1600×1200 space, or <c>null</c> when
    /// the tile has no map marker (the on-disk (-1,-1) sentinel).
    /// </summary>
    public List<MapMarker?> Markers { get; set; } = new();
}

public class MapMarker {
    public int X { get; set; }
    public int Y { get; set; }
}
