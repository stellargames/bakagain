namespace GameData.Resources.Layout;

/// <summary>
/// Where the Android touch aids sit inside the pillarbox side bars
/// (docs/superpowers/specs/2026-09-29-android-touch-aids-design.md in the private workspace).
/// Fractions of the bar's own width/height unless noted, so the same numbers fit any phone.
/// The defaults are the shipped values.
/// </summary>
public sealed class TouchControlsLayout
{
    public float PadSize { get; set; } = 0.8f;
    public float PadCentreY { get; set; } = 0.55f;
    public float ButtonWidth { get; set; } = 0.8f;
    public float ButtonHeight { get; set; } = 0.12f;
    public float ThrustY { get; set; } = 0.45f;
    public float SwingY { get; set; } = 0.62f;
    public float GridButtonSize { get; set; } = 0.25f;
    public float GridButtonMargin { get; set; } = 0.04f;

    /// <summary>Panel px: a narrower bar (a 4:3 tablet) shows no side controls.</summary>
    public float MinBarWidth { get; set; } = 120f;
}
