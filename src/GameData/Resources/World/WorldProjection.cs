namespace GameData.Resources.World;

using System;

/// <summary>
/// The original's perspective scale, and the Unity vertical FOV that reproduces it.
/// </summary>
/// <remarks>
/// <b>The scale is the ZONE's, and the map views share it with travel.</b> The renderer projects a
/// point at horizontal offset <c>X</c> and depth <c>Z</c> to <c>X * (1 &lt;&lt; zoom) / Z</c> VGA
/// pixels from the view centre (canassa <c>project_world_to_screen</c>, <c>PROJECT.C</c>), and
/// <c>zoom</c> is the first member of the view. <c>zone_load</c> reads it from the second word of
/// <c>Z##DEF.DAT</c> on every zone change (ZONE.C:76-77, <c>res_fread(g_world_widget, 2, 1, file)</c>),
/// so it is <see cref="ZoneDefinition.ViewZoomShift"/>: 9 in zones 1-9, 8 underground. The
/// extractors turn the shift into <see cref="ZoneDefinition.FocalLength"/> and
/// <c>StartData.FocalLength</c> (START.DAT's 9, the default before a zone loads).
///
/// <para><b>Measured, 2026-10-02 (TASK-706):</b> read live from the original in zone 4, with the
/// overhead map open: <c>g_world_widget-&gt;zoom</c> = 9 and <c>g_active_window-&gt;zoom</c> = 9.
/// TASK-374 had given the map views a fixed 7 (<c>VIEW_ZOOM_DEFAULT</c>, which
/// <c>ts_create_fullscreen_view</c> sets and <c>zone_load</c> then overwrites), so the overhead map,
/// the locator and the Spyglass showed four times the ground the original shows; landmark distances
/// from the party on the same save agreed with the factor of four.</para>
///
/// <para><b>Square, since TASK-764.</b> The original projects with one focal length in VGA pixels,
/// and a VGA pixel is 5 canonical units wide and 6 tall — so on its screen everything stood 1.2x
/// taller than its numbers. The port keeps the camera isotropic and puts that 1.2 into the world's
/// heights instead (<see cref="WorldUp"/>), so the focal length here is the HORIZONTAL one, in
/// canonical units, and the vertical field follows from the viewport's canonical height. TASK-439
/// had put the 6:5 into the camera's aspect, which made the Unity world itself non-square and
/// would have squashed any model a modder authored in true proportions.</para>
/// </remarks>
public static class WorldProjection {
    /// <summary>
    /// The camera's vertical field of view, in degrees, for a viewport <paramref name="viewHeight"/>
    /// canonical units tall at <paramref name="focalLength"/> canonical units — StartData's or the
    /// zone's <c>FocalLength</c>. The camera's aspect is the viewport's own.
    /// </summary>
    public static double VerticalFovDegrees(double viewHeight, double focalLength) {
        if (viewHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(viewHeight));
        }
        if (focalLength <= 0) {
            throw new ArgumentOutOfRangeException(nameof(focalLength));
        }

        return 2.0 * Math.Atan(0.5 * viewHeight / focalLength) * (180.0 / Math.PI);
    }

    /// <summary>
    /// The vertical field for a window of <paramref name="aspect"/> that never shows less than the
    /// original view on either axis (Enhanced full-screen, spec 2026-10-08): the larger of the
    /// original's vertical field and the vertical field that keeps its horizontal one.
    /// </summary>
    public static double CoverVerticalFovDegrees(double viewWidth, double viewHeight, double focalLength, double aspect) {
        if (aspect <= 0) {
            throw new ArgumentOutOfRangeException(nameof(aspect));
        }
        double keepVertical = VerticalFovDegrees(viewHeight, focalLength);
        double keepHorizontal = 2.0 * Math.Atan(0.5 * viewWidth / focalLength / aspect) * (180.0 / Math.PI);
        return Math.Max(keepVertical, keepHorizontal);
    }
}
