namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

public class SpriteNearFadeTests {
    [Fact]
    public void AnythingTheOriginalDrawsIsDrawnWhole() {
        // WORLDRND.C:196-199 culls below 0x5dc; at and beyond it the port must not fade at all.
        Assert.Equal(1.0, SpriteNearFade.Visibility(1500));
        Assert.Equal(1.0, SpriteNearFade.Visibility(4000));
    }

    [Fact]
    public void ASpriteAtTheCameraIsHidden_AndTheBandBetweenIsLinear() {
        Assert.Equal(0.0, SpriteNearFade.Visibility(0));
        Assert.Equal(0.0, SpriteNearFade.Visibility(1000));
        Assert.Equal(0.5, SpriteNearFade.Visibility(1250), 6);
    }
}
