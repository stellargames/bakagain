namespace GameData.Resources.Layout;

/// <summary>
/// Tuning for the overhead map's pointer controls (drag to pan, pinch and wheel zoom, twist and
/// right-drag to turn). Fractions and rates only; the defaults are the shipped values.
/// </summary>
public sealed class EnhancedMapLayout
{
    /// <summary>How far the view may be dragged off the party: the party stays this far inside the view's half-extent.</summary>
    public float PanLimitFraction { get; set; } = 0.85f;

    /// <summary>How quickly the shown height eases to the zoom step (per second; higher is snappier).</summary>
    public float ZoomEaseRate { get; set; } = 12f;

    /// <summary>The finger-distance ratio a pinch must change by to take one zoom step.</summary>
    public float PinchStepRatio { get; set; } = 1.25f;

    /// <summary>The share of a full turn one right-drag across the whole view turns the map.</summary>
    public float TurnPerViewWidth { get; set; } = 0.5f;
}
