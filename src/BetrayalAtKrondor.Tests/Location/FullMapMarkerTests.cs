namespace BetrayalAtKrondor.Tests.Location;

using GameData.Resources.Data;
using GameData.Resources.Location;
using GameData.Resources.World;
using ResourceExtraction.Extractors;
using Xunit;

/// <summary>
/// The full map's party marker comes from FMAP_XY.DAT at the party's tile, with the icon from the
/// heading — <c>fmap_xy_lookup_for_chapter</c> and <c>fmap_screen_run</c> (canassa SCREENS/FMAP.C),
/// TASK-794.
/// </summary>
public class FullMapMarkerTests {
    private const int Tile = 64000;

    // Zone 1 has three tiles; tile 1 has no marker. Zone 2 has one.
    private static FullMapPositions Positions() {
        var p = new FullMapPositions("FMAP_XY.DAT");
        p.Zones.Add(new FullMapZone { Markers = { new MapMarker { X = 400, Y = 600 }, null, new MapMarker { X = 800, Y = 300 } } });
        p.Zones.Add(new FullMapZone { Markers = { new MapMarker { X = 1200, Y = 900 } } });
        return p;
    }

    private static ZoneRef Ref(params (byte x, byte y)[] tiles) {
        var r = new ZoneRef("Z01REF.DAT");
        foreach (var (x, y) in tiles) {
            r.Tiles.Add(new TileCoordinate { X = x, Y = y });
        }
        return r;
    }

    [Fact]
    public void TheMarkerIsTheEntryForThePartysTileInZoneRefOrder() {
        ZoneRef zoneRef = Ref((10, 20), (11, 20), (12, 20));

        FullMapIcon m = Positions().MarkerFor(1, zoneRef, 12 * Tile + 5, 20 * Tile + 7, 0);

        Assert.True(m.Visible);
        Assert.Equal(50f, m.XPercent, 3);
        Assert.Equal(25f, m.YPercent, 3);
    }

    [Fact]
    public void ANoMarkerTileAnUnknownTileOrAnUnknownZoneHidesIt() {
        ZoneRef zoneRef = Ref((10, 20), (11, 20), (12, 20));

        Assert.False(Positions().MarkerFor(1, zoneRef, 11 * Tile, 20 * Tile, 0).Visible);
        Assert.False(Positions().MarkerFor(1, zoneRef, 40 * Tile, 40 * Tile, 0).Visible);
        Assert.False(Positions().MarkerFor(13, zoneRef, 10 * Tile, 20 * Tile, 0).Visible);
        // A tile index past the zone's FMAP_XY list (refIndex >= entry_count).
        Assert.False(Positions().MarkerFor(2, zoneRef, 11 * Tile, 20 * Tile, 0).Visible);
    }

    [Theory]
    // fmap_farptr_normalize rounds the heading to the nearest eighth, a half rounding up;
    // the icon base is that eighth times four, and 360 degrees wraps to north.
    [InlineData(0x0000, 0)]
    [InlineData(0x0FFF, 0)]
    [InlineData(0x1000, 4)]
    [InlineData(0x2000, 4)]
    [InlineData(0x4000, 8)]
    [InlineData(0xF000, 0)]
    [InlineData(0xEFFF, 28)]
    public void TheIconBaseIsTheHeadingRoundedToAnEighth(int yaw, int iconBase) {
        Assert.Equal(iconBase, FullMapPositions.IconBaseFor(yaw));
    }

    [Fact]
    public void TheSaveHeaderGetsTheMarkerBackInFullMapPixels() {
        FullMapIcon shown = SaveGameExtractor.BuildFullMapMarker(160, 50, 12);

        Assert.Equal(((short)160, (short)50, (short)12), SaveGameExtractor.HeaderFieldsFor(shown));
        Assert.Equal(((short)-1, (short)-1, (short)0),
            SaveGameExtractor.HeaderFieldsFor(new FullMapIcon(false, 0f, 0f, 0)));
    }
}
