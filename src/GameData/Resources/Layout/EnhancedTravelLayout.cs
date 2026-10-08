namespace GameData.Resources.Layout;

/// <summary>
/// Tuning for Enhanced full-screen travel (docs/superpowers/specs/2026-10-08-enhanced-mode-travel-design.md
/// in the private workspace). Fractions and rates only, no coordinates; the HUD's geometry is in
/// the theme's <c>.enhanced-hud*</c> classes. The defaults are the shipped values.
/// </summary>
public sealed class EnhancedTravelLayout
{
    /// <summary>Panel px a press must travel before it is a look-drag rather than a click.</summary>
    public float DragThresholdPx { get; set; } = 8f;

    /// <summary>Look rotation per panel px of drag.</summary>
    public float DegreesPerPixel { get; set; } = 0.25f;

    /// <summary>How far mouse/touch look may pitch the camera either way.</summary>
    public float PitchLimitDegrees { get; set; } = 30f;

    /// <summary>Vertical centre of the touch movement pads, as a fraction of the window height.</summary>
    public float TouchPadCentreY { get; set; } = 0.86f;

    /// <summary>The HUD status line's font size, as a fraction of the window height.</summary>
    public float StatusFontFraction { get; set; } = 0.024f;
}
