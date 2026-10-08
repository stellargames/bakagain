namespace BetrayalAtKrondor.Tests.Character;

using GameData.Resources.Character;
using Xunit;

public class PortraitRingTests {
    [Fact]
    public void StaminaThenHealthAsShareOfTheCombinedPool() {
        (double s, double h) = PortraitRing.Fractions(stamina: 10, health: 30, poolMax: 80);
        Assert.Equal(0.125, s, 9);
        Assert.Equal(0.375, h, 9);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-5, 40, 40)]
    [InlineData(60, 60, 100)]
    public void NeverNegativeAndNeverMoreThanTheWholeRing(int stamina, int health, int max) {
        (double s, double h) = PortraitRing.Fractions(stamina, health, max);
        Assert.InRange(s, 0.0, 1.0);
        Assert.InRange(h, 0.0, 1.0);
        Assert.True(s + h <= 1.0 + 1e-9);
    }
}
