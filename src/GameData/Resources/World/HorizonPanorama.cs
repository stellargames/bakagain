namespace GameData.Resources.World;

/// <summary>
/// Where the Z##H.BMX panorama sits on screen — <c>skyrender_sky_ground_bands</c> (SKYREND.C:190-202).
/// </summary>
/// <remarks>
/// <b>A screen-space backdrop at native size, not a world object.</b> The four panels form a ring
/// one panel-width each (256 VGA px, so 1024 per turn); <c>uScroll = (yaw &gt;&gt; 6) &amp; 0xff</c>
/// and <c>uQuad = (-yaw - 1) &gt;&gt; 14 &amp; 3</c> put panel uQuad at x = uScroll with its
/// neighbours either side, top at <c>horizon - 29</c>, so the bitmap's 29 rows end on the horizon
/// line. TASK-760: the port stretched six stitched panels across a fixed world quad, which drew the
/// peaks small, low and at the wrong headings.
/// </remarks>
public static class HorizonPanorama {
    /// <summary>Panels in the ring.</summary>
    public const int PanelCount = 4;

    /// <summary>Yaw units per VGA pixel of scroll: <c>yaw &gt;&gt; 6</c>, 65536 / 1024.</summary>
    public const int YawUnitsPerPixel = 64;

    /// <summary>The world viewport in VGA pixels — REQ_MAIN's 3D-view hotspot (192), canonical
    /// 1470 x 606 at x5 / x6.</summary>
    public const int ViewportVgaWidth = 294;

    /// <inheritdoc cref="ViewportVgaWidth"/>
    public const int ViewportVgaHeight = 101;

    /// <summary>
    /// The ring position (0..1 of a full ring) at the viewport's left edge, for a 16-bit yaw.
    /// </summary>
    /// <remarks>With Y = yaw &gt;&gt; 6 = 256q + r, panel (3 - q) is drawn at x = r, so screen
    /// x = 0 shows ring pixel (3 - q) * 256 - r = 768 - Y (mod 1024).</remarks>
    public static double LeftEdgeRingFraction(int yaw16, int panelVgaWidth) {
        int ring = PanelCount * panelVgaWidth;
        int y = ((yaw16 & 0xffff) / YawUnitsPerPixel) % ring;
        int left = ((3 * panelVgaWidth - y) % ring + ring) % ring;
        return (double)left / ring;
    }

    /// <summary>How much of the ring the viewport shows across its width.</summary>
    public static double VisibleRingFraction(int panelVgaWidth) =>
        (double)ViewportVgaWidth / (PanelCount * panelVgaWidth);

    /// <summary>The panorama's height as a fraction of the viewport's: its own rows, 1:1.</summary>
    public static double HeightFraction(int panelVgaHeight) =>
        (double)panelVgaHeight / ViewportVgaHeight;
}
