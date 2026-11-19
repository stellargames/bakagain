using GameData.Resources.Config;
using Xunit;

namespace BetrayalAtKrondor.Tests.Data;

// ENCAMP.C:74-77, 150-158: the cure is once per camp-screen visit, due once 13 whole hours have passed
// since the screen OPENED (game_time / 0x708 against the opening's), however many rests that took.
public class CampSickCureTests {
    private const long H = 1800;

    [Fact]
    public void TwoRestsInOneVisitAddUp() {
        // 8 h, then 6 h: the original cures five hours into the second rest.
        Assert.False(CampSickCure.Due(pending: true, visitStart: 10 * H, now: 18 * H));
        Assert.True(CampSickCure.Due(pending: true, visitStart: 10 * H, now: 23 * H));
    }

    [Fact]
    public void WholeHoursOfTheClockNotOfTheRest() {
        // Opened at 10:30; the clock reads 23:00 after 12.5 h: hour 23 - hour 10 = 13.
        Assert.True(CampSickCure.Due(pending: true, visitStart: 10 * H + H / 2, now: 23 * H));
    }

    [Fact]
    public void OncePerVisit() {
        Assert.False(CampSickCure.Due(pending: false, visitStart: 0, now: 40 * H));
    }
}
