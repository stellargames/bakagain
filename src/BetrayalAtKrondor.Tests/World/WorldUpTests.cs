namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

/// <summary>World-up heights: the original's units scaled into the square-pixel world (TASK-764).</summary>
public class WorldUpTests {
    [Theory]
    [InlineData(0, 0)]
    [InlineData(230, 276)]
    [InlineData(510, 612)]
    [InlineData(7, 8)]      // 8.4 rounds down
    [InlineData(-5, -6)]    // -6.0 exactly
    [InlineData(-3, -4)]    // -3.6 rounds away from zero
    public void FromOriginal_ScalesByTheVgaPixelAspect(int original, int square) =>
        Assert.Equal(square, WorldUp.FromOriginal(original));

    /// <summary>The save writer puts the original's units back; integers survive the round trip.</summary>
    [Fact]
    public void ToOriginal_InvertsFromOriginal_ForEveryShortValue() {
        for (int z = short.MinValue; z <= short.MaxValue; z++) {
            Assert.Equal(z, WorldUp.ToOriginal(WorldUp.FromOriginal(z)));
        }
    }

    /// <summary>
    /// A camera pitch turns with the world: it looks at the same world point once heights are x1.2,
    /// so tan(pitch) scales by 1.2. Simulated against the original's arena grid this lands within
    /// 3.5 VGA px rms (best possible 3.48); keeping the original's angle missed by 20 (TASK-764).
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-2112, -2519)]   // START.DAT's combat pitch, above ground
    [InlineData(-3030, -3593)]   // ...and underground
    [InlineData(280, 336)]       // the outdoor walking pitch
    [InlineData(-0x4000, -0x4000)] // straight down stays straight down
    public void PitchFromOriginal_LooksAtTheSameWorldPoint(int original, int square) =>
        Assert.Equal(square, WorldUp.PitchFromOriginal(original));
}
