namespace GameData.Resources.World;

using System;

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

    /// <summary>The viewport's left edge on screen: REQ_MAIN 192 at canonical x 65, VGA 13.</summary>
    public const int ViewportVgaLeft = 13;

    /// <summary>Texture pixels per VGA pixel: Z##H.BMX is extracted at canonical scale.</summary>
    private const int TexturePixelsAcross = 5;
    private const int TexturePixelsDown = 6;

    private static int VgaWidth(int panelTextureWidth) => Math.Max(1, panelTextureWidth / TexturePixelsAcross);

    /// <summary>
    /// The ring position (0..1 of a full ring) at the viewport's left edge, for a 16-bit yaw.
    /// </summary>
    /// <remarks>With Y = yaw &gt;&gt; 6 = 256q + r, panel (3 - q) is drawn at x = r, so screen
    /// x = 0 shows ring pixel (3 - q) * 256 - r = 768 - Y (mod 1024).</remarks>
    /// <param name="panelTextureWidth">One panel's canonical texture width (the Unity side never
    /// sees VGA pixels, TASK-764).</param>
    public static double LeftEdgeRingFraction(int yaw16, int panelTextureWidth) {
        int panelVgaWidth = VgaWidth(panelTextureWidth);
        int ring = PanelCount * panelVgaWidth;
        int y = ((yaw16 & 0xffff) / YawUnitsPerPixel) % ring;
        // + ViewportVgaLeft: the panels are blitted at ABSOLUTE screen x, and the viewport starts 13
        // px in, so its left edge already shows 13 px of the ring (TASK-763, measured on c296).
        int left = ((3 * panelVgaWidth - y + ViewportVgaLeft) % ring + ring) % ring;
        return (double)left / ring;
    }

    /// <summary>How much of the ring the viewport shows across its width.</summary>
    public static double VisibleRingFraction(int panelTextureWidth) =>
        (double)ViewportVgaWidth / (PanelCount * VgaWidth(panelTextureWidth));

    /// <summary>The panorama's height as a fraction of the viewport's: its own rows, 1:1.</summary>
    public static double HeightFraction(int panelTextureHeight) =>
        (double)panelTextureHeight / (ViewportVgaHeight * TexturePixelsDown);

    /// <summary>
    /// The original's 16-bit yaw for a CLOCKWISE heading in degrees (0 = north, 90 = east).
    /// </summary>
    /// <remarks>The original's yaw runs the other way: east is 49152, and a right turn subtracts
    /// (TASK-763). Feeding clockwise degrees straight in put the panel half a turn off at E/W.</remarks>
    public static int YawFromClockwiseDegrees(double degrees) =>
        (int)Math.Round(-degrees / 360.0 * 65536.0) & 0xffff;
}
