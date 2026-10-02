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
/// so it is <see cref="ZoneDefinition.ViewZoomShift"/>: 9 in zones 1-9, 8 underground.
/// <see cref="TravelProjectionShift"/> is START.DAT's 9, the default before a zone loads.
///
/// <para><b>Measured, 2026-10-02 (TASK-706):</b> read live from the original in zone 4, with the
/// overhead map open: <c>g_world_widget-&gt;zoom</c> = 9 and <c>g_active_window-&gt;zoom</c> = 9.
/// TASK-374 had given the map views a fixed 7 (<c>VIEW_ZOOM_DEFAULT</c>, which
/// <c>ts_create_fullscreen_view</c> sets and <c>zone_load</c> then overwrites), so the overhead map,
/// the locator and the Spyglass showed four times the ground the original shows; landmark distances
/// from the party on the same save agreed with the factor of four.</para>
///
/// <para><b>The projection is ANISOTROPIC in canonical space, and one FOV cannot express it.</b>
/// The original projects with the same focal length in both axes — <c>nScrX</c> and <c>nScrY</c>
/// both shift by <c>g_nScreenShift</c> (WORLDRND.C:185-186) — but those are VGA pixels, which are
/// not square: a VGA rect <c>w x h</c> occupies <c>5w x 6h</c> canonical units. So in canonical
/// units the vertical focal length is <b>6/5 of the horizontal</b>, and a Unity camera with square
/// pixels cannot have both right from a single <c>fieldOfView</c>.
///
/// <para>This class used to fold the 6:5 into the FOV — <c>tan(halfFovV) = 0.6h / (1 &lt;&lt; shift)</c>
/// — which makes the HORIZONTAL come out exactly right and leaves the vertical short by that same
/// 1.2. Measured 2026-09-12 against the original's own tactical grid, with both games in one fight:
/// all 13 rows agreed on width to within 0.5% and the vertical was compressed by 0.85 about the
/// viewport centre, fitting an affine map to an rms of 2.2 canonical units. That is the 1/1.2.</para>
///
/// <para>So <see cref="VerticalFovDegrees"/> now returns the TRUE vertical half-angle,
/// <c>tan(halfFovV) = (6h/2) / (6 &lt;&lt; shift) = 0.5h / (1 &lt;&lt; shift)</c>, and the caller
/// restores the horizontal by overriding the camera's aspect with
/// <see cref="CameraAspect"/> — <c>1.2 x</c> the viewport's own pixel aspect, which is just the VGA
/// rect's <c>w/h</c>. Setting one without the other is worse than setting neither.</para>
/// </remarks>
public static class WorldProjection {
    /// <summary>START.DAT's shift, used by the travel and combat view.</summary>
    public const int TravelProjectionShift = 9;

    /// <summary>
    /// VGA's pixel aspect: one VGA pixel is 5 canonical units wide and 6 tall.
    /// </summary>
    public const double CanonicalPixelAspect = 6.0 / 5.0;

    /// <summary>Half, because the FOV formula wants a half-height — see the class remarks.</summary>
    private const double HalfHeight = 0.5;

    /// <summary>
    /// The Unity camera vertical FOV, in degrees, that reproduces the original's projection for a
    /// view whose VGA rectangle is <paramref name="viewHeightVgaPx"/> tall.
    /// </summary>
    /// <summary>
    /// The aspect a Unity camera must be given so its HORIZONTAL field matches the original, once
    /// <see cref="VerticalFovDegrees"/> has set the vertical one.
    /// </summary>
    /// <remarks>
    /// <b>It is not the viewport's own aspect.</b> Unity derives x from y through the aspect, so a
    /// square-pixel aspect makes the two focal lengths equal — which is exactly what the original's
    /// non-square VGA pixels are not. Overriding it with <c>1.2 x</c> the viewport aspect restores
    /// the 6:5, and since the viewport is the canonical <c>5w x 6h</c> of a VGA rect, the answer is
    /// simply that rect's <c>w / h</c>.
    ///
    /// <para>Takes the CANONICAL (or device) size rather than the VGA one so a caller with only a
    /// RenderTexture can use it; the ratio is all that matters.</para>
    /// </remarks>
    public static double CameraAspect(double viewportWidth, double viewportHeight) {
        if (viewportWidth <= 0 || viewportHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }

        return CanonicalPixelAspect * viewportWidth / viewportHeight;
    }

    public static double VerticalFovDegrees(double viewHeightVgaPx, int projectionShift) {
        if (viewHeightVgaPx <= 0) {
            throw new ArgumentOutOfRangeException(nameof(viewHeightVgaPx));
        }
        if (projectionShift is < 0 or > 15) {
            throw new ArgumentOutOfRangeException(nameof(projectionShift));
        }
        double tanHalf = HalfHeight * viewHeightVgaPx / (1 << projectionShift);

        return 2.0 * Math.Atan(tanHalf) * (180.0 / Math.PI);
    }
}
