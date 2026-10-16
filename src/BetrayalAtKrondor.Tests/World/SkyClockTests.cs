namespace BetrayalAtKrondor.Tests.World;

using System;
using GameData.Resources.GameState;
using GameData.Resources.World;
using Xunit;

/// <summary>The Enhanced sky's clock: where the sun and moons are, and how cloudy a zone is on a day.
/// Invented for Enhanced (the original has no sky); the brightness rule stays DaylightLevel's.</summary>
public class SkyClockTests {
    private static readonly SkyClock Clock = new(new SkyLayout());

    private static long At(int day, int hour, int minute = 0) =>
        (day * (long)GameTime.UnitsPerDay) + (hour * GameTime.UnitsPerHour) + ((minute * GameTime.UnitsPerHour) / 60);

    [Fact]
    public void TheSunCrossesTheHorizonAtSixAndHalfPastSix() {
        Assert.Equal(0, Clock.Sun(At(0, 6)).ElevationDegrees, 3);
        Assert.Equal(0, Clock.Sun(At(0, 18, 30)).ElevationDegrees, 3);
    }

    [Fact]
    public void TheSunIsHighestAtQuarterPastTwelve() {
        double noon = Clock.Sun(At(0, 12, 15)).ElevationDegrees;
        Assert.True(noon > Clock.Sun(At(0, 11, 45)).ElevationDegrees);
        Assert.True(noon > Clock.Sun(At(0, 12, 45)).ElevationDegrees);
        Assert.Equal(90 - new SkyLayout().ArcTiltDegrees, noon, 3);
    }

    [Fact]
    public void TheSunIsBelowTheHorizonAtThree() =>
        Assert.True(Clock.Sun(At(0, 3)).ElevationDegrees < 0);

    [Fact]
    public void TheSunRisesInTheEastAndSetsInTheWest() {
        Assert.True(Clock.Sun(At(0, 6, 1)).East > 0.9);
        Assert.True(Clock.Sun(At(0, 18, 29)).East < -0.9);
    }

    [Fact]
    public void EachDirectionIsAUnitVector() {
        for (int h = 0; h < 24; h++) {
            SkyDirection s = Clock.Sun(At(0, h));
            Assert.Equal(1, Math.Sqrt((s.East * s.East) + (s.Up * s.Up) + (s.North * s.North)), 6);
        }
    }

    [Fact]
    public void EachMoonsPhaseRepeatsAfterItsPeriod() {
        var layout = new SkyLayout();
        for (int m = 0; m < layout.Moons.Length; m++) {
            long period = (long)(layout.Moons[m].PeriodDays * GameTime.UnitsPerDay);
            Assert.Equal(Clock.MoonPhase(m, At(3, 10)), Clock.MoonPhase(m, At(3, 10) + period), 6);
        }
    }

    [Fact]
    public void AFullMoonRisesTwelveHoursAfterTheSun() {
        // A moon whose period is so long its phase stays 0.5 all day: it trails the sun by 12 hours,
        // so it crosses the horizon at 18:00 (the sun's 06:00) and is up at midnight.
        var layout = new SkyLayout { Moons = new[] { new MoonLayout("Test", 1, 1e9, 0.5e9, 1, 25) } };
        var clock = new SkyClock(layout);
        Assert.Equal(0.5, clock.MoonPhase(0, At(0, 18)), 6);
        Assert.Equal(0, clock.Moon(0, At(0, 18)).ElevationDegrees, 3);
        Assert.True(clock.Moon(0, At(0, 0)).ElevationDegrees > 30);
    }

    [Fact]
    public void CloudCoverIsStableForAZoneAndDay() =>
        Assert.Equal(Clock.CloudCover(1, At(5, 12)), Clock.CloudCover(1, At(5, 12)));

    [Fact]
    public void CloudCoverStaysInsideTheZonesClimate() {
        var layout = new SkyLayout();
        CloudClimate c = layout.ClimateFor(3);
        for (int d = 0; d < 200; d++) {
            double cover = Clock.CloudCover(3, At(d, 12));
            Assert.InRange(cover, Math.Max(0, c.Mean - c.Spread) - 1e-9, Math.Min(layout.CloudMaxCover, c.Mean + c.Spread) + 1e-9);
        }
    }

    [Fact]
    public void CloudCoverDiffersBetweenDays() {
        var distinct = new System.Collections.Generic.HashSet<double>();
        for (int d = 0; d < 20; d++) distinct.Add(Math.Round(Clock.CloudCover(1, At(d, 12)), 4));
        Assert.True(distinct.Count > 10);
    }

    [Fact]
    public void CloudCoverOnlyChangesAtNight() =>
        Assert.Equal(Clock.CloudCover(2, At(4, 4)), Clock.CloudCover(2, At(4, 20, 59)), 9);

    [Fact]
    public void CloudCoverMeetsTheNextDayAtThree() {
        double nextDay = Clock.CloudCover(2, At(5, 12));
        Assert.Equal(nextDay, Clock.CloudCover(2, At(5, 3)), 9);
    }

    [Fact]
    public void CloudCoverIsContinuousAcrossMidnight() =>
        Assert.True(Math.Abs(Clock.CloudCover(2, At(4, 23, 59)) - Clock.CloudCover(2, At(5, 0))) < 0.01);

    /// <summary>A mod's fade that does not span midnight (TASK-885): no jump at its end or at midnight.</summary>
    [Fact]
    public void CloudCoverIsContinuousForAFadeAfterMidnight() {
        var clock = new SkyClock(new SkyLayout { CloudFadeStartHour = 1, CloudFadeEndHour = 5 });
        for (int zone = 1; zone <= 12; zone++) {
            Assert.True(Math.Abs(clock.CloudCover(zone, At(5, 4, 59)) - clock.CloudCover(zone, At(5, 5))) < 0.01, $"zone {zone} at 05:00");
            Assert.True(Math.Abs(clock.CloudCover(zone, At(4, 23, 59)) - clock.CloudCover(zone, At(5, 0))) < 0.01, $"zone {zone} at midnight");
        }
    }

    [Fact]
    public void AnUnknownZoneUsesTheDefaultClimate() {
        var layout = new SkyLayout();
        double cover = Clock.CloudCover(42, At(1, 12));
        Assert.InRange(cover, Math.Max(0, layout.DefaultClimate.Mean - layout.DefaultClimate.Spread) - 1e-9,
            layout.DefaultClimate.Mean + layout.DefaultClimate.Spread + 1e-9);
    }

    [Fact]
    public void TheSunIsWarmLowAndWhiteHigh() {
        (double r, double g, double b) low = Clock.SunColor(2);
        (double r2, double g2, double b2) high = Clock.SunColor(60);
        Assert.True(low.r > low.b + 0.3);
        Assert.True(Math.Abs(high.r2 - high.b2) < 0.15);
    }

    [Fact]
    public void GradingIsInterpolatedAndWrapsAtMidnight() {
        GradingKey a = Clock.GradingAt(At(0, 23, 59));
        GradingKey b = Clock.GradingAt(At(1, 0));
        Assert.True(Math.Abs(a.Temperature - b.Temperature) < 1);
    }

}
