namespace GameData.Resources.Layout;

/// <summary>
/// Tuning for Enhanced full-screen travel (docs/specs/2026-10-08-enhanced-mode-travel-design.md
/// in the private workspace). Fractions and rates only, no coordinates; the HUD's geometry is in
/// the theme's <c>.enhanced-hud*</c> classes. The defaults are the shipped values.
/// </summary>
public sealed class EnhancedTravelLayout
{
    /// <summary>Screen pixels (<c>IPointer.Delta</c>) a press must travel before it is a look-drag rather than a click.</summary>
    public float DragThresholdPx { get; set; } = 8f;

    /// <summary>Look rotation per screen pixel (<c>IPointer.Delta</c>) of drag.</summary>
    public float DegreesPerPixel { get; set; } = 0.25f;

    /// <summary>Pitch's share of <see cref="DegreesPerPixel"/>: looking up and down is slower than turning.</summary>
    public float PitchFraction { get; set; } = 0.5f;

    /// <summary>
    /// The pitch mouse/touch look approaches but never reaches, either way. There is no hard stop:
    /// near level a drag pitches at full rate, and each further degree takes more drag than the last.
    /// </summary>
    public float PitchMaxDegrees { get; set; } = 89f;

    /// <summary>
    /// Where the touch pads' band starts, as a fraction of the window height; it runs to the bottom.
    /// The left edge's fourth quarter (TASK-939): the theme's portrait column (<c>.enhanced-hud__portraits</c>) takes the three above.
    /// </summary>
    public float TouchPadBandTop { get; set; } = 0.75f;

    /// <summary>The HUD status line's font size, as a fraction of the window height.</summary>
    public float StatusFontFraction { get; set; } = 0.024f;
}
