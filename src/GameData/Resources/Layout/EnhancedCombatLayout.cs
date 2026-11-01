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
    /// The band's four slots, left to right, as relative widths at 1600 wide: medallion and party,
    /// hero card, ledger, stones. The foe card stands above the stones, its width the stones' share
    /// (owner, 2026-11-01: it gave its band width to the hero card and the ledger).
    /// </summary>
    public float[] BandSlotWidths { get; set; } = new float[] { 278, 468, 432, 360 };

    /// <summary>Log lines the ledger shows at once.</summary>
    public int LedgerLogLines { get; set; } = 2;

    /// <summary>Size of one party coin on the desktop band, in pixels.</summary>
    public float PartyCoinPx { get; set; } = 56f;

    /// <summary>Most party coins the desktop band lays out.</summary>
    public int MaxPartyCoins { get; set; } = 7;

    /// <summary>The window height the desktop frames were drawn at; <see cref="PartyCoinPx"/> is in its pixels.</summary>
    public float DesktopReferenceHeightPx { get; set; } = 900f;

    /// <summary>The medallion (the actor's portrait) as a fraction of the band's height.</summary>
    public float MedallionBandFraction { get; set; } = 0.62f;

    /// <summary>One stone's disc as a fraction of the band's height.</summary>
    public float StoneBandFraction { get; set; } = 0.40f;

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

    /// <summary>Phone body text, in points.</summary>
    public float PhoneBodyTextPt { get; set; } = 13f;

    /// <summary>Phone headline text (names, the Confirm action), in points.</summary>
    public float PhoneHeadTextPt { get; set; } = 15f;

    /// <summary>Gap between the phone's docks and from the safe edge, in points.</summary>
    public float PhoneGapPt { get; set; } = 8f;

    /// <summary>The phone's top row height (hero chip, coins, sentence), in points.</summary>
    public float PhoneTopRowPt { get; set; } = 44f;

    /// <summary>The phone's hero chip width, in points.</summary>
    public float PhoneHeroChipWidthPt { get; set; } = 210f;

    /// <summary>The Confirm button, in points; Cancel is a <see cref="FoeChipPt"/> square above it.</summary>
    public float PhoneConfirmWidthPt { get; set; } = 150f;

    public float PhoneConfirmHeightPt { get; set; } = 56f;

    /// <summary>The peek (forecast, log, describe) between Confirm and the grid: width and height, in points.</summary>
    public float PhonePeekWidthPt { get; set; } = 406f;

    public float PhonePeekHeightPt { get; set; } = 106f;

    /// <summary>The hero card sheet's widest, in points (it opens from the left under the top row, as wide as
    /// its content up to this: at a fixed 500 its few numbers were spread too far apart to read).</summary>
    public float PhoneSheetWidthPt { get; set; } = 330f;

    /// <summary>The sheet's portrait, square, in points.</summary>
    public float PhoneSheetHeadPt { get; set; } = 72f;

    /// <summary>The sheet's text, in points: larger than the docks', it is read at rest.</summary>
    public float PhoneSheetTextPt { get; set; } = 15f;

    /// <summary>The sheet's stat labels' width, in points, so the values line up beside them.</summary>
    public float PhoneSheetLabelPt { get; set; } = 64f;

    /// <summary>Points per inch the phone's point is defined at (Android's dp: 160).</summary>
    public float PointsPerInch { get; set; } = 160f;

    // Shared

    /// <summary>Camera orbit per screen pixel of drag, in degrees.</summary>
    public float OrbitDegreesPerPixel { get; set; } = 0.25f;

    /// <summary>The lowest pitch the camera may take, in degrees. The driver lowers it further to
    /// <see cref="HorizonFromTop"/>'s pitch, which depends on the lens.</summary>
    public float PitchMinDegrees { get; set; } = 25f;

    /// <summary>How far the camera may tilt up: until the horizon is this fraction of the screen height from
    /// the top (owner, round 6: about a quarter).</summary>
    public float HorizonFromTop { get; set; } = 0.25f;

    /// <summary>The highest pitch the camera may take, in degrees. Above about 55 the standing figures
    /// (billboards) are seen from too far above and flatten into cards (tuned live, TASK-972 task 11).</summary>
    public float PitchMaxDegrees { get; set; } = 55f;

    /// <summary>The pitch a fight opens at, in degrees: the whole board in view, the figures still upright.</summary>
    public float StartPitchDegrees { get; set; } = 40f;

    /// <summary>The phone's free area for the board, bottom and top as fractions of the screen height
    /// from the bottom: between the bottom docks (action grid, peek, Confirm) and the top row.</summary>
    public float PhoneBoardBottom { get; set; } = 0.30f;

    public float PhoneBoardTop { get; set; } = 0.86f;

    /// <summary>Space kept between the framed board and its free area's top and bottom edges, as a fraction of
    /// the screen height.</summary>
    public float BoardFitMarginY { get; set; } = 0.02f;

    /// <summary>Space kept between the framed board and the screen's sides, as a fraction of the screen width
    /// (the phone's side buttons and foe chips sit there).</summary>
    public float BoardFitMarginX { get; set; } = 0.1f;

    /// <summary>Zoom factor per mouse-wheel notch (the camera distance is multiplied or divided by it).</summary>
    public float WheelZoomStep { get; set; } = 1.1f;

    /// <summary>The closest zoom, as a factor of the whole-board distance: a few cells fill the free area.</summary>
    public float ZoomMin { get; set; } = 0.2f;

    /// <summary>The farthest zoom: 1 is the distance at which the whole board just fits the free area (owner,
    /// 2026-10-28: never farther).</summary>
    public float ZoomMax { get; set; } = 1f;

    /// <summary>The zoom a phone fight opens at (and Reset returns to): closer than the whole board, whose
    /// cells are too small to tap there. The desktop opens at the whole board.</summary>
    public float PhoneStartZoom { get; set; } = 0.6f;

    /// <summary>Seconds a close view takes to glide, eased in and out, to the next acting hero (owner, 2026-10-29:
    /// "make the camera jump smooth").</summary>
    public float FocusGlideSeconds { get; set; } = 0.6f;

    /// <summary>How far inside the board's framed band (viewport fraction) a hero's or monster's cell must be to count
    /// as in view; only outside it does the camera glide to it (owner, 2026-10-30: "the camera appears to be jerking
    /// around the enemy turns" -- the fewest moves). A tenth keeps one at the very edge from acting half off screen.</summary>
    public float FocusInViewMargin { get; set; } = 0.1f;

    /// <summary>The least a glide must shift the view's centre, in cells, to be made at all: a smaller shift is a
    /// twitch, and at the whole-board zoom the centre does not follow the focus, so no glide happens there.</summary>
    public float FocusGlideMinCells { get; set; } = 1f;

    /// <summary>
    /// Seconds held after a monster's action has finished playing, before the next turn (owner, 2026-10-30:
    /// "I can't follow what's going on"). The original moves on at once (COMBAT.C:1588's loop has no wait);
    /// this is presentation only. 0.6 s: the struck combatant's number floats for 8 arena frames, 0.43 s
    /// (dmgFloatFrames = 8, COMBAT.C:383, at 18.5 frames a second), so it has finished and the log line
    /// has been on screen a moment before anything else moves.
    /// </summary>
    public float EnemyActionBeatSeconds { get; set; } = 0.6f;

    /// <summary>Opacity of a figure seen through the board's occluders (see-through): the occluder stays
    /// clearly visible while the unit behind reads through it (owner's phone test, 2026-10-28).</summary>
    public float SeeThroughAlpha { get; set; } = 0.55f;

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

    /// <summary>Phone: a second tap on what is forecast acts, as Confirm does -- unless it comes sooner than this
    /// after the tap that made the forecast, which is an accidental double tap and is ignored (owner, 2026-10-29).</summary>
    public float RetapCommitMinSeconds { get; set; } = 0.3f;

    // Board marks

    /// <summary>A mark's half-width as a fraction of the cell, the original's combat marker (128 of 300).</summary>
    public float MarkHalfCell { get; set; } = 128f / 300f;

    /// <summary>The cell marks are frames, not fills (owner, 2026-10-29: the filled squares dominated the board and
    /// hid the combatants' feet): the frame's width, in cells. Read at the phone's opening zoom and fully out.</summary>
    public float MarkFrameCells { get; set; } = 0.06f;

    /// <summary>How far the marks sit above the ground, in cells: clear of the road's own paint-order lift.</summary>
    public float MarkLiftCells { get; set; } = 0.05f;

    /// <summary>A reachable cell is drawn as the ground under it this much lighter (1.3 = 30%, as the eye sees
    /// it), not in a colour of its own (owner, round 6: the green squares were too bright; then "a touch
    /// lighter"). 1..2.</summary>
    public float MarkReachLighten { get; set; } = 1.3f;

    /// <summary>The other marks' colours, RGBA 0..1: the acting hero's cell, a foe a Thrust reaches,
    /// a foe a Swing reaches (a frame nested inside the Thrust's), the foe under the pointer.</summary>

    public float[] MarkActorRgba { get; set; } = { 0.45f, 1f, 0.55f, 1f };

    public float[] MarkThrustRgba { get; set; } = { 1f, 0.5f, 0.2f, 0.95f };

    public float[] MarkSwingRgba { get; set; } = { 1f, 0.85f, 0.2f, 0.95f };

    public float[] MarkTargetRgba { get; set; } = { 1f, 1f, 1f, 0.9f };

    /// <summary>Where the forecast walk ends -- the cell a Move stops on, or the side a Thrust walks to before it
    /// strikes (owner, 2026-10-29: explain the move + attack combo).</summary>
    public float[] MarkWalkEndRgba { get; set; } = { 0.55f, 0.85f, 1f, 1f };

    /// <summary>Trap boards: how far a drag may turn the camera either side of its start, in degrees.</summary>
    public float TrapYawLimitDegrees { get; set; } = 15f;

    /// <summary>Trap boards: the width of the rim drawn round the board edge, in cells.</summary>
    public float TrapRimCells { get; set; } = 0.15f;

    /// <summary>Trap boards: the rim's colour, RGBA 0..1.</summary>
    public float[] TrapRimRgba { get; set; } = { 0.54f, 0.49f, 0.42f, 1f };

    /// <summary>Trap boards: the dim laid over everything past the rim, RGBA 0..1.</summary>
    public float[] TrapOutsideDimRgba { get; set; } = { 0f, 0f, 0f, 0.55f };

    /// <summary>Trap boards: the farthest zoom frames the board's width (rim included) with this margin each side,
    /// as a fraction of the screen width; its depth may run off screen (owner, round 6).</summary>
    public float TrapFitMarginX { get; set; } = 0.03f;

    /// <summary>A foe's letter plate: width and height, in cells.</summary>
    public float PlateWidthCells { get; set; } = 0.35f;

    public float PlateHeightCells { get; set; } = 0.4f;

    /// <summary>How far above a foe's drawn top the plate floats, in cells.</summary>
    public float PlateLiftCells { get; set; } = 0.15f;

    /// <summary>The path's dots: spacing along the walk and size, in cells.</summary>
    public float PathDotSpacingCells { get; set; } = 0.25f;

    /// <summary>
    /// How far the board's marks and grid follow the world's light level: 0 never dim, 1 dim exactly as
    /// the ground. Most of the way, so they stay readable at night but no longer glow (owner, 2026-11-01).
    /// </summary>
    public float MarkLightFollow { get; set; } = 0.75f;

    public float PathDotSizeCells { get; set; } = 0.07f;
}
