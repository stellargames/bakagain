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
    /// <summary>The grid toggle in a fight (the original's G key).</summary>
    public float GridY { get; set; } = 0.3f;
    public float ThrustY { get; set; } = 0.45f;
    public float SwingY { get; set; } = 0.62f;
    public float CycleSize { get; set; } = 0.25f;
    public float CycleMargin { get; set; } = 0.04f;

    /// <summary>T3: an arrow's invisible touch area as a multiple of its drawn size.</summary>
    public float ArrowTouchGrow { get; set; } = 3.0f;

    /// <summary>C2: panel px the hover point sits above the finger, so the finger never hides it.</summary>
    public float FingerHoverOffsetY { get; set; } = 120f;

    /// <summary>C1: panel px a tap may miss a target by and still select it.</summary>
    public float SnapRadius { get; set; } = 60f;

    /// <summary>Panel px: a narrower bar (a 4:3 tablet) shows no side controls.</summary>
    public float MinBarWidth { get; set; } = 120f;
}
