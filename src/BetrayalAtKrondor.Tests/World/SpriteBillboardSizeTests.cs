namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;

using Xunit;

/// <summary>
/// How big a world sprite is — <c>renderSprite2</c> @0x23031 with <c>RenderWorldItem</c> @0x2a95a.
/// </summary>
public class SpriteBillboardSizeTests {
    [Fact]
    public void SizeScaleIsAFractionInOneHundredTwentyEighths() {
        // 128 is "the entity's own extent"; 64 is half of it.
        Assert.Equal(1000, SpriteBMeshFace.WorldExtentFor(sizeScale: 128, entityExtent: 1000));
        Assert.Equal(500, SpriteBMeshFace.WorldExtentFor(sizeScale: 64, entityExtent: 1000));
    }

    [Fact]
    public void ZeroMeansTwiceTheExtent_NotZeroSize() {
        // The trap: read literally, SizeScale 0 makes the sprite vanish. The engine instead uses
        // the entity extent directly, which spans 2x — equivalent to 256.
        Assert.Equal(SpriteBMeshFace.SizeScaleWhenZero, 256);
        Assert.Equal(2000, SpriteBMeshFace.WorldExtentFor(sizeScale: 0, entityExtent: 1000));
        Assert.Equal(SpriteBMeshFace.WorldExtentFor(sizeScale: 256, entityExtent: 1000),
            SpriteBMeshFace.WorldExtentFor(sizeScale: 0, entityExtent: 1000));
    }

    [Fact]
    public void ANegativeExtentIsADirection_NotANegativeSize() {
        Assert.Equal(SpriteBMeshFace.WorldExtentFor(100, 1000),
            SpriteBMeshFace.WorldExtentFor(100, -1000));
    }

    [Fact]
    public void TheTexturesPixelSizeDoesNotAffectIt() {
        // Only the ASPECT comes from the bitmap; the extent is entity-derived. Expressed as the
        // absence of a texture parameter, and asserted here so the point survives a refactor that
        // is tempted to pass one in.
        Assert.Equal(320, SpriteBMeshFace.WorldExtentFor(sizeScale: 40, entityExtent: 1024));
    }

    /// <summary>
    /// The original fits the larger side IN VGA PIXELS to the extent and scales both by the same
    /// factor (WORLDRND.C:171-180). The texture arrives 5x6 per VGA pixel, so a tall sprite's
    /// larger canonical side over-counts its height by 1.2 (TASK-764).
    /// </summary>
    [Fact]
    public void ATallSpriteIsItsExtentWideInVgaTermsAndOnePointTwoTallerInTheSquareWorld() {
        // A 50x100 VGA sprite: canonical 250x600. Its larger VGA side (100) is the extent, so it is
        // 500 wide and 1000 tall in VGA proportion, and 1200 tall once the pixel aspect is in.
        (double w, double h) = SpriteBMeshFace.BillboardWorldSize(1000, textureWidth: 250, textureHeight: 600);
        Assert.Equal(500, w, 6);
        Assert.Equal(1200, h, 6);
    }

    [Fact]
    public void AWideSpritesWidthIsTheExtent() {
        // 100x50 VGA -> canonical 500x300: the width is the extent; height 50/100 of it, x1.2.
        (double w, double h) = SpriteBMeshFace.BillboardWorldSize(1000, textureWidth: 500, textureHeight: 300);
        Assert.Equal(1000, w, 6);
        Assert.Equal(600, h, 6);
    }

    [Fact]
    public void TheAnchorIsAFractionOfTheCanonicalTexture() {
        // AnchorX/Y are VGA pixels of the source bitmap; the texture is 5x6 per pixel.
        var face = new SpriteBMeshFace { AnchorX = 25, AnchorY = 90 };
        (double u, double v) = face.AnchorFraction(textureWidth: 250, textureHeight: 564);
        Assert.Equal(0.5, u, 6);
        Assert.Equal(540.0 / 564.0, v, 6);
    }
}
