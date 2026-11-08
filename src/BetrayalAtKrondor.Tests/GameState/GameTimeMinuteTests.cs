namespace BetrayalAtKrondor.Tests.GameState;

using GameData.Resources.GameState;
using GameData.Resources.Text;
using Xunit;

[Collection(BetrayalAtKrondor.Tests.Text.UiStringsCollection.Name)]
// The Enhanced HUD's clock reads minutes too (TASK-1057: "Day 2 · 9:00" at 09:34).
public class GameTimeMinuteTests {
    [Fact]
    public void MinutesAreThirtyTwoSecondUnitsIntoTheHour() {
        long t = (2 * GameTime.UnitsPerDay) + (9 * GameTime.UnitsPerHour) + (34 * 30) + 29;
        Assert.Equal(9, GameTime.HourOfDay(t));
        Assert.Equal(34, GameTime.MinuteOfHour(t));
    }

    [Fact]
    public void TheStatusLineShowsTwoDigitMinutes() =>
        Assert.Equal("Day 2 · 9:05", UiTemplates.Format(UiTemplates.EnhancedStatusKey,
            ("day", (object)2), ("hour", (object)9), ("minute", (object)"05")));
}
