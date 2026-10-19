namespace GameData.Resources.World;

using System;
using GameData.Resources.GameState;

/// <summary>A direction on the sky: East (+X in the world), Up, North; a unit vector.</summary>
public readonly record struct SkyDirection(double East, double Up, double North) {
    public double ElevationDegrees => Math.Asin(Math.Clamp(Up, -1, 1)) * 180 / Math.PI;
}

/// <summary>
/// Where the Enhanced sky's bodies are and how cloudy it is, from the game clock (2-second ticks).
/// Engine-free so the rules are tested here; Unity only draws them.
/// </summary>
public sealed class SkyClock {
    private readonly SkyLayout _layout;

    public SkyClock(SkyLayout layout) => _layout = layout;

    private static double HourOf(long ticks) =>
        (double)(((ticks % GameTime.UnitsPerDay) + GameTime.UnitsPerDay) % GameTime.UnitsPerDay) / GameTime.UnitsPerHour;

    private static double DaysOf(long ticks) => (double)ticks / GameTime.UnitsPerDay;

    /// <summary>
    /// A body on an east→west arc that crosses the horizon at <paramref name="rise"/> and
    /// <paramref name="set"/>: the day half is spread over the time it is up, the night half over the rest,
    /// so it is highest exactly halfway between and lowest halfway through the night.
    /// </summary>
    private static SkyDirection OnArc(double hour, double rise, double set, double tiltDegrees) {
        double up = Wrap(set - rise);
        double since = Wrap(hour - rise);
        double theta = since <= up
            ? Math.PI * since / up
            : Math.PI + (Math.PI * (since - up) / (24 - up));
        double tilt = tiltDegrees * Math.PI / 180;
        double sin = Math.Sin(theta);
        // Rotate the east-up half circle about the east-west axis, leaning it south.
        return new SkyDirection(Math.Cos(theta), sin * Math.Cos(tilt), -sin * Math.Sin(tilt));
    }

    private static double Wrap(double hours) => ((hours % 24) + 24) % 24;

    public SkyDirection Sun(long ticks) =>
        OnArc(HourOf(ticks), _layout.SunriseHour, _layout.SunsetHour, _layout.ArcTiltDegrees);

    /// <summary>0 = new, 0.5 = full.</summary>
    public double MoonPhase(int index, long ticks) {
        MoonLayout m = _layout.Moons[index];
        double p = (DaysOf(ticks) + m.PhaseOffsetDays) / m.PeriodDays;
        return p - Math.Floor(p);
    }

    /// <summary>How lit a moon's face is: 0 new, 1 full.</summary>
    public double MoonFullness(int index, long ticks) => 1 - (Math.Abs(MoonPhase(index, ticks) - 0.5) * 2);

    /// <summary>A moon trails the sun by its phase: new rises with the sun, full rises at sunset.</summary>
    public SkyDirection Moon(int index, long ticks) {
        double lag = MoonPhase(index, ticks) * 24;
        return OnArc(Wrap(HourOf(ticks) - lag), _layout.SunriseHour, _layout.SunsetHour, _layout.Moons[index].ArcTiltDegrees);
    }

    /// <summary>
    /// Cover 0..CloudMaxCover for a zone: the zone's climate plus a per-day value, held all day and
    /// cross-faded to the next day's between CloudFadeStartHour and CloudFadeEndHour.
    /// </summary>
    public double CloudCover(int zone, long ticks) {
        // A day's sky runs from one fade's end to the next's, so it is the day on which the last fade ended
        // (whether or not the fade spans midnight), and the fade leads into the following day's.
        // Floored, not GameTime.DayOf: that clamps the hours before the first fade's end to day 0,
        // which is the day AFTER them (the first night would snap back at 03:00).
        long since = ticks - (long)(_layout.CloudFadeEndHour * GameTime.UnitsPerHour);
        long from = since >= 0 ? since / GameTime.UnitsPerDay : ((since + 1) / GameTime.UnitsPerDay) - 1;
        double fadeLength = Wrap(_layout.CloudFadeEndHour - _layout.CloudFadeStartHour);
        double intoFade = Wrap(HourOf(ticks) - _layout.CloudFadeStartHour);
        if (intoFade >= fadeLength) return DayCover(zone, from);
        double t = intoFade / fadeLength;
        t = t * t * (3 - (2 * t));
        return DayCover(zone, from) + ((DayCover(zone, from + 1) - DayCover(zone, from)) * t);
    }

    private double DayCover(int zone, long day) {
        CloudClimate c = _layout.ClimateFor(zone);
        double v = c.Mean + (c.Spread * ((2 * Hash01(zone, day)) - 1));
        return Math.Clamp(v, 0, _layout.CloudMaxCover);
    }

    /// <summary>A fixed integer hash, so the same (zone, day) gives the same sky on every runtime.</summary>
    private static double Hash01(int zone, long day) {
        unchecked {
            ulong x = ((ulong)zone * 0x9E3779B97F4A7C15UL) ^ ((ulong)day * 0xBF58476D1CE4E5B9UL);
            x ^= x >> 31; x *= 0x94D049BB133111EBUL; x ^= x >> 29;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }

    public (double R, double G, double B) SunColor(double elevationDegrees) {
        double t = Math.Clamp(elevationDegrees / _layout.WarmToWhiteDegrees, 0, 1);
        (double R, double G, double B) lo = _layout.SunColorLow, hi = _layout.SunColorHigh;
        return (lo.R + ((hi.R - lo.R) * t), lo.G + ((hi.G - lo.G) * t), lo.B + ((hi.B - lo.B) * t));
    }

    public GradingKey GradingAt(long ticks) {
        double hour = HourOf(ticks);
        GradingKey[] keys = _layout.Grading;
        int next = 0;   // a plain loop: this runs every frame, and a lambda would allocate its closure
        while (next < keys.Length && keys[next].Hour <= hour) next++;
        if (next == keys.Length) next = -1;
        GradingKey b = next < 0 ? keys[0] : keys[next];
        GradingKey a = next <= 0 ? keys[^1] : keys[next - 1];
        double span = Wrap(b.Hour - a.Hour);
        double t = span == 0 ? 0 : Wrap(hour - a.Hour) / span;
        static double L(double x, double y, double t) => x + ((y - x) * t);
        static (double, double, double) C((double R, double G, double B) x, (double R, double G, double B) y, double t) =>
            (L(x.R, y.R, t), L(x.G, y.G, t), L(x.B, y.B, t));
        return new GradingKey(hour, L(a.Temperature, b.Temperature, t), L(a.Tint, b.Tint, t),
            C(a.Shadows, b.Shadows, t), C(a.Highlights, b.Highlights, t),
            L(a.Saturation, b.Saturation, t), L(a.Bloom, b.Bloom, t));
    }
}
