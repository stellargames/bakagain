namespace GameData.Resources.Layout;

/// <summary>
/// Tuning for the Enhanced combat HUD (docs/specs/2026-10-28-enhanced-combat-hud-design.md
/// in the private workspace). Fractions, sizes and rates only, no coordinates; the HUD's styling is in
/// the theme's <c>.enhanced-combat*</c> classes. The defaults are the approved frames' values.
/// </summary>
public sealed class EnhancedCombatLayout
{
    // Desktop

    /// <summary>The bottom band's height, as a fraction of the screen height.</summary>
    public float BandHeight { get; set; } = 0.27f;

    /// <summary>
    /// The band's five slots, left to right, as relative widths at 1600 wide: medallion and party,
    /// hero card, ledger, foe slot, stones.
    /// </summary>
    public float[] BandSlotWidths { get; set; } = new float[] { 278, 340, 304, 256, 360 };

    /// <summary>Log lines the ledger shows at once.</summary>
    public int LedgerLogLines { get; set; } = 2;

    /// <summary>Size of one party coin on the desktop band, in pixels.</summary>
    public float PartyCoinPx { get; set; } = 56f;

    /// <summary>Most party coins the desktop band lays out.</summary>
    public int MaxPartyCoins { get; set; } = 7;

    // Phone

    /// <summary>Width of one action tile, in points (the 44 pt touch-target floor is met with room).</summary>
    public float TileWidthPt { get; set; } = 54f;

    /// <summary>Height of one action tile, in points.</summary>
    public float TileHeightPt { get; set; } = 50f;

    /// <summary>Gap between the Confirm and Cancel buttons, in points.</summary>
    public float ConfirmCancelGapPt { get; set; } = 12f;

    /// <summary>Most hero coins the phone HUD lays out.</summary>
    public int MaxHeroCoins { get; set; } = 3;

    /// <summary>Size of one foe chip, in points.</summary>
    public float FoeChipPt { get; set; } = 44f;

    /// <summary>Smallest text size the phone HUD uses, in points.</summary>
    public float MinTextPt { get; set; } = 11f;

    // Shared

    /// <summary>Camera orbit per screen pixel of drag, in degrees.</summary>
    public float OrbitDegreesPerPixel { get; set; } = 0.25f;

    /// <summary>The lowest pitch the camera may take, in degrees.</summary>
    public float PitchMinDegrees { get; set; } = 25f;

    /// <summary>The highest pitch the camera may take, in degrees (tuned in the camera task).</summary>
    public float PitchMaxDegrees { get; set; } = 70f;

    /// <summary>Zoom factor per mouse-wheel notch (the camera distance is multiplied or divided by it).</summary>
    public float WheelZoomStep { get; set; } = 1.1f;

    /// <summary>The lowest zoom factor.</summary>
    public float ZoomMin { get; set; } = 0.6f;

    /// <summary>The highest zoom factor.</summary>
    public float ZoomMax { get; set; } = 1.6f;

    /// <summary>Opacity of a figure seen through the board's occluders (see-through).</summary>
    public float SeeThroughAlpha { get; set; } = 0.25f;

    /// <summary>Hit chance (percent) at or above which a melee hit is shown as "likely".</summary>
    public int HitPipLikelyAtLeast { get; set; } = 67;

    /// <summary>Hit chance (percent) at or above which a melee hit is shown as "even".</summary>
    public int HitPipEvenAtLeast { get; set; } = 34;

    /// <summary>
    /// Lower edges of the damage buckets shown before a foe's Defense is revealed. Bucket i is
    /// [edge i, edge i+1 - 1]; the last is open-ended (its high bound is <see cref="int.MaxValue"/>, "21+").
    /// </summary>
    public int[] DamageBandEdges { get; set; } = { 1, 3, 6, 10, 15, 21 };

    /// <summary>How long a press must be held to count as a long press, in seconds.</summary>
    public float LongPressSeconds { get; set; } = 0.5f;

    // Board marks

    /// <summary>A mark's half-width as a fraction of the cell, the original's combat marker (128 of 300).</summary>
    public float MarkHalfCell { get; set; } = 128f / 300f;

    /// <summary>A foe's letter plate: width and height, in cells.</summary>
    public float PlateWidthCells { get; set; } = 0.55f;

    public float PlateHeightCells { get; set; } = 0.4f;

    /// <summary>How far above a foe's drawn top the plate floats, in cells.</summary>
    public float PlateLiftCells { get; set; } = 0.15f;

    /// <summary>The path's dots: spacing along the walk and size, in cells.</summary>
    public float PathDotSpacingCells { get; set; } = 0.25f;

    public float PathDotSizeCells { get; set; } = 0.07f;
}
