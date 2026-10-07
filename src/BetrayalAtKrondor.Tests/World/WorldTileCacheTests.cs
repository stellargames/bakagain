namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>
/// The tile cache's game-facing rules: which tile a position is in, and which tiles are resident.
/// </summary>
public class WorldTileCacheTests {
    [Fact]
    public void ATilesCoordinateIsTheTruncatedDivision() {
        Assert.Equal(0, WorldTileCache.TileOf(0));
        Assert.Equal(0, WorldTileCache.TileOf(WorldTileCache.TileWorldSize - 1));
        Assert.Equal(1, WorldTileCache.TileOf(WorldTileCache.TileWorldSize));
        Assert.Equal(3, WorldTileCache.TileOf(WorldTileCache.TileWorldSize * 3 + 250));
    }

    [Fact]
    public void TheResidentSetIsTheNineTilesAroundTheParty() {
        // Nine, which is exactly WorldTileCache.Slots — the original keeps that many because that is
        // the party's tile plus its ring, and the ring is the part worth porting.
        var resident = 0;
        for (var x = 8; x <= 12; x++) {
            for (var y = 8; y <= 12; y++) {
                if (WorldTileCache.IsResident(x, y, 10, 10)) {
                    resident++;
                }
            }
        }

        Assert.Equal(WorldTileCache.Slots, resident);
    }

    [Fact]
    public void ADiagonalNeighbourIsAsResidentAsAnOrthogonalOne() {
        // *** CHEBYSHEV, NOT EUCLIDEAN. *** The ring is a 3x3 SQUARE. Measuring it as a radius drops
        // the four corners, which shows the player an empty quadrant exactly when they walk
        // diagonally toward a boundary — the one direction that reaches a corner tile first.
        Assert.True(WorldTileCache.IsResident(11, 11, 10, 10));
        Assert.True(WorldTileCache.IsResident(9, 11, 10, 10));
        Assert.True(WorldTileCache.IsResident(10, 11, 10, 10));

        // And two away on either axis is out, corners included.
        Assert.False(WorldTileCache.IsResident(12, 10, 10, 10));
        Assert.False(WorldTileCache.IsResident(12, 12, 10, 10));
    }

    [Fact]
    public void ResidencyIsDecidedInTHESAMESpaceAsTheTileNames() {
        // The chunk name Tzzxxyy carries cx/cy, and HotspotService looks its triggers up by
        // Floor(worldX, 64000) against those same numbers. So TileOf(position) lands in the tile
        // NAME's space and residency needs no second mapping — which is what lets the scene builder
        // register by parsed name and the runtime apply by party position.
        Assert.Equal(10, WorldTileCache.TileOf(10 * WorldTileCache.TileWorldSize));
        Assert.Equal(10, WorldTileCache.TileOf((10 * WorldTileCache.TileWorldSize) + 63999));
        Assert.Equal(11, WorldTileCache.TileOf(11 * WorldTileCache.TileWorldSize));
    }

}
