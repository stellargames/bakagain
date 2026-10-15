namespace GameData.Resources.World;

using System.Collections.Generic;

/// <summary>A moon of Midkemia (after the Riftwar novels; the game's data has no moons).</summary>
public sealed record MoonLayout(string Name, double SizeDegrees, double PeriodDays, double PhaseOffsetDays,
    double Brightness, double ArcTiltDegrees);

/// <summary>A zone's typical sky: mean cloud cover 0..1 and how far a day may stray from it.</summary>
public sealed record CloudClimate(double Mean, double Spread);

/// <summary>One time-of-day colour grade. Colours are linear RGB 0..1; Temperature/Tint in URP's
/// White Balance units (-100..100); Contrast in Color Adjustments units; Bloom is the intensity.</summary>
public sealed record GradingKey(double Hour, double Temperature, double Tint,
    (double R, double G, double B) Shadows, (double R, double G, double B) Highlights,
    double Contrast, double Saturation, double Bloom);

/// <summary>
/// Tuning for the Enhanced sky and lighting (spec 2026-10-15-enhanced-sky-design). Invented for
/// Enhanced, not ported: the defaults are art choices, and a mod can retint the sky by replacing them.
/// The brightness of the world is NOT here; it stays <see cref="DaylightLevel"/>'s.
/// </summary>
public sealed class SkyLayout {
    /// <summary>Halfway up the original's dawn ramp (04:00 → 08:00).</summary>
    public double SunriseHour { get; set; } = 6.0;

    /// <summary>Halfway down the original's dusk ramp (17:00 → 20:00).</summary>
    public double SunsetHour { get; set; } = 18.5;

    /// <summary>How far the sun's arc leans south of the zenith; its noon elevation is 90 minus this.</summary>
    public double ArcTiltDegrees { get; set; } = 30;

    /// <summary>Large first: the one the narration calls "the moon".</summary>
    public MoonLayout[] Moons { get; set; } = {
        new("Large", 2.4, 29.0, 0.0, 1.00, 25),
        new("Middle", 1.1, 17.0, 6.0, 0.55, 35),
        new("Small", 0.6, 9.0, 2.5, 0.35, 15),
    };

    /// <summary>Per overland zone. Art values to tune by eye, not geography read from data.</summary>
    public Dictionary<int, CloudClimate> Climate { get; set; } = new() {
        [1] = new(0.30, 0.25), [2] = new(0.40, 0.25), [3] = new(0.25, 0.20), [4] = new(0.45, 0.30),
        [5] = new(0.35, 0.25), [6] = new(0.20, 0.20), [7] = new(0.50, 0.25),
    };

    public CloudClimate DefaultClimate { get; set; } = new(0.35, 0.25);

    /// <summary>Never so cloudy it reads as rain: the game has no weather.</summary>
    public double CloudMaxCover { get; set; } = 0.85;

    /// <summary>The cover cross-fades to the next day's value over these hours (wrapping midnight).</summary>
    public double CloudFadeStartHour { get; set; } = 21;
    public double CloudFadeEndHour { get; set; } = 3;

    /// <summary>How far the mountain panels are blended toward the sky behind them, and the extra at their base.</summary>
    public double HazeStrength { get; set; } = 0.30;
    public double HazeBaseExtra { get; set; } = 0.20;

    /// <summary>How strongly the noon horizon is pulled to the zone's sky pen (0 = pure scattering).</summary>
    public double ZoneAnchorWeight { get; set; } = 0.8;

    /// <summary>Sun light colour at the horizon and at height, linear RGB; blended over 0..WarmToWhiteDegrees.</summary>
    public (double R, double G, double B) SunColorLow { get; set; } = (1.00, 0.50, 0.25);
    public (double R, double G, double B) SunColorHigh { get; set; } = (1.00, 0.97, 0.92);
    public double WarmToWhiteDegrees { get; set; } = 20;

    /// <summary>Time-of-day grades, sorted by hour; interpolated, wrapping midnight.</summary>
    public GradingKey[] Grading { get; set; } = {
        new(0.0, -25, 5, (0.30, 0.38, 0.60), (0.55, 0.60, 0.80), 10, -15, 0.20),
        new(5.0, -20, 5, (0.35, 0.35, 0.60), (0.80, 0.60, 0.55), 10, -10, 0.25),
        new(6.5, 20, 0, (0.45, 0.35, 0.40), (1.00, 0.75, 0.45), 15, 5, 0.60),
        new(9.0, 5, 0, (0.50, 0.50, 0.50), (0.55, 0.53, 0.50), 8, 5, 0.35),
        new(12.25, 0, 0, (0.50, 0.50, 0.50), (0.50, 0.50, 0.50), 8, 5, 0.30),
        new(16.0, 8, 0, (0.50, 0.48, 0.48), (0.60, 0.55, 0.48), 10, 5, 0.35),
        new(18.5, 30, 5, (0.40, 0.30, 0.45), (1.00, 0.65, 0.35), 18, 10, 0.70),
        new(20.0, -15, 8, (0.30, 0.32, 0.55), (0.70, 0.55, 0.65), 12, -5, 0.30),
    };

    /// <summary>Post-exposure (EV) per hour that holds the frame's mean luminance to Faithful's with
    /// Lighting on — ACES crushes the darks, so the night needs the most. Calibrated 2026-10-15 by
    /// EnhancedLightingBrightnessTests.Calibrate on the terrain of a real noon frame; re-run it after
    /// changing <see cref="Grading"/>.</summary>
    public double[] ExposureByHour { get; set; } = {
        2.962, 2.982, 3.070, 3.073, 3.076, 2.619, 2.221, 1.846, 1.500, 1.453, 1.453, 1.453,
        1.459, 1.465, 1.471, 1.471, 1.500, 1.594, 2.086, 2.707, 3.270, 3.158, 3.152, 3.111,
    };

    public CloudClimate ClimateFor(int zone) =>
        Climate.TryGetValue(zone, out CloudClimate c) ? c : DefaultClimate;
}
